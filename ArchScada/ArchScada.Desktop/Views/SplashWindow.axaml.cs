using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using ArchScada.Desktop.Services;
using ArchScada.Desktop.ViewModels;

namespace ArchScada.Desktop.Views
{
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

            // Honor accessibility preferences: when the platform reports a reduced-motion
            // or high-contrast setting, add a class that disables looping decorative
            // animations (see MacOSLightStyles.axaml "splash-reduced-motion" selectors).
            if (IsReducedMotionRequested())
            {
                Classes.Add("splash-reduced-motion");
            }

            _viewModel.Completed += OnViewModelCompleted;
            KeyDown += OnSplashKeyDown;

            PlayFadeIn();

            if (autoStart)
            {
                _ = _viewModel.RunStartupSequenceAsync();
            }
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

                // High-contrast themes are a strong signal the user wants minimal motion.
                return settings.GetColorValues().ContrastPreference
                    != Avalonia.Platform.ColorContrastPreference.NoPreference;
            }
            catch
            {
                return false;
            }
        }

        private void PlayFadeIn()
        {
            // The automated capture path renders a single deterministic frame, so it
            // must not depend on a running animation clock. Skip the fade there.
            if (IsAutomatedCaptureMode || Classes.Contains("splash-reduced-motion"))
            {
                // Jump immediately to final frame for deterministic capture
                Opacity = 1.0;
                return;
            }

            Opacity = 0;
            var fadeIn = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(420),
                FillMode = FillMode.Forward,
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0.0),
                        Setters = { new Setter(Visual.OpacityProperty, 0.0) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters = { new Setter(Visual.OpacityProperty, 1.0) }
                    }
                }
            };

            _ = fadeIn.RunAsync(this);
        }

        /// <summary>
        /// Set by <see cref="App"/> when the splash is being driven by the
        /// <c>--capture-splash</c> automation hook, so decorative motion is suppressed
        /// for deterministic screenshot output.
        /// </summary>
        public bool IsAutomatedCaptureMode { get; set; }

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
}
