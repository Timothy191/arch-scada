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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;

namespace ArchScada.Desktop.Controls;

/// <summary>
/// Interactive 3D CHPP Digital Twin viewport:
/// - Primary: Hardware-accelerated OpenGL 3.3/ES3 renderer (Plant3DOpenGlCanvas) loading Blender .glb meshes + procedural conveyors
/// - Fallback: High-Fidelity Pure-Avalonia software renderer with full conveyor, loader, and crusher animation parity
/// - Unified HUD: Direct 3D raycast picking &amp; hover tooltips for all 29 machines &amp; conveyors, decluttered labels, axis gizmo
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

    public static readonly StyledProperty<Plant3DNode?> Selected3DNodeProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, Plant3DNode?>(
            nameof(Selected3DNode),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> FocusTargetIdProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, string?>(nameof(FocusTargetId));

    public static readonly StyledProperty<string> TelemetrySourceBadgeProperty =
        AvaloniaProperty.Register<Plant3DViewportControl, string>(
            nameof(TelemetrySourceBadge),
            "SIMULATED DEMO TELEMETRY · READ-ONLY (No PLC Write)");

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

    public Plant3DNode? Selected3DNode
    {
        get => GetValue(Selected3DNodeProperty);
        set => SetValue(Selected3DNodeProperty, value);
    }

    public string? FocusTargetId
    {
        get => GetValue(FocusTargetIdProperty);
        set => SetValue(FocusTargetIdProperty, value);
    }

    public string TelemetrySourceBadge
    {
        get => GetValue(TelemetrySourceBadgeProperty);
        set => SetValue(TelemetrySourceBadgeProperty, value);
    }

    public bool IsHardwareAccelerated => _glCanvas.IsHardwareActive;
    public int LastFrameDrawCalls => _glCanvas.LastFrameDrawCalls;
    public bool FreezeSimulationTime { get; set; }
    public bool CaptureOpenGlFrame
    {
        get => _glCanvas.CaptureRequested;
        set => _glCanvas.CaptureRequested = value;
    }
    internal Control OverlayControl => _overlay;

    private readonly Plant3DOpenGlCanvas _glCanvas;
    private readonly HudOverlayControl _overlay;
    private readonly DispatcherTimer _animTimer;
    private readonly List<(string Id, Rect Bounds)> _hitRects = new();

    private double _time;
    private double _yaw = -0.58;
    private double _pitch = 0.56;
    private double _zoom = 0.68;
    private double _targetX = -2.0;
    private double _targetZ = -3.0;

    private double _goalYaw = -0.58;
    private double _goalPitch = 0.56;
    private double _goalZoom = 0.68;
    private double _goalTargetX = -2.0;
    private double _goalTargetZ = -3.0;

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
            Selected3DNodeProperty,
            FocusTargetIdProperty,
            TelemetrySourceBadgeProperty);
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

            if (!FreezeSimulationTime)
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
        else if (change.Property == EquipmentProperty ||
                 change.Property == EffectsProperty ||
                 change.Property == SelectedEquipmentProperty ||
                 change.Property == Selected3DNodeProperty ||
                 change.Property == TelemetrySourceBadgeProperty)
        {
            SyncCanvasState();
            _glCanvas.RequestNextFrameRendering();
            _overlay.InvalidateVisual();
        }
    }

    public void SnapCameraImmediate(double? simTimeOverride = null)
    {
        _yaw = _goalYaw;
        _pitch = _goalPitch;
        _zoom = _goalZoom;
        _targetX = _goalTargetX;
        _targetZ = _goalTargetZ;
        if (simTimeOverride.HasValue)
            _time = simTimeOverride.Value;
        if (FreezeSimulationTime)
        {
            _hoverPointer = null;
            _hoveredId = null;
        }

        SyncCanvasState();
        _glCanvas.RequestNextFrameRendering();
        _overlay.InvalidateVisual();
    }

    public WriteableBitmap? GetLastOpenGlBitmap() => _glCanvas.CaptureLastFrameBitmap();

    public void FocusOnEquipment(string equipmentId)
    {
        var node = Plant3DSceneData.FindNode(equipmentId);
        if (node is null) return;

        var pos = node.Position;
        if (node.Id == "FEL-101")
        {
            bool active = Effects?.AnimateConveyors ?? true;
            pos = Plant3DSceneData.ComputeLoaderPose(_time, active).ChassisPosition;
        }

        _goalTargetX = pos.X;
        _goalTargetZ = pos.Z;
        _goalZoom = (node.IsSubComponent || node.IsPipe) ? 1.65 : node.IsConveyor ? 1.28 : 1.56;
        _goalPitch = 0.48;
        SyncCanvasState();
        _glCanvas.RequestNextFrameRendering();
        _overlay.InvalidateVisual();
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
        _glCanvas.SelectedNodeId = Selected3DNode?.Id ?? SelectedEquipment?.Id;
        _glCanvas.HoveredNodeId = _hoveredId;
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
            case "Top-Down GA Layout":
            case "Top-Down Schematic":
                _goalYaw = 0.0; _goalPitch = 1.48; _goalZoom = 0.42; _goalTargetX = -1.6; _goalTargetZ = -0.2;
                break;
            case "Front View (South)":
                _goalYaw = 0.0; _goalPitch = 0.24; _goalZoom = 0.72; _goalTargetX = -4.5; _goalTargetZ = 2.0;
                break;
            case "Side View (West)":
                _goalYaw = -1.57; _goalPitch = 0.24; _goalZoom = 0.78; _goalTargetX = -13.5; _goalTargetZ = 8.4;
                break;
            case "Stage 1: Loader & Feeder":
                _goalYaw = -1.16; _goalPitch = 0.34; _goalZoom = 0.94; _goalTargetX = -13.8; _goalTargetZ = 10.6;
                break;
            case "Stage 2–3: Jaw Crusher":
                _goalYaw = 0.92; _goalPitch = 0.42; _goalZoom = 1.16; _goalTargetX = -12.8; _goalTargetZ = 1.8;
                break;
            case "Stage 4–5: Double Roll Crusher":
                _goalYaw = -0.35; _goalPitch = 0.46; _goalZoom = 1.48; _goalTargetX = -5.2; _goalTargetZ = -0.8;
                break;
            case "Stage 6: Central Sizing Screen":
                _goalYaw = -0.48; _goalPitch = 0.48; _goalZoom = 1.48; _goalTargetX = 0.0; _goalTargetZ = 0.5;
                break;
            case "Wash Plant & Thickener":
                _goalYaw = -0.45; _goalPitch = 0.58; _goalZoom = 0.86; _goalTargetX = -0.8; _goalTargetZ = -11.8;
                break;
            default: // "Plant Overview"
                _goalYaw = -0.58; _goalPitch = 0.56; _goalZoom = 0.52; _goalTargetX = -1.8; _goalTargetZ = -1.2;
                break;
        }
        SyncCanvasState();
        _glCanvas.RequestNextFrameRendering();
        _overlay.InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = 0.90 / Math.Max(0.4, _zoom);
        switch (e.Key)
        {
            case Key.W or Key.Up:
                _goalTargetZ = Math.Clamp(_goalTargetZ - step * Math.Cos(_yaw), -20, 18);
                _goalTargetX = Math.Clamp(_goalTargetX - step * Math.Sin(_yaw), -18, 18);
                e.Handled = true;
                break;
            case Key.S or Key.Down:
                _goalTargetZ = Math.Clamp(_goalTargetZ + step * Math.Cos(_yaw), -20, 18);
                _goalTargetX = Math.Clamp(_goalTargetX + step * Math.Sin(_yaw), -18, 18);
                e.Handled = true;
                break;
            case Key.A or Key.Left:
                _goalTargetX = Math.Clamp(_goalTargetX - step * Math.Cos(_yaw), -18, 18);
                _goalTargetZ = Math.Clamp(_goalTargetZ + step * Math.Sin(_yaw), -20, 18);
                e.Handled = true;
                break;
            case Key.D or Key.Right:
                _goalTargetX = Math.Clamp(_goalTargetX + step * Math.Cos(_yaw), -18, 18);
                _goalTargetZ = Math.Clamp(_goalTargetZ - step * Math.Sin(_yaw), -20, 18);
                e.Handled = true;
                break;
            case Key.F:
                var focusId = Selected3DNode?.Id ?? SelectedEquipment?.Id;
                if (focusId is not null) FocusOnEquipment(focusId);
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
            var newHover = HitTestNodeAt(cur);
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
            double panScale = 0.020 / Math.Max(0.4, _zoom);
            double cy = Math.Cos(_yaw);
            double sy = Math.Sin(_yaw);
            _goalTargetX = Math.Clamp(_goalTargetX - (dx * cy + dy * sy) * panScale, -18, 18);
            _goalTargetZ = Math.Clamp(_goalTargetZ - (-dx * sy + dy * cy) * panScale, -20, 18);
        }
        else if (props.IsLeftButtonPressed)
        {
            _goalYaw += dx * 0.008;
            _goalPitch = Math.Clamp(_goalPitch + dy * 0.008, 0.12, 1.48);
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
        _goalZoom = Math.Clamp(_goalZoom + e.Delta.Y * 0.09, 0.40, 2.80);
        SyncCanvasState();
        _glCanvas.RequestNextFrameRendering();
        _overlay.InvalidateVisual();
    }

    private string? HitTestNodeAt(Point p)
    {
        for (int i = _hitRects.Count - 1; i >= 0; i--)
        {
            if (_hitRects[i].Bounds.Contains(p))
                return _hitRects[i].Id;
        }

        if (Bounds.Width < 40 || Bounds.Height < 40) return null;
        var fx = Effects ?? new Unity3DEffectSettings();
        var (view, proj, _) = Plant3DSceneData.BuildCameraMatrices(
            _yaw, _pitch, _zoom, _targetX, _targetZ, Bounds.Width, Bounds.Height, fx.IsOrthographic);
        var ray = Plant3DSceneData.UnprojectRay(p, view * proj, Bounds.Width, Bounds.Height);

        var loaderPose = Plant3DSceneData.ComputeLoaderPose(_time, fx.AnimateConveyors);
        return Plant3DSceneData.PickEquipment(ray, Plant3DSceneData.LoadAllGlbMeshes(), loaderPose.ChassisPosition);
    }

    private void TrySelectAt(Point p)
    {
        var hitId = HitTestNodeAt(p);
        if (hitId is null) return;

        var node = Plant3DSceneData.FindNode(hitId);
        if (node is not null)
        {
            Selected3DNode = node;
            if (Equipment is not null)
            {
                var match = Equipment.FirstOrDefault(eq => string.Equals(eq.Id, node.LinkedScadaId, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    SelectedEquipment = match;
            }
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

        private static readonly Pen FaceEdgePen = new(new SolidColorBrush(Color.FromArgb(44, 255, 255, 255)), 0.55);
        private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(36, 56, 189, 248)), 1);
        private static readonly SolidColorBrush LabelBgBrush = new(Color.FromArgb(198, 15, 23, 42));
        private static readonly SolidColorBrush SelectedLabelBgBrush = new(Color.FromArgb(235, 12, 74, 110));
        private static readonly SolidColorBrush HoverLabelBgBrush = new(Color.FromArgb(224, 30, 41, 59));
        private static readonly Pen SelectedLabelPen = new(new SolidColorBrush(Color.Parse("#38BDF8")), 1.6);
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

            var (view, proj, _) = Plant3DSceneData.BuildCameraMatrices(
                _owner._yaw, _owner._pitch, _owner._zoom, _owner._targetX, _owner._targetZ, w, h, fx.IsOrthographic);
            var viewProj = view * proj;
            var flowMap = Plant3DSceneData.EvaluateProcessFlows(eqMap, _owner._time);
            eqMap.TryGetValue("AF-001", out var afEq);
            bool loaderActive = fx.AnimateConveyors && (afEq is null || afEq.Status is not (EquipmentStatus.Stopped or EquipmentStatus.Fault or EquipmentStatus.Offline));
            var loaderPose = Plant3DSceneData.ComputeLoaderPose(_owner._time, loaderActive);

            if (!_owner._glCanvas.IsHardwareActive)
            {
                RenderSoftwareFallback(context, w, h, viewProj, fx, eqMap, flowMap, loaderPose);
            }

            if (fx.ShowEquipmentLabels)
            {
                DrawDeclutteredLabels(context, w, h, viewProj, eqMap, loaderPose);
            }
            else
            {
                _owner._hitRects.Clear();
            }

            DrawHoverTooltip(context, w, h, eqMap, flowMap);
            DrawAxisGizmo(context, w);
            DrawHudFooter(context, w, h, fx);
        }

        private void RenderSoftwareFallback(
            DrawingContext context, double w, double h,
            Matrix4x4 viewProj,
            Unity3DEffectSettings fx,
            Dictionary<string, EquipmentItem> eqMap,
            IReadOnlyDictionary<string, ProcessFlowState> flowMap,
            LoaderPose loaderPose)
        {
            context.FillRectangle(FallbackBgBrush, new Rect(0, 0, w, h), 6);

            // Ground Grid covering X in [-18, 14], Z in [-18, 16]
            for (int x = -18; x <= 14; x += 2)
            {
                if (Plant3DSceneData.TryProjectToScreen(new(x, 0, -18), viewProj, w, h, out var a, out _) &&
                    Plant3DSceneData.TryProjectToScreen(new(x, 0, 16), viewProj, w, h, out var b, out _))
                    context.DrawLine(GridPen, a, b);
            }
            for (int z = -18; z <= 16; z += 2)
            {
                if (Plant3DSceneData.TryProjectToScreen(new(-18, 0, z), viewProj, w, h, out var c, out _) &&
                    Plant3DSceneData.TryProjectToScreen(new(14, 0, z), viewProj, w, h, out var d, out _))
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
                    GetBrush(24, 32, 46, 200),
                    new Pen(GetBrush(pad.AccentColor.R, pad.AccentColor.G, pad.AccentColor.B, 140), 1.4));
            }

            // Orthogonal 3D Slurry / Magnetite / Water Pipes
            string? pipeSel = _owner.Selected3DNode?.Id ?? _owner.SelectedEquipment?.Id;
            foreach (var route in Plant3DSceneData.PipeRoutes)
            {
                var c = Plant3DSceneData.GetMediumColor(route.Medium);
                bool pipeActive = !string.IsNullOrWhiteSpace(route.Id) &&
                    (string.Equals(pipeSel, route.Id, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_owner._hoveredId, route.Id, StringComparison.OrdinalIgnoreCase));
                var pipePen = pipeActive
                    ? new Pen(GetBrush(56, 189, 248, 240), 5.0)
                    : new Pen(GetBrush(c.R, c.G, c.B, 175), 2.5);
                for (int i = 0; i < route.Waypoints.Length - 1; i++)
                {
                    if (Plant3DSceneData.TryProjectToScreen(route.Waypoints[i], viewProj, w, h, out var p0, out _) &&
                        Plant3DSceneData.TryProjectToScreen(route.Waypoints[i + 1], viewProj, w, h, out var p1, out _))
                    {
                        context.DrawLine(pipePen, p0, p1);
                    }
                }
            }

            // Project loaded GLB meshes (or fallback boxes)
            var meshes = Plant3DSceneData.LoadAllGlbMeshes();
            var lightDir = Vector3.Normalize(new Vector3(0.48f, 0.82f, 0.34f));
            var tris = new List<SoftTri>(4096);
            string? activeSelected = _owner.Selected3DNode?.Id ?? _owner.SelectedEquipment?.Id;

            foreach (var p in Plant3DSceneData.MachineNodes)
            {
                eqMap.TryGetValue(p.LinkedScadaId, out var eq);
                var flow = flowMap.GetValueOrDefault(p.Id);
                bool running = flow is not null ? flow.IsRunning : (eq is null || eq.Status != EquipmentStatus.Stopped);

                var tint = Plant3DSceneData.GetStatusTint(p, eq, fx);
                float tintMix = fx.ShowThermalHeatmap ? 0.78f
                    : (eq?.Status is EquipmentStatus.Fault or EquipmentStatus.Warning or EquipmentStatus.Stopped ? 0.58f : 0.10f);

                if (p.Id == "FEL-101")
                {
                    const float deg = MathF.PI / 180f;
                    var chassisMat = Matrix4x4.CreateRotationZ(loaderPose.PitchDegrees * deg)
                                   * Matrix4x4.CreateRotationY(loaderPose.YawDegrees * deg)
                                   * Matrix4x4.CreateTranslation(loaderPose.ChassisPosition);
                    if (meshes.TryGetValue("wheel_loader_chassis.glb", out var cMesh))
                        AppendMeshTris(cMesh, chassisMat, tint, tintMix, viewProj, w, h, lightDir, tris);
                    else
                        AppendBoxTris(Matrix4x4.CreateScale(2.3f, 1.05f, 1.45f) * Matrix4x4.CreateTranslation(-0.15f, 0.85f, 0f) * chassisMat, tint, viewProj, w, h, lightDir, tris);

                    var bucketMat = Matrix4x4.CreateRotationZ((loaderPose.BoomAngleDegrees + loaderPose.BucketDumpAngleDegrees * 0.45f) * deg)
                                  * Matrix4x4.CreateTranslation(1.30f, 1.38f, 0f)
                                  * chassisMat;
                    if (meshes.TryGetValue("wheel_loader_bucket.glb", out var bMesh))
                        AppendMeshTris(bMesh, bucketMat, tint, tintMix, viewProj, w, h, lightDir, tris);
                    else
                        AppendBoxTris(Matrix4x4.CreateScale(0.75f, 0.52f, 1.55f) * Matrix4x4.CreateTranslation(0.28f, -0.08f, 0f) * bucketMat, Color.Parse("#475569"), viewProj, w, h, lightDir, tris);
                    continue;
                }

                float vibY = (p.Vibrates && fx.EnableVibrationPhysics && running)
                    ? MathF.Sin((float)(_owner._time * 28.0)) * (0.018f + 0.024f * (float)((eq?.LoadPercent ?? 70) / 100.0))
                    : 0f;

                var model = Matrix4x4.CreateScale(p.Scale)
                          * Matrix4x4.CreateRotationY(p.YawDegrees * (MathF.PI / 180f))
                          * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + vibY, p.Position.Z);

                if (meshes.TryGetValue(p.GlbFileName, out var mesh))
                {
                    AppendMeshTris(mesh, model, tint, tintMix, viewProj, w, h, lightDir, tris);
                }
                else
                {
                    var fbMat = Matrix4x4.CreateScale(p.HalfExtents.X * 2f, p.HalfExtents.Y * 2f, p.HalfExtents.Z * 2f)
                              * Matrix4x4.CreateTranslation(p.Position.X, p.Position.Y + p.HalfExtents.Y + vibY, p.Position.Z);
                    AppendBoxTris(fbMat, tint, viewProj, w, h, lightDir, tris);
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

            // Procedural Conveyors (trestles, belt frame, transfer chutes, moving cleats, coal stream, flow arrows)
            foreach (var c in Plant3DSceneData.Conveyors)
            {
                var flow = flowMap.GetValueOrDefault(c.Id);
                double speed = (fx.AnimateConveyors && flow is not null) ? flow.NormalizedRate : 0.0;
                bool isSel = string.Equals(activeSelected, c.Id, StringComparison.OrdinalIgnoreCase);
                bool isHov = string.Equals(_owner._hoveredId, c.Id, StringComparison.OrdinalIgnoreCase);

                var delta = c.HeadPos - c.TailPos;
                float len = delta.Length();
                var dir = len > 1e-4f ? delta / len : Vector3.UnitX;
                var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY)) * (c.Width * 0.5f);

                // Trestle support legs down to Y=0
                var legPen = new Pen(GetBrush(71, 85, 105, 190), 1.6);
                int bents = Math.Max(2, (int)Math.Ceiling(len / 3.5f) + 1);
                for (int i = 0; i < bents; i++)
                {
                    float frac = Math.Clamp(0.10f + 0.80f * i / Math.Max(1, bents - 1), 0.08f, 0.92f);
                    var bp = Vector3.Lerp(c.TailPos, c.HeadPos, frac);
                    if (bp.Y > 0.2f &&
                        Plant3DSceneData.TryProjectToScreen(bp, viewProj, w, h, out var topPt, out _) &&
                        Plant3DSceneData.TryProjectToScreen(bp with { Y = 0f }, viewProj, w, h, out var botPt, out _))
                    {
                        context.DrawLine(legPen, topPt, botPt);
                    }
                }

                // Belt ribbon quad
                var beltColor = isSel ? Color.Parse("#38BDF8") : isHov ? Color.Parse("#7DD3FC") : Color.Parse("#334155");
                DrawProjectedQuad(context, viewProj, w, h,
                    c.TailPos - right, c.TailPos + right, c.HeadPos + right, c.HeadPos - right,
                    GetBrush(30, 41, 59, 215),
                    new Pen(GetBrush(beltColor.R, beltColor.G, beltColor.B), isSel ? 2.6 : 1.5));

                // Enclosed Head Transfer Chute
                var chuteBot = c.GetChuteBottomPos();
                if (Plant3DSceneData.TryProjectToScreen(c.HeadPos, viewProj, w, h, out var chTop, out _) &&
                    Plant3DSceneData.TryProjectToScreen(chuteBot, viewProj, w, h, out var chBot, out _))
                {
                    context.DrawLine(new Pen(GetBrush(100, 116, 139, 220), 6.0), chTop, chBot);
                }

                // Optional curved feed chute (SS-101 -> CV-106)
                if (c.FeedChuteWaypoints is { Length: >= 2 })
                {
                    var fcPen = new Pen(GetBrush(100, 116, 139, 210), 4.0);
                    for (int i = 0; i < c.FeedChuteWaypoints.Length - 1; i++)
                    {
                        if (Plant3DSceneData.TryProjectToScreen(c.FeedChuteWaypoints[i], viewProj, w, h, out var a, out _) &&
                            Plant3DSceneData.TryProjectToScreen(c.FeedChuteWaypoints[i + 1], viewProj, w, h, out var b, out _))
                            context.DrawLine(fcPen, a, b);
                    }
                }

                // Coal stream parcels + flow direction arrows
                if (flow is not null && flow.NormalizedRate > 0.02)
                {
                    var matCol = Plant3DSceneData.GetMaterialColor(c.MaterialClass);
                    var coalBrush = GetBrush(matCol.R, matCol.G, matCol.B);
                    var coalRimPen = new Pen(GetBrush(100, 116, 139, 175), 0.9);
                    double rPx = Math.Clamp(Plant3DSceneData.GetMaterialLumpSize(c.MaterialClass) * 18.0, 1.6, 6.2);
                    int count = Math.Max(6, (int)(len / 0.9f));
                    for (int i = 0; i < count; i++)
                    {
                        float t = (float)((_owner._time * 0.24 * Math.Max(0.2, speed) + (double)i / count) % 1.0);
                        var pos = Plant3DSceneData.SampleConveyorCoalTrajectory(c, t);
                        if (Plant3DSceneData.TryProjectToScreen(pos, viewProj, w, h, out var pt, out _))
                            context.DrawEllipse(coalBrush, coalRimPen, pt, rPx, rPx);
                    }

                    if (fx.ShowFlowDirection)
                    {
                        var arrowBrush = GetBrush(56, 189, 248);
                        float t = (float)((_owner._time * 0.45 * Math.Max(0.25, speed)) % 1.0);
                        var pA = Vector3.Lerp(c.TailPos, c.HeadPos, t) + new Vector3(0, 0.55f, 0);
                        var pB = Vector3.Lerp(c.TailPos, c.HeadPos, Math.Min(1f, t + 0.08f)) + new Vector3(0, 0.55f, 0);
                        if (Plant3DSceneData.TryProjectToScreen(pA, viewProj, w, h, out var sA, out _) &&
                            Plant3DSceneData.TryProjectToScreen(pB, viewProj, w, h, out var sB, out _))
                        {
                            context.DrawLine(new Pen(arrowBrush, 2.2), sA, sB);
                            context.DrawEllipse(arrowBrush, null, sB, 2.6, 2.6);
                        }
                    }
                }
            }

            // Loader Coal Dump Stream in Software Fallback
            if (loaderPose.IsDumpingCoal)
            {
                var dumpBrush = GetBrush(28, 30, 34);
                var dumpPen = new Pen(GetBrush(100, 116, 139, 175), 0.9);
                for (int i = 0; i < 6; i++)
                {
                    float frac = (float)((_owner._time * 2.8 + i / 6.0) % 1.0);
                    var p = Vector3.Lerp(new(-13.8f, 6.2f, 9.5f), new(-13.8f, 5.5f, 8.8f), frac);
                    if (Plant3DSceneData.TryProjectToScreen(p, viewProj, w, h, out var pt, out _))
                        context.DrawEllipse(dumpBrush, dumpPen, pt, 5.2, 5.2);
                }
            }

            // Pipe Flow Particles
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
                        float t = (float)((_owner._time * 0.30 * speed + i / 6.0) % 1.0);
                        var pos = Plant3DSceneData.SamplePolyline(route.Waypoints, t);
                        if (Plant3DSceneData.TryProjectToScreen(pos, viewProj, w, h, out var pt, out _))
                            context.DrawEllipse(brush, null, pt, 3.0, 3.0);
                    }
                }
            }

            // Alarm Halos & Selection Ring
            double pulse = 0.55 + 0.45 * Math.Sin(_owner._time * 4.5);
            foreach (var p in Plant3DSceneData.InteractiveNodes)
            {
                eqMap.TryGetValue(p.LinkedScadaId, out var eq);
                var pos = p.Id == "FEL-101" ? loaderPose.ChassisPosition : p.Position;
                float rad = p.IsConveyor ? 1.15f : p.IsPipe ? 0.55f : MathF.Max(p.HalfExtents.X, p.HalfExtents.Z) + 0.3f;
                if (!p.IsConveyor && !p.IsSubComponent && !p.IsPipe && fx.ShowAlarmHalos && eq is not null && eq.Status is EquipmentStatus.Warning or EquipmentStatus.Fault)
                {
                    var ac = eq.Status == EquipmentStatus.Fault ? Color.Parse("#EF4444") : Color.Parse("#F59E0B");
                    DrawProjectedRing(context, viewProj, w, h, pos with { Y = 0.12f }, rad + (float)(0.2 * pulse),
                        new Pen(GetBrush(ac.R, ac.G, ac.B, (byte)(220 * pulse)), 2.4));
                }
                if (string.Equals(activeSelected, p.Id, StringComparison.OrdinalIgnoreCase))
                {
                    DrawProjectedRing(context, viewProj, w, h, pos with { Y = (p.IsConveyor || p.IsPipe || p.IsSubComponent) ? pos.Y + 0.10f : 0.14f }, rad + 0.18f, SelectedLabelPen);
                }
            }
        }

        private static void AppendMeshTris(
            GlbMeshData mesh, Matrix4x4 model, Color tint, float tintMix,
            Matrix4x4 viewProj, double w, double h, Vector3 lightDir, List<SoftTri> tris)
        {
            var verts = mesh.Vertices;
            var idx = mesh.Indices;
            int triStride = idx.Length > 12000 ? 6 : 3;
            for (int i = 0; i + 2 < idx.Length; i += triStride)
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

                double cross = (s1.X - s0.X) * (s2.Y - s0.Y) - (s1.Y - s0.Y) * (s2.X - s0.X);
                if (cross >= 0) continue;

                var n = Vector3.Normalize(Vector3.Cross(w1 - w0, w2 - w0));
                float ndotl = MathF.Max(0f, Vector3.Dot(n, lightDir));
                float shade = 0.42f + 0.58f * ndotl;

                float lr = MathF.Pow(Math.Clamp(verts[i0 + 6], 0f, 1f), 1f / 2.2f);
                float lg = MathF.Pow(Math.Clamp(verts[i0 + 7], 0f, 1f), 1f / 2.2f);
                float lb = MathF.Pow(Math.Clamp(verts[i0 + 8], 0f, 1f), 1f / 2.2f);
                float br = lr * (1f - tintMix) + (tint.R / 255f) * tintMix;
                float bg = lg * (1f - tintMix) + (tint.G / 255f) * tintMix;
                float bb = lb * (1f - tintMix) + (tint.B / 255f) * tintMix;

                byte r = (byte)Math.Clamp(br * shade * 255f, 0, 255);
                byte g = (byte)Math.Clamp(bg * shade * 255f, 0, 255);
                byte b = (byte)Math.Clamp(bb * shade * 255f, 0, 255);

                tris.Add(new SoftTri(s0, s1, s2, Color.FromRgb(r, g, b), (d0 + d1 + d2) / 3f));
            }
        }

        private static readonly Vector3[] BoxCorners =
        {
            new(-0.5f, -0.5f,  0.5f), new( 0.5f, -0.5f,  0.5f), new( 0.5f,  0.5f,  0.5f), new(-0.5f,  0.5f,  0.5f),
            new(-0.5f, -0.5f, -0.5f), new( 0.5f, -0.5f, -0.5f), new( 0.5f,  0.5f, -0.5f), new(-0.5f,  0.5f, -0.5f)
        };

        private static readonly int[][] BoxFaces =
        {
            new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 },
            new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 }
        };

        private static void AppendBoxTris(
            Matrix4x4 model, Color tint, Matrix4x4 viewProj, double w, double h, Vector3 lightDir, List<SoftTri> tris)
        {
            foreach (var face in BoxFaces)
            {
                var p0 = Vector3.Transform(BoxCorners[face[0]], model);
                var p1 = Vector3.Transform(BoxCorners[face[1]], model);
                var p2 = Vector3.Transform(BoxCorners[face[2]], model);
                var p3 = Vector3.Transform(BoxCorners[face[3]], model);

                if (!Plant3DSceneData.TryProjectToScreen(p0, viewProj, w, h, out var s0, out float d0) ||
                    !Plant3DSceneData.TryProjectToScreen(p1, viewProj, w, h, out var s1, out float d1) ||
                    !Plant3DSceneData.TryProjectToScreen(p2, viewProj, w, h, out var s2, out float d2) ||
                    !Plant3DSceneData.TryProjectToScreen(p3, viewProj, w, h, out var s3, out float d3))
                    continue;

                var n = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
                float shade = 0.42f + 0.58f * MathF.Max(0f, Vector3.Dot(n, lightDir));
                var col = Color.FromRgb(
                    (byte)Math.Clamp(tint.R * shade, 0, 255),
                    (byte)Math.Clamp(tint.G * shade, 0, 255),
                    (byte)Math.Clamp(tint.B * shade, 0, 255));

                tris.Add(new SoftTri(s0, s1, s2, col, (d0 + d1 + d2) / 3f));
                tris.Add(new SoftTri(s0, s2, s3, col, (d0 + d2 + d3) / 3f));
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
            Matrix4x4 viewProj, Dictionary<string, EquipmentItem> eqMap, LoaderPose loaderPose)
        {
            _owner._hitRects.Clear();
            var placed = new List<Rect>(Plant3DSceneData.InteractiveNodes.Count + 3)
            {
                // Reserve top HUD bar, top-right axis gizmo, and bottom hint bar from label clutter
                new Rect(0, 0, w, 40),
                new Rect(Math.Max(0, w - 95), 0, 95, 88),
                new Rect(0, Math.Max(0, h - 30), w, 30)
            };
            string? activeSelected = _owner.Selected3DNode?.Id ?? _owner.SelectedEquipment?.Id;

            var ordered = Plant3DSceneData.InteractiveNodes
                .OrderByDescending(p => string.Equals(activeSelected, p.Id, StringComparison.OrdinalIgnoreCase) ? 6
                    : string.Equals(_owner._hoveredId, p.Id, StringComparison.OrdinalIgnoreCase) ? 5
                    : (!p.IsConveyor && !p.IsSubComponent && !p.IsPipe ? 3 : p.IsSubComponent ? 2 : 0)
                      + (!p.IsSubComponent && !p.IsPipe && eqMap.TryGetValue(p.LinkedScadaId, out var e) && e.Status is EquipmentStatus.Fault or EquipmentStatus.Warning ? 1 : 0));

            foreach (var p in ordered)
            {
                var basePos = p.Id == "FEL-101" ? loaderPose.ChassisPosition : p.Position;
                var anchor = new Vector3(basePos.X, basePos.Y + p.LabelHeight, basePos.Z);
                if (!Plant3DSceneData.TryProjectToScreen(anchor, viewProj, w, h, out var pt, out _))
                    continue;

                bool isSelected = string.Equals(activeSelected, p.Id, StringComparison.OrdinalIgnoreCase);
                bool isHovered = string.Equals(_owner._hoveredId, p.Id, StringComparison.OrdinalIgnoreCase);

                // When zoomed out, only show conveyor labels if hovered/selected or zoomed in >= 0.85
                if (p.IsConveyor && !isSelected && !isHovered && _owner._zoom < 0.78)
                    continue;
                if (p.IsSubComponent && !isSelected && !isHovered && _owner._zoom < 0.76)
                    continue;
                if (p.IsPipe && !isSelected && !isHovered && _owner._zoom < 0.84)
                    continue;

                bool showDetail = isSelected || isHovered || _owner._zoom >= 1.28;
                string labelText = showDetail
                    ? $"{p.Id} · {p.GaLabel}"
                    : p.Id;

                var text = new FormattedText(labelText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 10.0, Brushes.White);
                var bg = new Rect(pt.X - 6, pt.Y - 10, text.Width + 12, text.Height + 5);

                int nudges = 0;
                bool overlaps = false;
                while (nudges < 3 && placed.Any(r => r.Intersects(bg)))
                {
                    bg = new Rect(bg.X, bg.Y - (bg.Height + 3), bg.Width, bg.Height);
                    nudges++;
                }
                if (!isSelected && !isHovered && placed.Any(r => r.Intersects(bg)))
                {
                    overlaps = true;
                }
                if (overlaps) continue;

                placed.Add(bg);
                _owner._hitRects.Add((p.Id, bg));

                var bgBrush = isSelected ? SelectedLabelBgBrush : isHovered ? HoverLabelBgBrush : LabelBgBrush;
                var borderPen = isSelected ? SelectedLabelPen : isHovered ? HoverLabelPen : null;

                context.DrawRectangle(bgBrush, borderPen, bg, 4);
                context.DrawText(text, new Point(bg.X + 6, bg.Y + 2));
            }
        }

        private void DrawHoverTooltip(
            DrawingContext context, double w, double h,
            Dictionary<string, EquipmentItem> eqMap,
            IReadOnlyDictionary<string, ProcessFlowState> flowMap)
        {
            if (_owner._hoveredId is null || _owner._hoverPointer is not Point ptr)
                return;

            var node = Plant3DSceneData.FindNode(_owner._hoveredId);
            if (node is null) return;

            eqMap.TryGetValue(node.LinkedScadaId, out var eq);
            if (!flowMap.TryGetValue(node.Id, out var flow) &&
                !flowMap.TryGetValue(node.LinkedScadaId, out flow) &&
                (node.IsSubComponent || node.IsPipe))
            {
                flowMap.TryGetValue("WP-301", out flow);
            }

            string line1 = $"{node.Id} — {node.GaLabel}";
            string line2 = $"{node.StageLabel}  |  {node.FlowSummary}";
            string line3 = eq is not null
                ? $"SCADA {eq.Id}: {eq.Status}  |  {eq.PrimaryMetricDisplay}  |  Load {eq.LoadPercent:0}%  |  Est. {flow?.EstimatedTph ?? 0:0} t/h"
                : $"Est. Throughput: {flow?.EstimatedTph ?? 0:0} t/h";

            var t1 = new FormattedText(line1, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 11.5, Brushes.White);
            var t2 = new FormattedText(line2, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 10.0, GetBrush(56, 189, 248));
            var t3 = new FormattedText(line3, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 10.0, GetBrush(148, 163, 184));

            double boxW = Math.Max(t1.Width, Math.Max(t2.Width, t3.Width)) + 20;
            double boxH = t1.Height + t2.Height + t3.Height + 20;
            double x = Math.Clamp(ptr.X + 14, 10, Math.Max(10, w - boxW - 10));
            double y = Math.Clamp(ptr.Y - boxH - 10, 10, Math.Max(10, h - boxH - 30));

            var rect = new Rect(x, y, boxW, boxH);
            context.DrawRectangle(GetBrush(10, 18, 34, 240), SelectedLabelPen, rect, 6);
            context.DrawText(t1, new Point(x + 10, y + 6));
            context.DrawText(t2, new Point(x + 10, y + 8 + t1.Height));
            context.DrawText(t3, new Point(x + 10, y + 10 + t1.Height + t2.Height));
        }

        private void DrawAxisGizmo(DrawingContext context, double w)
        {
            var origin = new Point(w - 46, 46);
            const double len = 22;
            DrawAxisVec(context, origin, new Vector3(1, 0, 0), len, GetBrush(239, 68, 68), "E(+X)");
            DrawAxisVec(context, origin, new Vector3(0, 1, 0), len, GetBrush(34, 197, 94), "Y");
            DrawAxisVec(context, origin, new Vector3(0, 0, 1), len, GetBrush(56, 189, 248), "S(+Z)");
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
            var txt = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 8.5, brush);
            context.DrawText(txt, new Point(tip.X + 2, tip.Y - 5));
        }

        private void DrawHudFooter(DrawingContext context, double w, double h, Unity3DEffectSettings fx)
        {
            string mode = _owner._glCanvas.IsHardwareActive
                ? $"GPU GGX-PBR · 16-Tap PCSS · Clearcoat · GTAO+CAS · {_owner._glCanvas.LastFrameDrawCalls} draws"
                : $"Software 3D · {Plant3DSceneData.LoadAllGlbMeshes().Count} GLB · {Plant3DSceneData.AllNodes.Count} GA Nodes";

            var badgeTxt = new FormattedText(mode, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 10, GetBrush(56, 189, 248));
            var badgeRect = new Rect(12, 10, badgeTxt.Width + 14, badgeTxt.Height + 6);
            context.DrawRectangle(LabelBgBrush, HoverLabelPen, badgeRect, 4);
            context.DrawText(badgeTxt, new Point(19, 13));

            string sourceBadge = string.IsNullOrWhiteSpace(_owner.TelemetrySourceBadge)
                ? "SIMULATED DEMO TELEMETRY · READ-ONLY (No PLC Write)"
                : _owner.TelemetrySourceBadge;
            bool isLive = sourceBadge.Contains("LIVE", StringComparison.OrdinalIgnoreCase);
            var srcBrush = isLive ? GetBrush(34, 197, 94) : GetBrush(251, 191, 36);
            var srcBg = isLive ? GetBrush(6, 78, 59, 210) : GetBrush(120, 53, 15, 210);
            var srcPen = new Pen(srcBrush, 1.1);
            var srcTxt = new FormattedText(sourceBadge, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 9.8, srcBrush);
            var srcRect = new Rect(badgeRect.Right + 8, 10, srcTxt.Width + 14, srcTxt.Height + 6);
            if (srcRect.Right < w - 90)
            {
                context.DrawRectangle(srcBg, srcPen, srcRect, 4);
                context.DrawText(srcTxt, new Point(srcRect.X + 7, 13));
            }
            else
            {
                srcRect = new Rect(12, badgeRect.Bottom + 6, srcTxt.Width + 14, srcTxt.Height + 6);
                context.DrawRectangle(srcBg, srcPen, srcRect, 4);
                context.DrawText(srcTxt, new Point(19, srcRect.Y + 3));
            }

            string projMode = fx.IsOrthographic ? "ORTHO" : "PERSP";
            var hint = new FormattedText(
                $"[{projMode}] Click Machine/Conveyor: Inspect  |  Left-Drag: Orbit  |  Right-Drag / WASD: Pan  |  Scroll: Zoom  |  F: Focus  |  R: Reset",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, HudTypeface, 10.5, GetBrush(203, 213, 225));
            var hintRect = new Rect(8, h - 26, hint.Width + 14, hint.Height + 6);
            context.DrawRectangle(LabelBgBrush, null, hintRect, 4);
            context.DrawText(hint, new Point(15, h - 23));
        }
    }
}
