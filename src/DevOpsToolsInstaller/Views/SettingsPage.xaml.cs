using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        DownloadPathText.Text = dlFolder;
        UpdateStorageInfo(dlFolder);

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
}
