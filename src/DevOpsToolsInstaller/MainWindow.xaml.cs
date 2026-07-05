using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;
using DevOpsToolsInstaller.Views;

namespace DevOpsToolsInstaller;

public sealed partial class MainWindow : Window
{
    private readonly CatalogService _catalog = new();
    private readonly DownloadService _download = new();

    /// <summary>
    /// Shared tool list — loaded once, referenced by all pages.
    /// </summary>
    public ObservableCollection<ToolDefinition> Tools { get; } = new();

    /// <summary>
    /// Tools that are queued / in-progress / completed downloads.
    /// </summary>
    public ObservableCollection<ToolDefinition> DownloadQueue { get; } = new();

    public CatalogService CatalogSvc => _catalog;
    public DownloadService DownloadSvc => _download;

    public MainWindow()
    {
        InitializeComponent();
        Title = "DevOps Tools Installer";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
    }

    private void NavView_SelectionChanged(
        NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        if (item.Tag is not string tag) return;

        var pageType = tag switch
        {
            "Home"      => typeof(HomePage),
            "Catalog"   => typeof(CatalogPage),
            "Downloads" => typeof(DownloadsPage),
            "Settings"  => typeof(SettingsPage),
            _           => typeof(HomePage)
        };

        ContentFrame.Navigate(pageType);
    }

    /// <summary>
    /// Navigate to a page by its sidebar tag. Used by quick-link buttons.
    /// </summary>
    public void NavigateTo(string tag)
    {
        foreach (NavigationViewItem item in NavView.MenuItems)
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }

        foreach (NavigationViewItem item in NavView.FooterMenuItems)
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }
}
