using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Controls;

/// <summary>
/// Interactive 3D viewport rendering the coal processing plant with:
/// - Smooth camera orbit/pan/zoom, Auto-Orbit, Perspective/Orthographic toggle, WASD/F/R keys
/// - Focus-on-Selected and Focus-on-Alarm camera fly-to
/// - Telemetry-driven vibration physics (SC-102, CF-501) and rotating drum/rake (TK-201, MS-301)
/// - Ground cast shadows, depth fog, specular highlights, connecting pipes, and 3D axis gizmo
/// </summary>
public sealed class Plant3DViewportControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<EquipmentItem>?> EquipmentProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, IReadOnlyList<EquipmentItem>?>(nameof(Equipment));

    public static readonly StyledProperty<Unity3DEffectSettings?> EffectsProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, Unity3DEffectSettings?>(nameof(Effects));

    public static readonly StyledProperty<string> CameraPresetProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, string>(nameof(CameraPreset), "Plant Overview");

    public static readonly StyledProperty<EquipmentItem?> SelectedEquipmentProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, EquipmentItem?>(
            nameof(SelectedEquipment),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> FocusTargetIdProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, string?>(nameof(FocusTargetId));

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

    public EquipmentItem? SelectedEquipment
    {
        get => GetValue(SelectedEquipmentProperty);
        set => SetValue(SelectedEquipmentProperty, value);
    }

    public string? FocusTargetId
    {
        get => GetValue(FocusTargetIdProperty);
        set => SetValue(FocusTargetIdProperty, value);
    }

    private readonly DispatcherTimer _animTimer;
    private readonly Dictionary<uint, SolidColorBrush> _brushCache = new();
    private readonly List<(string Id, Rect Bounds)> _hitRects = new();

    private static readonly Pen FaceEdgePen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 0.7);
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(42, 56, 189, 248)), 1);
    private static readonly Pen PipePen = new(new SolidColorBrush(Color.FromArgb(140, 148, 163, 184)), 2.2);
    private static readonly SolidColorBrush LabelBgBrush = new(Color.FromArgb(195, 15, 23, 42));
    private static readonly SolidColorBrush SelectedLabelBgBrush = new(Color.FromArgb(225, 12, 74, 110));
    private static readonly Pen SelectedLabelPen = new(new SolidColorBrush(Color.Parse("#38BDF8")), 1.5);
    private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface HudTypeface = new("Inter");

    private double _time;
    private double _yaw = -0.62;
    private double _pitch = 0.52;
    private double _zoom = 1.05;
    private double _targetX;
    private double _targetZ;

    private double _goalYaw = -0.62;
    private double _goalPitch = 0.52;
    private double _goalZoom = 1.05;
    private double _goalTargetX;
    private double _goalTargetZ;

    private Point? _pressOrigin;
    private Point? _lastPointer;
    private bool _isPanning;
    private bool _didDrag;

    private readonly record struct Vec3(double X, double Y, double Z);
    private readonly record struct Face3D(Vec3[] Vertices, Color BaseColor, double Depth, Vec3 Normal);

    static Plant3DViewportControl()
    {
        AffectsRender<Plant3DViewportControl>(
            EquipmentProperty,
            EffectsProperty,
            CameraPresetProperty,
            SelectedEquipmentProperty,
            FocusTargetIdProperty);
    }

    public Plant3DViewportControl()
    {
        ClipToBounds = true;
        Focusable = true;
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animTimer.Tick += (_, _) =>
        {
            _time += 0.033;
            if (Effects?.AutoOrbit == true && _lastPointer is null)
            {
                _goalYaw += 0.0045;
            }
            StepCameraSmoothing();
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
        else if (change.Property == FocusTargetIdProperty && change.NewValue is string id && !string.IsNullOrWhiteSpace(id))
        {
            FocusOnEquipment(id);
        }
    }

    public void FocusOnEquipment(string equipmentId)
    {
        foreach (var (id, pos) in GetLabelPositions())
        {
            if (id == equipmentId)
            {
                _goalTargetX = pos.X;
                _goalTargetZ = pos.Z;
                _goalZoom = 1.68;
                _goalPitch = 0.48;
                InvalidateVisual();
                return;
            }
        }
    }

    private void StepCameraSmoothing()
    {
        const double lerp = 0.22;
        _yaw += (_goalYaw - _yaw) * lerp;
        _pitch += (_goalPitch - _pitch) * lerp;
        _zoom += (_goalZoom - _zoom) * lerp;
        _targetX += (_goalTargetX - _targetX) * lerp;
        _targetZ += (_goalTargetZ - _targetZ) * lerp;
    }

    private void ApplyCameraPreset(string preset)
    {
        switch (preset)
        {
            case "Crushing & Conveyors":
                _goalYaw = -0.45; _goalPitch = 0.45; _goalZoom = 1.38; _goalTargetX = -4.8; _goalTargetZ = -1.6;
                break;
            case "Thickener Circuit":
                _goalYaw = 0.52; _goalPitch = 0.50; _goalZoom = 1.36; _goalTargetX = 0.8; _goalTargetZ = 3.2;
                break;
            case "Magnetite Prep":
                _goalYaw = 0.82; _goalPitch = 0.46; _goalZoom = 1.38; _goalTargetX = 5.2; _goalTargetZ = -1.2;
                break;
            case "Fine Coal & Flotation":
                _goalYaw = -0.78; _goalPitch = 0.48; _goalZoom = 1.45; _goalTargetX = -5.6; _goalTargetZ = 3.5;
                break;
            case "Dewatering & Tailings":
                _goalYaw = 0.72; _goalPitch = 0.48; _goalZoom = 1.45; _goalTargetX = 5.4; _goalTargetZ = 4.2;
                break;
            case "ROM & Silo Loadout":
                _goalYaw = -0.95; _goalPitch = 0.44; _goalZoom = 1.42; _goalTargetX = -8.0; _goalTargetZ = 1.4;
                break;
            case "Top-Down Schematic":
                _goalYaw = 0.0; _goalPitch = 1.42; _goalZoom = 0.96; _goalTargetX = 0.0; _goalTargetZ = 0.5;
                break;
            default:
                _goalYaw = -0.62; _goalPitch = 0.52; _goalZoom = 1.05; _goalTargetX = 0.0; _goalTargetZ = 0.5;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = 0.75 / _zoom;
        switch (e.Key)
        {
            case Key.W or Key.Up:
                _goalTargetZ -= step * Math.Cos(_yaw);
                _goalTargetX -= step * Math.Sin(_yaw);
                e.Handled = true;
                break;
            case Key.S or Key.Down:
                _goalTargetZ += step * Math.Cos(_yaw);
                _goalTargetX += step * Math.Sin(_yaw);
                e.Handled = true;
                break;
            case Key.A or Key.Left:
                _goalTargetX -= step * Math.Cos(_yaw);
                _goalTargetZ += step * Math.Sin(_yaw);
                e.Handled = true;
                break;
            case Key.D or Key.Right:
                _goalTargetX += step * Math.Cos(_yaw);
                _goalTargetZ -= step * Math.Sin(_yaw);
                e.Handled = true;
                break;
            case Key.F when SelectedEquipment is not null:
                FocusOnEquipment(SelectedEquipment.Id);
                e.Handled = true;
                break;
            case Key.R:
                ApplyCameraPreset("Plant Overview");
                e.Handled = true;
                break;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        if (e.ClickCount == 2 && props.IsLeftButtonPressed)
        {
            ApplyCameraPreset("Plant Overview");
            return;
        }

        _pressOrigin = e.GetPosition(this);
        _lastPointer = _pressOrigin;
        _isPanning = props.IsRightButtonPressed || props.IsMiddleButtonPressed;
        _didDrag = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (_lastPointer is not Point prev || (!props.IsLeftButtonPressed && !props.IsRightButtonPressed && !props.IsMiddleButtonPressed))
        {
            return;
        }

        var cur = e.GetPosition(this);
        var dx = cur.X - prev.X;
        var dy = cur.Y - prev.Y;
        if (_pressOrigin is Point origin && Math.Abs(cur.X - origin.X) + Math.Abs(cur.Y - origin.Y) > 4)
        {
            _didDrag = true;
        }

        if (_isPanning || props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            double panScale = 0.018 / _zoom;
            double cy = Math.Cos(_yaw);
            double sy = Math.Sin(_yaw);
            _goalTargetX = Math.Clamp(_goalTargetX - (dx * cy + dy * sy) * panScale, -14, 14);
            _goalTargetZ = Math.Clamp(_goalTargetZ - (-dx * sy + dy * cy) * panScale, -12, 12);
        }
        else if (props.IsLeftButtonPressed)
        {
            _goalYaw += dx * 0.008;
            _goalPitch = Math.Clamp(_goalPitch + dy * 0.008, 0.15, 1.48);
        }

        _lastPointer = cur;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_didDrag && !_isPanning)
        {
            var pos = e.GetPosition(this);
            TrySelectAt(pos);
        }

        _pressOrigin = null;
        _lastPointer = null;
        _isPanning = false;
        _didDrag = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _goalZoom = Math.Clamp(_goalZoom + e.Delta.Y * 0.10, 0.50, 2.60);
        InvalidateVisual();
    }

    private void TrySelectAt(Point p)
    {
        if (Equipment is null) return;
        for (int i = _hitRects.Count - 1; i >= 0; i--)
        {
            if (_hitRects[i].Bounds.Contains(p))
            {
                var match = Equipment.FirstOrDefault(eq => eq.Id == _hitRects[i].Id);
                if (match is not null)
                {
                    SelectedEquipment = match;
                    InvalidateVisual();
                }
                return;
            }
        }
    }

    private SolidColorBrush GetBrush(byte r, byte g, byte b, byte a = 255)
    {
        uint key = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        if (!_brushCache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            _brushCache[key] = brush;
        }
        return brush;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 40 || h < 40) return;

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

        var fx = Effects ?? new Unity3DEffectSettings();
        var eqMap = (Equipment ?? Array.Empty<EquipmentItem>()).ToDictionary(e => e.Id, e => e);

        DrawGroundGrid(context, w, h);
        if (fx.ShowGroundShadows)
        {
            DrawGroundShadows(context, w, h);
        }
        DrawConnectingPipes(context, w, h);

        var faces = new List<Face3D>(384);

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

        double GetVibrationOffset(string id)
        {
            if (!fx.EnableVibrationPhysics || !eqMap.TryGetValue(id, out var eq) || eq.Status == EquipmentStatus.Stopped)
                return 0.0;
            double amp = 0.02 + 0.03 * (eq.LoadPercent / 100.0);
            return Math.Sin(_time * 28.0) * amp;
        }

        // --- CRUSHING & CONVEYORS ---
        AddBox(faces, new Vec3(-7.5, 0.9, -2.5), 2.2, 1.8, 2.0, GetAssetColor("CR-101", Color.Parse("#334155")));
        AddPyramidHopper(faces, new Vec3(-7.5, 2.4, -2.5), 2.6, 1.2, Color.Parse("#D97706"));

        AddSubdividedConveyorX(faces, -6.0, -2.4, 0.65, 1.25, -2.5, 0.95, 3, GetAssetColor("CV-101", Color.Parse("#1E3A5F")));
        double scVib = GetVibrationOffset("SC-102");
        AddBox(faces, new Vec3(-1.3, 1.25 + scVib, -2.5), 1.8, 1.5, 1.8, GetAssetColor("SC-102", Color.Parse("#0284C7")));
        AddSubdividedConveyorZ(faces, -1.3, -1.3, 2.5, 0.95, 0.65, 1.0, 3, GetAssetColor("CV-102", Color.Parse("#1E3A5F")));

        // --- THICKENER CIRCUIT ---
        AddCylinder(faces, new Vec3(1.2, 0.0, 3.2), 2.8, 1.6, 20, GetAssetColor("TK-201", Color.Parse("#1E40AF")));
        double rakeAngle = fx.AnimateConveyors ? _time * 0.45 : 0.2;
        AddRotatedBar(faces, new Vec3(1.2, 1.78, 3.2), 5.8, 0.22, 0.42, rakeAngle, Color.Parse("#38BDF8"));

        AddBox(faces, new Vec3(-2.4, 0.45, 4.2), 1.1, 0.9, 0.9, GetAssetColor("PP-201", Color.Parse("#0EA5E9")));
        AddBox(faces, new Vec3(-2.4, 0.45, 5.5), 1.1, 0.9, 0.9, GetAssetColor("PP-202", Color.Parse("#475569")));
        AddBox(faces, new Vec3(4.6, 0.40, 4.2), 0.9, 0.8, 0.8, GetAssetColor("PP-203", Color.Parse("#10B981")));

        // --- MAGNETITE PREPARATION ---
        AddCylinder(faces, new Vec3(5.8, 0.0, -2.2), 1.45, 2.4, 14, GetAssetColor("TK-301", Color.Parse("#B45309")));
        AddCylinder(faces, new Vec3(3.2, 0.6, -2.2), 0.95, 1.4, 12, GetAssetColor("MS-301", Color.Parse("#0D9488")));
        double drumAngle = fx.AnimateConveyors ? _time * 1.4 : 0.0;
        AddRotatedBar(faces, new Vec3(3.2, 2.08, -2.2), 1.7, 0.16, 0.25, drumAngle, Color.Parse("#5EEAD4"));
        AddBox(faces, new Vec3(5.8, 0.55, 0.8), 1.4, 1.1, 1.2, GetAssetColor("DM-301", Color.Parse("#0284C7")));
        AddPyramidHopper(faces, new Vec3(5.8, 1.6, 0.8), 1.2, 0.9, Color.Parse("#38BDF8"));

        // --- FINE COAL, DEWATERING & SILO LOADOUT ---
        AddCylinder(faces, new Vec3(-5.8, 0.0, 3.6), 0.95, 3.0, 14, GetAssetColor("FC-401", Color.Parse("#0284C7")));
        AddCylinder(faces, new Vec3(-5.8, 2.75, 3.6), 1.25, 0.35, 14, Color.Parse("#38BDF8"));
        double cfVib = GetVibrationOffset("CF-501");
        AddBox(faces, new Vec3(5.6, 0.7 + cfVib, 4.4), 2.0, 1.4, 1.3, GetAssetColor("CF-501", Color.Parse("#7C3AED")));
        AddCylinder(faces, new Vec3(-8.2, 0.7, 1.6), 1.35, 2.9, 16, GetAssetColor("SL-601", Color.Parse("#059669")));
        AddPyramidHopper(faces, new Vec3(-8.2, 0.35, 1.6), 2.0, 0.7, Color.Parse("#334155"));

        faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        var lightDir = Normalize(new Vec3(0.45, 0.82, -0.35));
        foreach (var face in faces)
        {
            var c0 = TransformToCamera(face.Vertices[0]);
            var c1 = TransformToCamera(face.Vertices[1]);
            var c2 = TransformToCamera(face.Vertices[2]);
            var camCross = Cross(Sub(c1, c0), Sub(c2, c0));
            if (camCross.Z <= 0) continue;

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

            double ndotl = Math.Max(0, Dot(face.Normal, lightDir));
            double shade = 0.46 + 0.54 * ndotl + (ndotl > 0.82 ? 0.12 : 0.0);
            double fog = Math.Clamp((face.Depth - 14.0) / 28.0, 0.0, 0.35);

            byte r = (byte)Math.Clamp(face.BaseColor.R * shade * (1 - fog) + 11 * fog, 0, 255);
            byte g = (byte)Math.Clamp(face.BaseColor.G * shade * (1 - fog) + 19 * fog, 0, 255);
            byte b = (byte)Math.Clamp(face.BaseColor.B * shade * (1 - fog) + 36 * fog, 0, 255);

            var geom = new StreamGeometry();
            using (var gctx = geom.Open())
            {
                gctx.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++)
                    gctx.LineTo(pts[i]);
                gctx.EndFigure(true);
            }

            context.DrawGeometry(GetBrush(r, g, b), FaceEdgePen, geom);
        }

        if (fx.ShowFlowParticles)
        {
            DrawFlowParticles(context, w, h);
        }

        if (fx.ShowAlarmHalos)
        {
            DrawAlarmHalos(context, w, h, eqMap);
        }

        DrawSelectionRing(context, w, h);
        DrawEquipmentLabels(context, w, h, eqMap);
        DrawAxisGizmo(context, w);

        string projMode = fx.IsOrthographic ? "ORTHO" : "PERSP";
        var hint = new FormattedText(
            $"[{projMode}] Left-Drag: Orbit  |  Right-Drag / WASD: Pan  |  Scroll: Zoom  |  F: Focus  |  R / Dbl-Click: Reset",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            HudTypeface,
            11,
            GetBrush(148, 163, 184));
        context.DrawText(hint, new Point(12, h - 22));
    }

    private void DrawGroundGrid(DrawingContext context, double w, double h)
    {
        for (int i = -10; i <= 10; i += 2)
        {
            if (TryProject(new Vec3(i, 0, -8), w, h, out var a) && TryProject(new Vec3(i, 0, 8), w, h, out var b))
                context.DrawLine(GridPen, a, b);
            if (TryProject(new Vec3(-10, 0, i * 0.8), w, h, out var c) && TryProject(new Vec3(10, 0, i * 0.8), w, h, out var d))
                context.DrawLine(GridPen, c, d);
        }
    }

    private void DrawGroundShadows(DrawingContext context, double w, double h)
    {
        var shadowBrush = GetBrush(2, 6, 14, 130);
        var shadowCenters = new (double X, double Z, double R)[]
        {
            (-7.3, -2.3, 1.5), (-1.1, -2.3, 1.3), (1.5, 3.4, 3.0),
            (6.0, -2.0, 1.7), (-5.6, 3.8, 1.2), (5.8, 4.6, 1.3), (-8.0, 1.8, 1.6)
        };
        foreach (var (sx, sz, rad) in shadowCenters)
        {
            var geom = new StreamGeometry();
            using var gctx = geom.Open();
            bool started = false;
            for (int i = 0; i < 12; i++)
            {
                double ang = i * Math.PI * 2 / 12;
                if (TryProject(new Vec3(sx + Math.Cos(ang) * rad, 0.02, sz + Math.Sin(ang) * rad), w, h, out var pt))
                {
                    if (!started) { gctx.BeginFigure(pt, true); started = true; }
                    else gctx.LineTo(pt);
                }
            }
            if (started)
            {
                gctx.EndFigure(true);
                context.DrawGeometry(shadowBrush, null, geom);
            }
        }
    }

    private void DrawConnectingPipes(DrawingContext context, double w, double h)
    {
        DrawPipeSegment(context, w, h, new Vec3(-1.3, 0.7, 2.5), new Vec3(-8.2, 1.5, 1.6));
        DrawPipeSegment(context, w, h, new Vec3(-1.3, 1.1, -1.6), new Vec3(-5.8, 2.2, 3.6));
        DrawPipeSegment(context, w, h, new Vec3(1.2, 0.5, 3.2), new Vec3(-2.4, 0.5, 4.2));
        DrawPipeSegment(context, w, h, new Vec3(-2.4, 0.5, 4.2), new Vec3(5.8, 1.1, -2.2));
        DrawPipeSegment(context, w, h, new Vec3(5.8, 0.8, -2.2), new Vec3(5.8, 0.8, 0.8));
        DrawPipeSegment(context, w, h, new Vec3(5.8, 0.8, 0.8), new Vec3(5.6, 0.8, 4.4));
    }

    private void DrawPipeSegment(DrawingContext context, double w, double h, Vec3 a, Vec3 b)
    {
        if (TryProject(a, w, h, out var pa) && TryProject(b, w, h, out var pb))
            context.DrawLine(PipePen, pa, pb);
    }

    private void DrawFlowParticles(DrawingContext context, double w, double h)
    {
        var coalBrush = GetBrush(245, 158, 11);
        var slurryBrush = GetBrush(56, 189, 248);
        var frothBrush = GetBrush(34, 197, 94);

        for (int i = 0; i < 8; i++)
        {
            double t = (_time * 0.35 + i * 0.125) % 1.0;
            if (TryProject(new Vec3(-6.4 + t * 5.1, 1.25, -2.5), w, h, out var pt))
                context.DrawEllipse(coalBrush, null, pt, 3.2, 3.2);
        }

        for (int i = 0; i < 6; i++)
        {
            double t = (_time * 0.38 + i * 0.16) % 1.0;
            if (TryProject(new Vec3(-1.3 - t * 6.9, 1.1 + t * 0.8, 2.5 - t * 0.9), w, h, out var pt))
                context.DrawEllipse(coalBrush, null, pt, 3.0, 3.0);
        }

        for (int i = 0; i < 6; i++)
        {
            double t = (_time * 0.32 + i * 0.16) % 1.0;
            if (TryProject(new Vec3(-1.3 - t * 4.5, 1.2 + t * 1.1, -1.6 + t * 5.2), w, h, out var pt))
                context.DrawEllipse(frothBrush, null, pt, 2.8, 2.8);
        }

        for (int i = 0; i < 8; i++)
        {
            double t = (_time * 0.30 + i * 0.125) % 1.0;
            if (TryProject(new Vec3(1.2 + t * 4.6, 0.65, 3.2 - t * 5.4), w, h, out var pt))
                context.DrawEllipse(slurryBrush, null, pt, 3.0, 3.0);
        }
        for (int i = 0; i < 5; i++)
        {
            double t = (_time * 0.34 + i * 0.2) % 1.0;
            if (TryProject(new Vec3(5.8 - t * 0.2, 0.8, 0.8 + t * 3.6), w, h, out var pt))
                context.DrawEllipse(slurryBrush, null, pt, 2.8, 2.8);
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
            var pen = new Pen(GetBrush(color.R, color.G, color.B, (byte)(220 * pulse)), 2.5);
            DrawRing(context, w, h, new Vec3(pos.X, 0.15, pos.Z), 1.45 + 0.2 * pulse, pen);
        }
    }

    private void DrawSelectionRing(DrawingContext context, double w, double h)
    {
        if (SelectedEquipment is null) return;
        foreach (var (id, pos) in GetLabelPositions())
        {
            if (id == SelectedEquipment.Id)
            {
                DrawRing(context, w, h, new Vec3(pos.X, 0.12, pos.Z), 1.75, SelectedLabelPen);
                break;
            }
        }
    }

    private void DrawRing(DrawingContext context, double w, double h, Vec3 center, double radius, Pen pen)
    {
        Point? prev = null;
        for (int i = 0; i <= 18; i++)
        {
            double ang = i * Math.PI * 2 / 18;
            var world = new Vec3(center.X + Math.Cos(ang) * radius, center.Y, center.Z + Math.Sin(ang) * radius);
            if (TryProject(world, w, h, out var pt))
            {
                if (prev.HasValue) context.DrawLine(pen, prev.Value, pt);
                prev = pt;
            }
        }
    }

    private void DrawEquipmentLabels(DrawingContext context, double w, double h, Dictionary<string, EquipmentItem> eqMap)
    {
        _hitRects.Clear();
        foreach (var (id, pos) in GetLabelPositions())
        {
            if (!TryProject(pos, w, h, out var pt)) continue;
            bool isSelected = SelectedEquipment?.Id == id;
            string metric = eqMap.TryGetValue(id, out var eq) ? $"{id} · {eq.PrimaryMetricDisplay}" : id;
            var text = new FormattedText(
                metric,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                Brushes.White);

            var bg = new Rect(pt.X - 6, pt.Y - 10, text.Width + 12, text.Height + 6);
            _hitRects.Add((id, bg));
            context.DrawRectangle(isSelected ? SelectedLabelBgBrush : LabelBgBrush, isSelected ? SelectedLabelPen : null, bg, 4);
            context.DrawText(text, new Point(pt.X, pt.Y - 7));
        }
    }

    private void DrawAxisGizmo(DrawingContext context, double w)
    {
        var origin = new Point(w - 46, 46);
        double len = 24;
        DrawAxisVec(context, origin, new Vec3(1, 0, 0), len, GetBrush(239, 68, 68), "X");
        DrawAxisVec(context, origin, new Vec3(0, 1, 0), len, GetBrush(34, 197, 94), "Y");
        DrawAxisVec(context, origin, new Vec3(0, 0, 1), len, GetBrush(56, 189, 248), "Z");
    }

    private void DrawAxisVec(DrawingContext context, Point origin, Vec3 axis, double len, IBrush brush, string label)
    {
        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
        double x1 = axis.X * cy - axis.Z * sy;
        double z1 = axis.X * sy + axis.Z * cy;
        double cp = Math.Cos(_pitch), sp = Math.Sin(_pitch);
        double y2 = axis.Y * cp - z1 * sp;

        var tip = new Point(origin.X + x1 * len, origin.Y - y2 * len);
        context.DrawLine(new Pen(brush, 1.8), origin, tip);
        var txt = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 9, brush);
        context.DrawText(txt, new Point(tip.X + 2, tip.Y - 5));
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
        yield return ("DM-301", new Vec3(5.8, 2.3, 0.8));
        yield return ("FC-401", new Vec3(-5.8, 3.6, 3.6));
        yield return ("CF-501", new Vec3(5.6, 2.0, 4.4));
        yield return ("SL-601", new Vec3(-8.2, 4.2, 1.6));
    }

    private void AddSubdividedConveyorX(List<Face3D> faces, double x0, double x1, double y0, double y1, double z, double width, int segs, Color color)
    {
        for (int i = 0; i < segs; i++)
        {
            double sx = x0 + (x1 - x0) * (i + 0.5) / segs;
            double sy = y0 + (y1 - y0) * (i + 0.5) / segs;
            AddBox(faces, new Vec3(sx, sy, z), (x1 - x0) / segs - 0.04, 0.38, width, color);
        }
        AddBox(faces, new Vec3(x0 + 0.3, y0 * 0.5, z), 0.2, y0, 0.2, Color.Parse("#475569"));
        AddBox(faces, new Vec3(x1 - 0.3, y1 * 0.5, z), 0.2, y1, 0.2, Color.Parse("#475569"));
    }

    private void AddSubdividedConveyorZ(List<Face3D> faces, double x, double z0, double z1, double y0, double y1, double width, int segs, Color color)
    {
        for (int i = 0; i < segs; i++)
        {
            double sz = z0 + (z1 - z0) * (i + 0.5) / segs;
            double sy = y0 + (y1 - y0) * (i + 0.5) / segs;
            AddBox(faces, new Vec3(x, sy, sz), width, 0.36, (z1 - z0) / segs - 0.04, color);
        }
        AddBox(faces, new Vec3(x, y1 * 0.5, z1 - 0.3), 0.2, y1, 0.2, Color.Parse("#475569"));
    }

    private void AddFace(List<Face3D> faces, Vec3[] pts, Color color)
    {
        var v1 = Sub(pts[1], pts[0]);
        var v2 = Sub(pts[2], pts[0]);
        faces.Add(new Face3D(pts, color, AvgDepth(pts), Normalize(Cross(v1, v2))));
    }

    private void AddBox(List<Face3D> faces, Vec3 center, double sx, double sy, double sz, Color color)
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
            AddFace(faces, new[] { v[q[0]], v[q[1]], v[q[2]], v[q[3]] }, color);
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
            AddFace(faces, new[] { v[q[0]], v[q[1]], v[q[2]], v[q[3]] }, color);
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
            AddFace(faces, new[] { v[q[0]], v[q[1]], v[q[2]], v[q[3]] }, color);
    }

    private void AddCylinder(List<Face3D> faces, Vec3 baseCenter, double radius, double height, int segs, Color color)
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
            AddFace(faces, new[] { b0, b1, t1, t0 }, color);
        }
        AddFace(faces, topPts, color);
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

        double cy = Math.Cos(_yaw), sy = Math.Sin(_yaw);
        double x1 = x * cy - z * sy;
        double z1 = x * sy + z * cy;

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
        if (Effects?.IsOrthographic == true)
        {
            double orthoScale = Math.Min(w, h) * 0.055 * _zoom;
            screen = new Point(w * 0.5 + cam.X * orthoScale, h * 0.52 - cam.Y * orthoScale);
            return true;
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
