using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// WinUI 3 dialog that shows how much disk the app's own folders use
/// (download cache, extracted tools, audit logs) and offers safe, opt-in
/// cleanup of re-downloadable files. Cleanup never touches installed tools
/// or transfers that are currently in flight.
/// </summary>
public static class StorageManagerDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        if (xamlRoot is null) return;

        // ── Captured state (declared up-front so the Refresh/Clean local
        //    functions below can safely reference every control) ──────────
        TextBlock totalValue, downloadsValue, toolsValue, logsValue;
        // Assigned during layout below; the null! initializers satisfy
        // definite-assignment for the lambdas created in between.
        TextBlock artifactsDetail = null!, partialDetail = null!, resultText = null!;
        Button cleanOldButton = null!, cleanAllButton = null!, cleanPartialButton = null!;

        var panel = new StackPanel { Spacing = 14, MaxWidth = 560 };

        // ── 1. Storage overview card ─────────────────────────────────────
        var overviewCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(14, 10, 14, 10)
        };
        var overviewStack = new StackPanel { Spacing = 8 };
        overviewStack.Children.Add(MakeStatLine("\uE9D9", "Total app storage", out totalValue));
        overviewStack.Children.Add(MakeStatLine("\uE838", "Download cache (installers, archives)", out downloadsValue));
        overviewStack.Children.Add(MakeStatLine("\uE8B7", "Extracted tools (Tools folder)", out toolsValue));
        overviewStack.Children.Add(MakeStatLine("\uE7BA", "Audit logs", out logsValue));
        overviewCard.Child = overviewStack;
        panel.Children.Add(overviewCard);

        // ── 2. Reclaimable space section ─────────────────────────────────
        panel.Children.Add(new TextBlock
        {
            Text = "Reclaim space",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });

        // Downloaded artifacts row
        var artifactsCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(14, 10, 14, 10)
        };
        var artifactsGrid = new Grid { ColumnSpacing = 12 };
        artifactsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        artifactsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var artifactsInfo = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        artifactsInfo.Children.Add(new TextBlock
        {
            Text = "Downloaded artifacts",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        artifactsDetail = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
        artifactsInfo.Children.Add(artifactsDetail);
        Grid.SetColumn(artifactsInfo, 0);
        artifactsGrid.Children.Add(artifactsInfo);

        cleanOldButton = MakeConfirmButton("Older than 30 days", "Confirm", () =>
        {
            ShowCleanResult(StorageManagerService.CleanCompletedArtifacts(olderThanDays: 30));
            Refresh();
        });
        cleanAllButton = MakeConfirmButton("Delete all", "Confirm delete", () =>
        {
            ShowCleanResult(StorageManagerService.CleanCompletedArtifacts(olderThanDays: 0));
            Refresh();
        });
        var artifactsButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        artifactsButtons.Children.Add(cleanOldButton);
        artifactsButtons.Children.Add(cleanAllButton);
        Grid.SetColumn(artifactsButtons, 1);
        artifactsGrid.Children.Add(artifactsButtons);
        artifactsCard.Child = artifactsGrid;
        panel.Children.Add(artifactsCard);

        // Interrupted transfers row
        var partialCard = new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(14, 10, 14, 10)
        };
        var partialGrid = new Grid { ColumnSpacing = 12 };
        partialGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        partialGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var partialInfo = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        partialInfo.Children.Add(new TextBlock
        {
            Text = "Interrupted transfers (.partial)",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        partialDetail = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
        partialInfo.Children.Add(partialDetail);
        Grid.SetColumn(partialInfo, 0);
        partialGrid.Children.Add(partialInfo);

        cleanPartialButton = MakeConfirmButton("Delete", "Confirm", () =>
        {
            ShowCleanResult(StorageManagerService.CleanResumeState());
            Refresh();
        });
        Grid.SetColumn(cleanPartialButton, 1);
        partialGrid.Children.Add(cleanPartialButton);
        partialCard.Child = partialGrid;
        panel.Children.Add(partialCard);

        // Result line (updated after each cleanup)
        resultText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(resultText);

        panel.Children.Add(new TextBlock
        {
            Text = "Cleanup only removes cached installer files — installed tools, their extracted binaries, and in-flight transfers are never touched. Deleted artifacts are simply re-downloaded from the vendor if you ever need them again.",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        // ── Local functions (every captured control is declared above) ───
        void Refresh()
        {
            var scan = StorageManagerService.Scan();
            totalValue.Text = StorageManagerService.FormatBytes(scan.TotalBytes);
            downloadsValue.Text = StorageManagerService.FormatBytes(scan.DownloadsBytes);
            toolsValue.Text = StorageManagerService.FormatBytes(scan.ToolsBytes);
            logsValue.Text = StorageManagerService.FormatBytes(scan.LogsBytes);

            var oldestNote = scan.OldestArtifactUtc.HasValue
                ? $" • oldest {scan.OldestArtifactUtc.Value.ToLocalTime():yyyy-MM-dd}"
                : string.Empty;
            artifactsDetail.Text = scan.ArtifactCount > 0
                ? $"{scan.ArtifactCount} file(s) • {StorageManagerService.FormatBytes(scan.ArtifactsBytes)}{oldestNote}"
                : "No cached artifacts — nothing to clean.";
            partialDetail.Text = scan.PartialCount > 0
                ? $"{scan.PartialCount} file(s) • {StorageManagerService.FormatBytes(scan.PartialBytes)} kept for resumable downloads"
                : "No interrupted transfers.";

            if (scan.ProtectedCount > 0)
            {
                partialDetail.Text += $" ({scan.ProtectedCount} in-flight transfer(s) are always skipped)";
            }

            cleanOldButton.IsEnabled = scan.ArtifactCount > 0;
            cleanAllButton.IsEnabled = scan.ArtifactCount > 0;
            cleanPartialButton.IsEnabled = scan.PartialCount > 0;
        }

        void ShowCleanResult(StorageCleanResult result)
        {
            resultText.Text = result.DeletedFiles > 0
                ? $"✓ Removed {result.DeletedFiles} file(s) — {StorageManagerService.FormatBytes(result.FreedBytes)} freed."
                : "Nothing to remove.";
            resultText.Visibility = Visibility.Visible;
        }

        // ── 3. Dialog ────────────────────────────────────────────────────
        var dialog = new ContentDialog
        {
            Title = "Storage & Download Cache",
            Content = panel,
            PrimaryButtonText = "Open Downloads Folder",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        dialog.PrimaryButtonClick += (sender, args) =>
        {
            LauncherService.OpenDownloadsFolder(SettingsService.DownloadsFolder);
            args.Cancel = true; // keep the dialog open
        };

        Refresh();
        await dialog.ShowAsync();
    }

    /// <summary>Builds an "icon | label ..... value" statistics row; the value column is returned via <paramref name="valueText"/>.</summary>
    private static Grid MakeStatLine(string glyph, string label, out TextBlock valueText)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["CustomAccentPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var labelText = new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(labelText, 1);
        grid.Children.Add(labelText);

        valueText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(valueText, 2);
        grid.Children.Add(valueText);

        return grid;
    }

    /// <summary>
    /// A button that asks for a second click ("Confirm") before running a
    /// destructive action — WinUI does not allow nesting a confirmation
    /// ContentDialog inside another dialog. Resets after 4 seconds.
    /// </summary>
    private static Button MakeConfirmButton(string caption, string confirmCaption, Action onConfirmed)
    {
        var button = new Button
        {
            Content = caption,
            Padding = new Thickness(12, 6, 12, 6)
        };

        var armed = false;
        Microsoft.UI.Dispatching.DispatcherQueueTimer? resetTimer = null;

        button.Click += (sender, _) =>
        {
            if (!armed)
            {
                armed = true;
                button.Content = confirmCaption;
                button.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;

                resetTimer = button.DispatcherQueue.CreateTimer();
                resetTimer.Interval = TimeSpan.FromSeconds(4);
                resetTimer.Tick += (s, e) =>
                {
                    armed = false;
                    button.Content = caption;
                    button.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                    resetTimer.Stop();
                };
                resetTimer.Start();
                return;
            }

            resetTimer?.Stop();
            armed = false;
            button.Content = caption;
            button.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            onConfirmed();
        };

        return button;
    }
}
