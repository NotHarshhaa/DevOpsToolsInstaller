using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class HomePage : Page
{
    private string? _updateUrl;

    public HomePage()
    {
        InitializeComponent();
        HeroLogoImage.Source = AppLogoHelper.GetLogoImage();
        Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        try
        {
            await mw.EnsureCatalogLoadedAsync();
        }
        catch
        {
            ToolCountText.Text = "Error";
            ToolCountLabel.Text = "Failed to load catalog";
            return;
        }

        // Update stats
        ToolCountText.Text = mw.Tools.Count.ToString();
        ToolCountLabel.Text = "Tools Available";

        var downloaded = mw.Tools.Count(t => t.Status == Models.ToolStatus.Downloaded);
        DownloadedCountText.Text = downloaded.ToString();
        DownloadedCountLabel.Text = "Downloaded";

        var categories = mw.Tools.Select(t => t.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Count();
        CategoriesCountText.Text = categories.ToString();

        // Check for updates (fire-and-forget, non-blocking)
        _ = CheckForUpdatesAsync();
    }

    private AppReleaseInfo? _discoveredRelease;

    private async Task CheckForUpdatesAsync()
    {
        if (!SettingsService.CheckForUpdatesOnStartup)
            return;

        try
        {
            var release = await AppUpdaterService.CheckForUpdatesAsync();
            if (release is { IsUpdateAvailable: true })
            {
                _discoveredRelease = release;
                _updateUrl = release.HtmlUrl;
                UpdateInfoBar.Title = $"Update Available — {release.TagName}";
                UpdateInfoBar.Message = string.IsNullOrWhiteSpace(release.Title)
                    ? "A newer version of DevOps Tools Installer is available with updates and improvements."
                    : release.Title;
                UpdateInfoBar.IsOpen = true;
            }
        }
        catch
        {
            // Silently ignore — update check is best-effort
        }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_discoveredRelease != null)
        {
            await Controls.UpdateDialog.ShowAsync(this.XamlRoot, _discoveredRelease);
        }
        else if (!string.IsNullOrWhiteSpace(_updateUrl))
        {
            LauncherService.OpenUrl(_updateUrl);
        }
    }

    private void UpdateLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_updateUrl))
            LauncherService.OpenUrl(_updateUrl);
        else if (_discoveredRelease != null)
            LauncherService.OpenUrl(_discoveredRelease.HtmlUrl);
    }

    private void Catalog_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Catalog");

    private void Downloads_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Downloads");

    private async void BrowseAllBundles_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || this.XamlRoot is null) return;

        var selected = await Controls.BundleSelectionDialog.ShowAsync(this.XamlRoot, mw.Bundles, mw.Tools);
        if (selected != null)
        {
            mw.NavigateToCatalogWithBundle(selected.Id);
        }
    }

    private void SelectBundle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string bundleId)
        {
            App.MainWindowInstance?.NavigateToCatalogWithBundle(bundleId);
        }
    }
}
