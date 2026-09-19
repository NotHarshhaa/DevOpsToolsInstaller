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
}
