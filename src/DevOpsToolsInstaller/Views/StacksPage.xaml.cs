using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;
using DevOpsToolsInstaller.ViewModels;

namespace DevOpsToolsInstaller.Views;

/// <summary>One tool inside a stack card's preview list.</summary>
public sealed class BundleToolItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE74C";
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);
    public bool IsInstalled { get; set; }

    /// <summary>Catalog version, prefixed with "v" (empty when unknown).</summary>
    public string VersionText { get; set; } = string.Empty;

    /// <summary>Install state shown in the preview row's tooltip.</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>"Name • v1.2.3 • Installed" — keeps identical icons distinguishable.</summary>
    public string TooltipText { get; set; } = string.Empty;
}

public sealed class BundleCardViewModel
{
    /// <summary>How many tools a card previews before collapsing into "+N more".</summary>
    private const int PreviewLimit = 3;

    public ToolBundle Bundle { get; }
    public string Id => Bundle.Id;
    public string Name => Bundle.Name;
    public string Description => Bundle.Description;
    public string Glyph => Bundle.Glyph;
    public string Category => Bundle.Category;
    public int TotalCount => Bundle.Tools.Count;
    public int InstalledCount { get; set; }
    public int PendingCount => Math.Max(0, TotalCount - InstalledCount);
    public bool IsFullyInstalled => TotalCount > 0 && InstalledCount >= TotalCount;
    public bool CanDownload => !IsFullyInstalled;

    /// <summary>"3 of 7 installed" progress caption.</summary>
    public string ProgressText => $"{InstalledCount} of {TotalCount} installed";

    /// <summary>Progress bar value (installed tools).</summary>
    public double ProgressValue => InstalledCount;

    /// <summary>Progress bar maximum; never zero so the bar renders cleanly.</summary>
    public double ProgressMax => Math.Max(1, TotalCount);

    /// <summary>"est. 42.5 MB" — the stack's total estimated download footprint.</summary>
    public string EstimatedSizeText { get; set; } = string.Empty;

    public string ToolsCountBadgeText => $"{TotalCount} Tools";

    public string DownloadButtonText => IsFullyInstalled
        ? "All Installed"
        : (InstalledCount > 0 ? $"Install Remaining ({PendingCount})" : "Install Stack");

    public string DownloadButtonGlyph => IsFullyInstalled ? "\uE73E" : "\uE896";

    public List<BundleToolItemViewModel> ToolItems { get; set; } = new();

    public List<BundleToolItemViewModel> PreviewTools => ToolItems.Take(PreviewLimit).ToList();

    public int RemainingToolsCount => Math.Max(0, TotalCount - PreviewLimit);
    public bool HasRemainingTools => RemainingToolsCount > 0;
    public string RemainingToolsText => $"+{RemainingToolsCount} more tool{(RemainingToolsCount == 1 ? "" : "s")}";

    public string RemainingToolsTooltip
    {
        get
        {
            var remaining = ToolItems.Skip(PreviewLimit).Select(t => t.TooltipText).ToList();
            return remaining.Count > 0
                ? $"Also includes: {string.Join("; ", remaining)}"
                : "";
        }
    }

    public BundleCardViewModel(ToolBundle bundle)
    {
        Bundle = bundle;
    }
}

/// <summary>
/// One row in the stack detail pane: a tool with its version, size and install
/// state, plus a checkbox that decides whether it is queued for install.
/// </summary>
public sealed class StackToolDetailViewModel : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "\uE74C";
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);

    /// <summary>Catalog version, prefixed with "v" (or "—" when unknown).</summary>
    public string VersionText { get; set; } = "—";

    /// <summary>Estimated download size, formatted (or "—" when unknown).</summary>
    public string SizeText { get; set; } = "—";

    /// <summary>Raw estimate, summed across the checked rows for the footer.</summary>
    public long EstimatedBytes { get; set; }

    public string StatusText { get; set; } = string.Empty;
    public bool IsInstalledOrDownloaded { get; set; }
    public bool IsDownloading { get; set; }

    /// <summary>Only tools that still need something are selectable.</summary>
    public bool CanSelect => !IsInstalledOrDownloaded && !IsDownloading;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class StacksPage : Page, IRefreshablePage
{
    public StackBuilderViewModel BuilderViewModel { get; } = new();

    private List<BundleCardViewModel> _allCardModels = new();
    private string _selectedCategory = "All";
    private ItemsWrapGrid? _stacksWrapGrid;
    private double _measuredForWidth = -1;
    private ToolDefinition? _draggedTool;
    private bool _isBuilderMode;

    private readonly ObservableCollection<StackToolDetailViewModel> _detailTools = new();
    private BundleCardViewModel? _detailCard;

    public StacksPage()
    {
        InitializeComponent();
        CategorySegmented.SelectionChanged += CategorySegmented_SelectionChanged;
        ViewModeSegmented.SelectionChanged += ViewModeSegmented_SelectionChanged;
        DetailToolsList.ItemsSource = _detailTools;
        Loaded += StacksPage_Loaded;

        BuilderViewModel.StackTools.CollectionChanged += (s, e) =>
        {
            if (_isBuilderMode && Header != null)
            {
                Header.BadgeText = BuilderViewModel.ToolsCountBadgeText;
            }
        };
    }

    private async void StacksPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        // Show the loading ring while the catalog hydrates.
        StacksLoading.Visibility = Visibility.Visible;
        StacksGridView.Visibility = Visibility.Collapsed;
        try
        {
            await mw.EnsureCatalogLoadedAsync();
        }
        finally
        {
            StacksLoading.Visibility = Visibility.Collapsed;
            StacksGridView.Visibility = Visibility.Visible;
        }

        BuilderViewModel.Initialize(mw.Tools);

        RefreshCardModels();
        ApplyFilter();
        UpdateScrollButtons();
    }

    void IRefreshablePage.RefreshView()
    {
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

                    var version = string.IsNullOrWhiteSpace(tool.Version) ? string.Empty : $"v{tool.Version}";
                    var status = isInst
                        ? "Installed"
                        : tool.Status == ToolStatus.Downloading ? "Downloading" : "Not installed";

                    card.ToolItems.Add(new BundleToolItemViewModel
                    {
                        Id = tool.Id,
                        Name = tool.Name,
                        LogoUrl = tool.LogoUrl,
                        IconGlyph = tool.IconGlyph,
                        IsInstalled = isInst,
                        VersionText = version,
                        StatusText = status,
                        TooltipText = BuildTooltip(tool.Name, version, status)
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
                        IsInstalled = false,
                        StatusText = "Not in catalog",
                        TooltipText = BuildTooltip(toolId, string.Empty, "Not in catalog")
                    });
                }
            }

            card.InstalledCount = installed;
            card.EstimatedSizeText =
                $"est. {CustomStacksService.FormatBytes(CustomStacksService.EstimateDownloadSizeBytes(bundle.Tools, mw.Tools))}";
            list.Add(card);
        }

        _allCardModels = list;
        _measuredForWidth = -1;

        if (!_isBuilderMode && Header != null)
        {
            Header.BadgeText = $"{_allCardModels.Count} Presets";
        }

        // Keep an open detail pane in sync with the rebuilt models.
        if (_detailCard is not null)
        {
            var refreshed = _allCardModels.FirstOrDefault(c =>
                string.Equals(c.Id, _detailCard.Id, StringComparison.OrdinalIgnoreCase));

            if (refreshed is not null)
            {
                PopulateDetailPane(refreshed);
            }
            else if (StackDetailSplitView is not null)
            {
                // The stack (e.g. a deleted custom one) is gone.
                StackDetailSplitView.IsPaneOpen = false;
            }
        }
    }

    private static string BuildTooltip(string name, string version, string status)
    {
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(version)) parts.Add(version);
        if (!string.IsNullOrWhiteSpace(status)) parts.Add(status);
        return string.Join("  •  ", parts);
    }

    // ── Mode Switcher ────────────────────────────────────────────────────────

    private void ViewModeSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not CommunityToolkit.WinUI.Controls.Segmented segmented ||
            segmented.SelectedItem is not CommunityToolkit.WinUI.Controls.SegmentedItem selected)
        {
            return;
        }

        if (PresetsContainerGrid is null || BuilderContainerGrid is null)
        {
            return;
        }

        var mode = selected.Tag as string ?? "Presets";
        _isBuilderMode = string.Equals(mode, "Builder", StringComparison.OrdinalIgnoreCase);

        if (_isBuilderMode)
        {
            // The detail pane describes a preset stack, so it makes no sense
            // alongside the builder composer.
            if (StackDetailSplitView is not null) StackDetailSplitView.IsPaneOpen = false;

            PresetsContainerGrid.Visibility = Visibility.Collapsed;
            BuilderContainerGrid.Visibility = Visibility.Visible;

            if (Header != null) Header.Title = "Custom Stack Builder";
            if (Header != null) Header.Subtitle = "Assemble custom toolsets by dragging catalog tools or using '+ Add'. Save, export, or install in 1-click.";
            if (Header != null) Header.BadgeText = BuilderViewModel.ToolsCountBadgeText;
        }
        else
        {
            PresetsContainerGrid.Visibility = Visibility.Visible;
            BuilderContainerGrid.Visibility = Visibility.Collapsed;

            if (Header != null) Header.Title = "Stacks & Bundles";
            if (Header != null) Header.Subtitle = "Pre-configured toolsets for common DevOps workflows. Install entire stacks in 1-click, or build your own custom stack.";
            if (Header != null) Header.BadgeText = $"{_allCardModels.Count} Presets";
        }
    }

    public void SelectBuilderMode()
    {
        if (ViewModeSegmented != null)
        {
            foreach (var item in ViewModeSegmented.Items.OfType<CommunityToolkit.WinUI.Controls.SegmentedItem>())
            {
                if (string.Equals(item.Tag as string, "Builder", StringComparison.OrdinalIgnoreCase))
                {
                    ViewModeSegmented.SelectedItem = item;
                    break;
                }
            }
        }
    }

    // ── Presets Filtering ───────────────────────────────────────────────────

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void CategorySegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not CommunityToolkit.WinUI.Controls.Segmented segmented ||
            segmented.SelectedItem is not CommunityToolkit.WinUI.Controls.SegmentedItem selected)
        {
            return;
        }
        _selectedCategory = selected.Tag as string ?? "All";
        selected.StartBringIntoView();
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
        RelayoutStacksGrid();
        StatusText.Text = $"{list.Count} of {_allCardModels.Count} stacks shown";
        if (FooterStatusText != null)
        {
            FooterStatusText.Text = $"{list.Count} Stacks Available  •  DevOps Tools Installer";
        }
    }

    private void StacksGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RelayoutStacksGrid();
    }

    /// <summary>
    /// Sizes the wrap-grid cells: columns fit the viewport, and the cell height
    /// is measured from the tallest card. <see cref="ItemsWrapGrid"/> falls back
    /// to the <em>first</em> cell's height when <c>ItemHeight</c> is left unset,
    /// which would clip any taller card, so the height is always set explicitly.
    /// </summary>
    private void RelayoutStacksGrid()
    {
        if (StacksGridView is null) return;

        _stacksWrapGrid ??= FindVisualChild<ItemsWrapGrid>(StacksGridView);
        if (_stacksWrapGrid is null) return;

        var availableWidth = StacksGridView.ActualWidth - 24;
        if (availableWidth <= 0) return;

        int columns = Math.Max(1, Math.Min(3, (int)(availableWidth / 360)));
        var itemWidth = (availableWidth / columns) - 14;
        _stacksWrapGrid.ItemWidth = itemWidth;

        // Re-measuring instantiates every card template, so only do it when the
        // width has moved materially.
        if (Math.Abs(itemWidth - _measuredForWidth) < 24) return;

        var itemHeight = MeasureTallestCard(itemWidth);
        if (itemHeight <= 0) return;

        _stacksWrapGrid.ItemHeight = itemHeight;
        _measuredForWidth = itemWidth;
    }

    /// <summary>Measures the card template against every stack and returns the tallest height.</summary>
    private double MeasureTallestCard(double itemWidth)
    {
        if (StacksGridView?.ItemTemplate is not DataTemplate template || _allCardModels.Count == 0)
        {
            return 0;
        }

        const double containerMargin = 12; // GridViewItem Margin="6"
        var contentWidth = Math.Max(80, itemWidth - containerMargin);
        double tallest = 0;

        foreach (var model in _allCardModels)
        {
            if (template.LoadContent() is not FrameworkElement content) continue;

            content.DataContext = model;
            content.Measure(new Windows.Foundation.Size(contentWidth, double.PositiveInfinity));
            tallest = Math.Max(tallest, content.DesiredSize.Height);
        }

        return tallest > 0 ? Math.Ceiling(tallest) + containerMargin : 0;
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

    // ── Stack detail pane ────────────────────────────────────────────────────

    private void StacksGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BundleCardViewModel vm)
        {
            OpenStackDetailPane(vm);
        }
    }

    private void ViewStackTools_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BundleCardViewModel vm })
        {
            OpenStackDetailPane(vm);
        }
    }

    private void OpenStackDetailPane(BundleCardViewModel card)
    {
        PopulateDetailPane(card);
        StackDetailSplitView.IsPaneOpen = true;
    }

    private void PopulateDetailPane(BundleCardViewModel card)
    {
        _detailCard = card;

        DetailNameText.Text = card.Name;
        DetailCategoryText.Text = string.IsNullOrWhiteSpace(card.Category) ? "Stack" : $"{card.Category}  •  {card.ToolsCountBadgeText}";
        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(card.Description)
            ? "No description was provided for this stack."
            : card.Description;
        DetailGlyph.Glyph = string.IsNullOrWhiteSpace(card.Glyph) ? "\uE71D" : card.Glyph;

        _detailTools.Clear();

        var mw = App.MainWindowInstance;
        var catalog = (IReadOnlyList<ToolDefinition>?)mw?.Tools ?? Array.Empty<ToolDefinition>();
        var toolsById = mw?.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase)
                        ?? new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolId in card.Bundle.Tools)
        {
            toolsById.TryGetValue(toolId, out var tool);

            bool installed = tool is not null && (tool.IsInstalled || tool.Status == ToolStatus.Downloaded);
            bool downloading = tool?.Status == ToolStatus.Downloading;
            long size = tool is not null
                ? CustomStacksService.EstimateDownloadSizeBytes(new[] { tool.Id }, catalog)
                : 0;

            _detailTools.Add(new StackToolDetailViewModel
            {
                Id = toolId,
                Name = tool?.Name ?? toolId,
                LogoUrl = tool?.LogoUrl ?? string.Empty,
                IconGlyph = string.IsNullOrWhiteSpace(tool?.IconGlyph) ? "\uE74C" : tool!.IconGlyph,
                VersionText = string.IsNullOrWhiteSpace(tool?.Version) ? "—" : $"v{tool!.Version}",
                SizeText = size > 0 ? CustomStacksService.FormatBytes(size) : "—",
                EstimatedBytes = size,
                IsInstalledOrDownloaded = installed,
                IsDownloading = downloading,
                StatusText = tool is null ? "Not in catalog"
                    : installed ? "Installed"
                    : downloading ? "Downloading…"
                    : "Not installed",
                IsSelected = tool is not null && !installed && !downloading
            });
        }

        UpdateDetailSummary();
    }

    private void UpdateDetailSummary()
    {
        int total = _detailTools.Count;
        int installed = _detailTools.Count(t => t.IsInstalledOrDownloaded);

        DetailProgressText.Text = $"{installed} of {total} installed";
        DetailProgressBar.Maximum = Math.Max(1, total);
        DetailProgressBar.Value = installed;

        var selectable = _detailTools.Where(t => t.CanSelect).ToList();
        var selected = selectable.Where(t => t.IsSelected).ToList();
        long selectedBytes = selected.Sum(t => t.EstimatedBytes);

        DetailEstSizeText.Text = selected.Count > 0
            ? $"≈ {CustomStacksService.FormatBytes(selectedBytes)} to download"
            : "Nothing selected";

        DetailInstallButton.IsEnabled = selected.Count > 0;
        DetailInstallText.Text = selected.Count > 0
            ? $"Install {selected.Count} selected"
            : "Nothing to install";

        DetailSelectAllButton.Content = selectable.Count > 0 && selected.Count == selectable.Count
            ? "Clear selection"
            : "Select all";

        DetailEmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DetailToolCheck_Click(object sender, RoutedEventArgs e) => UpdateDetailSummary();

    /// <summary>Clicking anywhere on a row toggles its include checkbox.</summary>
    private void DetailToolRow_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StackToolDetailViewModel row && row.CanSelect)
        {
            row.IsSelected = !row.IsSelected;
            UpdateDetailSummary();
        }
    }

    private void DetailSelectAll_Click(object sender, RoutedEventArgs e)
    {
        var selectable = _detailTools.Where(t => t.CanSelect).ToList();
        bool allSelected = selectable.Count > 0 && selectable.All(t => t.IsSelected);

        foreach (var tool in selectable)
        {
            tool.IsSelected = !allSelected;
        }

        UpdateDetailSummary();
    }

    private void DetailClose_Click(object sender, RoutedEventArgs e)
    {
        if (StackDetailSplitView is not null)
        {
            StackDetailSplitView.IsPaneOpen = false;
        }
    }

    private void StackDetailSplitView_PaneClosed(SplitView sender, object args)
    {
        // PaneClosed arrives after the close animation, so a rapid re-open can
        // land first — don't clobber that pane's state.
        if (sender.IsPaneOpen) return;

        _detailCard = null;
        _detailTools.Clear();
    }

    private void DetailOpenCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_detailCard is not null)
        {
            App.MainWindowInstance?.NavigateToCatalogWithBundle(_detailCard.Id);
        }
    }

    private void DetailInstall_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || _detailCard is null) return;

        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var toDownload = new List<ToolDefinition>();

        foreach (var row in _detailTools)
        {
            if (!row.IsSelected || !row.CanSelect) continue;

            if (toolsById.TryGetValue(row.Id, out var tool) &&
                !tool.IsInstalled &&
                tool.Status != ToolStatus.Downloaded &&
                tool.Status != ToolStatus.Downloading &&
                tool.Status != ToolStatus.Scheduled)
            {
                toDownload.Add(tool);
            }
        }

        if (toDownload.Count == 0)
        {
            StatusText.Text = "Nothing selected to install.";
            return;
        }

        StartStackDownload(toDownload, _detailCard.Name);
    }

    private void ToolLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not Image image) return;

        image.Visibility = Visibility.Collapsed;

        // Reveal the glyph fallback sitting behind the image in the same grid.
        if (VisualTreeHelper.GetParent(image) is Panel panel)
        {
            foreach (var child in panel.Children.OfType<FontIcon>())
            {
                child.Visibility = Visibility.Visible;
            }
        }
    }

    // ── Downloads ────────────────────────────────────────────────────────────

    private void CustomizeInCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement btn && btn.Tag is string bundleId)
        {
            App.MainWindowInstance?.NavigateToCatalogWithBundle(bundleId);
        }
    }

    private void OpenInStackBuilder_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        ToolBundle? targetBundle = null;
        if (sender is FrameworkElement { DataContext: BundleCardViewModel vm })
        {
            targetBundle = vm.Bundle;
        }
        else if (sender is FrameworkElement { Tag: string bundleId })
        {
            targetBundle = mw.Bundles.FirstOrDefault(b => string.Equals(b.Id, bundleId, StringComparison.OrdinalIgnoreCase));
        }

        if (targetBundle != null)
        {
            BuilderViewModel.LoadBundle(targetBundle);
            SelectBuilderMode();
            ShowInfoBar(InfoBarSeverity.Informational, "Stack Loaded", $"Loaded preset '{targetBundle.Name}' into the stack builder.");
        }
    }

    private void InstallStack_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
    {
        if (sender is not FrameworkElement btn || btn.Tag is not string bundleId) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var targetBundle = mw.Bundles.FirstOrDefault(b => string.Equals(b.Id, bundleId, StringComparison.OrdinalIgnoreCase));
        if (targetBundle == null) return;

        // Find the tools in this bundle that still need to be downloaded.
        var toDownload = ResolvePendingTools(mw, targetBundle.Tools);

        if (toDownload.Count == 0)
        {
            StatusText.Text = $"All tools in {targetBundle.Name} are already installed.";
            return;
        }

        StartStackDownload(toDownload, targetBundle.Name);
    }

    /// <summary>
    /// The catalog tools behind <paramref name="toolIds"/> that still have
    /// something to download (not installed, not already queued).
    /// </summary>
    private static List<ToolDefinition> ResolvePendingTools(MainWindow mw, IEnumerable<string> toolIds)
    {
        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var pending = new List<ToolDefinition>();

        foreach (var toolId in toolIds)
        {
            if (!toolsById.TryGetValue(toolId, out var tool)) continue;
            if (tool.IsInstalled || tool.Status is ToolStatus.Downloaded or ToolStatus.Downloading or ToolStatus.Scheduled) continue;

            pending.Add(tool);
        }

        return pending;
    }

    /// <summary>
    /// Queues <paramref name="toDownload"/> (or schedules them for off-peak
    /// hours), kicks off the batch download and lands the user on Downloads.
    /// </summary>
    private void StartStackDownload(IReadOnlyList<ToolDefinition> toDownload, string stackName)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || toDownload.Count == 0) return;

        foreach (var tool in toDownload)
        {
            tool.IsSelected = true;
            if (!mw.DownloadQueue.Contains(tool))
            {
                mw.DownloadQueue.Add(tool);
            }
        }

        if (DownloadSchedulerService.IsOffPeakActive)
        {
            foreach (var tool in toDownload)
            {
                tool.Status = ToolStatus.Scheduled;
                tool.Progress = 0;
            }
            ActivityLogService.Info(stackName, $"Stack transfer scheduled for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).");
            if (SettingsService.EnableNotifications)
            {
                ToastService.Show($"{stackName} Scheduled", $"Stack queued for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).");
            }
            mw.NavigateTo("Downloads");
            return;
        }

        // Start background download.
        _ = Task.Run(async () =>
        {
            try
            {
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                await mw.DownloadSvc.DownloadBatchAsync(toDownload, dlFolder, maxConcurrency: 3);

                var installedCount = toDownload.Count(t => t.Status == ToolStatus.Downloaded);
                ToastService.Show(
                    $"{stackName}: {installedCount}/{toDownload.Count} downloaded",
                    "Downloaded tools are ready to deploy from the Downloads page.");
            }
            catch
            {
                // Managed by DownloadsPage
            }
        });

        // Navigate directly to Downloads so the user immediately sees live progress.
        mw.NavigateTo("Downloads");
    }

    private async void ExportStackDevContainer_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        ToolBundle? targetBundle = null;
        if (sender is FrameworkElement { DataContext: BundleCardViewModel vm })
        {
            targetBundle = vm.Bundle;
        }
        else if (sender is FrameworkElement { Tag: string bundleId })
        {
            var card = _allCardModels.FirstOrDefault(c => c.Id == bundleId);
            targetBundle = card?.Bundle;
        }

        if (targetBundle is null) return;

        var stackTools = mw.Tools.Where(t => targetBundle.Tools.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (stackTools.Count == 0) return;

        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var safeName = targetBundle.Id.ToLowerInvariant().Replace(' ', '-');
            var filePath = System.IO.Path.Combine(folder, $"devcontainer-stack-{safeName}-{DateTime.Now:yyyyMMdd-HHmm}.zip");

            DevContainerGeneratorService.ExportDevContainerZip(stackTools, filePath, targetBundle.Name);

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "DevContainer Stack Exported",
                Content = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"DevContainer package for {targetBundle.Name} ({stackTools.Count} tools) generated at:",
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBox { Text = filePath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap },
                            new TextBlock
                            {
                                Text = "Extract into your repository root and launch in VS Code with Remote-Containers to develop inside an isolated environment with this exact stack pre-configured.",
                                FontSize = 12,
                                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"DevContainer export failed: {ex.Message}";
        }
    }

    private void BrowseCatalog_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Catalog");
    }

    private void MenuViewDownloads_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Downloads");
    }

    // ── Custom Stack Builder Handlers ────────────────────────────────────────

    private void AvailableToolsListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is ToolDefinition tool)
        {
            _draggedTool = tool;
            e.Data.SetText(tool.Id);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void MyStackArea_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to My Stack";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void MyStackArea_Drop(object sender, DragEventArgs e)
    {
        ToolDefinition? tool = _draggedTool;
        if (tool == null && e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            try
            {
                var text = await e.DataView.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var mw = App.MainWindowInstance;
                    tool = mw?.Tools.FirstOrDefault(t => string.Equals(t.Id, text, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { }
        }

        if (tool != null)
        {
            AddToolToBuilder(tool);
        }
        _draggedTool = null;
    }

    private void AddToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToolDefinition tool })
        {
            AddToolToBuilder(tool);
        }
    }

    private void AddToolToBuilder(ToolDefinition tool)
    {
        bool added = BuilderViewModel.AddTool(tool);
        if (added)
        {
            ShowInfoBar(InfoBarSeverity.Success, "Tool Added", $"Added {tool.Name} to {BuilderViewModel.StackName}.");
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Informational, "Already in Stack", $"{tool.Name} is already included in this stack.");
        }
    }

    private void RemoveToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToolDefinition tool })
        {
            BuilderViewModel.RemoveTool(tool);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToolDefinition tool })
        {
            BuilderViewModel.MoveUp(tool);
        }
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToolDefinition tool })
        {
            BuilderViewModel.MoveDown(tool);
        }
    }

    private void MyStackListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            if (MyStackListView.SelectedItem is ToolDefinition tool)
            {
                BuilderViewModel.RemoveTool(tool);
                e.Handled = true;
            }
        }
    }

    private void ClearCustomStack_Click(object sender, RoutedEventArgs e)
    {
        BuilderViewModel.Clear();
        ShowInfoBar(InfoBarSeverity.Informational, "Stack Cleared", "Custom stack has been reset.");
    }

    private void SaveCustomStack_Click(object sender, RoutedEventArgs e)
    {
        if (BuilderViewModel.StackTools.Count == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Cannot Save Empty Stack", "Add at least one tool to the stack before saving.");
            return;
        }

        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var bundle = BuilderViewModel.ToBundle();
        CustomStacksService.SaveCustomStack(bundle);

        // Update MainWindow.Bundles collection
        var existing = mw.Bundles.FirstOrDefault(b => string.Equals(b.Id, bundle.Id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var idx = mw.Bundles.IndexOf(existing);
            mw.Bundles[idx] = bundle;
        }
        else
        {
            mw.Bundles.Add(bundle);
        }

        RefreshCardModels();
        ApplyFilter();

        ShowInfoBar(InfoBarSeverity.Success, "Stack Saved",
            $"'{bundle.Name}' saved with {bundle.Tools.Count} tool(s). It is now available under presets and quick stacks.");
    }

    private async void ExportCustomStack_Click(object sender, RoutedEventArgs e)
    {
        if (BuilderViewModel.StackTools.Count == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Cannot Export Empty Stack", "Add at least one tool to the stack before exporting.");
            return;
        }

        var mw = App.MainWindowInstance;
        if (mw is null) return;

        try
        {
            var savePicker = new FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            var safeName = string.IsNullOrWhiteSpace(BuilderViewModel.StackName)
                ? "custom-stack"
                : BuilderViewModel.StackName.Trim().ToLowerInvariant().Replace(' ', '-');

            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.SuggestedFileName = safeName;
            savePicker.FileTypeChoices.Add("JSON Stack Definition", new List<string> { ".json" });

            var file = await savePicker.PickSaveFileAsync();
            if (file is null) return;

            var bundle = BuilderViewModel.ToBundle();
            var json = CustomStacksService.ExportToJson(bundle);
            await Windows.Storage.FileIO.WriteTextAsync(file, json);

            ShowInfoBar(InfoBarSeverity.Success, "Stack Exported", $"Exported '{bundle.Name}' ({bundle.Tools.Count} tools) to {file.Name}.");
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Export Failed", ex.Message);
        }
    }

    private async void ImportCustomStack_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        try
        {
            var openPicker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);

            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".json");

            var file = await openPicker.PickSingleFileAsync();
            if (file is null) return;

            var json = await Windows.Storage.FileIO.ReadTextAsync(file);
            var (stack, unknownToolIds, error) = CustomStacksService.ImportFromJson(json, mw.Tools);

            if (error != null)
            {
                ShowInfoBar(InfoBarSeverity.Error, "Import Failed", error);
                return;
            }

            if (stack == null)
            {
                ShowInfoBar(InfoBarSeverity.Error, "Import Failed", "No valid stack found in the selected file.");
                return;
            }

            BuilderViewModel.LoadBundle(stack);

            if (unknownToolIds.Count > 0)
            {
                ShowInfoBar(InfoBarSeverity.Warning, $"Stack '{stack.Name}' Imported with Warnings",
                    $"Loaded {stack.Tools.Count} tool(s). {unknownToolIds.Count} unknown tool ID(s) were ignored: {string.Join(", ", unknownToolIds)}");
            }
            else
            {
                ShowInfoBar(InfoBarSeverity.Success, "Import Successful",
                    $"Successfully imported '{stack.Name}' with {stack.Tools.Count} tool(s).");
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, "Import Failed", ex.Message);
        }
    }

    private void InstallCustomStack_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        if (BuilderViewModel.StackTools.Count == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Stack is Empty", "Add tools to the stack before installing.");
            return;
        }

        var toDownload = ResolvePendingTools(mw, BuilderViewModel.StackTools.Select(t => t.Id).ToList());

        if (toDownload.Count == 0)
        {
            ShowInfoBar(InfoBarSeverity.Informational, "All Tools Installed",
                $"All {BuilderViewModel.StackTools.Count} tools in '{BuilderViewModel.StackName}' are already downloaded or installed.");
            return;
        }

        StartStackDownload(toDownload, BuilderViewModel.StackName);
    }

    private void ShowInfoBar(InfoBarSeverity severity, string title, string message)
    {
        if (BuilderInfoBar == null) return;
        BuilderInfoBar.Severity = severity;
        BuilderInfoBar.Title = title;
        BuilderInfoBar.Message = message;
        BuilderInfoBar.IsOpen = true;
    }
}
