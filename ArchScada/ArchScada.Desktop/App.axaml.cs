using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArchScada.Desktop.Controls;
using ArchScada.Desktop.Controls.ThreeD;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;
using ArchScada.Desktop.ViewModels;
using ArchScada.Desktop.Views;

namespace ArchScada.Desktop;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SplashWindow? _splashWindow;
    private bool _splashCompleted;
    private bool _isSplashCaptureMode;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var captureSplashPath = ParseArgValue(desktop.Args, "--capture-splash");
            if (!string.IsNullOrWhiteSpace(captureSplashPath))
            {
                _isSplashCaptureMode = true;
                var unityBridge = new UnityBridgeService();
                var readiness = new DeploymentReadinessService(unityBridge);
                var splashVm = new SplashViewModel(readiness, stepVisualDelayMs: 10, completionPauseMs: 0)
                {
                    HoldOnSplash = true
                };

                _splashWindow = new SplashWindow(splashVm, autoStart: false);
                desktop.MainWindow = _splashWindow;
                _splashWindow.Show();

                Dispatcher.UIThread.Post(
                    () => _ = RunAutomatedSplashCaptureAsync(desktop, _splashWindow, splashVm, unityBridge, captureSplashPath),
                    DispatcherPriority.Background);

                base.OnFrameworkInitializationCompleted();
                return;
            }

            var captureDir = ParseArgValue(desktop.Args, "--capture-3d-views");
            if (!string.IsNullOrWhiteSpace(captureDir))
            {
                var mainVm = new MainViewModel();
                _mainWindow = new MainWindow
                {
                    DataContext = mainVm,
                    Width = 1720,
                    Height = 1060
                };
                desktop.MainWindow = _mainWindow;
                _mainWindow.Show();

                if (mainVm.CurrentContent is MainShellViewModel shellVm)
                {
                    shellVm.NavigateCommand.Execute(PlantSection.DigitalTwin3D);
                    Dispatcher.UIThread.Post(
                        () => _ = RunAutomated3DCaptureAsync(desktop, _mainWindow, shellVm, captureDir),
                        DispatcherPriority.Background);
                }
                else
                {
                    desktop.Shutdown(1);
                }

                base.OnFrameworkInitializationCompleted();
                return;
            }

            // Show splash window first
            _splashWindow = new SplashWindow();
            _splashWindow.Show();

            // Create main window but keep it hidden until splash completes
            _mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void OnSplashCompleted()
    {
        if (_splashCompleted || _isSplashCaptureMode)
        {
            return;
        }

        _splashCompleted = true;

        var splash = _splashWindow;
        _splashWindow = null;
        splash?.Close();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _mainWindow;
            _mainWindow?.Show();
        }
    }

    private static string? ParseArgValue(string[]? args, string optionName)
    {
        if (args is null || args.Length == 0)
            return null;

        var prefix = optionName + "=";
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];

            if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return args[i][prefix.Length..];
        }

        return null;
    }

    private static async Task RunAutomatedSplashCaptureAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splashWindow,
        SplashViewModel splashVm,
        UnityBridgeService unityBridge,
        string outputPath)
    {
        int exitCode = 0;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await splashVm.RunStartupSequenceAsync();
            await Task.Delay(120);
            splashWindow.UpdateLayout();

            double scale = TopLevel.GetTopLevel(splashWindow)?.RenderScaling ?? 1.0;
            var clientSize = splashWindow.ClientSize;
            int pxW = Math.Max(1, (int)Math.Round(clientSize.Width * scale));
            int pxH = Math.Max(1, (int)Math.Round(clientSize.Height * scale));
            var dpi = new Vector(96.0 * scale, 96.0 * scale);

            using var bmp = new RenderTargetBitmap(new PixelSize(pxW, pxH), dpi);
            bmp.Render(splashWindow);
            bmp.Save(outputPath, new PngBitmapEncoderOptions());
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Console.Error.WriteLine($"Splash capture failed: {ex}");
        }
        finally
        {
            unityBridge.Dispose();
            desktop.Shutdown(exitCode);
        }
    }

    private static async Task RunAutomated3DCaptureAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        MainShellViewModel shellVm,
        string outputDir)
    {
        int exitCode = 0;
        try
        {
            Directory.CreateDirectory(outputDir);
            await Task.Delay(600);

            var viewport = mainWindow.GetVisualDescendants()
                .OfType<Plant3DViewportControl>()
                .FirstOrDefault();

            if (viewport is not null)
            {
                viewport.CaptureOpenGlFrame = true;
                viewport.FreezeSimulationTime = true;
            }

            var views = new (string FileName, string Preset, string NodeId)[]
            {
                ("01_isometric_plant_overview.png", "Plant Overview", "SS-101"),
                ("02_top_down_ga_layout.png", "Top-Down GA Layout", "SS-101"),
                ("03_stage1_loader_and_feeder_bin.png", "Stage 1: Loader & Feeder", "FEL-101"),
                ("04_stage2_3_jaw_crusher.png", "Stage 2–3: Jaw Crusher", "JC-101"),
                ("05_stage4_5_double_roll_crusher.png", "Stage 4–5: Double Roll Crusher", "DRC-101"),
                ("06_stage6_central_sizing_screen.png", "Stage 6: Central Sizing Screen", "SS-101"),
                ("07_wash_plant_desliming_thickener.png", "Wash Plant & Thickener", "WP-301"),
                ("08_side_elevation_west_inspection.png", "Side View (West)", "BN-101")
            };

            double scale = TopLevel.GetTopLevel(mainWindow)?.RenderScaling ?? 1.0;

            foreach (var (fileName, preset, nodeId) in views)
            {
                shellVm.UnityEffects.CameraPreset = preset;
                shellVm.Selected3DNode = Plant3DSceneData.FindNode(nodeId);

                if (viewport is not null)
                {
                    viewport.SnapCameraImmediate(simTimeOverride: 4.8);
                }

                await Task.Delay(280);
                if (viewport is not null && viewport.IsHardwareAccelerated && viewport.GetLastOpenGlBitmap() is null)
                {
                    await Task.Delay(250);
                }

                mainWindow.UpdateLayout();

                var clientSize = mainWindow.ClientSize;
                int winPxW = Math.Max(1, (int)Math.Round(clientSize.Width * scale));
                int winPxH = Math.Max(1, (int)Math.Round(clientSize.Height * scale));
                var dpi = new Vector(96.0 * scale, 96.0 * scale);

                using var winBmp = new RenderTargetBitmap(new PixelSize(winPxW, winPxH), dpi);
                winBmp.Render(mainWindow);

                var outPath = Path.Combine(outputDir, fileName);

                using var glBmp = viewport?.GetLastOpenGlBitmap();
                if (viewport is not null && glBmp is not null && viewport.Bounds.Width > 1 && viewport.Bounds.Height > 1)
                {
                    var vpPos = viewport.TranslatePoint(new Point(0, 0), mainWindow) ?? new Point(0, 0);
                    var vpSize = viewport.Bounds.Size;
                    int vpPxW = Math.Max(1, (int)Math.Round(vpSize.Width * scale));
                    int vpPxH = Math.Max(1, (int)Math.Round(vpSize.Height * scale));

                    using var overlayBmp = new RenderTargetBitmap(new PixelSize(vpPxW, vpPxH), dpi);
                    overlayBmp.Render(viewport.OverlayControl);

                    var composite = new Canvas
                    {
                        Width = clientSize.Width,
                        Height = clientSize.Height
                    };

                    var baseImg = new Image
                    {
                        Source = winBmp,
                        Width = clientSize.Width,
                        Height = clientSize.Height,
                        Stretch = Stretch.Fill
                    };
                    Canvas.SetLeft(baseImg, 0);
                    Canvas.SetTop(baseImg, 0);
                    composite.Children.Add(baseImg);

                    var glImg = new Image
                    {
                        Source = glBmp,
                        Width = vpSize.Width,
                        Height = vpSize.Height,
                        Stretch = Stretch.Fill
                    };
                    Canvas.SetLeft(glImg, vpPos.X);
                    Canvas.SetTop(glImg, vpPos.Y);
                    composite.Children.Add(glImg);

                    var hudImg = new Image
                    {
                        Source = overlayBmp,
                        Width = vpSize.Width,
                        Height = vpSize.Height,
                        Stretch = Stretch.Fill
                    };
                    Canvas.SetLeft(hudImg, vpPos.X);
                    Canvas.SetTop(hudImg, vpPos.Y);
                    composite.Children.Add(hudImg);

                    composite.Measure(clientSize);
                    composite.Arrange(new Rect(clientSize));

                    using var finalBmp = new RenderTargetBitmap(new PixelSize(winPxW, winPxH), dpi);
                    finalBmp.Render(composite);
                    finalBmp.Save(outPath, new PngBitmapEncoderOptions());
                }
                else
                {
                    winBmp.Save(outPath, new PngBitmapEncoderOptions());
                }
            }

            var metrics = Plant3DSceneData.BenchmarkScenePerformance();
            var perfReport = new
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                IsHardwareAccelerated = viewport?.IsHardwareAccelerated ?? false,
                LastFrameDrawCalls = viewport?.LastFrameDrawCalls ?? 0,
                metrics.TotalGlbCount,
                metrics.TotalVertices,
                metrics.TotalTriangles,
                metrics.TotalGpuVboMiB,
                metrics.ColdGlbLoadMs,
                metrics.FlowAndPoseMicrosPerFrame,
                metrics.ManagedMemoryDeltaBytes,
                metrics.Meshes
            };

            var json = JsonSerializer.Serialize(perfReport, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(outputDir, "perf_metrics.json"), json);
        }
        catch (Exception ex)
        {
            exitCode = 1;
            try
            {
                Directory.CreateDirectory(outputDir);
                await File.WriteAllTextAsync(Path.Combine(outputDir, "capture_error.txt"), ex.ToString());
            }
            catch
            {
                // Ignore secondary error while reporting capture failure.
            }
        }
        finally
        {
            desktop.Shutdown(exitCode);
        }
    }
}
