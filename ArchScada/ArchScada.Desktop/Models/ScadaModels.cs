using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArchScada.Desktop.Models;

public enum EquipmentStatus
{
    Running,
    Stopped,
    Warning,
    Fault,
    Offline,
    Maintenance
}

public enum AlarmSeverity
{
    Critical,
    High,
    Medium,
    Low
}

public enum StartupCheckState
{
    Pending,
    Running,
    Passed,
    Warning,
    Failed
}

public enum PlantSection
{
    Overview,
    CrushingConveyors,
    ThickenerCircuit,
    MagnetitePrep,
    DigitalTwin3D,
    EquipmentAssets,
    TrendsHistorian,
    AlarmsEvents,
    Diagnostics
}

public partial class StartupCheckItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StartupCheckState State { get; set; } = StartupCheckState.Pending;
}

public partial class TelemetryTag : ObservableObject
{
    public string TagId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double MinLimit { get; init; }
    public double MaxLimit { get; init; } = 100;

    [ObservableProperty]
    public partial double Value { get; set; }

    [ObservableProperty]
    public partial DateTime Timestamp { get; set; } = DateTime.Now;

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    public string FormattedValue => $"{Value:0.##} {Unit}".Trim();
}

public partial class EquipmentItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string InterlockSummary { get; init; } = "Hardware Interlock OK (Read-Only)";

    [ObservableProperty]
    public partial EquipmentStatus Status { get; set; } = EquipmentStatus.Stopped;

    [ObservableProperty]
    public partial double PowerKw { get; set; }

    [ObservableProperty]
    public partial double LoadPercent { get; set; }

    [ObservableProperty]
    public partial double PrimaryMetric { get; set; }

    public string PrimaryMetricUnit { get; init; } = string.Empty;
    public string PrimaryMetricLabel { get; init; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    public List<TelemetryTag> Tags { get; init; } = new();

    public string PrimaryMetricDisplay => $"{PrimaryMetric:0.#} {PrimaryMetricUnit}".Trim();
}

public partial class AlarmItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public AlarmSeverity Severity { get; init; }
    public string Area { get; init; } = string.Empty;
    public string EquipmentId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAcknowledged { get; set; }

    [ObservableProperty]
    public partial DateTime? AcknowledgedAt { get; set; }

    public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
}

public partial class Unity3DEffectSettings : ObservableObject
{
    [ObservableProperty]
    public partial bool ShowFlowParticles { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowThermalHeatmap { get; set; }

    [ObservableProperty]
    public partial bool ShowAlarmHalos { get; set; } = true;

    [ObservableProperty]
    public partial bool AnimateConveyors { get; set; } = true;

    [ObservableProperty]
    public partial string CameraPreset { get; set; } = "Plant Overview";
}
