using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class InstalledPage : Page
{
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

        // Scan on a background thread (registry scan can be slow)
        var installedTools = await Task.Run(() =>
        {
            var results = new List<ToolDefinition>();
            foreach (var tool in mw.Tools)
            {
                if (UninstallService.IsInstalled(tool, dlFolder))
                {
                    tool.IsInstalled = true;

                    // If it's an installer, grab the version from the registry
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

        InstalledList.ItemsSource = installedTools;

        var count = installedTools.Count;
        InstalledCountText.Text = $"{count} deployed";

        if (count > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            InstalledList.Visibility = Visibility.Visible;
            StatusText.Text = $"{count} tool(s) detected on your workstation";

            // Asynchronously resolve CLI versions in the background for tools missing version
            _ = Task.Run(async () =>
            {
                foreach (var tool in installedTools)
                {
                    if (string.IsNullOrWhiteSpace(tool.DetectedVersion))
                    {
                        var probe = await CliHealthService.ProbeToolAsync(tool, timeoutSeconds: 2);
                        if (probe.Success && !string.IsNullOrWhiteSpace(probe.DetectedVersion))
                        {
                            tool.DetectedVersion = probe.DetectedVersion;
                            tool.HealthStatus = "Healthy";
                            tool.HealthOutput = probe.RawOutput;
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

    private async void TestCli_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

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
        if (InstalledList.ItemsSource is not List<ToolDefinition> tools || tools.Count == 0)
        {
            StatusText.Text = "No installed tools to check.";
            return;
        }

        StatusText.Text = $"Running health checks on {tools.Count} tools…";
        int healthyCount = 0;

        foreach (var tool in tools)
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

        StatusText.Text = $"Health check complete: {healthyCount}/{tools.Count} CLIs healthy.";
    }

    private async void ShellCompletion_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        var installedTools = (InstalledList.ItemsSource as List<ToolDefinition>) ?? mw?.Tools.Where(t => t.IsInstalled).ToList() ?? new List<ToolDefinition>();

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
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

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
