using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Manages scheduled and off-peak downloads, automatic retries for failed transfers,
/// and background queue execution.
/// </summary>
public sealed class DownloadSchedulerService : IDisposable
{
    private static DownloadSchedulerService? _instance;
    public static DownloadSchedulerService Instance => _instance ??= new DownloadSchedulerService();

    private readonly Timer _schedulerTimer;
    private readonly ConcurrentDictionary<string, DateTime> _failedTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private int _isProcessing = 0;

    public event Action? SchedulerTicked;

    private DownloadSchedulerService()
    {
        // Check every 30 seconds for scheduled window entries and auto-retries
        _schedulerTimer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
    }

    public static bool IsOffPeakActive => SettingsService.DownloadScheduleEnabled && !SettingsService.IsWithinScheduleWindow();

    /// <summary>
    /// Queues a tool download. If off-peak scheduling is active, marks it as Scheduled.
    /// Otherwise executes immediate background download.
    /// </summary>
    public void QueueDownload(ToolDefinition tool, bool installAfter = false)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        if (tool.IsDownloadedOrInstalled || tool.Status == ToolStatus.Downloading) return;

        if (!mw.DownloadQueue.Contains(tool))
        {
            mw.DownloadQueue.Add(tool);
        }

        if (IsOffPeakActive)
        {
            tool.Status = ToolStatus.Scheduled;
            tool.Progress = 0;
            ActivityLogService.Info(tool.Name, $"Queued for off-peak schedule ({SettingsService.GetScheduleWindowDescription()}).");
            if (SettingsService.EnableNotifications)
            {
                ToastService.Show(
                    $"{tool.Name} Scheduled",
                    $"Transfer scheduled for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).");
            }
        }
        else
        {
            ToolActions.RunInBackground(tool, installAfter);
        }
    }

    /// <summary>
    /// Bypasses schedule and immediately starts all currently scheduled downloads.
    /// </summary>
    public void StartAllScheduledNow()
    {
        _ = ProcessScheduledQueueAsync(bypassWindowCheck: true);
    }

    /// <summary>
    /// Records a tool failure timestamp for auto-retry backoff calculation.
    /// </summary>
    public void RecordFailure(string toolId)
    {
        _failedTimestamps[toolId] = DateTime.Now;
    }

    /// <summary>
    /// Clears recorded failure for a tool once it succeeds or is cancelled.
    /// </summary>
    public void ClearFailure(string toolId)
    {
        _failedTimestamps.TryRemove(toolId, out _);
    }

    private void OnTimerTick(object? state)
    {
        UiDispatcher.Run(() => SchedulerTicked?.Invoke());

        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessScheduledQueueAsync(bypassWindowCheck: false);
                await ProcessAutoRetriesAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        });
    }

    private async Task ProcessScheduledQueueAsync(bool bypassWindowCheck)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        bool canRun = bypassWindowCheck || SettingsService.IsWithinScheduleWindow();
        if (!canRun) return;

        List<ToolDefinition> scheduledTools;
        lock (mw.DownloadQueue)
        {
            scheduledTools = mw.DownloadQueue
                .Where(t => t.Status == ToolStatus.Scheduled)
                .ToList();
        }

        if (scheduledTools.Count == 0) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        ActivityLogService.Info("Scheduler", $"Starting {scheduledTools.Count} scheduled tool transfer(s).");

        try
        {
            await mw.DownloadSvc.DownloadBatchAsync(scheduledTools, dlFolder, maxConcurrency: 3);
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Scheduler", $"Error executing scheduled batch: {ex.Message}");
        }
    }

    private async Task ProcessAutoRetriesAsync()
    {
        var mw = App.MainWindowInstance;
        if (mw is null || !SettingsService.AutoRetryFailedDownloads) return;

        if (!SettingsService.IsWithinScheduleWindow()) return;

        var cutoff = DateTime.Now.AddMinutes(-Math.Max(1, SettingsService.AutoRetryIntervalMinutes));
        var toRetry = new List<ToolDefinition>();

        lock (mw.DownloadQueue)
        {
            foreach (var tool in mw.DownloadQueue)
            {
                if (tool.Status == ToolStatus.Failed &&
                    _failedTimestamps.TryGetValue(tool.Id, out var failedAt) &&
                    failedAt <= cutoff)
                {
                    toRetry.Add(tool);
                }
            }
        }

        if (toRetry.Count == 0) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        foreach (var tool in toRetry)
        {
            _failedTimestamps[tool.Id] = DateTime.Now; // Reset backoff timestamp
            ActivityLogService.Info(tool.Name, "Auto-retrying failed download after interval backoff.");
        }

        try
        {
            await mw.DownloadSvc.DownloadBatchAsync(toRetry, dlFolder, maxConcurrency: 2);
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("AutoRetry", $"Auto-retry batch encountered error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _schedulerTimer.Dispose();
    }
}
