using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Scada.Web.Code;

/// <summary>
/// Health check for the upstream ScadaServer TCP connectivity.
/// Verifies the server is reachable and reporting channels.
/// Uses TCP connect attempt only - no HTTP dependency.
/// </summary>
public class ScadaServerHealthCheck : IHealthCheck
{
    private readonly string _host;
    private readonly int _port;

    public ScadaServerHealthCheck()
    {
        // Default to local Rapid SCADA server
        _host = "127.0.0.1";
        _port = 10000;
    }

    public ScadaServerHealthCheck(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, cancellationToken);
            stopwatch.Stop();

            return HealthCheckResult.Healthy(
                description: $"ScadaServer TCP reachable at {_host}:{_port} ({stopwatch.ElapsedMilliseconds}ms)",
                data: new Dictionary<string, object> { ["durationMs"] = stopwatch.ElapsedMilliseconds });
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                description: "ScadaServer connect was canceled",
                exception: null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return HealthCheckResult.Degraded(
                description: $"ScadaServer unreachable at {_host}:{_port}: {ex.Message}",
                exception: ex,
                data: new Dictionary<string, object>());
        }
    }
}