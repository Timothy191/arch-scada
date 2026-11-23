using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Controls;

/// <summary>
/// Interactive 3D CHPP Digital Twin viewport:
/// - Primary: Hardware-accelerated OpenGL 3.3/ES3 renderer (Plant3DOpenGlCanvas) loading Blender .glb meshes with true Z-buffer
/// - Fallback: High-Fidelity Pure-Avalonia software renderer (foundation pads, orthogonal 3D pipes, live fill levels, telemetry-coupled flow)
/// - Unified HUD: Direct 3D raycast picking &amp; hover tooltips, decluttered labels for all 14 assets, axis gizmo, and lifecycle-aware animation
/// </summary>
public sealed class Plant3DViewportControl : Panel
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

    public bool IsHardwareAccelerated => _glCanvas.IsHardwareActive;

    private readonly Plant3DOpenGlCanvas _glCanvas;
    private readonly HudOverlayControl _overlay;
    private readonly DispatcherTimer _animTimer;
    private readonly List<(string Id, Rect Bounds)> _hitRects = new();

    private double _time;
    private double _yaw = -0.62;
    private double _pitch = 0.52;
    private double _zoom = 1.05;
    private double _targetX;
    private double _targetZ = 0.5;

    private double _goalYaw = -0.62;
    private double _goalPitch = 0.52;
    private double _goalZoom = 1.05;
    private double _goalTargetX;
    private double _goalTargetZ = 0.5;

    private Point? _pressOrigin;
    private Point? _lastPointer;
    private Point? _hoverPointer;
    private string? _hoveredId;
    private bool _isPanning;
    private bool _didDrag;

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
        Background = new SolidColorBrush(Color.Parse("#080E1B"));

        _glCanvas = new Plant3DOpenGlCanvas { IsHitTestVisible = false };
        _overlay = new HudOverlayControl(this) { IsHitTestVisible = false };

        Children.Add(_glCanvas);
        Children.Add(_overlay);

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animTimer.Tick += (_, _) =>
        {
            if (!IsEffectivelyVisible || Bounds.Width < 40 || Bounds.Height < 40)
                return;

            _time += 0.033;
            if (Effects?.AutoOrbit == true && _lastPointer is null)
            {
                _goalYaw += 0.0045;
            }
            StepCameraSmoothing();
            SyncCanvasState();
            _glCanvas.RequestNextFrameRendering();
            _overlay.InvalidateVisual();
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncCanvasState();
        _animTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animTimer.Stop();
        base.OnDetachedFromVisualTree(e);
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
        else if (change.Property == EquipmentProperty || change.Property == EffectsProperty || change.Property == SelectedEquipmentProperty)
        {
            SyncCanvasState();
            _glCanvas.RequestNextFrameRendering();
            _overlay.InvalidateVisual();
        }
    }

    public void FocusOnEquipment(string equipmentId)
    {
        foreach (var p in Plant3DSceneData.Placements)
        {
            if (string.Equals(p.Id, equipmentId, StringComparison.OrdinalIgnoreCase))
            {
                _goalTargetX = p.Position.X;
                _goalTargetZ = p.Position.Z;
                _goalZoom = 1.68;
                _goalPitch = 0.48;
                SyncCanvasState();
                _glCanvas.RequestNextFrameRendering();
                _overlay.InvalidateVisual();
                return;
            }
        }
    }

    private void SyncCanvasState()
    {
        _glCanvas.CameraYaw = _yaw;
        _glCanvas.CameraPitch = _pitch;
        _glCanvas.CameraZoom = _zoom;
        _glCanvas.TargetX = _targetX;
        _glCanvas.TargetZ = _targetZ;
        _glCanvas.SimTime = _time;
        _glCanvas.Equipment = Equipment;
        _glCanvas.Effects = Effects;
        _glCanvas.SelectedEquipmentId = SelectedEquipment?.Id;
        _glCanvas.HoveredEquipmentId = _hoveredId;
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
        SyncCanvasState();
        _glCanvas.RequestNextFrameRendering();
        _overlay.InvalidateVisual();
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
        var cur = e.GetPosition(this);
        _hoverPointer = cur;

        var props = e.GetCurrentPoint(this).Properties;
        if (_lastPointer is not Point prev || (!props.IsLeftButtonPressed && !props.IsRightButtonPressed && !props.IsMiddleButtonPressed))
        {
            // Direct 3D hover picking
            var newHover = HitTestEquipmentAt(cur);
            if (!string.Equals(newHover, _hoveredId, StringComparison.OrdinalIgnoreCase))
            {
                _hoveredId = newHover;
                SyncCanvasState();
                _glCanvas.RequestNextFrameRendering();
                _overlay.InvalidateVisual();
            }
            return;
        }

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
        SyncCanvasState();
        _glCanvas.RequestNextFrameRendering();
        _overlay.InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverPointer = null;
        if (_hoveredId is not null)
        {
            _hoveredId = null;
            SyncCanvasState();
            _overlay.InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_didDrag && !_isPanning)
        {
            TrySelectAt(e.GetPosition(this));
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
        SyncCanvasState();
        _glCanvas.RequestNextFrameRendering();
        _overlay.InvalidateVisual();
    }

    private string? HitTestEquipmentAt(Point p)
    {
        // 1. Check floating HUD labels first (exact click on tag)
        for (int i = _hitRects.Count - 1; i >= 0; i--)
        {
            if (_hitRects[i].Bounds.Contains(p))
                return _hitRects[i].Id;
        }

        // 2. Direct 3D Ray-AABB picking across all 14 plant assemblies
        if (Bounds.Width < 40 || Bounds.Height < 40) return null;
        var fx = Effects ?? new Unity3DEffectSettings();
        var (view, proj, _) = Plant3DSceneData.BuildCameraMatrices(
            _yaw, _pitch, _zoom, _targetX, _targetZ, Bounds.Width, Bounds.Height, fx.IsOrthographic);
        var ray = Plant3DSceneData.UnprojectRay(p, view * proj, Bounds.Width, Bounds.Height);
        return Plant3DSceneData.PickEquipment(ray, Plant3DSceneData.LoadAllGlbMeshes());
    }

    private void TrySelectAt(Point p)
    {
        if (Equipment is null) return;
        var hitId = HitTestEquipmentAt(p);
        if (hitId is null) return;

        var match = Equipment.FirstOrDefault(eq => string.Equals(eq.Id, hitId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedEquipment = match;
            SyncCanvasState();
            _glCanvas.RequestNextFrameRendering();
            _overlay.InvalidateVisual();
        }
    }

    /// <summary>
    /// Layered 2D HUD + High-Fidelity Pure-Avalonia software fallback renderer when OpenGL is unavailable.
    /// </summary>
    private sealed class HudOverlayControl : Control
    {
        private readonly Plant3DViewportControl _owner;
        private readonly Dictionary<uint, SolidColorBrush> _brushCache = new();

        private static readonly LinearGradientBrush FallbackBgBrush = new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#060A12"), 0),
                new GradientStop(Color.Parse("#0B1324"), 1)
            }
        };

        private static readonly Pen FaceEdgePen = new(new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)), 0.55);
        private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(38, 56, 189, 248)), 1);
        private static readonly SolidColorBrush LabelBgBrush = new(Color.FromArgb(198, 15, 23, 42));
        private static readonly SolidColorBrush SelectedLabelBgBrush = new(Color.FromArgb(232, 12, 74, 110));
        private static readonly SolidColorBrush HoverLabelBgBrush = new(Color.FromArgb(220, 30, 41, 59));
        private static readonly Pen SelectedLabelPen = new(new SolidColorBrush(Color.Parse("#38BDF8")), 1.5);
        private static readonly Pen HoverLabelPen = new(new SolidColorBrush(Color.Parse("#93C5FD")), 1.1);
        private static readonly Typeface LabelTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
        private static readonly Typeface HudTypeface = new("Inter");

        private readonly record struct SoftTri(Point A, Point B, Point C, Color Fill, float Depth);

        public HudOverlayControl(Plant3DViewportControl owner) => _owner = owner;

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

            var fx = _owner.Effects ?? new Unity3DEffectSettings();
            var eqMap = (_owner.Equipment ?? Array.Empty<EquipmentItem>()).ToDictionary(e => e.Id, e => e, StringComparer.OrdinalIgnoreCase);

            var (view, proj, camPos) = Plant3DSceneData.BuildCameraMatrices(
                _owner._yaw, _owner._pitch, _owner._zoom, _owner._targetX, _owner._targetZ, w, h, fx.IsOrthographic);
            var viewProj = view * proj;

            // If hardware OpenGL is not active (e.g., headless tests or software VM), render the High-Fidelity Pure-Avalonia 3D fallback
            if (!_owner._glCanvas.IsHardwareActive)
            {
                RenderSoftwareFallback(context, w, h, viewProj, camPos, fx, eqMap);
            }

            // Always render crisp 2D HUD overlay on top
            DrawDeclutteredLabels(context, w, h, viewProj, eqMap);
            DrawHoverTooltip(context, w, h, eqMap);
            DrawAxisGizmo(context, w);
            DrawHudFooter(context, w, h, fx);
        }

        private void RenderSoftwareFallback(
            DrawingContext context, double w, double h,
            Matrix4x4 viewProj, Vector3 camPos,
            Unity3DEffectSettings fx, Dictionary<string, EquipmentItem> eqMap)
        {
            context.FillRectangle(FallbackBgBrush, new Rect(0, 0, w, h), 6);

            // Ground Grid
            for (int i = -12; i <= 12; i += 2)
            {
                if (Plant3DSceneData.TryProjectToScreen(new(i, 0, -8), viewProj, w, h, out var a, out _) &&
                    Plant3DSceneData.TryProjectToScreen(new(i, 0, 8), viewProj, w, h, out var b, out _))
                    context.DrawLine(GridPen, a, b);
            }
            for (int j = -8; j <= 8; j += 2)
            {
                if (Plant3DSceneData.TryProjectToScreen(new(-12, 0, j), viewProj, w, h, out var c, out _) &&
                    Plant3DSceneData.TryProjectToScreen(new(12, 0, j), viewProj, w, h, out var d, out _))
                    context.DrawLine(GridPen, c, d);
            }

            // Concrete Foundation Pads
            foreach (var pad in Plant3DSceneData.FoundationPads)
            {
                DrawProjectedQuad(context, viewProj, w, h,
                    new Vector3(pad.Center.X - pad.Size.X * 0.5f, 0.01f, pad.Center.Z - pad.Size.Z * 0.5f),
                    new Vector3(pad.Center.X + pad.Size.X * 0.5f, 0.01f, pad.Center.Z - pad.Size.Z * 0.5f),
                    new Vector3(pad.Center.X + pad.Size.X * 0.5f, 0.01f, pad.Center.Z + pad.Size.Z * 0.5f),
                    new Vector3(pad.Center.X - pad.Size.X * 0.5f, 0.01f, pad.Center.Z + pad.Size.Z * 0.5f),
                    GetBrush(28, 37, 52, 200),
                    new Pen(GetBrush(pad.AccentColor.R, pad.AccentColor.G, pad.AccentColor.B, 140), 1.4));
            }

            // Orthogonal 3D Pipes
            foreach (var route in Plant3DSceneData.PipeRoutes)
            {
                var c = Plant3DSceneData.GetMediumColor(route.Medium);
                var pipePen = new Pen(GetBrush(c.R, c.G, c.B, 165), 2.4);
                for (int i = 0; i < route.Waypoints.Length - 1; i++)
                {
                    if (Plant3DSceneData.TryProjectToScreen(route.Waypoints[i], viewProj, w, h, out var p0, out _) &&
                        Plant3DSceneData.TryProjectToScreen(route.Waypoints[i + 1], viewProj, w, h, out var p1, out _))
                    {
                        context.DrawLine(pipePen, p0, p1);
                    }
                }
            }

            // Project loaded GLB meshes (depth-sorted triangles with directional + hemisphere lighting)
            var meshes = Plant3DSceneData.LoadAllGlbMeshes();
            var lightDir = Vector3.Normalize(new Vector3(0.48f, 0.82f, 0.34f));
            var tris = new List<SoftTri>(2048);

            foreach (var p in Plant3DSceneData.Placements)
            {
                eqMap.TryGetValue(p.Id, out var eq);
                float vibY = (p.Vibrates && fx.EnableVibrationPhysics && eq is not null && eq.Status != EquipmentStatus.Stopped)
                    ? MathF.Sin((float)(_owner._time * 28.0)) * (0.018f + 0.025f * (float)(eq.LoadPercent / 100.0))
                    : 0f;

                var model = Matrix4x4.CreateScale(p.Scale)
                          * Matrix4x4.CreateRotationY(p.YawDegrees * (MathF.PI / 180f))
                          * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + vibY, p.Position.Z);

                var tint = Plant3DSceneData.GetStatusTint(p, eq, fx);
                float tintMix = fx.ShowThermalHeatmap ? 0.78f
                    : (eq?.Status is EquipmentStatus.Fault or EquipmentStatus.Warning or EquipmentStatus.Stopped ? 0.58f : 0.10f);

                if (meshes.TryGetValue(p.GlbFileName, out var mesh))
                {
                    var verts = mesh.Vertices;
                    var idx = mesh.Indices;
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        int i0 = (int)idx[i] * GlbMeshData.FloatsPerVertex;
                        int i1 = (int)idx[i + 1] * GlbMeshData.FloatsPerVertex;
                        int i2 = (int)idx[i + 2] * GlbMeshData.FloatsPerVertex;

                        var w0 = Vector3.Transform(new Vector3(verts[i0], verts[i0 + 1], verts[i0 + 2]), model);
                        var w1 = Vector3.Transform(new Vector3(verts[i1], verts[i1 + 1], verts[i1 + 2]), model);
                        var w2 = Vector3.Transform(new Vector3(verts[i2], verts[i2 + 1], verts[i2 + 2]), model);

                        if (!Plant3DSceneData.TryProjectToScreen(w0, viewProj, w, h, out var s0, out float d0) ||
                            !Plant3DSceneData.TryProjectToScreen(w1, viewProj, w, h, out var s1, out float d1) ||
                            !Plant3DSceneData.TryProjectToScreen(w2, viewProj, w, h, out var s2, out float d2))
                            continue;

                        // Backface cull in screen space
                        double cross = (s1.X - s0.X) * (s2.Y - s0.Y) - (s1.Y - s0.Y) * (s2.X - s0.X);
                        if (cross >= 0) continue;

                        var n = Vector3.Normalize(Vector3.Cross(w1 - w0, w2 - w0));
                        float ndotl = MathF.Max(0f, Vector3.Dot(n, lightDir));
                        float shade = 0.42f + 0.58f * ndotl;

                        float br = verts[i0 + 6] * (1f - tintMix) + (tint.R / 255f) * tintMix;
                        float bg = verts[i0 + 7] * (1f - tintMix) + (tint.G / 255f) * tintMix;
                        float bb = verts[i0 + 8] * (1f - tintMix) + (tint.B / 255f) * tintMix;

                        byte r = (byte)Math.Clamp(br * shade * 255f, 0, 255);
                        byte g = (byte)Math.Clamp(bg * shade * 255f, 0, 255);
                        byte b = (byte)Math.Clamp(bb * shade * 255f, 0, 255);

                        tris.Add(new SoftTri(s0, s1, s2, Color.FromRgb(r, g, b), (d0 + d1 + d2) / 3f));
                    }
                }
            }

            tris.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            foreach (var t in tris)
            {
                var geom = new StreamGeometry();
                using (var gctx = geom.Open())
                {
                    gctx.BeginFigure(t.A, true);
                    gctx.LineTo(t.B);
                    gctx.LineTo(t.C);
                    gctx.EndFigure(true);
                }
                context.DrawGeometry(GetBrush(t.Fill.R, t.Fill.G, t.Fill.B), FaceEdgePen, geom);
            }

            // Telemetry-coupled flow particles
            if (fx.ShowFlowParticles)
            {
                foreach (var route in Plant3DSceneData.PipeRoutes)
                {
                    eqMap.TryGetValue(route.SourceEquipmentId, out var srcEq);
                    double speed = Plant3DSceneData.GetFlowSpeedFactor(srcEq);
                    if (speed <= 0.0) continue;

                    var c = Plant3DSceneData.GetMediumColor(route.Medium);
                    var brush = GetBrush(c.R, c.G, c.B);
                    for (int i = 0; i < 6; i++)
                    {
                        float t = (float)((_owner._time * 0.28 * speed + i / 6.0) % 1.0);
                        var pos = Plant3DSceneData.SamplePolyline(route.Waypoints, t);
                        if (Plant3DSceneData.TryProjectToScreen(pos, viewProj, w, h, out var pt, out _))
                            context.DrawEllipse(brush, null, pt, 3.2, 3.2);
                    }
                }
            }

            // Alarm halos & selection ring
            double pulse = 0.55 + 0.45 * Math.Sin(_owner._time * 4.5);
            foreach (var p in Plant3DSceneData.Placements)
            {
                eqMap.TryGetValue(p.Id, out var eq);
                float rad = MathF.Max(p.HalfExtents.X, p.HalfExtents.Z) + 0.3f;
                if (fx.ShowAlarmHalos && eq is not null && eq.Status is EquipmentStatus.Warning or EquipmentStatus.Fault)
                {
                    var ac = eq.Status == EquipmentStatus.Fault ? Color.Parse("#EF4444") : Color.Parse("#F59E0B");
                    DrawProjectedRing(context, viewProj, w, h, p.Position with { Y = 0.12f }, rad + (float)(0.2 * pulse),
                        new Pen(GetBrush(ac.R, ac.G, ac.B, (byte)(220 * pulse)), 2.4));
                }
                if (string.Equals(_owner.SelectedEquipment?.Id, p.Id, StringComparison.OrdinalIgnoreCase))
                {
                    DrawProjectedRing(context, viewProj, w, h, p.Position with { Y = 0.14f }, rad + 0.18f, SelectedLabelPen);
                }
            }
        }

        private static void DrawProjectedQuad(
            DrawingContext context, Matrix4x4 vp, double w, double h,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, IBrush fill, Pen? pen)
        {
            if (!Plant3DSceneData.TryProjectToScreen(p0, vp, w, h, out var s0, out _) ||
                !Plant3DSceneData.TryProjectToScreen(p1, vp, w, h, out var s1, out _) ||
                !Plant3DSceneData.TryProjectToScreen(p2, vp, w, h, out var s2, out _) ||
                !Plant3DSceneData.TryProjectToScreen(p3, vp, w, h, out var s3, out _))
                return;

            var geom = new StreamGeometry();
            using (var gctx = geom.Open())
            {
                gctx.BeginFigure(s0, true);
                gctx.LineTo(s1);
                gctx.LineTo(s2);
                gctx.LineTo(s3);
                gctx.EndFigure(true);
            }
            context.DrawGeometry(fill, pen, geom);
        }

        private static void DrawProjectedRing(
            DrawingContext context, Matrix4x4 vp, double w, double h,
            Vector3 center, float radius, Pen pen)
        {
            Point? prev = null;
            for (int i = 0; i <= 20; i++)
            {
                float ang = i * MathF.PI * 2f / 20f;
                var pt3 = new Vector3(center.X + MathF.Cos(ang) * radius, center.Y, center.Z + MathF.Sin(ang) * radius);
                if (Plant3DSceneData.TryProjectToScreen(pt3, vp, w, h, out var pt, out _))
                {
                    if (prev.HasValue) context.DrawLine(pen, prev.Value, pt);
                    prev = pt;
                }
            }
        }

        private void DrawDeclutteredLabels(
            DrawingContext context, double w, double h,
            Matrix4x4 viewProj, Dictionary<string, EquipmentItem> eqMap)
        {
            _owner._hitRects.Clear();
            var placed = new List<Rect>(Plant3DSceneData.Placements.Count);

            // Prioritize selected, hovered, and alarmed equipment so they always win overlap resolution
            var ordered = Plant3DSceneData.Placements
                .OrderByDescending(p => string.Equals(_owner.SelectedEquipment?.Id, p.Id, StringComparison.OrdinalIgnoreCase) ? 3
                    : string.Equals(_owner._hoveredId, p.Id, StringComparison.OrdinalIgnoreCase) ? 2
                    : (eqMap.TryGetValue(p.Id, out var e) && e.Status is EquipmentStatus.Fault or EquipmentStatus.Warning) ? 1 : 0);

            foreach (var p in ordered)
            {
                var anchor = new Vector3(p.Position.X, p.Position.Y + p.LabelHeight, p.Position.Z);
                if (!Plant3DSceneData.TryProjectToScreen(anchor, viewProj, w, h, out var pt, out _))
                    continue;

                bool isSelected = string.Equals(_owner.SelectedEquipment?.Id, p.Id, StringComparison.OrdinalIgnoreCase);
                bool isHovered = string.Equals(_owner._hoveredId, p.Id, StringComparison.OrdinalIgnoreCase);
                bool showDetail = isSelected || isHovered || _owner._zoom >= 1.25;

                string labelText = showDetail && eqMap.TryGetValue(p.Id, out var eq)
                    ? $"{p.Id} · {eq.PrimaryMetricDisplay}"
                    : p.Id;

                var text = new FormattedText(labelText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 10.5, Brushes.White);
                var bg = new Rect(pt.X - 6, pt.Y - 10, text.Width + 12, text.Height + 5);

                // Nudge upward if colliding with a higher-priority label
                int nudges = 0;
                while (nudges < 3 && placed.Any(r => r.Intersects(bg)))
                {
                    bg = new Rect(bg.X, bg.Y - (bg.Height + 3), bg.Width, bg.Height);
                    nudges++;
                }

                placed.Add(bg);
                _owner._hitRects.Add((p.Id, bg));

                var bgBrush = isSelected ? SelectedLabelBgBrush : isHovered ? HoverLabelBgBrush : LabelBgBrush;
                var borderPen = isSelected ? SelectedLabelPen : isHovered ? HoverLabelPen : null;

                context.DrawRectangle(bgBrush, borderPen, bg, 4);
                context.DrawText(text, new Point(bg.X + 6, bg.Y + 2));
            }
        }

        private void DrawHoverTooltip(DrawingContext context, double w, double h, Dictionary<string, EquipmentItem> eqMap)
        {
            if (_owner._hoveredId is null || _owner._hoverPointer is not Point ptr)
                return;
            if (!eqMap.TryGetValue(_owner._hoveredId, out var eq))
                return;

            string line1 = $"{eq.Id} — {eq.Name}";
            string line2 = $"{eq.Status}  |  {eq.PrimaryMetricDisplay}  |  Load {eq.LoadPercent:0}%  |  {eq.PowerKw:0.0} kW";

            var t1 = new FormattedText(line1, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 11.5, Brushes.White);
            var t2 = new FormattedText(line2, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 10.5, GetBrush(148, 163, 184));

            double boxW = Math.Max(t1.Width, t2.Width) + 20;
            double boxH = t1.Height + t2.Height + 16;
            double x = Math.Clamp(ptr.X + 14, 10, Math.Max(10, w - boxW - 10));
            double y = Math.Clamp(ptr.Y - boxH - 10, 10, Math.Max(10, h - boxH - 30));

            var rect = new Rect(x, y, boxW, boxH);
            context.DrawRectangle(GetBrush(10, 18, 34, 238), SelectedLabelPen, rect, 6);
            context.DrawText(t1, new Point(x + 10, y + 6));
            context.DrawText(t2, new Point(x + 10, y + 8 + t1.Height));
        }

        private void DrawAxisGizmo(DrawingContext context, double w)
        {
            var origin = new Point(w - 46, 46);
            double len = 22;
            DrawAxisVec(context, origin, new Vector3(1, 0, 0), len, GetBrush(239, 68, 68), "X");
            DrawAxisVec(context, origin, new Vector3(0, 1, 0), len, GetBrush(34, 197, 94), "Y");
            DrawAxisVec(context, origin, new Vector3(0, 0, 1), len, GetBrush(56, 189, 248), "Z");
        }

        private void DrawAxisVec(DrawingContext context, Point origin, Vector3 axis, double len, IBrush brush, string label)
        {
            double cy = Math.Cos(_owner._yaw), sy = Math.Sin(_owner._yaw);
            double x1 = axis.X * cy - axis.Z * sy;
            double z1 = axis.X * sy + axis.Z * cy;
            double cp = Math.Cos(_owner._pitch), sp = Math.Sin(_owner._pitch);
            double y2 = axis.Y * cp - z1 * sp;

            var tip = new Point(origin.X + x1 * len, origin.Y - y2 * len);
            context.DrawLine(new Pen(brush, 1.8), origin, tip);
            var txt = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 9, brush);
            context.DrawText(txt, new Point(tip.X + 2, tip.Y - 5));
        }

        private void DrawHudFooter(DrawingContext context, double w, double h, Unity3DEffectSettings fx)
        {
            string mode = _owner._glCanvas.IsHardwareActive
                ? $"GPU OpenGL · {_owner._glCanvas.LoadedGlbCount} GLB"
                : $"Software 3D · {Plant3DSceneData.LoadAllGlbMeshes().Count} GLB";

            var badgeTxt = new FormattedText(mode, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 10, GetBrush(56, 189, 248));
            var badgeRect = new Rect(12, 10, badgeTxt.Width + 14, badgeTxt.Height + 6);
            context.DrawRectangle(LabelBgBrush, HoverLabelPen, badgeRect, 4);
            context.DrawText(badgeTxt, new Point(19, 13));

            string projMode = fx.IsOrthographic ? "ORTHO" : "PERSP";
            var hint = new FormattedText(
                $"[{projMode}] Click Machine: Select  |  Left-Drag: Orbit  |  Right-Drag / WASD: Pan  |  Scroll: Zoom  |  F: Focus  |  R: Reset",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 10.5, GetBrush(148, 163, 184));
            context.DrawText(hint, new Point(12, h - 22));
        }
    }
}
