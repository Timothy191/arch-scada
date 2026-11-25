using System;
using System.Globalization;
using ArchScada.Desktop.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArchScada.Desktop.Controls;

/// <summary>
/// Live status-driven conveyor and equipment flow animation control for the Crushing &amp; Conveyors page.
/// Belt velocity, coal burden thickness, idler rotation, and visual warning/fault states directly
/// reflect real-time <see cref="EquipmentItem"/> status, load percentage, belt speed, and safety interlocks.
/// </summary>
public sealed class ConveyorStatusAnimationControl : Control
{
    public static readonly StyledProperty<string> EquipmentIdProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, string>(nameof(EquipmentId), "CV-101");

    public static readonly StyledProperty<string> EquipmentTypeProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, string>(nameof(EquipmentType), "Conveyor");

    public static readonly StyledProperty<EquipmentStatus> StatusProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, EquipmentStatus>(nameof(Status), EquipmentStatus.Running);

    public static readonly StyledProperty<double> LoadPercentProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, double>(nameof(LoadPercent), 75.0);

    public static readonly StyledProperty<double> BeltSpeedProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, double>(nameof(BeltSpeed), 2.8);

    public static readonly StyledProperty<bool> PullCordOkProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, bool>(nameof(PullCordOk), true);

    public static readonly StyledProperty<bool> BeltAlignmentOkProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, bool>(nameof(BeltAlignmentOk), true);

    public static readonly StyledProperty<bool> BeltSlipOkProperty =
        AvaloniaProperty.Register<ConveyorStatusAnimationControl, bool>(nameof(BeltSlipOk), true);

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#080E1A"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#1E293B")), 1.0);
    private static readonly IBrush PulleyFill = new SolidColorBrush(Color.Parse("#162238"));
    private static readonly IPen IdlerPen = new Pen(new SolidColorBrush(Color.Parse("#475569")), 1.3);
    private static readonly IPen RunningBeltPen = new Pen(new SolidColorBrush(Color.Parse("#22C55E")), 2.0);
    private static readonly IPen WarningBeltPen = new Pen(new SolidColorBrush(Color.Parse("#F59E0B")), 2.2);
    private static readonly IPen FaultBeltPen = new Pen(new SolidColorBrush(Color.Parse("#EF4444")), 2.2);
    private static readonly IPen StoppedBeltPen = new Pen(new SolidColorBrush(Color.Parse("#64748B")), 1.8);
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush FaultBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush CyanBrush = new SolidColorBrush(Color.Parse("#38BDF8"));
    private static readonly IBrush CoalBurdenBrush = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IPen CoalEdgePen = new Pen(new SolidColorBrush(Color.Parse("#38BDF8")), 1.0);
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#E2E8F0"));
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.Parse("#94A3B8"));

    private readonly DispatcherTimer _timer;
    private double _phase;

    public string EquipmentId
    {
        get => GetValue(EquipmentIdProperty);
        set => SetValue(EquipmentIdProperty, value);
    }

    public string EquipmentType
    {
        get => GetValue(EquipmentTypeProperty);
        set => SetValue(EquipmentTypeProperty, value);
    }

    public EquipmentStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public double LoadPercent
    {
        get => GetValue(LoadPercentProperty);
        set => SetValue(LoadPercentProperty, value);
    }

    public double BeltSpeed
    {
        get => GetValue(BeltSpeedProperty);
        set => SetValue(BeltSpeedProperty, value);
    }

    public bool PullCordOk
    {
        get => GetValue(PullCordOkProperty);
        set => SetValue(PullCordOkProperty, value);
    }

    public bool BeltAlignmentOk
    {
        get => GetValue(BeltAlignmentOkProperty);
        set => SetValue(BeltAlignmentOkProperty, value);
    }

    public bool BeltSlipOk
    {
        get => GetValue(BeltSlipOkProperty);
        set => SetValue(BeltSlipOkProperty, value);
    }

    public double Phase => _phase;

    static ConveyorStatusAnimationControl()
    {
        AffectsRender<ConveyorStatusAnimationControl>(
            EquipmentIdProperty,
            EquipmentTypeProperty,
            StatusProperty,
            LoadPercentProperty,
            BeltSpeedProperty,
            PullCordOkProperty,
            BeltAlignmentOkProperty,
            BeltSlipOkProperty);
    }

    public ConveyorStatusAnimationControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            AdvancePhase(0.033);
        };
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public void AdvancePhase(double dtSeconds)
    {
        if (Status is EquipmentStatus.Stopped or EquipmentStatus.Offline or EquipmentStatus.Fault ||
            !PullCordOk)
        {
            InvalidateVisual();
            return;
        }

        double speedNorm = BeltSpeed > 15.0
            ? Math.Clamp(BeltSpeed / 550.0, 0.35, 1.8)
            : Math.Clamp(BeltSpeed / 2.6, 0.35, 1.8);
        if (!BeltSlipOk) speedNorm *= 0.45;
        _phase = (_phase + dtSeconds * speedNorm) % 1000.0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 20 || h <= 20) return;

        var rect = new Rect(0, 0, w, h);
        context.FillRectangle(BgBrush, rect, 5f);

        bool isTripped = !PullCordOk || Status == EquipmentStatus.Fault;
        bool isWarning = Status == EquipmentStatus.Warning || !BeltAlignmentOk || !BeltSlipOk || LoadPercent >= 85.0;
        bool isRunning = Status is EquipmentStatus.Running or EquipmentStatus.Warning && !isTripped;

        IPen activePen = isTripped ? FaultBeltPen
            : isWarning ? WarningBeltPen
            : isRunning ? RunningBeltPen
            : StoppedBeltPen;

        IBrush activeBrush = isTripped ? FaultBrush
            : isWarning ? WarningBrush
            : isRunning ? RunningBrush
            : StoppedBrush;

        double leftX = 22;
        double rightX = w - 22;
        double topBeltY = h * 0.44;
        double botBeltY = h * 0.68;
        double pulleyR = (botBeltY - topBeltY) * 0.5;
        double centerY = (topBeltY + botBeltY) * 0.5;

        // Lateral belt wobble when misaligned
        double wobbleY = (!BeltAlignmentOk && isRunning) ? Math.Sin(_phase * 16.0) * 1.6 : 0.0;

        // Top carrying strand & bottom return strand
        context.DrawLine(activePen, new Point(leftX, topBeltY + wobbleY), new Point(rightX, topBeltY - wobbleY));
        context.DrawLine(IdlerPen, new Point(leftX, botBeltY), new Point(rightX, botBeltY));

        // Head & Tail pulleys with rotating spokes
        var tailCenter = new Point(leftX, centerY);
        var headCenter = new Point(rightX, centerY);
        context.DrawEllipse(PulleyFill, activePen, tailCenter, pulleyR, pulleyR);
        context.DrawEllipse(PulleyFill, activePen, headCenter, pulleyR, pulleyR);

        double angle = _phase * 7.5;
        double dx = Math.Cos(angle) * (pulleyR - 2);
        double dy = Math.Sin(angle) * (pulleyR - 2);
        context.DrawLine(activePen, new Point(tailCenter.X - dx, tailCenter.Y - dy), new Point(tailCenter.X + dx, tailCenter.Y + dy));
        context.DrawLine(activePen, new Point(headCenter.X - dx, headCenter.Y - dy), new Point(headCenter.X + dx, headCenter.Y + dy));

        // Carrying trough idlers along the span
        int idlers = 5;
        for (int i = 1; i <= idlers; i++)
        {
            double ix = leftX + (rightX - leftX) * i / (idlers + 1.0);
            context.DrawEllipse(PulleyFill, IdlerPen, new Point(ix, topBeltY + 3.2), 2.6, 2.6);
        }

        // Animated coal burden lumps & directional flow chevrons proportional to LoadPercent
        if (isRunning && LoadPercent > 1.0)
        {
            int lumpCount = Math.Clamp((int)Math.Round(LoadPercent / 14.0), 3, 8);
            double lumpH = Math.Clamp(2.5 + (LoadPercent / 100.0) * 4.5, 2.5, 7.0);
            double span = rightX - leftX;

            for (int i = 0; i < lumpCount; i++)
            {
                double u = ((_phase * 0.75) + (double)i / lumpCount) % 1.0;
                double lx = leftX + span * u;
                double ly = topBeltY - lumpH * 0.55 + wobbleY * (1.0 - 2.0 * u);
                context.DrawEllipse(CoalBurdenBrush, CoalEdgePen, new Point(lx, ly), 5.5, lumpH * 0.55);
            }
        }

        // Top HUD line inside animation bar: Status + Speed/Load + Safety state
        string stateText = isTripped ? "TRIPPED / FAULT"
            : isWarning ? $"WARN · LOAD {LoadPercent:0}%"
            : isRunning ? $"RUNNING · LOAD {LoadPercent:0}%"
            : "STOPPED / STANDBY";

        var ftLeft = new FormattedText(
            $"{EquipmentId} ({EquipmentType.ToUpperInvariant()})",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            9.5,
            CyanBrush);
        context.DrawText(ftLeft, new Point(8, 3));

        var ftRight = new FormattedText(
            stateText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            9.5,
            activeBrush);
        context.DrawText(ftRight, new Point(Math.Max(8, w - ftRight.Width - 8), 3));

        context.DrawRectangle(null, BorderPen, rect, 5f, 5f);
    }
}
