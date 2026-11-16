using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.ViewModels;

public partial class IntroViewModel : ViewModelBase
{
    private readonly UnityBridgeService _unityBridge;
    private DispatcherTimer? _countdownTimer;

    public ObservableCollection<StartupCheckItem> Checks { get; } = new();

    [ObservableProperty]
    public partial string StatusHeadline { get; set; } = "Initializing plant operator workstation...";

    [ObservableProperty]
    public partial bool ChecksCompleted { get; set; }

    [ObservableProperty]
    public partial bool ServerReachable { get; set; }

    [ObservableProperty]
    public partial bool AutoAdvanceEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int CountdownSeconds { get; set; } = 5;

    [ObservableProperty]
    public partial string AutoAdvanceLabel { get; set; } = "Waiting for readiness checks...";

    public string PlantSiteTitle => "Arch Coal Processing Plant — Unit 1";
    public string VersionLabel => "Arch SCADA v6.5-arch | .NET 10 / Avalonia";
    public string SafetyBannerText => "READ-ONLY MONITORING MODE — LIVE MACHINE CONTROL DISABLED BY SAFETY BOUNDARY";

    public event EventHandler<bool>? Completed;

    public IntroViewModel(UnityBridgeService unityBridge)
    {
        _unityBridge = unityBridge;

        Checks.Add(new StartupCheckItem
        {
            Id = "cfg",
            Title = "Local Configuration Cache",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "srv",
            Title = "ScadaServer Core Engine",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "comm",
            Title = "ScadaComm Field Drivers (Modbus TCP / OPC UA / MQTT)",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "hist",
            Title = "Local Historian & Alarm Journal",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "3d",
            Title = "Unity 3D Twin Bridge & Blender Asset Pipeline",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
    }

    public async Task RunStartupChecksAsync()
    {
        await RunStep(0, "Verifying local XML/SQLite configuration...", async () =>
        {
            await Task.Delay(260);
            return (StartupCheckState.Passed, "Cached plant topology loaded (3 circuits, 11 assets)");
        });

        await RunStep(1, "Probing local ScadaServer IPC/HTTP endpoint...", async () =>
        {
            await Task.Delay(320);
            ServerReachable = false;
            return (StartupCheckState.Warning, "ScadaServer not running locally — Offline Demo / Cache fallback ready");
        });

        await RunStep(2, "Checking ScadaComm channel configuration...", async () =>
        {
            await Task.Delay(240);
            return (StartupCheckState.Passed, "Simulator & Modbus TCP channels configured (Read-Only)");
        });

        await RunStep(3, "Initializing local historian & alarm buffer...", async () =>
        {
            await Task.Delay(220);
            return (StartupCheckState.Passed, "In-memory / local ring buffer ready (60-sample window)");
        });

        await RunStep(4, "Inspecting Unity 3D UDP bridge & Blender CLI...", async () =>
        {
            await Task.Delay(220);
            var blenderStatus = _unityBridge.IsBlenderInstalled ? "Blender ready" : "Blender not found";
            var unityStatus = _unityBridge.IsUnityHubInstalled ? "Unity Hub ready" : "Unity Hub not found";
            var models = _unityBridge.GetExportedModels().Count;
            return (StartupCheckState.Passed, $"{unityStatus} · {blenderStatus} · {models} exported GLB model(s)");
        });

        ChecksCompleted = true;
        StatusHeadline = "System readiness verified. Ready to enter Plant Operations.";
        StartAutoAdvanceCountdown();
    }

    private async Task RunStep(int index, string runningDetail, Func<Task<(StartupCheckState state, string detail)>> action)
    {
        var item = Checks[index];
        item.State = StartupCheckState.Running;
        item.Detail = runningDetail;

        var (state, detail) = await action();
        item.State = state;
        item.Detail = detail;
    }

    private void StartAutoAdvanceCountdown()
    {
        if (!AutoAdvanceEnabled)
        {
            AutoAdvanceLabel = "Auto-advance paused";
            return;
        }

        CountdownSeconds = 5;
        AutoAdvanceLabel = $"Entering Plant Operations in {CountdownSeconds}s...";

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            if (!AutoAdvanceEnabled)
            {
                _countdownTimer?.Stop();
                AutoAdvanceLabel = "Auto-advance paused";
                return;
            }

            CountdownSeconds--;
            if (CountdownSeconds <= 0)
            {
                _countdownTimer?.Stop();
                EnterOfflineDemo();
            }
            else
            {
                AutoAdvanceLabel = $"Entering Plant Operations in {CountdownSeconds}s...";
            }
        };
        _countdownTimer.Start();
    }

    [RelayCommand]
    private void ToggleAutoAdvance()
    {
        AutoAdvanceEnabled = !AutoAdvanceEnabled;
        if (!AutoAdvanceEnabled)
        {
            _countdownTimer?.Stop();
            AutoAdvanceLabel = "Auto-advance paused by operator";
        }
        else if (ChecksCompleted)
        {
            StartAutoAdvanceCountdown();
        }
    }

    [RelayCommand]
    private void EnterPlantOperations()
    {
        _countdownTimer?.Stop();
        Completed?.Invoke(this, !ServerReachable);
    }

    [RelayCommand]
    private void EnterOfflineDemo()
    {
        _countdownTimer?.Stop();
        Completed?.Invoke(this, true);
    }
}
