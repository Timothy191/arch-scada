using System;
using System.ComponentModel;
using System.Linq;
using ArchScada.Desktop.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArchScada.Desktop.Views;

public partial class MainShellView : UserControl
{
    private MainShellViewModel? _vm;
    private bool _plotConfigured;
    private bool _historianPlotConfigured;

    public MainShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnControlPropertyChanged;
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty && Bounds.Width > 0 && Bounds.Height > 0 && _vm is not null)
        {
            if (_vm.IsDigitalTwin3D) RefreshSparkline();
            if (_vm.IsTrends) RefreshHistorianPlot();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as MainShellViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            RefreshSparkline();
            RefreshHistorianPlot();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainShellViewModel.Selected3DTrendValues)
            or nameof(MainShellViewModel.Selected3DTrendLabel)
            or nameof(MainShellViewModel.IsDigitalTwin3D))
        {
            if (Dispatcher.UIThread.CheckAccess())
                RefreshSparkline();
            else
                Dispatcher.UIThread.Post(RefreshSparkline);
        }

        if (e.PropertyName is nameof(MainShellViewModel.SelectedTrendValues)
            or nameof(MainShellViewModel.SelectedTrendTag)
            or nameof(MainShellViewModel.SelectedHistorianPreset)
            or nameof(MainShellViewModel.IsTrends))
        {
            if (Dispatcher.UIThread.CheckAccess())
                RefreshHistorianPlot();
            else
                Dispatcher.UIThread.Post(RefreshHistorianPlot);
        }
    }

    private void EnsureHistorianPlotConfigured()
    {
        if (_historianPlotConfigured || HistorianAvaPlot is null)
            return;

        _historianPlotConfigured = true;
        var plt = HistorianAvaPlot.Plot;
        plt.FigureBackground.Color = new ScottPlot.Color(17, 17, 19);
        plt.DataBackground.Color = new ScottPlot.Color(20, 20, 24);
        plt.Axes.Color(new ScottPlot.Color(161, 161, 170));
        plt.Grid.MajorLineColor = new ScottPlot.Color(39, 39, 42);
    }

    private void RefreshHistorianPlot()
    {
        if (_vm is null || !_vm.IsTrends || HistorianAvaPlot is null)
            return;

        try
        {
            EnsureHistorianPlotConfigured();
            var plt = HistorianAvaPlot.Plot;
            plt.Clear();

            string preset = _vm.SelectedHistorianPreset ?? "Single Tag (Selected)";
            string[]? overlayIds = preset switch
            {
                var p when p.StartsWith("DMC-301", StringComparison.OrdinalIgnoreCase) => new[] { "DM301_PRS", "MS301_REC" },
                var p when p.StartsWith("Thickener", StringComparison.OrdinalIgnoreCase) => new[] { "TK201_LVL", "TK201_TRB" },
                var p when p.StartsWith("Crushing", StringComparison.OrdinalIgnoreCase) => new[] { "CR101_TPH", "CV101_TPH" },
                var p when p.StartsWith("Quality", StringComparison.OrdinalIgnoreCase) => new[] { "AN601_ASH", "AN601_MOIST", "AN601_CV" },
                _ => null
            };

            if (overlayIds is not null)
            {
                ScottPlot.Color[] palette =
                {
                    new(245, 158, 11),
                    new(34, 197, 94),
                    new(251, 191, 36)
                };

                for (int i = 0; i < overlayIds.Length; i++)
                {
                    var history = _vm.Telemetry.GetTagHistory(overlayIds[i]);
                    if (history.Count < 2) continue;
                    double[] ys = history is double[] a ? a : history.ToArray();
                    var sig = plt.Add.Signal(ys);
                    sig.Color = palette[i % palette.Length];
                    sig.LineWidth = 2.0f;
                    sig.LegendText = overlayIds[i];
                }
                plt.ShowLegend();
                plt.Axes.AutoScale();
            }
            else
            {
                var values = _vm.SelectedTrendValues;
                if (values is not null && values.Count >= 2)
                {
                    double[] ys = values is double[] arr ? arr : values.ToArray();
                    var sig = plt.Add.Signal(ys);
                    sig.Color = new ScottPlot.Color(245, 158, 11);
                    sig.LineWidth = 2.2f;

                    double minVal = ys.Min();
                    double maxVal = ys.Max();
                    double pad = Math.Max((maxVal - minVal) * 0.22, 0.5);

                    if (_vm.SelectedTrendTag is { } tag)
                    {
                        var hi = plt.Add.HorizontalLine(tag.MaxLimit * 0.85);
                        hi.Color = new ScottPlot.Color(217, 119, 6, 150);
                        hi.LineWidth = 1.1f;
                        hi.LinePattern = ScottPlot.LinePattern.Dashed;
                    }

                    plt.Axes.AutoScale();
                    plt.Axes.SetLimitsY(minVal - pad, maxVal + pad);
                }
            }

            HistorianAvaPlot.Refresh();
        }
        catch
        {
            // Keep UI resilient during headless/teardown passes
        }
    }

    private void EnsurePlotConfigured()
    {
        if (_plotConfigured || SelectedNodeTrendPlot is null)
            return;

        _plotConfigured = true;
        var bg = new ScottPlot.Color(17, 17, 19);
        var plt = SelectedNodeTrendPlot.Plot;
        plt.FigureBackground.Color = bg;
        plt.DataBackground.Color = bg;
        plt.Axes.Frameless();
        plt.Layout.Frameless();
        plt.HideGrid();
        SelectedNodeTrendPlot.UserInputProcessor.IsEnabled = false;
    }

    private void RefreshSparkline()
    {
        if (_vm is null || !_vm.IsDigitalTwin3D || SelectedNodeTrendPlot is null)
            return;

        try
        {
            EnsurePlotConfigured();

            var values = _vm.Selected3DTrendValues;
            var plt = SelectedNodeTrendPlot.Plot;
            plt.Clear();

            if (values is not null && values.Count >= 2)
            {
                double[] ys = values is double[] arr ? arr : values.ToArray();
                var sig = plt.Add.Signal(ys);
                sig.Color = new ScottPlot.Color(245, 158, 11);
                sig.LineWidth = 1.6f;
                sig.MaximumMarkerSize = 0;

                double minVal = ys[0];
                double maxVal = ys[0];
                for (int i = 1; i < ys.Length; i++)
                {
                    if (ys[i] < minVal) minVal = ys[i];
                    if (ys[i] > maxVal) maxVal = ys[i];
                }
                double span = Math.Max(maxVal - minVal, 1e-3);

                if (_vm.Selected3DTrendMaxLimit is double maxLim &&
                    maxLim >= minVal - span * 0.8 &&
                    maxLim <= maxVal + span * 1.2)
                {
                    var hl = plt.Add.HorizontalLine(maxLim);
                    hl.Color = new ScottPlot.Color(245, 158, 11, 140);
                    hl.LineWidth = 1.0f;
                    hl.LinePattern = ScottPlot.LinePattern.Dashed;
                }

                plt.Axes.AutoScale();
                double pad = Math.Max((maxVal - minVal) * 0.18, 0.25);
                plt.Axes.SetLimitsY(minVal - pad, maxVal + pad);
            }

            SelectedNodeTrendPlot.Refresh();
        }
        catch
        {
            // Keep UI resilient during headless/teardown passes
        }
    }
}
