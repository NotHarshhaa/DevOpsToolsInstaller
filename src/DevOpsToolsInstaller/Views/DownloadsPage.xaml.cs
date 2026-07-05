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
        }
    }

    private void RunInstaller_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        try
        {
            LauncherService.Launch(tool, DownloadService.DefaultDownloadsFolder);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to launch {tool.Name}: {ex.Message}";
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
