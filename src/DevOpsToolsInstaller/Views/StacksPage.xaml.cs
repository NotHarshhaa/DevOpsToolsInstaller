using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Views;

public sealed partial class StacksPage : Page
{
    private List<ToolBundle> _allBundles = new();

    public StacksPage()
    {
        InitializeComponent();
        Loaded += StacksPage_Loaded;
    }

    private async void StacksPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        await mw.EnsureCatalogLoadedAsync();

        _allBundles = mw.Bundles.ToList();
        HeaderCountBadge.Text = $"{_allBundles.Count} Presets";
        ApplyFilter();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? "";

        IEnumerable<ToolBundle> filtered = _allBundles;

        if (!string.IsNullOrEmpty(query))
        {
            filtered = _allBundles.Where(b =>
                b.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                b.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                b.Tools.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var list = filtered.ToList();
        StacksList.ItemsSource = list;
        StatusText.Text = $"{list.Count} of {_allBundles.Count} stacks shown";
    }

    private void SelectStack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string bundleId)
        {
            App.MainWindowInstance?.NavigateToCatalogWithBundle(bundleId);
        }
    }

    private void BrowseCatalog_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Catalog");
    }
}
