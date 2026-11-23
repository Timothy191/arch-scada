using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ArchScada.Desktop.ViewModels;
using ArchScada.Desktop.Views;

namespace ArchScada.Desktop;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SplashWindow? _splashWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Show splash window first
            _splashWindow = new SplashWindow();
            _splashWindow.Show();

            // Create main window but keep it hidden until splash completes
            _mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };

            // When the main window's ViewModel signals readiness (after intro),
            // we can show it. But since splash now handles checks, MainViewModel
            // will go directly to MainShellViewModel.
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void OnSplashCompleted()
    {
        // Transition from splash to main window
        _splashWindow?.Close();
        _splashWindow = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _mainWindow;
            _mainWindow?.Show();
        }
    }
}