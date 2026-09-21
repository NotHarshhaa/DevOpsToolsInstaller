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
