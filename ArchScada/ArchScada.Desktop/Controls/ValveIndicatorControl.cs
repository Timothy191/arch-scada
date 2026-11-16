using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArchScada.Desktop.Controls;

public enum ValveState
{
    Open,
    Closed,
    Transitioning,
    Faulted
}

public sealed class ValveIndicatorControl : Control
{
    public static readonly StyledProperty<ValveState> StateProperty =
        AvaloniaProperty.Register<ValveIndicatorControl, ValveState>(nameof(State), ValveState.Closed);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ValveIndicatorControl, string>(nameof(Label), string.Empty);

    public ValveState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    static ValveIndicatorControl()
    {
        AffectsRender<ValveIndicatorControl>(StateProperty, LabelProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 12 || rect.Height <= 12) return;

        var (stateColor, stateText) = State switch
        {
            ValveState.Open => (Color.Parse("#10B981"), "OPEN"),
            ValveState.Closed => (Color.Parse("#64748B"), "CLOSED"),
            ValveState.Transitioning => (Color.Parse("#F59E0B"), "MOVING"),
            ValveState.Faulted => (Color.Parse("#EF4444"), "FAULT"),
            _ => (Color.Parse("#64748B"), "UNKNOWN")
        };

        var fillBrush = new SolidColorBrush(stateColor, 0.85);
        var strokePen = new Pen(new SolidColorBrush(Color.Parse("#E2E8F0")), 1.4);

        double cx = rect.Width / 2.0;
        double cy = rect.Height / 2.0;
        double halfW = Math.Min(rect.Width * 0.32, 22);
        double halfH = Math.Min(rect.Height * 0.24, 14);

        // Actuator stem and cap
        double stemTop = Math.Max(4, cy - halfH - 10);
        context.DrawLine(strokePen, new Point(cx, cy), new Point(cx, stemTop));
        context.DrawLine(strokePen, new Point(cx - halfW * 0.45, stemTop), new Point(cx + halfW * 0.45, stemTop));

        // Bowtie valve body (left & right triangles meeting at center)
        var leftTriangle = new StreamGeometry();
        using (var ctx = leftTriangle.Open())
        {
            ctx.BeginFigure(new Point(cx - halfW, cy - halfH), true);
            ctx.LineTo(new Point(cx, cy));
            ctx.LineTo(new Point(cx - halfW, cy + halfH));
            ctx.EndFigure(true);
        }

        var rightTriangle = new StreamGeometry();
        using (var ctx = rightTriangle.Open())
        {
            ctx.BeginFigure(new Point(cx + halfW, cy - halfH), true);
            ctx.LineTo(new Point(cx, cy));
            ctx.LineTo(new Point(cx + halfW, cy + halfH));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fillBrush, strokePen, leftTriangle);
        context.DrawGeometry(fillBrush, strokePen, rightTriangle);

        if (!string.IsNullOrWhiteSpace(Label))
        {
            var labelText = new FormattedText(
                Label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10,
                new SolidColorBrush(Color.Parse("#CBD5E1")));
            context.DrawText(labelText, new Point((rect.Width - labelText.Width) / 2.0, 2));
        }

        var statusFormatted = new FormattedText(
            stateText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9,
            new SolidColorBrush(stateColor));
        context.DrawText(
            statusFormatted,
            new Point((rect.Width - statusFormatted.Width) / 2.0, Math.Min(rect.Height - statusFormatted.Height - 2, cy + halfH + 3)));
    }
}
