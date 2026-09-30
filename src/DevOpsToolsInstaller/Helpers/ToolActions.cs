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
        if (tool.IsDownloadedOrInstalled || tool.Status == ToolStatus.Downloading) return;

        if (!mw.DownloadQueue.Contains(tool))
        {
            mw.DownloadQueue.Add(tool);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                await mw.DownloadSvc.DownloadBatchAsync(new[] { tool }, dlFolder, maxConcurrency: 1);

                if (tool.Status == ToolStatus.Downloaded && installAfter)
                {
                    var res = ArtifactService.Perform(tool, dlFolder);
                    ToastService.Show(
                        $"{tool.Name} ready",
                        res.Success ? res.Message : $"{tool.Name} was downloaded but the install step needs attention.");
                }
                else if (tool.Status == ToolStatus.Downloaded)
                {
                    ToastService.Show($"{tool.Name} downloaded", "Ready to install from the Downloads page.");
                }
                else
                {
                    ToastService.Show($"{tool.Name} download failed", "Check your connection and try again.");
                }
            }
            catch
            {
                // Failures surface through the Downloads page and toasts.
            }
        });
    }
}
