using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class InstalledPage : Page
{
    private List<ToolDefinition> _allInstalledTools = new();
    private DateTime _lastChecked = DateTime.Now;

    public InstalledPage()
    {
        InitializeComponent();
        Loaded += InstalledPage_Loaded;
    }

    private async void InstalledPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanInstalledToolsAsync();
    }

    private async Task ScanInstalledToolsAsync()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        StatusText.Text = "Scanning installed tools…";
        await mw.EnsureCatalogLoadedAsync();

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        // Scan on a background thread (registry & folder check)
        var installedTools = await Task.Run(() =>
        {
            var results = new List<ToolDefinition>();
            foreach (var tool in mw.Tools)
            {
                if (UninstallService.IsInstalled(tool, dlFolder))
                {
                    tool.IsInstalled = true;

                    // If it's an installer, grab the version from the registry if available
                    if (tool.Kind == ArtifactKind.Installer)
                    {
                        var regVer = UninstallService.GetInstalledVersion(tool);
                        if (!string.IsNullOrWhiteSpace(regVer))
                        {
                            tool.DetectedVersion = regVer;
                        }
                    }

                    results.Add(tool);
                }
            }
            return results;
        });

        _allInstalledTools = installedTools;
        _lastChecked = DateTime.Now;

        ApplyFilter();

        var count = _allInstalledTools.Count;
        InstalledCountText.Text = $"{count} deployed";
        PageHeadingText.Text = $"Installed Tools & Updates ({count} total)";

        if (count > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            InstalledList.Visibility = Visibility.Visible;
            StatusText.Text = $"{count} tool(s) detected on your workstation";

            // Asynchronously resolve CLI versions in the background for tools missing version
            _ = Task.Run(async () =>
            {
                foreach (var tool in _allInstalledTools)
                {
                    if (string.IsNullOrWhiteSpace(tool.DetectedVersion))
                    {
                        var probe = await CliHealthService.ProbeToolAsync(tool, timeoutSeconds: 2);
                        if (probe.Success && !string.IsNullOrWhiteSpace(probe.DetectedVersion))
                        {
                            _ = DispatcherQueue.TryEnqueue(() =>
                            {
                                tool.DetectedVersion = probe.DetectedVersion;
                                tool.HealthStatus = "Healthy";
                                tool.HealthOutput = probe.RawOutput;
                            });
                        }
                    }
                }
            });
        }
        else
        {
            EmptyState.Visibility = Visibility.Visible;
            InstalledList.Visibility = Visibility.Collapsed;
            StatusText.Text = "No installed tools found";
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";

        var filtered = _allInstalledTools.AsEnumerable();
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(t =>
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(t.DetectedVersion) && t.DetectedVersion.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var list = filtered.ToList();
        InstalledList.ItemsSource = list;

        StatusText.Text = $"{list.Count} of {_allInstalledTools.Count} tool(s) shown";
        if (FooterStatusText != null)
        {
            FooterStatusText.Text = $"{_allInstalledTools.Count} tools managed. Check for Updates: Last checked {GetLastCheckedText()}.";
        }
    }

    private string GetLastCheckedText()
    {
        var elapsed = DateTime.Now - _lastChecked;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
        return $"{(int)elapsed.TotalHours} hr ago";
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Checking for tool updates…";
        await ScanInstalledToolsAsync();
        StatusText.Text = $"Update check complete for {_allInstalledTools.Count} tools.";
    }

    private async void CheckSingleToolUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        StatusText.Text = $"Checking {tool.Name}…";
        var probe = await CliHealthService.ProbeToolAsync(tool);
        if (probe.Success && !string.IsNullOrWhiteSpace(probe.DetectedVersion))
        {
            tool.DetectedVersion = probe.DetectedVersion;
            tool.HealthStatus = "Healthy";
        }
        StatusText.Text = $"{tool.Name}: {(tool.HasUpdate ? "Update available" : "Up to date")}";
    }

    private void UpdateTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        StatusText.Text = $"Updating {tool.Name}…";

        tool.IsSelected = true;
        if (!mw.DownloadQueue.Contains(tool))
        {
            mw.DownloadQueue.Add(tool);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                await mw.DownloadSvc.DownloadAsync(tool, dlFolder);

                if (tool.Status == ToolStatus.Downloaded)
                {
                    var res = ArtifactService.Perform(tool, dlFolder);
                    _ = mw.DispatcherQueue.TryEnqueue(() =>
                        StatusText.Text = $"{tool.Name}: {res.Message}");
                }
            }
            catch (Exception ex)
            {
                ActivityLogService.Error(tool.Name, $"Update failed: {ex.Message}");
                _ = mw.DispatcherQueue.TryEnqueue(() =>
                    StatusText.Text = $"Update failed for {tool.Name}: {ex.Message}");
            }
        });

        mw.NavigateTo("Downloads");
    }

    private void LaunchCli_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        try
        {
            var (exeName, _) = CliHealthService.GetProbeCommand(tool);
            var exePath = CliHealthService.ResolveExecutablePath(exeName, tool.Id);
            var cmdTarget = !string.IsNullOrEmpty(exePath) ? exePath : exeName;

            var cmd = $"Write-Host '--- {tool.Name} Terminal Session ---' -ForegroundColor Cyan; if (Get-Command '{cmdTarget}' -ErrorAction SilentlyContinue) {{ & '{cmdTarget}' --help }} else {{ Write-Host 'Run {tool.Name} command:' -ForegroundColor Yellow }}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"{cmd}\"",
                UseShellExecute = true
            };

            try
            {
                var wtPsi = new ProcessStartInfo
                {
                    FileName = "wt.exe",
                    Arguments = $"powershell.exe -NoExit -Command \"{cmd}\"",
                    UseShellExecute = true
                };
                Process.Start(wtPsi);
            }
            catch
            {
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not launch CLI: {ex.Message}";
        }
    }

    private void OpenSingleToolFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: ToolDefinition tool }) return;
        var (exeName, _) = CliHealthService.GetProbeCommand(tool);
        var path = CliHealthService.ResolveExecutablePath(exeName, tool.Id);
        var dir = !string.IsNullOrEmpty(path) && File.Exists(path)
            ? Path.GetDirectoryName(path)
            : DownloadService.DefaultDownloadsFolder;

        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = dir, UseShellExecute = true });
        }
    }

    private void CopyToolPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: ToolDefinition tool }) return;
        var (exeName, _) = CliHealthService.GetProbeCommand(tool);
        var path = CliHealthService.ResolveExecutablePath(exeName, tool.Id) ?? exeName;
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(path);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        StatusText.Text = $"Copied path to clipboard: {path}";
    }

    private void OpenDoc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: ToolDefinition tool }) return;
        if (!string.IsNullOrWhiteSpace(tool.Homepage))
        {
            LauncherService.OpenUrl(tool.Homepage);
        }
    }

    private async void TestCli_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        StatusText.Text = $"Testing {tool.Name}…";
        tool.HealthStatus = "Testing…";

        var result = await CliHealthService.ProbeToolAsync(tool);
        tool.HealthStatus = result.Success ? "Healthy" : "Failed";
        tool.HealthOutput = result.RawOutput;

        if (result.Success && !string.IsNullOrWhiteSpace(result.DetectedVersion))
        {
            tool.DetectedVersion = result.DetectedVersion;
        }

        StatusText.Text = $"{tool.Name}: {result.Message}";

        // Show detailed health report dialog
        var reportBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(result.RawOutput) ? "(No output returned)" : result.RawOutput,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            Height = 180
        };

        var contentPanel = new StackPanel { Spacing = 10 };
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"Status: {(result.Success ? "Healthy (Pass)" : "Failed / Unresponsive")}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = result.Success
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"Command: {result.CommandRun}  ({result.ElapsedMilliseconds} ms latency)",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        contentPanel.Children.Add(reportBox);

        var dialog = new ContentDialog
        {
            Title = $"{tool.Name} Health Check",
            Content = contentPanel,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async void RunAllHealthChecks_Click(object sender, RoutedEventArgs e)
    {
        if (_allInstalledTools.Count == 0)
        {
            StatusText.Text = "No installed tools to check.";
            return;
        }

        StatusText.Text = $"Running health checks on {_allInstalledTools.Count} tools…";
        int healthyCount = 0;

        foreach (var tool in _allInstalledTools)
        {
            tool.HealthStatus = "Testing…";
            var result = await CliHealthService.ProbeToolAsync(tool);
            tool.HealthStatus = result.Success ? "Healthy" : "Failed";
            tool.HealthOutput = result.RawOutput;
            if (result.Success)
            {
                healthyCount++;
                if (!string.IsNullOrWhiteSpace(result.DetectedVersion))
                {
                    tool.DetectedVersion = result.DetectedVersion;
                }
            }
        }

        StatusText.Text = $"Health check complete: {healthyCount}/{_allInstalledTools.Count} CLIs healthy.";
    }

    private async void ShellCompletion_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        var installedTools = _allInstalledTools.Count > 0 ? _allInstalledTools : (mw?.Tools.Where(t => t.IsInstalled).ToList() ?? new List<ToolDefinition>());

        await ShowShellCompletionModalAsync(installedTools);
    }

    public async Task ShowShellCompletionModalAsync(IReadOnlyList<ToolDefinition> tools)
    {
        var psSnippet = ShellCompletionService.GeneratePowerShellSnippet(tools);
        var bashSnippet = ShellCompletionService.GenerateBashSnippet(tools);

        var snippetBox = new TextBox
        {
            Text = psSnippet,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            Height = 220
        };

        var statusLabel = new TextBlock
        {
            Text = ShellCompletionService.IsSnippetInProfile()
                ? "Active in your PowerShell profile ($PROFILE)"
                : "Not yet configured in your PowerShell profile",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        var psRadio = new RadioButton { Content = "PowerShell ($PROFILE)", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
        var bashRadio = new RadioButton { Content = "Bash / Zsh (WSL / Git Bash)" };

        var radioPanel = new StackPanel { Orientation = Orientation.Horizontal };
        radioPanel.Children.Add(psRadio);
        radioPanel.Children.Add(bashRadio);

        var saveProfileBtn = new Button
        {
            Content = "Append to $PROFILE",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            CornerRadius = new CornerRadius(4)
        };
        saveProfileBtn.Click += (s, e) =>
        {
            var (success, msg) = ShellCompletionService.ApplyToPowerShellProfile(psSnippet);
            statusLabel.Text = msg;
            if (success)
            {
                saveProfileBtn.IsEnabled = false;
                saveProfileBtn.Content = "Saved to $PROFILE";
            }
        };

        psRadio.Checked += (s, e) =>
        {
            snippetBox.Text = psSnippet;
            saveProfileBtn.Visibility = Visibility.Visible;
        };
        bashRadio.Checked += (s, e) =>
        {
            snippetBox.Text = bashSnippet;
            saveProfileBtn.Visibility = Visibility.Collapsed;
        };

        var copyBtn = new Button
        {
            Content = "Copy to Clipboard",
            CornerRadius = new CornerRadius(4)
        };
        copyBtn.Click += (s, e) =>
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(snippetBox.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            copyBtn.Content = "Copied!";
        };

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actionPanel.Children.Add(copyBtn);
        actionPanel.Children.Add(saveProfileBtn);

        var root = new StackPanel { Spacing = 12, MaxWidth = 540 };
        root.Children.Add(new TextBlock
        {
            Text = "Enable instant tab completion for tools installed on your workstation.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        root.Children.Add(radioPanel);
        root.Children.Add(snippetBox);
        root.Children.Add(statusLabel);
        root.Children.Add(actionPanel);

        var dialog = new ContentDialog
        {
            Title = "Terminal Shell Autocompletion",
            Content = root,
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        var (title, body, primary) = tool.Kind switch
        {
            ArtifactKind.Installer => (
                $"Uninstall {tool.Name}?",
                $"This opens the vendor uninstaller for {tool.Name}.",
                "Uninstall"),
            ArtifactKind.Archive => (
                $"Remove {tool.Name}?",
                $"This deletes the extracted files for {tool.Name} from your Tools folder.",
                "Remove"),
            ArtifactKind.Binary => (
                $"Remove {tool.Name}?",
                $"This deletes {tool.FileName} from Tools\\bin.",
                "Remove"),
            _ => (
                $"Delete {tool.Name}?",
                "This removes the installed files.",
                "Delete")
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = body,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var result = UninstallService.Uninstall(tool, dlFolder, removeDownload: false);
        StatusText.Text = result.Message;

        if (result.Success)
        {
            tool.IsInstalled = false;
            // Re-scan to refresh the list
            await ScanInstalledToolsAsync();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = ScanInstalledToolsAsync();
    }

    private void OpenToolsFolder_Click(object sender, RoutedEventArgs e)
    {
        var toolsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevOpsToolsInstaller", "Tools");
        if (!Directory.Exists(toolsFolder))
            Directory.CreateDirectory(toolsFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = toolsFolder,
            UseShellExecute = true
        });
    }

    private void GoToCatalog_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Catalog");
    }
}
