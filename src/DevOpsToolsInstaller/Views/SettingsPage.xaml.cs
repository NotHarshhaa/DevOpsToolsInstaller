using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        SettingsLogoImage.Source = AppLogoHelper.GetLogoImage();
        SettingsAuthorPicture.ProfilePicture = AppLogoHelper.GetAuthorImage();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        DownloadPathText.Text = dlFolder;
        ToolsPathText.Text = ArtifactService.BinFolder;
        UpdateStorageInfo(dlFolder);
        UpdatePathStatus();
        UpdateCompletionStatus();

        // Set theme selector active value
        var currentTheme = SettingsService.Theme;
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag as string == currentTheme.ToString())
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void UpdateCompletionStatus()
    {
        var inProfile = ShellCompletionService.IsSnippetInProfile();
        CompletionStatusText.Text = inProfile ? "Configured in $PROFILE" : "Not configured";
    }

    private void UpdatePathStatus()
    {
        var onPath = SettingsService.IsFolderOnUserPath(ArtifactService.BinFolder);
        if (onPath)
        {
            PathStatusText.Text = "On User PATH";
            AddToPathButton.IsEnabled = false;
        }
        else
        {
            PathStatusText.Text = "Not on PATH";
            AddToPathButton.IsEnabled = true;
        }
    }

    private void AddToPath_Click(object sender, RoutedEventArgs e)
    {
        var binFolder = ArtifactService.BinFolder;
        var added = SettingsService.AddToUserPath(binFolder);
        UpdatePathStatus();

        NoticeInfoBar.IsOpen = true;
        if (added)
        {
            NoticeInfoBar.Severity = InfoBarSeverity.Success;
            NoticeInfoBar.Title = "PATH Updated";
            NoticeInfoBar.Message = $"{binFolder} was successfully added to your User PATH. Any newly opened terminals can run installed CLI tools directly.";
        }
        else
        {
            NoticeInfoBar.Severity = InfoBarSeverity.Informational;
            NoticeInfoBar.Title = "Already on PATH";
            NoticeInfoBar.Message = $"{binFolder} is already present in your PATH.";
        }
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            picker.FileTypeFilter.Add("*");

            if (App.MainWindowInstance != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                SettingsService.DownloadsFolder = folder.Path;
                SettingsService.SaveSettings();

                DownloadPathText.Text = folder.Path;
                UpdateStorageInfo(folder.Path);

                NoticeInfoBar.IsOpen = true;
                NoticeInfoBar.Severity = InfoBarSeverity.Success;
                NoticeInfoBar.Title = "Downloads Directory Changed";
                NoticeInfoBar.Message = $"Installers will now be saved to: {folder.Path}";
            }
        }
        catch (Exception ex)
        {
            NoticeInfoBar.IsOpen = true;
            NoticeInfoBar.Severity = InfoBarSeverity.Error;
            NoticeInfoBar.Title = "Folder Selection Failed";
            NoticeInfoBar.Message = ex.Message;
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string tag &&
            Enum.TryParse<AppTheme>(tag, out var theme))
        {
            if (SettingsService.Theme != theme)
            {
                SettingsService.Theme = theme;
                SettingsService.SaveSettings();

                // Apply theme dynamically to the MainWindow
                var mw = App.MainWindowInstance;
                if (mw is not null)
                {
                    mw.ApplyTheme(theme);
                }
            }
        }
    }

    private void UpdateStorageInfo(string folder)
    {
        if (!System.IO.Directory.Exists(folder))
        {
            StorageText.Text = "No files downloaded yet.";
            return;
        }

        var files = System.IO.Directory.GetFiles(folder);
        long totalBytes = 0;
        foreach (var f in files)
        {
            try { totalBytes += new System.IO.FileInfo(f).Length; } catch { }
        }

        var sizeMB = totalBytes / (1024.0 * 1024.0);
        StorageText.Text = $"{files.Length} file(s), {sizeMB:F1} MB total";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        LauncherService.OpenDownloadsFolder(DownloadService.DefaultDownloadsFolder);
    }

    private void OpenToolsFolder_Click(object sender, RoutedEventArgs e)
    {
        LauncherService.OpenDownloadsFolder(ArtifactService.BinFolder);
    }

    private async void ClearDownloads_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear Downloads",
            Content = "Delete all downloaded installer files? You can re-download them anytime.",
            PrimaryButtonText = "Delete All",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var folder = DownloadService.DefaultDownloadsFolder;
            var freed = DownloadService.ClearDownloads(folder);
            var freedMB = freed / (1024.0 * 1024.0);
            StorageText.Text = $"Cleared {freedMB:F1} MB";

            // Reset tool statuses
            var mw = App.MainWindowInstance;
            if (mw is not null)
            {
                foreach (var tool in mw.Tools)
                {
                    tool.Status = Models.ToolStatus.NotDownloaded;
                    tool.Progress = 0;
                }
                mw.DownloadQueue.Clear();
            }
        }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        LauncherService.OpenUrl("https://github.com/NotHarshhaa/DevOpsToolsInstaller");
    }

    private void AuthorGitHub_Click(object sender, RoutedEventArgs e)
    {
        LauncherService.OpenUrl("https://github.com/NotHarshhaa");
    }

    private void ViewAboutPage_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("About");
    }

    private void ShowNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        NoticeInfoBar.Message = message;
        NoticeInfoBar.Severity = severity;
        NoticeInfoBar.IsOpen = true;
    }

    private void DesktopShortcut_Click(object sender, RoutedEventArgs e)
    {
        var ok = SettingsService.CreateDesktopShortcut();
        if (ok)
        {
            ShortcutStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
            ShortcutStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            ShortcutStatusText.Text = "Desktop Shortcut Created";
            ShowNotice("Desktop shortcut created with official logo.", InfoBarSeverity.Success);
        }
        else
        {
            ShowNotice("Could not create desktop shortcut.", InfoBarSeverity.Error);
        }
    }

    private void StartMenuShortcut_Click(object sender, RoutedEventArgs e)
    {
        var ok = SettingsService.CreateStartMenuShortcut();
        if (ok)
        {
            ShortcutStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
            ShortcutStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            ShortcutStatusText.Text = "Start Menu Shortcut Created";
            ShowNotice("Start Menu shortcut created with official logo.", InfoBarSeverity.Success);
        }
        else
        {
            ShowNotice("Could not create Start Menu shortcut.", InfoBarSeverity.Error);
        }
    }

    private async void GenerateCompletion_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        var tools = mw?.Tools.Where(t => t.IsInstalled).ToList() ?? new System.Collections.Generic.List<Models.ToolDefinition>();

        var psSnippet = ShellCompletionService.GeneratePowerShellSnippet(tools, includeAllSupported: tools.Count == 0);
        var bashSnippet = ShellCompletionService.GenerateBashSnippet(tools, includeAllSupported: tools.Count == 0);

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
        saveProfileBtn.Click += (s, ev) =>
        {
            var (success, msg) = ShellCompletionService.ApplyToPowerShellProfile(psSnippet);
            statusLabel.Text = msg;
            if (success)
            {
                saveProfileBtn.IsEnabled = false;
                saveProfileBtn.Content = "Saved to $PROFILE";
                UpdateCompletionStatus();
            }
        };

        psRadio.Checked += (s, ev) =>
        {
            snippetBox.Text = psSnippet;
            saveProfileBtn.Visibility = Visibility.Visible;
        };
        bashRadio.Checked += (s, ev) =>
        {
            snippetBox.Text = bashSnippet;
            saveProfileBtn.Visibility = Visibility.Collapsed;
        };

        var copyBtn = new Button { Content = "Copy to Clipboard", CornerRadius = new CornerRadius(4) };
        copyBtn.Click += (s, ev) =>
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
}
