using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchScada.CoreIntegration;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;
using ArchScada.Desktop.ViewModels;
using ArchScada.PlcSimulator;

namespace ArchScada.Desktop.Tests;

public sealed class SplashDeploymentChecksTests
{
    private sealed class StubReadinessService : IDeploymentReadinessService
    {
        public IReadOnlyList<DeploymentCheckSpec> Checks { get; }

        public StubReadinessService(IReadOnlyList<DeploymentCheckSpec> checks)
        {
            Checks = checks;
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Plant.sln")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Projects", "ArchCoalPlant", "BaseXML")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    [Fact]
    public async Task RealDeploymentReadiness_AllEightChecksExecuteWithZeroFailures()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"archscada-splash-real-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var repoRoot = ResolveRepoRoot();
            using var bridge = new UnityBridgeService();
            var options = new DeploymentReadinessOptions
            {
                AuditDataDirectory = Path.Combine(tempDir, "audit"),
                HistorianStorageDirectory = Path.Combine(tempDir, "historian"),
                BaseXmlDirectory = Path.Combine(repoRoot, "Projects", "ArchCoalPlant", "BaseXML"),
                ScadaCommConfigPath = Path.Combine(repoRoot, "Projects", "ArchCoalPlant", "Instances", "Default", "ScadaComm", "Config", "ScadaCommConfig.xml"),
                // Use ephemeral unused ports so test does not depend on local background daemons
                ScadaServerBaseUrl = "http://127.0.0.1:59991",
                NativeAdapterHost = "127.0.0.1",
                NativeAdapterPort = 59992,
                ModbusHost = "127.0.0.1",
                ModbusPort = 59993,
                PostgresHost = "127.0.0.1",
                PostgresPort = 59994,
                ProbeTimeoutMs = 150
            };

            var service = new DeploymentReadinessService(bridge, options);
            var vm = new SplashViewModel(service, stepVisualDelayMs: 0, completionPauseMs: 0)
            {
                HoldOnSplash = true
            };

            await vm.RunStartupSequenceAsync();

            Assert.Equal(8, vm.Checks.Count);
            Assert.True(vm.IsComplete);
            Assert.False(vm.IsRunning);
            Assert.Equal(100.0, vm.Progress);
            Assert.Equal("100%", vm.ProgressText);
            Assert.Equal(0, vm.FailedCount);
            Assert.False(vm.HasFailures);
            Assert.All(vm.Checks, check =>
            {
                Assert.NotEqual(StartupCheckState.Pending, check.State);
                Assert.NotEqual(StartupCheckState.Running, check.State);
                Assert.NotEqual(StartupCheckState.Failed, check.State);
                Assert.False(string.IsNullOrWhiteSpace(check.DurationText));
                Assert.False(string.IsNullOrWhiteSpace(check.Detail));
            });

            var xmlCheck = vm.Checks.Single(c => c.Id == "xml");
            Assert.Equal(StartupCheckState.Passed, xmlCheck.State);
            Assert.Contains("52 read-only Cnl channels", xmlCheck.Detail);

            var twinCheck = vm.Checks.Single(c => c.Id == "3d");
            Assert.Equal(StartupCheckState.Passed, twinCheck.State);
            Assert.Contains("24/24 glTF 2.0 PBR models verified", twinCheck.Detail);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SplashViewModel_CriticalFailure_BlocksAutoLaunchUntilExplicitOverride()
    {
        var stub = new StubReadinessService(new[]
        {
            new DeploymentCheckSpec("cfg", "CONFIG", "Config", true, _ => Task.FromResult((StartupCheckState.Passed, "OK"))),
            new DeploymentCheckSpec("3d", "3D TWIN", "3D Assets", true, _ => Task.FromResult((StartupCheckState.Failed, "Missing jaw_crusher_jc101.glb")))
        });

        var vm = new SplashViewModel(stub, stepVisualDelayMs: 0, completionPauseMs: 0);
        int completedCount = 0;
        vm.Completed += (_, _) => completedCount++;

        await vm.RunStartupSequenceAsync();

        Assert.True(vm.IsComplete);
        Assert.True(vm.HasFailures);
        Assert.Equal(1, vm.FailedCount);
        Assert.Equal(StartupCheckState.Failed, vm.ReadinessState);
        Assert.Equal("DEPLOYMENT BLOCKED — CRITICAL CHECK FAILED", vm.ReadinessBannerTitle);
        Assert.Equal("Override & Launch", vm.LaunchButtonText);
        Assert.Equal(0, completedCount);

        vm.LaunchAnywayCommand.Execute(null);
        Assert.Equal(1, completedCount);

        // Guard prevents duplicate Completed events
        vm.ContinueToAppCommand.Execute(null);
        Assert.Equal(1, completedCount);
    }

    [Fact]
    public async Task SplashViewModel_HoldOnSplash_PreventsAutoLaunchUntilUserClicksContinue()
    {
        var stub = new StubReadinessService(new[]
        {
            new DeploymentCheckSpec("cfg", "CONFIG", "Config", true, _ => Task.FromResult((StartupCheckState.Passed, "OK"))),
            new DeploymentCheckSpec("srv", "SERVER", "Server", false, _ => Task.FromResult((StartupCheckState.Warning, "Demo")))
        });

        var vm = new SplashViewModel(stub, stepVisualDelayMs: 0, completionPauseMs: 10)
        {
            HoldOnSplash = true
        };

        int completedCount = 0;
        vm.Completed += (_, _) => completedCount++;

        await vm.RunStartupSequenceAsync();

        Assert.True(vm.IsComplete);
        Assert.False(vm.HasFailures);
        Assert.True(vm.HasWarnings);
        Assert.Equal(StartupCheckState.Warning, vm.ReadinessState);
        Assert.Equal("Launch Plant HMI", vm.LaunchButtonText);
        Assert.Equal(0, completedCount);

        vm.ContinueToAppCommand.Execute(null);
        Assert.Equal(1, completedCount);
    }

    [Fact]
    public async Task SplashViewModel_RerunChecksCommand_ResetsAndReexecutesAllChecks()
    {
        int invocation = 0;
        var stub = new StubReadinessService(new[]
        {
            new DeploymentCheckSpec("cfg", "CONFIG", "Config", true, _ =>
            {
                invocation++;
                return invocation == 1
                    ? Task.FromResult((StartupCheckState.Failed, "Initial failure"))
                    : Task.FromResult((StartupCheckState.Passed, "Recovered on re-run"));
            })
        });

        var vm = new SplashViewModel(stub, stepVisualDelayMs: 0, completionPauseMs: 0);
        int completedCount = 0;
        vm.Completed += (_, _) => completedCount++;

        await vm.RunStartupSequenceAsync();
        Assert.True(vm.HasFailures);
        Assert.Equal(0, completedCount);

        await vm.RerunChecksCommand.ExecuteAsync(null);

        Assert.False(vm.HasFailures);
        Assert.Equal(1, vm.PassedCount);
        Assert.Equal("Recovered on re-run", vm.Checks[0].Detail);
        Assert.Equal(1, completedCount);
    }

    [Fact]
    public async Task DeploymentReadiness_WithLivePlcSimulator_TransitionsServerAndDriversToPassed()
    {
        await using var sim = new PlcSimulatorHost(httpPort: 0, tcpPort: 0, modbusPort: 0);
        await sim.StartAsync();

        using var bridge = new UnityBridgeService();

        var offlineService = new DeploymentReadinessService(bridge, new DeploymentReadinessOptions
        {
            ScadaServerBaseUrl = "http://127.0.0.1:59995",
            NativeAdapterHost = "127.0.0.1",
            NativeAdapterPort = 59996,
            ModbusHost = "127.0.0.1",
            ModbusPort = 59997,
            ProbeTimeoutMs = 150
        });

        var offlineSrv = await offlineService.Checks.Single(c => c.Id == "srv").ExecuteAsync(CancellationToken.None);
        var offlineComm = await offlineService.Checks.Single(c => c.Id == "comm").ExecuteAsync(CancellationToken.None);
        Assert.Equal(StartupCheckState.Warning, offlineSrv.State);
        Assert.Equal(StartupCheckState.Warning, offlineComm.State);

        var liveService = new DeploymentReadinessService(bridge, new DeploymentReadinessOptions
        {
            ScadaServerBaseUrl = $"http://127.0.0.1:{sim.HttpPort}",
            NativeAdapterHost = "127.0.0.1",
            NativeAdapterPort = sim.TcpPort,
            ModbusHost = "127.0.0.1",
            ModbusPort = sim.ModbusPort,
            ProbeTimeoutMs = 350
        });

        var liveSrv = await liveService.Checks.Single(c => c.Id == "srv").ExecuteAsync(CancellationToken.None);
        var liveComm = await liveService.Checks.Single(c => c.Id == "comm").ExecuteAsync(CancellationToken.None);

        Assert.Equal(StartupCheckState.Passed, liveSrv.State);
        Assert.Contains($"Native TCP :{sim.TcpPort} online", liveSrv.Detail);
        Assert.Equal(StartupCheckState.Passed, liveComm.State);
        Assert.Contains($"Modbus TCP :{sim.ModbusPort} live", liveComm.Detail);
    }

    [Fact]
    public async Task DeploymentReadiness_MissingOrCorruptGlbDirectory_Fails3DCheck()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"archscada-bad-glb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write a corrupt placeholder for one required GLB while leaving others missing
            await File.WriteAllBytesAsync(Path.Combine(tempDir, "stockpile_cone.glb"), new byte[128]);

            using var bridge = new UnityBridgeService();
            var service = new DeploymentReadinessService(bridge, new DeploymentReadinessOptions
            {
                GlbExportDirectory = tempDir
            });

            var result = await service.Checks.Single(c => c.Id == "3d").ExecuteAsync(CancellationToken.None);
            Assert.Equal(StartupCheckState.Failed, result.State);
            Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DeploymentReadiness_NonReadOnlyOrMalformedBaseXml_FailsXmlCheck()
    {
        var tempXmlDir = Path.Combine(Path.GetTempPath(), $"archscada-bad-xml-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempXmlDir);

        try
        {
            var repoRoot = ResolveRepoRoot();
            var srcBaseXml = Path.Combine(repoRoot, "Projects", "ArchCoalPlant", "BaseXML");

            File.Copy(Path.Combine(srcBaseXml, "Device.xml"), Path.Combine(tempXmlDir, "Device.xml"));
            var cnlContent = await File.ReadAllTextAsync(Path.Combine(srcBaseXml, "Cnl.xml"));

            // Mutate first channel from read-only CnlTypeID=1 to writable CnlTypeID=2
            var idx = cnlContent.IndexOf("<CnlTypeID>1</CnlTypeID>", StringComparison.Ordinal);
            Assert.True(idx >= 0);
            var mutatedCnl = cnlContent.Remove(idx, "<CnlTypeID>1</CnlTypeID>".Length)
                .Insert(idx, "<CnlTypeID>2</CnlTypeID>");
            await File.WriteAllTextAsync(Path.Combine(tempXmlDir, "Cnl.xml"), mutatedCnl);

            using var bridge = new UnityBridgeService();
            var service = new DeploymentReadinessService(bridge, new DeploymentReadinessOptions
            {
                BaseXmlDirectory = tempXmlDir
            });

            var result = await service.Checks.Single(c => c.Id == "xml").ExecuteAsync(CancellationToken.None);
            Assert.Equal(StartupCheckState.Failed, result.State);
            Assert.Contains("CnlTypeID != 1", result.Detail);

            // Also verify malformed XML fails cleanly
            await File.WriteAllTextAsync(Path.Combine(tempXmlDir, "Cnl.xml"), "<ArrayOfCnl><Broken");
            var malformedResult = await service.Checks.Single(c => c.Id == "xml").ExecuteAsync(CancellationToken.None);
            Assert.Equal(StartupCheckState.Failed, malformedResult.State);
            Assert.Contains("BaseXML parse error", malformedResult.Detail);
        }
        finally
        {
            if (Directory.Exists(tempXmlDir))
                Directory.Delete(tempXmlDir, recursive: true);
        }
    }
}
