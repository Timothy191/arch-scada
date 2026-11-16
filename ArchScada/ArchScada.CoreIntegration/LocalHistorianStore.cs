using System.Text;
using System.Text.Json;

namespace ArchScada.CoreIntegration;

/// <summary>
/// Append-only local JSONL historian and alarm journal for offline trend and alarm persistence across restarts.
/// </summary>
public sealed class LocalHistorianStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _sampleLock = new(1, 1);
    private readonly SemaphoreSlim _alarmLock = new(1, 1);
    private int _maxRetainedSamples = DefaultMaxRetainedSamples;

    public const int DefaultMaxRetainedSamples = 10_000;

    public string StorageDirectory { get; }
    public string SamplesFilePath { get; }
    public string AlarmsFilePath { get; }

    /// <summary>
    /// Maximum number of telemetry samples retained in the local historian file before automatic compaction.
    /// </summary>
    public int MaxRetainedSamples
    {
        get => _maxRetainedSamples;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "MaxRetainedSamples must be greater than zero.");
            }

            _maxRetainedSamples = value;
        }
    }

    public LocalHistorianStore(string storageDirectory, int maxRetainedSamples = DefaultMaxRetainedSamples)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
        {
            throw new ArgumentException("Storage directory must not be empty.", nameof(storageDirectory));
        }

        if (maxRetainedSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetainedSamples), "MaxRetainedSamples must be greater than zero.");
        }

        StorageDirectory = storageDirectory;
        SamplesFilePath = Path.Combine(storageDirectory, "historian-samples.jsonl");
        AlarmsFilePath = Path.Combine(storageDirectory, "alarm-journal.jsonl");
        _maxRetainedSamples = maxRetainedSamples;
    }

    /// <summary>
    /// Appends a single telemetry sample to the local JSONL historian and enforces retention limits.
    /// </summary>
    public async Task AppendSampleAsync(TelemetrySample sample, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sample);
        await AppendJsonLineAsync(SamplesFilePath, _sampleLock, sample, cancellationToken).ConfigureAwait(false);
        await EnforceMaxRetentionIfNeededAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends multiple telemetry samples to the local JSONL historian in a single lock acquisition.
    /// </summary>
    public async Task AppendSamplesAsync(IEnumerable<TelemetrySample> samples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);

        await _sampleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            await using (var stream = new FileStream(SamplesFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var sample in samples)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = JsonSerializer.Serialize(sample, JsonOptions);
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await PruneSamplesCoreAsync(_maxRetainedSamples, onlyWhenExceedingLimit: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    /// <summary>
    /// Compacts the historian sample file to retain at most <see cref="MaxRetainedSamples"/> most recent samples.
    /// </summary>
    public Task<int> PruneSamplesAsync(CancellationToken cancellationToken = default) =>
        PruneSamplesAsync(_maxRetainedSamples, cancellationToken);

    /// <summary>
    /// Compacts the historian sample file to retain at most <paramref name="maxSamplesToKeep"/> most recent samples.
    /// Returns the number of pruned samples.
    /// </summary>
    public async Task<int> PruneSamplesAsync(int maxSamplesToKeep, CancellationToken cancellationToken = default)
    {
        if (maxSamplesToKeep < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSamplesToKeep), "maxSamplesToKeep must not be negative.");
        }

        await _sampleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PruneSamplesCoreAsync(maxSamplesToKeep, onlyWhenExceedingLimit: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    /// <summary>
    /// Compacts the historian sample file by removing all samples with a timestamp strictly older than <paramref name="cutoffUtc"/>.
    /// Returns the number of pruned samples.
    /// </summary>
    public async Task<int> PruneOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        await _sampleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(SamplesFilePath))
            {
                return 0;
            }

            var (samples, hadCorruptLines) = await ReadJsonLinesCoreAsync<TelemetrySample>(SamplesFilePath, cancellationToken).ConfigureAwait(false);
            var retained = samples
                .Where(s => s.TimestampUtc >= cutoffUtc)
                .OrderBy(s => s.TimestampUtc)
                .ToList();

            var removedCount = samples.Count - retained.Count;
            if (removedCount > 0 || hadCorruptLines)
            {
                await WriteJsonLinesCoreAsync(SamplesFilePath, retained, cancellationToken).ConfigureAwait(false);
            }

            return removedCount;
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    /// <summary>
    /// Queries persisted telemetry samples filtered by tag ID and UTC time range, ordered chronologically.
    /// </summary>
    public async Task<IReadOnlyList<TelemetrySample>> QuerySamplesAsync(
        string? tagId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var entries = await ReadJsonLinesAsync<TelemetrySample>(SamplesFilePath, _sampleLock, cancellationToken).ConfigureAwait(false);

        IEnumerable<TelemetrySample> query = entries;

        if (!string.IsNullOrWhiteSpace(tagId))
        {
            query = query.Where(s => string.Equals(s.TagId, tagId, StringComparison.OrdinalIgnoreCase));
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(s => s.TimestampUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(s => s.TimestampUtc <= toUtc.Value);
        }

        query = query.OrderBy(s => s.TimestampUtc);

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        return query.ToList();
    }

    /// <summary>
    /// Appends an alarm lifecycle entry to the local JSONL alarm journal.
    /// </summary>
    public Task AppendAlarmEventAsync(AlarmJournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return AppendJsonLineAsync(AlarmsFilePath, _alarmLock, entry, cancellationToken);
    }

    /// <summary>
    /// Loads persisted alarm journal entries with optional filtering, ordered chronologically.
    /// </summary>
    public async Task<IReadOnlyList<AlarmJournalEntry>> LoadAlarmEventsAsync(
        string? areaId = null,
        string? equipmentId = null,
        bool? unacknowledgedOnly = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default)
    {
        var entries = await ReadJsonLinesAsync<AlarmJournalEntry>(AlarmsFilePath, _alarmLock, cancellationToken).ConfigureAwait(false);

        IEnumerable<AlarmJournalEntry> query = entries;

        if (!string.IsNullOrWhiteSpace(areaId))
        {
            query = query.Where(a => string.Equals(a.AreaId, areaId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(equipmentId))
        {
            query = query.Where(a => string.Equals(a.EquipmentId, equipmentId, StringComparison.OrdinalIgnoreCase));
        }

        if (unacknowledgedOnly == true)
        {
            query = query.Where(a => !a.IsAcknowledged);
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(a => a.TimestampUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(a => a.TimestampUtc <= toUtc.Value);
        }

        return query.OrderBy(a => a.TimestampUtc).ToList();
    }

    private async Task EnforceMaxRetentionIfNeededAsync(CancellationToken cancellationToken)
    {
        await _sampleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PruneSamplesCoreAsync(_maxRetainedSamples, onlyWhenExceedingLimit: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sampleLock.Release();
        }
    }

    private async Task<int> PruneSamplesCoreAsync(int maxSamplesToKeep, bool onlyWhenExceedingLimit, CancellationToken cancellationToken)
    {
        if (!File.Exists(SamplesFilePath))
        {
            return 0;
        }

        var (samples, hadCorruptLines) = await ReadJsonLinesCoreAsync<TelemetrySample>(SamplesFilePath, cancellationToken).ConfigureAwait(false);
        if (onlyWhenExceedingLimit && samples.Count <= maxSamplesToKeep)
        {
            return 0;
        }

        var ordered = samples.OrderBy(s => s.TimestampUtc).ToList();
        var retained = maxSamplesToKeep <= 0
            ? []
            : ordered.Count > maxSamplesToKeep
                ? ordered.Skip(ordered.Count - maxSamplesToKeep).ToList()
                : ordered;

        var removedCount = samples.Count - retained.Count;
        if (removedCount > 0 || hadCorruptLines)
        {
            await WriteJsonLinesCoreAsync(SamplesFilePath, retained, cancellationToken).ConfigureAwait(false);
        }

        return removedCount;
    }

    private async Task AppendJsonLineAsync<T>(string filePath, SemaphoreSlim gate, T item, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(StorageDirectory);
            var line = JsonSerializer.Serialize(item, JsonOptions);

            await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<List<T>> ReadJsonLinesAsync<T>(string filePath, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (results, _) = await ReadJsonLinesCoreAsync<T>(filePath, cancellationToken).ConfigureAwait(false);
            return results;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<(List<T> Items, bool HadCorruptLines)> ReadJsonLinesCoreAsync<T>(string filePath, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        var hadCorruptLines = false;
        if (!File.Exists(filePath))
        {
            return (results, false);
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var item = JsonSerializer.Deserialize<T>(line, JsonOptions);
                if (item is not null)
                {
                    results.Add(item);
                }
            }
            catch (JsonException)
            {
                // Skip truncated/corrupt trailing lines to preserve offline historian resilience.
                hadCorruptLines = true;
            }
        }

        return (results, hadCorruptLines);
    }

    private async Task WriteJsonLinesCoreAsync<T>(string filePath, IReadOnlyList<T> items, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StorageDirectory);
        var tempFilePath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = JsonSerializer.Serialize(item, JsonOptions);
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempFilePath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                    // Ignore cleanup failures on temporary compaction file.
                }
            }
        }
    }
}
