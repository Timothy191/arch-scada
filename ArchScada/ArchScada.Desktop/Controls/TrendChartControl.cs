using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArchScada.Desktop.Controls;

public sealed class TrendChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<TrendChartControl, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<TrendChartControl, IBrush>(nameof(LineBrush), new SolidColorBrush(Color.Parse("#F59E0B")));

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    static TrendChartControl()
    {
        AffectsRender<TrendChartControl>(ValuesProperty, LineBrushProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var rect = new Rect(Bounds.Size);
        if (rect.Width <= 10 || rect.Height <= 10) return;

        var bg = new SolidColorBrush(Color.Parse("#111113"));
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#27272A")), 1);
        context.FillRectangle(bg, rect, 6);

        // Horizontal grid lines
        for (int i = 1; i <= 3; i++)
        {
            double y = rect.Height * i / 4.0;
            context.DrawLine(gridPen, new Point(0, y), new Point(rect.Width, y));
        }

        var data = Values;
        if (data is null || data.Count < 2) return;

        double min = data.Min();
        double max = data.Max();
        if (Math.Abs(max - min) < 0.001)
        {
            min -= 1;
            max += 1;
        }

        double pad = Math.Min(10.0, Math.Max(4.0, rect.Height * 0.16));
        double w = rect.Width - pad * 2;
        double h = rect.Height - pad * 2;
        var pen = new Pen(LineBrush, rect.Height < 60 ? 1.7 : 2.2);

        for (int i = 1; i < data.Count; i++)
        {
            double x1 = pad + w * (i - 1) / (data.Count - 1);
            double y1 = pad + h * (1.0 - (data[i - 1] - min) / (max - min));
            double x2 = pad + w * i / (data.Count - 1);
            double y2 = pad + h * (1.0 - (data[i] - min) / (max - min));
            context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        }
    }
}
