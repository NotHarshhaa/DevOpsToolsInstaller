using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class InstalledPage : Page
{
    public InstalledPage()
    {
        InitializeComponent();
        Loaded += InstalledPage_Loaded;
    }

    private async void InstalledPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanInstalledToolsAsync();
    }

    private async Task ScanInstalledToolsAsync()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        StatusText.Text = "Scanning installed tools…";
        await mw.EnsureCatalogLoadedAsync();

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        // Scan on a background thread (registry scan can be slow)
        var installedTools = await Task.Run(() =>
        {
            var results = new List<ToolDefinition>();
            foreach (var tool in mw.Tools)
            {
                if (UninstallService.IsInstalled(tool, dlFolder))
                {
                    tool.IsInstalled = true;
                    results.Add(tool);
                }
            }
            return results;
        });

        InstalledList.ItemsSource = installedTools;

        var count = installedTools.Count;
        InstalledCountText.Text = $"{count} deployed";

        if (count > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            InstalledList.Visibility = Visibility.Visible;
            StatusText.Text = $"{count} tool(s) detected on your workstation";
        }
        else
        {
            EmptyState.Visibility = Visibility.Visible;
            InstalledList.Visibility = Visibility.Collapsed;
            StatusText.Text = "No installed tools found";
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        var (title, body, primary) = tool.Kind switch
        {
            ArtifactKind.Installer => (
                $"Uninstall {tool.Name}?",
                $"This opens the vendor uninstaller for {tool.Name}.",
                "Uninstall"),
            ArtifactKind.Archive => (
                $"Remove {tool.Name}?",
                $"This deletes the extracted files for {tool.Name} from your Tools folder.",
                "Remove"),
            ArtifactKind.Binary => (
                $"Remove {tool.Name}?",
                $"This deletes {tool.FileName} from Tools\\bin.",
                "Remove"),
            _ => (
                $"Delete {tool.Name}?",
                "This removes the installed files.",
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

        var result = UninstallService.Uninstall(tool, dlFolder, removeDownload: false);
        StatusText.Text = result.Message;

        if (result.Success)
        {
            tool.IsInstalled = false;
            // Re-scan to refresh the list
            await ScanInstalledToolsAsync();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = ScanInstalledToolsAsync();
    }

    private void OpenToolsFolder_Click(object sender, RoutedEventArgs e)
    {
        var toolsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevOpsToolsInstaller", "Tools");
        if (!Directory.Exists(toolsFolder))
            Directory.CreateDirectory(toolsFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = toolsFolder,
            UseShellExecute = true
        });
    }

    private void GoToCatalog_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Catalog");
    }
}
