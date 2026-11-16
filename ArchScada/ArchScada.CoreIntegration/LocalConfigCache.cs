using System.Text.Json;

namespace ArchScada.CoreIntegration;

/// <summary>
/// JSON file-backed offline configuration cache for plant areas, equipment definitions, and tag metadata.
/// </summary>
public sealed class LocalConfigCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public string CacheFilePath { get; }

    public LocalConfigCache(string cacheFilePath)
    {
        if (string.IsNullOrWhiteSpace(cacheFilePath))
        {
            throw new ArgumentException("Cache file path must not be empty.", nameof(cacheFilePath));
        }

        CacheFilePath = cacheFilePath;
    }

    /// <summary>
    /// Persists the plant configuration snapshot atomically to the local JSON cache file.
    /// </summary>
    public async Task SaveConfigAsync(PlantConfigurationSnapshot config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{CacheFilePath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, CacheFilePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Loads the cached plant configuration snapshot, or returns <see langword="null"/> if no cache exists.
    /// </summary>
    public async Task<PlantConfigurationSnapshot?> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                return null;
            }

            await using var stream = new FileStream(CacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (stream.Length == 0)
            {
                return null;
            }

            return await JsonSerializer.DeserializeAsync<PlantConfigurationSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
