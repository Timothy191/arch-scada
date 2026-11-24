using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--verify-deployment", StringComparison.OrdinalIgnoreCase)))
        {
            return RunCliDeploymentVerificationAsync().GetAwaiter().GetResult();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static async Task<int> RunCliDeploymentVerificationAsync()
    {
        using var unityBridge = new UnityBridgeService();
        var readiness = new DeploymentReadinessService(unityBridge);

        Console.WriteLine("Arch SCADA — Deployment Readiness & System Requirements Verification");
        Console.WriteLine(new string('=', 74));

        int passed = 0;
        int warned = 0;
        int failed = 0;
        var totalSw = Stopwatch.StartNew();

        foreach (var spec in readiness.Checks)
        {
            var sw = Stopwatch.StartNew();
            StartupCheckState state;
            string detail;
            try
            {
                (state, detail) = await spec.ExecuteAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                state = spec.IsCritical ? StartupCheckState.Failed : StartupCheckState.Warning;
                detail = ex.Message;
            }
            finally
            {
                sw.Stop();
            }

            string tag = state switch
            {
                StartupCheckState.Passed => "[PASS]",
                StartupCheckState.Warning => "[WARN]",
                StartupCheckState.Failed => "[FAIL]",
                _ => "[INFO]"
            };

            switch (state)
            {
                case StartupCheckState.Passed:
                    passed++;
                    break;
                case StartupCheckState.Warning:
                    warned++;
                    break;
                case StartupCheckState.Failed:
                    failed++;
                    break;
            }

            string elapsed = sw.ElapsedMilliseconds < 1 ? "<1 ms" : $"{sw.ElapsedMilliseconds} ms";
            Console.WriteLine($"{tag} [{spec.Category,-7}] {spec.Title} ({elapsed})");
            Console.WriteLine($"       {detail}");
        }

        totalSw.Stop();
        Console.WriteLine(new string('-', 74));
        Console.WriteLine(
            $"Summary: {readiness.Checks.Count}/{readiness.Checks.Count} checked in {totalSw.ElapsedMilliseconds} ms | " +
            $"{passed} Passed | {warned} Warnings | {failed} Failed");

        return failed == 0 ? 0 : 1;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
