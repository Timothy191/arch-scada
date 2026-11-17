using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ArchScada.CoreIntegration;
using ArchScada.Desktop.Services;
using Scada.Data.Const;
using Scada.Data.Models;
using Xunit;

namespace ArchScada.Desktop.Tests;

public sealed class LiveAdapterAndHistorianTests
{
    [Fact]
    public void SimulatedTelemetryService_Constructor_InjectsNativeScadaServerAdapter()
    {
        using var customAdapter = new NativeScadaServerAdapter(
            host: "10.0.0.50",
            port: 10001,
            timeoutMs: 500);

        using var telemetry = new SimulatedTelemetryService(customAdapter);

        Assert.NotNull(telemetry.NativeAdapter);
        Assert.Same(customAdapter, telemetry.NativeAdapter);
        Assert.Equal("10.0.0.50", telemetry.NativeAdapter.ConnectionOptions.Host);
        Assert.Equal(10001, telemetry.NativeAdapter.ConnectionOptions.Port);

        using var defaultTelemetry = new SimulatedTelemetryService();
        Assert.NotNull(defaultTelemetry.NativeAdapter);
        Assert.Equal("127.0.0.1", defaultTelemetry.NativeAdapter.ConnectionOptions.Host);
    }

    [Fact]
    public async Task LivePolling_WhenOnline_UpdatesMatchingHmiTagsAndStatus()
    {
        using var adapter = new NativeScadaServerAdapter(
            connectionOptions: NativeScadaServerAdapter.CreateDefaultConnectionOptions(),
            customDataFetcher: cnlNums =>
            {
                var result = new CnlData[cnlNums.Length];
                for (var i = 0; i < cnlNums.Length; i++)
                {
                    result[i] = cnlNums[i] switch
                    {
                        101 => new CnlData(725.4, CnlStatusID.Defined), // CR101_TPH
                        102 => new CnlData(340.5, CnlStatusID.Defined), // CR101_KW
                        201 => new CnlData(82.5, CnlStatusID.Defined),  // TK201_LVL
                        301 => new CnlData(1.58, CnlStatusID.Defined),  // TK301_SG
                        _ => new CnlData(25.0, CnlStatusID.Defined)
                    };
                }

                return result;
            });

        using var telemetry = new SimulatedTelemetryService(adapter);
        telemetry.IsOfflineDemoMode = false;

        await telemetry.TickAsync();

        Assert.True(telemetry.IsServerConnected);
        Assert.Contains("Online", telemetry.NativeProtocolStatus, StringComparison.OrdinalIgnoreCase);

        var cr101Tph = telemetry.AllTags.Single(t => t.TagId == "CR101_TPH");
        Assert.Equal(725.4, cr101Tph.Value);
        Assert.False(cr101Tph.IsStale);

        var tk301Sg = telemetry.AllTags.Single(t => t.TagId == "TK301_SG");
        Assert.Equal(1.58, tk301Sg.Value);
        Assert.False(tk301Sg.IsStale);

        var crusher = telemetry.Equipment.Single(e => e.Id == "CR-101");
        Assert.Equal(725.4, crusher.PrimaryMetric);
        Assert.Equal(725.4, telemetry.TotalFeedTonnageTph);

        var magTank = telemetry.Equipment.Single(e => e.Id == "TK-301");
        Assert.Equal(1.58, magTank.PrimaryMetric);
        Assert.Equal(1.58, telemetry.MediumSpecificGravity);
    }

    [Fact]
    public async Task LivePolling_WhenSnapshotStale_UpdatesIsStaleFlag()
    {
        using var adapter = new NativeScadaServerAdapter(
            connectionOptions: NativeScadaServerAdapter.CreateDefaultConnectionOptions(),
            customDataFetcher: cnlNums =>
            {
                var result = new CnlData[cnlNums.Length];
                for (var i = 0; i < cnlNums.Length; i++)
                {
                    result[i] = cnlNums[i] switch
                    {
                        101 => new CnlData(double.NaN, CnlStatusID.Unreliable), // Stale snapshot
                        _ => new CnlData(50.0, CnlStatusID.Defined)
                    };
                }

                return result;
            });

        using var telemetry = new SimulatedTelemetryService(adapter);
        telemetry.IsOfflineDemoMode = false;

        await telemetry.PollAndTickAsync();

        Assert.True(telemetry.IsServerConnected);
        var cr101Tph = telemetry.AllTags.Single(t => t.TagId == "CR101_TPH");
        Assert.True(cr101Tph.IsStale);
        Assert.True(telemetry.StaleTagCount >= 1);
    }

    [Fact]
    public async Task LivePolling_WhenServerOffline_FallsBackGracefullyToCoupledSimulation()
    {
        int unusedPort;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            unusedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        using var adapter = new NativeScadaServerAdapter(
            host: "127.0.0.1",
            port: unusedPort,
            timeoutMs: 150);

        using var telemetry = new SimulatedTelemetryService(adapter);
        telemetry.IsOfflineDemoMode = false;

        await telemetry.TickAsync();

        Assert.False(telemetry.IsServerConnected);
        Assert.Contains("Offline", telemetry.NativeProtocolStatus, StringComparison.OrdinalIgnoreCase);

        var cr101Tph = telemetry.AllTags.Single(t => t.TagId == "CR101_TPH");
        Assert.InRange(cr101Tph.Value, cr101Tph.MinLimit, cr101Tph.MaxLimit);
        Assert.False(cr101Tph.IsStale);
    }

    [Fact]
    public void OfflineDemoMode_RunsCoupledSimulationWithoutPollingAdapter()
    {
        using var adapter = new NativeScadaServerAdapter();
        using var telemetry = new SimulatedTelemetryService(adapter);
        telemetry.IsOfflineDemoMode = true;

        telemetry.Tick();

        Assert.False(telemetry.IsServerConnected);
        Assert.True(telemetry.TotalPowerDrawKw > 0);
        Assert.True(telemetry.AllTags.Count > 0);
        Assert.All(telemetry.AllTags, t => Assert.InRange(t.Value, t.MinLimit, t.MaxLimit));
    }

    [Fact]
    public async Task PeriodicHistorianPersistence_DurablyWritesTagSamples()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"live-historian-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var adapter = new NativeScadaServerAdapter(
                connectionOptions: NativeScadaServerAdapter.CreateDefaultConnectionOptions(),
                customDataFetcher: cnlNums =>
                {
                    var result = new CnlData[cnlNums.Length];
                    for (var i = 0; i < cnlNums.Length; i++)
                    {
                        result[i] = cnlNums[i] switch
                        {
                            101 => new CnlData(680.0, CnlStatusID.Defined),
                            301 => new CnlData(1.55, CnlStatusID.Defined),
                            _ => new CnlData(30.0, CnlStatusID.Defined)
                        };
                    }

                    return result;
                });

            using var telemetry = new SimulatedTelemetryService(
                storageDirectory: tempDir,
                serverClient: null,
                nativeAdapter: adapter);

            telemetry.IsOfflineDemoMode = false;

            // Tick 1: Samples persisted to historian
            await telemetry.TickAsync();

            var samplesTick1 = await telemetry.HistorianStore.QuerySamplesAsync(tagId: "CR101_TPH");
            Assert.Single(samplesTick1);
            Assert.Equal(680.0, samplesTick1[0].Value);
            Assert.Equal("Good", samplesTick1[0].Quality);

            // Tick 2: Subsequent sample persisted
            await telemetry.TickAsync();

            var samplesTick2 = await telemetry.HistorianStore.QuerySamplesAsync(tagId: "CR101_TPH");
            Assert.Equal(2, samplesTick2.Count);

            var magSamples = await telemetry.HistorianStore.QuerySamplesAsync(tagId: "TK301_SG");
            Assert.Equal(2, magSamples.Count);
            Assert.Equal(1.55, magSamples[0].Value);
            Assert.Equal("Good", magSamples[0].Quality);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PeriodicHistorianPersistence_PersistsStaleQualityWhenTagIsStale()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"live-historian-stale-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var adapter = new NativeScadaServerAdapter(
                connectionOptions: NativeScadaServerAdapter.CreateDefaultConnectionOptions(),
                customDataFetcher: cnlNums =>
                {
                    var result = new CnlData[cnlNums.Length];
                    for (var i = 0; i < cnlNums.Length; i++)
                    {
                        result[i] = cnlNums[i] switch
                        {
                            101 => new CnlData(double.NaN, CnlStatusID.FormulaError),
                            _ => new CnlData(20.0, CnlStatusID.Defined)
                        };
                    }

                    return result;
                });

            using var telemetry = new SimulatedTelemetryService(
                storageDirectory: tempDir,
                serverClient: null,
                nativeAdapter: adapter);

            telemetry.IsOfflineDemoMode = false;

            await telemetry.PollAndTickAsync();

            var samples = await telemetry.HistorianStore.QuerySamplesAsync(tagId: "CR101_TPH");
            Assert.Single(samples);
            Assert.Equal("Stale", samples[0].Quality);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
