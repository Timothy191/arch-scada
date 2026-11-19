using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchScada.ControlBoundary;
using ArchScada.CoreIntegration;
using ArchScada.Desktop.Controls;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;
using ArchScada.Desktop.ViewModels;

namespace ArchScada.Desktop.Tests;

public class DesktopTests
{
    [Fact]
    public void IntroViewModel_InitializesWithFivePendingChecks()
    {
        using var bridge = new UnityBridgeService();
        var vm = new IntroViewModel(bridge);

        Assert.Equal(5, vm.Checks.Count);
        Assert.All(vm.Checks, check => Assert.Equal(StartupCheckState.Pending, check.State));
        Assert.Equal(new[] { "cfg", "srv", "comm", "hist", "3d" }, vm.Checks.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void IntroViewModel_ToggleAutoAdvanceCommand_TogglesFlagAndLabel()
    {
        using var bridge = new UnityBridgeService();
        var vm = new IntroViewModel(bridge);

        Assert.True(vm.AutoAdvanceEnabled);

        vm.ToggleAutoAdvanceCommand.Execute(null);
        Assert.False(vm.AutoAdvanceEnabled);
        Assert.Equal("Auto-advance paused by operator", vm.AutoAdvanceLabel);

        vm.ToggleAutoAdvanceCommand.Execute(null);
        Assert.True(vm.AutoAdvanceEnabled);
    }

    [Fact]
    public void IntroViewModel_EnterOfflineDemoCommand_FiresCompletedWithTrue()
    {
        using var bridge = new UnityBridgeService();
        var vm = new IntroViewModel(bridge);

        bool? completedArg = null;
        int fireCount = 0;
        vm.Completed += (_, offline) =>
        {
            fireCount++;
            completedArg = offline;
        };

        vm.EnterOfflineDemoCommand.Execute(null);

        Assert.Equal(1, fireCount);
        Assert.True(completedArg);
    }

    [Fact]
    public void SimulatedTelemetryService_SeedsAllChppCircuits()
    {
        var telemetry = new SimulatedTelemetryService();

        Assert.Equal(23, telemetry.Equipment.Count);
        Assert.Contains(telemetry.Equipment, e => e.Id == "CR-101");
        Assert.Contains(telemetry.Equipment, e => e.Id == "TK-201");
        Assert.Contains(telemetry.Equipment, e => e.Id == "DM-301");

        // Fine Coal & Froth Flotation
        Assert.Contains(telemetry.Equipment, e => e.Id == "SP-401");
        Assert.Contains(telemetry.Equipment, e => e.Id == "FC-401");
        Assert.Contains(telemetry.Equipment, e => e.Id == "AB-401");
        Assert.Contains(telemetry.Equipment, e => e.Id == "PP-401");
        Assert.Contains(telemetry.Equipment, e => e.Id == "PP-402");

        // Dewatering & Tailings
        var centrifuge = telemetry.Equipment.Single(e => e.Id == "CF-501");
        Assert.Contains(centrifuge.Tags, t => t.TagId == "CF501_MOIST");
        Assert.Contains(centrifuge.Tags, t => t.TagId == "CF501_AMP");
        Assert.Contains(centrifuge.Tags, t => t.TagId == "CF501_VIB");
        Assert.Contains(telemetry.Equipment, e => e.Id == "FP-501");
        var tailingsConv = telemetry.Equipment.Single(e => e.Id == "CV-501");
        Assert.True(tailingsConv.PullCordOk);
        Assert.True(tailingsConv.BeltAlignmentOk);
        Assert.True(tailingsConv.BeltSlipOk);
        Assert.Equal("FP-501", tailingsConv.TripCascadeTarget);

        // ROM, Loadout & PGNAA
        Assert.Contains(telemetry.Equipment, e => e.Id == "AF-001");
        Assert.Contains(telemetry.Equipment, e => e.Id == "SL-601");
        Assert.Contains(telemetry.Equipment, e => e.Id == "AN-601");
        Assert.Contains(telemetry.Equipment, e => e.Id == "LD-601");
    }

    [Fact]
    public void SimulatedTelemetryService_AcknowledgeAlarm_UpdatesAlarmAndRaisesEvent()
    {
        var telemetry = new SimulatedTelemetryService();
        int initialUnacked = telemetry.UnacknowledgedAlarmCount;
        Assert.True(initialUnacked >= 2);

        bool updatedRaised = false;
        telemetry.TelemetryUpdated += (_, _) => updatedRaised = true;

        telemetry.AcknowledgeAlarm("ALM-1001");

        var alarm = telemetry.Alarms.Single(a => a.Id == "ALM-1001");
        Assert.True(alarm.IsAcknowledged);
        Assert.NotNull(alarm.AcknowledgedAt);
        Assert.Equal(initialUnacked - 1, telemetry.UnacknowledgedAlarmCount);
        Assert.True(updatedRaised);
    }

    [Fact]
    public void SimulatedTelemetryService_AcknowledgeAllAlarms_ClearsAllUnacknowledged()
    {
        var telemetry = new SimulatedTelemetryService();
        Assert.True(telemetry.UnacknowledgedAlarmCount > 0);

        bool updatedRaised = false;
        telemetry.TelemetryUpdated += (_, _) => updatedRaised = true;

        telemetry.AcknowledgeAllAlarms();

        Assert.Equal(0, telemetry.UnacknowledgedAlarmCount);
        Assert.All(telemetry.Alarms, a => Assert.True(a.IsAcknowledged));
        Assert.True(updatedRaised);
    }

    [Fact]
    public void SimulatedTelemetryService_GetTagHistory_ReturnsSeededSeriesOrEmptyForUnknown()
    {
        var telemetry = new SimulatedTelemetryService();

        var history = telemetry.GetTagHistory("CR101_TPH");
        Assert.Equal(30, history.Count);

        var unknown = telemetry.GetTagHistory("NON_EXISTENT_TAG");
        Assert.Empty(unknown);
    }

    [Theory]
    [InlineData(PlantSection.Overview, "Plant Overview")]
    [InlineData(PlantSection.CrushingConveyors, "Crushing & Conveyor Circuit")]
    [InlineData(PlantSection.ThickenerCircuit, "Thickener Circuit")]
    [InlineData(PlantSection.MagnetitePrep, "Magnetite Preparation")]
    [InlineData(PlantSection.FineCoalFlotation, "Fine Coal & Froth Flotation")]
    [InlineData(PlantSection.DewateringTailings, "Dewatering & Tailings")]
    [InlineData(PlantSection.RomLoadoutQuality, "ROM, Loadout & PGNAA")]
    [InlineData(PlantSection.DigitalTwin3D, "3D Digital Twin (Unity & Blender)")]
    [InlineData(PlantSection.EquipmentAssets, "Equipment & Assets Inspector")]
    [InlineData(PlantSection.TrendsHistorian, "Trends & Local Historian")]
    [InlineData(PlantSection.AlarmsEvents, "Alarms & Events")]
    [InlineData(PlantSection.Diagnostics, "System & Driver Diagnostics")]
    public void MainShellViewModel_NavigateCommand_UpdatesSectionAndFlags(PlantSection section, string expectedTitle)
    {
        var telemetry = new SimulatedTelemetryService();
        using var bridge = new UnityBridgeService();
        var vm = new MainShellViewModel(telemetry, bridge, offlineDemoMode: true);

        try
        {
            vm.NavigateCommand.Execute(section);

            Assert.Equal(section, vm.CurrentSection);
            Assert.Equal(expectedTitle, vm.CurrentSectionTitle);
            Assert.Equal(section == PlantSection.Overview, vm.IsOverview);
            Assert.Equal(section == PlantSection.CrushingConveyors, vm.IsCrushing);
            Assert.Equal(section == PlantSection.ThickenerCircuit, vm.IsThickener);
            Assert.Equal(section == PlantSection.MagnetitePrep, vm.IsMagnetite);
            Assert.Equal(section == PlantSection.FineCoalFlotation, vm.IsFineCoal);
            Assert.Equal(section == PlantSection.DewateringTailings, vm.IsDewatering);
            Assert.Equal(section == PlantSection.RomLoadoutQuality, vm.IsRomLoadout);
            Assert.Equal(section == PlantSection.DigitalTwin3D, vm.IsDigitalTwin3D);
            Assert.Equal(section == PlantSection.EquipmentAssets, vm.IsEquipmentAssets);
            Assert.Equal(section == PlantSection.TrendsHistorian, vm.IsTrends);
            Assert.Equal(section == PlantSection.AlarmsEvents, vm.IsAlarms);
            Assert.Equal(section == PlantSection.Diagnostics, vm.IsDiagnostics);
            Assert.Equal(5, vm.FlotationEquipment.Count);
            Assert.Equal(3, vm.DewateringEquipment.Count);
            Assert.Equal(4, vm.RomLoadoutEquipment.Count);
        }
        finally
        {
            telemetry.Stop();
        }
    }

    [Fact]
    public void MainShellViewModel_SelectEquipmentCommand_UpdatesSelectedEquipment()
    {
        var telemetry = new SimulatedTelemetryService();
        using var bridge = new UnityBridgeService();
        var vm = new MainShellViewModel(telemetry, bridge, offlineDemoMode: true);

        try
        {
            var target = telemetry.Equipment.Single(e => e.Id == "TK-201");
            vm.SelectEquipmentCommand.Execute(target);

            Assert.Same(target, vm.SelectedEquipment);

            vm.SelectEquipmentCommand.Execute(null);
            Assert.Same(target, vm.SelectedEquipment);
        }
        finally
        {
            telemetry.Stop();
        }
    }

    [Fact]
    public void MainShellViewModel_AcknowledgeAlarmCommand_AcknowledgesAlarm()
    {
        var telemetry = new SimulatedTelemetryService();
        using var bridge = new UnityBridgeService();
        var vm = new MainShellViewModel(telemetry, bridge, offlineDemoMode: true);

        try
        {
            var unackedAlarm = telemetry.Alarms.First(a => !a.IsAcknowledged);
            int beforeCount = vm.UnacknowledgedAlarmCount;

            vm.AcknowledgeAlarmCommand.Execute(unackedAlarm);

            Assert.True(unackedAlarm.IsAcknowledged);
            Assert.Equal(beforeCount - 1, vm.UnacknowledgedAlarmCount);
        }
        finally
        {
            telemetry.Stop();
        }
    }

    [Fact]
    public void UnityBridgeService_EndpointAndExportedModels_AreValid()
    {
        using var bridge = new UnityBridgeService();

        Assert.Equal("udp://127.0.0.1:17850", bridge.Endpoint);

        var models = bridge.GetExportedModels();
        Assert.NotEmpty(models);
        Assert.Contains(models, m => m.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IndustrialControls_ExposeExpectedDefaultsAndProperties()
    {
        var tank = new TankLevelControl
        {
            LevelPercent = 72.5,
            HighHighLimit = 92.0,
            HighLimit = 82.0,
            LowLimit = 18.0,
            LowLowLimit = 8.0,
            Label = "TK-301"
        };

        Assert.Equal(72.5, tank.LevelPercent);
        Assert.Equal(92.0, tank.HighHighLimit);
        Assert.Equal(82.0, tank.HighLimit);
        Assert.Equal(18.0, tank.LowLimit);
        Assert.Equal(8.0, tank.LowLowLimit);
        Assert.Equal("TK-301", tank.Label);

        var valve = new ValveIndicatorControl
        {
            State = ValveState.Transitioning,
            Label = "XV-101"
        };

        Assert.Equal(ValveState.Transitioning, valve.State);
        Assert.Equal("XV-101", valve.Label);
    }

    [Fact]
    public async Task SimulatedTelemetryService_CoreIntegration_PersistsConfigSamplesAlarmsAndProbesGracefully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"archscada-desktop-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var serverClient = new ScadaServerClient(new ScadaServerOptions
            {
                BaseUrl = "http://127.0.0.1:59999",
                Timeout = TimeSpan.FromMilliseconds(200)
            });

            var telemetry = new SimulatedTelemetryService(tempDir, serverClient);

            await telemetry.PersistConfigurationSnapshotAsync();
            var cachedConfig = await telemetry.ConfigCache.LoadConfigAsync();
            Assert.NotNull(cachedConfig);
            Assert.Equal(23, cachedConfig.Equipment.Count);
            Assert.True(cachedConfig.Areas.Count >= 6);

            await telemetry.PersistCurrentSamplesAsync();
            var samples = await telemetry.HistorianStore.QuerySamplesAsync(tagId: "CF501_MOIST");
            Assert.Single(samples);

            var alarm = telemetry.Alarms.First(a => !a.IsAcknowledged);
            alarm.IsAcknowledged = true;
            alarm.AcknowledgedAt = DateTime.Now;
            await telemetry.PersistAlarmEventAsync(alarm, "Acknowledged");
            var alarmEvents = await telemetry.HistorianStore.LoadAlarmEventsAsync(equipmentId: alarm.EquipmentId);
            Assert.Single(alarmEvents);
            Assert.True(alarmEvents[0].IsAcknowledged);

            var isOnline = await telemetry.ProbeServerHealthAsync();
            Assert.False(isOnline);
            Assert.False(telemetry.IsServerConnected);
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
    public async Task MainShellViewModel_ControlBoundary_RunsSimulatedAuditInReadOnlyMode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"archscada-control-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var auditPath = Path.Combine(tempDir, "control-audit.jsonl");
            var auditLogger = new ControlAuditLogger(auditPath);
            var engine = new ControlBoundaryEngine(
                auditLogger,
                authorizationState: new ControlAuthorizationState
                {
                    IsRealControlAuthorized = false,
                    CauseAndEffectVerified = false
                },
                initialRules: new[]
                {
                    new CauseAndEffectRule
                    {
                        RuleId = "CE-SIM-001",
                        EquipmentId = "CR-101",
                        CommandType = "SIM_INTERLOCK_AUDIT",
                        RequiredPermission = "OPERATOR_SIM_TEST",
                        SafeState = "ReadOnly",
                        IsApproved = true
                    }
                });

            var telemetry = new SimulatedTelemetryService(tempDir);
            using var bridge = new UnityBridgeService();
            var vm = new MainShellViewModel(telemetry, bridge, offlineDemoMode: true, controlBoundary: engine);

            try
            {
                Assert.Contains("Read-Only / Simulated", vm.ControlModeSummary);
                await vm.RunSimulatedControlAuditCommand.ExecuteAsync(null);

                Assert.Contains("Simulated", vm.LastControlAuditResult);
                var entries = auditLogger.ReadAll();
                Assert.Single(entries);
                Assert.True(entries[0].Simulated);
                Assert.Equal("CR-101", entries[0].Equipment);
            }
            finally
            {
                telemetry.Stop();
            }
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
    public void MainShellViewModel_MicroOptimization_AppliesBoundedSimulatedTrimsAndImprovesEfficiency()
    {
        var telemetry = new SimulatedTelemetryService();
        using var bridge = new UnityBridgeService();
        var vm = new MainShellViewModel(telemetry, bridge, offlineDemoMode: true);

        try
        {
            Assert.True(vm.IsAutoOptimizationEnabled);
            int beforeTrims = vm.TotalMicroTrimsApplied;
            double beforeEff = vm.PlantEfficiencyScorePercent;

            vm.StepMicroOptimizationCommand.Execute(null);

            Assert.Equal(beforeTrims + 1, vm.TotalMicroTrimsApplied);
            Assert.True(vm.PlantEfficiencyScorePercent >= beforeEff);
            Assert.NotEmpty(vm.OptimizationLog);
            Assert.Equal("SIMULATED - NO DEVICE WRITE", vm.OptimizationLog[0].AuditStatus);
        }
        finally
        {
            telemetry.Stop();
        }
    }

    [Fact]
    public void MainShellViewModel_Isa182AlarmsCoupledPhysicsAndOperatorRbac_WorkEndToEnd()
    {
        var telemetry = new SimulatedTelemetryService();
        using var bridge = new UnityBridgeService();
        var vm = new MainShellViewModel(telemetry, bridge, offlineDemoMode: true);

        try
        {
            // 1. Coupled physics evaluation
            telemetry.CoupledModel.StepCoupledPhysics(
                telemetry.AllTags,
                out var yieldPct,
                out var ashPct,
                out var waterRecPct,
                out var specEnergy);

            Assert.InRange(yieldPct, 70.0, 92.0);
            Assert.InRange(ashPct, 6.0, 16.0);
            Assert.InRange(waterRecPct, 75.0, 96.0);
            Assert.True(specEnergy > 0);

            // 2. Operator RBAC & shift handover
            vm.SwitchOperatorCommand.Execute("supervisor1:2468");
            Assert.Equal(OperatorRole.ShiftSupervisor, vm.OperatorAuth.CurrentRole);
            Assert.True(vm.OperatorAuth.CanShelveAlarms);

            var alarm = vm.Alarms.First();
            vm.ShelveAlarmCommand.Execute(alarm);
            Assert.Equal(IsaAlarmState.Shelved, telemetry.AlarmEngine.GetState(alarm.Id));

            int notesBefore = vm.HandoverLog.Count;
            vm.HandoverNoteDraft = "Shift handover verified: DMC SG steady at 1.52.";
            vm.AddHandoverNoteCommand.Execute(null);
            Assert.Equal(notesBefore + 1, vm.HandoverLog.Count);
        }
        finally
        {
            telemetry.Stop();
        }
    }
}
