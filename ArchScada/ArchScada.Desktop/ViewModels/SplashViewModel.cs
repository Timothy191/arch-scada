using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ArchScada.Desktop.Models;
using ArchScada.Desktop.Services;

namespace ArchScada.Desktop.ViewModels;

public partial class SplashViewModel : ViewModelBase
{
    private readonly IDeploymentReadinessService _readinessService;
    private readonly int _stepVisualDelayMs;
    private readonly int _completionPauseMs;
    private int _completedSignaled;

    public ObservableCollection<StartupCheckItem> Checks { get; } = new();

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Initializing Arch SCADA...";

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "0%";

    [ObservableProperty]
    public partial bool IsComplete { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailures))]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    public partial int PassedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    public partial int WarningCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailures))]
    [NotifyPropertyChangedFor(nameof(LaunchButtonText))]
    public partial int FailedCount { get; set; }

    [ObservableProperty]
    public partial string ReadinessBannerTitle { get; set; } = "CHECKING DEPLOYMENT REQUIREMENTS...";

    [ObservableProperty]
    public partial string ReadinessBannerDetail { get; set; } = "0/8 verified · 0 Passed · 0 Warnings · 0 Failed";

    [ObservableProperty]
    public partial StartupCheckState ReadinessState { get; set; } = StartupCheckState.Running;

    [ObservableProperty]
    public partial bool HoldOnSplash { get; set; }

    public bool HasFailures => FailedCount > 0;
    public bool HasWarnings => WarningCount > 0;
    public string LaunchButtonText => HasFailures ? "Override & Launch" : "Launch Plant HMI";

    public string AppTitle => "Arch SCADA";
    public string AppSubtitle => "Coal Processing Plant HMI · Deployment Readiness Gate";
    public string VersionLabel => "v6.5-arch  |  .NET 10 / Avalonia 12";

    public event EventHandler<bool>? Completed;

    public SplashViewModel(UnityBridgeService unityBridge)
        : this(new DeploymentReadinessService(unityBridge))
    {
    }

    public SplashViewModel(
        IDeploymentReadinessService readinessService,
        int stepVisualDelayMs = 90,
        int completionPauseMs = 850)
    {
        _readinessService = readinessService ?? throw new ArgumentNullException(nameof(readinessService));
        _stepVisualDelayMs = Math.Max(0, stepVisualDelayMs);
        _completionPauseMs = Math.Max(0, completionPauseMs);

        foreach (var spec in _readinessService.Checks)
        {
            Checks.Add(new StartupCheckItem
            {
                Id = spec.Id,
                Category = spec.Category,
                Title = spec.Title,
                IsCritical = spec.IsCritical,
                Detail = "Pending...",
                State = StartupCheckState.Pending,
                DurationText = string.Empty
            });
        }

        ReadinessBannerDetail = $"0/{Checks.Count} verified · 0 Passed · 0 Warnings · 0 Failed";
    }

    public async Task RunStartupSequenceAsync(CancellationToken ct = default)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        IsComplete = false;
        Progress = 0.0;
        ProgressText = "0%";
        PassedCount = 0;
        WarningCount = 0;
        FailedCount = 0;
        ReadinessState = StartupCheckState.Running;
        ReadinessBannerTitle = "CHECKING DEPLOYMENT REQUIREMENTS...";
        StatusText = "Verifying deployment prerequisites...";

        int total = Checks.Count;
        ReadinessBannerDetail = $"0/{total} verified · 0 Passed · 0 Warnings · 0 Failed";

        foreach (var item in Checks)
        {
            item.State = StartupCheckState.Pending;
            item.Detail = "Pending...";
            item.DurationText = string.Empty;
        }

        try
        {
            IReadOnlyList<DeploymentCheckSpec> specs = _readinessService.Checks;
            for (int i = 0; i < specs.Count && i < Checks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var spec = specs[i];
                var item = Checks[i];
                item.State = StartupCheckState.Running;
                item.Detail = $"Checking {spec.Category}...";
                StatusText = $"Verifying [{spec.Category}] {spec.Title}...";

                var sw = Stopwatch.StartNew();
                StartupCheckState state;
                string detail;
                try
                {
                    (state, detail) = await spec.ExecuteAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
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

                item.DurationText = sw.ElapsedMilliseconds < 1 ? "<1 ms" : $"{sw.ElapsedMilliseconds} ms";
                item.State = state;
                item.Detail = detail;

                switch (state)
                {
                    case StartupCheckState.Passed:
                        PassedCount++;
                        break;
                    case StartupCheckState.Warning:
                        WarningCount++;
                        break;
                    case StartupCheckState.Failed:
                        FailedCount++;
                        break;
                }

                Progress = total > 0 ? ((double)(i + 1) / total) * 100.0 : 100.0;
                ProgressText = $"{Progress:F0}%";
                ReadinessBannerDetail = $"{i + 1}/{total} verified · {PassedCount} Passed · {WarningCount} Warnings · {FailedCount} Failed";

                if (_stepVisualDelayMs > 0)
                {
                    await Task.Delay(_stepVisualDelayMs, ct);
                }
            }

            IsComplete = true;
            ReadinessState = HasFailures
                ? StartupCheckState.Failed
                : (HasWarnings ? StartupCheckState.Warning : StartupCheckState.Passed);

            ReadinessBannerTitle = HasFailures
                ? "DEPLOYMENT BLOCKED — CRITICAL CHECK FAILED"
                : (HasWarnings ? "SYSTEM READY (DEMO / FALLBACK MODE)" : "SYSTEM READY FOR DEPLOYMENT");

            ReadinessBannerDetail = $"{total}/{total} verified · {PassedCount} Passed · {WarningCount} Warnings · {FailedCount} Failed";
            StatusText = HasFailures
                ? "Critical deployment check failed — inspect results or override"
                : (HoldOnSplash ? "Inspection hold active — click Launch Plant HMI when ready" : "Ready — launching Plant Operations...");
        }
        catch (OperationCanceledException)
        {
            StatusText = "Verification canceled";
            return;
        }
        finally
        {
            IsRunning = false;
        }

        if (!HasFailures && !HoldOnSplash && !ct.IsCancellationRequested)
        {
            if (_completionPauseMs > 0)
            {
                try
                {
                    await Task.Delay(_completionPauseMs, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (!HasFailures && !HoldOnSplash && !ct.IsCancellationRequested)
            {
                RaiseCompletedOnce(true);
            }
            else if (HoldOnSplash && !HasFailures)
            {
                StatusText = "Inspection hold active — click Launch Plant HMI when ready";
            }
        }
    }

    private void RaiseCompletedOnce(bool offlineDemoMode)
    {
        if (Interlocked.Exchange(ref _completedSignaled, 1) == 0)
        {
            Completed?.Invoke(this, offlineDemoMode);
        }
    }

    [RelayCommand]
    private void SkipSplash()
    {
        RaiseCompletedOnce(true);
    }

    [RelayCommand]
    private void ContinueToApp()
    {
        RaiseCompletedOnce(true);
    }

    [RelayCommand]
    private void LaunchAnyway()
    {
        RaiseCompletedOnce(true);
    }

    [RelayCommand]
    private async Task RerunChecksAsync()
    {
        if (IsRunning)
        {
            return;
        }

        Interlocked.Exchange(ref _completedSignaled, 0);
        await RunStartupSequenceAsync();
    }
}
