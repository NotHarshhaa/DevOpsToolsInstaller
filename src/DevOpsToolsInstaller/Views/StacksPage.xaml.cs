using System;
using System.Collections.Generic;
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

public sealed partial class StacksPage : Page, IRefreshablePage
{
    public StackBuilderViewModel BuilderViewModel { get; } = new();

    private List<BundleCardViewModel> _allCardModels = new();
    private string _selectedCategory = "All";
    private ItemsWrapGrid? _stacksWrapGrid;
    private ToolDefinition? _draggedTool;
    private bool _isBuilderMode;

    public StacksPage()
    {
        InitializeComponent();
        CategorySegmented.SelectionChanged += CategorySegmented_SelectionChanged;
        ViewModeSegmented.SelectionChanged += ViewModeSegmented_SelectionChanged;
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

        await mw.EnsureCatalogLoadedAsync();

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
        if (!_isBuilderMode && Header != null)
        {
            Header.BadgeText = $"{_allCardModels.Count} Presets";
        }
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

    private async void InstallStack_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args)
    {
        if (sender is not FrameworkElement btn || btn.Tag is not string bundleId) return;
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

        if (DownloadSchedulerService.IsOffPeakActive)
        {
            foreach (var tool in toDownload)
            {
                tool.Status = ToolStatus.Scheduled;
                tool.Progress = 0;
            }
            ActivityLogService.Info(targetBundle.Name, $"Stack transfer scheduled for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).");
            if (SettingsService.EnableNotifications)
            {
                ToastService.Show($"{targetBundle.Name} Scheduled", $"Stack queued for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).");
            }
            mw.NavigateTo("Downloads");
            return;
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

    private async void InstallCustomStack_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        if (BuilderViewModel.StackTools.Count == 0)
        {
            ShowInfoBar(InfoBarSeverity.Warning, "Stack is Empty", "Add tools to the stack before installing.");
            return;
        }

        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        var toDownload = new List<ToolDefinition>();
        foreach (var stackTool in BuilderViewModel.StackTools)
        {
            if (toolsById.TryGetValue(stackTool.Id, out var tool))
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
            ShowInfoBar(InfoBarSeverity.Informational, "All Tools Installed",
                $"All {BuilderViewModel.StackTools.Count} tools in '{BuilderViewModel.StackName}' are already downloaded or installed.");
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

        if (DownloadSchedulerService.IsOffPeakActive)
        {
            foreach (var tool in toDownload)
            {
                tool.Status = ToolStatus.Scheduled;
                tool.Progress = 0;
            }
            ActivityLogService.Info(BuilderViewModel.StackName,
                $"Custom stack transfer scheduled for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).");
            if (SettingsService.EnableNotifications)
            {
                ToastService.Show($"{BuilderViewModel.StackName} Scheduled",
                    $"Custom stack queued for off-peak hours ({SettingsService.GetScheduleWindowDescription()}).");
            }
            mw.NavigateTo("Downloads");
            return;
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
                    $"{BuilderViewModel.StackName}: {installedCount}/{toDownload.Count} downloaded",
                    "Downloaded tools are ready to deploy from the Downloads page.");
            }
            catch
            {
                // Managed by DownloadsPage
            }
        });

        // Navigate directly to Downloads page
        mw.NavigateTo("Downloads");
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
