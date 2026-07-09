using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

/// <summary>
/// A category of tools shown under a single header in the catalog. Deriving
/// from <see cref="List{T}"/> lets a grouped <see cref="CollectionViewSource"/>
/// treat the group itself as the item collection, while <see cref="Key"/> and
/// the inherited <c>Count</c> drive the header UI.
/// </summary>
public sealed class ToolCategoryGroup : List<ToolDefinition>
{
    public ToolCategoryGroup(string key, IEnumerable<ToolDefinition> items) : base(items)
        => Key = key;

    public string Key { get; }
}

public sealed partial class CatalogPage : Page
{
    private readonly ObservableCollection<ToolCategoryGroup> _groups = new();
    private readonly CollectionViewSource _groupedView;
    private bool _busy;

    public CatalogPage()
    {
        InitializeComponent();

        _groupedView = new CollectionViewSource
        {
            IsSourceGrouped = true,
            Source = _groups
        };
        ToolsList.ItemsSource = _groupedView.View;

        Loaded += CatalogPage_Loaded;
    }

    private async void CatalogPage_Loaded(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        if (mw.Tools.Count == 0)
        {
            SetBusy(true, "Loading catalog...");
            try
            {
                await mw.EnsureCatalogLoadedAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load catalog: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        ApplyFilter();
        StatusText.Text = $"{mw.Tools.Count} tools available";
    }

    private void ApplyFilter()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var query = SearchBox.Text?.Trim() ?? "";

        bool Matches(ToolDefinition tool) =>
            string.IsNullOrEmpty(query)
            || tool.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || tool.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || tool.Description.Contains(query, StringComparison.OrdinalIgnoreCase);

        var grouped = mw.Tools
            .Where(Matches)
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .GroupBy(t => t.Category)
            .Select(g => new ToolCategoryGroup(g.Key, g));

        _groups.Clear();
        foreach (var group in grouped)
            _groups.Add(group);
    }

    /// <summary>Every tool currently visible across all category groups.</summary>
    private IEnumerable<ToolDefinition> VisibleTools => _groups.SelectMany(g => g);

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ApplyFilter();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in VisibleTools) item.IsSelected = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in VisibleTools) item.IsSelected = false;
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || _busy) return;

        var selected = VisibleTools.Where(t => t.IsSelected && t.Status != ToolStatus.Downloaded).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "Select at least one tool to download.";
            return;
        }

        SetBusy(true, $"Downloading {selected.Count} tool(s)...");

        // Add to download queue for the DownloadsPage to track
        foreach (var tool in selected)
        {
            if (!mw.DownloadQueue.Contains(tool))
                mw.DownloadQueue.Add(tool);
        }

        try
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            await mw.DownloadSvc.DownloadBatchAsync(selected, dlFolder, maxConcurrency: 3);

            var succeeded = selected.Count(t => t.Status == ToolStatus.Downloaded);
            var failed = selected.Count(t => t.Status == ToolStatus.Failed);
            StatusText.Text = $"Done - {succeeded} succeeded, {failed} failed";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Download error: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = !busy;
        SelectAllButton.IsEnabled = !busy;
        ClearButton.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }
}
