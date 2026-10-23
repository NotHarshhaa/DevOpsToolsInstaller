using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

/// <summary>
/// A category of tools shown under a single header in the catalog.
/// </summary>
public sealed class ToolCategoryGroup : List<ToolDefinition>
{
    public ToolCategoryGroup(string key, IEnumerable<ToolDefinition> items) : base(items)
        => Key = key;

    public string Key { get; }
}

public sealed partial class CatalogPage : Page, IRefreshablePage
{
    /// <summary>Pixels moved by one click of the category scroll buttons.</summary>
    private const double ChipScrollStep = 220;

    /// <summary>Skeleton cards drawn while the catalog loads.</summary>
    private const int ShimmerCardCount = 6;

    /// <summary>Marker set on a card template's root border so a click on any
    /// child can walk back up to the card itself.</summary>
    private const string CardRootTag = "tool-card";

    /// <summary>
    /// The category chips, in display order: (button label, catalog category).
    /// The tag must be the exact category string the catalog uses — the display
    /// label is the friendlier short form shown on the chip.
    /// </summary>
    private static readonly (string Label, string Tag)[] CategoryDefinitions =
    {
        ("All Tools", "All"),
        ("Favorites", "Favorites"),
        ("Cloud CLIs", "Cloud CLIs"),
        ("Containerization", "Containerization"),
        ("Kubernetes", "Kubernetes"),
        ("Infrastructure as Code", "Infrastructure as Code"),
        ("CI/CD & Version Control", "CI/CD and Version Control"),
        ("Monitoring & Observability", "Monitoring and Observability"),
        ("Security", "Security"),
        ("Networking", "Networking"),
        ("Policy & Compliance", "Policy & Compliance"),
        ("Database & Data", "Database & Data"),
        ("Editors & Terminals", "Editors and Terminals"),
        ("Utilities", "Utilities"),
    };

    private readonly ObservableCollection<ToolDefinition> _filteredTools = new();
    private readonly List<Button> _categoryChipButtons = new();
    private readonly HashSet<ToolDefinition> _hookedTools = new();

    private bool _busy;
    private bool _hooked;
    private CancellationTokenSource? _downloadCts;
    private string _selectedCategory = "All";
    private string _sortMode = "category"; // az, za, category, kind, downloaded, favorites
    private string _statusFilter = "all";  // all, not_installed, installed, updates, favorites
    private string _density = "cards";     // cards, list
    private ToolBundle? _activeBundle;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchDebounceTimer;

    // Detail pane state
    private ToolDefinition? _detailTool;
    private int _detailLoadToken;
    private int _connectedAnimationKey;
    private bool _suppressVersionChange;

    public CatalogPage()
    {
        InitializeComponent();

        Header.BadgeText = "All tools";

        ToolsRepeater.ItemTemplate = (DataTemplate)Resources["CatalogCardTemplate"];
        ToolsRepeater.ItemsSource = _filteredTools;
        ShimmerRepeater.ItemsSource = Enumerable.Range(0, ShimmerCardCount).ToArray();

        BuildCategoryChips();

        Loaded += CatalogPage_Loaded;
        Unloaded += CatalogPage_Unloaded;
    }

    private async void CatalogPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        HookCatalog(mw);

        if (mw.Tools.Count == 0)
        {
            // Shimmer placeholders while the catalog loads; suppressed under
            // the Windows "reduce animations" setting (falls back to the ring).
            var showShimmer = AnimationSettingsHelper.AnimationsEnabled;
            ShowShimmer(showShimmer);
            SetBusy(true, "Loading catalog...");
            try
            {
                await mw.EnsureCatalogLoadedAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load catalog: {ex.Message}";
            }
            finally
            {
                ShowShimmer(false);
                SetBusy(false);
                HookCatalog(mw);
            }
        }

        if (!string.IsNullOrWhiteSpace(mw.PendingCatalogSearchQuery))
        {
            var query = mw.PendingCatalogSearchQuery;
            mw.PendingCatalogSearchQuery = null;
            SearchBox.Text = query;
        }

        ApplyFilter();
        ApplyDensity();
        PopulatePresetsMenu();

        if (!string.IsNullOrWhiteSpace(mw.PendingBundleSelectionId))
        {
            var bundleId = mw.PendingBundleSelectionId;
            mw.PendingBundleSelectionId = null;
            var targetBundle = mw.Bundles.FirstOrDefault(b => string.Equals(b.Id, bundleId, StringComparison.OrdinalIgnoreCase));
            if (targetBundle != null)
            {
                ApplyBundleSelection(targetBundle);
            }
            else
            {
                StatusText.Text = $"{mw.Tools.Count} tools available";
            }
        }
        else
        {
            StatusText.Text = $"{mw.Tools.Count} tools available";
        }

        // Background check for installed tools so "Installed" badges appear accurately
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                foreach (var tool in mw.Tools)
                {
                    if (!tool.IsInstalled && UninstallService.IsInstalled(tool, dlFolder))
                    {
                        _ = DispatcherQueue.TryEnqueue(() => tool.IsInstalled = true);
                    }
                }
            }
            catch { }
        });

        UpdateScrollButtons();
        UpdateSelectionBar();
    }

    private void CatalogPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // The page is cached by the Frame, so unhook the long-lived catalog
        // subscriptions here to avoid leaking handlers across navigations.
        var mw = App.MainWindowInstance;
        if (mw is not null)
        {
            mw.Tools.CollectionChanged -= Tools_CollectionChanged;
        }

        foreach (var tool in _hookedTools)
        {
            tool.PropertyChanged -= Tool_PropertyChanged;
        }

        _hookedTools.Clear();
        _hooked = false;

        CloseDetailPane();
    }

    // ── Selection model plumbing ─────────────────────────────────────────

    /// <summary>
    /// Subscribes to the catalog so the selection bar and the open detail pane
    /// stay in sync when any tool's state changes.
    /// </summary>
    private void HookCatalog(MainWindow mw)
    {
        if (!_hooked)
        {
            mw.Tools.CollectionChanged += Tools_CollectionChanged;
            _hooked = true;
        }

        foreach (var tool in mw.Tools)
        {
            if (_hookedTools.Add(tool))
            {
                tool.PropertyChanged += Tool_PropertyChanged;
            }
        }
    }

    private void Tools_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A Reset (catalog reload) carries no OldItems, so unhook everything and
        // re-subscribe to whatever the collection now holds.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var hooked in _hookedTools)
            {
                hooked.PropertyChanged -= Tool_PropertyChanged;
            }
            _hookedTools.Clear();

            if (sender is ObservableCollection<ToolDefinition> tools)
            {
                foreach (var tool in tools)
                {
                    if (_hookedTools.Add(tool))
                    {
                        tool.PropertyChanged += Tool_PropertyChanged;
                    }
                }
            }

            UpdateSelectionBar();
            return;
        }

        if (e.NewItems is not null)
        {
            foreach (ToolDefinition tool in e.NewItems)
            {
                if (_hookedTools.Add(tool))
                {
                    tool.PropertyChanged += Tool_PropertyChanged;
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (ToolDefinition tool in e.OldItems)
            {
                if (_hookedTools.Remove(tool))
                {
                    tool.PropertyChanged -= Tool_PropertyChanged;
                }
            }
        }
    }

    private void Tool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ToolDefinition tool) return;

        if (e.PropertyName is nameof(ToolDefinition.IsSelected))
        {
            UpdateSelectionBar();
            return;
        }

        if (ReferenceEquals(tool, _detailTool) &&
            e.PropertyName is nameof(ToolDefinition.IsInstalled)
                or nameof(ToolDefinition.Status)
                or nameof(ToolDefinition.DetectedVersion)
                or nameof(ToolDefinition.DownloadUrl)
                or nameof(ToolDefinition.FileName)
                or nameof(ToolDefinition.Sha256)
                or nameof(ToolDefinition.IsFavorite)
                or nameof(ToolDefinition.SelectedVersion))
        {
            RefreshDetailPane();
        }
    }

    private void UpdateSelectionBar()
    {
        if (SelectionBar is null) return;

        var mw = App.MainWindowInstance;
        var selected = mw?.Tools.Where(t => t.IsSelected).ToList() ?? new List<ToolDefinition>();

        if (selected.Count == 0)
        {
            SelectionBar.Visibility = Visibility.Collapsed;
            ToolsScrollViewer.Padding = new Thickness(0, 6, 0, 8);
            return;
        }

        var downloadable = DownloadableSelection(selected).Count;

        SelectionCountText.Text = selected.Count == 1
            ? "1 selected"
            : $"{selected.Count} selected";
        SelectionDownloadButton.IsEnabled = !_busy && downloadable > 0;
        SelectionDownloadButton.Opacity = downloadable > 0 ? 1 : 0.5;
        ToolTipService.SetToolTip(SelectionDownloadButton, downloadable > 0
            ? $"Download and install {downloadable} selected tool(s)"
            : "Every selected tool is already downloaded and up to date");

        SelectionBar.Visibility = Visibility.Visible;

        // Reserve room at the bottom of the grid so the floating bar never hides
        // the last row of cards.
        ToolsScrollViewer.Padding = new Thickness(0, 6, 0, 88);
    }

    /// <summary>
    /// The tools in a selection that a batch download would actually act on:
    /// not on this machine yet, or installed but with a newer release available.
    /// The selection bar's count and the download itself share this predicate so
    /// the number always matches what happens.
    /// </summary>
    private static List<ToolDefinition> DownloadableSelection(IEnumerable<ToolDefinition> tools) =>
        tools
            .Where(t => t.Status != ToolStatus.Downloading &&
                        (!t.IsDownloadedOrInstalled || t.HasUpdate))
            .ToList();

    private void SelectionClear_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        foreach (var tool in mw.Tools) tool.IsSelected = false;
        UpdateSelectionBar();
    }

    private async void SelectionSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var selected = mw.Tools.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "Select at least one tool before saving a profile.";
            return;
        }

        var nameBox = new TextBox
        {
            Header = "Profile name",
            PlaceholderText = "e.g. Frontend workstation",
            Text = $"{Environment.MachineName} selection"
        };
        var descriptionBox = new TextBox
        {
            Header = "Description (optional)",
            PlaceholderText = "What is this profile for?"
        };

        var panel = new StackPanel { Spacing = 10, MaxWidth = 400 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Save the {selected.Count} selected tool(s) as a named workstation profile you can reapply on any machine.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);

        var dialog = new ContentDialog
        {
            Title = "Save selection as profile",
            Content = panel,
            PrimaryButtonText = "Save profile",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            StatusText.Text = "A profile name is required.";
            return;
        }

        var profile = ProfileStoreService.CreateFromSelection(
            nameBox.Text, descriptionBox.Text, selected.Select(t => t.Id));

        StatusText.Text = $"Saved profile '{profile.Name}' with {profile.ToolCount} tool(s).";
        ToastService.Show("Profile saved",
            $"{profile.Name} — {profile.ToolCount} tool(s). Manage it on the Profiles page.");
    }

    // ── Category chips ───────────────────────────────────────────────────

    private void BuildCategoryChips()
    {
        CategoryChipPanel.Children.Clear();
        _categoryChipButtons.Clear();

        foreach (var (label, tag) in CategoryDefinitions)
        {
            var chip = new Button
            {
                Content = label,
                Tag = tag,
                VerticalAlignment = VerticalAlignment.Center
            };
            chip.Click += CategoryChip_Click;
            AutomationProperties.SetName(chip, $"{label} category filter");

            CategoryChipPanel.Children.Add(chip);
            _categoryChipButtons.Add(chip);
        }

        UpdateChipSelection();
    }

    private void UpdateChipSelection()
    {
        var selected = Resources["CatalogChipSelectedStyle"] as Style;
        var normal = Resources["CatalogChipStyle"] as Style;

        foreach (var chip in _categoryChipButtons)
        {
            var isSelected = chip.Tag is string tag &&
                             string.Equals(tag, _selectedCategory, StringComparison.OrdinalIgnoreCase);
            chip.Style = isSelected ? selected : normal;
        }
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } chip) return;

        _selectedCategory = tag;
        UpdateChipSelection();
        ApplyFilter();
        chip.StartBringIntoView();

        // Re-evaluate the fade now that the row may have scrolled.
        DispatcherQueue.TryEnqueue(UpdateScrollButtons);
    }

    private void ScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        var target = Math.Max(0, ChipsScrollViewer.HorizontalOffset - ChipScrollStep);
        ChipsScrollViewer.ChangeView(target, null, null, false);
    }

    private void ScrollRight_Click(object sender, RoutedEventArgs e)
    {
        var target = Math.Min(ChipsScrollViewer.ScrollableWidth, ChipsScrollViewer.HorizontalOffset + ChipScrollStep);
        ChipsScrollViewer.ChangeView(target, null, null, false);
    }

    private void ChipsScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs? e)
        => UpdateScrollButtons();

    private void ChipsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateScrollButtons();

    private void UpdateScrollButtons()
    {
        if (ChipsScrollViewer is null || ScrollLeftButton is null || ScrollRightButton is null) return;

        ScrollLeftButton.Visibility = ChipsScrollViewer.HorizontalOffset > 5
            ? Visibility.Visible
            : Visibility.Collapsed;

        ScrollRightButton.Visibility = ChipsScrollViewer.HorizontalOffset < (ChipsScrollViewer.ScrollableWidth - 5)
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyChipEdgeFade();
    }

    /// <summary>
    /// Shows a soft gradient over whichever edge of the category row has more
    /// chips hidden beyond it, so the row reads as scrollable rather than
    /// clipped. The overlays are plain rectangles filled with a theme-aware
    /// gradient; when the row fits, both are hidden so no chip is ever dimmed.
    /// </summary>
    private void ApplyChipEdgeFade()
    {
        if (ChipsScrollViewer is null || ChipFadeLeft is null || ChipFadeRight is null) return;

        var width = ChipsScrollViewer.ActualWidth;
        if (width <= 0 || double.IsNaN(width))
        {
            ChipFadeLeft.Visibility = Visibility.Collapsed;
            ChipFadeRight.Visibility = Visibility.Collapsed;
            return;
        }

        ChipFadeLeft.Visibility = ChipsScrollViewer.HorizontalOffset > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        ChipFadeRight.Visibility = ChipsScrollViewer.HorizontalOffset < ChipsScrollViewer.ScrollableWidth - 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── Filtering / sorting ──────────────────────────────────────────────

    private void ApplyFilter()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var query = SearchBox.Text?.Trim() ?? "";

        bool Matches(ToolDefinition tool)
        {
            if (_activeBundle != null && !_activeBundle.Tools.Contains(tool.Id, StringComparer.OrdinalIgnoreCase))
                return false;

            var textMatch = string.IsNullOrEmpty(query)
                || tool.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || tool.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || tool.Description.Contains(query, StringComparison.OrdinalIgnoreCase);

            bool categoryMatch;
            if (_selectedCategory == "All")
                categoryMatch = true;
            else if (_selectedCategory == "Favorites")
                categoryMatch = tool.IsFavorite;
            else
                categoryMatch = string.Equals(tool.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase);

            bool statusMatch = _statusFilter switch
            {
                "not_installed" => !tool.IsDownloadedOrInstalled,
                "installed" => tool.IsDownloadedOrInstalled,
                "updates" => tool.HasUpdate,
                "favorites" => tool.IsFavorite,
                _ => true
            };

            return textMatch && categoryMatch && statusMatch;
        }

        var filtered = mw.Tools.Where(Matches);

        // Apply sort
        var sorted = _sortMode switch
        {
            "az" => filtered.OrderBy(t => t.Name),
            "za" => filtered.OrderByDescending(t => t.Name),
            "kind" => filtered.OrderBy(t => t.KindLabel).ThenBy(t => t.Name),
            "downloaded" => filtered.OrderByDescending(t => t.IsDownloadedOrInstalled).ThenBy(t => t.Name),
            "updates" => filtered.OrderByDescending(t => t.HasUpdate).ThenBy(t => t.Category).ThenBy(t => t.Name),
            "favorites" => filtered.OrderByDescending(t => t.IsFavorite).ThenBy(t => t.Category).ThenBy(t => t.Name),
            _ => filtered.OrderBy(t => t.Category).ThenBy(t => t.Name), // "category" (default)
        };

        var sortedList = sorted.ToList();

        // Fast path: if items match exactly, do not churn the ObservableCollection
        if (_filteredTools.Count == sortedList.Count && _filteredTools.SequenceEqual(sortedList))
        {
            FinishFilterRefresh(mw, _filteredTools.Count, updateStatus: true);
            return;
        }

        // Smart diff update: remove only missing items and insert/move new items
        for (int i = _filteredTools.Count - 1; i >= 0; i--)
        {
            if (!sortedList.Contains(_filteredTools[i]))
            {
                _filteredTools.RemoveAt(i);
            }
        }

        for (int i = 0; i < sortedList.Count; i++)
        {
            var item = sortedList[i];
            if (i >= _filteredTools.Count)
            {
                _filteredTools.Add(item);
            }
            else if (_filteredTools[i] != item)
            {
                int existingIdx = _filteredTools.IndexOf(item);
                if (existingIdx >= 0)
                {
                    _filteredTools.Move(existingIdx, i);
                }
                else
                {
                    _filteredTools.Insert(i, item);
                }
            }
        }

        FinishFilterRefresh(mw, _filteredTools.Count, updateStatus: true);
    }

    private void FinishFilterRefresh(MainWindow mw, int shown, bool updateStatus)
    {
        if (updateStatus)
        {
            StatusText.Text = $"{shown} of {mw.Tools.Count} tools shown";
            if (FooterStatusText != null)
            {
                FooterStatusText.Text = $"{shown} tools available • Catalog: {CatalogService.CatalogFreshness}";
            }
        }

        if (NoResults != null)
        {
            NoResults.Visibility = shown == 0 && ShimmerPanel.Visibility != Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void NoResults_ActionClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        _statusFilter = "all";
        StatusFilterButton.Label = "Filter: All";
        _selectedCategory = "All";
        _activeBundle = null;
        ActiveStackInfoBar.IsOpen = false;
        Header.BadgeText = "All tools";
        UpdateChipSelection();
        ApplyFilter();
    }

    /// <summary>Every tool currently visible in the catalog grid.</summary>
    private IEnumerable<ToolDefinition> VisibleTools => _filteredTools;

    // ── Density ──────────────────────────────────────────────────────────

    private void Density_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string tag } item) return;
        _density = tag;
        ApplyDensity();
        StatusText.Text = tag == "list" ? "Compact list layout." : "Card layout.";
    }

    /// <summary>
    /// Swaps the repeater's layout (and item template) between the roomy card
    /// grid and the compact single-line list. The items source is reset so the
    /// repeater re-realizes every element with the new template.
    /// </summary>
    private void ApplyDensity()
    {
        var compact = string.Equals(_density, "list", StringComparison.OrdinalIgnoreCase);
        var templateKey = compact ? "CatalogCompactRowTemplate" : "CatalogCardTemplate";

        if (Resources[templateKey] is DataTemplate template)
        {
            ToolsRepeater.ItemTemplate = template;
        }

        ToolsRepeater.Layout = compact
            ? new StackLayout { Orientation = Orientation.Vertical, Spacing = 8 }
            : new UniformGridLayout
            {
                MinItemWidth = 340,
                MinRowSpacing = 12,
                MinColumnSpacing = 12,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill
            };

        // Force re-realization: ItemsRepeater recycles containers per template.
        ToolsRepeater.ItemsSource = null;
        ToolsRepeater.ItemsSource = _filteredTools;

        if (DensityButton is not null)
        {
            DensityButton.Label = compact ? "Density: List" : "Density: Cards";
            DensityButton.Icon = new FontIcon { Glyph = compact ? "\uE8FD" : "\uE80A" };
        }
    }

    // ── Right-hand detail pane ───────────────────────────────────────────

    private void ToolCard_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement card || card.DataContext is not ToolDefinition tool) return;

        // Ignore taps that landed on an interactive child (checkbox, install
        // split-button, favorite star, version link …).
        if (IsInteractiveSource(e.OriginalSource as DependencyObject, card)) return;

        OpenDetailPane(tool, card);
    }

    private static bool IsInteractiveSource(DependencyObject? source, DependencyObject boundary)
    {
        while (source is not null && !ReferenceEquals(source, boundary))
        {
            if (source is ButtonBase or HyperlinkButton or SplitButton or MenuFlyoutItem or TextBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ToolDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool } element) return;

        // Context-menu items are not part of the card's visual tree, so the
        // row lookup legitimately fails there and the pane simply opens without
        // a connected animation.
        OpenDetailPane(tool, FindCardRoot(element));
    }

    /// <summary>Walks up from a card's child to the card's own root border.</summary>
    private static FrameworkElement? FindCardRoot(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Border { Tag: CardRootTag } border) return border;
            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>Depth-first search for the card's logo badge (glyph + image pair).</summary>
    private static Border? FindLogoBadge(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Border { Child: Grid grid } candidate &&
                grid.Children.OfType<Image>().Any() &&
                grid.Children.OfType<FontIcon>().Any())
            {
                return candidate;
            }

            if (FindLogoBadge(child) is { } found) return found;
        }

        return null;
    }

    private void OpenDetailPane(ToolDefinition tool, FrameworkElement? source)
    {
        if (!ReferenceEquals(_detailTool, tool))
        {
            UnhookDetailTool();
            ShowDetailPane(tool);
        }

        DetailSplitView.IsPaneOpen = true;

        // Let the grid reflow first so the connected animation starts from the
        // card's final position rather than its pre-reflow one.
        DetailPaneRoot.UpdateLayout();

        if (source is null || !AnimationSettingsHelper.AnimationsEnabled) return;

        try
        {
            var from = FindLogoBadge(source) ?? source;
            var key = $"catalogToolDetail{++_connectedAnimationKey}";
            var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, from);
            animation?.TryStart(PaneLogoBorder);
        }
        catch
        {
            // A connected animation is a nicety: if the runtime refuses the
            // target the pane is simply already visible.
        }
    }

    private void CloseDetailPane()
    {
        UnhookDetailTool();
        _detailTool = null;
        if (DetailSplitView is not null) DetailSplitView.IsPaneOpen = false;
    }

    private void DetailSplitView_PaneClosed(SplitView sender, object args)
    {
        // PaneClosed arrives after the close animation, so a rapid re-open of a
        // different tool can land first — don't clear that pane's state.
        if (sender.IsPaneOpen) return;

        UnhookDetailTool();
        _detailTool = null;
    }

    private void PaneClose_Click(object sender, RoutedEventArgs e) => CloseDetailPane();

    private void UnhookDetailTool()
    {
        // Bump the token so an in-flight licence lookup can no longer write to
        // the pane now that it is showing (or going to show) a different tool.
        _detailLoadToken++;
    }

    private void ShowDetailPane(ToolDefinition tool)
    {
        _detailTool = tool;

        PaneName.Text = tool.Name;
        PaneVendor.Text = tool.VendorLabel;
        PaneCategory.Text = tool.Category;
        PaneKind.Text = tool.KindLabel;
        PaneDescription.Text = string.IsNullOrWhiteSpace(tool.Description)
            ? "No description was provided for this tool in the catalog."
            : tool.Description;
        PaneGlyph.Glyph = string.IsNullOrWhiteSpace(tool.IconGlyph) ? "\uE74C" : tool.IconGlyph;

        ApplyPaneLogo(tool);

        // Version picker (guarded: populating it raises SelectionChanged).
        _suppressVersionChange = true;
        PaneVersionCombo.Items.Clear();
        foreach (var version in tool.AllAvailableVersions)
        {
            PaneVersionCombo.Items.Add(version);
        }

        var selectedIndex = 0;
        for (var i = 0; i < PaneVersionCombo.Items.Count; i++)
        {
            if (PaneVersionCombo.Items[i] is not string candidate) continue;

            var value = candidate.Replace("(Latest)", "").Trim();
            if (string.Equals(value, tool.SelectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }
        PaneVersionCombo.SelectedIndex = PaneVersionCombo.Items.Count > 0 ? selectedIndex : -1;
        _suppressVersionChange = false;

        RefreshDetailPane();
        _ = LoadLicenseAsync(tool);
    }

    private void ApplyPaneLogo(ToolDefinition tool)
    {
        ImageSource? imageSource = null;

        if (tool.HasLogo)
        {
            try
            {
                // Reuse the same URL handling the catalog cards bind through, so
                // ms-appx:/// and https:// logos both resolve identically.
                imageSource = new Converters.StringToImageSourceConverter()
                    .Convert(tool.LogoUrl, typeof(ImageSource), string.Empty, string.Empty)
                    as ImageSource;
            }
            catch
            {
                imageSource = null;
            }
        }

        PaneLogo.Source = imageSource;
        PaneLogo.Visibility = imageSource is null ? Visibility.Collapsed : Visibility.Visible;
        PaneGlyph.Visibility = imageSource is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PaneLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        PaneLogo.Visibility = Visibility.Collapsed;
        PaneGlyph.Visibility = Visibility.Visible;
    }

    private void RefreshDetailPane()
    {
        var tool = _detailTool;
        if (tool is null) return;

        PaneInstalledBadge.Visibility = tool.IsDownloadedOrInstalled ? Visibility.Visible : Visibility.Collapsed;
        PaneInstalledText.Text = tool.InstalledVersionLabel;

        PaneInstalledDetail.Text = !tool.IsDownloadedOrInstalled
            ? "Not installed on this machine"
            : string.IsNullOrWhiteSpace(tool.DetectedVersion)
                ? tool.StatusText
                : $"{tool.DisplayDetectedVersion} — {tool.VersionStateText}";

        PaneSizeText.Text = GetArtifactSizeText(tool);

        PaneShaText.Text = string.IsNullOrWhiteSpace(tool.Sha256) ? "Not published" : tool.Sha256;
        ToolTipService.SetToolTip(PaneShaText, tool.Sha256);

        PaneSourceUrl.Text = string.IsNullOrWhiteSpace(tool.DownloadUrl) ? "Not published" : tool.DownloadUrl;
        ToolTipService.SetToolTip(PaneSourceUrl, tool.DownloadUrl);

        PaneFavoriteIcon.Glyph = tool.FavoriteGlyph;

        var installed = tool.IsDownloadedOrInstalled;
        PaneInstallButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        PaneDownloadOnlyButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        PaneInstallButton.IsEnabled = !_busy;

        PaneReleaseNotesLink.Visibility = ToolReleaseNotesService.GetReleasesUrl(tool.DownloadUrl) is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        PaneSourceLink.Visibility = string.IsNullOrWhiteSpace(tool.DownloadUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PaneHomepageLink.Visibility = string.IsNullOrWhiteSpace(tool.Homepage)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string GetArtifactSizeText(ToolDefinition tool)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(tool.FileName))
            {
                var path = Path.Combine(DownloadService.DefaultDownloadsFolder, tool.FileName);
                if (File.Exists(path))
                {
                    return CustomStacksService.FormatBytes(new FileInfo(path).Length);
                }
            }
        }
        catch
        {
            // Fall through to the unknown-size label.
        }

        return "Not downloaded yet";
    }

    private async Task LoadLicenseAsync(ToolDefinition tool)
    {
        var token = ++_detailLoadToken;

        if (!string.IsNullOrWhiteSpace(tool.License))
        {
            PaneLicenseText.Text = tool.License;
            return;
        }

        var githubHosted = ToolReleaseNotesService.IsGitHubHosted(tool.DownloadUrl);
        if (!githubHosted)
        {
            PaneLicenseText.Text = "See vendor homepage";
            return;
        }

        PaneLicenseText.Text = "Checking…";

        string? spdx = null;
        try
        {
            spdx = await ToolReleaseNotesService.GetRepositoryLicenseAsync(tool.DownloadUrl);
        }
        catch
        {
            // Offline or rate-limited: fall through to the generic label.
        }

        // The user may have opened a different tool while this was in flight.
        if (token != _detailLoadToken || !ReferenceEquals(_detailTool, tool)) return;

        PaneLicenseText.Text = spdx ?? "Not declared by vendor";
    }

    private void PaneVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVersionChange || _detailTool is null) return;
        if (PaneVersionCombo.SelectedItem is not string raw) return;

        var clean = raw.Replace("(Latest)", "").Trim();
        if (string.IsNullOrWhiteSpace(clean)) return;

        _detailTool.SetVersion(clean);

        StatusText.Text = _detailTool.IsPreviousVersionSelected
            ? $"{_detailTool.Name} pinned to v{_detailTool.SelectedVersion}."
            : $"{_detailTool.Name} reset to the latest release.";

        RefreshDetailPane();
        ApplyFilter();
    }

    private async void PaneInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_detailTool is null) return;
        await RunSingleToolAsync(_detailTool, installAfter: true);
        RefreshDetailPane();
    }

    private async void PaneDownloadOnly_Click(object sender, RoutedEventArgs e)
    {
        if (_detailTool is null) return;
        await RunSingleToolAsync(_detailTool, installAfter: false);
        RefreshDetailPane();
    }

    private void PaneFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_detailTool is null) return;
        _detailTool.IsFavorite = FavoritesService.Toggle(_detailTool.Id);
    }

    private void PaneHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_detailTool?.Homepage))
        {
            LauncherService.OpenUrl(_detailTool.Homepage);
        }
    }

    private void PaneSourceLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_detailTool?.DownloadUrl))
        {
            LauncherService.OpenUrl(_detailTool.DownloadUrl);
        }
    }

    private void PaneReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_detailTool is null) return;

        var url = ToolReleaseNotesService.GetReleasesUrl(_detailTool.DownloadUrl);
        if (!string.IsNullOrWhiteSpace(url))
        {
            LauncherService.OpenUrl(url);
        }
        else if (!string.IsNullOrWhiteSpace(_detailTool.Homepage))
        {
            LauncherService.OpenUrl(_detailTool.Homepage);
        }
    }

    private void PaneFullDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_detailTool is null) return;
        Frame.Navigate(typeof(ToolDetailPage), _detailTool);
    }

    private void CopySha_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_detailTool?.Sha256))
        {
            StatusText.Text = "This tool does not publish a SHA-256 checksum.";
            return;
        }

        CopyToClipboard(_detailTool.Sha256);
        StatusText.Text = $"Copied the SHA-256 checksum for {_detailTool.Name}.";
    }

    private void CopySourceUrl_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_detailTool?.DownloadUrl)) return;

        CopyToClipboard(_detailTool.DownloadUrl);
        StatusText.Text = $"Copied the download link for {_detailTool.Name}.";
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ── Shimmer ──────────────────────────────────────────────────────────

    private void ShowShimmer(bool show)
    {
        if (ShimmerPanel is null) return;
        ShimmerPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ToolsScrollViewer.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (NoResults is not null) NoResults.Visibility = Visibility.Collapsed;
    }

    // ── Search & Command Bar ─────────────────────────────────────────────
    // (The native CommandBar handles overflow/labels automatically.)

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = DispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer?.Stop();
                ApplyFilter();
            };
            _searchDebounceTimer.Start();
        }
    }

    // ── Sort & Filter ───────────────────────────────────────────────────

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string tag) return;
        _sortMode = tag;
        ApplyFilter();
    }

    private void StatusFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string tag) return;
        _statusFilter = tag;
        StatusFilterButton.Label = tag switch
        {
            "not_installed" => "Filter: Not installed",
            "installed" => "Filter: Installed",
            "updates" => "Filter: Updates",
            "favorites" => "Filter: Favorites",
            _ => "Filter: All"
        };
        ApplyFilter();
    }

    // ── Favorites ───────────────────────────────────────────────────────

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;
        tool.IsFavorite = FavoritesService.Toggle(tool.Id);
    }

    // ── Copy Install Command ────────────────────────────────────────────

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        var commands = new List<string>
        {
            $"# {tool.Name} v{tool.Version}",
            "# Direct download URL:",
            $"curl -LO \"{tool.DownloadUrl}\""
        };

        if (!string.IsNullOrWhiteSpace(tool.Sha256))
        {
            commands.Add($"# SHA256: {tool.Sha256}");
        }

        CopyToClipboard(string.Join(Environment.NewLine, commands));
        StatusText.Text = $"Copied install command for {tool.Name}";
    }

    // ── Presets ──────────────────────────────────────────────────────────

    private void PopulatePresetsMenu()
    {
        var mw = App.MainWindowInstance;
        if (mw is null || PresetsMenuFlyout is null) return;

        PresetsMenuFlyout.Items.Clear();

        // 1. Quick batch actions
        var selectNotInstalledItem = new MenuFlyoutItem
        {
            Text = "Select All Not Installed",
            Icon = new FontIcon { Glyph = "\uE896" }
        };
        selectNotInstalledItem.Click += (s, e) =>
        {
            int count = 0;
            foreach (var tool in mw.Tools)
            {
                tool.IsSelected = !tool.IsDownloadedOrInstalled;
                if (tool.IsSelected) count++;
            }
            StatusText.Text = $"Selected {count} uninstalled tool(s).";
        };
        PresetsMenuFlyout.Items.Add(selectNotInstalledItem);

        var selectUpdatesItem = new MenuFlyoutItem
        {
            Text = "Select All with Updates Available",
            Icon = new FontIcon { Glyph = "\uE72C" }
        };
        selectUpdatesItem.Click += (s, e) =>
        {
            int count = 0;
            foreach (var tool in mw.Tools)
            {
                tool.IsSelected = tool.HasUpdate;
                if (tool.IsSelected) count++;
            }
            StatusText.Text = $"Selected {count} tool(s) with available updates.";
        };
        PresetsMenuFlyout.Items.Add(selectUpdatesItem);

        var browseAllItem = new MenuFlyoutItem
        {
            Text = "Explore All Stacks Section…",
            Icon = new FontIcon { Glyph = "\uE71D" }
        };
        browseAllItem.Click += (s, e) => mw.NavigateTo("Stacks");
        PresetsMenuFlyout.Items.Add(browseAllItem);
        PresetsMenuFlyout.Items.Add(new MenuFlyoutSeparator());

        // 2. Add each bundle dynamically from mw.Bundles
        foreach (var bundle in mw.Bundles)
        {
            var item = new MenuFlyoutItem
            {
                Text = $"{bundle.Name} ({bundle.Tools.Count})",
                Icon = new FontIcon { Glyph = bundle.Glyph }
            };
            ToolTipService.SetToolTip(item, bundle.Description);
            var capturedBundle = bundle;
            item.Click += (s, e) => ApplyBundleSelection(capturedBundle);
            PresetsMenuFlyout.Items.Add(item);
        }
    }

    private void ApplyBundleSelection(ToolBundle bundle)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        _activeBundle = bundle;
        int selectedCount = mw.SelectBundle(bundle);

        ActiveStackInfoBar.Title = $"Stack Filter: {bundle.Name} ({bundle.Tools.Count} tools)";
        ActiveStackInfoBar.Message = $"Showing only tools included in {bundle.Name}. All {selectedCount} tools are pre-selected. Use the selection bar to download them, or customize your selection.";
        ActiveStackInfoBar.IsOpen = true;

        Header.BadgeText = $"{bundle.Name} ({bundle.Tools.Count} tools)";
        StatusText.Text = $"Showing {selectedCount} tools from {bundle.Name}";

        ApplyFilter();
        UpdateSelectionBar();
    }

    private void ClearStackFilter_Click(object sender, RoutedEventArgs e)
    {
        _activeBundle = null;
        ActiveStackInfoBar.IsOpen = false;
        Header.BadgeText = "All tools";
        ApplyFilter();
    }

    private void ActiveStackInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _activeBundle = null;
        Header.BadgeText = "All tools";
        ApplyFilter();
    }

    // ── Select / Clear ──────────────────────────────────────────────────

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in VisibleTools) item.IsSelected = true;
        UpdateSelectionBar();
    }

    // ── Export / Import ─────────────────────────────────────────────────

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var selectedCount = mw.Tools.Count(t => t.IsSelected);
        if (selectedCount == 0)
        {
            StatusText.Text = "Select at least one tool before exporting.";
            return;
        }

        var savePicker = new FileSavePicker();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

        savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        savePicker.SuggestedFileName = "devops-tool-profile";
        savePicker.FileTypeChoices.Add("JSON Profile", new List<string> { ".json" });

        var file = await savePicker.PickSaveFileAsync();
        if (file is null) return;

        var json = ProfileService.Export(mw.Tools);
        await Windows.Storage.FileIO.WriteTextAsync(file, json);
        StatusText.Text = $"Exported {selectedCount} tool(s) to {file.Name}";
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var openPicker = new FileOpenPicker();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!);
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);

        openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        openPicker.FileTypeFilter.Add(".json");

        var file = await openPicker.PickSingleFileAsync();
        if (file is null) return;

        var json = await Windows.Storage.FileIO.ReadTextAsync(file);
        var count = ProfileService.Import(json, mw.Tools);
        ApplyFilter();
        UpdateSelectionBar();
        StatusText.Text = $"Imported profile — {count} tool(s) selected from {file.Name}";
    }

    // ── Batch download (selection bar) ──────────────────────────────────

    private async void SelectionDownload_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || _busy) return;

        var selected = DownloadableSelection(mw.Tools.Where(t => t.IsSelected));

        if (selected.Count == 0)
        {
            var anySelected = mw.Tools.Any(t => t.IsSelected);
            StatusText.Text = anySelected
                ? "Every selected tool is already downloaded and up to date."
                : "Select at least one tool to download.";
            return;
        }

        _downloadCts = new CancellationTokenSource();
        CancelButton.Visibility = Visibility.Visible;
        SetBusy(true, $"Downloading {selected.Count} tool(s)...");

        // Add to download queue for the DownloadsPage to track
        foreach (var tool in selected)
        {
            if (!mw.DownloadQueue.Contains(tool))
                mw.DownloadQueue.Add(tool);
        }

        try
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            await mw.DownloadSvc.DownloadBatchAsync(selected, dlFolder, maxConcurrency: 3, ct: _downloadCts.Token);

            var succeeded = selected.Count(t => t.Status == ToolStatus.Downloaded);
            var failed = selected.Count(t => t.Status == ToolStatus.Failed);
            StatusText.Text = $"Done - {succeeded} succeeded, {failed} failed";

            ToastService.Show(
                $"Download finished ({succeeded}/{selected.Count})",
                failed > 0
                    ? $"{failed} download(s) failed. Interrupted transfers can resume on the next attempt."
                    : "All selected tools were downloaded successfully.");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Downloads cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Download error: {ex.Message}";
        }
        finally
        {
            CancelButton.Visibility = Visibility.Collapsed;
            _downloadCts?.Dispose();
            _downloadCts = null;
            SetBusy(false);
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        StatusText.Text = "Cancelling downloads...";
    }

    // ── Busy state ──────────────────────────────────────────────────────

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SelectAllButton.IsEnabled = !busy;
        PresetsButton.IsEnabled = !busy;
        ProfileButton.IsEnabled = !busy;
        DensityButton.IsEnabled = !busy;
        if (SelectionDownloadButton is not null) SelectionDownloadButton.IsEnabled = !busy;
        if (PaneInstallButton is not null) PaneInstallButton.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }

    private async void VersionPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;
        await ShowVersionPickerDialogAsync(tool);
    }

    private async Task ShowVersionPickerDialogAsync(ToolDefinition tool)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 480 };

        panel.Children.Add(new TextBlock
        {
            Text = $"Choose a specific release version of {tool.Name}. You can select from known releases or enter a custom version tag.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        // Curated versions combo box
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Select known version…"
        };
        foreach (var v in tool.AllAvailableVersions)
        {
            combo.Items.Add(v);
        }

        var customBox = new TextBox
        {
            PlaceholderText = "Or enter custom version (e.g. 1.8.5)",
            Text = tool.IsPreviousVersionSelected ? tool.SelectedVersion : ""
        };

        var previewBox = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        void UpdatePreview(string v)
        {
            var clean = v.Trim().TrimStart('v', 'V');
            if (clean.EndsWith("(Latest)")) clean = clean.Replace("(Latest)", "").Trim();

            previewBox.Text = $"Target version: v{clean}\nFile: {tool.FileName}";
        }

        combo.SelectionChanged += (s, ev) =>
        {
            if (combo.SelectedItem is string sel)
            {
                var v = sel.Replace("(Latest)", "").Trim();
                customBox.Text = v;
                UpdatePreview(v);
            }
        };

        customBox.TextChanged += (s, ev) =>
        {
            if (!string.IsNullOrWhiteSpace(customBox.Text))
            {
                UpdatePreview(customBox.Text);
            }
        };

        UpdatePreview(tool.SelectedVersion);

        // ── Assemble the dialog content ────────────────────────────────────
        var canFetchLive = ToolVersionHistoryService.CanFetchVersionHistory(tool.Id, tool.DownloadUrl);

        if (!tool.SupportsVersionTargeting)
        {
            // The vendor serves a single always-latest static link (aka.ms
            // redirect, "latest" channel download, fixed MSI name): selecting
            // an older version cannot change what gets downloaded. Be upfront
            // instead of silently installing the newest release.
            panel.Children.Add(new InfoBar
            {
                Severity = InfoBarSeverity.Warning,
                Title = "This vendor serves one always-latest installer link",
                Message = canFetchLive
                    ? "The download link doesn't carry the version, but the vendor mirrors its releases to GitHub — choose an older build from the live release list below."
                    : "The download URL doesn't carry the version and the vendor keeps no mirror we can rewrite, so selecting an older version here would still download the newest release. Curated versions are shown for reference; install an archived build manually via the vendor's release archive (then add it as a custom tool).",
                IsOpen = true,
                IsClosable = false,
                MaxWidth = 460
            });

            combo.IsEnabled = false;
            customBox.IsEnabled = false;
        }

        panel.Children.Add(new TextBlock { Text = "Catalog versions:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(combo);

        // Live GitHub releases: the newest releases of the vendor repository,
        // fetched on demand — for GitHub-hosted tools this reflects what the
        // vendor actually published (fresher than the curated catalog list)
        // and lets users install an old version exactly as shipped. Tools whose
        // vendor CDN has a verified GitHub mirror (helm, kind, minikube,
        // dbeaver) get the same treatment even though their catalog URL points
        // at the vendor's own site.
        var ghCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Fetching recent releases…",
            IsEnabled = false
        };
        var ghStatus = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        var ghReleasesByVersion = new Dictionary<string, ToolReleaseVersion>(StringComparer.OrdinalIgnoreCase);

        if (canFetchLive)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Recent releases (last 5, fetched live):",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0)
            });
            panel.Children.Add(ghCombo);
            panel.Children.Add(ghStatus);

            _ = LoadGitHubReleasesAsync(tool, ghCombo, ghStatus, ghReleasesByVersion, customBox, UpdatePreview);
        }

        panel.Children.Add(new TextBlock { Text = "Custom Version:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(customBox);
        panel.Children.Add(previewBox);

        var dialog = new ContentDialog
        {
            Title = $"{tool.Name} Version Selection",
            Content = panel,
            PrimaryButtonText = "Apply Version",
            SecondaryButtonText = tool.IsPreviousVersionSelected ? "Reset to Latest" : "",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var chosen = customBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(chosen) && combo.SelectedItem is string sel)
            {
                chosen = sel.Replace("(Latest)", "").Trim();
            }

            if (!string.IsNullOrWhiteSpace(chosen))
            {
                // When the chosen version came from the live GitHub lookup and
                // its exact Windows asset was identified, install that artifact
                // directly — vendors rename files between releases, so the
                // templated file name alone would otherwise 404.
                if (ghReleasesByVersion.TryGetValue(chosen, out var release) &&
                    !string.IsNullOrWhiteSpace(release.WindowsAssetUrl))
                {
                    tool.SetVersionWithAsset(release.Version, release.WindowsAssetUrl, release.WindowsAssetName ?? "");
                    StatusText.Text = $"Set {tool.Name} to {release.TagName} from the GitHub release asset {release.WindowsAssetName}.";
                }
                else
                {
                    tool.SetVersion(chosen);
                    StatusText.Text = $"Updated {tool.Name} to version v{tool.SelectedVersion}.";
                }
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            tool.SetVersion(tool.Version);
            StatusText.Text = $"Reset {tool.Name} to latest version (v{tool.Version}).";
        }
    }

    /// <summary>Wrapper so a ComboBox item renders the release readably.</summary>
    private sealed class GitHubReleaseChoice
    {
        public GitHubReleaseChoice(ToolReleaseVersion release) => Release = release;

        public ToolReleaseVersion Release { get; }

        public override string ToString()
        {
            var date = Release.PublishedAt is { } published
                ? published.LocalDateTime.ToString("MMM d, yyyy")
                : null;
            var text = $"v{Release.Version}";
            if (date is not null) text += $"  ·  {date}";
            if (Release.IsPrerelease) text += "  ·  pre-release";
            return text;
        }
    }

    /// <summary>
    /// Fetches the tool's last 5 GitHub releases in the background and
    /// populates the release combo box. Runs after the dialog is shown, so the
    /// picker is usable immediately and fills in as results arrive.
    /// </summary>
    private static async Task LoadGitHubReleasesAsync(
        ToolDefinition tool,
        ComboBox ghCombo,
        TextBlock ghStatus,
        Dictionary<string, ToolReleaseVersion> ghReleasesByVersion,
        TextBox customBox,
        Action<string> updatePreview)
    {
        var releases = await ToolVersionHistoryService.GetRecentReleasesAsync(tool.Id, tool.DownloadUrl);
        if (releases is null || releases.Count == 0)
        {
            ghStatus.Text = "Couldn't fetch recent GitHub releases — use the catalog versions or enter a version manually.";
            ghCombo.PlaceholderText = "Unavailable";
            return;
        }

        foreach (var release in releases)
        {
            if (ghReleasesByVersion.ContainsKey(release.Version)) continue;
            ghReleasesByVersion[release.Version] = release;
            ghCombo.Items.Add(new GitHubReleaseChoice(release));
        }

        // Prune releases that were removed between fetches (defensive; cached
        // lists are immutable per fetch).
        if (ghCombo.Items.Count == 0)
        {
            ghStatus.Text = "No published GitHub releases found for this repository.";
            return;
        }

        ghCombo.IsEnabled = true;
        ghCombo.PlaceholderText = "Select a GitHub release…";
        ghStatus.Text = "Fetched live from the vendor's GitHub releases.";

        ghCombo.SelectionChanged += (s, ev) =>
        {
            if (ghCombo.SelectedItem is not GitHubReleaseChoice choice) return;
            customBox.Text = choice.Release.Version;
            updatePreview(choice.Release.Version);
        };
    }

    // ── Logo fallback ───────────────────────────────────────────────────

    private void LogoImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img && img.Parent is Grid parent)
        {
            img.Visibility = Visibility.Collapsed;
            foreach (var child in parent.Children)
            {
                if (child is FontIcon icon)
                {
                    icon.Visibility = Visibility.Visible;
                }
            }
        }
    }

    // ── Install actions ─────────────────────────────────────────────────

    private async void InstallSingleTool_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;
        await RunSingleToolAsync(tool, installAfter: true);
    }

    /// <summary>Compact-list row install button.</summary>
    private async void CompactInstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;
        await RunSingleToolAsync(tool, installAfter: true);
    }

    /// <summary>SplitButton menu: download without running the install step.</summary>
    private async void DownloadOnly_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;
        await RunSingleToolAsync(tool, installAfter: false);
    }

    /// <summary>SplitButton menu: open the tool's vendor homepage.</summary>
    private void OpenHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;
        if (!string.IsNullOrWhiteSpace(tool.Homepage))
        {
            LauncherService.OpenUrl(tool.Homepage);
        }
    }

    private async Task RunSingleToolAsync(ToolDefinition tool, bool installAfter)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || _busy) return;

        if (tool.IsDownloadedOrInstalled || tool.Status == ToolStatus.Downloading) return;

        _downloadCts = new CancellationTokenSource();
        CancelButton.Visibility = Visibility.Visible;
        SetBusy(true, $"Installing {tool.Name}...");

        if (!mw.DownloadQueue.Contains(tool))
            mw.DownloadQueue.Add(tool);

        try
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            await mw.DownloadSvc.DownloadBatchAsync(new[] { tool }, dlFolder, maxConcurrency: 1, ct: _downloadCts.Token);

            if (tool.Status == ToolStatus.Downloaded)
            {
                if (installAfter)
                {
                    StatusText.Text = $"{tool.Name} downloaded successfully.";
                    var res = ArtifactService.Perform(tool, dlFolder);
                    if (!string.IsNullOrWhiteSpace(res.Message))
                    {
                        StatusText.Text = res.Message;
                    }

                    ToastService.Show(
                        $"{tool.Name} ready",
                        res.Success ? res.Message : $"{tool.Name} was downloaded but the install step needs attention.");
                }
                else
                {
                    StatusText.Text = $"{tool.Name} downloaded (install skipped).";
                    ToastService.Show($"{tool.Name} downloaded", "Install it from the Downloads page whenever you're ready.");
                }
            }
            else
            {
                StatusText.Text = $"{tool.Name} download failed.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            CancelButton.Visibility = Visibility.Collapsed;
            _downloadCts?.Dispose();
            _downloadCts = null;
            SetBusy(false);
        }
    }

    // ── Installed Card Button Action Menu ────────────────────────────────

    private void InstalledAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not ToolDefinition tool) return;

        var flyout = new MenuFlyout();

        var specsItem = new MenuFlyoutItem
        {
            Text = $"{tool.Name} Specifications…",
            Icon = new FontIcon { Glyph = "\uE946" }
        };
        specsItem.Click += (_, _) => ToolDetails_Click(btn, e);
        flyout.Items.Add(specsItem);

        var verItem = new MenuFlyoutItem
        {
            Text = $"Switch Version (Current: {tool.DisplayVersionWithV})…",
            Icon = new FontIcon { Glyph = "\uE8EC" }
        };
        verItem.Click += (_, _) => VersionPicker_Click(btn, e);
        flyout.Items.Add(verItem);

        var copyItem = new MenuFlyoutItem
        {
            Text = "Copy Install Command",
            Icon = new FontIcon { Glyph = "\uE8C8" }
        };
        copyItem.Click += (_, _) => CopyCommand_Click(btn, e);
        flyout.Items.Add(copyItem);

        if (!string.IsNullOrWhiteSpace(tool.Homepage))
        {
            var docsItem = new MenuFlyoutItem
            {
                Text = "Open Documentation / Homepage",
                Icon = new FontIcon { Glyph = "\uE8A7" }
            };
            docsItem.Click += (_, _) => LauncherService.OpenUrl(tool.Homepage);
            flyout.Items.Add(docsItem);
        }

        flyout.ShowAt(btn);
    }

    // ── Footer Refresh & Sync ───────────────────────────────────────────

    private void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
        StatusText.Text = $"View refreshed • Catalog: {CatalogService.CatalogFreshness}";
    }

    void IRefreshablePage.RefreshView() => RefreshCatalog_Click(this, new RoutedEventArgs());
}
