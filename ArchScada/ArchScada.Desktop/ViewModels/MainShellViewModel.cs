using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArchScada.ControlBoundary;
using ArchScada.Desktop.Controls;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.ViewModels;

public partial class MainShellViewModel : ViewModelBase
{
    private readonly ITelemetryService _telemetry;
    private readonly UnityBridgeService _unityBridge;
    private readonly ControlBoundaryEngine _controlBoundary;
    private readonly DispatcherTimer _clockTimer;

    public ITelemetryService Telemetry => _telemetry;
    public UnityBridgeService UnityBridge => _unityBridge;
    public ControlBoundaryEngine ControlBoundary => _controlBoundary;
    public OperatorAuthService OperatorAuth { get; } = new();

    public ObservableCollection<EquipmentItem> Equipment => _telemetry.Equipment;
    public ObservableCollection<AlarmItem> Alarms => _telemetry.Alarms;
    public ObservableCollection<TelemetryTag> AllTags => _telemetry.AllTags;
    public ObservableCollection<OptimizationTrimRecord> OptimizationLog => _telemetry.OptimizationLog;
    public ObservableCollection<ShiftHandoverEntry> HandoverLog => OperatorAuth.HandoverLog;

    [ObservableProperty]
    public partial string HandoverNoteDraft { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OperatorBadge { get; set; } = "J. Mokoena (CCR Operator) [Operator]";

    [ObservableProperty]
    public partial string NativeProtocolStatus { get; set; } = "Native TCP 127.0.0.1:10000 (34 Cnl mapped)";

    public ObservableCollection<EquipmentItem> CrushingEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> ThickenerEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> MagnetiteEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> FlotationEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> DewateringEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> RomLoadoutEquipment { get; } = new();
    public ObservableCollection<string> Exported3DModels { get; } = new();

    public Unity3DEffectSettings UnityEffects { get; } = new();

    public IReadOnlyList<string> CameraPresets { get; } = new[]
    {
        "Plant Overview",
        "Crushing & Conveyors",
        "Thickener Circuit",
        "Magnetite Prep",
        "Fine Coal & Flotation",
        "Dewatering & Tailings",
        "ROM & Silo Loadout",
        "Top-Down Schematic"
    };

    [ObservableProperty]
    public partial PlantSection CurrentSection { get; set; } = PlantSection.Overview;

    [ObservableProperty]
    public partial string CurrentSectionTitle { get; set; } = "Plant Overview";

    [ObservableProperty]
    public partial string CurrentTimeDisplay { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    [ObservableProperty]
    public partial string ConnectionStatusLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double TotalFeedTonnageTph { get; set; }

    [ObservableProperty]
    public partial double TotalPowerDrawKw { get; set; }

    [ObservableProperty]
    public partial double WaterRecoveryPercent { get; set; }

    [ObservableProperty]
    public partial double MediumSpecificGravity { get; set; }

    [ObservableProperty]
    public partial double PlantEfficiencyScorePercent { get; set; } = 94.2;

    [ObservableProperty]
    public partial double CleanCoalYieldPercent { get; set; } = 83.8;

    [ObservableProperty]
    public partial double SpecificEnergyKwhPerTon { get; set; } = 2.28;

    [ObservableProperty]
    public partial int TotalMicroTrimsApplied { get; set; }

    public bool IsAutoOptimizationEnabled
    {
        get => _telemetry.IsAutoOptimizationEnabled;
        set
        {
            if (_telemetry.IsAutoOptimizationEnabled != value)
            {
                _telemetry.IsAutoOptimizationEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    [ObservableProperty]
    public partial double ThickenerBedLevelPercent { get; set; } = 71.5;

    [ObservableProperty]
    public partial double MagnetiteTankLevelPercent { get; set; } = 66.0;

    [ObservableProperty]
    public partial ValveState ThickenerValveState { get; set; } = ValveState.Open;

    [ObservableProperty]
    public partial ValveState MagnetiteValveState { get; set; } = ValveState.Open;

    [ObservableProperty]
    public partial int ActiveAlarmCount { get; set; }

    [ObservableProperty]
    public partial int UnacknowledgedAlarmCount { get; set; }

    [ObservableProperty]
    public partial string LatestAlarmTicker { get; set; } = "No active alarms";

    [ObservableProperty]
    public partial EquipmentItem? SelectedEquipment { get; set; }

    [ObservableProperty]
    public partial TelemetryTag? SelectedTrendTag { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double> SelectedTrendValues { get; set; } = Array.Empty<double>();

    [ObservableProperty]
    public partial string UnityStreamStatus { get; set; } = "Ready (UDP 127.0.0.1:17850)";

    [ObservableProperty]
    public partial string BlenderBuildStatus { get; set; } = "Idle";

    [ObservableProperty]
    public partial bool IsGeneratingBlenderAssets { get; set; }

    [ObservableProperty]
    public partial string LastControlAuditResult { get; set; } = "No simulated audit executed yet";

    [ObservableProperty]
    public partial string? FocusTargetId { get; set; }

    public string ControlModeSummary => _controlBoundary.AuthorizationState.CanExecuteRealControl
        ? "Authorized Real Control Active"
        : "Read-Only / Simulated (Real Control Disabled)";

    public bool IsOverview => CurrentSection == PlantSection.Overview;
    public bool IsCrushing => CurrentSection == PlantSection.CrushingConveyors;
    public bool IsThickener => CurrentSection == PlantSection.ThickenerCircuit;
    public bool IsMagnetite => CurrentSection == PlantSection.MagnetitePrep;
    public bool IsFineCoal => CurrentSection == PlantSection.FineCoalFlotation;
    public bool IsDewatering => CurrentSection == PlantSection.DewateringTailings;
    public bool IsRomLoadout => CurrentSection == PlantSection.RomLoadoutQuality;
    public bool IsDigitalTwin3D => CurrentSection == PlantSection.DigitalTwin3D;
    public bool IsEquipmentAssets => CurrentSection == PlantSection.EquipmentAssets;
    public bool IsTrends => CurrentSection == PlantSection.TrendsHistorian;
    public bool IsAlarms => CurrentSection == PlantSection.AlarmsEvents;
    public bool IsDiagnostics => CurrentSection == PlantSection.Diagnostics;

    public MainShellViewModel(
        ITelemetryService telemetry,
        UnityBridgeService unityBridge,
        bool offlineDemoMode,
        ControlBoundaryEngine? controlBoundary = null)
    {
        _telemetry = telemetry;
        _unityBridge = unityBridge;
        _controlBoundary = controlBoundary ?? CreateDefaultControlBoundary();

        foreach (var eq in _telemetry.Equipment)
        {
            if (eq.Area == "Crushing & Conveyors") CrushingEquipment.Add(eq);
            else if (eq.Area == "Thickener Circuit") ThickenerEquipment.Add(eq);
            else if (eq.Area == "Magnetite Preparation") MagnetiteEquipment.Add(eq);
            else if (eq.Area == "Fine Coal & Froth Flotation") FlotationEquipment.Add(eq);
            else if (eq.Area == "Dewatering & Tailings") DewateringEquipment.Add(eq);
            else if (eq.Area == "ROM, Loadout & PGNAA") RomLoadoutEquipment.Add(eq);
        }

        SelectedEquipment = _telemetry.Equipment.FirstOrDefault();
        SelectedTrendTag = _telemetry.AllTags.FirstOrDefault();

        _telemetry.TelemetryUpdated += (_, _) => RefreshFromTelemetry();
        _telemetry.Start(offlineDemoMode);

        RefreshExportedModels();
        RefreshFromTelemetry();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => CurrentTimeDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clockTimer.Start();
    }

    private static ControlBoundaryEngine CreateDefaultControlBoundary()
    {
        var auditPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArchScada",
            "control-audit.jsonl");

        var logger = new ControlAuditLogger(auditPath);
        var rules = CauseAndEffectMatrixLoader.LoadMatrix();

        return new ControlBoundaryEngine(
            logger,
            authorizationState: new ControlAuthorizationState
            {
                IsRealControlAuthorized = false,
                CauseAndEffectVerified = true,
                Scope = "ALL_6_CHPP_CIRCUITS"
            },
            initialRules: rules);
    }

    partial void OnCurrentSectionChanged(PlantSection value)
    {
        CurrentSectionTitle = value switch
        {
            PlantSection.Overview => "Plant Overview",
            PlantSection.CrushingConveyors => "Crushing & Conveyor Circuit",
            PlantSection.ThickenerCircuit => "Thickener Circuit",
            PlantSection.MagnetitePrep => "Magnetite Preparation",
            PlantSection.FineCoalFlotation => "Fine Coal & Froth Flotation",
            PlantSection.DewateringTailings => "Dewatering & Tailings",
            PlantSection.RomLoadoutQuality => "ROM, Loadout & PGNAA",
            PlantSection.DigitalTwin3D => "3D Digital Twin (Unity & Blender)",
            PlantSection.EquipmentAssets => "Equipment & Assets Inspector",
            PlantSection.TrendsHistorian => "Trends & Local Historian",
            PlantSection.AlarmsEvents => "Alarms & Events",
            PlantSection.Diagnostics => "System & Driver Diagnostics",
            _ => "Plant Overview"
        };

        OnPropertyChanged(nameof(IsOverview));
        OnPropertyChanged(nameof(IsCrushing));
        OnPropertyChanged(nameof(IsThickener));
        OnPropertyChanged(nameof(IsMagnetite));
        OnPropertyChanged(nameof(IsFineCoal));
        OnPropertyChanged(nameof(IsDewatering));
        OnPropertyChanged(nameof(IsRomLoadout));
        OnPropertyChanged(nameof(IsDigitalTwin3D));
        OnPropertyChanged(nameof(IsEquipmentAssets));
        OnPropertyChanged(nameof(IsTrends));
        OnPropertyChanged(nameof(IsAlarms));
        OnPropertyChanged(nameof(IsDiagnostics));
    }

    partial void OnSelectedTrendTagChanged(TelemetryTag? value)
    {
        if (value is not null)
        {
            SelectedTrendValues = _telemetry.GetTagHistory(value.TagId).ToList();
        }
    }

    [RelayCommand]
    private void Navigate(PlantSection section)
    {
        CurrentSection = section;
    }

    [RelayCommand]
    private void SelectEquipment(EquipmentItem? item)
    {
        if (item is not null)
        {
            SelectedEquipment = item;
        }
    }

    [RelayCommand]
    private void AcknowledgeAlarm(AlarmItem? alarm)
    {
        if (alarm is not null)
        {
            _telemetry.AcknowledgeAlarm(alarm.Id);
        }
    }

    [RelayCommand]
    private void AcknowledgeAllAlarms()
    {
        _telemetry.AcknowledgeAllAlarms();
    }

    [RelayCommand]
    private void SetCameraPreset(string preset)
    {
        UnityEffects.CameraPreset = preset;
        _unityBridge.PublishFrame(_telemetry.Equipment, UnityEffects);
    }

    [RelayCommand]
    private async Task GenerateBlenderAssetsAsync()
    {
        if (IsGeneratingBlenderAssets) return;
        IsGeneratingBlenderAssets = true;
        BlenderBuildStatus = "Running Blender headless export...";

        var (success, output) = await _unityBridge.GenerateBlenderAssetsAsync();
        BlenderBuildStatus = success ? output : $"Error: {output}";
        if (success)
        {
            ArchScada.Desktop.Controls.ThreeD.Plant3DSceneData.InvalidateMeshCache();
        }
        RefreshExportedModels();
        IsGeneratingBlenderAssets = false;
    }

    [RelayCommand]
    private async Task RunSimulatedControlAuditAsync()
    {
        var request = new ControlCommandRequest
        {
            OperatorId = "HMI_OPERATOR",
            OperatorPermissions = new[] { "OPERATOR_SIM_TEST" },
            RequiredPermission = "OPERATOR_SIM_TEST",
            IsSessionActive = true,
            CommandType = "SIM_INTERLOCK_AUDIT",
            EquipmentId = "CR-101",
            OperatorConfirmed = true,
            InterlocksSatisfied = true,
            InterlockIds = new[] { "ILK-CR101-LUBE", "ILK-CV101-RUN" },
            EquipmentFaulted = false,
            CommunicationQuality = CommunicationQuality.Good,
            LastTelemetryAge = TimeSpan.FromSeconds(1)
        };

        var result = await _controlBoundary.ExecuteCommandAsync(request);
        var shortAuditId = result.AuditId.Length >= 8 ? result.AuditId[..8] : result.AuditId;
        LastControlAuditResult = $"{result.Status} (Simulated={result.IsSimulated}) · Audit #{shortAuditId} · {result.Message}";
    }

    [RelayCommand]
    private void FocusSelected3D()
    {
        if (SelectedEquipment is null) return;
        FocusTargetId = null;
        FocusTargetId = SelectedEquipment.Id;
    }

    [RelayCommand]
    private void FocusActiveAlarm3D()
    {
        var alarm = _telemetry.Alarms.FirstOrDefault(a => !a.IsAcknowledged) ?? _telemetry.Alarms.FirstOrDefault();
        if (alarm is null) return;
        var eq = _telemetry.Equipment.FirstOrDefault(e => e.Id == alarm.EquipmentId);
        if (eq is not null) SelectedEquipment = eq;
        FocusTargetId = null;
        FocusTargetId = alarm.EquipmentId;
    }

    [RelayCommand]
    private void StepMicroOptimization()
    {
        if (!OperatorAuth.CanChangeOptimizationMode) return;
        _telemetry.ApplyOptimizationTick();
        RefreshFromTelemetry();
    }

    [RelayCommand]
    private void SilenceHorn()
    {
        if (_telemetry is SimulatedTelemetryService sim)
        {
            sim.AlarmEngine.SilenceHorn();
        }
    }

    [RelayCommand]
    private void ShelveAlarm(AlarmItem? alarm)
    {
        if (alarm is null || !OperatorAuth.CanShelveAlarms) return;
        if (_telemetry is SimulatedTelemetryService sim)
        {
            sim.AlarmEngine.ShelveAlarm(alarm.Id, TimeSpan.FromMinutes(15));
        }
    }

    [RelayCommand]
    private void SwitchOperator(string? usernamePin)
    {
        if (string.IsNullOrWhiteSpace(usernamePin))
            return;

        var parts = usernamePin.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return;

        var username = parts[0];
        var pin = parts[1];

        if (OperatorAuth.Login(username, pin))
        {
            OperatorBadge = $"{OperatorAuth.CurrentDisplayName} [{OperatorAuth.CurrentRole}]";
        }
    }

    [RelayCommand]
    private void AddHandoverNote()
    {
        if (string.IsNullOrWhiteSpace(HandoverNoteDraft)) return;
        OperatorAuth.AddShiftHandoverNote(HandoverNoteDraft);
        HandoverNoteDraft = string.Empty;
    }

    private void RefreshExportedModels()
    {
        Exported3DModels.Clear();
        foreach (var model in _unityBridge.GetExportedModels())
        {
            Exported3DModels.Add(model);
        }
    }

    private void RefreshFromTelemetry()
    {
        ConnectionStatusLabel = _telemetry.ConnectionModeLabel;
        TotalFeedTonnageTph = _telemetry.TotalFeedTonnageTph;
        TotalPowerDrawKw = _telemetry.TotalPowerDrawKw;
        WaterRecoveryPercent = _telemetry.WaterRecoveryPercent;
        MediumSpecificGravity = _telemetry.MediumSpecificGravity;
        PlantEfficiencyScorePercent = _telemetry.PlantEfficiencyScorePercent;
        CleanCoalYieldPercent = _telemetry.CleanCoalYieldPercent;
        SpecificEnergyKwhPerTon = _telemetry.SpecificEnergyKwhPerTon;
        TotalMicroTrimsApplied = _telemetry.TotalMicroTrimsApplied;
        ActiveAlarmCount = _telemetry.ActiveAlarmCount;
        UnacknowledgedAlarmCount = _telemetry.UnacknowledgedAlarmCount;

        var tk201 = _telemetry.Equipment.FirstOrDefault(e => e.Id == "TK-201");
        if (tk201 is not null)
        {
            ThickenerBedLevelPercent = tk201.PrimaryMetric;
        }

        var pp201 = _telemetry.Equipment.FirstOrDefault(e => e.Id == "PP-201");
        if (pp201 is not null)
        {
            ThickenerValveState = pp201.Status == EquipmentStatus.Running ? ValveState.Open : ValveState.Closed;
        }

        var tk301Lvl = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "TK301_LVL");
        if (tk301Lvl is not null)
        {
            MagnetiteTankLevelPercent = tk301Lvl.Value;
        }

        var dm301 = _telemetry.Equipment.FirstOrDefault(e => e.Id == "DM-301");
        if (dm301 is not null)
        {
            MagnetiteValveState = dm301.Status == EquipmentStatus.Running ? ValveState.Open : ValveState.Closed;
        }

        var topAlarm = _telemetry.Alarms.FirstOrDefault(a => !a.IsAcknowledged) ?? _telemetry.Alarms.FirstOrDefault();
        LatestAlarmTicker = topAlarm is not null
            ? $"[{topAlarm.Severity}] {topAlarm.TimeDisplay} — {topAlarm.Message}"
            : "All systems normal — no active alarms";

        if (SelectedTrendTag is not null)
        {
            SelectedTrendValues = _telemetry.GetTagHistory(SelectedTrendTag.TagId).ToList();
        }

        _unityBridge.PublishFrame(_telemetry.Equipment, UnityEffects);
        UnityStreamStatus = $"Streaming {_unityBridge.Endpoint} · {_unityBridge.PacketsSent} frames";
    }
}
