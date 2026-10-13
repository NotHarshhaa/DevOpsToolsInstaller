using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DevOpsToolsInstaller.ViewModels;

namespace DevOpsToolsInstaller.Views;

public sealed partial class DoctorPage : Page
{
    public DoctorViewModel ViewModel { get; } = new();

    private CancellationTokenSource? _scanCts;
    private bool _hasInitiallyScanned;

    public DoctorPage()
    {
        InitializeComponent();
        Loaded += DoctorPage_Loaded;
        Unloaded += DoctorPage_Unloaded;
    }

    private async void DoctorPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_hasInitiallyScanned)
        {
            _hasInitiallyScanned = true;
            await TriggerScanAsync();
        }
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

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var markdown = ViewModel.GetMarkdownReport();
            var dp = new DataPackage();
            dp.SetText(markdown);
            Clipboard.SetContent(dp);

            DoctorNoticeInfoBar.Title = "Report copied";
            DoctorNoticeInfoBar.Message = "Workstation Doctor diagnostic report copied to clipboard as markdown.";
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
