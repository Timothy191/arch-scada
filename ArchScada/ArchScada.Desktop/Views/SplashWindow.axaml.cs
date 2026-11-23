using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ArchScada.Desktop.Services;
using ArchScada.Desktop.ViewModels;

namespace ArchScada.Desktop.Views;

public partial class SplashWindow : Window
{
    private readonly SplashViewModel _viewModel;

    public SplashWindow()
    {
        InitializeComponent();
        _viewModel = new SplashViewModel(new UnityBridgeService());
        DataContext = _viewModel;
        _ = RunSplashAsync();
    }

    private async System.Threading.Tasks.Task RunSplashAsync()
    {
        await _viewModel.RunStartupSequenceAsync();

        // Signal the app to transition to the main window
        // The App class listens for this via the Completed event
        _viewModel.Completed += (_, _) =>
        {
            // Close the splash window; App will show MainWindow
            Close();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Notify the application that splash is done
        if (Application.Current is App app)
        {
            app.OnSplashCompleted();
        }
    }
}