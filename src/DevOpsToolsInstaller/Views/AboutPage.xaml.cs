using System;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevOpsToolsInstaller.Views;

public sealed partial class AboutPage : Page
{
    private const string ToolRepoUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller";
    private const string AuthorUrl = "https://github.com/NotHarshhaa";
    private const string IssuesUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller/issues";
    private const string DocsUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller#readme";
    private const string LicenseUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller/blob/main/LICENSE";
    private const string ReleasesUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller/releases";

    public AboutPage()
    {
        InitializeComponent();
        ToolLogoImage.Source = Services.AppLogoHelper.GetLogoImage();
        AuthorPicture.ProfilePicture = Services.AppLogoHelper.GetAuthorImage();
        Loaded += AboutPage_Loaded;
    }

    private void AboutPage_Loaded(object sender, RoutedEventArgs e)
    {
        OsVersionText.Text = GetFriendlyOsVersion();
        ArchitectureText.Text = $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} (64-bit Native)";
        RuntimeVersionText.Text = $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} • WinUI 3";
        
        var isPathConfigured = Services.SettingsService.IsFolderOnUserPath(Services.ArtifactService.BinFolder);
        PathStatusBadgeText.Text = isPathConfigured ? "Configured in User PATH" : "Not yet added to PATH";
        if (isPathConfigured)
        {
            PathStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
            PathStatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
    }

    private static string GetFriendlyOsVersion()
    {
        var v = Environment.OSVersion.Version;
        if (v.Major == 10 && v.Build >= 22000) return $"Windows 11 (Build {v.Build})";
        if (v.Major == 10) return $"Windows 10 (Build {v.Build})";
        return Environment.OSVersion.VersionString;
    }

    private async void ToolGitHub_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(ToolRepoUrl));
    }

    private async void AuthorGitHub_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(AuthorUrl));
    }

    private async void ReportIssue_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(IssuesUrl));
    }

    private async void Documentation_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(DocsUrl));
    }

    private async void License_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(LicenseUrl));
    }

    private async void Releases_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(ReleasesUrl));
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var diagnostics = $"DevOps Tools Installer v{Services.AppUpdaterService.CurrentVersion}\n" +
            $"Operating System: {GetFriendlyOsVersion()} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription})\n" +
            $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n" +
            $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\n" +
            $"Windows App SDK: 1.6 / WinUI 3\n" +
            $"Bin Path: {Services.ArtifactService.BinFolder}\n" +
            $"Downloads Path: {Services.DownloadService.DefaultDownloadsFolder}\n" +
            $"User PATH Configured: {Services.SettingsService.IsFolderOnUserPath(Services.ArtifactService.BinFolder)}";

        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dataPackage.SetText(diagnostics);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

        AboutInfoBar.Severity = InfoBarSeverity.Success;
        AboutInfoBar.Title = "Diagnostics Copied";
        AboutInfoBar.Message = "Workstation environment details copied to clipboard. Ready to paste into GitHub issues.";
        AboutInfoBar.IsOpen = true;
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AboutCheckUpdatesButton.IsEnabled = false;
            AboutCheckRing.IsActive = true;
            AboutCheckRing.Visibility = Visibility.Visible;
            AboutCheckIcon.Visibility = Visibility.Collapsed;
            AboutCheckText.Text = "Checking…";
            AboutInfoBar.IsOpen = false;

            var release = await Services.AppUpdaterService.CheckForUpdatesAsync();
            if (release == null)
            {
                AboutInfoBar.Severity = InfoBarSeverity.Warning;
                AboutInfoBar.Title = "Update Check Failed";
                AboutInfoBar.Message = "Unable to reach GitHub Releases API. Please check your internet connection.";
                AboutInfoBar.IsOpen = true;
                return;
            }

            if (release.IsUpdateAvailable)
            {
                AboutInfoBar.Severity = InfoBarSeverity.Success;
                AboutInfoBar.Title = $"Update {release.TagName} Available";
                AboutInfoBar.Message = string.IsNullOrWhiteSpace(release.Title)
                    ? "A newer version of DevOps Tools Installer is ready to download."
                    : release.Title;
                AboutInfoBar.IsOpen = true;

                await Controls.UpdateDialog.ShowAsync(this.XamlRoot, release);
            }
            else
            {
                AboutInfoBar.Severity = InfoBarSeverity.Informational;
                AboutInfoBar.Title = "Up to Date";
                AboutInfoBar.Message = $"You are running the latest version of DevOps Tools Installer (v{Services.AppUpdaterService.CurrentVersion}).";
                AboutInfoBar.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            AboutInfoBar.Severity = InfoBarSeverity.Error;
            AboutInfoBar.Title = "Error";
            AboutInfoBar.Message = ex.Message;
            AboutInfoBar.IsOpen = true;
        }
        finally
        {
            AboutCheckRing.IsActive = false;
            AboutCheckRing.Visibility = Visibility.Collapsed;
            AboutCheckIcon.Visibility = Visibility.Visible;
            AboutCheckText.Text = "Check for Updates";
            AboutCheckUpdatesButton.IsEnabled = true;
        }
    }
}
