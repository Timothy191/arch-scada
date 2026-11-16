using System;
using System.Linq;
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
    public void SimulatedTelemetryService_SeedsElevenEquipmentItems()
    {
        var telemetry = new SimulatedTelemetryService();

        Assert.Equal(11, telemetry.Equipment.Count);
        Assert.Contains(telemetry.Equipment, e => e.Id == "CR-101");
        Assert.Contains(telemetry.Equipment, e => e.Id == "TK-201");
        Assert.Contains(telemetry.Equipment, e => e.Id == "DM-301");
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
            Assert.Equal(section == PlantSection.DigitalTwin3D, vm.IsDigitalTwin3D);
            Assert.Equal(section == PlantSection.EquipmentAssets, vm.IsEquipmentAssets);
            Assert.Equal(section == PlantSection.TrendsHistorian, vm.IsTrends);
            Assert.Equal(section == PlantSection.AlarmsEvents, vm.IsAlarms);
            Assert.Equal(section == PlantSection.Diagnostics, vm.IsDiagnostics);
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
}
