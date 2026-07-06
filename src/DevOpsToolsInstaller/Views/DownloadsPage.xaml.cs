using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class DownloadsPage : Page
{
    public DownloadsPage()
    {
        InitializeComponent();
        Loaded += DownloadsPage_Loaded;
    }

    private void DownloadsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        // Also include any already-downloaded tools from the catalog
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        foreach (var tool in mw.Tools)
        {
            if (tool.Status == ToolStatus.Downloaded && !mw.DownloadQueue.Contains(tool))
            {
                mw.DownloadQueue.Add(tool);
            }
        }

        DownloadsList.ItemsSource = mw.DownloadQueue;

        var hasItems = mw.DownloadQueue.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        DownloadsList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        if (hasItems)
        {
            var completed = mw.DownloadQueue.Count(t => t.Status == ToolStatus.Downloaded);
            StatusText.Text = $"{completed}/{mw.DownloadQueue.Count} ready to install";

            RefreshInstalledStates(mw.DownloadQueue.ToList(), dlFolder);
        }
    }

    /// <summary>
    /// Detects, off the UI thread, which queued tools have something the app
    /// can remove (extracted folder, copied binary, or a Windows uninstall
    /// entry) and flips their <see cref="ToolDefinition.IsInstalled"/> flag so
    /// the Uninstall/Remove button enables itself. The registry scan for
    /// installer-kind tools can be slow, so it never runs on the UI thread.
    /// </summary>
    private static void RefreshInstalledStates(
        System.Collections.Generic.List<ToolDefinition> tools, string downloadsFolder)
    {
        _ = Task.Run(() =>
        {
            foreach (var tool in tools)
            {
                // The IsInstalled setter marshals its PropertyChanged back to
                // the UI thread, so this is safe to set from a worker thread.
                tool.IsInstalled = UninstallService.IsInstalled(tool, downloadsFolder);
            }
        });
    }

    private void RunInstaller_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var result = ArtifactService.Perform(tool, DownloadService.DefaultDownloadsFolder);
        StatusText.Text = result.Message;

        // The action may have just created what we can later remove
        // (extracted folder / copied binary), so re-check its state.
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        RefreshInstalledStates(new System.Collections.Generic.List<ToolDefinition> { tool }, dlFolder);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        var (title, body, primary) = tool.Kind switch
        {
            ArtifactKind.Installer => (
                $"Uninstall {tool.Name}?",
                $"This opens the vendor uninstaller for {tool.Name}, which shows its own " +
                "prompts (and a UAC prompt if needed). The cached download will also be removed.",
                "Uninstall"),
            ArtifactKind.Archive => (
                $"Remove {tool.Name}?",
                $"This deletes the extracted files for {tool.Name} from your Tools folder " +
                "and removes the cached download. You can re-download and extract it anytime.",
                "Remove"),
            ArtifactKind.Binary => (
                $"Remove {tool.Name}?",
                $"This deletes {tool.FileName} from Tools\\bin and removes the cached " +
                "download. You can add it back anytime.",
                "Remove"),
            _ => (
                $"Delete {tool.Name}?",
                "This removes the cached download.",
                "Delete")
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = body,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var result = UninstallService.Uninstall(tool, dlFolder, removeDownload: true);
        StatusText.Text = result.Message;

        if (result.Success)
        {
            // Reflect that the artifact is gone: reset download status and
            // re-check installed state (installer uninstalls run async in the
            // vendor UI, so their entry may linger until that finishes).
            if (tool.Kind != ArtifactKind.Installer)
            {
                tool.Status = ToolStatus.NotDownloaded;
                tool.Progress = 0;
            }

            RefreshInstalledStates(
                new System.Collections.Generic.List<ToolDefinition> { tool }, dlFolder);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        LauncherService.OpenDownloadsFolder(DownloadService.DefaultDownloadsFolder);
    }

    private void GoToCatalog_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Catalog");
    }
}
