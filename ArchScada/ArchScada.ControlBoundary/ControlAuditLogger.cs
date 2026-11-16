using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArchScada.ControlBoundary;

/// <summary>
/// Append-only JSONL audit log writer with SHA-256 hash chaining.
/// </summary>
public sealed class ControlAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _logFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string _lastHash = "GENESIS";

    public ControlAuditLogger(string logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            throw new ArgumentException("Audit log file path must not be empty.", nameof(logFilePath));
        }

        _logFilePath = logFilePath;
        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _lastHash = LoadLastHashIfExists();
    }

    public string LogFilePath => _logFilePath;

    public async Task<ControlAuditEntry> AppendAsync(
        ControlAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previousHash = _lastHash;
            var entryWithPrev = entry with { PreviousHash = previousHash };
            var hash = ComputeHash(entryWithPrev);
            var finalizedEntry = entryWithPrev with { Hash = hash };

            var jsonLine = JsonSerializer.Serialize(finalizedEntry, SerializerOptions);

            await using var stream = new FileStream(
                _logFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteLineAsync(jsonLine.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _lastHash = hash;
            return finalizedEntry;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public IReadOnlyList<ControlAuditEntry> ReadAll()
    {
        if (!File.Exists(_logFilePath))
        {
            return Array.Empty<ControlAuditEntry>();
        }

        var entries = new List<ControlAuditEntry>();
        using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<ControlAuditEntry>(line, SerializerOptions);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private string LoadLastHashIfExists()
    {
        if (!File.Exists(_logFilePath))
        {
            return "GENESIS";
        }

        string? lastNonEmptyLine = null;
        using var stream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lastNonEmptyLine = line;
            }
        }

        if (lastNonEmptyLine is null)
        {
            return "GENESIS";
        }

        var lastEntry = JsonSerializer.Deserialize<ControlAuditEntry>(lastNonEmptyLine, SerializerOptions);
        return string.IsNullOrWhiteSpace(lastEntry?.Hash) ? "GENESIS" : lastEntry.Hash;
    }

    private static string ComputeHash(ControlAuditEntry entry)
    {
        var payload = $"{entry.PreviousHash}|{entry.AuditId}|{entry.Timestamp:O}|{entry.User}|{entry.Command}|{entry.Equipment}|{entry.Result}|{entry.Simulated}|{entry.CeRule}|{entry.Reason}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
