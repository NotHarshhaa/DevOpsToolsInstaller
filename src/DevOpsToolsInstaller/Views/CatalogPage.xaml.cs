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

public sealed partial class CatalogPage : Page
{
    private readonly ObservableCollection<ToolDefinition> _filteredTools = new();
    private bool _busy;
    private CancellationTokenSource? _downloadCts;
    private string _selectedCategory = "All";
    private string _sortMode = "category"; // az, za, category, kind, downloaded, favorites
    private bool _downloadedOnly;
    private ToolBundle? _activeBundle;
    private DateTime _lastCatalogSync = DateTime.Now;

    public CatalogPage()
    {
        InitializeComponent();
        CategorySegmented.SelectionChanged += CategorySegmented_SelectionChanged;

        ToolsGridView.ItemsSource = _filteredTools;

        Loaded += CatalogPage_Loaded;
    }

    private async void CatalogPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        if (mw.Tools.Count == 0)
        {
            // Shimmer placeholders while the catalog loads; suppressed under
            // the Windows "reduce animations" setting (falls back to the ring).
            var showShimmer = AnimationSettingsHelper.AnimationsEnabled;
            LoadingShimmerPanel.Visibility = showShimmer ? Visibility.Visible : Visibility.Collapsed;
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
                LoadingShimmerPanel.Visibility = Visibility.Collapsed;
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
    }

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

            var downloadedMatch = !_downloadedOnly || tool.IsDownloadedOrInstalled;

            return textMatch && categoryMatch && downloadedMatch;
        }

        var filtered = mw.Tools.Where(Matches);

        // Apply sort
        var sorted = _sortMode switch
        {
            "az" => filtered.OrderBy(t => t.Name),
            "za" => filtered.OrderByDescending(t => t.Name),
            "kind" => filtered.OrderBy(t => t.KindLabel).ThenBy(t => t.Name),
            "downloaded" => filtered.OrderByDescending(t => t.IsDownloadedOrInstalled).ThenBy(t => t.Name),
            "favorites" => filtered.OrderByDescending(t => t.IsFavorite).ThenBy(t => t.Category).ThenBy(t => t.Name),
            _ => filtered.OrderBy(t => t.Category).ThenBy(t => t.Name), // "category" (default)
        };

        _filteredTools.Clear();
        foreach (var tool in sorted)
            _filteredTools.Add(tool);

        var count = _filteredTools.Count;
        StatusText.Text = $"{count} of {mw.Tools.Count} tools shown";
        if (FooterStatusText != null)
        {
            FooterStatusText.Text = $"{count} Tools Available | Last Sync: {GetLastSyncText()}";
        }
    }

    // ── Category Chips ──────────────────────────────────────────────────

    private void CategorySegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Use the sender: this event can fire during XAML parse (initial
        // IsSelected) before the generated field is connected.
        if (sender is not CommunityToolkit.WinUI.Controls.Segmented segmented ||
            segmented.SelectedItem is not CommunityToolkit.WinUI.Controls.SegmentedItem selected)
        {
            return;
        }
        _selectedCategory = selected.Tag as string ?? "All";
        selected.StartBringIntoView();
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

    /// <summary>Every tool currently visible in the catalog grid.</summary>
    private IEnumerable<ToolDefinition> VisibleTools => _filteredTools;

    // ── Search & Command Bar Layout ──────────────────────────────────────
    // (The native CommandBar handles overflow/labels automatically, so the
    //  old manual width-responsive layout code was removed with it.)

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
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;
        tool.IsFavorite = FavoritesService.Toggle(tool.Id);
    }

    // ── Copy Install Command ────────────────────────────────────────────

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

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

        _activeBundle = bundle;
        int selectedCount = mw.SelectBundle(bundle);

        ActiveStackInfoBar.Title = $"Stack Filter: {bundle.Name} ({bundle.Tools.Count} tools)";
        ActiveStackInfoBar.Message = $"Showing only tools included in {bundle.Name}. All {selectedCount} tools are pre-selected. Click 'Download' to install them, or customize your selection.";
        ActiveStackInfoBar.IsOpen = true;

        HeaderCountBadge.Text = $"{bundle.Name} ({bundle.Tools.Count} tools)";
        StatusText.Text = $"Showing {selectedCount} tools from {bundle.Name}";

        ApplyFilter();
    }

    private void ClearStackFilter_Click(object sender, RoutedEventArgs e)
    {
        _activeBundle = null;
        ActiveStackInfoBar.IsOpen = false;
        HeaderCountBadge.Text = "All tools";
        ApplyFilter();
    }

    private void ActiveStackInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _activeBundle = null;
        HeaderCountBadge.Text = "All tools";
        ApplyFilter();
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

    // ── Tool Detail Dialog ──────────────────────────────────────────────

    private void ToolDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        // Animate the whole card into the detail page (when allowed).
        FrameworkElement? animationSource = sender as FrameworkElement;
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(animationSource!);
        while (parent is not null)
        {
            if (parent is Border border && border.Child is not null &&
                Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(border) is Microsoft.UI.Xaml.Controls.GridViewItem)
            {
                animationSource = border;
                break;
            }
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }

        if (Helpers.AnimationSettingsHelper.AnimationsEnabled && animationSource is not null)
        {
            Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService
                .GetForCurrentView().PrepareToAnimate("toolCard", animationSource);
        }

        Frame.Navigate(typeof(ToolDetailPage), tool);
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

    // ── Responsive Grid Sizing ──────────────────────────────────────────

    private void ToolsGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ToolsGridView.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
        {
            var width = e.NewSize.Width - 24;
            if (width > 200)
            {
                int columns = width >= 1080 ? 3 : (width >= 700 ? 2 : 1);
                wrapGrid.ItemWidth = Math.Floor(width / columns);
            }
        }
    }

    // ── 1-Click Install Button on Card ──────────────────────────────────

    private async void InstallSingleTool_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
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
        _lastCatalogSync = DateTime.Now;
        ApplyFilter();
        StatusText.Text = "Catalog refreshed.";
    }

    private string GetLastSyncText()
    {
        var elapsed = DateTime.Now - _lastCatalogSync;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} mins ago";
        return $"{(int)elapsed.TotalHours} hours ago";
    }
}
