using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed class BundleToolItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE74C";
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);
    public bool IsInstalled { get; set; }
}

public sealed class BundleCardViewModel
{
    public ToolBundle Bundle { get; }
    public string Id => Bundle.Id;
    public string Name => Bundle.Name;
    public string Description => Bundle.Description;
    public string Glyph => Bundle.Glyph;
    public string Category => Bundle.Category;
    public int TotalCount => Bundle.Tools.Count;
    public int InstalledCount { get; set; }
    public int PendingCount => Math.Max(0, TotalCount - InstalledCount);
    public bool IsFullyInstalled => InstalledCount >= TotalCount;
    public bool CanDownload => !IsFullyInstalled;
    public bool HasInstalledTools => InstalledCount > 0;

    public string StatusBadgeText => IsFullyInstalled
        ? "All tools installed"
        : $"{InstalledCount} of {TotalCount} installed";

    public string ToolsCountBadgeText => $"{TotalCount} Tools";

    public string DownloadButtonText => IsFullyInstalled
        ? "All Installed"
        : (InstalledCount > 0 ? $"Install Remaining ({PendingCount})" : "Install Stack");

    public string DownloadButtonGlyph => IsFullyInstalled ? "\uE73E" : "\uE896";

    public List<BundleToolItemViewModel> ToolItems { get; set; } = new();

    public List<BundleToolItemViewModel> PreviewTools => ToolItems.Take(4).ToList();

    public int RemainingToolsCount => Math.Max(0, TotalCount - 4);
    public bool HasRemainingTools => RemainingToolsCount > 0;
    public string RemainingToolsText => $"+{RemainingToolsCount}";

    public string RemainingToolsTooltip
    {
        get
        {
            var remaining = ToolItems.Skip(4).Select(t => t.Name).ToList();
            return remaining.Count > 0
                ? $"+{remaining.Count} more: {string.Join(", ", remaining)}"
                : "";
        }
    }

    public BundleCardViewModel(ToolBundle bundle)
    {
        Bundle = bundle;
    }
}

public sealed partial class StacksPage : Page
{
    private List<BundleCardViewModel> _allCardModels = new();
    private string _selectedCategory = "All";
    private ItemsWrapGrid? _stacksWrapGrid;

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
        UpdateScrollButtons();
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
                    bool isInst = tool.Status == ToolStatus.Downloaded || tool.IsInstalled;
                    if (isInst) installed++;

                    card.ToolItems.Add(new BundleToolItemViewModel
                    {
                        Id = tool.Id,
                        Name = tool.Name,
                        LogoUrl = tool.LogoUrl,
                        IconGlyph = tool.IconGlyph,
                        IsInstalled = isInst
                    });
                }
                else
                {
                    card.ToolItems.Add(new BundleToolItemViewModel
                    {
                        Id = toolId,
                        Name = toolId,
                        LogoUrl = string.Empty,
                        IconGlyph = "\uE74C",
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

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedBtn) return;
        var tag = clickedBtn.Tag as string ?? "All";
        _selectedCategory = tag;

        if (CategoryChipsPanel != null)
        {
            foreach (var child in CategoryChipsPanel.Children)
            {
                if (child is Button btn)
                {
                    bool isSelected = string.Equals(btn.Tag as string, _selectedCategory, StringComparison.OrdinalIgnoreCase);
                    btn.Style = (Style)Application.Current.Resources[isSelected ? "SelectedCategoryChipStyle" : "CategoryChipStyle"];
                }
            }
        }

        clickedBtn.StartBringIntoView();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";

        IEnumerable<BundleCardViewModel> filtered = _allCardModels;

        if (_selectedCategory != "All")
        {
            filtered = filtered.Where(c => string.Equals(c.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.ToolItems.Any(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     t.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var list = filtered.ToList();
        StacksGridView.ItemsSource = list;
        StatusText.Text = $"{list.Count} of {_allCardModels.Count} stacks shown";
        if (FooterStatusText != null)
        {
            FooterStatusText.Text = $"{list.Count} Stacks Available  •  DevOps Tools Installer";
        }
    }

    private void StacksGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _stacksWrapGrid ??= FindVisualChild<ItemsWrapGrid>(StacksGridView);
        if (_stacksWrapGrid != null)
        {
            var availableWidth = StacksGridView.ActualWidth - 24;
            if (availableWidth > 0)
            {
                int columns = Math.Max(1, Math.Min(3, (int)(availableWidth / 360)));
                _stacksWrapGrid.ItemWidth = (availableWidth / columns) - 14;
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        var target = Math.Max(0, ChipsScrollViewer.HorizontalOffset - 220);
        ChipsScrollViewer.ChangeView(target, null, null, false);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        var target = Math.Min(ChipsScrollViewer.ScrollableWidth, ChipsScrollViewer.HorizontalOffset + 220);
        ChipsScrollViewer.ChangeView(target, null, null, false);
    }

    private void ChipsScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs? e)
    {
        UpdateScrollButtons();
    }

    private void ChipsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateScrollButtons();
    }

    private void UpdateScrollButtons()
    {
        if (ChipsScrollViewer == null || ScrollLeftButton == null || ScrollRightButton == null) return;
        ScrollLeftButton.Visibility = ChipsScrollViewer.HorizontalOffset > 5 ? Visibility.Visible : Visibility.Collapsed;
        ScrollRightButton.Visibility = ChipsScrollViewer.HorizontalOffset < (ChipsScrollViewer.ScrollableWidth - 5)
            ? Visibility.Visible
            : Visibility.Collapsed;
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
                if (tool.IsInstalled || tool.Status == ToolStatus.Downloaded || tool.Status == ToolStatus.Downloading)
                {
                    continue;
                }

                tool.IsSelected = true;
                toDownload.Add(tool);
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

                var installedCount = toDownload.Count(t => t.Status == ToolStatus.Downloaded);
                ToastService.Show(
                    $"{targetBundle.Name}: {installedCount}/{toDownload.Count} downloaded",
                    "Downloaded tools are ready to deploy from the Downloads page.");
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
