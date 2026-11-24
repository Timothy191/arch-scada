using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.Tests;

/// <summary>
/// Regression tests for the splash readiness root-cause investigation:
///  1. archscada-plc-sim.service was installed but never enabled, so SERVER/DRIVERS warned after every login.
///  2. The STORAGE check accepted any TCP listener on 55432 OR 5432, so an unrelated PostgreSQL on 5432
///     (or any non-PostgreSQL socket) produced a false "historian online" pass.
/// </summary>
public sealed class DeploymentRootCauseRegressionTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Plant.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    /// <summary>Accepts one connection, optionally replies with a fixed payload, then closes.</summary>
    private static (TcpListener Listener, int Port, Task Server) StartFakeListener(byte[]? reply)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var buf = new byte[8];
                int total = 0;
                while (total < 8)
                {
                    int n = await stream.ReadAsync(buf.AsMemory(total, 8 - total));
                    if (n == 0) break;
                    total += n;
                }
                if (reply is not null)
                    await stream.WriteAsync(reply);
                await Task.Delay(50);
            }
            catch
            {
                // Listener stopped by test teardown.
            }
        });
        return (listener, port, server);
    }

    private static async Task<(StartupCheckState State, string Detail)> RunHistCheckAsync(int pgPort, string tempDir)
    {
        using var bridge = new UnityBridgeService();
        var service = new DeploymentReadinessService(bridge, new DeploymentReadinessOptions
        {
            HistorianStorageDirectory = Path.Combine(tempDir, "historian"),
            PostgresHost = "127.0.0.1",
            PostgresPort = pgPort,
            ProbeTimeoutMs = 250
        });
        var hist = service.Checks.Single(c => c.Id == "hist");
        return await hist.ExecuteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HistorianCheck_NonPostgresListener_IsNotTreatedAsHistorian()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"archscada-rc-nonpg-{Guid.NewGuid():N}");
        var (listener, port, server) = StartFakeListener(reply: null); // accepts TCP, never speaks PostgreSQL
        try
        {
            var (state, detail) = await RunHistCheckAsync(port, tempDir);
            Assert.Equal(StartupCheckState.Warning, state);
            Assert.Contains($":{port} offline", detail);
        }
        finally
        {
            listener.Stop();
            await server;
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData((byte)'N')]
    [InlineData((byte)'S')]
    public async Task HistorianCheck_PostgresSslResponse_PassesWithPortInDetail(byte sslReply)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"archscada-rc-pg-{Guid.NewGuid():N}");
        var (listener, port, server) = StartFakeListener(new[] { sslReply });
        try
        {
            var (state, detail) = await RunHistCheckAsync(port, tempDir);
            Assert.Equal(StartupCheckState.Passed, state);
            Assert.Contains($"PostgreSQL historian :{port} online", detail);
        }
        finally
        {
            listener.Stop();
            await server;
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ProbePostgresProtocol_ClosedPort_ReturnsFalse()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Assert.False(await DeploymentReadinessService.ProbePostgresProtocolAsync("127.0.0.1", port, 200, CancellationToken.None));
    }

    [Fact]
    public void ResolvePostgresPort_DefaultsToDedicatedHistorianPort_NeverSharedDefault5432()
    {
        var previous = Environment.GetEnvironmentVariable("ARCHSCADA_PGPORT");
        try
        {
            Environment.SetEnvironmentVariable("ARCHSCADA_PGPORT", null);
            using var bridge = new UnityBridgeService();

            var defaults = new DeploymentReadinessService(bridge, new DeploymentReadinessOptions());
            Assert.Equal(55432, defaults.ResolvePostgresPort());
            Assert.Equal(DeploymentReadinessService.DefaultHistorianPostgresPort, defaults.ResolvePostgresPort());

            Environment.SetEnvironmentVariable("ARCHSCADA_PGPORT", "not-a-port");
            Assert.Equal(55432, defaults.ResolvePostgresPort());

            var overridden = new DeploymentReadinessService(bridge, new DeploymentReadinessOptions { PostgresPort = 6543 });
            Assert.Equal(6543, overridden.ResolvePostgresPort());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARCHSCADA_PGPORT", previous);
        }
    }

    [Fact]
    public void SetupSystemdUserScript_EnablesAndRestartsPlcSimulatorAtLogin()
    {
        var script = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "deploy", "scripts", "setup-systemd-user.sh"));

        Assert.Contains("systemctl --user enable --now archscada-backup.timer", script);
        Assert.Contains("systemctl --user enable archscada-plc-sim.service", script);
        Assert.Contains("systemctl --user restart archscada-plc-sim.service", script);
        Assert.Contains("ARCHSCADA_SKIP_PLC_SIM", script);

        var unit = File.ReadAllText(Path.Combine(ResolveRepoRoot(), "deploy", "systemd-user", "archscada-plc-sim.service"));
        Assert.Contains("WantedBy=default.target", unit);
    }
}
