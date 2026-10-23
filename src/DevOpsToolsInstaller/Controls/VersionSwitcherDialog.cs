using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// Modern WinUI 3 dialog for Side-by-Side CLI versions and version switching (like tfenv/nvm).
/// Keep multiple versions of kubectl, terraform, helm, etc. installed side-by-side and switch active version.
/// </summary>
public static class VersionSwitcherDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot, ToolDefinition tool, IReadOnlyList<ToolDefinition> catalog)
    {
        if (xamlRoot == null || tool == null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"{tool.Name} — Side-by-Side Version Switcher",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        var root = new StackPanel { Spacing = 14, MaxWidth = 620 };

        var introText = new TextBlock
        {
            Text = $"Keep multiple versions of {tool.Name} installed side-by-side on this machine and switch active versions per project (like tfenv or nvm). Active version is instantly linked in Tools\\bin.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(introText);

        // Shadowing inspection
        var shadowingReport = VersionSwitcherService.InspectCliShadowing(tool.Name, tool.FileName);
        if (shadowingReport.IsShadowed)
        {
            var shadowBar = new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = "CLI Shadowing Detected",
                Message = shadowingReport.Advice
            };
            root.Children.Add(shadowBar);
        }

        // Active Version Banner
        var activeBanner = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10)
        };
        var bannerGrid = new Grid();
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bannerLeft = new StackPanel();
        bannerLeft.Children.Add(new TextBlock
        {
            Text = "CURRENT ACTIVE VERSION",
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });
        var activeVerLabel = new TextBlock
        {
            Text = $"v{VersionSwitcherService.GetActiveVersion(tool) ?? tool.Version}",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            Margin = new Thickness(0, 2, 0, 0)
        };
        bannerLeft.Children.Add(activeVerLabel);
        Grid.SetColumn(bannerLeft, 0);
        bannerGrid.Children.Add(bannerLeft);

        var activeBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["CustomAccentSubtleBrush"],
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        activeBadge.Child = new TextBlock
        {
            Text = "Active in PATH",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["CustomAccentPrimaryBrush"]
        };
        Grid.SetColumn(activeBadge, 1);
        bannerGrid.Children.Add(activeBadge);
        activeBanner.Child = bannerGrid;
        root.Children.Add(activeBanner);

        // Status infobar
        var statusInfo = new InfoBar { IsOpen = false, IsClosable = true };
        root.Children.Add(statusInfo);

        // Installed side-by-side versions list
        var listHeader = new TextBlock
        {
            Text = "Installed Side-by-Side Versions:",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 0)
        };
        root.Children.Add(listHeader);

        var versionsContainer = new StackPanel { Spacing = 6 };
        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = versionsContainer
        };
        root.Children.Add(scrollViewer);

        void RefreshVersionsList()
        {
            versionsContainer.Children.Clear();
            var versions = VersionSwitcherService.GetInstalledVersions(tool);
            var curActive = VersionSwitcherService.GetActiveVersion(tool);
            activeVerLabel.Text = $"v{curActive ?? tool.Version}";

            if (versions.Count == 0)
            {
                versionsContainer.Children.Add(new TextBlock
                {
                    Text = "No side-by-side versions recorded yet. Install additional versions below.",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    Margin = new Thickness(4, 8, 4, 8)
                });
                return;
            }

            foreach (var v in versions)
            {
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8)
                };

                var itemGrid = new Grid { ColumnSpacing = 12 };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new FontIcon
                {
                    Glyph = v.IsActive ? "\uE73E" : "\uE7B8",
                    FontSize = 14,
                    Foreground = v.IsActive
                        ? (Brush)Application.Current.Resources["CustomAccentPrimaryBrush"]
                        : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 0);
                itemGrid.Children.Add(icon);

                var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titlePanel.Children.Add(new TextBlock
                {
                    Text = $"Version {v.Version}",
                    FontWeight = v.IsActive ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold
                });
                titlePanel.Children.Add(new TextBlock
                {
                    Text = $"{v.FileSizeFormatted} • Installed {v.InstalledAt:yyyy-MM-dd}",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                Grid.SetColumn(titlePanel, 1);
                itemGrid.Children.Add(titlePanel);

                if (v.IsActive)
                {
                    var activeTag = new Border
                    {
                        Background = (Brush)Application.Current.Resources["CustomAccentSubtleBrush"],
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 2, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    activeTag.Child = new TextBlock
                    {
                        Text = "Active",
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = (Brush)Application.Current.Resources["CustomAccentPrimaryBrush"]
                    };
                    Grid.SetColumn(activeTag, 2);
                    itemGrid.Children.Add(activeTag);
                }
                else
                {
                    var switchBtn = new Button
                    {
                        Content = "Switch to this",
                        FontSize = 11,
                        Padding = new Thickness(10, 4, 10, 4),
                        Tag = v.Version,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    switchBtn.Click += async (s, e) =>
                    {
                        switchBtn.IsEnabled = false;
                        var (ok, msg) = await VersionSwitcherService.SwitchActiveVersionAsync(tool, v.Version, catalog);
                        statusInfo.IsOpen = true;
                        statusInfo.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
                        statusInfo.Title = ok ? "Switched Active Version" : "Switch Failed";
                        statusInfo.Message = msg;
                        RefreshVersionsList();
                    };
                    Grid.SetColumn(switchBtn, 2);
                    itemGrid.Children.Add(switchBtn);
                }

                card.Child = itemGrid;
                versionsContainer.Children.Add(card);
            }
        }

        // Add / Target New Version Row
        var addHeader = new TextBlock
        {
            Text = "Install & Switch to another version:",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0)
        };
        root.Children.Add(addHeader);

        var addGrid = new Grid { ColumnSpacing = 8 };
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var newVerBox = new TextBox
        {
            PlaceholderText = "e.g. 1.29.2 or 1.5.7..."
        };
        Grid.SetColumn(newVerBox, 0);
        addGrid.Children.Add(newVerBox);

        var downloadSwitchBtn = new Button
        {
            Content = "Download & Switch",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        downloadSwitchBtn.Click += async (s, e) =>
        {
            var targetVer = newVerBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetVer)) return;

            downloadSwitchBtn.IsEnabled = false;
            statusInfo.IsOpen = true;
            statusInfo.Severity = InfoBarSeverity.Informational;
            statusInfo.Title = "Downloading version...";
            statusInfo.Message = $"Fetching {tool.Name} v{targetVer} from upstream...";

            var (ok, msg) = await VersionSwitcherService.SwitchActiveVersionAsync(tool, targetVer, catalog);
            statusInfo.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            statusInfo.Title = ok ? "Version Installed & Active" : "Installation Failed";
            statusInfo.Message = msg;
            downloadSwitchBtn.IsEnabled = true;
            newVerBox.Text = string.Empty;
            RefreshVersionsList();
        };
        Grid.SetColumn(downloadSwitchBtn, 1);
        addGrid.Children.Add(downloadSwitchBtn);
        root.Children.Add(addGrid);

        // Project Auto-Switch Action
        var projectBtn = new Button
        {
            Content = "Switch from Project (.terraform-version / devtools.json)...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0)
        };
        projectBtn.Click += async (s, e) =>
        {
            var picker = new FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                var res = await VersionSwitcherService.DetectAndSwitchProjectVersionsAsync(folder.Path, catalog);
                statusInfo.IsOpen = true;
                if (res.Results.Count > 0)
                {
                    statusInfo.Severity = InfoBarSeverity.Success;
                    statusInfo.Title = "Project Versions Aligned";
                    statusInfo.Message = string.Join("\n", res.Results.Select(r => $"{r.ToolId}: {r.Message}"));
                }
                else
                {
                    statusInfo.Severity = InfoBarSeverity.Warning;
                    statusInfo.Title = "No Project Version Files";
                    statusInfo.Message = "No .terraform-version, .kubectl-version, or devtools.json found in folder.";
                }
                RefreshVersionsList();
            }
        };
        root.Children.Add(projectBtn);

        dialog.Content = root;
        RefreshVersionsList();
        await dialog.ShowAsync();
    }
}
