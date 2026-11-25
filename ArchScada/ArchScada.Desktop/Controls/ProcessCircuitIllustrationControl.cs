using System;
using System.Globalization;
using ArchScada.Desktop.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArchScada.Desktop.Controls;

public enum ProcessCircuitKind
{
    CrushingConveyors,
    ThickenerCircuit,
    MagnetitePrep,
    FineCoalFlotation,
    DewateringTailings,
    RomLoadoutQuality
}

/// <summary>
/// Live data-driven animated industrial schematic illustration for each of the 6 CHPP process circuits
/// on the Plant Overview page. Animation velocity, fill levels, vortex/impeller RPM, bubble density,
/// and status colors follow real-time telemetry bound from <see cref="ViewModels.MainShellViewModel"/>.
/// </summary>
public sealed class ProcessCircuitIllustrationControl : Control
{
    public static readonly StyledProperty<ProcessCircuitKind> CircuitKindProperty =
        AvaloniaProperty.Register<ProcessCircuitIllustrationControl, ProcessCircuitKind>(nameof(CircuitKind));

    public static readonly StyledProperty<double> PrimaryValueProperty =
        AvaloniaProperty.Register<ProcessCircuitIllustrationControl, double>(nameof(PrimaryValue), 640.0);

    public static readonly StyledProperty<double> SecondaryValueProperty =
        AvaloniaProperty.Register<ProcessCircuitIllustrationControl, double>(nameof(SecondaryValue), 78.0);

    public static readonly StyledProperty<double> TertiaryValueProperty =
        AvaloniaProperty.Register<ProcessCircuitIllustrationControl, double>(nameof(TertiaryValue), 85.0);

    public static readonly StyledProperty<EquipmentStatus> CircuitStatusProperty =
        AvaloniaProperty.Register<ProcessCircuitIllustrationControl, EquipmentStatus>(nameof(CircuitStatus), EquipmentStatus.Running);

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#080E1A"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#1E293B")), 1.0);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#111C30")), 1.0);
    private static readonly IBrush SteelFill = new SolidColorBrush(Color.Parse("#162238"));
    private static readonly IBrush SteelDarkFill = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IPen SteelEdgePen = new Pen(new SolidColorBrush(Color.Parse("#334155")), 1.4);
    private static readonly IPen CyanPen = new Pen(new SolidColorBrush(Color.Parse("#38BDF8")), 1.8);
    private static readonly IPen EmeraldPen = new Pen(new SolidColorBrush(Color.Parse("#22C55E")), 1.8);
    private static readonly IPen AmberPen = new Pen(new SolidColorBrush(Color.Parse("#F59E0B")), 1.8);
    private static readonly IPen PurplePen = new Pen(new SolidColorBrush(Color.Parse("#A855F7")), 1.8);
    private static readonly IBrush CyanBrush = new SolidColorBrush(Color.Parse("#38BDF8"));
    private static readonly IBrush CyanSoftBrush = new SolidColorBrush(Color.FromArgb(85, 56, 189, 248));
    private static readonly IBrush EmeraldBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush EmeraldSoftBrush = new SolidColorBrush(Color.FromArgb(75, 34, 197, 94));
    private static readonly IBrush AmberBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush AmberSoftBrush = new SolidColorBrush(Color.FromArgb(85, 245, 158, 11));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush CoalDarkBrush = new SolidColorBrush(Color.Parse("#1E242E"));
    private static readonly IBrush CoalHighlightBrush = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush TextPrimaryBrush = new SolidColorBrush(Color.Parse("#E2E8F0"));
    private static readonly IBrush TextMutedBrush = new SolidColorBrush(Color.Parse("#94A3B8"));

    private readonly DispatcherTimer _timer;
    private double _phase;

    public ProcessCircuitKind CircuitKind
    {
        get => GetValue(CircuitKindProperty);
        set => SetValue(CircuitKindProperty, value);
    }

    public double PrimaryValue
    {
        get => GetValue(PrimaryValueProperty);
        set => SetValue(PrimaryValueProperty, value);
    }

    public double SecondaryValue
    {
        get => GetValue(SecondaryValueProperty);
        set => SetValue(SecondaryValueProperty, value);
    }

    public double TertiaryValue
    {
        get => GetValue(TertiaryValueProperty);
        set => SetValue(TertiaryValueProperty, value);
    }

    public EquipmentStatus CircuitStatus
    {
        get => GetValue(CircuitStatusProperty);
        set => SetValue(CircuitStatusProperty, value);
    }

    public double Phase => _phase;

    static ProcessCircuitIllustrationControl()
    {
        AffectsRender<ProcessCircuitIllustrationControl>(
            CircuitKindProperty,
            PrimaryValueProperty,
            SecondaryValueProperty,
            TertiaryValueProperty,
            CircuitStatusProperty);
    }

    public ProcessCircuitIllustrationControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => OnTimerTick();

        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public double ComputeSpeedMultiplier()
    {
        if (CircuitStatus is EquipmentStatus.Stopped or EquipmentStatus.Offline or EquipmentStatus.Fault)
            return 0.0;

        double norm = CircuitKind switch
        {
            ProcessCircuitKind.CrushingConveyors => Math.Clamp(PrimaryValue / 650.0, 0.25, 1.8),
            ProcessCircuitKind.ThickenerCircuit => Math.Clamp(TertiaryValue / 385.0, 0.25, 1.8),
            ProcessCircuitKind.MagnetitePrep => Math.Clamp(SecondaryValue / 185.0, 0.25, 1.8),
            ProcessCircuitKind.FineCoalFlotation => Math.Clamp(SecondaryValue / 1420.0, 0.25, 1.8),
            ProcessCircuitKind.DewateringTailings => Math.Clamp(TertiaryValue / 2.15, 0.25, 1.8),
            ProcessCircuitKind.RomLoadoutQuality => Math.Clamp(TertiaryValue / 1850.0, 0.25, 1.8),
            _ => 1.0
        };
        return double.IsFinite(norm) ? norm : 1.0;
    }

    public void AdvancePhase(double dtSeconds)
    {
        double mult = ComputeSpeedMultiplier();
        if (mult <= 0.0) return;
        _phase = (_phase + dtSeconds * mult) % 1000.0;
        InvalidateVisual();
    }

    private void OnTimerTick()
    {
        if (!IsVisible) return;
        AdvancePhase(0.033);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 24 || h <= 24) return;

        var rect = new Rect(0, 0, w, h);
        context.FillRectangle(BgBrush, rect, 7f);

        // Subtle engineering schematic grid
        for (double gx = 24; gx < w; gx += 28)
            context.DrawLine(GridPen, new Point(gx, 2), new Point(gx, h - 2));
        for (double gy = 22; gy < h; gy += 24)
            context.DrawLine(GridPen, new Point(2, gy), new Point(w - 2, gy));

        switch (CircuitKind)
        {
            case ProcessCircuitKind.CrushingConveyors:
                RenderCrushingConveyors(context, w, h);
                break;
            case ProcessCircuitKind.ThickenerCircuit:
                RenderThickenerCircuit(context, w, h);
                break;
            case ProcessCircuitKind.MagnetitePrep:
                RenderMagnetitePrep(context, w, h);
                break;
            case ProcessCircuitKind.FineCoalFlotation:
                RenderFineCoalFlotation(context, w, h);
                break;
            case ProcessCircuitKind.DewateringTailings:
                RenderDewateringTailings(context, w, h);
                break;
            case ProcessCircuitKind.RomLoadoutQuality:
                RenderRomLoadoutQuality(context, w, h);
                break;
        }

        // Top-right live status LED pill
        var statusBrush = CircuitStatus switch
        {
            EquipmentStatus.Running => EmeraldBrush,
            EquipmentStatus.Warning => AmberBrush,
            EquipmentStatus.Fault => RedBrush,
            _ => TextMutedBrush
        };
        context.DrawEllipse(statusBrush, null, new Point(w - 13, 12), 4.0, 4.0);
        context.DrawRectangle(null, BorderPen, rect, 7f, 7f);
    }

    private void RenderCrushingConveyors(DrawingContext ctx, double w, double h)
    {
        double tph = PrimaryValue;
        double beltSpd = SecondaryValue;
        double loadPct = TertiaryValue;

        // 1. Inclined Feed Conveyor CV-101 (left -> jaw crusher)
        var pTail = new Point(18, h * 0.62);
        var pHead = new Point(w * 0.34, h * 0.28);
        ctx.DrawLine(CyanPen, pTail, pHead);
        ctx.DrawEllipse(SteelFill, CyanPen, pTail, 6, 6);
        ctx.DrawEllipse(SteelFill, CyanPen, pHead, 6, 6);

        // Moving coal lumps along CV-101
        int lumpCount = Math.Clamp((int)Math.Round(tph / 130.0), 3, 7);
        for (int i = 0; i < lumpCount; i++)
        {
            double u = ((_phase * 0.85) + (double)i / lumpCount) % 1.0;
            double lx = pTail.X + (pHead.X - pTail.X) * u;
            double ly = pTail.Y + (pHead.Y - pTail.Y) * u - 5;
            ctx.DrawEllipse(CoalDarkBrush, SteelEdgePen, new Point(lx, ly), 4.2, 3.2);
        }
        DrawBadge(ctx, "CV-101", $"{beltSpd:0.00} m/s", 14, h * 0.72, CyanBrush);

        // 2. Primary Jaw Crusher CR-101 (center-left)
        double cx = w * 0.42;
        double cy = h * 0.48;
        var crusherBox = new Rect(cx - 22, cy - 18, 44, 38);
        ctx.FillRectangle(SteelFill, crusherBox, 4f);
        ctx.DrawRectangle(null, SteelEdgePen, crusherBox, 4f, 4f);

        // Reciprocating swing jaw + rotating eccentric flywheel
        double jawSwing = Math.Sin(_phase * 14.0) * 3.2;
        ctx.DrawLine(AmberPen, new Point(cx - 12, cy - 12), new Point(cx - 10, cy + 14));
        ctx.DrawLine(CyanPen, new Point(cx + 12 - jawSwing, cy - 12), new Point(cx - 2 - jawSwing * 0.6, cy + 14));

        var wheelCenter = new Point(cx + 10, cy - 4);
        ctx.DrawEllipse(SteelDarkFill, AmberPen, wheelCenter, 9, 9);
        double spokeAngle = _phase * 8.0;
        ctx.DrawLine(AmberPen,
            new Point(wheelCenter.X - Math.Cos(spokeAngle) * 7, wheelCenter.Y - Math.Sin(spokeAngle) * 7),
            new Point(wheelCenter.X + Math.Cos(spokeAngle) * 7, wheelCenter.Y + Math.Sin(spokeAngle) * 7));
        DrawBadge(ctx, "CR-101", $"{tph:0} t/h · {loadPct:0}%", cx - 26, h * 0.75, AmberBrush);

        // 3. Multi-Deck Vibrating Sizing Screen SC-102 (right)
        double sx = w * 0.76;
        double sy = h * 0.45;
        double vibX = Math.Sin(_phase * 24.0) * 1.6;
        double vibY = Math.Cos(_phase * 24.0) * 1.2;

        // Transfer belt CR-101 -> SC-102
        ctx.DrawLine(EmeraldPen, new Point(cx + 22, cy + 10), new Point(sx - 28, sy - 10));

        var screenRect = new Rect(sx - 26 + vibX, sy - 14 + vibY, 52, 26);
        ctx.FillRectangle(SteelFill, screenRect, 4f);
        ctx.DrawRectangle(null, CyanPen, screenRect, 4f, 4f);
        ctx.DrawLine(CyanPen, new Point(screenRect.Left + 4, screenRect.Top + 9), new Point(screenRect.Right - 4, screenRect.Top + 13));
        ctx.DrawLine(EmeraldPen, new Point(screenRect.Left + 4, screenRect.Top + 17), new Point(screenRect.Right - 4, screenRect.Top + 21));
        DrawBadge(ctx, "SC-102", "Dual-Deck 50/6mm", sx - 28, h * 0.72, EmeraldBrush);
    }

    private void RenderThickenerCircuit(DrawingContext ctx, double w, double h)
    {
        double bedLvl = Math.Clamp(PrimaryValue, 5.0, 96.0);
        double waterRec = SecondaryValue;
        double uflw = TertiaryValue;

        // 1. High-Rate Thickener Basin TK-201
        double tx = w * 0.40;
        double ty = h * 0.46;
        double tw = w * 0.54;
        double th = h * 0.46;
        var basinRect = new Rect(tx - tw * 0.5, ty - th * 0.5, tw, th);
        ctx.FillRectangle(SteelDarkFill, basinRect, 4f);

        // Clarified water zone (top) & settled mud bed height driven by live ThickenerBedLevelPercent
        double bedRatio = Math.Clamp(bedLvl / 100.0, 0.15, 0.90);
        double bedH = th * bedRatio * 0.78;
        var waterRect = new Rect(basinRect.Left + 2, basinRect.Top + 3, tw - 4, th - 6);
        ctx.FillRectangle(CyanSoftBrush, waterRect, 3f);
        var mudRect = new Rect(basinRect.Left + 2, basinRect.Bottom - bedH - 2, tw - 4, bedH);
        ctx.FillRectangle(AmberSoftBrush, mudRect, 3f);
        ctx.DrawRectangle(null, CyanPen, basinRect, 4f, 4f);

        // Rotating central rake shaft & dual sweeping rake blades
        ctx.DrawLine(SteelEdgePen, new Point(tx, basinRect.Top - 5), new Point(tx, basinRect.Bottom - 2));
        double rakeSpan = Math.Cos(_phase * 2.4) * (tw * 0.42);
        ctx.DrawLine(AmberPen, new Point(tx, basinRect.Bottom - 4), new Point(tx - rakeSpan, basinRect.Bottom - 10));
        ctx.DrawLine(AmberPen, new Point(tx, basinRect.Bottom - 4), new Point(tx + rakeSpan, basinRect.Bottom - 10));

        // Peripheral clarified water overflow ripples
        for (int i = 0; i < 4; i++)
        {
            double rx = basinRect.Left + 10 + ((_phase * 28 + i * (tw / 4.0)) % Math.Max(12, tw - 20));
            ctx.DrawLine(CyanPen, new Point(rx, basinRect.Top + 5), new Point(rx + 8, basinRect.Top + 5));
        }
        DrawBadge(ctx, "TK-201", $"Bed {bedLvl:0.0}% · Rec {waterRec:0.1}%", basinRect.Left, h * 0.74, CyanBrush);

        // 2. Duty Centrifugal Underflow Pump PP-201 (right)
        double px = w * 0.83;
        double py = h * 0.56;
        ctx.DrawLine(AmberPen, new Point(tx, basinRect.Bottom), new Point(tx, py));
        ctx.DrawLine(AmberPen, new Point(tx, py), new Point(px - 14, py));

        ctx.DrawEllipse(SteelFill, EmeraldPen, new Point(px, py), 13, 13);
        for (int b = 0; b < 3; b++)
        {
            double ang = _phase * 7.5 + b * (Math.PI * 2.0 / 3.0);
            ctx.DrawLine(EmeraldPen, new Point(px, py), new Point(px + Math.Cos(ang) * 9, py + Math.Sin(ang) * 9));
        }
        DrawBadge(ctx, "PP-201", $"{uflw:0} m³/h", px - 24, h * 0.76, EmeraldBrush);
    }

    private void RenderMagnetitePrep(DrawingContext ctx, double w, double h)
    {
        double sg = PrimaryValue;
        double kpa = SecondaryValue;
        double rec = TertiaryValue;

        // 1. TK-301 Medium Tank & Sweeping West Feed Pipe PIPE-304 (left)
        var tankRect = new Rect(14, h * 0.28, 36, h * 0.44);
        ctx.FillRectangle(SteelDarkFill, tankRect, 3f);
        var medFill = new Rect(16, tankRect.Bottom - tankRect.Height * 0.68, 32, tankRect.Height * 0.68 - 2);
        ctx.FillRectangle(EmeraldSoftBrush, medFill, 2f);
        ctx.DrawRectangle(null, EmeraldPen, tankRect, 3f, 3f);
        DrawBadge(ctx, "TK-301", $"{sg:0.00} SG", 12, h * 0.75, EmeraldBrush);

        // Sweeping West inlet pipe PIPE-304 into West side of Wide Diamond DMC-301
        double dmcX = w * 0.48;
        double dmcY = h * 0.36;
        ctx.DrawLine(CyanPen, new Point(tankRect.Right, h * 0.50), new Point(dmcX - 38, dmcY));

        // 2. Full-Length Wide Asymmetric Diamond Cyclone DMC-301 (center)
        var diamond = new StreamGeometry();
        using (var g = diamond.Open())
        {
            g.BeginFigure(new Point(dmcX - 34, dmcY - 6), isFilled: true);
            g.LineTo(new Point(dmcX + 6, dmcY - 18));
            g.LineTo(new Point(dmcX + 34, dmcY - 12));
            g.LineTo(new Point(dmcX + 34, dmcY + 12));
            g.LineTo(new Point(dmcX + 6, dmcY + 18));
            g.LineTo(new Point(dmcX - 34, dmcY + 6));
            g.EndFigure(isClosed: true);
        }
        ctx.DrawGeometry(SteelFill, AmberPen, diamond);

        // Animated helical vortex rings inside DMC-301
        for (int v = 0; v < 4; v++)
        {
            double u = ((_phase * 1.4) + v * 0.25) % 1.0;
            double vx = dmcX - 24 + u * 50;
            double ry = 4 + u * 8;
            ctx.DrawEllipse(null, CyanPen, new Point(vx, dmcY), 3.0, ry);
        }
        DrawBadge(ctx, "DMC-301", $"PT-301 {kpa:0} kPa", dmcX - 34, h * 0.74, AmberBrush);

        // East Product (SC-301) & Discard (SC-302) drops + Macsep MS-301 drum (right)
        double msX = w * 0.84;
        double msY = h * 0.46;
        ctx.DrawLine(CyanPen, new Point(dmcX + 34, dmcY - 7), new Point(msX - 16, msY - 10));
        ctx.DrawLine(AmberPen, new Point(dmcX + 34, dmcY + 7), new Point(msX - 16, msY + 10));

        ctx.DrawEllipse(SteelFill, CyanPen, new Point(msX, msY), 14, 14);
        double drumAng = _phase * 5.0;
        ctx.DrawLine(CyanPen,
            new Point(msX - Math.Cos(drumAng) * 10, msY - Math.Sin(drumAng) * 10),
            new Point(msX + Math.Cos(drumAng) * 10, msY + Math.Sin(drumAng) * 10));
        DrawBadge(ctx, "MS-301", $"Rec {rec:0.1}%", msX - 24, h * 0.74, CyanBrush);
    }

    private void RenderFineCoalFlotation(DrawingContext ctx, double w, double h)
    {
        double pulpLvl = Math.Clamp(PrimaryValue, 15.0, 95.0);
        double airflow = SecondaryValue;
        double rec = TertiaryValue;

        // 1. Helical Spiral Concentrator SP-401 (left)
        double spX = w * 0.22;
        double spY1 = h * 0.16;
        double spY2 = h * 0.70;
        ctx.DrawLine(SteelEdgePen, new Point(spX, spY1), new Point(spX, spY2));
        for (int turn = 0; turn < 4; turn++)
        {
            double ty = spY1 + 6 + turn * ((spY2 - spY1 - 12) / 3.5);
            ctx.DrawEllipse(null, CyanPen, new Point(spX, ty), 16, 4.5);
            double px = spX + Math.Cos(_phase * 6.0 + turn * 1.5) * 13;
            ctx.DrawEllipse(AmberBrush, null, new Point(px, ty), 2.5, 2.5);
        }
        DrawBadge(ctx, "SP-401", "Spiral Bank", 12, h * 0.75, CyanBrush);

        // 2. Flotation Column FC-401 with rising micro-bubbles driven by AB-401 airflow
        double fcX = w * 0.56;
        var colRect = new Rect(fcX - 24, h * 0.14, 48, h * 0.58);
        ctx.FillRectangle(SteelDarkFill, colRect, 4f);
        double fillH = colRect.Height * (pulpLvl / 100.0);
        var pulpRect = new Rect(colRect.Left + 2, colRect.Bottom - fillH, colRect.Width - 4, fillH - 2);
        ctx.FillRectangle(CyanSoftBrush, pulpRect, 3f);
        ctx.DrawRectangle(null, CyanPen, colRect, 4f, 4f);

        int bubbles = Math.Clamp((int)Math.Round(airflow / 220.0), 4, 9);
        for (int b = 0; b < bubbles; b++)
        {
            double bu = ((_phase * 1.1) + (double)b / bubbles) % 1.0;
            double bx = colRect.Left + 8 + ((b * 13) % 32) + Math.Sin(_phase * 5 + b) * 2.5;
            double by = colRect.Bottom - 6 - bu * (colRect.Height - 12);
            ctx.DrawEllipse(CyanBrush, null, new Point(bx, by), 2.4, 2.4);
        }
        DrawBadge(ctx, "FC-401", $"Pulp {pulpLvl:0}% · Rec {rec:0.1}%", fcX - 34, h * 0.75, EmeraldBrush);

        // 3. Air Blower AB-401 (right)
        double abX = w * 0.85;
        double abY = h * 0.48;
        ctx.DrawLine(EmeraldPen, new Point(abX - 12, abY), new Point(colRect.Right, colRect.Bottom - 10));
        ctx.DrawEllipse(SteelFill, EmeraldPen, new Point(abX, abY), 12, 12);
        DrawBadge(ctx, "AB-401", $"{airflow:0} Nm³/h", abX - 28, h * 0.75, AmberBrush);
    }

    private void RenderDewateringTailings(DrawingContext ctx, double w, double h)
    {
        double moist = PrimaryValue;
        double bar = SecondaryValue;
        double spd = TertiaryValue;

        // 1. Screen-Bowl Centrifuge CF-501 (left)
        double cfX = w * 0.22;
        double cfY = h * 0.40;
        ctx.DrawEllipse(SteelFill, PurplePen, new Point(cfX, cfY), 18, 18);
        for (int k = 0; k < 4; k++)
        {
            double a = _phase * 9.5 + k * (Math.PI * 0.5);
            ctx.DrawLine(CyanPen, new Point(cfX, cfY), new Point(cfX + Math.Cos(a) * 13, cfY + Math.Sin(a) * 13));
        }
        DrawBadge(ctx, "CF-501", $"Moist {moist:0.1}%", 12, h * 0.74, CyanBrush);

        // 2. Multi-Plate Tailings Filter Press FP-501 (center)
        double fpX = w * 0.54;
        double fpY = h * 0.38;
        double squeeze = (Math.Sin(_phase * 3.2) * 0.5 + 0.5) * 2.2;
        var frameRect = new Rect(fpX - 28, fpY - 14, 56, 28);
        ctx.DrawRectangle(SteelDarkFill, SteelEdgePen, frameRect, 3f, 3f);
        for (int p = 0; p < 6; p++)
        {
            double px = frameRect.Left + 8 + p * (7.2 - squeeze * 0.25);
            ctx.DrawLine(AmberPen, new Point(px, frameRect.Top + 3), new Point(px, frameRect.Bottom - 3));
        }
        DrawBadge(ctx, "FP-501", $"{bar:0.1} bar", fpX - 24, h * 0.74, AmberBrush);

        // 3. Takeaway Conveyor CV-501 (bottom-right)
        var c1 = new Point(w * 0.34, h * 0.64);
        var c2 = new Point(w - 18, h * 0.64);
        ctx.DrawLine(EmeraldPen, c1, c2);
        for (int i = 0; i < 5; i++)
        {
            double u = ((_phase * 0.9) + i * 0.2) % 1.0;
            double lx = c1.X + (c2.X - c1.X) * u;
            ctx.DrawEllipse(CoalHighlightBrush, null, new Point(lx, c1.Y - 4), 3.5, 2.5);
        }
        DrawBadge(ctx, "CV-501", $"{spd:0.00} m/s", w * 0.74, h * 0.74, EmeraldBrush);
    }

    private void RenderRomLoadoutQuality(DrawingContext ctx, double w, double h)
    {
        double siloLvl = Math.Clamp(PrimaryValue, 10.0, 96.0);
        double ash = SecondaryValue;
        double batchTph = TertiaryValue;

        // 1. PGNAA Online Quality Analyzer Portal AN-601 (left)
        double anX = w * 0.22;
        double anY = h * 0.42;
        var portalRect = new Rect(anX - 16, anY - 18, 32, 36);
        ctx.FillRectangle(SteelFill, portalRect, 4f);
        ctx.DrawRectangle(null, EmeraldPen, portalRect, 4f, 4f);
        double scanY = portalRect.Top + 5 + ((Math.Sin(_phase * 6.0) * 0.5 + 0.5) * (portalRect.Height - 10));
        ctx.DrawLine(CyanPen, new Point(portalRect.Left + 3, scanY), new Point(portalRect.Right - 3, scanY));
        DrawBadge(ctx, "AN-601", $"Ash {ash:0.2}%", 12, h * 0.74, EmeraldBrush);

        // 2. Clean Coal Silo SL-601 (center)
        double slX = w * 0.52;
        var siloRect = new Rect(slX - 20, h * 0.14, 40, h * 0.52);
        ctx.FillRectangle(SteelDarkFill, siloRect, 4f);
        double coalH = siloRect.Height * (siloLvl / 100.0);
        var coalFill = new Rect(siloRect.Left + 2, siloRect.Bottom - coalH, siloRect.Width - 4, coalH - 2);
        ctx.FillRectangle(CyanSoftBrush, coalFill, 3f);
        ctx.DrawRectangle(null, CyanPen, siloRect, 4f, 4f);
        DrawBadge(ctx, "SL-601", $"Silo {siloLvl:0.0}%", slX - 24, h * 0.74, CyanBrush);

        // 3. Train Loadout Batcher LD-601 & Moving Rail Hopper Cars (right)
        double ldX = w * 0.80;
        double trackY = h * 0.66;
        ctx.DrawLine(SteelEdgePen, new Point(w * 0.64, trackY), new Point(w - 10, trackY));
        double carOffset = (_phase * 22.0) % 34.0;
        for (int c = -1; c <= 1; c++)
        {
            double cx = ldX - 14 + c * 34 + carOffset - 17;
            if (cx < w * 0.62 || cx > w - 26) continue;
            var carRect = new Rect(cx, trackY - 14, 26, 12);
            ctx.FillRectangle(SteelFill, carRect, 2f);
            ctx.DrawRectangle(null, AmberPen, carRect, 2f, 2f);
        }
        DrawBadge(ctx, "LD-601", $"{batchTph:0} t/h", ldX - 24, h * 0.74, AmberBrush);
    }

    private static void DrawBadge(DrawingContext ctx, string tag, string metric, double x, double y, IBrush accent)
    {
        var ftTag = new FormattedText(
            tag,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
            10.0,
            accent);
        ctx.DrawText(ftTag, new Point(x, y));

        var ftVal = new FormattedText(
            metric,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            9.5,
            TextPrimaryBrush);
        ctx.DrawText(ftVal, new Point(x, y + 12));
    }
}
