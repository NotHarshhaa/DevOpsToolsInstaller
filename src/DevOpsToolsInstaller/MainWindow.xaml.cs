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
    /// Set while the sidebar selection is being updated to mirror the frame's
    /// current page, so the change does not trigger a second navigation.
    /// </summary>
    private bool _syncingNavSelection;

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

            var customStacks = CustomStacksService.LoadCustomStacks();
            foreach (var custom in customStacks)
            {
                if (!Bundles.Any(b => string.Equals(b.Id, custom.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    Bundles.Add(custom);
                }
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
        App.MainWindowInstance = this;
        InitializeComponent();
        Title = "DevOps Tools Installer";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Single source of truth for the product version: the assembly version.
        AppTitleBar.Subtitle = $"v{AppUpdaterService.CurrentVersion}";

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

        // Keep the sidebar selection and the last-visited page in sync with
        // whatever the frame actually shows — including navigations that did
        // not originate from the NavigationView (Home cards, the search box,
        // deep links, or the tool-detail sub-page).
        ContentFrame.Navigated += (s, e) =>
        {
            if (e is not Microsoft.UI.Xaml.Navigation.NavigationEventArgs nav) return;
            var tag = nav.Content?.GetType().Name switch
            {
                nameof(HomePage) => "Home",
                nameof(CatalogPage) => "Catalog",
                nameof(ToolDetailPage) => "Catalog",
                nameof(StacksPage) => "Stacks",
                nameof(DownloadsPage) => "Downloads",
                nameof(InstalledPage) => "Installed",
                nameof(DoctorPage) => "Doctor",
                nameof(ProfilesPage) => "Profiles",
                nameof(CatalogEditorPage) => "CatalogEditor",
                nameof(SettingsPage) => "Settings",
                nameof(AboutPage) => "About",
                _ => SettingsService.LastPage
            };

            SettingsService.LastPage = tag;
            SyncNavSelection(tag);
        };

        ContentFrame.NavigationFailed += (s, e) =>
        {
            var details = e.Exception?.ToString() ?? "Unknown error";
            ActivityLogService.Error("Navigation", $"Navigation to {e.SourcePageType?.Name} failed: {details}");
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

        // Restore the last visited page from the previous session directly,
        // avoiding instantiating HomePage and immediately tearing it down.
        var lastPage = SettingsService.LastPage;
        NavigationViewItem? initialItem = null;
        if (!string.IsNullOrEmpty(lastPage))
        {
            var allItems = NavView.MenuItems.OfType<NavigationViewItem>()
                .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>());
            initialItem = allItems.FirstOrDefault(i => (i.Tag as string) == lastPage);
        }
        NavView.SelectedItem = initialItem ?? NavView.MenuItems[0];

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

        // Ensure keyboard accelerator tooltips (such as floating 'Ctrl+K' badge) are suppressed
        RootGrid.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;

        // Shortcut Ctrl+` to toggle collapsible bottom live log drawer
        var logDrawerAccel = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
            Key = (Windows.System.VirtualKey)192 // VK_OEM_3 (Backtick `)
        };
        logDrawerAccel.Invoked += (s, e) =>
        {
            BottomLogDrawer.Toggle(animate: true);
            e.Handled = true;
        };
        RootGrid.KeyboardAccelerators.Add(logDrawerAccel);

        RootGrid.KeyDown += (s, e) =>
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl && ((int)e.Key == 192 || (int)e.OriginalKey == 192))
            {
                BottomLogDrawer.Toggle(animate: true);
                e.Handled = true;
            }
            else if (ctrl && (e.Key == Windows.System.VirtualKey.K || e.OriginalKey == Windows.System.VirtualKey.K))
            {
                _ = ShowCommandPaletteAsync();
                e.Handled = true;
            }
            else if (ctrl && (int)e.Key == 188) // Ctrl + ,  → Settings
            {
                NavigateTo("Settings");
                e.Handled = true;
            }
        };
    }

    private void LogDrawerToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        BottomLogDrawer.Toggle(animate: true);
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
        // Programmatic selection sync (below) must not re-navigate.
        if (_syncingNavSelection) return;

        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;

        NavigateToTag(tag);
    }

    /// <summary>
    /// Highlights the sidebar item that corresponds to the page currently in
    /// the content frame. Keeps the selection truthful for navigations that
    /// bypass the NavigationView (Home cards, search box, deep links).
    /// </summary>
    private void SyncNavSelection(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;

        var allItems = NavView.MenuItems.OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>());

        foreach (var item in allItems)
        {
            if (item.Tag as string != tag) continue;
            if (ReferenceEquals(NavView.SelectedItem, item)) return;

            _syncingNavSelection = true;
            try
            {
                NavView.SelectedItem = item;
            }
            finally
            {
                _syncingNavSelection = false;
            }
            return;
        }
    }

    private void NavigateToTag(string tag) => ContentFrame.Navigate(PageTypeForTag(tag));

    /// <summary>Maps a sidebar tag to the page type it opens.</summary>
    private static Type PageTypeForTag(string tag) => tag switch
    {
        "Home"      => typeof(HomePage),
        "Catalog"   => typeof(CatalogPage),
        "Stacks"    => typeof(StacksPage),
        "Downloads" => typeof(DownloadsPage),
        "Installed" => typeof(InstalledPage),
        "Doctor"    => typeof(DoctorPage),
        "Profiles"  => typeof(ProfilesPage),
        "CatalogEditor" => typeof(CatalogEditorPage),
        "Settings"  => typeof(SettingsPage),
        "About"     => typeof(AboutPage),
        _           => typeof(HomePage)
    };

    /// <summary>
    /// Safety net for the sidebar. <c>SelectionChanged</c> does not fire when
    /// the user re-invokes the item that is already selected, so if the
    /// selection and the displayed page ever disagreed the page would refuse
    /// to open. Here we make sure the frame really shows the invoked item's
    /// page. Items that differ from the current selection are left to
    /// <c>SelectionChanged</c> so no navigation happens twice.
    /// </summary>
    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;
        if (!ReferenceEquals(NavView.SelectedItem, item)) return;
        if (ContentFrame.Content?.GetType() == PageTypeForTag(tag)) return;

        NavigateToTag(tag);
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

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _navSearchDebounceTimer;

    private void NavSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _navSearchDebounceTimer?.Stop();
            _navSearchDebounceTimer = DispatcherQueue.CreateTimer();
            _navSearchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
            _navSearchDebounceTimer.Tick += (s, e) =>
            {
                _navSearchDebounceTimer?.Stop();
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
            };
            _navSearchDebounceTimer.Start();
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

    /// <summary>
    /// Navigates to the Stacks page and switches to Custom Stack Builder mode.
    /// </summary>
    public void NavigateToStackBuilder()
    {
        NavigateTo("Stacks");
        if (ContentFrame.Content is StacksPage sp)
        {
            sp.SelectBuilderMode();
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

    private void FocusSearch_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NavSearchBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private void OpenSettings_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NavigateTo("Settings");
    }

    private void RefreshPage_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (ContentFrame.Content is IRefreshablePage page)
        {
            page.RefreshView();
        }
    }

    private void NavigateAccel_Invoked(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        var tag = sender.Key switch
        {
            Windows.System.VirtualKey.Number1 => "Home",
            Windows.System.VirtualKey.Number2 => "Catalog",
            Windows.System.VirtualKey.Number3 => "Stacks",
            Windows.System.VirtualKey.Number4 => "Installed",
            Windows.System.VirtualKey.Number5 => "Doctor",
            Windows.System.VirtualKey.Number6 => "Profiles",
            Windows.System.VirtualKey.Number7 => "Downloads",
            _ => null
        };

        if (tag is null) return;
        args.Handled = true;
        NavigateTo(tag);
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
            new("Go to Doctor (Diagnostics)", "\uE9D9", () => NavigateTo("Doctor")),
            new("Go to Profiles & History", "\uE77B", () => NavigateTo("Profiles")),
            new("Edit Custom Tools", "\uE70F", () => NavigateTo("CatalogEditor")),
            new("Refresh current page (F5)", "\uE72C", () =>
            {
                if (ContentFrame.Content is IRefreshablePage p) p.RefreshView();
            }),
            new("Open downloads folder", "\uE838", () =>
                LauncherService.OpenDownloadsFolder(DownloadService.DefaultDownloadsFolder)),
            new("Capture workstation snapshot", "\uE7C3", () => NavigateTo("Profiles")),
            new("Toggle Live Logs (Ctrl+`)", "\uE756", () => BottomLogDrawer.Toggle(animate: true)),
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
