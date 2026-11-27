using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArchScada.Desktop.Controls
{
    /// <summary>
    /// Procedural "motion-graphics" intro animation rendered live with the Avalonia
    /// drawing engine — no video codec, no binary asset, fully themeable and DPI-aware.
    /// </summary>
    /// <remarks>
    /// 8-Second Cinematic Sequence per §4 VISUAL_STYLE_GUIDE:
    ///   0.0 - 2.0s  : Coal-plant silhouette build-up (vertex-by-vertex reveal, smoothstep)
    ///   2.0 - 4.0s  : 180°/s scan sweep left-to-right with optional particle sparks
    ///   4.0 - 6.0s  : "ARCH SCADA" wordmark reveal (Inter 700 24px, cubic-bezier ease-out,
    ///                   20px→0 horizontal offset tracking)
    ///   6.0 - 8.0s  : Hold + smooth fade to final frame
    /// </remarks>
    public sealed class IntroAnimationControl : Control
    {
        private static readonly Color Accent = Color.Parse("#F59E0B");
        private static readonly Color AccentDim = Color.Parse("#B45309");
        private static readonly Color Silhouette = Color.Parse("#453517");
        private static readonly Color SilhouetteEdge = Color.Parse("#D97706");
        private static readonly Color TextColor = Color.Parse("#FAFAFA");

        // Particle system constants
        private const int MaxParticles = 25;
        private Particle[] _particles = Array.Empty<Particle>();
        private double _particleAccumulator;

        private readonly DispatcherTimer _timer;
        private double _progress;
        private bool _playing;
        private bool _reducedMotion;
        private bool _automatedCapture;

        public IntroAnimationControl()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Advance();

            // Never keep the dispatcher timer alive after the control leaves the tree
            DetachedFromVisualTree += (_, _) => Stop();
        }

        /// <summary>Total intro duration in milliseconds.</summary>
        public double DurationMs { get; set; } = 8000;

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
                    _automatedCapture = false;
                    InvalidateVisual();
                }
            }
        }

        /// <summary>Current normalized playback position (0..1).</summary>
        public double Progress => _progress;

        public bool IsPlaying => _playing;

        /// <summary>Automated capture mode — suppresses timing-dependent effects for deterministic screenshots.</summary>
        public bool AutomatedCaptureMode
        {
            get => _automatedCapture;
            set
            {
                _automatedCapture = value;
                InvalidateVisual();
            }
        }

        /// <summary>Begin (or restart) the intro from the first frame.</summary>
        public void Play()
        {
            if (_reducedMotion)
            {
                _progress = 1.0;
                _automatedCapture = false;
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

        /// <summary>Jump to the final frame immediately (reduced motion / automated capture pattern).</summary>
        public void JumpToFinalFrame()
        {
            _progress = 1.0;
            _automatedCapture = false;
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

        // ---- Timeline phasing per §4 ----
        // 0.00-0.25 : silhouette build (0-2s)
        // 0.25-0.50 : scan sweep (2-4s)
        // 0.50-0.75 : title reveal (4-6s)
        // 0.75-1.00 : hold + fade (6-8s)

        private double GetTimelinePhase(double normalizedProgress)
        {
            if (normalizedProgress <= 0.25)
                return GetSilhouettePhase(normalizedProgress / 0.25);
            if (normalizedProgress <= 0.50)
                return GetScanPhase((normalizedProgress - 0.25) / 0.25);
            if (normalizedProgress <= 0.75)
                return GetTitlePhase((normalizedProgress - 0.50) / 0.25);
            return GetFadePhase((normalizedProgress - 0.75) / 0.25);
        }

        private static double CubicBezierEase(double t) =>
            (1 - Math.Pow(1 - t, 3)) * 0.4 + Math.Pow(t, 3) * 1.0;

        // ---- Silhouette build: vertex-by-vertex reveal with smoothstep ----
        private double GetSilhouettePhase(double t)
        {
            var smooth = t * t * (3.0 - 2.0 * t);
            return smooth;
        }

        // ---- Scan sweep: 180°/s scan line with optional particle sparks ----
        private double GetScanPhase(double t)
        {
            var eased = t * t * (3.0 - 2.0 * t);
            eased = 0.4 * eased * eased + 0.2 * eased * eased * eased;
            return Math.Clamp(eased, 0.0, 1.0);
        }

        // ---- Title reveal: Inter 700 24px, ease-out cubic-bezier, 20px→0 offset ----
        private double GetTitlePhase(double t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            var eased = (-0.6 * t3) + (1.6 * t2) + (-1.2 * t) + 1.0;
            eased = Math.Clamp(eased, 0.0, 1.0);
            return eased;
        }

        // ---- Hold + fade (6-8s) ----
        private double GetFadePhase(double t)
        {
            var eased = 1.0 - Math.Pow(1.0 - t, 3.0);
            eased = eased * 0.4 + (1.0 - eased) * 0.6;
            return Math.Clamp(eased, 0.0, 1.0);
        }

        // ---- Particle system ----

        private struct Particle
        {
            public Point Position;
            public Point Velocity;
            public double Life; // 0.0 = dead, 1.0 = alive
            public double TTL;  // total lifetime
            public Color Color;
            public double Size;
        }

        private void EmitParticles(Point source, Color color, double quantity)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].Life > 0.0) continue;

                var angle = new Random().NextDouble() * Math.PI * 2;
                var speed = 40.0 + new Random().NextDouble() * 60.0;
                _particles[i] = new Particle
                {
                    Position = source,
                    Velocity = new Point(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
                    TTL = 0.6 + new Random().NextDouble() * 0.4,
                    Life = 0.0,
                    Color = color,
                    Size = 2.0 + new Random().NextDouble() * 3.0
                };
                break;
            }
        }

        private void UpdateParticles(double deltaTime)
        {
            _particleAccumulator += deltaTime;
            if (_particleAccumulator < 0.05) return;
            _particleAccumulator -= 0.05;

            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].Life <= 0.0) continue;

                _particles[i].Life += deltaTime / _particles[i].TTL;
                _particles[i].Position += _particles[i].Velocity * deltaTime;

                if (_particles[i].Life > 0.8)
                    _particles[i].Color = new Color(
                        _particles[i].Color.R,
                        _particles[i].Color.G,
                        _particles[i].Color.B,
                        (byte)Math.Clamp((1.0 - (_particles[i].Life - 0.8) * 5.0) * 255.0, 0.0, 255.0));

                if (_particles[i].Life > 1.0 || _particles[i].Position.Y > 1000)
                {
                    _particles[i] = new Particle { Life = -1.0 };
                }
            }
        }

        private void DrawParticles(DrawingContext context)
        {
            if (_particles == null) return;
            foreach (var p in _particles)
            {
                if (p.Life <= 0.0 || p.Life > 1.0) continue;
                var alpha = 1.0 - (1.0 - p.Life) * 0.5;
                var brush = new SolidColorBrush(p.Color, alpha * 0.8);
                context.DrawEllipse(brush, null, new Rect(p.Position.X - p.Size, p.Position.Y - p.Size, p.Size * 2, p.Size * 2));
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w <= 20 || h <= 20) return;

            var timeline = GetTimelinePhase(_progress);

            // Background ground line
            var groundY = h * 0.82;
            var sceneLeft = w * 0.10;
            var sceneRight = w * 0.90;
            var sceneW = sceneRight - sceneLeft;

            context.DrawLine(
                new Pen(new SolidColorBrush(AccentDim, 0.35), 1.0),
                new Point(sceneLeft, groundY),
                new Point(sceneRight, groundY));

            // --- Phase 1: Silhouette build (0-2s) ----
            if (_progress <= 0.25 || _automatedCapture)
            {
                DrawPlantSilhouette(context, sceneLeft, sceneW, groundY, h, timeline);
            }

            // --- Phase 2: Scan sweep (2-4s) ----
            if (_progress > 0.25 && _progress <= 0.50 || _automatedCapture)
            {
                var sweep = GetScanPhase(_progress);
                if (sweep > 0.0 && sweep < 1.0)
                {
                    DrawScanSweep(context, sceneLeft, sceneRight, groundY, h, sweep);
                }
            }

            // --- Phase 3: Title reveal (4-6s) ----
            if (_progress > 0.50 && _progress <= 0.75 || _automatedCapture)
            {
                var title = GetTitlePhase(_progress);
                if (title > 0.0)
                    DrawTitle(context, w, h, title);
            }

            // --- Phase 4: Hold + fade (6-8s) ----
            if (_progress > 0.75 || _automatedCapture)
            {
                var fade = GetFadePhase(_progress);
                using (context.PushOpacity(Math.Clamp(1.0 - fade, 0.0, 1.0)))
                {
                    DrawPlantSilhouette(context, sceneLeft, sceneW, groundY, h, 1.0);
                    DrawScanSweep(context, sceneLeft, sceneRight, groundY, h, 1.0);
                    DrawTitle(context, w, h, 1.0);
                    DrawParticles(context);
                }
            }
            else
            {
                DrawPlantSilhouette(context, sceneLeft, sceneW, groundY, h, timeline);
                DrawScanSweep(context, sceneLeft, sceneRight, groundY, h, timeline > 0.0 && timeline < 1.0 ? timeline : 0.0);
                var title = timeline > 0.0 ? GetTitlePhase(timeline) : 0.0;
                if (title > 0.0)
                    DrawTitle(context, w, h, title);
            }
        }

        // ---- Silhouette draw (vertex-by-vertex reveal) ----
        private void DrawPlantSilhouette(
            DrawingContext context, double left, double sceneW, double groundY, double h, double build)
        {
            if (build <= 0.0) return;

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
            if (height <= 0.5) return;
            context.DrawRectangle(fill, edge, new Rect(x, topY, width, height), 3.0, 3.0);
        }

        // ---- Scan sweep ----
        private void DrawScanSweep(
            DrawingContext context, double sceneLeft, double sceneRight, double groundY, double h, double sweep)
        {
            var x = sceneLeft + (sceneRight - sceneLeft) * sweep;

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
            context.DrawRectangle(grad, null, new Rect(x - 26, h * 0.10, 26, groundY - h * 0.10));
            context.DrawLine(
                new Pen(new SolidColorBrush(Accent, 0.9), 1.5),
                new Point(x, h * 0.10),
                new Point(x, groundY));

            if (sweep > 0.3 && sweep < 0.7)
            {
                for (int i = 0; i < 2; i++)
                {
                    var sparkX = x + (new Random().NextDouble() - 0.5) * 8.0;
                    var sparkY = h * 0.12 + (sweep - 0.3) / 0.4 * 10.0;
                    var sparkColor = new Color(0xFF, 0xD7, 0x00, 0x64);
                    context.DrawLine(
                        new Pen(new SolidColorBrush(sparkColor, 0.7), 1.2),
                        new Point(sparkX, sparkY),
                        new Point(sparkX + new Random().NextDouble() * 6 - 3, sparkY + 4));
                }
            }
        }

        // ---- Title reveal ----
        private void DrawTitle(DrawingContext context, double w, double h, double title)
        {
            var fontSize = Math.Clamp(w * 0.052, 16.0, 30.0);
            var typeface = new Typeface("Inter");

            if (typeface.FontFamily.Name == "Inter" || typeface.FontFamily.Name == "")
            {
                typeface = new Typeface(new FontFamily("Segoe UI"));
            }

            var tracking = (1.0 - title) * fontSize * 0.35;
            const string text = "ARCH SCADA";
            var brush = new SolidColorBrush(TextColor, Math.Clamp(title * 1.2, 0.0, 1.0));
            var pen = new Pen(new SolidColorBrush(Accent, 0.8 * title), 1.0);

            double cx = w * 0.5;
            double y = h * 0.92;

            double totalWidth = MeasureTracked(context, text, typeface, fontSize, tracking);
            double offset = 20.0 * (1.0 - title);
            double cursor = cx - totalWidth / 2.0 + offset;

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
}
