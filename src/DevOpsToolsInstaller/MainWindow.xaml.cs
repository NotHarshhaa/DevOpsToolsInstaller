using System.Collections.ObjectModel;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DevOpsToolsInstaller.Helpers;
using static DevOpsToolsInstaller.Services.TrayIconService;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;
using DevOpsToolsInstaller.Views;

namespace DevOpsToolsInstaller;

public sealed partial class MainWindow : Window
{
    private readonly CatalogService _catalog = new();
    private readonly DownloadService _download = new();

    /// <summary>
    /// Shared tool list — loaded once, referenced by all pages.
    /// </summary>
    public ObservableCollection<ToolDefinition> Tools { get; } = new();

    /// <summary>
    /// Curated tool stacks / presets loaded from bundles.json.
    /// </summary>
    public ObservableCollection<ToolBundle> Bundles { get; } = new();

    /// <summary>
    /// Tools that are queued / in-progress / completed downloads.
    /// </summary>
    public ObservableCollection<ToolDefinition> DownloadQueue { get; } = new();

    /// <summary>
    /// Holds a bundle ID to select when navigating to CatalogPage from another page (e.g. HomePage).
    /// </summary>
    public string? PendingBundleSelectionId { get; set; }

    public CatalogService CatalogSvc => _catalog;
    public DownloadService DownloadSvc => _download;

    private readonly SemaphoreSlim _catalogLoadLock = new(1, 1);
    private bool _catalogLoaded;
    private bool _forceExit;
    private bool _onboardingStarted;
    private int _lastUpdateBadgeCount;

    /// <summary>
    /// Loads the catalog and bundles into <see cref="Tools"/> and <see cref="Bundles"/> exactly once.
    /// </summary>
    public async Task EnsureCatalogLoadedAsync()
    {
        if (_catalogLoaded) return;

        await _catalogLoadLock.WaitAsync();
        try
        {
            if (_catalogLoaded) return;

            var toolsTask = _catalog.LoadCatalogAsync();
            var bundlesTask = _catalog.LoadBundlesAsync();
            await Task.WhenAll(toolsTask, bundlesTask);

            var tools = await toolsTask;
            var bundles = await bundlesTask;

            // Mark already-downloaded tools based on files on disk.
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            foreach (var tool in tools)
            {
                if (DownloadService.IsAlreadyDownloaded(tool, dlFolder))
                {
                    tool.Status = ToolStatus.Downloaded;
                    tool.Progress = 100;
                }
            }

            Tools.Clear();
            foreach (var tool in tools)
            {
                tool.IsFavorite = FavoritesService.IsFavorite(tool.Id);
                Tools.Add(tool);
            }

            Bundles.Clear();
            foreach (var bundle in bundles)
            {
                Bundles.Add(bundle);
            }

            _catalogLoaded = true;
        }
        finally
        {
            _catalogLoadLock.Release();
        }
    }

    /// <summary>
    /// Selects all tools belonging to the specified bundle and unselects others.
    /// Returns the number of tools selected.
    /// </summary>
    public int SelectBundle(ToolBundle bundle)
    {
        var targetIds = new HashSet<string>(bundle.Tools, StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var tool in Tools)
        {
            tool.IsSelected = targetIds.Contains(tool.Id);
            if (tool.IsSelected) count++;
        }
        return count;
    }

    /// <summary>
    /// Navigates to the Catalog page and triggers selection of the given bundle.
    /// </summary>
    public void NavigateToCatalogWithBundle(string bundleId)
    {
        PendingBundleSelectionId = bundleId;
        NavigateTo("Catalog");
    }

    /// <summary>
    /// Navigates to the Catalog page and pre-populates the search query.
    /// </summary>
    public void NavigateToCatalogWithSearch(string query)
    {
        PendingCatalogSearchQuery = query;
        NavigateTo("Catalog");
    }

    public MainWindow()
    {
        InitializeComponent();
        Title = "DevOps Tools Installer";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Native Windows 11 Mica backdrop (the translucent, desktop-tinted
        // "blur"). Falls back gracefully to a solid background on OSes that
        // don't support it.
        TrySetMicaBackdrop();

        // Set taskbar and window icon
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (System.IO.File.Exists(iconPath))
        {
            AppWindow?.SetIcon(iconPath);
        }

        // Sensible default window size on first display.
        AppWindow?.Resize(new Windows.Graphics.SizeInt32(1280, 820));

        AppTitleBarIconSource.ImageSource = AppLogoHelper.GetLogoImage();

        RestoreSavedWindowBounds();
        ApplyTheme(SettingsService.Theme);

        InitializeTray();

        // Track the last visited page so it can be restored on next launch.
        ContentFrame.Navigated += (s, e) =>
        {
            if (e is not Microsoft.UI.Xaml.Navigation.NavigationEventArgs nav) return;
            SettingsService.LastPage = nav.Content?.GetType().Name switch
            {
                nameof(HomePage) => "Home",
                nameof(CatalogPage) => "Catalog",
                nameof(StacksPage) => "Stacks",
                nameof(DownloadsPage) => "Downloads",
                nameof(InstalledPage) => "Installed",
                nameof(SettingsPage) => "Settings",
                nameof(AboutPage) => "About",
                _ => SettingsService.LastPage
            };
        };

        // Close-to-tray: hide the window instead of exiting (unless the user
        // picked Exit from the tray menu).
        if (AppWindow is not null)
        {
            AppWindow.Closing += (s, e) =>
            {
                if (_forceExit || !SettingsService.CloseToTray || !TrayIconService.IsAvailable)
                {
                    return;
                }
                e.Cancel = true;
                SaveWindowState();
                _ = DispatcherQueue.TryEnqueue(() => AppWindow.Hide());
            };
        }

        Closed += (s, e) =>
        {
            TrayIconService.Shutdown();
            SaveWindowState();
            ToastService.Shutdown();
        };

        Activated += async (s, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated && !_onboardingStarted)
            {
                _onboardingStarted = true;
                SaveWindowState();
                await RunOnboardingIfFirstRunAsync();
            }
        };

        SetupUpdateScheduler();

        RootGrid.ActualThemeChanged += (s, e) =>
        {
            UpdateTitleBarButtonColors();
        };

        // Selecting the first item raises SelectionChanged, which navigates to
        // HomePage. Do NOT also call ContentFrame.Navigate here — that would
        // create a second HomePage instance and race the catalog load.
        NavView.SelectedItem = NavView.MenuItems[0];

        // Restore the last visited page from the previous session.
        var lastPage = SettingsService.LastPage;
        if (!string.IsNullOrEmpty(lastPage) && lastPage != "Home")
        {
            NavigateTo(lastPage);
        }

        NavView.Loaded += (s, e) =>
        {
            RemoveTogglePaneButtonFocusVisual(NavView);
            ApplyPaneAcrylicSurface();
            ContentFrame.Focus(FocusState.Programmatic);
        };

        Activated += (s, e) =>
        {
            RemoveTogglePaneButtonFocusVisual(NavView);
        };
    }

    public void ApplyTheme(AppTheme theme)
    {
        var elementTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (RootGrid != null)
        {
            RootGrid.RequestedTheme = elementTheme;
            UpdateTitleBarButtonColors();
        }
    }

    /// <summary>
    /// Holds a pending search query to apply when navigating to the Tool Catalog.
    /// </summary>
    public string? PendingCatalogSearchQuery { get; set; }

    /// <summary>
    /// Applies the Mica Alt system backdrop when the OS supports it.
    /// Falls back to DesktopAcrylic on Windows 10/unsupported versions.
    /// </summary>
    private void TrySetMicaBackdrop()
    {
        try
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop
                {
                    Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt
                };
            }
            else if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            }
        }
        catch
        {
            // Backdrop is a nice-to-have; ignore if unavailable.
        }
    }

    private void UpdateTitleBarButtonColors()
    {
        var titleBar = this.AppWindow?.TitleBar;
        if (titleBar is null || RootGrid is null) return;

        bool isDark = RootGrid.ActualTheme == ElementTheme.Dark;

        if (isDark)
        {
            titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 255, 255, 255);
            titleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        else
        {
            titleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(25, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
    }

    private void NavView_SelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;

        NavigateToTag(tag);
    }

    private void NavigateToTag(string tag)
    {
        var pageType = tag switch
        {
            "Home"      => typeof(HomePage),
            "Catalog"   => typeof(CatalogPage),
            "Stacks"    => typeof(StacksPage),
            "Downloads" => typeof(DownloadsPage),
            "Installed" => typeof(InstalledPage),
            "Settings"  => typeof(SettingsPage),
            "About"     => typeof(AboutPage),
            _           => typeof(HomePage)
        };

        ContentFrame.Navigate(pageType);
    }

    public async Task OpenCuratedStacksDialogAsync()
    {
        await EnsureCatalogLoadedAsync();
        if (ContentFrame.XamlRoot is null) return;
        var selected = await Controls.BundleSelectionDialog.ShowAsync(ContentFrame.XamlRoot, Bundles, Tools);
        if (selected != null)
        {
            NavigateToCatalogWithBundle(selected.Id);
        }
    }

    private void NavSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            PendingCatalogSearchQuery = query;
            NavigateTo("Catalog");
        }
    }

    private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var text = sender.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                sender.ItemsSource = null;
                return;
            }

            var matches = Tools
                .Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                            t.Category.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .Select(t => t.Name)
                .ToList();
            sender.ItemsSource = matches;
        }
    }

    /// <summary>
    /// Navigate to a page by its sidebar tag. Used by quick-link buttons.
    /// </summary>
    public void NavigateTo(string tag)
    {
        var allItems = NavView.MenuItems.OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>());

        foreach (var item in allItems)
        {
            if (item.Tag as string == tag)
            {
                if (ReferenceEquals(NavView.SelectedItem, item))
                {
                    NavigateToTag(tag);
                }
                else
                {
                    NavView.SelectedItem = item;
                }
                return;
            }
        }
    }

    // ── System tray ──────────────────────────────────────────────────────
    // Native Shell_NotifyIcon icon; right-click opens a WinUI-styled
    // MenuFlyout hosted in a borderless flyout window (TrayMenuWindow).

    private TrayMenuWindow? _trayMenuWindow;

    private void InitializeTray()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (!System.IO.File.Exists(iconPath)) return;

        if (!TrayIconService.InitializeExternalMenu(iconPath, "DevOps Tools Installer")) return;

        TrayIconService.OpenRequested += () =>
            DispatcherQueue.TryEnqueue(ShowMainWindow);
        TrayIconService.ContextMenuRequested += point =>
            DispatcherQueue.TryEnqueue(() => ShowTrayMenu(point));
    }

    private void ShowTrayMenu(POINT point)
    {
        if (_trayMenuWindow is null)
        {
            _trayMenuWindow = new TrayMenuWindow(
                () => DispatcherQueue.TryEnqueue(ShowMainWindow),
                () => DispatcherQueue.TryEnqueue(() => NavigateTo("Catalog")),
                () => DispatcherQueue.TryEnqueue(() => NavigateTo("Installed")),
                () => DispatcherQueue.TryEnqueue(ExitApplication));
        }
        _trayMenuWindow.ShowAt(point);
    }

    private void ShowMainWindow()
    {
        AppWindow.Show();
        Activate();
    }

    private void ExitApplication()
    {
        _forceExit = true;
        TrayIconService.Shutdown();
        _trayMenuWindow?.CloseWindow();
        Close();
    }

    // ── Window state persistence ─────────────────────────────────────────

    /// <summary>Applies the saved window bounds ("x,y,w,h") from the last session.</summary>
    private void RestoreSavedWindowBounds()
    {
        try
        {
            var parts = SettingsService.WindowBounds.Split(',');
            if (parts.Length == 4 &&
                int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y) &&
                int.TryParse(parts[2], out var w) && int.TryParse(parts[3], out var h) &&
                w > 200 && h > 200)
            {
                AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
                AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
                return;
            }
        }
        catch
        {
            // Fall through to the default size.
        }

        AppWindow?.Resize(new Windows.Graphics.SizeInt32(1280, 820));
    }

    private void SaveWindowState()
    {
        try
        {
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            SettingsService.WindowBounds = $"{pos.X},{pos.Y},{size.Width},{size.Height}";
            SettingsService.SaveSettings();
        }
        catch
        {
            // Persistence is best-effort.
        }
    }

    // ── Ctrl+K command palette ───────────────────────────────────────────

    private sealed record PaletteCommand(string Title, string Glyph, Action Run);

    private void CommandPalette_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowCommandPaletteAsync();
    }

    private async System.Threading.Tasks.Task ShowCommandPaletteAsync()
    {
        if (ContentFrame.XamlRoot is null) return;

        var search = new AutoSuggestBox
        {
            PlaceholderText = "Type a page, tool, or action...",
            QueryIcon = new FontIcon { Glyph = "\uE721" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var hint = new TextBlock
        {
            Text = "Enter runs the highlighted command. Pages, tools (install), and update checks are all here.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var panel = new StackPanel { Spacing = 4, MinWidth = 460 };
        panel.Children.Add(search);
        panel.Children.Add(hint);

        var dialog = new ContentDialog
        {
            Title = "Quick actions  (Ctrl+K)",
            Content = panel,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ContentFrame.XamlRoot
        };

        PaletteCommand? chosen = null;

        search.TextChanged += (s, e) =>
        {
            s.ItemsSource = BuildPaletteCommands(s.Text).Take(8).ToList();
        };

        search.QuerySubmitted += (s, e) =>
        {
            if (e.ChosenSuggestion is PaletteCommand cmd)
            {
                chosen = cmd;
            }
            else
            {
                var top = BuildPaletteCommands(e.QueryText).FirstOrDefault();
                if (top is not null) chosen = top;
            }
            dialog.Hide();
        };

        await dialog.ShowAsync();

        if (chosen is not null)
        {
            DispatcherQueue.TryEnqueue(() => chosen.Run());
        }
    }

    private System.Collections.Generic.IEnumerable<PaletteCommand> BuildPaletteCommands(string query)
    {
        query = query?.Trim() ?? string.Empty;
        bool match(string text) => string.IsNullOrEmpty(query) ||
            text.Contains(query, StringComparison.OrdinalIgnoreCase);

        var commands = new System.Collections.Generic.List<PaletteCommand>
        {
            new("Go to Home", "\uE80F", () => NavigateTo("Home")),
            new("Go to Tool Catalog", "\uE8F1", () => NavigateTo("Catalog")),
            new("Go to Stacks", "\uE71D", () => NavigateTo("Stacks")),
            new("Go to Downloads", "\uE896", () => NavigateTo("Downloads")),
            new("Go to Installed", "\uE73E", () => NavigateTo("Installed")),
            new("Go to Settings", "\uE713", () => NavigateTo("Settings")),
            new("Go to About", "\uE946", () => NavigateTo("About")),
            new("Check for tool updates", "\uE895", () => NavigateTo("Installed")),
        };

        foreach (var tool in Tools)
        {
            if (match(tool.Name) && tool.Status != ToolStatus.Downloaded && tool.Status != ToolStatus.Downloading)
            {
                commands.Add(new PaletteCommand($"Install {tool.Name}", "\uE896",
                    () => ToolActions.RunInBackground(tool, installAfter: true)));
            }
        }

        return commands.Where(c => match(c.Title));
    }

    // ── First-run onboarding (TeachingTips, 3 steps max) ─────────────────

    private async System.Threading.Tasks.Task RunOnboardingIfFirstRunAsync()
    {
        if (!SettingsService.ShowOnboardingTips || SettingsService.OnboardingCompleted) return;
        if (ContentFrame.XamlRoot is null) return;

        await System.Threading.Tasks.Task.Delay(1200);

        if (!SettingsService.ShowOnboardingTips || SettingsService.OnboardingCompleted) return;

        var tip1 = new TeachingTip
        {
            Title = "Search anything, instantly",
            Content = "Use the search box up here — or press Ctrl+K anywhere — to jump to pages, install tools, and run actions.",
            Target = AppTitleBar,
            PreferredPlacement = TeachingTipPlacementMode.Bottom,
            CloseButtonContent = "Next",
            IsLightDismissEnabled = false
        };
        tip1.Closed += (s, e) => ShowOnboardingTip2();
        tip1.IsOpen = true;
    }

    private void ShowOnboardingTip2()
    {
        if (!SettingsService.ShowOnboardingTips) return;

        var tip2 = new TeachingTip
        {
            Title = "Track what's installed",
            Content = "The Installed page shows everything on your workstation, with update badges when new versions ship.",
            Target = InstalledNavItem,
            PreferredPlacement = TeachingTipPlacementMode.Right,
            CloseButtonContent = "Next",
            IsLightDismissEnabled = false
        };
        tip2.Closed += (s, e) => ShowOnboardingTip3();
        tip2.IsOpen = true;
    }

    private void ShowOnboardingTip3()
    {
        if (!SettingsService.ShowOnboardingTips) return;

        var tip3 = new TeachingTip
        {
            Title = "Made to stay out of your way",
            Content = "Closing the window keeps the app in your system tray. You can turn these tips off in Settings.",
            Target = NavView.FooterMenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag as string == "Settings"),
            PreferredPlacement = TeachingTipPlacementMode.Left,
            CloseButtonContent = "Got it",
            IsLightDismissEnabled = false
        };
        tip3.Closed += (s, e) =>
        {
            SettingsService.OnboardingCompleted = true;
            SettingsService.SaveSettings();
        };
        tip3.IsOpen = true;
    }

    // ── Background tool-update scheduler ─────────────────────────────────

    private void SetupUpdateScheduler()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(30);
        timer.Tick += async (s, e) => await CheckToolUpdatesForBadgeAsync(toastOnNew: false);
        timer.Start();

        _ = RunInitialBadgeCheckAsync();
    }

    private async Task RunInitialBadgeCheckAsync()
    {
        try
        {
            // Give startup a moment, then do the first scan for the nav badge.
            await Task.Delay(8000);
            await CheckToolUpdatesForBadgeAsync(toastOnNew: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private async Task CheckToolUpdatesForBadgeAsync(bool toastOnNew)
    {
        try
        {
            await EnsureCatalogLoadedAsync();

            var dlFolder = DownloadService.DefaultDownloadsFolder;
            var installed = await Task.Run(() =>
            {
                var list = new System.Collections.Generic.List<ToolDefinition>();
                foreach (var tool in Tools)
                {
                    if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded)
                    {
                        list.Add(tool);
                    }
                }
                return list;
            });

            var updates = await new ToolUpdateService().CheckForUpdatesAsync(installed);
            int count = updates.Count;

            UpdateInstalledBadge(count);

            if (count > 0 && count != _lastUpdateBadgeCount)
            {
                var names = string.Join(", ", updates.Take(3).Select(u => u.Name)) +
                            (count > 3 ? $" and {count - 3} more" : "");
                ToastService.Show(
                    $"{count} tool update{(count == 1 ? "" : "s")} available",
                    names);
            }

            _lastUpdateBadgeCount = count;
        }
        catch
        {
            // Background check is best-effort.
        }
    }

    private void UpdateInstalledBadge(int count)
    {
        InstalledNavItem.InfoBadge = count > 0 ? new InfoBadge { Value = count } : null;
    }

    /// <summary>
    /// Inserts an in-app Acrylic SystemBackdropElement (Windows App SDK 2.x)
    /// behind the NavigationView pane content, giving the sidebar its own
    /// surface distinct from the window's Mica Alt backdrop. Skipped when
    /// Windows transparency effects are disabled or high contrast is on —
    /// the pane then shows the window's (auto-fallback) Mica surface.
    /// </summary>
    private void ApplyPaneAcrylicSurface()
    {
        if (!Helpers.SystemBackdropHelper.TransparencyEffectsEnabled) return;

        var paneGrid = FindDescendantGridByName(NavView, "PaneContentGrid");
        if (paneGrid is null) return;
        if (paneGrid.Children.Count > 0 && paneGrid.Children[0] is Microsoft.UI.Xaml.Controls.SystemBackdropElement) return;

        paneGrid.Children.Insert(0, new Microsoft.UI.Xaml.Controls.SystemBackdropElement
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            IsHitTestVisible = false
        });
    }

    private static Grid? FindDescendantGridByName(DependencyObject parent, string name)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Grid grid && grid.Name == name) return grid;
            var result = FindDescendantGridByName(child, name);
            if (result is not null) return result;
        }
        return null;
    }

    private static void RemoveTogglePaneButtonFocusVisual(DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Button btn && (btn.Name == "TogglePaneButton" || btn.Name == "PaneToggleButton"))
            {
                btn.IsTabStop = false;
                btn.FocusVisualPrimaryThickness = new Thickness(0);
                btn.FocusVisualSecondaryThickness = new Thickness(0);
                btn.FocusVisualPrimaryBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                btn.FocusVisualSecondaryBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            }
            else
            {
                RemoveTogglePaneButtonFocusVisual(child);
            }
        }
    }
}
