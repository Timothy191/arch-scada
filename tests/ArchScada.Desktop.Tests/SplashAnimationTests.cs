using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArchScada.Desktop.Controls;
using Xunit;

namespace ArchScada.Desktop.Tests;

/// <summary>
/// Verifies the splash-screen animation resources are present and well-formed.
/// These tests are intentionally headless (no Avalonia UI thread / asset loader):
/// they parse the XAML source files directly so they run in plain CI.
/// </summary>
public sealed class SplashAnimationTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Plant.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    private static string ReadDesktopFile(string relativePath)
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(root, "ArchScada", "ArchScada.Desktop", relativePath);
        Assert.True(File.Exists(path), $"Expected desktop file not found: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void StylesDefineSplashAnimations()
    {
        var xaml = ReadDesktopFile(Path.Combine("Styles", "MacOSLightStyles.axaml"));
        var doc = XDocument.Parse(xaml);

        // Every splash animation style must declare at least one <Animation>.
        string[] animatedSelectors =
        {
            "Border.splash-brand-badge",
            "Ellipse.splash-core-dot",
            "Arc.splash-processing-arc",
            "Arc.splash-brand-ring",
            "Ellipse.splash-status-dot",
            "Button.splash-launch-ready",
        };

        foreach (var selector in animatedSelectors)
        {
            var style = doc.Descendants()
                .Where(e => (string?)e.Attribute("Selector") == selector)
                .FirstOrDefault();
            Assert.NotNull(style);

            var animation = style!.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Animation");
            Assert.NotNull(animation);
            Assert.NotNull(animation!.Attribute("Duration"));
            Assert.NotNull(animation.Attribute("IterationCount"));

            // Each animation must contain keyframes with cues.
            var keyframes = animation.Descendants()
                .Where(e => e.Name.LocalName == "KeyFrame")
                .ToList();
            Assert.NotEmpty(keyframes);
            Assert.All(keyframes, kf => Assert.NotNull(kf.Attribute("Cue")));
        }
    }

    [Fact]
    public void StylesDefineProgressAndRowTransitions()
    {
        var xaml = ReadDesktopFile(Path.Combine("Styles", "MacOSLightStyles.axaml"));
        var doc = XDocument.Parse(xaml);

        foreach (var selector in new[] { "Border.splash-progress-fill", "Border.splash-check-row" })
        {
            var style = doc.Descendants()
                .Where(e => (string?)e.Attribute("Selector") == selector)
                .FirstOrDefault();
            Assert.NotNull(style);

            var transitions = style!.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Transitions");
            Assert.NotNull(transitions);

            var transition = transitions!.Elements()
                .FirstOrDefault(e => e.Name.LocalName.EndsWith("Transition", StringComparison.Ordinal));
            Assert.NotNull(transition);
            Assert.NotNull(transition!.Attribute("Property"));
            Assert.NotNull(transition.Attribute("Duration"));
        }
    }

    [Fact]
    public void StylesHonorReducedMotionPreference()
    {
        var xaml = ReadDesktopFile(Path.Combine("Styles", "MacOSLightStyles.axaml"));
        var doc = XDocument.Parse(xaml);

        // A reduced-motion selector must exist that targets the looping animations.
        var reducedMotion = doc.Descendants()
            .Where(e => (string?)e.Attribute("Selector") is string s &&
                        s.Contains("splash-reduced-motion", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(reducedMotion);

        // The looping launch pulse and shimmer must be neutralized under reduced motion.
        Assert.Contains("splash-launch-ready", xaml);
        Assert.Contains("splash-status-dot", xaml);
    }

    [Fact]
    public void SplashViewUsesAnimatedElements()
    {
        var xaml = ReadDesktopFile(Path.Combine("Views", "SplashView.axaml"));

        // Animated class hooks must be wired into the view.
        Assert.Contains("splash-brand-badge", xaml);
        Assert.Contains("splash-core-dot", xaml);
        Assert.Contains("splash-brand-ring", xaml);
        Assert.Contains("splash-processing-arc", xaml);
        Assert.Contains("splash-progress-fill", xaml);
        Assert.Contains("splash-check-row", xaml);
        Assert.Contains("splash-status-dot", xaml);
        Assert.Contains("splash-launch-ready", xaml);

        // Processing arc is gated on the running state.
        Assert.Contains("IsVisible=\"{Binding IsRunning}\"", xaml);

        // Launch attention pulse is gated on completion.
        Assert.Contains("Classes.splash-launch-ready=\"{Binding IsComplete}\"", xaml);

        // View must be well-formed XAML.
        XDocument.Parse(xaml);
    }

    [Fact]
    public void SplashWindowSupportsAutomatedCaptureAndReducedMotion()
    {
        var code = ReadDesktopFile(Path.Combine("Views", "SplashWindow.axaml.cs"));

        // The fade-in must be suppressed for deterministic automated capture.
        Assert.Contains("IsAutomatedCaptureMode", code);
        Assert.Contains("splash-reduced-motion", code);
        Assert.Contains("PlayFadeIn", code);
    }

    [Fact]
    public void SplashViewHostsProceduralIntro()
    {
        var xaml = ReadDesktopFile(Path.Combine("Views", "SplashView.axaml"));

        // The procedural motion-graphics intro must be hosted and namespaced.
        Assert.Contains("xmlns:controls=\"using:ArchScada.Desktop.Controls\"", xaml);
        Assert.Contains("controls:IntroAnimationControl", xaml);
        Assert.Contains("x:Name=\"IntroAnimation\"", xaml);

        // View must remain well-formed XAML.
        XDocument.Parse(xaml);
    }

    [Fact]
    public void IntroControl_Seek_ClampsAndHoldsDeterministicFrame()
    {
        var intro = new IntroAnimationControl();

        intro.Seek(0.5);
        Assert.Equal(0.5, intro.Progress, 3);
        Assert.False(intro.IsPlaying);

        // Out-of-range seeks clamp to [0,1].
        intro.Seek(-1.0);
        Assert.Equal(0.0, intro.Progress, 3);
        intro.Seek(2.0);
        Assert.Equal(1.0, intro.Progress, 3);
    }

    [Fact]
    public void IntroControl_ReducedMotion_JumpsToFinalFrame()
    {
        var intro = new IntroAnimationControl
        {
            ReducedMotion = true
        };

        // Reduced motion holds the composed final frame without animating.
        Assert.Equal(1.0, intro.Progress, 3);
        Assert.False(intro.IsPlaying);

        // Play() under reduced motion stays on the final frame (no timer start).
        intro.Play();
        Assert.Equal(1.0, intro.Progress, 3);
        Assert.False(intro.IsPlaying);
    }
}