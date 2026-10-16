using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

/// <summary>
/// In-app editor for user-defined catalog entries (roadmap: "In-app catalog
/// editor with signature workflow"). Custom tools are validated, stored locally,
/// and merged into the live catalog so they behave exactly like shipped tools.
/// </summary>
public sealed partial class CatalogEditorPage : Page, IRefreshablePage
{
    private string? _editingId;

    public CatalogEditorPage()
    {
        InitializeComponent();
        Loaded += CatalogEditorPage_Loaded;
    }

    private void CatalogEditorPage_Loaded(object sender, RoutedEventArgs e) => RefreshList();

    void IRefreshablePage.RefreshView() => RefreshList();

    private void RefreshList()
    {
        var tools = CustomCatalogService.LoadCustomTools();
        CustomToolsList.ItemsSource = tools;
        bool empty = tools.Count == 0;
        CustomToolsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        CustomToolsEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Form ─────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var tool = BuildFromForm();
        var (ok, error) = CustomCatalogService.Validate(tool);
        if (!ok)
        {
            ShowNotice(error ?? "The tool definition is invalid.", InfoBarSeverity.Error);
            return;
        }

        CustomCatalogService.Save(tool);
        _editingId = tool.Id;
        DeleteButton.IsEnabled = true;
        RefreshList();
        _ = ReloadCatalogAsync();
        ShowNotice($"Saved custom tool '{tool.Name}'. It is now part of your catalog.", InfoBarSeverity.Success);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        DeleteButton.IsEnabled = false;
        IdBox.Text = string.Empty;
        NameBox.Text = string.Empty;
        CategoryBox.Text = string.Empty;
        VersionBox.Text = string.Empty;
        LaunchArgsBox.Text = string.Empty;
        FileNameBox.Text = string.Empty;
        HomepageBox.Text = string.Empty;
        DescriptionBox.Text = string.Empty;
        DownloadUrlBox.Text = string.Empty;
        Sha256Box.Text = string.Empty;
        LogoUrlBox.Text = string.Empty;
        KindBox.SelectedIndex = 0;
        CustomToolsList.SelectedItem = null;
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var id = _editingId;
        if (string.IsNullOrWhiteSpace(id))
        {
            ShowNotice("Select a custom tool to delete.", InfoBarSeverity.Informational);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = $"Delete custom tool '{id}'?",
            Content = "The entry is removed from your catalog. Any installed files are left in place.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        CustomCatalogService.Delete(id);
        Clear_Click(sender, e);
        RefreshList();
        await ReloadCatalogAsync();
        ShowNotice($"Deleted custom tool '{id}'.", InfoBarSeverity.Informational);
    }

    private void CustomToolsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomToolsList.SelectedItem is not ToolDefinition tool) return;

        _editingId = tool.Id;
        DeleteButton.IsEnabled = true;

        IdBox.Text = tool.Id;
        NameBox.Text = tool.Name;
        CategoryBox.Text = tool.Category;
        VersionBox.Text = tool.Version;
        DescriptionBox.Text = tool.Description;
        DownloadUrlBox.Text = tool.DownloadUrl;
        FileNameBox.Text = tool.FileName;
        Sha256Box.Text = tool.Sha256;
        HomepageBox.Text = tool.Homepage;
        LogoUrlBox.Text = tool.LogoUrl;
        LaunchArgsBox.Text = tool.LaunchArgs;

        foreach (ComboBoxItem item in KindBox.Items)
        {
            if (string.Equals(item.Tag as string, tool.Kind.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                KindBox.SelectedItem = item;
                break;
            }
        }
    }

    private ToolDefinition BuildFromForm()
    {
        var kind = (KindBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Installer";
        return new ToolDefinition
        {
            Id = IdBox.Text.Trim(),
            Name = NameBox.Text.Trim(),
            Category = CategoryBox.Text.Trim(),
            Description = DescriptionBox.Text.Trim(),
            Version = VersionBox.Text.Trim(),
            KindRaw = kind,
            DownloadUrl = DownloadUrlBox.Text.Trim(),
            FileName = FileNameBox.Text.Trim(),
            Sha256 = Sha256Box.Text.Trim(),
            Homepage = HomepageBox.Text.Trim(),
            LogoUrl = LogoUrlBox.Text.Trim(),
            LaunchArgs = LaunchArgsBox.Text.Trim()
        };
    }

    // ── Import / Export ──────────────────────────────────────────────────

    private async void ImportFeed_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        Initialize(picker);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var (imported, errors) = CustomCatalogService.ImportFeed(await File.ReadAllTextAsync(file.Path));
        RefreshList();
        await ReloadCatalogAsync();

        if (errors.Count > 0)
            ShowNotice($"Imported {imported} tool(s); {errors.Count} skipped: {string.Join("; ", errors.Take(3))}",
                imported > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
        else
            ShowNotice($"Imported {imported} custom tool(s).", InfoBarSeverity.Success);
    }

    private async void ExportFeed_Click(object sender, RoutedEventArgs e)
    {
        var json = CustomCatalogService.ExportFeedJson();
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
        {
            ShowNotice("There are no custom tools to export.", InfoBarSeverity.Informational);
            return;
        }

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = "custom-catalog",
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("JSON catalog feed", new[] { ".json" });
        Initialize(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await File.WriteAllTextAsync(file.Path, json);
        ShowNotice($"Custom catalog feed exported to {file.Path}", InfoBarSeverity.Success);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void Initialize(object picker)
    {
        if (App.MainWindowInstance is null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private async Task ReloadCatalogAsync()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        try
        {
            var tools = await mw.CatalogSvc.LoadCatalogAsync();
            mw.Tools.Clear();
            foreach (var t in tools)
            {
                t.IsFavorite = FavoritesService.IsFavorite(t.Id);
                mw.Tools.Add(t);
            }
        }
        catch
        {
            // Reloading is best-effort; the editor state is already saved.
        }
    }

    private void ShowNotice(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        NoticeInfoBar.Message = message;
        NoticeInfoBar.Severity = severity;
        NoticeInfoBar.IsOpen = true;
    }
}
