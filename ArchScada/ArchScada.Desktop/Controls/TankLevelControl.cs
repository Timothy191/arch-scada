using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArchScada.Desktop.Controls;

public sealed class TankLevelControl : Control
{
    public static readonly StyledProperty<double> LevelPercentProperty =
        AvaloniaProperty.Register<TankLevelControl, double>(nameof(LevelPercent), 50.0);

    public static readonly StyledProperty<double> HighHighLimitProperty =
        AvaloniaProperty.Register<TankLevelControl, double>(nameof(HighHighLimit), 90.0);

    public static readonly StyledProperty<double> HighLimitProperty =
        AvaloniaProperty.Register<TankLevelControl, double>(nameof(HighLimit), 80.0);

    public static readonly StyledProperty<double> LowLimitProperty =
        AvaloniaProperty.Register<TankLevelControl, double>(nameof(LowLimit), 20.0);

    public static readonly StyledProperty<double> LowLowLimitProperty =
        AvaloniaProperty.Register<TankLevelControl, double>(nameof(LowLowLimit), 10.0);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<TankLevelControl, string>(nameof(Label), string.Empty);

    public double LevelPercent
    {
        get => GetValue(LevelPercentProperty);
        set => SetValue(LevelPercentProperty, value);
    }

    public double HighHighLimit
    {
        get => GetValue(HighHighLimitProperty);
        set => SetValue(HighHighLimitProperty, value);
    }

    public double HighLimit
    {
        get => GetValue(HighLimitProperty);
        set => SetValue(HighLimitProperty, value);
    }

    public double LowLimit
    {
        get => GetValue(LowLimitProperty);
        set => SetValue(LowLimitProperty, value);
    }

    public double LowLowLimit
    {
        get => GetValue(LowLowLimitProperty);
        set => SetValue(LowLowLimitProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    static TankLevelControl()
    {
        AffectsRender<TankLevelControl>(
            LevelPercentProperty,
            HighHighLimitProperty,
            HighLimitProperty,
            LowLimitProperty,
            LowLowLimitProperty,
            LabelProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 12 || rect.Height <= 12) return;

        var hasLabel = !string.IsNullOrWhiteSpace(Label);
        double topPad = hasLabel ? 20 : 4;
        double bottomPad = 4;
        double sidePad = 4;

        var tankRect = new Rect(
            sidePad,
            topPad,
            Math.Max(8, rect.Width - sidePad * 2),
            Math.Max(8, rect.Height - topPad - bottomPad));

        var bgBrush = new SolidColorBrush(Color.Parse("#0B1220"));
        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#334155")), 1.5);
        context.DrawRectangle(bgBrush, borderPen, tankRect, 6, 6);

        double level = Math.Clamp(LevelPercent, 0.0, 100.0);
        Color fillColor = level >= HighHighLimit || level <= LowLowLimit
            ? Color.Parse("#EF4444")
            : level >= HighLimit || level <= LowLimit
                ? Color.Parse("#F59E0B")
                : Color.Parse("#0EA5E9");

        double innerPad = 2.0;
        var innerRect = tankRect.Deflate(innerPad);
        double fillHeight = innerRect.Height * (level / 100.0);

        if (fillHeight > 0.5)
        {
            var fillRect = new Rect(
                innerRect.X,
                innerRect.Bottom - fillHeight,
                innerRect.Width,
                fillHeight);
            var fillBrush = new SolidColorBrush(fillColor, 0.82);
            context.FillRectangle(fillBrush, fillRect, 4);
        }

        DrawThresholdMarker(context, innerRect, HighHighLimit, Color.Parse("#EF4444"), "HH");
        DrawThresholdMarker(context, innerRect, HighLimit, Color.Parse("#F59E0B"), "H");
        DrawThresholdMarker(context, innerRect, LowLimit, Color.Parse("#F59E0B"), "L");
        DrawThresholdMarker(context, innerRect, LowLowLimit, Color.Parse("#EF4444"), "LL");

        if (hasLabel)
        {
            var labelText = new FormattedText(
                Label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11,
                new SolidColorBrush(Color.Parse("#CBD5E1")));
            context.DrawText(labelText, new Point(Math.Max(sidePad, (rect.Width - labelText.Width) / 2), 2));
        }

        var pctText = new FormattedText(
            $"{level:0.0}%",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            Brushes.White);
        context.DrawText(
            pctText,
            new Point(
                tankRect.X + (tankRect.Width - pctText.Width) / 2,
                tankRect.Y + (tankRect.Height - pctText.Height) / 2));
    }

    private static void DrawThresholdMarker(
        DrawingContext context,
        Rect innerRect,
        double thresholdPercent,
        Color color,
        string tag)
    {
        double clamped = Math.Clamp(thresholdPercent, 0.0, 100.0);
        double y = innerRect.Bottom - innerRect.Height * (clamped / 100.0);
        var pen = new Pen(new SolidColorBrush(color, 0.75), 1.0, new DashStyle(new[] { 3.0, 3.0 }, 0));
        context.DrawLine(pen, new Point(innerRect.X, y), new Point(innerRect.Right, y));

        if (innerRect.Width >= 36)
        {
            var tagText = new FormattedText(
                tag,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9,
                new SolidColorBrush(color, 0.9));
            context.DrawText(tagText, new Point(innerRect.X + 3, Math.Max(innerRect.Y, y - tagText.Height)));
        }
    }
}
