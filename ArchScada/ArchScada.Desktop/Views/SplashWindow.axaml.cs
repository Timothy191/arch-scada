using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ArchScada.Desktop.Services;
using ArchScada.Desktop.ViewModels;

namespace ArchScada.Desktop.Views;

public partial class SplashWindow : Window
{
    private readonly SplashViewModel _viewModel;

    public SplashViewModel ViewModel => _viewModel;

    public SplashWindow()
        : this(new SplashViewModel(new UnityBridgeService()), autoStart: true)
    {
    }

    public SplashWindow(SplashViewModel viewModel, bool autoStart = true)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.Completed += OnViewModelCompleted;
        KeyDown += OnSplashKeyDown;

        if (autoStart)
        {
            _ = _viewModel.RunStartupSequenceAsync();
        }
    }

    private void OnViewModelCompleted(object? sender, bool e)
    {
        Close();
    }

    private void OnSplashKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.SkipSplashCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _viewModel.IsComplete)
        {
            _viewModel.ContinueToAppCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.R && _viewModel.IsComplete)
        {
            _viewModel.RerunChecksCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Completed -= OnViewModelCompleted;
        KeyDown -= OnSplashKeyDown;
        base.OnClosed(e);

        if (Application.Current is App app)
        {
            app.OnSplashCompleted();
        }
    }
}
