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
/// WinUI 3 dialog for Project Manifests (devtools.json & devtools.lock.json).
/// Open a repo, click "Install what this project needs", or export devtools.json and verify CI drift.
/// </summary>
public static class ProjectManifestDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot, IReadOnlyList<ToolDefinition> catalog, string? initialPath = null)
    {
        if (xamlRoot == null) return;

        var currentPath = string.IsNullOrWhiteSpace(initialPath)
            ? Path.Combine(Environment.CurrentDirectory, ProjectManifestService.ManifestFileName)
            : initialPath;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Project Manifest & Lockfile (devtools.json)",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        var root = new StackPanel { Spacing = 14, MaxWidth = 680 };

        var introText = new TextBlock
        {
            Text = "devtools.json in a repo pins the exact tools and versions a team needs. New teammates click 'Install what this project needs' to be ready in minutes, while CI verifies zero drift against devtools.lock.json.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(introText);

        // Path selector row
        var pathGrid = new Grid { ColumnSpacing = 8 };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathBox = new TextBox
        {
            Text = currentPath,
            PlaceholderText = "Path to repository or devtools.json..."
        };
        Grid.SetColumn(pathBox, 0);
        pathGrid.Children.Add(pathBox);

        var browseBtn = new Button { Content = "Browse Repo..." };
        Grid.SetColumn(browseBtn, 1);
        pathGrid.Children.Add(browseBtn);
        root.Children.Add(pathGrid);

        // Status banner
        var statusInfo = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Severity = InfoBarSeverity.Informational
        };
        root.Children.Add(statusInfo);

        // Content container
        var planContainer = new StackPanel { Spacing = 10 };
        root.Children.Add(planContainer);

        // Action Buttons Row
        var actionsRow = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        actionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var installBtn = new Button
        {
            Content = "Install what this project needs",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            IsEnabled = false
        };
        Grid.SetColumn(installBtn, 0);
        actionsRow.Children.Add(installBtn);

        var verifyBtn = new Button
        {
            Content = "Verify CI Drift",
            IsEnabled = false
        };
        Grid.SetColumn(verifyBtn, 1);
        actionsRow.Children.Add(verifyBtn);

        var exportBtn = new Button
        {
            Content = "Export Manifest"
        };
        Grid.SetColumn(exportBtn, 2);
        actionsRow.Children.Add(exportBtn);

        root.Children.Add(actionsRow);

        // Progress bar (initially collapsed)
        var progressPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        var pBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0 };
        var pText = new TextBlock { Text = "Preparing...", FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        progressPanel.Children.Add(pBar);
        progressPanel.Children.Add(pText);
        root.Children.Add(progressPanel);

        dialog.Content = root;

        ProjectManifest? loadedManifest = null;
        ManifestPlan? currentPlan = null;

        void RefreshPlan()
        {
            planContainer.Children.Clear();
            var targetPath = pathBox.Text.Trim();
            var (manifest, err) = ProjectManifestService.Load(targetPath);

            if (manifest == null)
            {
                statusInfo.IsOpen = true;
                statusInfo.Severity = InfoBarSeverity.Warning;
                statusInfo.Title = "No Manifest Found";
                statusInfo.Message = err;
                installBtn.IsEnabled = false;
                verifyBtn.IsEnabled = false;
                return;
            }

            loadedManifest = manifest;
            currentPlan = ProjectManifestService.BuildPlan(manifest, catalog, targetPath);

            statusInfo.IsOpen = true;
            statusInfo.Severity = currentPlan.InstallCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
            statusInfo.Title = string.IsNullOrWhiteSpace(manifest.Project) ? "Project Manifest Loaded" : $"Project: {manifest.Project}";
            statusInfo.Message = $"{currentPlan.SatisfiedCount} tool(s) satisfied, {currentPlan.InstallCount} need(s) installation or version alignment.";

            installBtn.IsEnabled = currentPlan.InstallCount > 0;
            installBtn.Content = $"Install what this project needs ({currentPlan.InstallCount})";
            verifyBtn.IsEnabled = true;

            var scroll = new ScrollViewer
            {
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var itemsStack = new StackPanel { Spacing = 6 };
            foreach (var item in currentPlan.Items)
            {
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8)
                };

                var itemGrid = new Grid { ColumnSpacing = 10 };
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var icon = new FontIcon
                {
                    Glyph = item.State == ManifestToolState.Satisfied ? "\uE73E" : "\uE7BA",
                    FontSize = 14,
                    Foreground = item.State == ManifestToolState.Satisfied
                        ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                        : (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 0);
                itemGrid.Children.Add(icon);

                var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                titlePanel.Children.Add(new TextBlock { Text = item.Tool.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                titlePanel.Children.Add(new TextBlock
                {
                    Text = item.Detail,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });
                Grid.SetColumn(titlePanel, 1);
                itemGrid.Children.Add(titlePanel);

                var verBadge = new Border
                {
                    Background = (Brush)Application.Current.Resources["CustomAccentSubtleBrush"],
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                verBadge.Child = new TextBlock
                {
                    Text = $"v{item.RequiredVersion}",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["CustomAccentPrimaryBrush"]
                };
                Grid.SetColumn(verBadge, 2);
                itemGrid.Children.Add(verBadge);

                card.Child = itemGrid;
                itemsStack.Children.Add(card);
            }

            scroll.Content = itemsStack;
            planContainer.Children.Add(scroll);
        }

        pathBox.LostFocus += (s, e) => RefreshPlan();

        browseBtn.Click += async (s, e) =>
        {
            var picker = new FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                pathBox.Text = Path.Combine(folder.Path, ProjectManifestService.ManifestFileName);
                RefreshPlan();
            }
        };

        installBtn.Click += async (s, e) =>
        {
            if (currentPlan == null) return;
            installBtn.IsEnabled = false;
            verifyBtn.IsEnabled = false;
            exportBtn.IsEnabled = false;
            progressPanel.Visibility = Visibility.Visible;

            var prog = new Progress<double>(pct =>
            {
                pBar.Value = pct;
                pText.Text = $"Applying manifest ({pct:F0}%)...";
            });

            var result = await ProjectManifestService.ApplyAsync(currentPlan, false, prog, msg =>
            {
                pText.Text = msg;
            });

            // Write or update lockfile automatically after install
            if (loadedManifest != null)
            {
                ProjectManifestService.WriteLock(pathBox.Text.Trim(), loadedManifest, catalog);
            }

            progressPanel.Visibility = Visibility.Collapsed;
            exportBtn.IsEnabled = true;
            RefreshPlan();

            statusInfo.IsOpen = true;
            statusInfo.Severity = result.AllOK ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            statusInfo.Title = result.AllOK ? "Project Dependencies Ready!" : "Install Completed with Warnings";
            statusInfo.Message = $"{result.Installed} tool(s) installed/aligned. devtools.lock.json written with SHA-256 hashes.";
        };

        verifyBtn.Click += (s, e) =>
        {
            var check = ProjectManifestService.Check(pathBox.Text.Trim(), catalog);
            statusInfo.IsOpen = true;
            statusInfo.Severity = check.AllOK ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            statusInfo.Title = check.AllOK ? "CI Check Passed (Zero Drift)" : "CI Check Detected Drift!";
            statusInfo.Message = string.Join("\n", check.Lines.Take(6));
        };

        exportBtn.Click += (s, e) =>
        {
            try
            {
                var manifest = ProjectManifestService.ExportManifest(catalog);
                var savePath = pathBox.Text.Trim();
                if (Directory.Exists(savePath))
                {
                    savePath = Path.Combine(savePath, ProjectManifestService.ManifestFileName);
                }
                ProjectManifestService.WriteManifest(savePath, manifest);
                ProjectManifestService.WriteLock(savePath, manifest, catalog);

                RefreshPlan();
                statusInfo.IsOpen = true;
                statusInfo.Severity = InfoBarSeverity.Success;
                statusInfo.Title = "Manifest Exported";
                statusInfo.Message = $"Captured {manifest.Tools.Count} tools to {savePath} and generated devtools.lock.json.";
            }
            catch (Exception ex)
            {
                statusInfo.IsOpen = true;
                statusInfo.Severity = InfoBarSeverity.Error;
                statusInfo.Title = "Export Failed";
                statusInfo.Message = ex.Message;
            }
        };

        RefreshPlan();
        await dialog.ShowAsync();
    }
}
