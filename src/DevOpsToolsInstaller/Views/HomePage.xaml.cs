using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        // Load catalog if not yet loaded
        if (mw.Tools.Count == 0)
        {
            try
            {
                var tools = await mw.CatalogSvc.LoadCatalogAsync();

                // Mark already-downloaded tools
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                foreach (var tool in tools)
                {
                    if (DownloadService.IsAlreadyDownloaded(tool, dlFolder))
                    {
                        tool.Status = Models.ToolStatus.Downloaded;
                        tool.Progress = 100;
                    }
                }

                mw.Tools.Clear();
                foreach (var tool in tools)
                    mw.Tools.Add(tool);
            }
            catch
            {
                ToolCountText.Text = "Error";
                ToolCountLabel.Text = "Failed to load catalog";
                return;
            }
        }

        // Update stats
        ToolCountText.Text = mw.Tools.Count.ToString();
        ToolCountLabel.Text = "Tools Available";

        var downloaded = mw.Tools.Count(t => t.Status == Models.ToolStatus.Downloaded);
        DownloadedCountText.Text = downloaded.ToString();
        DownloadedCountLabel.Text = "Downloaded";
    }

    private void Catalog_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Catalog");

    private void Downloads_Click(object sender, RoutedEventArgs e)
        => App.MainWindowInstance?.NavigateTo("Downloads");
}
