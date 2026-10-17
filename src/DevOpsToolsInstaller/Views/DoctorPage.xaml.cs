using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using DevOpsToolsInstaller.Services;
using DevOpsToolsInstaller.ViewModels;

namespace DevOpsToolsInstaller.Views;

public sealed partial class DoctorPage : Page, IRefreshablePage
{
    public DoctorViewModel ViewModel { get; } = new();

    private CancellationTokenSource? _scanCts;
    private bool _hasInitiallyScanned;
    private bool _suppressOptionEvents;
    private string? _lastPathFixBackup;

    public DoctorPage()
    {
        InitializeComponent();
        Loaded += DoctorPage_Loaded;
        Unloaded += DoctorPage_Unloaded;
    }

    private async void DoctorPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyOptionStates();

        if (!_hasInitiallyScanned)
        {
            _hasInitiallyScanned = true;
            await TriggerScanAsync();
        }
    }

    private void ApplyOptionStates()
    {
        // Reflect persisted preferences without re-saving them back on load.
        _suppressOptionEvents = true;
        RedactReportCheckBox.IsChecked = SettingsService.DoctorRedactReport;
        RecheckOnStartCheckBox.IsChecked = SettingsService.DoctorRecheckOnStartup;
        _suppressOptionEvents = false;
    }

    private void DoctorPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }

    private async Task TriggerScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        try
        {
            var catalogTools = App.MainWindowInstance?.Tools;
            await ViewModel.RunScanAsync(catalogTools, _scanCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Scan cancelled
        }
    }

    private async void RerunChecks_Click(object sender, RoutedEventArgs e)
    {
        DoctorNoticeInfoBar.IsOpen = false;
        await TriggerScanAsync();
    }

    void IRefreshablePage.RefreshView() => _ = TriggerScanAsync();

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var redact = RedactReportCheckBox.IsChecked == true;
            var markdown = ViewModel.GetMarkdownReport(redact);
            var dp = new DataPackage();
            dp.SetText(markdown);
            Clipboard.SetContent(dp);

            DoctorNoticeInfoBar.Title = "Report copied";
            DoctorNoticeInfoBar.Message = redact
                ? "Diagnostic report copied to the clipboard. Your username and machine name were redacted."
                : "Workstation Doctor diagnostic report copied to clipboard as markdown.";
            DoctorNoticeInfoBar.Severity = InfoBarSeverity.Success;
            DoctorNoticeInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            DoctorNoticeInfoBar.Title = "Copy failed";
            DoctorNoticeInfoBar.Message = ex.Message;
            DoctorNoticeInfoBar.Severity = InfoBarSeverity.Error;
            DoctorNoticeInfoBar.IsOpen = true;
        }
    }

    private void RedactReport_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressOptionEvents) return;

        SettingsService.DoctorRedactReport = RedactReportCheckBox.IsChecked == true;
        SettingsService.SaveSettings();
    }

    private void RecheckOnStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressOptionEvents) return;

        SettingsService.DoctorRecheckOnStartup = RecheckOnStartCheckBox.IsChecked == true;
        SettingsService.SaveSettings();
    }

    private async void FixAllSafe_Click(object sender, RoutedEventArgs e)
    {
        var plan = PathDoctorService.PlanSafeFixes();
        if (!plan.HasChanges)
        {
            ShowNotice("Nothing to fix",
                "No duplicate or dead entries were found on your User PATH.",
                InfoBarSeverity.Success);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Fix all safe PATH issues?",
            Content = BuildSafeFixPlanContent(plan),
            PrimaryButtonText = $"Fix {plan.TotalChanges} issue{(plan.TotalChanges == 1 ? "" : "s")}",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var result = PathDoctorService.ApplySafeFixes();
        if (!result.Success)
        {
            ShowNotice("Fix failed", result.Message, InfoBarSeverity.Error);
            return;
        }

        _lastPathFixBackup = result.BackupFilePath;
        UndoFixButton.Visibility = Visibility.Visible;

        var duplicates = result.RemovedDuplicatesCount;
        var dead = result.RemovedMissingCount;
        ShowNotice("PATH cleaned",
            $"Removed {duplicates} duplicate entr{(duplicates == 1 ? "y" : "ies")} and {dead} dead path{(dead == 1 ? "" : "s")}. " +
            "A timestamped backup was saved — use Undo to restore the previous PATH.",
            InfoBarSeverity.Success);

        await TriggerScanAsync();
    }

    private async void UndoFix_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastPathFixBackup) || !File.Exists(_lastPathFixBackup))
        {
            UndoFixButton.Visibility = Visibility.Collapsed;
            ShowNotice("Nothing to undo",
                "The PATH backup from the last fix is no longer available.",
                InfoBarSeverity.Informational);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Undo PATH fix?",
            Content = new TextBlock
            {
                Text = "Restore your User PATH to the value captured just before the last fix.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            },
            PrimaryButtonText = "Undo fix",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (PathDoctorService.RestoreBackup(_lastPathFixBackup))
        {
            _lastPathFixBackup = null;
            UndoFixButton.Visibility = Visibility.Collapsed;
            ShowNotice("PATH restored",
                "Your User PATH was restored from the backup taken before the fix.",
                InfoBarSeverity.Success);
        }
        else
        {
            ShowNotice("Undo failed", "The PATH backup could not be restored.", InfoBarSeverity.Error);
        }

        await TriggerScanAsync();
    }

    private void ShowNotice(string title, string message, InfoBarSeverity severity)
    {
        DoctorNoticeInfoBar.Title = title;
        DoctorNoticeInfoBar.Message = message;
        DoctorNoticeInfoBar.Severity = severity;
        DoctorNoticeInfoBar.IsOpen = true;
    }

    /// <summary>Builds the confirmation body listing exactly what the safe fix will change.</summary>
    private static UIElement BuildSafeFixPlanContent(PathSafeFixPlan plan)
    {
        var stack = new StackPanel { Spacing = 12, MaxWidth = 540 };

        stack.Children.Add(new TextBlock
        {
            Text = "These safe, reversible changes will be applied to your User PATH:",
            TextWrapping = TextWrapping.Wrap
        });

        if (plan.DuplicateEntries.Count > 0)
        {
            stack.Children.Add(BuildChangeList(
                $"Remove {plan.DuplicateEntries.Count} duplicate entr{(plan.DuplicateEntries.Count == 1 ? "y" : "ies")}",
                plan.DuplicateEntries));
        }

        if (plan.MissingDirectories.Count > 0)
        {
            stack.Children.Add(BuildChangeList(
                $"Remove {plan.MissingDirectories.Count} dead path{(plan.MissingDirectories.Count == 1 ? "" : "s")}",
                plan.MissingDirectories));
        }

        stack.Children.Add(new TextBlock
        {
            Text = "Your current User PATH is backed up first, and an Undo button appears afterwards.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.8
        });

        return stack;
    }

    private static UIElement BuildChangeList(string heading, IReadOnlyList<string> entries)
    {
        var panel = new StackPanel { Spacing = 6 };

        panel.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeights.SemiBold, FontSize = 13 });

        var list = new StackPanel { Spacing = 2 };
        foreach (var entry in entries.Take(30))
        {
            list.Children.Add(new TextBlock
            {
                Text = "• " + entry,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.9
            });
        }

        if (entries.Count > 30)
        {
            list.Children.Add(new TextBlock
            {
                Text = $"…and {entries.Count - 30} more",
                FontSize = 12,
                Opacity = 0.8
            });
        }

        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        return panel;
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DoctorCheckItemViewModel item)
            return;

        var check = item.Result;
        if (check.FixAction == null)
            return;

        var title = !string.IsNullOrWhiteSpace(check.FixDialogTitle)
            ? check.FixDialogTitle
            : $"Apply Fix for {check.Title}?";

        var message = !string.IsNullOrWhiteSpace(check.FixDialogMessage)
            ? check.FixDialogMessage
            : "Are you sure you want to apply this automated fix?";

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            },
            PrimaryButtonText = !string.IsNullOrWhiteSpace(check.FixButtonText) ? check.FixButtonText : "Apply Fix",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var dialogResult = await dialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary)
            return;

        try
        {
            DoctorNoticeInfoBar.Title = "Applying fix…";
            DoctorNoticeInfoBar.Message = $"Executing repair for '{check.Title}'";
            DoctorNoticeInfoBar.Severity = InfoBarSeverity.Informational;
            DoctorNoticeInfoBar.IsOpen = true;

            var success = await check.FixAction();

            if (success)
            {
                DoctorNoticeInfoBar.Title = "Fix applied successfully";
                DoctorNoticeInfoBar.Message = $"Automated fix for '{check.Title}' completed.";
                DoctorNoticeInfoBar.Severity = InfoBarSeverity.Success;
            }
            else
            {
                DoctorNoticeInfoBar.Title = "Fix completed with notices";
                DoctorNoticeInfoBar.Message = $"Fix action finished for '{check.Title}'. Please review the diagnostic status.";
                DoctorNoticeInfoBar.Severity = InfoBarSeverity.Warning;
            }
            DoctorNoticeInfoBar.IsOpen = true;

            // Automatically re-run diagnostics to reflect updated state
            await TriggerScanAsync();
        }
        catch (Exception ex)
        {
            DoctorNoticeInfoBar.Title = "Fix failed";
            DoctorNoticeInfoBar.Message = ex.Message;
            DoctorNoticeInfoBar.Severity = InfoBarSeverity.Error;
            DoctorNoticeInfoBar.IsOpen = true;
        }
    }

    private async void LearnMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DoctorCheckItemViewModel item)
            return;

        var check = item.Result;
        if (string.IsNullOrWhiteSpace(check.LearnMoreDetails) && string.IsNullOrWhiteSpace(check.LearnMoreUrl))
            return;

        var stack = new StackPanel { Spacing = 12, MaxWidth = 480 };

        if (!string.IsNullOrWhiteSpace(check.LearnMoreDetails))
        {
            stack.Children.Add(new TextBlock
            {
                Text = check.LearnMoreDetails,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            });
        }

        if (!string.IsNullOrWhiteSpace(check.LearnMoreUrl))
        {
            var url = check.LearnMoreUrl;
            var linkBtn = new HyperlinkButton
            {
                Content = $"Open Documentation: {url}",
                NavigateUri = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null
            };
            stack.Children.Add(linkBtn);
        }

        var dialog = new ContentDialog
        {
            Title = !string.IsNullOrWhiteSpace(check.LearnMoreTitle) ? check.LearnMoreTitle : $"About: {check.Title}",
            Content = stack,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
