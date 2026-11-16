using System.Net;
using System.Net.Sockets;
using System.Text;
using ArchScada.CoreIntegration;

namespace ArchScada.CoreIntegration.Tests;

public sealed class ScadaServerClientTests
{
    [Fact]
    public async Task GetHealthAsync_ReturnsOfflineResult_WhenServerIsUnreachable()
    {
        // Bind and immediately release an ephemeral port so connection is guaranteed refused.
        int unusedPort;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            unusedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        using var client = new ScadaServerClient(new ScadaServerOptions
        {
            BaseUrl = $"http://127.0.0.1:{unusedPort}",
            Timeout = TimeSpan.FromMilliseconds(500)
        });

        var result = await client.GetHealthAsync();

        Assert.False(result.IsSuccess);
        Assert.True(result.IsOffline);
        Assert.Null(result.Data);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task GetCurrentTagsAsync_ReturnsOfflineResult_WhenRequestTimesOut()
    {
        using var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new ScadaServerClient(
            new ScadaServerOptions
            {
                BaseUrl = "http://localhost:5000",
                Timeout = TimeSpan.FromMilliseconds(50)
            },
            handler);

        var result = await client.GetCurrentTagsAsync();

        Assert.False(result.IsSuccess);
        Assert.True(result.IsOffline);
        Assert.Null(result.Data);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPlantConfigAsync_ReturnsOfflineResult_WhenServerReturns503()
    {
        using var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable"
            }));

        using var client = new ScadaServerClient(
            new ScadaServerOptions { BaseUrl = "http://localhost:5000" },
            handler);

        var result = await client.GetPlantConfigAsync();

        Assert.False(result.IsSuccess);
        Assert.True(result.IsOffline);
        Assert.Equal(503, result.StatusCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task ClientMethods_ReturnSuccess_WhenServerRespondsWithValidJson()
    {
        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var json = path switch
            {
                "/api/v1/health" => """{"isOnline":true,"serverVersion":"6.2.0","serverTimeUtc":"2026-11-16T06:00:00Z","activeChannelCount":42}""",
                "/api/v1/tags/current" => """[{"tagId":"CR101_POWER_KW","value":312.4,"timestampUtc":"2026-11-16T06:00:00Z","quality":"Good"}]""",
                "/api/v1/config/plant" => """{"plantId":"coal-01","version":"1.0","savedAtUtc":"2026-11-16T06:00:00Z","areas":[],"equipment":[],"tags":[]}""",
                _ => "{}"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        using var client = new ScadaServerClient(
            new ScadaServerOptions { BaseUrl = "http://localhost:5000" },
            handler);

        var health = await client.GetHealthAsync();
        var tags = await client.GetCurrentTagsAsync();
        var config = await client.GetPlantConfigAsync();

        Assert.True(health.IsSuccess);
        Assert.False(health.IsOffline);
        Assert.Equal(42, health.Data?.ActiveChannelCount);

        Assert.True(tags.IsSuccess);
        Assert.Single(tags.Data!);
        Assert.Equal("CR101_POWER_KW", tags.Data![0].TagId);

        Assert.True(config.IsSuccess);
        Assert.Equal("coal-01", config.Data?.PlantId);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
