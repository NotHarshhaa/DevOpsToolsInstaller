using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class DownloadsPage : Page
{
    private string _searchFilter = string.Empty;
    private string _statusFilter = "All";
    private readonly ObservableCollection<ToolDefinition> _filteredQueue = new();

    public DownloadsPage()
    {
        InitializeComponent();
        Loaded += DownloadsPage_Loaded;
        LogList.ItemsSource = ActivityLogService.Entries;
    }

    private async void DownloadsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        await mw.EnsureCatalogLoadedAsync();

        // Also include any already-downloaded tools from the catalog
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        foreach (var tool in mw.Tools)
        {
            if (tool.Status == ToolStatus.Downloaded && !mw.DownloadQueue.Contains(tool))
            {
                mw.DownloadQueue.Add(tool);
            }
        }

        mw.DownloadQueue.CollectionChanged -= DownloadQueue_CollectionChanged;
        mw.DownloadQueue.CollectionChanged += DownloadQueue_CollectionChanged;

        DownloadsList.ItemsSource = _filteredQueue;
        ApplyFilter();
        UpdateMetrics();

        RefreshInstalledStates(mw.DownloadQueue.ToList(), dlFolder);
        RefreshSignatures(mw.DownloadQueue.ToList(), dlFolder);
    }

    private void DownloadQueue_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var items = mw.DownloadQueue.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var q = _searchFilter.Trim().ToLowerInvariant();
            items = items.Where(t => (t.Name != null && t.Name.ToLowerInvariant().Contains(q)) ||
                                     (t.Category != null && t.Category.ToLowerInvariant().Contains(q)) ||
                                     (t.Description != null && t.Description.ToLowerInvariant().Contains(q)) ||
                                     (t.FileName != null && t.FileName.ToLowerInvariant().Contains(q)));
        }

        items = _statusFilter switch
        {
            "Downloading" => items.Where(t => t.Status == ToolStatus.Downloading),
            "Ready" => items.Where(t => t.Status == ToolStatus.Downloaded && !t.IsInstalled),
            "Installed" => items.Where(t => t.IsInstalled),
            _ => items
        };

        var list = items.ToList();
        _filteredQueue.Clear();
        foreach (var item in list)
        {
            _filteredQueue.Add(item);
        }

        var total = mw.DownloadQueue.Count;
        EmptyState.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsList.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoFilterResultsPanel.Visibility = (total > 0 && _filteredQueue.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var total = mw.DownloadQueue.Count;
        var downloading = mw.DownloadQueue.Count(t => t.Status == ToolStatus.Downloading);
        var ready = mw.DownloadQueue.Count(t => t.Status == ToolStatus.Downloaded && !t.IsInstalled);
        var installed = mw.DownloadQueue.Count(t => t.IsInstalled);

        ActiveCountBadge.Text = downloading > 0
            ? $"{downloading} downloading"
            : ready > 0 ? $"{ready} ready" : $"{total} items";

        StatusText.Text = downloading > 0
            ? $"{downloading} downloading, {ready} ready to install"
            : $"{ready} ready to install • {installed} deployed";

        if (InstallAllReadyButton != null)
        {
            InstallAllReadyButton.IsEnabled = ready > 0;
            InstallAllReadyButton.Visibility = ready > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ClearFinishedButton != null)
        {
            ClearFinishedButton.IsEnabled = (installed > 0);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchFilter = SearchBox.Text;
        ApplyFilter();
    }

    private void FilterTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        _statusFilter = tag;

        FilterAllBtn.Style = (Style)Application.Current.Resources[tag == "All" ? "SelectedCategoryChipStyle" : "CategoryChipStyle"];
        FilterDownloadingBtn.Style = (Style)Application.Current.Resources[tag == "Downloading" ? "SelectedCategoryChipStyle" : "CategoryChipStyle"];
        FilterReadyBtn.Style = (Style)Application.Current.Resources[tag == "Ready" ? "SelectedCategoryChipStyle" : "CategoryChipStyle"];
        FilterInstalledBtn.Style = (Style)Application.Current.Resources[tag == "Installed" ? "SelectedCategoryChipStyle" : "CategoryChipStyle"];

        ApplyFilter();
    }

    private void LogToggle_Click(object sender, RoutedEventArgs e)
    {
        var isVisible = LogToggle.IsChecked == true;
        LogPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        LogRow.Height = isVisible ? new GridLength(240) : new GridLength(0);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        ActivityLogService.Clear();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var entries = ActivityLogService.Entries;
        if (entries.Count == 0) return;

        var text = string.Join(Environment.NewLine, entries.Select(x => $"[{x.TimeLabel}] [{x.Severity}] {x.ToolName}: {x.Message}"));
        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
    }

    private static void RefreshInstalledStates(
        List<ToolDefinition> tools, string downloadsFolder)
    {
        _ = Task.Run(() =>
        {
            foreach (var tool in tools)
            {
                tool.IsInstalled = UninstallService.IsInstalled(tool, downloadsFolder);
            }
        });
    }

    private static void RefreshSignatures(
        List<ToolDefinition> tools, string downloadsFolder)
    {
        _ = Task.Run(() =>
        {
            foreach (var tool in tools)
            {
                if (tool.Status == ToolStatus.Downloaded)
                {
                    var filePath = Path.Combine(downloadsFolder, tool.FileName);
                    if (File.Exists(filePath))
                    {
                        var sig = AuthenticodeService.VerifyFile(filePath);
                        tool.SignatureResult = sig;
                    }
                }
            }
        });
    }

    private async void RunInstaller_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var filePath = Path.Combine(dlFolder, tool.FileName);

        if (tool.Kind == ArtifactKind.Installer)
        {
            if (tool.SignatureResult is null && File.Exists(filePath))
            {
                tool.SignatureResult = AuthenticodeService.VerifyFile(filePath);
            }

            if (tool.SignatureResult is not null && !tool.SignatureResult.IsValid)
            {
                var warningDialog = new ContentDialog
                {
                    Title = "Digital Signature Warning",
                    Content = $"The installer for '{tool.Name}' does not have a verified digital signature from a trusted certificate authority.\n\n" +
                              $"Status: {tool.SignatureResult.StatusBadge}\n" +
                              $"Details: {tool.SignatureResult.Summary}\n\n" +
                              "DevOpsToolsInstaller checks signatures to protect your workstation. Do you still wish to launch this vendor installer?",
                    PrimaryButtonText = "Launch Installer",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                if (await warningDialog.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }
        }

        var result = ArtifactService.Perform(tool, dlFolder);
        StatusText.Text = result.Message;

        RefreshInstalledStates(new List<ToolDefinition> { tool }, dlFolder);
        UpdateMetrics();
    }

    private async void InstallAllReady_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var readyTools = mw.DownloadQueue.Where(t => t.IsDownloaded && !t.IsInstalled).ToList();
        if (readyTools.Count == 0) return;

        int processed = 0;
        foreach (var tool in readyTools)
        {
            var res = ArtifactService.Perform(tool, dlFolder);
            if (res.Success)
            {
                tool.IsInstalled = true;
                processed++;
            }
        }

        RefreshInstalledStates(readyTools, dlFolder);
        StatusText.Text = $"Processed {processed}/{readyTools.Count} tools.";
        UpdateMetrics();
    }

    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var toRemove = mw.DownloadQueue.Where(t => t.IsInstalled && t.Status != ToolStatus.Downloading).ToList();
        foreach (var tool in toRemove)
        {
            mw.DownloadQueue.Remove(tool);
        }
        ApplyFilter();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var filePath = Path.Combine(dlFolder, tool.FileName);
        LauncherService.OpenFileInFolder(filePath);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var mw = App.MainWindowInstance;
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
            tool.Status = ToolStatus.NotDownloaded;
            tool.Progress = 0;

            RefreshInstalledStates(new List<ToolDefinition> { tool }, dlFolder);

            if (mw is not null)
            {
                var completed = mw.DownloadQueue.Count(t => t.Status == ToolStatus.Downloaded);
                StatusText.Text = $"{result.Message} ({completed}/{mw.DownloadQueue.Count} ready to install)";
            }
            UpdateMetrics();
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

    private void GoToStacks_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Stacks");
    }
}
