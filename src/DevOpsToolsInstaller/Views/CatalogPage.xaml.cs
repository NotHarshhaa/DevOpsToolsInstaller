using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
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

public sealed partial class CatalogPage : Page
{
    private readonly ObservableCollection<ToolCategoryGroup> _groups = new();
    private readonly CollectionViewSource _groupedView;
    private bool _busy;
    private CancellationTokenSource? _downloadCts;
    private string _selectedCategory = "All";
    private string _sortMode = "category"; // az, za, category, kind, downloaded, favorites
    private bool _downloadedOnly;

    public CatalogPage()
    {
        InitializeComponent();

        _groupedView = new CollectionViewSource
        {
            IsSourceGrouped = true,
            Source = _groups
        };
        ToolsList.ItemsSource = _groupedView.View;

        Loaded += CatalogPage_Loaded;
    }

    private async void CatalogPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        if (mw.Tools.Count == 0)
        {
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
                SetBusy(false);
            }
        }

        if (!string.IsNullOrWhiteSpace(mw.PendingCatalogSearchQuery))
        {
            var query = mw.PendingCatalogSearchQuery;
            mw.PendingCatalogSearchQuery = null;
            SearchBox.Text = query;
        }

        ApplyFilter();
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

        UpdateScrollButtons();
    }

    private void ApplyFilter()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var query = SearchBox.Text?.Trim() ?? "";

        bool Matches(ToolDefinition tool)
        {
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

            var downloadedMatch = !_downloadedOnly || tool.Status == ToolStatus.Downloaded;

            return textMatch && categoryMatch && downloadedMatch;
        }

        var filtered = mw.Tools.Where(Matches);

        // Apply sort
        var sorted = _sortMode switch
        {
            "az" => filtered.OrderBy(t => t.Name),
            "za" => filtered.OrderByDescending(t => t.Name),
            "kind" => filtered.OrderBy(t => t.KindLabel).ThenBy(t => t.Name),
            "downloaded" => filtered.OrderByDescending(t => t.Status == ToolStatus.Downloaded).ThenBy(t => t.Name),
            "favorites" => filtered.OrderByDescending(t => t.IsFavorite).ThenBy(t => t.Category).ThenBy(t => t.Name),
            _ => filtered.OrderBy(t => t.Category).ThenBy(t => t.Name), // "category" (default)
        };

        // Group by category for list display
        var groupKey = _sortMode switch
        {
            "kind" => (Func<ToolDefinition, string>)(t => t.KindLabel),
            _ => t => t.Category
        };

        var grouped = sorted
            .GroupBy(groupKey)
            .Select(g => new ToolCategoryGroup(g.Key, g));

        _groups.Clear();
        foreach (var group in grouped)
            _groups.Add(group);

        var count = VisibleTools.Count();
        StatusText.Text = $"{count} of {mw.Tools.Count} tools shown";
    }

    // ── Category Chips ──────────────────────────────────────────────────

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedBtn) return;
        var tag = clickedBtn.Tag as string ?? "All";
        _selectedCategory = tag;

        // Visual update on chips
        foreach (var child in CategoryChipsPanel.Children)
        {
            if (child is Button btn)
            {
                var isSelected = (btn.Tag as string) == tag;
                btn.Style = (Style)Application.Current.Resources[isSelected ? "SelectedCategoryChipStyle" : "CategoryChipStyle"];
                btn.CornerRadius = new CornerRadius(4);
            }
        }

        // Bring clicked chip into view smoothly
        clickedBtn.StartBringIntoView();

        ApplyFilter();
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

    /// <summary>Every tool currently visible across all category groups.</summary>
    private IEnumerable<ToolDefinition> VisibleTools => _groups.SelectMany(g => g);

    // ── Search & Command Bar Layout ──────────────────────────────────────

    private void CommandBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;

        // When container is very wide (>= 1220px), place Search and Toolbar on the same line.
        // Otherwise, Search takes Row 0 (full width) and Toolbar takes Row 1.
        bool isTwoRows = width < 1220;

        if (isTwoRows)
        {
            SecondRowDef.Height = GridLength.Auto;
            Grid.SetRow(ToolbarPanel, 1);
            Grid.SetColumn(ToolbarPanel, 0);
            Grid.SetColumnSpan(ToolbarPanel, 2);
            SearchBox.MaxWidth = double.PositiveInfinity;
        }
        else
        {
            SecondRowDef.Height = new GridLength(0);
            Grid.SetRow(ToolbarPanel, 0);
            Grid.SetColumn(ToolbarPanel, 1);
            Grid.SetColumnSpan(ToolbarPanel, 1);
            SearchBox.MaxWidth = 360;
        }

        // Dynamically adjust button labels based on available width:
        // When width is compact (< 880px), collapse text on secondary buttons so they display as compact icon buttons with ToolTips.
        // When width is very compact (< 620px), shorten the CTA button text to "Download".
        bool isCompact = width < 880;
        bool isVeryCompact = width < 620;

        var labelVisibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        var sepVisibility = isCompact ? Visibility.Collapsed : Visibility.Visible;

        if (SortButtonText != null) SortButtonText.Visibility = labelVisibility;
        if (DownloadedToggleText != null) DownloadedToggleText.Visibility = labelVisibility;
        if (PresetsButtonText != null) PresetsButtonText.Visibility = labelVisibility;
        if (SelectAllButtonText != null) SelectAllButtonText.Visibility = labelVisibility;
        if (ClearButtonText != null) ClearButtonText.Visibility = labelVisibility;
        if (ProfileButtonText != null) ProfileButtonText.Visibility = labelVisibility;

        if (Separator1 != null) Separator1.Visibility = sepVisibility;
        if (Separator2 != null) Separator2.Visibility = sepVisibility;
        if (Separator3 != null) Separator3.Visibility = sepVisibility;

        if (DownloadButtonText != null)
        {
            DownloadButtonText.Text = isVeryCompact ? "Download" : "Download selected";
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ApplyFilter();
    }

    // ── Sort & Filter ───────────────────────────────────────────────────

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string tag) return;
        _sortMode = tag;
        ApplyFilter();
    }

    private void DownloadedOnly_Click(object sender, RoutedEventArgs e)
    {
        _downloadedOnly = DownloadedOnlyToggle.IsChecked == true;
        ApplyFilter();
    }

    // ── Favorites ───────────────────────────────────────────────────────

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;
        tool.IsFavorite = FavoritesService.Toggle(tool.Id);
    }

    // ── Copy Install Command ────────────────────────────────────────────

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var commands = new List<string>();

        // Build a useful clipboard block
        commands.Add($"# {tool.Name} v{tool.Version}");
        commands.Add($"# Direct download URL:");
        commands.Add($"curl -LO \"{tool.DownloadUrl}\"");

        if (!string.IsNullOrWhiteSpace(tool.Sha256))
        {
            commands.Add($"# SHA256: {tool.Sha256}");
        }

        var text = string.Join(Environment.NewLine, commands);

        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);

        // Provide feedback
        StatusText.Text = $"Copied install command for {tool.Name}";
    }

    // ── Presets ──────────────────────────────────────────────────────────

    private void PopulatePresetsMenu()
    {
        var mw = App.MainWindowInstance;
        if (mw is null || PresetsMenuFlyout is null) return;

        PresetsMenuFlyout.Items.Clear();

        // 1. "Browse All Stacks…" at the top
        var browseAllItem = new MenuFlyoutItem
        {
            Text = "Explore All Stacks Section…",
            Icon = new FontIcon { Glyph = "\uE71D" }
        };
        browseAllItem.Click += (s, e) =>
        {
            mw.NavigateTo("Stacks");
        };
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
            item.Click += (s, e) =>
            {
                ApplyBundleSelection(capturedBundle);
            };
            PresetsMenuFlyout.Items.Add(item);
        }
    }

    private void ApplyBundleSelection(ToolBundle bundle)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        int selectedCount = mw.SelectBundle(bundle);
        StatusText.Text = $"Selected {selectedCount} tools from {bundle.Name}";
    }

    // ── Select / Clear ──────────────────────────────────────────────────

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in VisibleTools) item.IsSelected = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in VisibleTools) item.IsSelected = false;
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

        // Get the HWND from the current window
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
        StatusText.Text = $"Imported profile — {count} tool(s) selected from {file.Name}";
    }

    // ── Download ────────────────────────────────────────────────────────

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || _busy) return;

        var selected = VisibleTools
            .Where(t => t.IsSelected && t.Status != ToolStatus.Downloaded && t.Status != ToolStatus.Downloading)
            .ToList();

        if (selected.Count == 0)
        {
            var anySelected = VisibleTools.Any(t => t.IsSelected);
            StatusText.Text = anySelected
                ? "Selected tool(s) are already downloaded or currently downloading."
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

    // ── Tool Detail Dialog ──────────────────────────────────────────────

    private async void ToolDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var panel = new StackPanel { Spacing = 14, MaxWidth = 520 };

        // Header with icon + name
        var headerGrid = new Grid { ColumnSpacing = 14 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1)
        };
        var logoSource = Services.ToolLogoService.GetLogo(tool.LogoUrl);
        if (logoSource is not null)
        {
            iconBorder.Child = new Image
            {
                Source = logoSource,
                Width = 32,
                Height = 32,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            iconBorder.Child = new FontIcon
            {
                Glyph = tool.IconGlyph,
                FontSize = 20,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconBorder, 0);
        headerGrid.Children.Add(iconBorder);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        titleStack.Children.Add(new TextBlock { Text = tool.NameWithVersion, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });

        var badgeStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        badgeStack.Children.Add(new TextBlock { Text = $"{tool.Category}  •  {tool.KindLabel}", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], FontSize = 12 });
        if (tool.IsFavorite)
        {
            badgeStack.Children.Add(new FontIcon { Glyph = "\uE735", FontSize = 13, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        }
        titleStack.Children.Add(badgeStack);

        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);
        panel.Children.Add(headerGrid);

        // Description
        panel.Children.Add(new TextBlock { Text = tool.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

        // Metadata grid
        var metaBorder = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14)
        };
        var metaStack = new StackPanel { Spacing = 6 };
        metaStack.Children.Add(new TextBlock { Text = $"File: {tool.FileName}", FontSize = 12 });
        metaStack.Children.Add(new TextBlock { Text = $"Deployment: {tool.ActionLabel}", FontSize = 12 });
        metaStack.Children.Add(new TextBlock { Text = $"Status: {tool.StatusText}", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        metaStack.Children.Add(new TextBlock { Text = $"Version: {tool.SelectedVersion} ({(tool.IsPreviousVersionSelected ? "Custom Selection" : "Latest Stable")})", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(tool.Sha256))
        {
            metaStack.Children.Add(new TextBlock { Text = $"SHA256: {tool.Sha256}", FontSize = 11, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        }
        if (!string.IsNullOrWhiteSpace(tool.Homepage))
            metaStack.Children.Add(new TextBlock { Text = $"Homepage: {tool.Homepage}", FontSize = 11, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        metaBorder.Child = metaStack;
        panel.Children.Add(metaBorder);

        // Action buttons row
        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };

        // Switch version button
        var switchVerBtn = new Button { Padding = new Thickness(14, 8, 14, 8), CornerRadius = new CornerRadius(8) };
        var switchVerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        switchVerStack.Children.Add(new FontIcon { Glyph = "\uE8EC", FontSize = 13 });
        switchVerStack.Children.Add(new TextBlock { Text = "Switch Version" });
        switchVerBtn.Content = switchVerStack;

        // Copy CLI name button
        var copyNameBtn = new Button { Padding = new Thickness(14, 8, 14, 8), CornerRadius = new CornerRadius(8) };
        var copyNameStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        copyNameStack.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 13 });
        copyNameStack.Children.Add(new TextBlock { Text = "Copy CLI name" });
        copyNameBtn.Content = copyNameStack;
        copyNameBtn.Click += (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(tool.Id);
            Clipboard.SetContent(dp);
            StatusText.Text = $"Copied '{tool.Id}' to clipboard";
        };
        actionPanel.Children.Add(copyNameBtn);

        // Copy command button
        var copyCmdBtn = new Button { Padding = new Thickness(14, 8, 14, 8), CornerRadius = new CornerRadius(8) };
        var copyCmdStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        copyCmdStack.Children.Add(new FontIcon { Glyph = "\uE756", FontSize = 13 });
        copyCmdStack.Children.Add(new TextBlock { Text = "Copy download URL" });
        copyCmdBtn.Content = copyCmdStack;
        copyCmdBtn.Click += (_, _) =>
        {
            var dp = new DataPackage();
            dp.SetText(tool.DownloadUrl);
            Clipboard.SetContent(dp);
            StatusText.Text = $"Copied download URL for {tool.Name}";
        };
        actionPanel.Children.Add(copyCmdBtn);
        actionPanel.Children.Add(switchVerBtn);

        panel.Children.Add(actionPanel);

        var dialog = new ContentDialog
        {
            Title = "Tool Specifications",
            Content = panel,
            PrimaryButtonText = !string.IsNullOrWhiteSpace(tool.Homepage) ? "Open Documentation" : "",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        switchVerBtn.Click += async (_, _) =>
        {
            dialog.Hide();
            await ShowVersionPickerDialogAsync(tool);
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tool.Homepage))
        {
            LauncherService.OpenUrl(tool.Homepage);
        }
    }

    // ── Busy state ──────────────────────────────────────────────────────

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = !busy;
        SelectAllButton.IsEnabled = !busy;
        ClearButton.IsEnabled = !busy;
        PresetsButton.IsEnabled = !busy;
        ProfileButton.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }

    private async void VersionPicker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;
        await ShowVersionPickerDialogAsync(tool);
    }

    private async Task ShowVersionPickerDialogAsync(ToolDefinition tool)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 480 };

        panel.Children.Add(new TextBlock
        {
            Text = $"Choose a specific release version of {tool.Name}. You can select from known releases or enter a custom version tag.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
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
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
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

        panel.Children.Add(new TextBlock { Text = "Available Releases:", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(combo);
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
                tool.SetVersion(chosen);
                StatusText.Text = $"Updated {tool.Name} to version v{tool.SelectedVersion}.";
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            // Reset to latest
            tool.SetVersion(tool.Version);
            StatusText.Text = $"Reset {tool.Name} to latest version (v{tool.Version}).";
        }
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
}
