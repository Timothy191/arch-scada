using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ArchScada.Desktop.Controls;

namespace ArchScada.Desktop.Views;

public partial class SplashView : UserControl
{
    private IntroAnimationControl? _intro;

    public SplashView()
    {
        InitializeComponent();
        _intro = this.FindControl<IntroAnimationControl>("IntroAnimation");

        // Kick off the procedural intro once the view is attached and laid out.
        Loaded += (_, _) => StartIntro();
    }

    private void StartIntro()
    {
        if (_intro is null)
        {
            return;
        }

        // In automated capture the splash is rendered as a single deterministic
        // frame, so jump the intro to its fully-composed final frame.
        if (TopLevel.GetTopLevel(this) is SplashWindow { IsAutomatedCaptureMode: true })
        {
            _intro.Seek(1.0);
            return;
        }

        // Honor reduced-motion / high-contrast: jump straight to the held final frame.
        if (IsReducedMotionRequested())
        {
            _intro.ReducedMotion = true;
            return;
        }

        _intro.Play();
    }

    private static bool IsReducedMotionRequested()
    {
        try
        {
            var settings = Application.Current?.PlatformSettings;
            if (settings is null)
            {
                return false;
            }
            return settings.GetColorValues().ContrastPreference
                != Avalonia.Platform.ColorContrastPreference.NoPreference;
        }
        catch
        {
            return false;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}