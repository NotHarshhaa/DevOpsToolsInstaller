using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;
using DevOpsToolsInstaller.ViewModels;

namespace DevOpsToolsInstaller.Views;

public sealed partial class HomePage : Page, IRefreshablePage
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = new HomeViewModel();
        DataContext = ViewModel;
        NavigationCacheMode = NavigationCacheMode.Required;
        InitializeComponent();

        // Version comes from the assembly so the hero chip, title bar and
        // About page never drift apart.
        HeroVersionChipText.Text = $"v{AppUpdaterService.CurrentVersion} • WinUI 3";

        Loaded += HomePage_Loaded;
        ActualThemeChanged += (s, e) => RenderActivityTrends();
    }

    // ── Local activity sparklines (installs / updates per day) ──
    //
    // The values come from the persisted install history file, so the charts
    // survive app restarts and never need a network call.

    private readonly ChartTelemetry.BoundedValues _installsTrend = new(InstallTrendService.DefaultDays);
    private readonly ChartTelemetry.BoundedValues _updatesTrend = new(InstallTrendService.DefaultDays);

    public System.Collections.ObjectModel.ObservableCollection<ISeries> InstallsTrendSeries { get; private set; } = new();
    public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> InstallsTrendXAxes { get; private set; } = ChartTelemetry.HiddenAxes();
    public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> InstallsTrendYAxes { get; private set; } = ChartTelemetry.HiddenAxes();
    public System.Collections.ObjectModel.ObservableCollection<ISeries> UpdatesTrendSeries { get; private set; } = new();
    public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> UpdatesTrendXAxes { get; private set; } = ChartTelemetry.HiddenAxes();
    public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> UpdatesTrendYAxes { get; private set; } = ChartTelemetry.HiddenAxes();

    /// <summary>
    /// Fills the daily sparklines from the persisted activity history, and keeps
    /// the friendly empty state until there is at least one recorded day.
    /// </summary>
    private void RenderActivityTrends()
    {
        var activity = ViewModel.DailyActivity;

        if (activity.Count == 0)
        {
            InstallsTrendChart.Visibility = Visibility.Collapsed;
            UpdatesTrendChart.Visibility = Visibility.Collapsed;
            InstallsTrendEmpty.Visibility = Visibility.Visible;
            UpdatesTrendEmpty.Visibility = Visibility.Visible;
            return;
        }

        _installsTrend.ClearSamples();
        _updatesTrend.ClearSamples();
        foreach (var day in activity)
        {
            _installsTrend.Push(day.Installs);
            _updatesTrend.Push(day.Updates);
        }

        var labels = activity.Select(day => day.ShortLabel).ToArray();

        InstallsTrendXAxes = ChartTelemetry.CreateDailySparkAxes(labels, ActualTheme);
        InstallsTrendYAxes = ChartTelemetry.HiddenAxes();
        InstallsTrendSeries = new System.Collections.ObjectModel.ObservableCollection<ISeries>
        {
            ChartTelemetry.CreateSparkLine(_installsTrend)
        };
        UpdatesTrendXAxes = ChartTelemetry.CreateDailySparkAxes(labels, ActualTheme);
        UpdatesTrendYAxes = ChartTelemetry.HiddenAxes();
        UpdatesTrendSeries = new System.Collections.ObjectModel.ObservableCollection<ISeries>
        {
            ChartTelemetry.CreateSparkLine(_updatesTrend)
        };

        Bindings.Update();

        bool drawable = ViewModel.HasTrendData && activity.Count >= 2;
        InstallsTrendChart.Visibility = drawable ? Visibility.Visible : Visibility.Collapsed;
        UpdatesTrendChart.Visibility = drawable ? Visibility.Visible : Visibility.Collapsed;
        InstallsTrendEmpty.Visibility = drawable ? Visibility.Collapsed : Visibility.Visible;
        UpdatesTrendEmpty.Visibility = drawable ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        await ViewModel.LoadDashboardAsync(mw);
        SeeAllStacksText.Text = $"See all {ViewModel.Stacks.Count}";
        RenderActivityTrends();
    }

    private async void RetryScan_Click(object sender, RoutedEventArgs e)
    {
        HomePage_Loaded(this, new Microsoft.UI.Xaml.RoutedEventArgs());
    }

    void IRefreshablePage.RefreshView() => HomePage_Loaded(this, new Microsoft.UI.Xaml.RoutedEventArgs());

    private void BrowseCatalog_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Catalog");

    private void BuildStack_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateToStackBuilder();

    private void StartTour_Click(object sender, RoutedEventArgs e)
    {
        TourStep1Tip.IsOpen = true;
    }

    private void DismissTour_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DismissTour();
    }

    private void TourStep1_NextClick(TeachingTip sender, object args)
    {
        sender.IsOpen = false;
        TourStep2Tip.IsOpen = true;
    }

    private void TourStep2_NextClick(TeachingTip sender, object args)
    {
        sender.IsOpen = false;
        TourStep3Tip.IsOpen = true;
    }

    private void Tour_DismissClick(TeachingTip sender, object args)
    {
        sender.IsOpen = false;
        ViewModel.DismissTour();
    }

    private async void FeatureCardAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string actionTag }) return;

        var mw = App.MainWindowInstance;
        if (mw is null) return;

        switch (actionTag)
        {
            case "Catalog":
                mw.NavigateTo("Catalog");
                break;
            case "Stacks":
                mw.NavigateTo("Stacks");
                break;
            case "Downloads":
                mw.NavigateTo("Downloads");
                break;
            case "Installed":
                mw.NavigateTo("Installed");
                break;
            case "Doctor":
                mw.NavigateTo("Doctor");
                break;
            case "Settings":
            case "Settings:Security":
            case "Settings:Notifications":
                mw.NavigateTo("Settings");
                break;
            case "About":
                mw.NavigateTo("About");
                break;
            case "CLI:Docs":
                await ShowCliDocsDialogAsync();
                break;
            default:
                mw.NavigateTo(actionTag);
                break;
        }
    }

    private async Task ShowCliDocsDialogAsync()
    {
        var mw = App.MainWindowInstance;
        if (mw?.Content?.XamlRoot is null) return;

        var stack = new StackPanel { Spacing = 14, MinWidth = 460 };

        var introText = new TextBlock
        {
            Text = "DevOpsToolsInstaller includes a headless command-line mode for unattended setup, CI/CD runners, and developer provisioning scripts.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        };
        stack.Children.Add(introText);

        var codeBlock = new Border
        {
            Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12)
        };

        var codeText = new TextBlock
        {
            Text = "DevOpsToolsInstaller.exe --list\n" +
                   "DevOpsToolsInstaller.exe --install kubectl,terraform,helm\n" +
                   "DevOpsToolsInstaller.exe --install-bundle k8s-starter\n" +
                   "DevOpsToolsInstaller.exe --update\n" +
                   "DevOpsToolsInstaller.exe --path-doctor\n" +
                   "DevOpsToolsInstaller.exe --export-profile my-tools.json\n" +
                   "DevOpsToolsInstaller.exe --report workstation.md",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace"),
            FontSize = 12,
            LineHeight = 18,
            IsTextSelectionEnabled = true
        };
        codeBlock.Child = codeText;
        stack.Children.Add(codeBlock);

        var copyButton = new Button
        {
            Content = "Copy CLI examples",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        copyButton.Click += (s, e) =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(codeText.Text);
                Clipboard.SetContent(package);
                copyButton.Content = "Copied!";
            }
            catch { }
        };
        stack.Children.Add(copyButton);

        var dialog = new ContentDialog
        {
            XamlRoot = mw.Content.XamlRoot,
            Title = "Headless CLI Documentation",
            Content = stack,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private void SeeAllStacks_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Stacks");

    private void InstalledMetric_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Installed");

    private void UpdatesMetric_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Installed");

    private void DownloadsMetric_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Downloads");

    /// <summary>Runs the action offered by the hero's "next best action" line.</summary>
    private void NextAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && !string.IsNullOrWhiteSpace(tag))
        {
            App.MainWindowInstance?.NavigateTo(tag);
        }
    }

    /// <summary>The "Needs attention" metric deep-links to full Doctor diagnostics.</summary>
    private void PathMetric_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Doctor");

    private void RunDoctor_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Doctor");

    private void MetricGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MetricCardInstalled == null || MetricCardUpdates == null ||
            MetricCardDownloads == null || MetricCardPath == null) return;

        if (e.NewSize.Width < 880)
        {
            // 2x2 layout below 880-900px
            Grid.SetRow(MetricCardInstalled, 0); Grid.SetColumn(MetricCardInstalled, 0); Grid.SetColumnSpan(MetricCardInstalled, 2);
            Grid.SetRow(MetricCardUpdates, 0); Grid.SetColumn(MetricCardUpdates, 2); Grid.SetColumnSpan(MetricCardUpdates, 2);
            Grid.SetRow(MetricCardDownloads, 1); Grid.SetColumn(MetricCardDownloads, 0); Grid.SetColumnSpan(MetricCardDownloads, 2);
            Grid.SetRow(MetricCardPath, 1); Grid.SetColumn(MetricCardPath, 2); Grid.SetColumnSpan(MetricCardPath, 2);
        }
        else
        {
            // 1x4 layout
            Grid.SetRow(MetricCardInstalled, 0); Grid.SetColumn(MetricCardInstalled, 0); Grid.SetColumnSpan(MetricCardInstalled, 1);
            Grid.SetRow(MetricCardUpdates, 0); Grid.SetColumn(MetricCardUpdates, 1); Grid.SetColumnSpan(MetricCardUpdates, 1);
            Grid.SetRow(MetricCardDownloads, 0); Grid.SetColumn(MetricCardDownloads, 2); Grid.SetColumnSpan(MetricCardDownloads, 1);
            Grid.SetRow(MetricCardPath, 0); Grid.SetColumn(MetricCardPath, 3); Grid.SetColumnSpan(MetricCardPath, 1);
        }
    }


    private void OpenBinFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.PathBinFolder))
        {
            LauncherService.OpenDownloadsFolder(ViewModel.PathBinFolder);
        }
    }

    private void CopyBinFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.PathBinFolder))
        {
            try
            {
                var package = new DataPackage();
                package.SetText(ViewModel.PathBinFolder);
                Clipboard.SetContent(package);
            }
            catch
            {
                // Clipboard copy fallback
            }
        }
    }

    private void StackAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HomeStackItemViewModel stackModel })
        {
            var mw = App.MainWindowInstance;
            if (mw is null) return;

            if (stackModel.IsComplete)
            {
                mw.NavigateToCatalogWithBundle(stackModel.Id);
            }
            else
            {
                ViewModel.InstallStack(stackModel, mw);
            }
        }
    }

    private async void UpdateTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToolDefinition tool })
        {
            var mw = App.MainWindowInstance;
            if (mw is null) return;

            await ViewModel.UpdateToolAsync(tool, mw);
        }
    }

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        await ViewModel.UpdateAllAsync(mw);
    }

    private async void WhatsNew_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ToolDefinition tool })
        {
            return;
        }

        var mw = App.MainWindowInstance;
        if (mw?.Content?.XamlRoot is null) return;

        var notes = await ToolReleaseNotesService.GetReleaseNotesAsync(tool.DownloadUrl);
        if (notes is null || string.IsNullOrWhiteSpace(notes.Body))
        {
            await ShowWhatsNewDialogAsync(
                mw.Content.XamlRoot,
                $"{tool.Name} — release notes",
                new TextBlock
                {
                    Text = "Release notes are not available for this tool (the vendor's releases are not on GitHub, or they could not be fetched).",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
                },
                "Close");
            return;
        }

        var body = new StackPanel { Spacing = 12, MinWidth = 420 };
        body.Children.Add(new TextBlock
        {
            Text = notes.TagName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14
        });
        var scroll = new ScrollViewer
        {
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = notes.Body,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontSize = 13,
                LineHeight = 19
            }
        };
        body.Children.Add(scroll);

        await ShowWhatsNewDialogAsync(mw.Content.XamlRoot, $"{tool.Name} — what's new in {notes.TagName}", body, "Close");
    }

    private static async Task ShowWhatsNewDialogAsync(
        Microsoft.UI.Xaml.XamlRoot xamlRoot, string title, object content, string closeText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    private async void InstallPopularTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToolDefinition tool })
        {
            var mw = App.MainWindowInstance;
            if (mw is null) return;

            await ViewModel.InstallToolAsync(tool, mw);
        }
    }

    private void PopularToolRow_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomePopularToolItemViewModel item)
        {
            var mw = App.MainWindowInstance;
            if (mw is null) return;

            mw.NavigateToCatalogWithSearch(item.Name);
        }
    }

    private void SetupSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var mw = App.MainWindowInstance;
            if (mw is null) return;

            ViewModel.UpdateSearchSuggestions(sender.Text?.Trim() ?? string.Empty, mw);
            sender.ItemsSource = ViewModel.SearchSuggestions;
        }
    }

    private void SetupSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            App.MainWindowInstance?.NavigateToCatalogWithSearch(query);
        }
    }
}
