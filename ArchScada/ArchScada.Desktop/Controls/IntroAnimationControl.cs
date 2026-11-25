using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArchScada.Desktop.Controls;

/// <summary>
/// Procedural "motion-graphics" intro animation rendered live with the Avalonia
/// drawing engine — no video codec, no binary asset, fully themeable and DPI-aware.
///
/// Sequence (a short branded title card for the CHPP HMI):
///   1. A coal-plant silhouette (crusher, screen, thickener, stack, conveyor)
///      "builds up" from the ground with a staggered rise + fade.
///   2. A cyan scan sweep travels left-to-right across the scene.
///   3. The "ARCH SCADA" wordmark reveals with a tracking (letter-spacing) ease.
///
/// Playback is driven by a DispatcherTimer. Under reduced motion, Play() jumps to
/// the final held frame. Call Seek() for deterministic automated capture.
/// </summary>
public sealed class IntroAnimationControl : Control
{
    private static readonly Color Accent = Color.Parse("#38BDF8");
    private static readonly Color AccentDim = Color.Parse("#0284C7");
    private static readonly Color Silhouette = Color.Parse("#1E3A5F");
    private static readonly Color SilhouetteEdge = Color.Parse("#0EA5E9");
    private static readonly Color TextColor = Color.Parse("#F8FAFC");

    private readonly DispatcherTimer _timer;
    private double _progress;
    private bool _playing;
    private bool _reducedMotion;

    public IntroAnimationControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 fps
        _timer.Tick += (_, _) => Advance();

        // Never keep the dispatcher timer alive after the control leaves the tree
        // (e.g. the splash window closing mid-intro); otherwise Advance() would
        // invalidate a detached visual.
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>Total intro duration in milliseconds.</summary>
    public double DurationMs { get; set; } = 2600;

    /// <summary>When true, motion is suppressed and the final frame is shown.</summary>
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            _reducedMotion = value;
            if (value)
            {
                Stop();
                _progress = 1.0;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Current normalized playback position (0..1).</summary>
    public double Progress => _progress;

    public bool IsPlaying => _playing;

    /// <summary>Begin (or restart) the intro from the first frame.</summary>
    public void Play()
    {
        if (_reducedMotion)
        {
            _progress = 1.0;
            InvalidateVisual();
            return;
        }
        _progress = 0.0;
        _playing = true;
        _timer.Start();
        InvalidateVisual();
    }

    /// <summary>Stop playback and hold the current frame.</summary>
    public void Stop()
    {
        _playing = false;
        _timer.Stop();
    }

    /// <summary>Seek to a normalized position (0..1) and render it.</summary>
    public void Seek(double progress)
    {
        _progress = Math.Clamp(progress, 0.0, 1.0);
        Stop();
        InvalidateVisual();
    }

    private void Advance()
    {
        var step = 33.0 / Math.Max(1.0, DurationMs);
        _progress += step;
        if (_progress >= 1.0)
        {
            _progress = 1.0;
            Stop();
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 20 || h <= 20)
        {
            return;
        }

        const double buildStart = 0.00, buildEnd = 0.62;   // silhouette rises
        const double sweepStart = 0.30, sweepEnd = 0.78;   // scan sweep crosses
        const double titleStart = 0.55, titleEnd = 0.95;   // wordmark reveals

        var groundY = h * 0.82;
        var sceneLeft = w * 0.10;
        var sceneRight = w * 0.90;
        var sceneW = sceneRight - sceneLeft;

        context.DrawLine(
            new Pen(new SolidColorBrush(AccentDim, 0.35), 1.0),
            new Point(sceneLeft, groundY),
            new Point(sceneRight, groundY));

        DrawPlantSilhouette(context, sceneLeft, sceneW, groundY, h,
            Phase(_progress, buildStart, buildEnd));

        var sweep = Phase(_progress, sweepStart, sweepEnd);
        if (sweep > 0.0 && sweep < 1.0)
        {
            DrawScanSweep(context, sceneLeft, sceneRight, groundY, h, sweep);
        }

        var title = Phase(_progress, titleStart, titleEnd);
        if (title > 0.0)
        {
            DrawTitle(context, w, h, title);
        }
    }

    /// <summary>Ease a sub-phase into 0..1 with a smoothstep curve.</summary>
    private static double Phase(double p, double start, double end)
    {
        if (end <= start)
        {
            return p >= end ? 1.0 : 0.0;
        }
        var t = Math.Clamp((p - start) / (end - start), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private void DrawPlantSilhouette(
        DrawingContext context, double left, double sceneW, double groundY, double h, double build)
    {
        if (build <= 0.0)
        {
            return;
        }

        var fill = new SolidColorBrush(Silhouette, 0.55 * build);
        var edge = new Pen(new SolidColorBrush(SilhouetteEdge, 0.85 * build), 1.2);

        double Rise(double stagger) => groundY - (groundY - h * 0.18) * Clamp01(build * 1.25 - stagger);

        DrawRiseRect(context, left + sceneW * 0.02, sceneW * 0.16, groundY, groundY - Rise(0.00), fill, edge);
        DrawRiseRect(context, left + sceneW * 0.26, sceneW * 0.14, groundY, groundY - Rise(0.08), fill, edge);
        DrawRiseRect(context, left + sceneW * 0.50, sceneW * 0.16, groundY, groundY - Rise(0.16), fill, edge);
        DrawRiseRect(context, left + sceneW * 0.78, sceneW * 0.05, groundY, groundY - Rise(0.24), fill, edge);

        var convTop = groundY - Rise(0.12) - (groundY - h * 0.18) * 0.10;
        var convLeft = new Point(left + sceneW * 0.18, groundY - (groundY - convTop) * 0.15);
        var convRight = new Point(left + sceneW * 0.26, convTop);
        context.DrawLine(new Pen(new SolidColorBrush(SilhouetteEdge, 0.7 * build), 2.0), convLeft, convRight);

        if (build > 0.6)
        {
            var dotBrush = new SolidColorBrush(Accent, 0.9 * build);
            for (int i = 0; i < 3; i++)
            {
                double t = (_progress * 3.0 + i * 0.33) % 1.0;
                var pt = Interpolate(convLeft, convRight, t);
                context.DrawEllipse(dotBrush, new Pen(dotBrush), new Rect(pt.X - 1.6, pt.Y - 1.6, 3.2, 3.2));
            }
        }
    }

    private static void DrawRiseRect(
        DrawingContext context, double x, double width, double groundY, double topY, IBrush fill, Pen edge)
    {
        var height = groundY - topY;
        if (height <= 0.5)
        {
            return;
        }
        context.DrawRectangle(fill, edge, new Rect(x, topY, width, height), 3.0, 3.0);
    }

    private void DrawScanSweep(
        DrawingContext context, double sceneLeft, double sceneRight, double groundY, double h, double sweep)
    {
        var x = sceneLeft + (sceneRight - sceneLeft) * sweep;
        var top = h * 0.10;

        var grad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(new Color(0x00, 0x38, 0xBD, 0xF8), 0.0),
                new GradientStop(new Color(0xCC, 0x38, 0xBD, 0xF8), 1.0),
            }
        };
        context.DrawRectangle(grad, null, new Rect(x - 26, top, 26, groundY - top));
        context.DrawLine(
            new Pen(new SolidColorBrush(Accent, 0.9), 1.5),
            new Point(x, top),
            new Point(x, groundY));
    }

    private void DrawTitle(DrawingContext context, double w, double h, double title)
    {
        var fontSize = Math.Clamp(w * 0.052, 16.0, 30.0);
        var typeface = new Typeface("Inter");

        var tracking = (1.0 - title) * fontSize * 0.35; // tracking expands as it reveals
        const string text = "ARCH SCADA";
        var brush = new SolidColorBrush(TextColor, Math.Clamp(title * 1.2, 0.0, 1.0));
        var pen = new Pen(new SolidColorBrush(Accent, 0.8 * title), 1.0);

        double cx = w * 0.5;
        double y = h * 0.92;

        double totalWidth = MeasureTracked(context, text, typeface, fontSize, tracking);
        double cursor = cx - totalWidth / 2.0;
        foreach (var ch in text)
        {
            var glyph = new FormattedText(
                ch.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                brush);
            context.DrawText(glyph, new Point(cursor, y - fontSize));
            cursor += glyph.Width + tracking;
        }

        var lineW = totalWidth * title;
        context.DrawLine(pen, new Point(cx - lineW / 2.0, y + 2), new Point(cx + lineW / 2.0, y + 2));
    }

    private static double MeasureTracked(
        DrawingContext context, string text, Typeface typeface, double fontSize, double tracking)
    {
        double width = 0;
        foreach (var ch in text)
        {
            var glyph = new FormattedText(
                ch.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White);
            width += glyph.Width + tracking;
        }
        return width - tracking;
    }

    private static Point Interpolate(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}