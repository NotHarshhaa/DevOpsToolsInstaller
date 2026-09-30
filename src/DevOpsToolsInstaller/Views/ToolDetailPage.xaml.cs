using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

/// <summary>
/// Full detail view for a single tool, reached from the catalog card with a
/// ConnectedAnimation. Shows the description, current version with known
/// previous versions, the vendor homepage and the SHA-256 checksum, and
/// mirrors the card's Install SplitButton actions. The connected animation
/// is skipped when the Windows "reduce animations" setting is on.
/// </summary>
public sealed partial class ToolDetailPage : Page
{
    public ToolDefinition Tool { get; private set; } = new();

    public ToolDetailPage()
    {
        InitializeComponent();
        Loaded += ToolDetailPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ToolDefinition tool)
        {
            Tool = tool;
        }
        BindTool();
    }

    private void ToolDetailPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Connected animation from the catalog card (forward direction).
        if (AnimationSettingsHelper.AnimationsEnabled)
        {
            ConnectedAnimationService.GetForCurrentView().GetAnimation("toolCard")?.TryStart(HeroCard);
        }
    }

    private void BindTool()
    {
        HeroName.Text = Tool.NameWithVersion;
        HeroCategory.Text = Tool.Category;
        HeroKind.Text = Tool.KindLabel;
        HeroVersion.Text = Tool.DisplayVersionWithV;
        DescriptionText.Text = string.IsNullOrWhiteSpace(Tool.Description)
            ? "No description available for this tool."
            : Tool.Description;
        CurrentVersionText.Text = Tool.DisplayVersionWithV;

        // Logo or glyph
        var logo = ToolLogoService.GetLogo(Tool.LogoUrl);
        if (logo is not null)
        {
            HeroLogo.Source = logo;
            HeroLogo.Visibility = Visibility.Visible;
            HeroGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeroGlyph.Glyph = Tool.IconGlyph;
            HeroGlyph.Visibility = Visibility.Visible;
            HeroLogo.Visibility = Visibility.Collapsed;
        }

        // Favorite state
        FavoriteButton.IsChecked = Tool.IsFavorite;
        HeroFavoriteBadge.Visibility = Tool.IsFavorite ? Visibility.Visible : Visibility.Collapsed;

        // Homepage + SHA-256
        var hasHomepage = !string.IsNullOrWhiteSpace(Tool.Homepage);
        HomepageCard.Visibility = hasHomepage ? Visibility.Visible : Visibility.Collapsed;
        HomepageText.Text = Tool.Homepage;

        var hasSha = !string.IsNullOrWhiteSpace(Tool.Sha256);
        ShaCard.Visibility = hasSha ? Visibility.Visible : Visibility.Collapsed;
        ShaText.Text = Tool.Sha256;

        // Known previous versions as cards inside the expander
        var previous = Tool.PreviousVersions?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct()
            .ToList() ?? new List<string>();

        NoPreviousVersionsCard.Visibility = previous.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = VersionsExpander.Items.Count - 1; i >= 0; i--)
        {
            if (VersionsExpander.Items[i] is FrameworkElement { Tag: "previousVersion" })
            {
                VersionsExpander.Items.RemoveAt(i);
            }
        }

        foreach (var version in previous)
        {
            VersionsExpander.Items.Add(new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Tag = "previousVersion",
                Header = $"v{version.TrimStart('v')}",
                Description = "Previous catalog release — install it via “Choose version…”.",
                IsClickEnabled = false
            });
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Frame?.GoBack();

    private void HomepageLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Tool.Homepage))
        {
            LauncherService.OpenUrl(Tool.Homepage);
        }
    }

    private void CopySha_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(Tool.Sha256);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch
        {
            // Clipboard is a convenience — never throw.
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        Tool.IsFavorite = FavoritesService.Toggle(Tool.Id);
        FavoriteButton.IsChecked = Tool.IsFavorite;
        HeroFavoriteBadge.Visibility = Tool.IsFavorite ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Actions (self-contained; progress lands on the Downloads page) ──

    private void Install_Click(Microsoft.UI.Xaml.Controls.SplitButton sender, Microsoft.UI.Xaml.Controls.SplitButtonClickEventArgs args) => RunTool(installAfter: true);

    private void DownloadOnly_Click(object sender, RoutedEventArgs e) => RunTool(installAfter: false);

    private void RunTool(bool installAfter) => ToolActions.RunInBackground(Tool, installAfter);

    private async void ChooseVersion_Click(object sender, RoutedEventArgs e)
    {
        var tool = Tool;
        if (XamlRoot is null) return;

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select known version…" };
        foreach (var v in tool.AllAvailableVersions)
        {
            combo.Items.Add(v);
        }

        var customBox = new TextBox { PlaceholderText = "Or enter custom version (e.g. 1.8.5)" };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Choose a specific release version of {tool.Name}, or reset to the latest catalog release.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(combo);
        panel.Children.Add(customBox);

        var dialog = new ContentDialog
        {
            Title = $"{tool.Name} Version Selection",
            Content = panel,
            PrimaryButtonText = "Apply Version",
            SecondaryButtonText = tool.IsPreviousVersionSelected ? "Reset to Latest" : "",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
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
                BindTool();
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            tool.SetVersion(tool.Version);
            BindTool();
        }
    }

    private void OpenHomepage_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Tool.Homepage))
        {
            LauncherService.OpenUrl(Tool.Homepage);
        }
    }
}
