using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ArchScada.CoreIntegration;

/// <summary>
/// HTTP/REST client for ScadaServer with short timeouts and graceful offline fallback.
/// Never throws unhandled exceptions when ScadaServer is offline or unreachable.
/// </summary>
public sealed class ScadaServerClient : IScadaServerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public Uri? BaseAddress => _httpClient.BaseAddress;
    public TimeSpan Timeout => _httpClient.Timeout;

    public ScadaServerClient(ScadaServerOptions? options = null, HttpMessageHandler? handler = null)
    {
        var effectiveOptions = options ?? new ScadaServerOptions();
        var timeout = effectiveOptions.Timeout > TimeSpan.Zero
            ? effectiveOptions.Timeout
            : TimeSpan.FromSeconds(2);

        _httpClient = handler is null
            ? new HttpClient { Timeout = timeout }
            : new HttpClient(handler, disposeHandler: false) { Timeout = timeout };
        _ownsHttpClient = true;

        if (Uri.TryCreate(effectiveOptions.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }
    }

    /// <summary>
    /// Creates a <see cref="ScadaServerClient"/> with options bound from configuration.
    /// </summary>
    public static ScadaServerClient FromConfiguration(IConfiguration configuration, HttpMessageHandler? handler = null)
    {
        var options = configuration.GetSection("ScadaServer").Get<ScadaServerOptions>() ?? new ScadaServerOptions();
        return new ScadaServerClient(options, handler);
    }

    public ScadaServerClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
    }

    /// <summary>
    /// Queries ScadaServer health endpoint (<c>/api/v1/health</c>).
    /// </summary>
    public Task<ScadaServerResult<ServerHealthStatus>> GetHealthAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ServerHealthStatus>("api/v1/health", cancellationToken);

    /// <summary>
    /// Queries current live tag values (<c>/api/v1/tags/current</c>).
    /// </summary>
    public Task<ScadaServerResult<IReadOnlyList<TagValueSnapshot>>> GetCurrentTagsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<TagValueSnapshot>>("api/v1/tags/current", cancellationToken);

    /// <summary>
    /// Queries the plant configuration snapshot (<c>/api/v1/config/plant</c>).
    /// </summary>
    public Task<ScadaServerResult<PlantConfigurationSnapshot>> GetPlantConfigAsync(CancellationToken cancellationToken = default) =>
        GetAsync<PlantConfigurationSnapshot>("api/v1/config/plant", cancellationToken);

    private async Task<ScadaServerResult<T>> GetAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        if (_httpClient.BaseAddress is null)
        {
            return ScadaServerResult<T>.Offline("Invalid or missing ScadaServer base URL.");
        }

        try
        {
            using var response = await _httpClient.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return ScadaServerResult<T>.Offline(
                    $"ScadaServer returned HTTP {statusCode} ({response.ReasonPhrase ?? "Error"}).",
                    statusCode);
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return ScadaServerResult<T>.Offline("ScadaServer returned an empty response payload.", statusCode);
            }

            return ScadaServerResult<T>.Success(payload, statusCode);
        }
        catch (OperationCanceledException)
        {
            return ScadaServerResult<T>.Offline("ScadaServer request timed out or was canceled.");
        }
        catch (HttpRequestException ex)
        {
            return ScadaServerResult<T>.Offline($"ScadaServer unreachable: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return ScadaServerResult<T>.Offline($"Invalid JSON response from ScadaServer: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ScadaServerResult<T>.Offline($"Unexpected ScadaServer client error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
