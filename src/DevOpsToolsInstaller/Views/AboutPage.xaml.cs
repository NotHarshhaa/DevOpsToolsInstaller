using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevOpsToolsInstaller.Views;

public sealed partial class AboutPage : Page
{
    private const string ToolRepoUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller";
    private const string AuthorUrl = "https://github.com/NotHarshhaa";
    private const string IssuesUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller/issues";

    public AboutPage()
    {
        InitializeComponent();
        ToolLogoImage.Source = Services.AppLogoHelper.GetLogoImage();
        AuthorPicture.ProfilePicture = Services.AppLogoHelper.GetAuthorImage();
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
