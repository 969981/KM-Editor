// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Windows.Threading;
using KM.Setup.UI.Burn;

namespace KM.Setup.UI.ViewModels;

internal sealed partial class InstallerViewModel
{
    private InstallerProgressSnapshot progressSnapshot = new();
    private DispatcherTimer? progressTimer;
    private int reportedProgressPercent;
    private long operationStarted = Stopwatch.GetTimestamp();
    private string elapsedText = string.Empty;
    private string lastUpdateText = string.Empty;

    public string ProgressActivity => progressSnapshot.ActionKey is { } key ? strings[key] : string.Empty;
    public bool ShowProgressActivity => !string.IsNullOrEmpty(ProgressActivity);
    public string ProgressFile => progressSnapshot.FileName;
    public bool ShowProgressFile => ProgressFile.Length != 0;
    public string ElapsedText => elapsedText;
    public string LastUpdateText => lastUpdateText;
    public string ProgressScopeLabel => strings[progressSnapshot.PackagePercent.HasValue
        ? "ProgressPackageScope" : progressSnapshot.CachePercent.HasValue ? "ProgressCacheScope" : "CurrentPackageLabel"];

    public string ProgressDetail => progressSnapshot.TotalBytes > 0
        ? strings.Format("ProgressBytes", progressSnapshot.Bytes, progressSnapshot.TotalBytes)
        : string.Empty;
    public bool ShowProgressDetail => ProgressDetail.Length != 0;

    private void UpdateProgressClock(InstallerPhase value)
    {
        if (value is InstallerPhase.Detecting or InstallerPhase.Planning)
        {
            operationStarted = Stopwatch.GetTimestamp();
        }
        if (!ShowProgressPage)
        {
            progressTimer?.Stop();
            return;
        }

        RefreshProgress();
        if (!adapter.ShouldShowWindow) return;
        if (progressTimer is null)
        {
            progressTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            progressTimer.Tick += (_, _) => RefreshProgress();
        }
        progressTimer.Start();
    }

    private void RefreshProgress()
    {
        var latest = adapter.ProgressDetails.Snapshot;
        // Show the measured preparation or current package percentage, with an
        // explicit scope label. Burn's bundle ticks can stay at 50 throughout MSI.
        if (!completionPending) ProgressPercent = Math.Clamp(
            latest.PackagePercent ?? latest.CachePercent ?? Volatile.Read(ref reportedProgressPercent), 0, 99);
        if (latest != progressSnapshot)
        {
            var previous = progressSnapshot;
            progressSnapshot = latest;
            if (latest.ActionKey != previous.ActionKey)
            {
                OnPropertyChanged(nameof(ProgressActivity));
                OnPropertyChanged(nameof(ShowProgressActivity));
            }
            if (latest.FileName != previous.FileName)
            {
                OnPropertyChanged(nameof(ProgressFile));
                OnPropertyChanged(nameof(ShowProgressFile));
            }
            if (latest.Bytes != previous.Bytes || latest.TotalBytes != previous.TotalBytes ||
                latest.PackagePercent != previous.PackagePercent || latest.RollingBack != previous.RollingBack)
            {
                OnPropertyChanged(nameof(ProgressDetail));
                OnPropertyChanged(nameof(ShowProgressDetail));
            }
            if (latest.PackagePercent.HasValue != previous.PackagePercent.HasValue ||
                latest.CachePercent.HasValue != previous.CachePercent.HasValue)
            {
                OnPropertyChanged(nameof(ProgressScopeLabel));
            }
            if (latest.RollingBack != previous.RollingBack)
            {
                OnPropertyChanged(nameof(IsIndeterminate));
                OnPropertyChanged(nameof(ShowProgressPercentage));
                RefreshCopy();
            }
        }
        SetProperty(ref elapsedText, strings.Format("ProgressElapsed", DurationSince(operationStarted)), nameof(ElapsedText));
        SetProperty(ref lastUpdateText, progressSnapshot.LastUpdate == 0 ? string.Empty :
            strings.Format("ProgressLastUpdate", DurationSince(progressSnapshot.LastUpdate)), nameof(LastUpdateText));
        // The cancellation boundary may close on an engine error before the
        // next phase callback arrives. Keep the button in sync while waiting.
        OnPropertyChanged(nameof(CanCancel));
        cancelCommand.RaiseCanExecuteChanged();
    }

    private static string DurationSince(long timestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        return $"{(long)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}
