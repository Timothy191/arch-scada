using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Controls;

/// <summary>
/// Interactive 3D perspective viewport rendering the coal processing plant,
/// with mouse orbit/zoom, shaded 3D meshes, animated conveyors/rake,
/// material flow particles, thermal load heatmap, and alarm halos.
/// </summary>
public sealed class Plant3DViewportControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<EquipmentItem>?> EquipmentProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, IReadOnlyList<EquipmentItem>?>(nameof(Equipment));

    public static readonly StyledProperty<Unity3DEffectSettings?> EffectsProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, Unity3DEffectSettings?>(nameof(Effects));

    public static readonly StyledProperty<string> CameraPresetProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, string>(nameof(CameraPreset), "Plant Overview");

    public IReadOnlyList<EquipmentItem>? Equipment
    {
        get => GetValue(EquipmentProperty);
        set => SetValue(EquipmentProperty, value);
    }

    public Unity3DEffectSettings? Effects
    {
        get => GetValue(EffectsProperty);
        set => SetValue(EffectsProperty, value);
    }

    public string CameraPreset
    {
        get => GetValue(CameraPresetProperty);
        set => SetValue(CameraPresetProperty, value);
    }

    private readonly DispatcherTimer _animTimer;
    private double _time;
    private double _yaw = -0.62;
    private double _pitch = 0.52;
    private double _zoom = 1.05;
    private double _targetX;
    private double _targetZ;
    private Point? _lastPointer;

    private readonly record struct Vec3(double X, double Y, double Z);

    private readonly record struct Face3D(Vec3[] Vertices, Color BaseColor, double Depth, string? Label = null, Color? LabelColor = null);

    static Plant3DViewportControl()
    {
        AffectsRender<Plant3DViewportControl>(EquipmentProperty, EffectsProperty, CameraPresetProperty);
    }

    public Plant3DViewportControl()
    {
        ClipToBounds = true;
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animTimer.Tick += (_, _) =>
        {
            _time += 0.033;
            InvalidateVisual();
        };
        _animTimer.Start();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CameraPresetProperty && change.NewValue is string preset)
        {
            ApplyCameraPreset(preset);
        }
    }

    private void ApplyCameraPreset(string preset)
    {
        switch (preset)
        {
            case "Crushing & Conveyors":
                _yaw = -0.45; _pitch = 0.45; _zoom = 1.35; _targetX = -5.5; _targetZ = -1.5;
                break;
            case "Thickener Circuit":
                _yaw = 0.55; _pitch = 0.50; _zoom = 1.35; _targetX = 0.5; _targetZ = 3.0;
                break;
            case "Magnetite Prep":
                _yaw = 0.85; _pitch = 0.46; _zoom = 1.38; _targetX = 6.0; _targetZ = -1.5;
                break;
            case "Top-Down Schematic":
                _yaw = 0.0; _pitch = 1.42; _zoom = 0.95; _targetX = 0.0; _targetZ = 0.0;
                break;
            default:
                _yaw = -0.62; _pitch = 0.52; _zoom = 1.05; _targetX = 0.0; _targetZ = 0.0;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _lastPointer = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_lastPointer is Point prev && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var cur = e.GetPosition(this);
            var dx = cur.X - prev.X;
            var dy = cur.Y - prev.Y;
            _yaw += dx * 0.008;
            _pitch = Math.Clamp(_pitch + dy * 0.008, 0.15, 1.48);
            _lastPointer = cur;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _lastPointer = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom + e.Delta.Y * 0.08, 0.55, 2.4);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 40 || h < 40) return;

        // Deep 3D sky/floor gradient background
        var bgBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#060A12"), 0),
                new GradientStop(Color.Parse("#0B1324"), 1)
            }
        };
        context.FillRectangle(bgBrush, new Rect(0, 0, w, h), 6);

        // 1. Ground grid in 3D
        DrawGroundGrid(context, w, h);

        // 2. Build 3D scene faces
        var faces = new List<Face3D>(256);
        var fx = Effects ?? new Unity3DEffectSettings();
        var eqMap = (Equipment ?? Array.Empty<EquipmentItem>()).ToDictionary(e => e.Id, e => e);

        Color GetAssetColor(string id, Color defaultColor)
        {
            if (!eqMap.TryGetValue(id, out var eq)) return defaultColor;
            if (fx.ShowThermalHeatmap)
            {
                if (eq.LoadPercent >= 85) return Color.Parse("#EF4444");
                if (eq.LoadPercent >= 70) return Color.Parse("#F59E0B");
                if (eq.LoadPercent >= 40) return Color.Parse("#22C55E");
                return Color.Parse("#38BDF8");
            }
            return eq.Status switch
            {
                EquipmentStatus.Warning => Color.Parse("#F59E0B"),
                EquipmentStatus.Fault => Color.Parse("#EF4444"),
                EquipmentStatus.Stopped => Color.Parse("#475569"),
                _ => defaultColor
            };
        }

        // --- CRUSHING & CONVEYORS (Left Zone) ---
        // CR-101 Primary Jaw Crusher + Hopper
        AddBox(faces, new Vec3(-7.5, 0.9, -2.5), 2.2, 1.8, 2.0, GetAssetColor("CR-101", Color.Parse("#334155")), "CR-101");
        AddPyramidHopper(faces, new Vec3(-7.5, 2.4, -2.5), 2.6, 1.2, Color.Parse("#D97706"));

        // CV-101 Main Feed Conveyor (angled ramp)
        AddBox(faces, new Vec3(-4.2, 0.85, -2.5), 4.2, 0.45, 1.0, GetAssetColor("CV-101", Color.Parse("#1E3A5F")), "CV-101");

        // SC-102 Vibrating Screen
        AddBox(faces, new Vec3(-1.3, 1.25, -2.5), 1.8, 1.5, 1.8, GetAssetColor("SC-102", Color.Parse("#0284C7")), "SC-102");

        // CV-102 Clean Coal Conveyor (Warning highlight when active)
        AddBox(faces, new Vec3(-1.3, 0.7, 0.6), 1.1, 0.4, 3.8, GetAssetColor("CV-102", Color.Parse("#1E3A5F")), "CV-102");

        // --- THICKENER CIRCUIT (Center-Front Zone) ---
        // TK-201 High-Rate Thickener (large cylinder + rotating rake bridge)
        AddCylinder(faces, new Vec3(1.2, 0.0, 3.2), 2.8, 1.6, 18, GetAssetColor("TK-201", Color.Parse("#1E40AF")), "TK-201");
        double rakeAngle = fx.AnimateConveyors ? _time * 0.45 : 0.2;
        AddRotatedBar(faces, new Vec3(1.2, 1.75, 3.2), 5.8, 0.25, 0.45, rakeAngle, Color.Parse("#38BDF8"));

        // PP-201, PP-202, PP-203 Pumps
        AddBox(faces, new Vec3(-2.4, 0.45, 4.2), 1.1, 0.9, 0.9, GetAssetColor("PP-201", Color.Parse("#0EA5E9")), "PP-201");
        AddBox(faces, new Vec3(-2.4, 0.45, 5.5), 1.1, 0.9, 0.9, GetAssetColor("PP-202", Color.Parse("#475569")), "PP-202");
        AddBox(faces, new Vec3(4.6, 0.40, 4.2), 0.9, 0.8, 0.8, GetAssetColor("PP-203", Color.Parse("#10B981")), "PP-203");

        // --- MAGNETITE PREPARATION (Right Zone) ---
        // TK-301 Magnetite Medium Tank
        AddCylinder(faces, new Vec3(5.8, 0.0, -2.2), 1.45, 2.4, 14, GetAssetColor("TK-301", Color.Parse("#B45309")), "TK-301");
        // MS-301 Magnetic Separator Drum
        AddCylinder(faces, new Vec3(3.2, 0.6, -2.2), 0.95, 1.4, 12, GetAssetColor("MS-301", Color.Parse("#0D9488")), "MS-301");
        // DM-301 Dense Medium Cyclone Pump
        AddBox(faces, new Vec3(5.8, 0.55, 0.8), 1.4, 1.1, 1.2, GetAssetColor("DM-301", Color.Parse("#0284C7")), "DM-301");

        // Sort faces back-to-front (Painter's algorithm)
        faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        // 3. Draw sorted 3D faces with directional lighting
        var lightDir = Normalize(new Vec3(0.45, 0.82, -0.35));
        foreach (var face in faces)
        {
            var pts = new Point[face.Vertices.Length];
            bool visible = true;
            for (int i = 0; i < face.Vertices.Length; i++)
            {
                if (!TryProject(face.Vertices[i], w, h, out pts[i]))
                {
                    visible = false;
                    break;
                }
            }
            if (!visible) continue;

            // Compute face normal for shading
            var v1 = Sub(face.Vertices[1], face.Vertices[0]);
            var v2 = Sub(face.Vertices[2], face.Vertices[0]);
            var normal = Normalize(Cross(v1, v2));
            double shade = 0.48 + 0.52 * Math.Max(0, Dot(normal, lightDir));

            byte r = (byte)Math.Clamp(face.BaseColor.R * shade, 0, 255);
            byte g = (byte)Math.Clamp(face.BaseColor.G * shade, 0, 255);
            byte b = (byte)Math.Clamp(face.BaseColor.B * shade, 0, 255);

            var geom = new StreamGeometry();
            using (var gctx = geom.Open())
            {
                gctx.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++)
                    gctx.LineTo(pts[i]);
                gctx.EndFigure(true);
            }

            context.DrawGeometry(
                new SolidColorBrush(Color.FromRgb(r, g, b)),
                new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 0.75),
                geom);
        }

        // 4. Draw 3D Material Flow Particles
        if (fx.ShowFlowParticles)
        {
            DrawFlowParticles(context, w, h);
        }

        // 5. Draw 3D Alarm Halos for Warning/Fault equipment
        if (fx.ShowAlarmHalos)
        {
            DrawAlarmHalos(context, w, h, eqMap);
        }

        // 6. Draw 3D Floating Equipment Labels
        DrawEquipmentLabels(context, w, h, eqMap);

        // 7. HUD Overlay hint
        var hint = new FormattedText(
            "3D Viewport · Drag to Orbit · Scroll to Zoom · Live Telemetry Synced",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter"),
            11,
            new SolidColorBrush(Color.Parse("#64748B")));
        context.DrawText(hint, new Point(12, h - 22));
    }

    private void DrawGroundGrid(DrawingContext context, double w, double h)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 56, 189, 248)), 1);
        for (int i = -10; i <= 10; i += 2)
        {
            if (TryProject(new Vec3(i, 0, -8), w, h, out var a) && TryProject(new Vec3(i, 0, 8), w, h, out var b))
                context.DrawLine(gridPen, a, b);
            if (TryProject(new Vec3(-10, 0, i * 0.8), w, h, out var c) && TryProject(new Vec3(10, 0, i * 0.8), w, h, out var d))
                context.DrawLine(gridPen, c, d);
        }
    }

    private void DrawFlowParticles(DrawingContext context, double w, double h)
    {
        var coalBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
        var slurryBrush = new SolidColorBrush(Color.Parse("#38BDF8"));

        // Crushing -> Screen -> Clean Coal Conveyor
        for (int i = 0; i < 10; i++)
        {
            double t = ((_time * 0.35 + i * 0.1) % 1.0);
            var p = new Vec3(-6.4 + t * 5.1, 1.25, -2.5);
            if (TryProject(p, w, h, out var pt))
                context.DrawEllipse(coalBrush, null, pt, 3.2, 3.2);
        }

        // Screen -> CV-102
        for (int i = 0; i < 6; i++)
        {
            double t = ((_time * 0.4 + i * 0.16) % 1.0);
            var p = new Vec3(-1.3, 1.05, -1.2 + t * 3.6);
            if (TryProject(p, w, h, out var pt))
                context.DrawEllipse(coalBrush, null, pt, 3.0, 3.0);
        }

        // Thickener -> Underflow Pump -> Magnetite Tank
        for (int i = 0; i < 8; i++)
        {
            double t = ((_time * 0.3 + i * 0.125) % 1.0);
            var p = new Vec3(1.2 + t * 4.6, 0.6, 3.2 - t * 5.4);
            if (TryProject(p, w, h, out var pt))
                context.DrawEllipse(slurryBrush, null, pt, 3.0, 3.0);
        }
    }

    private void DrawAlarmHalos(DrawingContext context, double w, double h, Dictionary<string, EquipmentItem> eqMap)
    {
        double pulse = 0.55 + 0.45 * Math.Sin(_time * 4.5);
        foreach (var (id, pos) in GetLabelPositions())
        {
            if (!eqMap.TryGetValue(id, out var eq)) continue;
            if (eq.Status != EquipmentStatus.Warning && eq.Status != EquipmentStatus.Fault) continue;

            var color = eq.Status == EquipmentStatus.Fault ? Color.Parse("#EF4444") : Color.Parse("#F59E0B");
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(220 * pulse), color.R, color.G, color.B)), 2.5);

            Point? prev = null;
            double radius = 1.45 + 0.2 * pulse;
            for (int i = 0; i <= 16; i++)
            {
                double ang = i * Math.PI * 2 / 16;
                var world = new Vec3(pos.X + Math.Cos(ang) * radius, 0.15, pos.Z + Math.Sin(ang) * radius);
                if (TryProject(world, w, h, out var pt))
                {
                    if (prev.HasValue) context.DrawLine(pen, prev.Value, pt);
                    prev = pt;
                }
            }
        }
    }

    private void DrawEquipmentLabels(DrawingContext context, double w, double h, Dictionary<string, EquipmentItem> eqMap)
    {
        foreach (var (id, pos) in GetLabelPositions())
        {
            if (!TryProject(pos, w, h, out var pt)) continue;
            string metric = eqMap.TryGetValue(id, out var eq) ? $"{id} · {eq.PrimaryMetricDisplay}" : id;
            var text = new FormattedText(
                metric,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                11,
                Brushes.White);

            var bg = new Rect(pt.X - 6, pt.Y - 10, text.Width + 12, text.Height + 6);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(185, 15, 23, 42)), bg, 4);
            context.DrawText(text, new Point(pt.X, pt.Y - 7));
        }
    }

    private static IEnumerable<(string Id, Vec3 Pos)> GetLabelPositions()
    {
        yield return ("CR-101", new Vec3(-7.5, 3.6, -2.5));
        yield return ("CV-101", new Vec3(-4.2, 1.8, -2.5));
        yield return ("SC-102", new Vec3(-1.3, 2.6, -2.5));
        yield return ("CV-102", new Vec3(-1.3, 1.5, 0.8));
        yield return ("TK-201", new Vec3(1.2, 2.5, 3.2));
        yield return ("PP-201", new Vec3(-2.4, 1.5, 4.2));
        yield return ("TK-301", new Vec3(5.8, 3.0, -2.2));
        yield return ("MS-301", new Vec3(3.2, 2.3, -2.2));
        yield return ("DM-301", new Vec3(5.8, 1.7, 0.8));
    }

    private void AddBox(List<Face3D> faces, Vec3 center, double sx, double sy, double sz, Color color, string? label = null)
    {
        double hx = sx / 2, hy = sy / 2, hz = sz / 2;
        var v = new[]
        {
            new Vec3(center.X - hx, center.Y - hy, center.Z - hz),
            new Vec3(center.X + hx, center.Y - hy, center.Z - hz),
            new Vec3(center.X + hx, center.Y + hy, center.Z - hz),
            new Vec3(center.X - hx, center.Y + hy, center.Z - hz),
            new Vec3(center.X - hx, center.Y - hy, center.Z + hz),
            new Vec3(center.X + hx, center.Y - hy, center.Z + hz),
            new Vec3(center.X + hx, center.Y + hy, center.Z + hz),
            new Vec3(center.X - hx, center.Y + hy, center.Z + hz),
        };

        int[][] quads =
        {
            new[] { 0, 1, 2, 3 },
            new[] { 5, 4, 7, 6 },
            new[] { 4, 0, 3, 7 },
            new[] { 1, 5, 6, 2 },
            new[] { 3, 2, 6, 7 },
        };

        foreach (var q in quads)
        {
            var pts = new[] { v[q[0]], v[q[1]], v[q[2]], v[q[3]] };
            faces.Add(new Face3D(pts, color, AvgDepth(pts), label));
        }
    }

    private void AddRotatedBar(List<Face3D> faces, Vec3 center, double length, double height, double width, double angle, Color color)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        double hx = length / 2, hy = height / 2, hz = width / 2;
        Vec3 Rot(double x, double y, double z) =>
            new(center.X + x * c - z * s, center.Y + y, center.Z + x * s + z * c);

        var v = new[]
        {
            Rot(-hx, -hy, -hz), Rot(hx, -hy, -hz), Rot(hx, hy, -hz), Rot(-hx, hy, -hz),
            Rot(-hx, -hy, hz),  Rot(hx, -hy, hz),  Rot(hx, hy, hz),  Rot(-hx, hy, hz)
        };
        int[][] quads =
        {
            new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 },
            new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }
        };
        foreach (var q in quads)
        {
            var pts = new[] { v[q[0]], v[q[1]], v[q[2]], v[q[3]] };
            faces.Add(new Face3D(pts, color, AvgDepth(pts)));
        }
    }

    private void AddPyramidHopper(List<Face3D> faces, Vec3 center, double topSize, double height, Color color)
    {
        double t = topSize / 2, b = topSize * 0.25, h = height / 2;
        var v = new[]
        {
            new Vec3(center.X - b, center.Y - h, center.Z - b),
            new Vec3(center.X + b, center.Y - h, center.Z - b),
            new Vec3(center.X + b, center.Y - h, center.Z + b),
            new Vec3(center.X - b, center.Y - h, center.Z + b),
            new Vec3(center.X - t, center.Y + h, center.Z - t),
            new Vec3(center.X + t, center.Y + h, center.Z - t),
            new Vec3(center.X + t, center.Y + h, center.Z + t),
            new Vec3(center.X - t, center.Y + h, center.Z + t),
        };
        int[][] quads = { new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
        foreach (var q in quads)
        {
            var pts = new[] { v[q[0]], v[q[1]], v[q[2]], v[q[3]] };
            faces.Add(new Face3D(pts, color, AvgDepth(pts)));
        }
    }

    private void AddCylinder(List<Face3D> faces, Vec3 baseCenter, double radius, double height, int segs, Color color, string? label = null)
    {
        var topPts = new Vec3[segs];
        for (int i = 0; i < segs; i++)
        {
            double a0 = i * Math.PI * 2 / segs;
            double a1 = (i + 1) * Math.PI * 2 / segs;
            var b0 = new Vec3(baseCenter.X + Math.Cos(a0) * radius, baseCenter.Y, baseCenter.Z + Math.Sin(a0) * radius);
            var b1 = new Vec3(baseCenter.X + Math.Cos(a1) * radius, baseCenter.Y, baseCenter.Z + Math.Sin(a1) * radius);
            var t1 = new Vec3(b1.X, baseCenter.Y + height, b1.Z);
            var t0 = new Vec3(b0.X, baseCenter.Y + height, b0.Z);
            topPts[i] = t0;
            var side = new[] { b0, b1, t1, t0 };
            faces.Add(new Face3D(side, color, AvgDepth(side), label));
        }
        faces.Add(new Face3D(topPts, color, AvgDepth(topPts)));
    }

    private double AvgDepth(Vec3[] pts)
    {
        double sum = 0;
        foreach (var p in pts) sum += TransformToCamera(p).Z;
        return sum / pts.Length;
    }

    private Vec3 TransformToCamera(Vec3 world)
    {
        double x = world.X - _targetX;
        double y = world.Y - 0.8;
        double z = world.Z - _targetZ;

        // Rotate around Y (yaw)
        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
        double x1 = x * cy - z * sy;
        double z1 = x * sy + z * cy;

        // Rotate around X (pitch)
        double cp = Math.Cos(_pitch), sp = Math.Sin(_pitch);
        double y2 = y * cp - z1 * sp;
        double z2 = y * sp + z1 * cp;

        return new Vec3(x1, y2, z2 + 19.0 / _zoom);
    }

    private bool TryProject(Vec3 world, double w, double h, out Point screen)
    {
        var cam = TransformToCamera(world);
        if (cam.Z <= 0.5)
        {
            screen = default;
            return false;
        }
        double fov = Math.Min(w, h) * 1.15;
        screen = new Point(w * 0.5 + (cam.X / cam.Z) * fov, h * 0.52 - (cam.Y / cam.Z) * fov);
        return true;
    }

    private static Vec3 Sub(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vec3 Normalize(Vec3 v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-6 ? new Vec3(0, 1, 0) : new Vec3(v.X / len, v.Y / len, v.Z / len);
    }
}
