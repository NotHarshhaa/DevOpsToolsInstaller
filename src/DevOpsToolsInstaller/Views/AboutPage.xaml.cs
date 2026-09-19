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
}
