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
        var intro = new IntroViewModel(_unityBridgeService);
        intro.Completed += (_, offlineMode) =>
        {
            CurrentContent = new MainShellViewModel(_telemetryService, _unityBridgeService, offlineMode);
        };

        CurrentContent = intro;
        _ = intro.RunStartupChecksAsync();
    }
}
