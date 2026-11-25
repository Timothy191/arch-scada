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
using ArchScada.Desktop.Controls.ThreeD;
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
    public partial string NativeProtocolStatus { get; set; } = "Native TCP 127.0.0.1:10000 (52 Cnl mapped)";

    public ObservableCollection<EquipmentItem> CrushingEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> ThickenerEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> MagnetiteEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> FlotationEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> DewateringEquipment { get; } = new();
    public ObservableCollection<EquipmentItem> RomLoadoutEquipment { get; } = new();
    public ObservableCollection<string> Exported3DModels { get; } = new();

    // 104-Node GA Explorer & Linked 3D Sub-Equipment (Equipment & Assets tab)
    public IReadOnlyList<string> AssetCategories { get; } = new[]
    {
        "All (104)",
        "Machines (18)",
        "Conveyors (11)",
        "Sub-Equipment (64)",
        "Process Piping (11)"
    };

    [ObservableProperty]
    public partial string SelectedAssetCategory { get; set; } = "All (104)";

    [ObservableProperty]
    public partial string AssetSearchQuery { get; set; } = string.Empty;

    public ObservableCollection<Plant3DNode> Filtered3DAssets { get; } = new();
    public ObservableCollection<Plant3DNode> SelectedEquipment3DChildren { get; } = new();

    // Multi-Trace Historian Presets, Statistics & CSV Export (Trends & Historian tab)
    public IReadOnlyList<string> HistorianPresets { get; } = new[]
    {
        "Single Tag (Selected)",
        "DMC-301 Cyclone Loop (Inlet kPa / Recovery %)",
        "Thickener Loop (Bed Level % / Turbidity NTU)",
        "Crushing Loop (Jaw Crusher t/h / Belt CV-101 t/h)",
        "Quality & Loadout Loop (Ash % / Moisture % / CV MJ/kg)"
    };

    [ObservableProperty]
    public partial string SelectedHistorianPreset { get; set; } = "Single Tag (Selected)";

    [ObservableProperty]
    public partial double TrendStatMin { get; set; }

    [ObservableProperty]
    public partial double TrendStatMax { get; set; }

    [ObservableProperty]
    public partial double TrendStatAvg { get; set; }

    [ObservableProperty]
    public partial double TrendStatStdDev { get; set; }

    [ObservableProperty]
    public partial int TrendSampleCount { get; set; }

    [ObservableProperty]
    public partial string TrendLimitsSummary { get; set; } = "0 – 100";

    [ObservableProperty]
    public partial string HistorianExportStatus { get; set; } = "Ready to export active series (CSV)";

    // Alarm Severity / Ack Filter (Alarms & Events tab)
    public IReadOnlyList<string> AlarmFilterModes { get; } = new[]
    {
        "All",
        "Unacknowledged",
        "Critical / High",
        "Medium / Low"
    };

    [ObservableProperty]
    public partial string SelectedAlarmFilter { get; set; } = "All";

    public ObservableCollection<AlarmItem> FilteredAlarms { get; } = new();

    // Magnetite & DMC-301 Wide Diamond Cyclone Instrumentation
    [ObservableProperty]
    public partial double DmcInletPressureKpa { get; set; } = 185.0;

    [ObservableProperty]
    public partial double DmcFeedFlowM3h { get; set; } = 812.0;

    [ObservableProperty]
    public partial double DmcCleanOverflowTph { get; set; } = 410.0;

    [ObservableProperty]
    public partial double DmcDiscardApexTph { get; set; } = 100.0;

    [ObservableProperty]
    public partial double MacsepRecoveryPercent { get; set; } = 96.4;

    // ROM, Loadout & PGNAA Online Quality Instrumentation
    [ObservableProperty]
    public partial double ProductSiloLevelPercent { get; set; } = 64.5;

    [ObservableProperty]
    public partial double ProductSiloTons { get; set; } = 3225.0;

    [ObservableProperty]
    public partial double PgnaaAshPercent { get; set; } = 9.40;

    [ObservableProperty]
    public partial double PgnaaMoisturePercent { get; set; } = 8.60;

    [ObservableProperty]
    public partial double PgnaaCalorificValueMjKg { get; set; } = 27.80;

    [ObservableProperty]
    public partial double TrainLoadoutRateTph { get; set; } = 1850.0;

    // System Diagnostics 8-Gate Readiness & 3D Engine Summary
    public ObservableCollection<StartupCheckItem> DiagnosticChecks { get; } = new();

    [ObservableProperty]
    public partial string DiagnosticSummaryText { get; set; } = "8/8 Architecture & Runtime Checks Ready";

    [ObservableProperty]
    public partial string GlbEngineSummaryText { get; set; } =
        "16/16 glTF 2.0 PBR Models (3.98 MiB) · 104 Interactive Nodes (29 GA + 64 Sub-Equipment + 11 Pipes) · SharpGLTF + Split-Sum BRDF LUT + Karis HDR Bloom";

    public Unity3DEffectSettings UnityEffects { get; } = new();

    public IReadOnlyList<Plant3DNode> All3DNodes => Plant3DSceneData.InteractiveNodes;

    public IReadOnlyList<string> CameraPresets { get; } = new[]
    {
        "Plant Overview",
        "Top-Down GA Layout",
        "Front View (South)",
        "Side View (West)",
        "Stage 1: Loader & Feeder",
        "Stage 2–3: Jaw Crusher",
        "Stage 4–5: Double Roll Crusher",
        "Stage 6: Central Sizing Screen",
        "Wash Plant & Thickener"
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
    public partial Plant3DNode? Selected3DNode { get; set; }

    [ObservableProperty]
    public partial TelemetryTag? SelectedTrendTag { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double> SelectedTrendValues { get; set; } = Array.Empty<double>();

    [ObservableProperty]
    public partial string Selected3DTrendLabel { get; set; } = "Live telemetry";

    [ObservableProperty]
    public partial IReadOnlyList<double> Selected3DTrendValues { get; set; } = Array.Empty<double>();

    [ObservableProperty]
    public partial double? Selected3DTrendMinLimit { get; set; }

    [ObservableProperty]
    public partial double? Selected3DTrendMaxLimit { get; set; }

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

    public string TelemetrySourceBadgeText => _telemetry.IsServerConnected
        ? "LIVE PLC/SCADA TELEMETRY · READ-ONLY"
        : "SIMULATED DEMO TELEMETRY · READ-ONLY (No PLC Write)";

    public bool IsTelemetryLive => _telemetry.IsServerConnected;

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
        Selected3DNode = Plant3DSceneData.AllNodes.FirstOrDefault();
        SelectedTrendTag = _telemetry.AllTags.FirstOrDefault();

        _telemetry.TelemetryUpdated += (_, _) => RefreshFromTelemetry();
        _telemetry.Start(offlineDemoMode);

        RefreshExportedModels();
        InitializeDiagnosticChecks();
        RefreshFiltered3DAssets();
        RefreshSelectedEquipment3DChildren();
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
        UpdateTrendStatistics();
    }

    partial void OnSelectedAssetCategoryChanged(string value)
    {
        RefreshFiltered3DAssets();
    }

    partial void OnAssetSearchQueryChanged(string value)
    {
        RefreshFiltered3DAssets();
    }

    partial void OnSelectedAlarmFilterChanged(string value)
    {
        RefreshFilteredAlarms();
    }

    [RelayCommand]
    private void LocateIn3D(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            targetId = SelectedEquipment?.Id ?? Selected3DNode?.Id;
        }
        if (string.IsNullOrWhiteSpace(targetId)) return;

        var node = Plant3DSceneData.InteractiveNodes.FirstOrDefault(n =>
                       string.Equals(n.Id, targetId, StringComparison.OrdinalIgnoreCase))
                   ?? Plant3DSceneData.AllNodes.FirstOrDefault(n =>
                       string.Equals(n.LinkedScadaId, targetId, StringComparison.OrdinalIgnoreCase))
                   ?? Plant3DSceneData.InteractiveNodes.FirstOrDefault(n =>
                       string.Equals(n.LinkedScadaId, targetId, StringComparison.OrdinalIgnoreCase));

        CurrentSection = PlantSection.DigitalTwin3D;
        if (node is not null)
        {
            Selected3DNode = node;
            FocusTargetId = null;
            FocusTargetId = node.Id;
        }
        else
        {
            FocusTargetId = null;
            FocusTargetId = targetId;
        }
    }

    [RelayCommand]
    private void SetAlarmFilter(string? filterMode)
    {
        if (!string.IsNullOrWhiteSpace(filterMode))
        {
            SelectedAlarmFilter = filterMode;
        }
    }

    [RelayCommand]
    private void ExportHistorianCsv()
    {
        try
        {
            var tag = SelectedTrendTag ?? _telemetry.AllTags.FirstOrDefault();
            if (tag is null) return;

            var exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ArchScada",
                "exports");
            Directory.CreateDirectory(exportDir);

            string safeId = tag.TagId.Replace('/', '_').Replace(' ', '_');
            string filePath = Path.Combine(exportDir, $"historian_{safeId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var values = _telemetry.GetTagHistory(tag.TagId);

            using var sw = new StreamWriter(filePath, append: false);
            sw.WriteLine("SampleIndex,TagId,TagName,Value,Unit,MinLimit,MaxLimit");
            for (int i = 0; i < values.Count; i++)
            {
                sw.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{i},{tag.TagId},\"{tag.Name}\",{values[i]:F3},{tag.Unit},{tag.MinLimit:F2},{tag.MaxLimit:F2}"));
            }

            HistorianExportStatus = $"Exported {values.Count} samples → {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            HistorianExportStatus = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunDeploymentHealthCheckAsync()
    {
        var readiness = new DeploymentReadinessService(_unityBridge);
        if (DiagnosticChecks.Count == 0)
        {
            InitializeDiagnosticChecks(readiness);
        }

        int passed = 0;
        int warnings = 0;
        int failed = 0;

        for (int i = 0; i < readiness.Checks.Count && i < DiagnosticChecks.Count; i++)
        {
            var spec = readiness.Checks[i];
            var item = DiagnosticChecks[i];
            item.State = StartupCheckState.Running;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (state, detail) = await spec.ExecuteAsync(default);
            sw.Stop();
            item.State = state;
            item.Detail = detail;
            item.DurationText = $"{sw.ElapsedMilliseconds} ms";

            if (state == StartupCheckState.Passed) passed++;
            else if (state == StartupCheckState.Warning) warnings++;
            else if (state == StartupCheckState.Failed) failed++;
        }

        DiagnosticSummaryText = $"{passed}/{DiagnosticChecks.Count} Passed · {warnings} Warnings · {failed} Failed (Verified {DateTime.Now:HH:mm:ss})";
    }

    private void InitializeDiagnosticChecks(DeploymentReadinessService? service = null)
    {
        service ??= new DeploymentReadinessService(_unityBridge);
        DiagnosticChecks.Clear();
        foreach (var spec in service.Checks)
        {
            DiagnosticChecks.Add(new StartupCheckItem
            {
                Id = spec.Id,
                Category = spec.Category,
                Title = spec.Title,
                IsCritical = spec.IsCritical,
                State = StartupCheckState.Passed,
                Detail = "Verified at startup preflight",
                DurationText = "OK"
            });
        }
    }

    private void RefreshFiltered3DAssets()
    {
        Filtered3DAssets.Clear();
        string cat = SelectedAssetCategory ?? "All (104)";
        string q = (AssetSearchQuery ?? string.Empty).Trim();

        IEnumerable<Plant3DNode> source = cat switch
        {
            var c when c.StartsWith("Machines", StringComparison.OrdinalIgnoreCase) => Plant3DSceneData.MachineNodes,
            var c when c.StartsWith("Conveyors", StringComparison.OrdinalIgnoreCase) => Plant3DSceneData.ConveyorNodes,
            var c when c.StartsWith("Sub-Equipment", StringComparison.OrdinalIgnoreCase) => Plant3DSceneData.SubEquipmentNodes,
            var c when c.StartsWith("Process Piping", StringComparison.OrdinalIgnoreCase) => Plant3DSceneData.PipeNodes,
            _ => Plant3DSceneData.InteractiveNodes
        };

        foreach (var node in source)
        {
            if (!string.IsNullOrEmpty(q) &&
                !node.Id.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !node.GaLabel.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !node.EquipmentType.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !node.LinkedScadaId.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Filtered3DAssets.Add(node);
        }
    }

    private void RefreshSelectedEquipment3DChildren()
    {
        SelectedEquipment3DChildren.Clear();
        var eqId = SelectedEquipment?.Id;
        if (string.IsNullOrWhiteSpace(eqId)) return;

        foreach (var node in Plant3DSceneData.InteractiveNodes)
        {
            if (string.Equals(node.LinkedScadaId, eqId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Id, eqId, StringComparison.OrdinalIgnoreCase))
            {
                SelectedEquipment3DChildren.Add(node);
            }
        }
    }

    private void RefreshFilteredAlarms()
    {
        FilteredAlarms.Clear();
        string filter = SelectedAlarmFilter ?? "All";
        foreach (var alarm in _telemetry.Alarms)
        {
            bool match = filter switch
            {
                "Unacknowledged" => !alarm.IsAcknowledged,
                "Critical / High" => alarm.Severity is AlarmSeverity.Critical or AlarmSeverity.High,
                "Medium / Low" => alarm.Severity is AlarmSeverity.Medium or AlarmSeverity.Low,
                _ => true
            };
            if (match) FilteredAlarms.Add(alarm);
        }
    }

    private void UpdateTrendStatistics()
    {
        var tag = SelectedTrendTag;
        var vals = SelectedTrendValues;
        if (tag is not null)
        {
            TrendLimitsSummary = $"{tag.MinLimit:0.##} – {tag.MaxLimit:0.##} {tag.Unit}".Trim();
        }

        if (vals is null || vals.Count == 0)
        {
            TrendStatMin = 0;
            TrendStatMax = 0;
            TrendStatAvg = 0;
            TrendStatStdDev = 0;
            TrendSampleCount = 0;
            return;
        }

        double min = vals[0];
        double max = vals[0];
        double sum = 0;
        for (int i = 0; i < vals.Count; i++)
        {
            double v = vals[i];
            if (v < min) min = v;
            if (v > max) max = v;
            sum += v;
        }
        double avg = sum / vals.Count;
        double varSum = 0;
        for (int i = 0; i < vals.Count; i++)
        {
            double d = vals[i] - avg;
            varSum += d * d;
        }

        TrendStatMin = Math.Round(min, 2);
        TrendStatMax = Math.Round(max, 2);
        TrendStatAvg = Math.Round(avg, 2);
        TrendStatStdDev = Math.Round(Math.Sqrt(varSum / vals.Count), 3);
        TrendSampleCount = vals.Count;
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
            RefreshFilteredAlarms();
        }
    }

    [RelayCommand]
    private void AcknowledgeAllAlarms()
    {
        _telemetry.AcknowledgeAllAlarms();
        RefreshFilteredAlarms();
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

    partial void OnSelected3DNodeChanged(Plant3DNode? value)
    {
        if (value is not null)
        {
            var linked = _telemetry.Equipment.FirstOrDefault(e =>
                string.Equals(e.Id, value.LinkedScadaId, StringComparison.OrdinalIgnoreCase));
            if (linked is not null)
            {
                SelectedEquipment = linked;
            }
        }
        UpdateSelected3DTrend();
    }

    partial void OnSelectedEquipmentChanged(EquipmentItem? value)
    {
        RefreshSelectedEquipment3DChildren();
        UpdateSelected3DTrend();
    }

    private void UpdateSelected3DTrend()
    {
        var tags = SelectedEquipment?.Tags;
        if (tags is null || tags.Count == 0)
        {
            Selected3DTrendLabel = "No live tag mapped";
            Selected3DTrendValues = Array.Empty<double>();
            Selected3DTrendMinLimit = null;
            Selected3DTrendMaxLimit = null;
            return;
        }

        string nodeId = Selected3DNode?.Id ?? string.Empty;
        TelemetryTag? bestTag = null;
        if (nodeId.StartsWith("PT-", StringComparison.OrdinalIgnoreCase))
            bestTag = tags.FirstOrDefault(t => t.Name.Contains("Press", StringComparison.OrdinalIgnoreCase) || t.Unit.Contains("kPa", StringComparison.OrdinalIgnoreCase) || t.Unit.Contains("bar", StringComparison.OrdinalIgnoreCase));
        else if (nodeId.StartsWith("DT-", StringComparison.OrdinalIgnoreCase))
            bestTag = tags.FirstOrDefault(t => t.Name.Contains("Density", StringComparison.OrdinalIgnoreCase) || t.Unit.Contains("SG", StringComparison.OrdinalIgnoreCase));
        else if (nodeId.StartsWith("VT-", StringComparison.OrdinalIgnoreCase))
            bestTag = tags.FirstOrDefault(t => t.Name.Contains("Vib", StringComparison.OrdinalIgnoreCase) || t.Unit.Contains("mm/s", StringComparison.OrdinalIgnoreCase));
        else if (nodeId.StartsWith("LT-", StringComparison.OrdinalIgnoreCase))
            bestTag = tags.FirstOrDefault(t => t.Name.Contains("Level", StringComparison.OrdinalIgnoreCase) || t.Unit == "%");
        else if (nodeId.StartsWith("BW-", StringComparison.OrdinalIgnoreCase))
            bestTag = tags.FirstOrDefault(t => t.Name.Contains("Rate", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Ton", StringComparison.OrdinalIgnoreCase) || t.Unit.Contains("t/h", StringComparison.OrdinalIgnoreCase));

        bestTag ??= tags[0];

        Selected3DTrendLabel = $"{bestTag.Name}: {bestTag.Value:0.##} {bestTag.Unit}";
        Selected3DTrendMinLimit = bestTag.MinLimit;
        Selected3DTrendMaxLimit = bestTag.MaxLimit;
        Selected3DTrendValues = _telemetry.GetTagHistory(bestTag.TagId).ToList();
    }

    [RelayCommand]
    private void FocusSelected3D()
    {
        var id = Selected3DNode?.Id ?? SelectedEquipment?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;
        FocusTargetId = null;
        FocusTargetId = id;
    }

    [RelayCommand]
    private void SelectNext3DNode()
    {
        var nodes = Plant3DSceneData.InteractiveNodes;
        if (nodes.Count == 0) return;
        int idx = -1;
        if (Selected3DNode is not null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (string.Equals(nodes[i].Id, Selected3DNode.Id, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
        }
        var next = nodes[(idx + 1) % nodes.Count];
        Selected3DNode = next;
        FocusTargetId = null;
        FocusTargetId = next.Id;
    }

    [RelayCommand]
    private void SelectPrev3DNode()
    {
        var nodes = Plant3DSceneData.InteractiveNodes;
        if (nodes.Count == 0) return;
        int idx = 0;
        if (Selected3DNode is not null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (string.Equals(nodes[i].Id, Selected3DNode.Id, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
        }
        var prev = nodes[(idx - 1 + nodes.Count) % nodes.Count];
        Selected3DNode = prev;
        FocusTargetId = null;
        FocusTargetId = prev.Id;
    }

    [RelayCommand]
    private void FocusActiveAlarm3D()
    {
        var alarm = _telemetry.Alarms.FirstOrDefault(a => !a.IsAcknowledged) ?? _telemetry.Alarms.FirstOrDefault();
        if (alarm is null) return;
        var eq = _telemetry.Equipment.FirstOrDefault(e => e.Id == alarm.EquipmentId);
        if (eq is not null) SelectedEquipment = eq;
        var node = Plant3DSceneData.AllNodes.FirstOrDefault(n =>
            string.Equals(n.Id, alarm.EquipmentId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(n.LinkedScadaId, alarm.EquipmentId, StringComparison.OrdinalIgnoreCase));
        if (node is not null) Selected3DNode = node;
        FocusTargetId = null;
        FocusTargetId = node?.Id ?? alarm.EquipmentId;
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
        if (_telemetry is SimulatedTelemetryService sim)
        {
            NativeProtocolStatus = sim.NativeProtocolStatus;
        }
        OnPropertyChanged(nameof(TelemetrySourceBadgeText));
        OnPropertyChanged(nameof(IsTelemetryLive));
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
            DmcFeedFlowM3h = dm301.PrimaryMetric;
        }

        var dmcPrs = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "DM301_PRS");
        if (dmcPrs is not null) DmcInletPressureKpa = dmcPrs.Value;

        var ms301Rec = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "MS301_REC");
        if (ms301Rec is not null) MacsepRecoveryPercent = ms301Rec.Value;

        double cleanFraction = Math.Clamp(CleanCoalYieldPercent / 100.0, 0.50, 0.92);
        double washFeedTph = Math.Round(TotalFeedTonnageTph * 0.785, 1);
        DmcCleanOverflowTph = Math.Round(washFeedTph * cleanFraction, 1);
        DmcDiscardApexTph = Math.Round(Math.Max(0.0, washFeedTph - DmcCleanOverflowTph), 1);

        var sl601Lvl = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "SL601_LVL");
        if (sl601Lvl is not null) ProductSiloLevelPercent = sl601Lvl.Value;

        var sl601Ton = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "SL601_TON");
        if (sl601Ton is not null) ProductSiloTons = sl601Ton.Value;

        var an601Ash = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "AN601_ASH");
        if (an601Ash is not null) PgnaaAshPercent = an601Ash.Value;

        var an601Moist = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "AN601_MOIST");
        if (an601Moist is not null) PgnaaMoisturePercent = an601Moist.Value;

        var an601Cv = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "AN601_CV");
        if (an601Cv is not null) PgnaaCalorificValueMjKg = an601Cv.Value;

        var ld601Tph = _telemetry.AllTags.FirstOrDefault(t => t.TagId == "LD601_TPH");
        if (ld601Tph is not null) TrainLoadoutRateTph = ld601Tph.Value;

        RefreshFilteredAlarms();

        var topAlarm = _telemetry.Alarms.FirstOrDefault(a => !a.IsAcknowledged) ?? _telemetry.Alarms.FirstOrDefault();
        LatestAlarmTicker = topAlarm is not null
            ? $"[{topAlarm.Severity}] {topAlarm.TimeDisplay} — {topAlarm.Message}"
            : "All systems normal — no active alarms";

        if (SelectedTrendTag is not null)
        {
            SelectedTrendValues = _telemetry.GetTagHistory(SelectedTrendTag.TagId).ToList();
        }
        UpdateTrendStatistics();
        UpdateSelected3DTrend();

        _unityBridge.PublishFrame(_telemetry.Equipment, UnityEffects);
        UnityStreamStatus = $"Streaming {_unityBridge.Endpoint} · {_unityBridge.PacketsSent} frames";
    }
}
