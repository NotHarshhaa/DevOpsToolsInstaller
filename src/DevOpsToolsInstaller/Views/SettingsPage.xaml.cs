using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.WinUI.Controls;
using DevOpsToolsInstaller.Controls;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        SettingsLogoImage.Source = AppLogoHelper.GetLogoImage();
        SettingsAuthorPicture.ProfilePicture = AppLogoHelper.GetAuthorImage();
        SettingsVersionText.Text = $"v{AppUpdaterService.CurrentVersion}";
        Loaded += SettingsPage_Loaded;
    }

    private AppReleaseInfo? _discoveredRelease;

    /// <summary>True when the latest check was against the Microsoft Store, so the banner install button routes to the Store flow instead of the GitHub update dialog.</summary>
    private bool _isStoreUpdate;

    // Settings search state: remembers which expanders were open before the
    // first search so clearing the box restores the user's layout.
    private bool _filterActive;
    private readonly Dictionary<SettingsExpander, bool> _expandState = new();

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        DownloadPathText.Text = dlFolder;
        ToolsPathText.Text = ArtifactService.BinFolder;
        UpdateStorageInfo(dlFolder);
        UpdatePathStatus();
        UpdatePathDoctorSummary();
        UpdateCompletionStatus();
        UpdateLastCheckedDisplay();
        AutoUpdateToggle.IsOn = SettingsService.CheckForUpdatesOnStartup;
        NotificationsToggle.IsOn = SettingsService.EnableNotifications;
        CloseToTrayToggle.IsOn = SettingsService.CloseToTray;
        OnboardingToggle.IsOn = SettingsService.ShowOnboardingTips || !SettingsService.OnboardingCompleted;

        // Hooks & automation section
        HooksPathText.Text = HookService.StorageFolder;
        HooksToggle.IsOn = SettingsService.HooksEnabled;
        UpdateHooksWarning();

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

        // Window material selector
        foreach (ComboBoxItem item in MaterialComboBox.Items)
        {
            if (item.Tag as string == SettingsService.WindowMaterial.ToString())
            {
                MaterialComboBox.SelectedItem = item;
                break;
            }
        }

        // Transparency-off / high-contrast fallback: the window is drawn with an
        // opaque surface, so explain why the material choice has no visible
        // effect and disable the selector.
        if (!SystemBackdropHelper.TransparencyEffectsEnabled)
        {
            MaterialCard.Description =
                "Windows transparency effects are off (or a high-contrast theme is active), so the window always uses a solid, opaque surface. Turn on Transparency effects in Windows Settings \u2192 Accessibility \u2192 Visual effects to enable Mica or Acrylic.";
            MaterialComboBox.IsEnabled = false;
        }

        // Security section
        AuditLogPathText.Text = ActivityLogService.LogDirectory;
        foreach (ComboBoxItem item in SignaturePolicyComboBox.Items)
        {
            if (item.Tag as string == SettingsService.SignaturePolicy.ToString())
            {
                SignaturePolicyComboBox.SelectedItem = item;
                break;
            }
        }
        UpdateSignaturePolicyWarning();

        // Network section
        ProxyToggle.IsOn = SettingsService.ProxyEnabled;
        ProxyFieldsPanel.Visibility = SettingsService.ProxyEnabled ? Visibility.Visible : Visibility.Collapsed;
        ProxyUrlBox.Text = SettingsService.ProxyUrl;
        ProxyUsernameBox.Text = SettingsService.ProxyUsername;
        ProxyPasswordBox.Password = SettingsService.ProxyPassword;

        foreach (ComboBoxItem item in BandwidthLimitComboBox.Items)
        {
            if (item.Tag as string == SettingsService.BandwidthLimitMBps.ToString())
            {
                BandwidthLimitComboBox.SelectedItem = item;
                break;
            }
        }
        if (BandwidthLimitComboBox.SelectedItem is null)
        {
            BandwidthLimitComboBox.SelectedIndex = 0;
        }

        // Off-peak scheduler & retries
        ScheduleToggle.IsOn = SettingsService.DownloadScheduleEnabled;
        PopulateScheduleHourComboBoxes();
        AutoRetryToggle.IsOn = SettingsService.AutoRetryFailedDownloads;
        foreach (ComboBoxItem item in AutoRetryIntervalComboBox.Items)
        {
            if (item.Tag as string == SettingsService.AutoRetryIntervalMinutes.ToString())
            {
                AutoRetryIntervalComboBox.SelectedItem = item;
                break;
            }
        }
        if (AutoRetryIntervalComboBox.SelectedItem is null)
        {
            AutoRetryIntervalComboBox.SelectedIndex = 1;
        }

        // Custom catalog source
        CustomCatalogSourceBox.Text = SettingsService.CustomCatalogSource;

        RefreshSummaries();
        ApplySettingsFilter(string.Empty);
    }

    private void PopulateScheduleHourComboBoxes()
    {
        ScheduleStartComboBox.Items.Clear();
        ScheduleEndComboBox.Items.Clear();
        for (int h = 0; h < 24; h++)
        {
            var text = DateTime.Today.AddHours(h).ToString("h:00 tt");
            var itemStart = new ComboBoxItem { Content = text, Tag = h };
            var itemEnd = new ComboBoxItem { Content = text, Tag = h };
            ScheduleStartComboBox.Items.Add(itemStart);
            ScheduleEndComboBox.Items.Add(itemEnd);
            if (h == SettingsService.ScheduleStartHour) ScheduleStartComboBox.SelectedItem = itemStart;
            if (h == SettingsService.ScheduleEndHour) ScheduleEndComboBox.SelectedItem = itemEnd;
        }
    }

    private void BandwidthLimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BandwidthLimitComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var limit) &&
            SettingsService.BandwidthLimitMBps != limit)
        {
            SettingsService.BandwidthLimitMBps = limit;
            SettingsService.SaveSettings();
            ShowNotice(
                limit > 0
                    ? $"Download speed limit set to {limit} MB/s."
                    : "Download bandwidth limit disabled (unlimited).",
                InfoBarSeverity.Success);
        }
    }

    private void CustomCatalogSource_LostFocus(object sender, RoutedEventArgs e)
    {
        SettingsService.CustomCatalogSource = CustomCatalogSourceBox.Text.Trim();
        SettingsService.SaveSettings();
    }

    private async void ApplyCustomCatalog_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.CustomCatalogSource = CustomCatalogSourceBox.Text.Trim();
        SettingsService.SaveSettings();

        var mw = App.MainWindowInstance;
        if (mw is null) return;

        try
        {
            ShowNotice("Reloading tool catalog...", InfoBarSeverity.Informational);
            var tools = await mw.CatalogSvc.LoadCatalogAsync();
            mw.Tools.Clear();
            foreach (var t in tools)
            {
                t.IsFavorite = FavoritesService.IsFavorite(t.Id);
                mw.Tools.Add(t);
            }
            ShowNotice($"Catalog successfully updated! {mw.Tools.Count} tools loaded.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotice($"Failed to reload catalog: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void ProxyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.ProxyEnabled = ProxyToggle.IsOn;
        ProxyFieldsPanel.Visibility = ProxyToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        SettingsService.SaveSettings();
        ShowNotice(
            ProxyToggle.IsOn
                ? "Proxy enabled — it will be used for downloads and update checks after the app restarts."
                : "Proxy disabled — the Windows system proxy will be used.",
            InfoBarSeverity.Success);
        RefreshSummaries();
    }

    private void ProxyField_LostFocus(object sender, RoutedEventArgs e)
    {
        SettingsService.ProxyUrl = ProxyUrlBox.Text.Trim();
        SettingsService.ProxyUsername = ProxyUsernameBox.Text.Trim();
        SettingsService.ProxyPassword = ProxyPasswordBox.Password;
        SettingsService.SaveSettings();
        RefreshSummaries();
    }

    private void SignaturePolicyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SignaturePolicyComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string tag &&
            Enum.TryParse<SignaturePolicy>(tag, out var policy) &&
            SettingsService.SignaturePolicy != policy)
        {
            SettingsService.SignaturePolicy = policy;
            SettingsService.SaveSettings();
            ShowNotice(
                policy == SignaturePolicy.BlockUnsigned
                    ? "Signature policy updated: installers without a verified trusted signature will be blocked."
                    : "Signature policy updated: you will be warned before launching unsigned installers.",
                InfoBarSeverity.Success);
            UpdateSignaturePolicyWarning();
            RefreshSummaries();
        }
    }

    private void UpdateSignaturePolicyWarning()
    {
        // While "Warn" is selected, surface a persistent warning that
        // unsigned installers are still launchable after confirmation.
        SignatureWarningInfoBar.Visibility = SettingsService.SignaturePolicy == SignaturePolicy.Warn
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OpenAuditLogFolder_Click(object sender, RoutedEventArgs e)
    {
        LauncherService.OpenDownloadsFolder(ActivityLogService.LogDirectory);
    }

    private void UpdateCompletionStatus()
    {
        var inProfile = ShellCompletionService.IsSnippetInProfile();
        CompletionStatusText.Text = inProfile ? "Configured in $PROFILE" : "Not configured";
        if (inProfile)
        {
            CompletionStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
            CompletionStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
        else
        {
            CompletionStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            CompletionStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
    }

    private void UpdatePathStatus()
    {
        var onPath = SettingsService.IsFolderOnUserPath(ArtifactService.BinFolder);
        if (onPath)
        {
            PathStatusText.Text = "On User PATH";
            PathStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
            PathStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            AddToPathButton.IsEnabled = false;
        }
        else
        {
            PathStatusText.Text = "Not on PATH";
            PathStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            PathStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            AddToPathButton.IsEnabled = true;
        }
    }

    private void CopyDownloadsPath_Click(object sender, RoutedEventArgs e)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(DownloadPathText.Text ?? DownloadService.DefaultDownloadsFolder);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        ShowNotice("Downloads folder path copied to clipboard.", InfoBarSeverity.Informational);
    }

    private void CopyToolsPath_Click(object sender, RoutedEventArgs e)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(ToolsPathText.Text ?? ArtifactService.BinFolder);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        ShowNotice("Tools bin folder path copied to clipboard.", InfoBarSeverity.Informational);
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
        UpdatePathDoctorSummary();
    }

    private void ScheduleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.DownloadScheduleEnabled = ScheduleToggle.IsOn;
        SettingsService.SaveSettings();
        ShowNotice(
            SettingsService.DownloadScheduleEnabled
                ? $"Off-peak download scheduler enabled ({SettingsService.GetScheduleWindowDescription()})."
                : "Off-peak download scheduler disabled. Downloads will run immediately.",
            InfoBarSeverity.Success);
        RefreshSummaries();
    }

    private void ScheduleTime_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScheduleStartComboBox.SelectedItem is ComboBoxItem startItem && startItem.Tag is int startHour)
        {
            SettingsService.ScheduleStartHour = startHour;
        }
        if (ScheduleEndComboBox.SelectedItem is ComboBoxItem endItem && endItem.Tag is int endHour)
        {
            SettingsService.ScheduleEndHour = endHour;
        }
        SettingsService.SaveSettings();
    }

    private void AutoRetryToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.AutoRetryFailedDownloads = AutoRetryToggle.IsOn;
        SettingsService.SaveSettings();
    }

    private void AutoRetryInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AutoRetryIntervalComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var mins))
        {
            SettingsService.AutoRetryIntervalMinutes = mins;
            SettingsService.SaveSettings();
        }
    }

    private void UpdatePathDoctorSummary()
    {
        var doc = PathDoctorService.Diagnose();
        PathDoctorSummaryText.Text = doc.Summary;
        if (!doc.IsHealthy)
        {
            var details = new List<string>();
            if (doc.DuplicateEntries.Count > 0)
                details.Add($"Duplicates: {string.Join(", ", doc.DuplicateEntries.Take(2))}{(doc.DuplicateEntries.Count > 2 ? "..." : "")}");
            if (doc.MissingDirectories.Count > 0)
                details.Add($"Broken: {string.Join(", ", doc.MissingDirectories.Take(2))}{(doc.MissingDirectories.Count > 2 ? "..." : "")}");
            PathDoctorDetailsText.Text = string.Join(" • ", details);
            PathDoctorDetailsText.Visibility = Visibility.Visible;
            FixPathButton.IsEnabled = true;
        }
        else
        {
            PathDoctorDetailsText.Visibility = Visibility.Collapsed;
            FixPathButton.IsEnabled = false;
        }
    }

    private async void DiagnosePath_Click(object sender, RoutedEventArgs e)
    {
        var doc = PathDoctorService.Diagnose();
        UpdatePathDoctorSummary();

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "PATH Environment Doctor Report",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        var sp = new StackPanel { Spacing = 12, MaxWidth = 520 };
        sp.Children.Add(new TextBlock
        {
            Text = doc.Summary,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        if (doc.Diagnostics.Count > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "Detected Issues:",
                Style = (Style)Application.Current.Resources["BaseTextBlockStyle"]
            });

            var issueList = new ListView { MaxHeight = 220, SelectionMode = ListViewSelectionMode.None };
            issueList.ItemsSource = doc.Diagnostics.Select(d => $"[{d.IssueType}] {d.Entry} - {d.Message}").ToList();
            sp.Children.Add(issueList);
        }

        sp.Children.Add(new TextBlock
        {
            Text = $"Total entries: {doc.TotalEntries} • Unique valid entries: {doc.CleanedEntries.Count}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        dialog.Content = sp;
        await dialog.ShowAsync();
    }

    private void FixPath_Click(object sender, RoutedEventArgs e)
    {
        var res = PathDoctorService.FixUserPath();
        UpdatePathStatus();
        UpdatePathDoctorSummary();

        if (res.Success)
        {
            ShowNotice(
                $"{res.Message} Removed {res.RemovedDuplicatesCount} duplicate(s) and {res.RemovedMissingCount} broken path(s).",
                InfoBarSeverity.Success);
        }
        else
        {
            ShowNotice(res.Message, InfoBarSeverity.Error);
        }
    }

    private async void RestorePathBackup_Click(object sender, RoutedEventArgs e)
    {
        var backups = PathDoctorService.GetBackups();
        if (backups.Count == 0)
        {
            ShowNotice("No previous PATH backups found to restore.", InfoBarSeverity.Informational);
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = this.XamlRoot,
            Title = "Restore PATH Backup",
            PrimaryButtonText = "Restore Selected",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var sp = new StackPanel { Spacing = 10, MaxWidth = 480 };
        sp.Children.Add(new TextBlock
        {
            Text = "Select a timestamped PATH backup created prior to an optimization:",
            TextWrapping = TextWrapping.Wrap
        });

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var b in backups)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = $"{b.Timestamp:yyyy-MM-dd HH:mm:ss} ({b.EntryCount} entries) - {b.FileName}",
                Tag = b.FilePath
            });
        }
        combo.SelectedIndex = 0;
        sp.Children.Add(combo);

        dialog.Content = sp;
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary &&
            combo.SelectedItem is ComboBoxItem selItem &&
            selItem.Tag is string backupPath)
        {
            if (PathDoctorService.RestoreBackup(backupPath))
            {
                UpdatePathStatus();
                UpdatePathDoctorSummary();
                ShowNotice($"Successfully restored User PATH from {Path.GetFileName(backupPath)}.", InfoBarSeverity.Success);
            }
            else
            {
                ShowNotice("Failed to restore PATH backup.", InfoBarSeverity.Error);
            }
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
            RefreshSummaries();
        }
    }

    private void MaterialComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MaterialComboBox.SelectedItem is ComboBoxItem selectedItem &&
            selectedItem.Tag is string tag &&
            Enum.TryParse<WindowMaterial>(tag, out var material))
        {
            if (SettingsService.WindowMaterial != material)
            {
                SettingsService.WindowMaterial = material;
                SettingsService.SaveSettings();

                // Apply the material live (window backdrop + sidebar surface).
                App.MainWindowInstance?.ApplyWindowMaterial(material);
            }
            RefreshSummaries();
        }
    }

    private void UpdateStorageInfo(string folder)
    {
        if (!System.IO.Directory.Exists(folder))
        {
            StorageText.Text = "No files downloaded yet.";
            StorageSummaryText.Text = "No downloads yet";
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
        StorageSummaryText.Text = $"{files.Length} file(s) · {sizeMB:F1} MB";
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

    private void UpdateLastCheckedDisplay()
    {
        if (SettingsService.LastUpdateCheckTime.HasValue)
        {
            var dt = SettingsService.LastUpdateCheckTime.Value;
            var isToday = dt.Date == DateTime.Today;
            var timeStr = isToday ? $"Today at {dt:hh:mm tt}" : dt.ToString("MMM dd, yyyy 'at' hh:mm tt");
            UpdateLastCheckedText.Text = $"Last checked: {timeStr}";
        }
        else
        {
            UpdateLastCheckedText.Text = "Last checked: Never";
        }
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckForUpdatesButton.IsEnabled = false;
            UpdateCheckRing.IsActive = true;
            UpdateCheckRing.Visibility = Visibility.Visible;
            UpdateStatusBadgeText.Text = "Checking…";
            UpdateStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            UpdateStatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

            // Store installs have package identity: the Store is the only valid
            // update channel and GitHub Releases must never be queried there
            // (releases carry .exe payloads for portable/Setup installs only).
            if (AppUpdaterService.IsRunningAsPackagedApp)
            {
                await CheckStoreForUpdatesAsync();
                return;
            }

            var release = await AppUpdaterService.CheckForUpdatesAsync();
            UpdateLastCheckedDisplay();

            if (release == null)
            {
                UpdateStatusBadgeText.Text = "Check Failed";
                ShowNotice("Unable to reach GitHub Releases. Please check your internet connection.", InfoBarSeverity.Warning);
                return;
            }

            _discoveredRelease = release;

            if (release.IsUpdateAvailable)
            {
                UpdateStatusBadgeText.Text = $"Update {release.TagName} Available";
                UpdateStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
                UpdateStatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

                UpdateAvailableTitle.Text = $"Update Available — {release.TagName}";
                UpdateAvailableSubtitle.Text = string.IsNullOrWhiteSpace(release.Title)
                    ? $"A new version of DevOps Tools Installer is ready to download and install."
                    : release.Title;
                UpdateAvailableBanner.Visibility = Visibility.Visible;

                // Prompt user with UpdateDialog modal
                await UpdateDialog.ShowAsync(this.XamlRoot, release);
            }
            else
            {
                UpdateAvailableBanner.Visibility = Visibility.Collapsed;
                UpdateStatusBadgeText.Text = $"Up to date (v{AppUpdaterService.CurrentVersion})";
                UpdateStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
                UpdateStatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                ShowNotice($"You are running the latest version of DevOps Tools Installer (v{AppUpdaterService.CurrentVersion}).", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            UpdateStatusBadgeText.Text = "Error";
            ShowNotice($"Failed to check for updates: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            UpdateCheckRing.IsActive = false;
            UpdateCheckRing.Visibility = Visibility.Collapsed;
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Update check for Microsoft Store installations: queries the Store (not
    /// GitHub) and, with an update pending, routes the banner's install button
    /// to the Store flow instead of the GitHub download dialog.
    /// </summary>
    private async Task CheckStoreForUpdatesAsync()
    {
        var store = await StoreUpdateService.CheckAsync();
        UpdateLastCheckedDisplay();

        if (store.IsUpdateAvailable)
        {
            _discoveredRelease = null;
            _isStoreUpdate = true;
            UpdateStatusBadgeText.Text = "Store Update Available";
            UpdateStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
            UpdateStatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

            UpdateAvailableTitle.Text = "Update Available — Microsoft Store";
            UpdateAvailableSubtitle.Text = $"Current version: v{store.CurrentVersion}. A newer build is available in the Microsoft Store — install it directly, or open the Store to update.";
            UpdateBannerInstallText.Text = "Update via Store";
            UpdateAvailableBanner.Visibility = Visibility.Visible;
        }
        else
        {
            _isStoreUpdate = false;
            UpdateAvailableBanner.Visibility = Visibility.Collapsed;
            UpdateStatusBadgeText.Text = $"Up to date (v{AppUpdaterService.CurrentVersion})";
            UpdateStatusBadge.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
            UpdateStatusBadgeText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            ShowNotice("Updates for this installation are managed by the Microsoft Store. You are on the latest version the Store has.", InfoBarSeverity.Success);
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        // Store installs: the banner button runs the Store's own install prompt
        // (or falls back to opening the product page there).
        if (_isStoreUpdate)
        {
            var ok = await StoreUpdateService.InstallNowAsync();
            if (!ok)
            {
                await StoreUpdateService.OpenStorePageAsync();
            }
            return;
        }

        if (_discoveredRelease != null)
        {
            await UpdateDialog.ShowAsync(this.XamlRoot, _discoveredRelease);
        }
        else
        {
            CheckForUpdates_Click(sender, e);
        }
    }

    private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.CheckForUpdatesOnStartup = AutoUpdateToggle.IsOn;
        SettingsService.SaveSettings();
    }

    private void NotificationsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.EnableNotifications = NotificationsToggle.IsOn;
        SettingsService.SaveSettings();
        RefreshSummaries();
    }

    private void CloseToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.CloseToTray = CloseToTrayToggle.IsOn;
        SettingsService.SaveSettings();
        RefreshSummaries();
    }

    private void OnboardingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.ShowOnboardingTips = OnboardingToggle.IsOn;
        if (!OnboardingToggle.IsOn)
        {
            SettingsService.OnboardingCompleted = true;
        }
        SettingsService.SaveSettings();
    }

    private void HooksToggle_Toggled(object sender, RoutedEventArgs e)
    {
        SettingsService.HooksEnabled = HooksToggle.IsOn;
        SettingsService.SaveSettings();
        UpdateHooksWarning();
        ShowNotice(HooksToggle.IsOn
            ? "Hooks enabled — your pre/post-install commands will run during installs."
            : "Hooks disabled — no pre/post-install commands will run.",
            HooksToggle.IsOn ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        RefreshSummaries();
    }

    private void UpdateHooksWarning()
    {
        HooksWarningInfoBar.Visibility = SettingsService.HooksEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OpenHooksFolder_Click(object sender, RoutedEventArgs e)
    {
        LauncherService.OpenFolder(HookService.StorageFolder);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static void SelectComboItemByTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (item.Tag as string == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private static string OnOff(bool value) => value ? "On" : "Off";

    private static string PolicyShortLabel(SignaturePolicy policy)
        => policy == SignaturePolicy.BlockUnsigned ? "Block" : "Warn";

    private static string SelectedComboText(ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—";

    // ─────────────────────── Current-value summaries ───────────────────────

    /// <summary>Refreshes every "current value on the right" readout.</summary>
    private void RefreshSummaries()
    {
        UpdateSectionSummaries();
        UpdateSecurityPosture();
    }

    private void UpdateSectionSummaries()
    {
        AppearanceSummaryText.Text =
            $"Theme: {SelectedComboText(ThemeComboBox)} · Material: {SelectedComboText(MaterialComboBox)}";
        SecuritySummaryText.Text = $"Policy: {PolicyShortLabel(SettingsService.SignaturePolicy)}";
        NotificationsSummaryText.Text =
            $"Toasts: {OnOff(NotificationsToggle.IsOn)} · Tray: {OnOff(CloseToTrayToggle.IsOn)}";
        HooksSummaryText.Text = $"Hooks: {OnOff(HooksToggle.IsOn)}";
        NetworkSummaryText.Text = $"Proxy: {OnOff(ProxyToggle.IsOn)}";
        SchedulingSummaryText.Text = $"Schedule: {OnOff(ScheduleToggle.IsOn)}";
    }

    private void UpdateSecurityPosture()
    {
        var policy = SettingsService.SignaturePolicy;
        PosturePolicyText.Text = policy == SignaturePolicy.BlockUnsigned
            ? "Block unsigned installers"
            : "Warn before launch";

        PostureHooksText.Text = SettingsService.HooksEnabled
            ? "Enabled — commands run on install"
            : "Disabled";

        PostureProxyText.Text = SettingsService.ProxyEnabled
            ? (string.IsNullOrWhiteSpace(SettingsService.ProxyUrl)
                ? "Enabled (no address set)"
                : $"Enabled · {SettingsService.ProxyUrl}")
            : "Off — Windows system proxy";

        PostureSummaryText.Text =
            $"Policy: {PolicyShortLabel(policy)} · Hooks: {OnOff(SettingsService.HooksEnabled)} · Proxy: {OnOff(SettingsService.ProxyEnabled)}";
    }

    // ───────────────────────────── Actions ─────────────────────────────

    /// <summary>One-click escalation from the unsigned-installer warning.</summary>
    private void SwitchToBlock_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.SignaturePolicy = SignaturePolicy.BlockUnsigned;
        SettingsService.SaveSettings();
        SelectComboItemByTag(SignaturePolicyComboBox, SignaturePolicy.BlockUnsigned.ToString());
        UpdateSignaturePolicyWarning();
        RefreshSummaries();
        ShowNotice(
            "Signature policy set to Block — installers without a verified trusted signature will be refused.",
            InfoBarSeverity.Success);
    }

    /// <summary>Restores the defaults for a single section (Tag on the invoking button).</summary>
    private void ResetSection_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string ?? string.Empty;
        string label;

        switch (tag)
        {
            case "appearance":
                SettingsService.Theme = AppTheme.Default;
                SettingsService.WindowMaterial = WindowMaterial.Mica;
                SelectComboItemByTag(ThemeComboBox, AppTheme.Default.ToString());
                SelectComboItemByTag(MaterialComboBox, WindowMaterial.Mica.ToString());
                App.MainWindowInstance?.ApplyTheme(AppTheme.Default);
                App.MainWindowInstance?.ApplyWindowMaterial(WindowMaterial.Mica);
                label = "Appearance";
                break;

            case "security":
                SettingsService.SignaturePolicy = SignaturePolicy.Warn;
                SelectComboItemByTag(SignaturePolicyComboBox, SignaturePolicy.Warn.ToString());
                UpdateSignaturePolicyWarning();
                label = "Security";
                break;

            case "notifications":
                SettingsService.EnableNotifications = true;
                SettingsService.CloseToTray = true;
                SettingsService.ShowOnboardingTips = true;
                SettingsService.OnboardingCompleted = false;
                NotificationsToggle.IsOn = true;
                CloseToTrayToggle.IsOn = true;
                OnboardingToggle.IsOn = true;
                label = "Notifications & tray";
                break;

            case "hooks":
                SettingsService.HooksEnabled = false;
                HooksToggle.IsOn = false;
                UpdateHooksWarning();
                label = "Hooks & automation";
                break;

            case "network":
                SettingsService.ProxyEnabled = false;
                SettingsService.ProxyUrl = string.Empty;
                SettingsService.ProxyUsername = string.Empty;
                SettingsService.ProxyPassword = string.Empty;
                SettingsService.BandwidthLimitMBps = 0;
                ProxyToggle.IsOn = false;
                ProxyFieldsPanel.Visibility = Visibility.Collapsed;
                ProxyUrlBox.Text = string.Empty;
                ProxyUsernameBox.Text = string.Empty;
                ProxyPasswordBox.Password = string.Empty;
                SelectComboItemByTag(BandwidthLimitComboBox, "0");
                label = "Network";
                break;

            case "scheduling":
                SettingsService.DownloadScheduleEnabled = false;
                SettingsService.ScheduleStartHour = 23;
                SettingsService.ScheduleEndHour = 7;
                SettingsService.AutoRetryFailedDownloads = true;
                SettingsService.AutoRetryIntervalMinutes = 10;
                ScheduleToggle.IsOn = false;
                PopulateScheduleHourComboBoxes();
                AutoRetryToggle.IsOn = true;
                SelectComboItemByTag(AutoRetryIntervalComboBox, "10");
                label = "Off-peak scheduling";
                break;

            case "storage":
                SettingsService.DownloadsFolder = string.Empty;
                DownloadPathText.Text = DownloadService.DefaultDownloadsFolder;
                UpdateStorageInfo(DownloadService.DefaultDownloadsFolder);
                label = "Storage & shortcuts";
                break;

            case "updates":
                SettingsService.CheckForUpdatesOnStartup = true;
                SettingsService.CustomCatalogSource = string.Empty;
                AutoUpdateToggle.IsOn = true;
                CustomCatalogSourceBox.Text = string.Empty;
                label = "Updates";
                break;

            default:
                return;
        }

        SettingsService.SaveSettings();
        RefreshSummaries();
        ShowNotice($"{label} settings reset to defaults.", InfoBarSeverity.Success);
    }

    // ─────────────────────────── Search / filter ───────────────────────────

    private void SettingsSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        => ApplySettingsFilter(sender.Text);

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SettingsSearchBox.Text = string.Empty;
        SettingsSearchBox.Focus(FocusState.Programmatic);
    }

    private void ApplySettingsFilter(string? term)
    {
        term = (term ?? string.Empty).Trim();
        var filtering = term.Length > 0;

        if (filtering && !_filterActive)
        {
            _expandState.Clear();
            foreach (var expander in EnumerateExpanders(SettingsSections))
            {
                _expandState[expander] = expander.IsExpanded;
            }
            _filterActive = true;
        }
        else if (!filtering && _filterActive)
        {
            foreach (var pair in _expandState)
            {
                pair.Key.IsExpanded = pair.Value;
            }
            _expandState.Clear();
            _filterActive = false;
        }

        var visibleSections = 0;
        foreach (var child in SettingsSections.Children)
        {
            if (child is FrameworkElement element && FilterElement(element, term, forceShow: false))
            {
                visibleSections++;
            }
        }

        NoSettingsResults.Visibility = filtering && visibleSections == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static IEnumerable<SettingsExpander> EnumerateExpanders(Panel root)
    {
        foreach (var child in root.Children)
        {
            if (child is SettingsExpander expander)
            {
                yield return expander;
            }
            else if (child is Panel panel)
            {
                foreach (var nested in EnumerateExpanders(panel))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool FilterElement(FrameworkElement element, string term, bool forceShow)
    {
        if (term.Length == 0 || forceShow)
        {
            SetVisibleDeep(element, true);
            return true;
        }

        switch (element)
        {
            case SettingsExpander expander:
            {
                var selfMatch = Matches(expander.Header, expander.Description, term);
                var childMatch = false;
                foreach (var item in expander.Items)
                {
                    if (item is FrameworkElement itemElement)
                    {
                        childMatch |= FilterElement(itemElement, term, selfMatch);
                    }
                }

                var visible = selfMatch || childMatch;
                expander.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible)
                {
                    expander.IsExpanded = true;
                }
                return visible;
            }

            case SettingsCard card:
            {
                var match = Matches(card.Header, card.Description, term);
                card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                return match;
            }

            case Panel panel:
            {
                var tagMatch = panel.Tag is string tag && ContainsIgnoreCase(tag, term);
                var any = false;
                foreach (var child in panel.Children)
                {
                    if (child is SettingsExpander or SettingsCard or Panel)
                    {
                        any |= FilterElement((FrameworkElement)child, term, tagMatch);
                    }
                }

                var visible = tagMatch || any;
                panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                return visible;
            }

            default:
                // Elements that are not settings cards (e.g. the warning InfoBars)
                // keep their own managed visibility and never count as a match.
                return true;
        }
    }

    private static void SetVisibleDeep(FrameworkElement element, bool visible)
    {
        switch (element)
        {
            case SettingsExpander expander:
                foreach (var item in expander.Items)
                {
                    if (item is FrameworkElement itemElement)
                    {
                        SetVisibleDeep(itemElement, visible);
                    }
                }
                break;

            case SettingsCard:
                break;

            case Panel panel:
                foreach (var child in panel.Children)
                {
                    if (child is SettingsExpander or SettingsCard or Panel)
                    {
                        SetVisibleDeep((FrameworkElement)child, visible);
                    }
                }
                break;

            default:
                // Non-filterable element: leave its visibility untouched.
                return;
        }

        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(object? header, object? description, string term)
        => ContainsIgnoreCase(ExtractText(header), term) || ContainsIgnoreCase(ExtractText(description), term);

    private static bool ContainsIgnoreCase(string? text, string term)
        => !string.IsNullOrEmpty(text) && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string ExtractText(object? content)
    {
        switch (content)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case TextBlock textBlock:
                return textBlock.Text;
            case Panel panel:
                return string.Join(" ", panel.Children.Select(child => ExtractText(child)));
            default:
                return content.ToString() ?? string.Empty;
        }
    }
}
