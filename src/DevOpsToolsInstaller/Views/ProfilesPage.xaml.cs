using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

/// <summary>
/// Consolidates the workstation-management features that operate on top of the
/// tool catalog:
///   • <b>Profiles</b> — named, reusable tool sets you can apply or install.
///   • <b>Snapshots</b> — point-in-time restore points with diff &amp; restore.
///   • <b>History</b> — install/update/uninstall log with per-tool rollback.
///   • <b>Sync</b> — merge profiles/snapshots across machines via a folder.
/// </summary>
public sealed partial class ProfilesPage : Page, IRefreshablePage
{
    public ProfilesPage()
    {
        InitializeComponent();

        // Wired here rather than in XAML: with IsSelected="True" on the first
        // SegmentedItem, a XAML-wired handler fires during InitializeComponent,
        // before the section containers exist.
        SectionSegmented.SelectionChanged += SectionSegmented_SelectionChanged;

        Loaded += ProfilesPage_Loaded;
    }

    private XamlRoot? PageXamlRoot =>
        XamlRoot ?? App.MainWindowInstance?.Content?.XamlRoot;

    private async void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is not null)
        {
            await mw.EnsureCatalogLoadedAsync();
        }
        RefreshProfiles();
        RefreshSnapshots();
        RefreshHistory();
        RefreshSync();
    }

    // ── Section switching ────────────────────────────────────────────────

    void IRefreshablePage.RefreshView()
    {
        RefreshProfiles();
        RefreshSnapshots();
        RefreshHistory();
        RefreshSync();
    }

    private void SectionSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not CommunityToolkit.WinUI.Controls.Segmented segmented)
            return;

        // Skip events raised before the page finished loading.
        if (ProfilesContainer is null || SnapshotsContainer is null ||
            HistoryContainer is null || SyncContainer is null)
            return;

        string tag = "Profiles";
        if (segmented.SelectedItem is CommunityToolkit.WinUI.Controls.SegmentedItem selected && selected.Tag is string t)
        {
            tag = t;
        }
        else
        {
            tag = segmented.SelectedIndex switch
            {
                1 => "Snapshots",
                2 => "History",
                3 => "Sync",
                _ => "Profiles"
            };
        }

        ProfilesContainer.Visibility = tag == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        SnapshotsContainer.Visibility = tag == "Snapshots" ? Visibility.Visible : Visibility.Collapsed;
        HistoryContainer.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        SyncContainer.Visibility = tag == "Sync" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Profiles ─────────────────────────────────────────────────────────

    private void RefreshProfiles()
    {
        var profiles = ProfileStoreService.LoadAll();
        ProfilesList.ItemsSource = profiles;
        bool empty = profiles.Count == 0;
        ProfilesList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ProfilesEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshProfiles_Click(object sender, RoutedEventArgs e) => RefreshProfiles();

    private async void CaptureProfile_Click(object sender, RoutedEventArgs e)
    {
        var (name, description, ok) = await PromptNameAsync(
            "Capture workstation profile",
            "Save the tools currently installed on this machine as a named profile.",
            $"{Environment.MachineName} — {DateTime.Now:yyyy-MM-dd}",
            allowDescription: true);

        if (!ok) return;

        var mw = App.MainWindowInstance;
        if (mw is null) return;
        var profile = ProfileStoreService.CaptureFromInstalled(name, description, mw.Tools.ToList());
        RefreshProfiles();
        ShowNotice($"Profile '{profile.Name}' captured with {profile.ToolCount} tool(s).", InfoBarSeverity.Success);
    }

    private async void NewProfileFromSelection_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;
        var selected = mw.Tools.Where(t => t.IsSelected).Select(t => t.Id).ToList();
        if (selected.Count == 0)
        {
            ShowNotice("Select tools in the Tool Catalog first, then create a profile from the selection.",
                InfoBarSeverity.Informational);
            return;
        }

        var (name, description, ok) = await PromptNameAsync(
            "New profile from selection",
            $"Save the {selected.Count} catalog-selected tool(s) as a named profile.",
            $"Selected tools — {DateTime.Now:yyyy-MM-dd}",
            allowDescription: true);

        if (!ok) return;

        var profile = ProfileStoreService.CreateFromSelection(name, description, selected);
        RefreshProfiles();
        ShowNotice($"Profile '{profile.Name}' saved with {profile.ToolCount} tool(s).", InfoBarSeverity.Success);
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        if (App.MainWindowInstance is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var (profile, error) = ProfileStoreService.ImportJson(await File.ReadAllTextAsync(file.Path));
        if (profile is null)
        {
            ShowNotice(error ?? "Could not read the profile.", InfoBarSeverity.Error);
            return;
        }

        ProfileStoreService.Save(profile);
        RefreshProfiles();
        ShowNotice($"Imported profile '{profile.Name}' ({profile.ToolCount} tool(s)).", InfoBarSeverity.Success);
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkstationProfile profile }) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var count = ProfileStoreService.Apply(profile, mw.Tools);
        mw.NavigateTo("Catalog");
        ShowNotice($"Applied '{profile.Name}': selected {count} tool(s) in the catalog.", InfoBarSeverity.Success);
    }

    private async void InstallProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkstationProfile profile }) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;
        var tools = ProfileStoreService.ResolveTools(profile, mw.Tools.ToList());
        await InstallToolsAsync(tools, profile.Name);
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkstationProfile profile }) return;

        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(folder, $"profile-{Sanitize(profile.Name)}-{DateTime.Now:yyyyMMdd-HHmm}.json");
        try
        {
            await File.WriteAllTextAsync(path, ProfileStoreService.ExportJson(profile));
            ShowNotice($"Exported profile to {path}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotice($"Export failed: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkstationProfile profile }) return;

        var xamlRoot = PageXamlRoot;
        if (xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            Title = $"Delete profile '{profile.Name}'?",
            Content = "This removes the saved profile. Installed tools are not affected.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ProfileStoreService.Delete(profile.Id);
        RefreshProfiles();
        ShowNotice($"Deleted profile '{profile.Name}'.", InfoBarSeverity.Informational);
    }

    // ── Snapshots ────────────────────────────────────────────────────────

    private void RefreshSnapshots()
    {
        var snapshots = SnapshotService.LoadAll();
        SnapshotsList.ItemsSource = snapshots;
        bool empty = snapshots.Count == 0;
        SnapshotsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        SnapshotsEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshSnapshots_Click(object sender, RoutedEventArgs e) => RefreshSnapshots();

    private void OpenSnapshotsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SnapshotService.StorageFolder);
        LauncherService.OpenDownloadsFolder(SnapshotService.StorageFolder);
    }

    private async void CaptureSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var (name, _, ok) = await PromptNameAsync(
            "Capture workstation snapshot",
            "Record the installed tools, their versions, and your User PATH as a restore point.",
            $"snapshot-{DateTime.Now:yyyyMMdd-HHmm}",
            allowDescription: false);

        if (!ok) return;

        var mw = App.MainWindowInstance;
        if (mw is null) return;
        var snapshot = SnapshotService.Capture(name, mw.Tools.ToList());
        RefreshSnapshots();
        ShowNotice($"Snapshot '{snapshot.Name}' captured ({snapshot.ToolCount} tool(s)).", InfoBarSeverity.Success);
    }

    private async void DiffSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkstationSnapshot snapshot }) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var xamlRoot = PageXamlRoot;
        if (xamlRoot is null) return;

        var diff = await Task.Run(() => SnapshotService.Diff(snapshot, mw.Tools.ToList()));

        var panel = new StackPanel { Spacing = 10, MaxWidth = 520 };
        panel.Children.Add(SectionLabel($"Missing from workstation ({diff.Missing.Count}) — present in snapshot, absent now:"));
        panel.Children.Add(ListBlock(diff.Missing.Select(x => $"• {x.ToolName} v{x.Version}").ToList(),
            diff.Missing.Count == 0 ? "Nothing missing." : null));
        panel.Children.Add(SectionLabel($"Version changed ({diff.Changed.Count}):"));
        panel.Children.Add(ListBlock(diff.Changed.Select(c => $"• {c.Snapshot.ToolName}: v{c.Snapshot.Version} → v{c.Current.Version}").ToList(),
            diff.Changed.Count == 0 ? "No version changes." : null));
        panel.Children.Add(SectionLabel($"Added since snapshot ({diff.Extra.Count}):"));
        panel.Children.Add(ListBlock(diff.Extra.Select(x => $"• {x.ToolName}").ToList(),
            diff.Extra.Count == 0 ? "Nothing added." : null));

        var dialog = new ContentDialog
        {
            Title = $"Diff: {snapshot.Name} vs this workstation",
            Content = new ScrollViewer { Content = panel },
            CloseButtonText = "Close",
            XamlRoot = xamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void RestoreSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkstationSnapshot snapshot }) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var xamlRoot = PageXamlRoot;
        if (xamlRoot is null) return;

        var missing = SnapshotService.GetMissingTools(snapshot, mw.Tools.ToList());
        if (missing.Count == 0)
        {
            ShowNotice($"Snapshot '{snapshot.Name}' already matches this workstation.", InfoBarSeverity.Informational);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = $"Restore '{snapshot.Name}'?",
            Content = $"This will download and install {missing.Count} tool(s) that are in the snapshot but missing now:\n\n" +
                      string.Join("\n", missing.Take(12).Select(t => $"• {t.Name}")) +
                      (missing.Count > 12 ? $"\n… and {missing.Count - 12} more" : ""),
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await InstallToolsAsync(missing, $"snapshot '{snapshot.Name}'");
    }

    private async void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkstationSnapshot snapshot }) return;

        var xamlRoot = PageXamlRoot;
        if (xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            Title = $"Delete snapshot '{snapshot.Name}'?",
            Content = "The snapshot file is removed. Installed tools are not affected.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SnapshotService.Delete(snapshot.Name);
        RefreshSnapshots();
        ShowNotice($"Deleted snapshot '{snapshot.Name}'.", InfoBarSeverity.Informational);
    }

    // ── History ──────────────────────────────────────────────────────────

    private void RefreshHistory()
    {
        var events = InstallHistoryService.Load(300);
        HistoryList.ItemsSource = events;
        bool empty = events.Count == 0;
        HistoryList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var xamlRoot = PageXamlRoot;
        if (xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            Title = "Clear install history?",
            Content = "This permanently deletes the recorded install/update/uninstall history. Snapshots and profiles are not affected.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        InstallHistoryService.Clear();
        RefreshHistory();
        ShowNotice("Install history cleared.", InfoBarSeverity.Informational);
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var xamlRoot = PageXamlRoot;
        if (xamlRoot is null) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        // Tools that have more than one recorded version are rollback candidates.
        var candidates = mw.Tools
            .Where(t => InstallHistoryService.GetInstalledVersions(t.Id).Count > 0)
            .OrderBy(t => t.Name)
            .ToList();

        if (candidates.Count == 0)
        {
            ShowNotice("No rollback targets yet — versions are recorded as tools are installed or updated.",
                InfoBarSeverity.Informational);
            return;
        }

        var toolCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select a tool" };
        foreach (var t in candidates) toolCombo.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Id });

        var versionCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select a previous version", IsEnabled = false };

        toolCombo.SelectionChanged += (s, ev) =>
        {
            versionCombo.Items.Clear();
            if (toolCombo.SelectedItem is ComboBoxItem item && item.Tag is string toolId)
            {
                foreach (var v in InstallHistoryService.GetInstalledVersions(toolId))
                    versionCombo.Items.Add(v);
                versionCombo.IsEnabled = versionCombo.Items.Count > 0;
            }
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = "Reinstall a tool at a previously installed version. The current version is replaced by re-downloading the older artifact.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        panel.Children.Add(toolCombo);
        panel.Children.Add(versionCombo);

        var dialog = new ContentDialog
        {
            Title = "Roll back a tool",
            Content = panel,
            PrimaryButtonText = "Roll back",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (toolCombo.SelectedItem is not ComboBoxItem selectedTool ||
            selectedTool.Tag is not string selectedId ||
            versionCombo.SelectedItem is not string version)
        {
            ShowNotice("Select both a tool and a version to roll back.", InfoBarSeverity.Warning);
            return;
        }

        var tool = mw.Tools.FirstOrDefault(t => string.Equals(t.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (tool is null) return;

        ShowNotice($"Rolling {tool.Name} back to v{version}…", InfoBarSeverity.Informational);

        if (!mw.DownloadQueue.Contains(tool)) mw.DownloadQueue.Add(tool);
        mw.NavigateTo("Downloads");

        var dispatcher = mw.DispatcherQueue;
        _ = Task.Run(async () =>
        {
            try
            {
                DownloadService.DiscardDownloadedArtifact(tool, dlFolder);
                tool.SetVersion(version);
                await mw.DownloadSvc.DownloadAsync(tool, dlFolder);

                if (tool.Status == ToolStatus.Downloaded)
                {
                    var res = ArtifactService.Perform(tool, dlFolder);
                    tool.IsInstalled = res.Success;
                    if (res.Success)
                        InstallHistoryService.RecordRollback(tool.Id, tool.Name, version, res.Message);

                    dispatcher.TryEnqueue(() =>
                    {
                        ShowNotice(res.Message, res.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
                        RefreshHistory();
                    });
                }
                else
                {
                    dispatcher.TryEnqueue(() =>
                        ShowNotice($"Rollback download for {tool.Name} did not complete.", InfoBarSeverity.Error));
                }
            }
            catch (Exception ex)
            {
                dispatcher.TryEnqueue(() =>
                    ShowNotice($"Rollback failed: {ex.Message}", InfoBarSeverity.Error));
            }
        });
    }

    // ── Sync ─────────────────────────────────────────────────────────────

    private void RefreshSync()
    {
        var status = ProfileSyncService.GetStatus();
        SyncFolderText.Text = status.Configured
            ? $"Sync folder: {status.Folder}"
            : "No sync folder configured.";
        SyncStatusText.Text = status.LastSync.HasValue
            ? $"Last sync: {status.LastSync.Value:yyyy-MM-dd HH:mm}"
            : "Last sync: never";
    }

    private async void ChooseSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            if (App.MainWindowInstance is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            SettingsService.SyncFolder = folder.Path;
            SettingsService.SaveSettings();
            RefreshSync();
            ShowNotice($"Sync folder set to {folder.Path}. Run 'Sync now' to merge.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not set sync folder: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void ClearSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.SyncFolder = string.Empty;
        SettingsService.SaveSettings();
        RefreshSync();
        ShowNotice("Sync disabled.", InfoBarSeverity.Informational);
    }

    private void SyncNow_Click(object sender, RoutedEventArgs e) => RunSync(ProfileSyncService.Sync);
    private void PushSync_Click(object sender, RoutedEventArgs e) => RunSync(ProfileSyncService.Push);
    private void PullSync_Click(object sender, RoutedEventArgs e) => RunSync(ProfileSyncService.Pull);

    private void RunSync(Func<SyncResult> action)
    {
        var result = action();
        RefreshSync();
        RefreshProfiles();
        RefreshSnapshots();
        ShowNotice(result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    // ── Shared helpers ───────────────────────────────────────────────────

    private async Task InstallToolsAsync(List<ToolDefinition> tools, string label)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var toInstall = tools
            .Where(t => !t.IsInstalled && t.Status != ToolStatus.Downloaded && t.Status != ToolStatus.Downloading)
            .ToList();

        if (toInstall.Count == 0)
        {
            ShowNotice($"Nothing to install — {label} is already fully present.", InfoBarSeverity.Informational);
            return;
        }

        foreach (var tool in toInstall)
        {
            tool.IsSelected = true;
            if (!mw.DownloadQueue.Contains(tool)) mw.DownloadQueue.Add(tool);
        }

        if (DownloadSchedulerService.IsOffPeakActive)
        {
            foreach (var tool in toInstall) { tool.Status = ToolStatus.Scheduled; tool.Progress = 0; }
            ShowNotice($"{label}: {toInstall.Count} tool(s) queued for off-peak hours.", InfoBarSeverity.Informational);
            mw.NavigateTo("Downloads");
            return;
        }

        ShowNotice($"Installing {toInstall.Count} tool(s) from {label}…", InfoBarSeverity.Informational);
        mw.NavigateTo("Downloads");

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var dispatcher = mw.DispatcherQueue;

        _ = Task.Run(async () =>
        {
            int installed = 0;
            try
            {
                await mw.DownloadSvc.DownloadBatchAsync(toInstall, dlFolder, maxConcurrency: 3);

                foreach (var tool in toInstall.Where(t => t.Status == ToolStatus.Downloaded))
                {
                    var res = ArtifactService.Perform(tool, dlFolder);
                    if (res.Success) installed++;
                    tool.IsInstalled = res.Success;
                }
            }
            catch { }

            dispatcher.TryEnqueue(() =>
            {
                ToastService.Show($"{label}", $"{installed}/{toInstall.Count} tool(s) ready.");
                RefreshProfiles();
                RefreshSnapshots();
            });
        });
    }

    private async Task<(string Name, string Description, bool Ok)> PromptNameAsync(
        string title, string message, string defaultName, bool allowDescription)
    {
        var nameBox = new TextBox { Header = "Name", Text = defaultName, HorizontalAlignment = HorizontalAlignment.Stretch };
        var descBox = new TextBox
        {
            Header = "Description (optional)",
            PlaceholderText = "What is this for?",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = allowDescription ? Visibility.Visible : Visibility.Collapsed
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(descBox);

        var xamlRoot = PageXamlRoot;
        if (xamlRoot is null) return (defaultName, string.Empty, false);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        return (
            string.IsNullOrWhiteSpace(nameBox.Text) ? defaultName : nameBox.Text.Trim(),
            descBox.Text?.Trim() ?? string.Empty,
            result == ContentDialogResult.Primary);
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock ListBlock(List<string> lines, string? emptyText)
    {
        return new TextBlock
        {
            Text = lines.Count > 0 ? string.Join("\n", lines) : (emptyText ?? "—"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "profile").Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "profile" : clean;
    }

    private void ShowNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        NoticeInfoBar.Message = message;
        NoticeInfoBar.Severity = severity;
        NoticeInfoBar.IsOpen = true;
    }
}
