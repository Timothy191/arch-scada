using CommunityToolkit.Mvvm.ComponentModel;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ITelemetryService _telemetryService = new SimulatedTelemetryService();
    private readonly UnityBridgeService _unityBridgeService = new();

    [ObservableProperty]
    public partial ViewModelBase CurrentContent { get; set; }

    public MainViewModel()
    {
        // Splash screen has already run startup checks.
        // Proceed directly to the main shell.
        CurrentContent = new MainShellViewModel(_telemetryService, _unityBridgeService, offlineDemoMode: true);
    }
}
