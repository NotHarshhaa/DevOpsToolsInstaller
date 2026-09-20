using System.Collections.ObjectModel;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        AppTitleBarLogo.Source = AppLogoHelper.GetLogoImage();

        ApplyTheme(SettingsService.Theme);

        RootGrid.ActualThemeChanged += (s, e) =>
        {
            UpdateTitleBarButtonColors();
        };

        // Selecting the first item raises SelectionChanged, which navigates to
        // HomePage. Do NOT also call ContentFrame.Navigate here — that would
        // create a second HomePage instance and race the catalog load.
        NavView.SelectedItem = NavView.MenuItems[0];

        NavView.Loaded += (s, e) =>
        {
            RemoveTogglePaneButtonFocusVisual(NavView);
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
    /// Applies the Mica system backdrop when the OS supports it. On
    /// unsupported systems the call is a no-op and the window uses its
    /// default background.
    /// </summary>
    private void TrySetMicaBackdrop()
    {
        try
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop
                {
                    Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base
                };
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

        var pageType = tag switch
        {
            "Home"      => typeof(HomePage),
            "Catalog"   => typeof(CatalogPage),
            "Downloads" => typeof(DownloadsPage),
            "Installed" => typeof(InstalledPage),
            "Settings"  => typeof(SettingsPage),
            "About"     => typeof(AboutPage),
            _           => typeof(HomePage)
        };

        ContentFrame.Navigate(pageType);
    }

    /// <summary>
    /// Navigate to a page by its sidebar tag. Used by quick-link buttons.
    /// </summary>
    public void NavigateTo(string tag)
    {
        foreach (NavigationViewItem item in NavView.MenuItems)
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }

        foreach (NavigationViewItem item in NavView.FooterMenuItems)
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
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
