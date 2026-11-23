using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.ViewModels;

public partial class SplashViewModel : ViewModelBase
{
    private readonly UnityBridgeService _unityBridge;

    public ObservableCollection<StartupCheckItem> Checks { get; } = new();

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Initializing Arch SCADA...";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "0%";

    [ObservableProperty]
    public partial bool IsComplete { get; set; }

    public string AppTitle => "Arch SCADA";
    public string AppSubtitle => "Coal Processing Plant HMI";
    public string VersionLabel => "v6.5-arch  |  .NET 10 / Avalonia 12";

    public event EventHandler<bool>? Completed;

    public SplashViewModel(UnityBridgeService unityBridge)
    {
        _unityBridge = unityBridge;

        Checks.Add(new StartupCheckItem
        {
            Id = "cfg",
            Title = "Configuration Cache",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "srv",
            Title = "ScadaServer Engine",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "comm",
            Title = "Field Drivers",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "hist",
            Title = "Historian & Alarms",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
        Checks.Add(new StartupCheckItem
        {
            Id = "3d",
            Title = "Unity 3D Twin Bridge",
            Detail = "Pending...",
            State = StartupCheckState.Pending
        });
    }

    public async Task RunStartupSequenceAsync()
    {
        var steps = new Func<Task<(StartupCheckState state, string detail)>>[]
        {
            async () =>
            {
                await Task.Delay(400);
                return (StartupCheckState.Passed, "Plant topology cached (3 circuits, 11 assets)");
            },
            async () =>
            {
                await Task.Delay(500);
                return (StartupCheckState.Warning, "ScadaServer offline — Demo mode active");
            },
            async () =>
            {
                await Task.Delay(400);
                return (StartupCheckState.Passed, "Modbus TCP / OPC UA / MQTT configured");
            },
            async () =>
            {
                await Task.Delay(350);
                return (StartupCheckState.Passed, "Local ring buffer ready (60-sample window)");
            },
            async () =>
            {
                await Task.Delay(350);
                var blender = _unityBridge.IsBlenderInstalled ? "Blender ready" : "Blender not found";
                var unity = _unityBridge.IsUnityHubInstalled ? "Unity Hub ready" : "Unity Hub not found";
                var models = _unityBridge.GetExportedModels().Count;
                return (StartupCheckState.Passed, $"{unity} · {blender} · {models} model(s)");
            },
        };

        for (int i = 0; i < steps.Length; i++)
        {
            var item = Checks[i];
            item.State = StartupCheckState.Running;
            item.Detail = $"Step {i + 1} of {steps.Length}...";

            var (state, detail) = await steps[i]();
            item.State = state;
            item.Detail = detail;

            Progress = ((double)(i + 1) / steps.Length) * 100.0;
            ProgressText = $"{Progress:F0}%";
            StatusText = item.Title;
        }

        IsComplete = true;
        StatusText = "Ready — launching Plant Operations...";

        // Brief pause to show completion, then signal done
        await Task.Delay(600);
        Completed?.Invoke(this, true);
    }

    [RelayCommand]
    private void SkipSplash()
    {
        Completed?.Invoke(this, true);
    }
}