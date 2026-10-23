using System;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Shared single-tool download/install action used by the tool detail page
/// and the Ctrl+K command palette. Runs on a background thread; progress is
/// visible on the Downloads page and completion lands in a toast.
/// </summary>
public static class ToolActions
{
    public static void RunInBackground(ToolDefinition tool, bool installAfter)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;
        if (tool.IsInstalled || tool.Status == ToolStatus.Downloading) return;
        if (tool.Status == ToolStatus.Downloaded && !installAfter) return;

        if (!mw.DownloadQueue.Contains(tool))
        {
            mw.DownloadQueue.Add(tool);
        }

        if (DownloadSchedulerService.IsOffPeakActive)
        {
            tool.Status = ToolStatus.Scheduled;
            tool.Progress = 0;
            ActivityLogService.Info(tool.Name, $"Transfer scheduled for off-peak window ({SettingsService.GetScheduleWindowDescription()}).");
            if (SettingsService.EnableNotifications)
            {
                ToastService.Show($"{tool.Name} Scheduled", $"Download scheduled for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).", NoteKind.Update);
            }
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var dlFolder = DownloadService.DefaultDownloadsFolder;

                if (tool.SelectedBackend != ToolBackend.Direct && installAfter)
                {
                    tool.Status = ToolStatus.Downloading;
                    var bRes = await ToolBackendService.InstallWithBackendAsync(tool, tool.SelectedBackend, dlFolder);
                    if (bRes.Success)
                    {
                        tool.IsInstalled = true;
                        tool.DetectedVersion = tool.SelectedVersion;
                        tool.Status = ToolStatus.Downloaded;
                        tool.Progress = 100;

                        _ = Task.Run(async () =>
                        {
                            var probe = await CliHealthService.ProbeToolAsync(tool, timeoutSeconds: 3);
                            if (probe.Success && !string.IsNullOrWhiteSpace(probe.DetectedVersion))
                            {
                                tool.DetectedVersion = probe.DetectedVersion;
                            }
                        });
                    }
                    else
                    {
                        tool.Status = ToolStatus.Failed;
                        tool.Progress = 0;
                    }

                    ToastService.Show(
                        $"{tool.Name} ({tool.SelectedBackend.ShortName()})",
                        bRes.Message,
                        bRes.Success ? NoteKind.Success : NoteKind.Error);
                    return;
                }

                await mw.DownloadSvc.DownloadBatchAsync(new[] { tool }, dlFolder, maxConcurrency: 1);

                if (tool.Status == ToolStatus.Downloaded && installAfter)
                {
                    var res = ArtifactService.Perform(tool, dlFolder);
                    ToastService.Show(
                        $"{tool.Name} ready",
                        res.Success ? res.Message : $"{tool.Name} was downloaded but the install step needs attention.",
                        res.Success ? NoteKind.Success : NoteKind.Warning);
                }
                else if (tool.Status == ToolStatus.Downloaded)
                {
                    ToastService.Show($"{tool.Name} downloaded", "Ready to install from the Downloads page.", NoteKind.Success);
                }
                else
                {
                    ToastService.Show($"{tool.Name} download failed", "Check your connection and try again.", NoteKind.Error);
                }
            }
            catch
            {
                // Failures surface through the Downloads page and toasts.
            }
        });
    }
}
