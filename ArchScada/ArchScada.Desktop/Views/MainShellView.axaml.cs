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

    public MainShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnControlPropertyChanged;
    }

    private void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty && Bounds.Width > 0 && Bounds.Height > 0 && _vm is not null && _vm.IsDigitalTwin3D)
        {
            RefreshSparkline();
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
    }

    private void EnsurePlotConfigured()
    {
        if (_plotConfigured || SelectedNodeTrendPlot is null)
            return;

        _plotConfigured = true;
        var bg = new ScottPlot.Color(11, 18, 32);
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
                sig.Color = new ScottPlot.Color(56, 189, 248);
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
