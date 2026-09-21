using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;
using DevOpsToolsInstaller.ViewModels;

namespace DevOpsToolsInstaller.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = new HomeViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        await ViewModel.LoadDashboardAsync(mw);
    }

    private async void RetryScan_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        await ViewModel.LoadDashboardAsync(mw);
    }

    private void BrowseCatalog_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Catalog");

    private void SeeAllStacks_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Stacks");

    private void InstalledMetric_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Installed");

    private void UpdatesMetric_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Installed");

    private void DownloadsMetric_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Downloads");

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
