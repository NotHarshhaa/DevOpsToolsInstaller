using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

/// <summary>
/// A category of tools shown under a single header in the catalog.
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
    private CancellationTokenSource? _downloadCts;
    private string _selectedCategory = "All";

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

        bool Matches(ToolDefinition tool)
        {
            var textMatch = string.IsNullOrEmpty(query)
                || tool.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || tool.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || tool.Description.Contains(query, StringComparison.OrdinalIgnoreCase);

            var categoryMatch = _selectedCategory == "All"
                || string.Equals(tool.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase);

            return textMatch && categoryMatch;
        }

        var grouped = mw.Tools
            .Where(Matches)
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .GroupBy(t => t.Category)
            .Select(g => new ToolCategoryGroup(g.Key, g));

        _groups.Clear();
        foreach (var group in grouped)
            _groups.Add(group);

        var count = VisibleTools.Count();
        StatusText.Text = $"{count} of {mw.Tools.Count} tools shown";
    }

    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedBtn) return;
        var tag = clickedBtn.Tag as string ?? "All";
        _selectedCategory = tag;

        // Visual update on chips
        foreach (var child in CategoryChipsPanel.Children)
        {
            if (child is Button btn)
            {
                var isSelected = (btn.Tag as string) == tag;
                btn.Style = (Style)Application.Current.Resources[isSelected ? "SelectedCategoryChipStyle" : "CategoryChipStyle"];
                btn.CornerRadius = new CornerRadius(14);
            }
        }

        ApplyFilter();
    }

    /// <summary>Every tool currently visible across all category groups.</summary>
    private IEnumerable<ToolDefinition> VisibleTools => _groups.SelectMany(g => g);

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ApplyFilter();
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string tag) return;
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        var targetIds = tag switch
        {
            "k8s"   => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kubectl", "helm", "k9s", "minikube", "kind", "skaffold" },
            "cloud" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "awscli", "azure-cli", "gcloud-cli", "doctl", "oci-cli" },
            "iac"   => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "terraform", "opentofu", "terragrunt", "pulumi", "packer", "ansible" },
            "sec"   => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "trivy", "sops", "gitleaks" },
            "cli"   => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "windows-terminal", "vscode", "lazygit", "lazydocker", "jq", "yq" },
            _       => new HashSet<string>()
        };

        foreach (var tool in mw.Tools)
        {
            tool.IsSelected = targetIds.Contains(tool.Id);
        }

        var selectedCount = mw.Tools.Count(t => t.IsSelected);
        StatusText.Text = $"Selected {selectedCount} tools for preset: {item.Text}";
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

        var selected = VisibleTools
            .Where(t => t.IsSelected && t.Status != ToolStatus.Downloaded && t.Status != ToolStatus.Downloading)
            .ToList();

        if (selected.Count == 0)
        {
            var anySelected = VisibleTools.Any(t => t.IsSelected);
            StatusText.Text = anySelected
                ? "Selected tool(s) are already downloaded or currently downloading."
                : "Select at least one tool to download.";
            return;
        }

        _downloadCts = new CancellationTokenSource();
        CancelButton.Visibility = Visibility.Visible;
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
            await mw.DownloadSvc.DownloadBatchAsync(selected, dlFolder, maxConcurrency: 3, ct: _downloadCts.Token);

            var succeeded = selected.Count(t => t.Status == ToolStatus.Downloaded);
            var failed = selected.Count(t => t.Status == ToolStatus.Failed);
            StatusText.Text = $"Done - {succeeded} succeeded, {failed} failed";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Downloads cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Download error: {ex.Message}";
        }
        finally
        {
            CancelButton.Visibility = Visibility.Collapsed;
            _downloadCts?.Dispose();
            _downloadCts = null;
            SetBusy(false);
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        StatusText.Text = "Cancelling downloads...";
    }

    private async void ToolDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        var panel = new StackPanel { Spacing = 14, MaxWidth = 480 };

        var headerGrid = new Grid { ColumnSpacing = 14 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(10),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"]
        };
        var glyph = new FontIcon
        {
            Glyph = tool.IconGlyph,
            FontSize = 20,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = glyph;
        Grid.SetColumn(iconBorder, 0);
        headerGrid.Children.Add(iconBorder);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        titleStack.Children.Add(new TextBlock { Text = tool.NameWithVersion, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });
        titleStack.Children.Add(new TextBlock { Text = $"{tool.Category} • {tool.KindLabel}", Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], FontSize = 12 });
        Grid.SetColumn(titleStack, 1);
        headerGrid.Children.Add(titleStack);
        panel.Children.Add(headerGrid);

        panel.Children.Add(new TextBlock { Text = tool.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

        var metaBorder = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14)
        };
        var metaStack = new StackPanel { Spacing = 6 };
        metaStack.Children.Add(new TextBlock { Text = $"File Name: {tool.FileName}", FontSize = 12 });
        metaStack.Children.Add(new TextBlock { Text = $"Deployment: {tool.ActionLabel}", FontSize = 12 });
        metaStack.Children.Add(new TextBlock { Text = $"Current Status: {tool.StatusText}", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(tool.Sha256))
        {
            metaStack.Children.Add(new TextBlock { Text = $"SHA256: {tool.Sha256}", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        }
        metaBorder.Child = metaStack;
        panel.Children.Add(metaBorder);

        var dialog = new ContentDialog
        {
            Title = "Tool Specifications",
            Content = panel,
            PrimaryButtonText = !string.IsNullOrWhiteSpace(tool.Homepage) ? "Open Documentation" : "",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tool.Homepage))
        {
            LauncherService.OpenUrl(tool.Homepage);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        DownloadButton.IsEnabled = !busy;
        SelectAllButton.IsEnabled = !busy;
        ClearButton.IsEnabled = !busy;
        PresetsButton.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }

    private void LogoImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image img && img.Parent is Grid parent)
        {
            img.Visibility = Visibility.Collapsed;
            foreach (var child in parent.Children)
            {
                if (child is FontIcon icon)
                {
                    icon.Visibility = Visibility.Visible;
                }
            }
        }
    }
}
