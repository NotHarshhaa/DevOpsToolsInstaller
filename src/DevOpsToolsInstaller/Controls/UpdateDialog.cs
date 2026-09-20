using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// Modern WinUI 3 dialog for displaying release changelogs, downloading application
/// updates with real-time progress, and triggering self-updating restarts.
/// </summary>
public static class UpdateDialog
{
    private static string? _cachedDownloadedPath;
    private static string? _cachedDownloadedVersion;

    public static async Task ShowAsync(XamlRoot xamlRoot, AppReleaseInfo release)
    {
        if (xamlRoot == null) return;

        var panel = new StackPanel
        {
            Spacing = 16,
            MaxWidth = 540
        };

        // ── 1. Version Comparison Banner ──────────────────────────────────
        var versionHeader = new Grid
        {
            ColumnSpacing = 12
        };
        versionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        versionHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logoBorder = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var logoImg = new Image
        {
            Source = AppLogoHelper.GetLogoImage(),
            Width = 36,
            Height = 36,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        logoBorder.Child = logoImg;
        Grid.SetColumn(logoBorder, 0);
        versionHeader.Children.Add(logoBorder);

        var titleStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        var versionBadges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var currentBadge = new Border
        {
            Style = (Style)Application.Current.Resources["BadgeStyle"],
            Padding = new Thickness(7, 2, 7, 2),
            Child = new TextBlock
            {
                Text = $"Current: v{AppUpdaterService.CurrentVersion}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        };
        var arrowIcon = new FontIcon
        {
            Glyph = "\uE72A",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        var newBadge = new Border
        {
            Background = (Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock
            {
                Text = $"Latest: {release.TagName}",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
            }
        };

        versionBadges.Children.Add(currentBadge);
        versionBadges.Children.Add(arrowIcon);
        versionBadges.Children.Add(newBadge);
        titleStack.Children.Add(versionBadges);

        var titleText = new TextBlock
        {
            Text = release.Title,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };
        titleStack.Children.Add(titleText);

        if (release.PublishedAt.HasValue)
        {
            var dateText = new TextBlock
            {
                Text = $"Published on {release.PublishedAt.Value.LocalDateTime:MMMM dd, yyyy}",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            titleStack.Children.Add(dateText);
        }

        Grid.SetColumn(titleStack, 1);
        versionHeader.Children.Add(titleStack);
        panel.Children.Add(versionHeader);

        // ── 2. Asset & Download Details Card ──────────────────────────────
        var assetCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(12, 10, 12, 10)
        };
        var assetGrid = new Grid { ColumnSpacing = 16 };
        assetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        assetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var assetInfoStack = new StackPanel { Spacing = 2 };
        var assetNameText = new TextBlock
        {
            Text = !string.IsNullOrEmpty(release.AssetName) ? release.AssetName : "Portable Executable",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        };
        var sizeMB = release.AssetSizeBytes > 0 ? (release.AssetSizeBytes / (1024.0 * 1024.0)) : 0;
        var assetSubText = new TextBlock
        {
            Text = sizeMB > 0 ? $"Official Release Binary • {sizeMB:F1} MB" : "Official Release Binary",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        assetInfoStack.Children.Add(assetNameText);
        assetInfoStack.Children.Add(assetSubText);
        Grid.SetColumn(assetInfoStack, 0);
        assetGrid.Children.Add(assetInfoStack);

        if (!string.IsNullOrEmpty(release.ExpectedSha256))
        {
            var hashBadge = new Border
            {
                Style = (Style)Application.Current.Resources["BadgeStyle"],
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE72E", FontSize = 10, Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] },
                        new TextBlock { Text = "SHA256 Verified", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                }
            };
            Grid.SetColumn(hashBadge, 1);
            assetGrid.Children.Add(hashBadge);
        }

        assetCard.Child = assetGrid;
        panel.Children.Add(assetCard);

        // ── 3. Release Notes / Changelog ──────────────────────────────────
        var notesLabel = new TextBlock
        {
            Text = "What's New in this Version:",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        };
        panel.Children.Add(notesLabel);

        var notesScroll = new ScrollViewer
        {
            MaxHeight = 180,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var notesBorder = new Border
        {
            Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12)
        };
        var notesText = new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(release.Body)
                ? release.Body
                : "No release notes were provided for this release.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 18,
            Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        };
        notesScroll.Content = notesText;
        notesBorder.Child = notesScroll;
        panel.Children.Add(notesBorder);

        // ── 4. Download Progress Section ──────────────────────────────────
        var progressContainer = new StackPanel
        {
            Spacing = 6,
            Visibility = Visibility.Collapsed
        };

        var progressBar = new ProgressBar
        {
            Value = 0,
            Maximum = 100,
            Height = 6,
            CornerRadius = new CornerRadius(3)
        };
        var progressLabel = new TextBlock
        {
            Text = "Preparing download...",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        progressContainer.Children.Add(progressBar);
        progressContainer.Children.Add(progressLabel);
        panel.Children.Add(progressContainer);

        // Error message text block
        var errorText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        };
        panel.Children.Add(errorText);

        // ── 5. Content Dialog Setup ───────────────────────────────────────
        var isAlreadyDownloaded = _cachedDownloadedVersion == release.Version &&
                                  !string.IsNullOrEmpty(_cachedDownloadedPath) &&
                                  File.Exists(_cachedDownloadedPath);

        var dialog = new ContentDialog
        {
            Title = "Application Update",
            Content = panel,
            PrimaryButtonText = isAlreadyDownloaded ? "Restart & Install Update" : "Download & Update",
            SecondaryButtonText = "View on GitHub",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var cts = new CancellationTokenSource();
        bool isDownloading = false;

        dialog.Closing += (sender, args) =>
        {
            if (isDownloading)
            {
                // If user clicks Cancel during download, cancel HTTP request
                cts.Cancel();
            }
        };

        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            // If already downloaded and ready to apply, restart now!
            if (isAlreadyDownloaded && !string.IsNullOrEmpty(_cachedDownloadedPath))
            {
                AppUpdaterService.ApplyUpdateAndRestart(_cachedDownloadedPath);
                return;
            }

            // Defer closing dialog while downloading
            var deferral = args.GetDeferral();
            try
            {
                isDownloading = true;
                dialog.IsPrimaryButtonEnabled = false;
                dialog.CloseButtonText = "Cancel";
                errorText.Visibility = Visibility.Collapsed;
                progressContainer.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;
                progressLabel.Text = "Connecting to GitHub Releases...";

                var progressReporter = new Progress<AppUpdateProgress>(p =>
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = p.Percent;
                    progressLabel.Text = p.StatusText;
                });

                var downloadedPath = await AppUpdaterService.DownloadUpdateAsync(release, progressReporter, cts.Token);

                _cachedDownloadedPath = downloadedPath;
                _cachedDownloadedVersion = release.Version;
                isAlreadyDownloaded = true;
                isDownloading = false;

                // Download complete - prompt to restart and apply
                progressLabel.Text = "Update downloaded and verified! Click Restart & Install to apply.";
                dialog.PrimaryButtonText = "Restart & Install Update";
                dialog.IsPrimaryButtonEnabled = true;
                dialog.CloseButtonText = "Close";

                args.Cancel = true; // Keep dialog open so user can click Restart & Install
            }
            catch (OperationCanceledException)
            {
                progressContainer.Visibility = Visibility.Collapsed;
                dialog.PrimaryButtonText = "Download & Update";
                dialog.IsPrimaryButtonEnabled = true;
                dialog.CloseButtonText = "Later";
                args.Cancel = true;
            }
            catch (Exception ex)
            {
                progressContainer.Visibility = Visibility.Collapsed;
                errorText.Text = $"Update failed: {ex.Message}";
                errorText.Visibility = Visibility.Visible;
                dialog.PrimaryButtonText = "Retry Download";
                dialog.IsPrimaryButtonEnabled = true;
                dialog.CloseButtonText = "Close";
                args.Cancel = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        dialog.SecondaryButtonClick += (sender, args) =>
        {
            LauncherService.OpenUrl(release.HtmlUrl);
            args.Cancel = true; // Keep dialog visible
        };

        await dialog.ShowAsync();
    }
}
