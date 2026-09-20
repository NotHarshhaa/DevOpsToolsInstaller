using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed class BundleToolItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
}

public sealed class BundleCardViewModel
{
    public ToolBundle Bundle { get; }
    public string Id => Bundle.Id;
    public string Name => Bundle.Name;
    public string Description => Bundle.Description;
    public string Glyph => Bundle.Glyph;
    public int TotalCount => Bundle.Tools.Count;
    public int InstalledCount { get; set; }
    public int PendingCount => Math.Max(0, TotalCount - InstalledCount);
    public bool IsFullyInstalled => InstalledCount >= TotalCount;
    public bool CanDownload => !IsFullyInstalled;

    public string StatusBadgeText => IsFullyInstalled
        ? "All tools installed"
        : $"{InstalledCount} of {TotalCount} installed";

    public string DownloadButtonText => IsFullyInstalled
        ? "All Installed"
        : $"Download Stack ({PendingCount})";

    public string DownloadButtonGlyph => IsFullyInstalled ? "\uE73E" : "\uE896";

    public List<BundleToolItemViewModel> ToolItems { get; set; } = new();

    public BundleCardViewModel(ToolBundle bundle)
    {
        Bundle = bundle;
    }
}

public sealed partial class StacksPage : Page
{
    private List<BundleCardViewModel> _allCardModels = new();

    public StacksPage()
    {
        InitializeComponent();
        Loaded += StacksPage_Loaded;
    }

    private async void StacksPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        await mw.EnsureCatalogLoadedAsync();

        RefreshCardModels();
        ApplyFilter();
    }

    private void RefreshCardModels()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        var list = new List<BundleCardViewModel>();
        foreach (var bundle in mw.Bundles)
        {
            var card = new BundleCardViewModel(bundle);
            int installed = 0;

            foreach (var toolId in bundle.Tools)
            {
                if (toolsById.TryGetValue(toolId, out var tool))
                {
                    bool isInst = tool.Status == ToolStatus.Downloaded;
                    if (isInst) installed++;

                    card.ToolItems.Add(new BundleToolItemViewModel
                    {
                        Id = tool.Id,
                        Name = tool.Name,
                        IsInstalled = isInst
                    });
                }
                else
                {
                    card.ToolItems.Add(new BundleToolItemViewModel
                    {
                        Id = toolId,
                        Name = toolId,
                        IsInstalled = false
                    });
                }
            }

            card.InstalledCount = installed;
            list.Add(card);
        }

        _allCardModels = list;
        HeaderCountBadge.Text = $"{_allCardModels.Count} Presets";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";

        IEnumerable<BundleCardViewModel> filtered = _allCardModels;

        if (!string.IsNullOrEmpty(query))
        {
            filtered = _allCardModels.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.ToolItems.Any(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     t.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var list = filtered.ToList();
        StacksList.ItemsSource = list;
        StatusText.Text = $"{list.Count} of {_allCardModels.Count} stacks shown";
    }

    private void CustomizeInCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string bundleId)
        {
            App.MainWindowInstance?.NavigateToCatalogWithBundle(bundleId);
        }
    }

    private async void InstallStack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string bundleId) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var targetBundle = mw.Bundles.FirstOrDefault(b => string.Equals(b.Id, bundleId, StringComparison.OrdinalIgnoreCase));
        if (targetBundle == null) return;

        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        // Find tools in this bundle that still need to be downloaded
        var toDownload = new List<ToolDefinition>();
        foreach (var tid in targetBundle.Tools)
        {
            if (toolsById.TryGetValue(tid, out var tool))
            {
                tool.IsSelected = true;
                if (tool.Status != ToolStatus.Downloaded && tool.Status != ToolStatus.Downloading)
                {
                    toDownload.Add(tool);
                }
            }
        }

        if (toDownload.Count == 0)
        {
            StatusText.Text = $"All tools in {targetBundle.Name} are already installed.";
            return;
        }

        // Add to queue
        foreach (var tool in toDownload)
        {
            if (!mw.DownloadQueue.Contains(tool))
            {
                mw.DownloadQueue.Add(tool);
            }
        }

        // Start background download
        _ = Task.Run(async () =>
        {
            try
            {
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                await mw.DownloadSvc.DownloadBatchAsync(toDownload, dlFolder, maxConcurrency: 3);
            }
            catch
            {
                // Managed by DownloadsPage
            }
        });

        // Navigate directly to Downloads page so the user immediately sees the live progress!
        mw.NavigateTo("Downloads");
    }

    private void BrowseCatalog_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Catalog");
    }
}
