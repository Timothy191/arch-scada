using System.Text.Json;

namespace ArchScada.CoreIntegration;

/// <summary>
/// Configuration options for <see cref="ScadaServerClient"/>.
/// </summary>
public sealed class ScadaServerOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Represents the outcome of a ScadaServer REST call with explicit offline fallback metadata.
/// </summary>
public sealed record ScadaServerResult<T>
{
    public bool IsSuccess { get; init; }
    public bool IsOffline { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public int? StatusCode { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public static ScadaServerResult<T> Success(T data, int statusCode = 200) => new()
    {
        IsSuccess = true,
        IsOffline = false,
        Data = data,
        StatusCode = statusCode,
        CheckedAtUtc = DateTimeOffset.UtcNow
    };

    public static ScadaServerResult<T> Offline(string errorMessage, int? statusCode = null) => new()
    {
        IsSuccess = false,
        IsOffline = true,
        Data = default,
        ErrorMessage = errorMessage,
        StatusCode = statusCode,
        CheckedAtUtc = DateTimeOffset.UtcNow
    };
}

/// <summary>
/// Interface for HTTP/REST client for ScadaServer with short timeouts and graceful offline fallback.
/// Never throws unhandled exceptions when ScadaServer is offline or unreachable.
/// </summary>
public interface IScadaServerClient : IDisposable
{
    Uri? BaseAddress { get; }
    TimeSpan Timeout { get; }

    Task<ScadaServerResult<ServerHealthStatus>> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<ScadaServerResult<IReadOnlyList<TagValueSnapshot>>> GetCurrentTagsAsync(CancellationToken cancellationToken = default);
    Task<ScadaServerResult<PlantConfigurationSnapshot>> GetPlantConfigAsync(CancellationToken cancellationToken = default);
}