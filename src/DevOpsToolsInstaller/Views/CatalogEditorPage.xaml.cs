using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
    /// <summary>Shared client for the optional "compute SHA-256 from URL" probe.</summary>
    private static readonly HttpClient ProbeHttp = CreateProbeClient();

    /// <summary>Matches the id shape enforced by <see cref="CustomCatalogService.Validate"/>.</summary>
    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9\-\._]{1,63}$", RegexOptions.Compiled);

    /// <summary>Matches a full SHA-256 digest.</summary>
    private static readonly Regex Sha256Pattern = new(@"^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    private string? _editingId;
    private bool _fileNameTouched;
    private bool _suppressFileNameEvents;
    private bool _computingSha;

    public CatalogEditorPage()
    {
        InitializeComponent();
        Loaded += CatalogEditorPage_Loaded;
    }

    private void CatalogEditorPage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshList();
        PopulateCategories();
        UpdateFileNamePlaceholder();
        UpdateSuggestedNameHint();
        UpdatePreview();
    }

    void IRefreshablePage.RefreshView()
    {
        RefreshList();
        PopulateCategories();
        UpdatePreview();
    }

    private void RefreshList()
    {
        var tools = CustomCatalogService.LoadCustomTools();
        CustomToolsList.ItemsSource = tools;
        bool empty = tools.Count == 0;
        CustomToolsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        CustomToolsEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Fills the category drop-down with every category already in use — both the
    /// shipped catalog and the user's own entries — while keeping the field
    /// editable so a brand-new category can still be typed.
    /// </summary>
    private void PopulateCategories()
    {
        var categories = new System.Collections.Generic.SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        var mw = App.MainWindowInstance;
        if (mw is not null)
        {
            foreach (var t in mw.Tools)
            {
                if (!string.IsNullOrWhiteSpace(t.Category)) categories.Add(t.Category.Trim());
            }
        }

        foreach (var t in CustomCatalogService.LoadCustomTools())
        {
            if (!string.IsNullOrWhiteSpace(t.Category)) categories.Add(t.Category.Trim());
        }

        var current = CategoryBox.Text;
        CategoryBox.ItemsSource = categories.ToList();
        if (!string.IsNullOrEmpty(current)) CategoryBox.Text = current;
    }

    // ── Form ─────────────────────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateAll())
        {
            ShowNotice("Please fix the highlighted fields before saving.", InfoBarSeverity.Error);
            return;
        }

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
        PopulateCategories();
        _ = ReloadCatalogAsync();
        ShowNotice($"Saved custom tool '{tool.Name}'. It is now part of your catalog.", InfoBarSeverity.Success);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _editingId = null;
        _fileNameTouched = false;
        DeleteButton.IsEnabled = false;

        _suppressFileNameEvents = true;
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
        _suppressFileNameEvents = false;

        ClearAllErrors();
        UpdateFileNamePlaceholder();
        UpdateSuggestedNameHint();
        UpdatePreview();
        CustomToolsList.SelectedItem = null;
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileNameBox is null) return;

        UpdateFileNamePlaceholder();
        AutoSuggestFileName();
        UpdatePreview();
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
        PopulateCategories();
        await ReloadCatalogAsync();
        ShowNotice($"Deleted custom tool '{id}'.", InfoBarSeverity.Informational);
    }

    private void CustomToolsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomToolsList.SelectedItem is not ToolDefinition tool) return;

        _editingId = tool.Id;
        _fileNameTouched = true;
        DeleteButton.IsEnabled = true;

        _suppressFileNameEvents = true;
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
        _suppressFileNameEvents = false;

        foreach (ComboBoxItem item in KindBox.Items)
        {
            if (string.Equals(item.Tag as string, tool.Kind.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                KindBox.SelectedItem = item;
                break;
            }
        }

        UpdateFileNamePlaceholder();
        UpdateSuggestedNameHint();
        UpdatePreview();
        ClearAllErrors();
    }

    /// <summary>
    /// Keeps the "File name" placeholder in step with the selected artifact
    /// kind, so the hint always shows a realistic example for that kind.
    /// </summary>
    private void UpdateFileNamePlaceholder()
    {
        if (FileNameBox is null) return;

        FileNameBox.PlaceholderText = SelectedKind() switch
        {
            "Archive" => "my-tool-1.0.0.zip",
            "Binary"  => "my-tool.exe",
            "Script"  => "my-tool.ps1",
            _         => "my-tool-1.0.0.exe", // Installer (.exe / .msi)
        };
    }

    private string SelectedKind()
        => (KindBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "Installer";

    private ToolDefinition BuildFromForm()
    {
        return new ToolDefinition
        {
            Id = IdBox.Text.Trim(),
            Name = NameBox.Text.Trim(),
            Category = CategoryBox.Text.Trim(),
            Description = DescriptionBox.Text.Trim(),
            Version = VersionBox.Text.Trim(),
            KindRaw = SelectedKind(),
            DownloadUrl = DownloadUrlBox.Text.Trim(),
            FileName = FileNameBox.Text.Trim(),
            Sha256 = Sha256Box.Text.Trim(),
            Homepage = HomepageBox.Text.Trim(),
            LogoUrl = LogoUrlBox.Text.Trim(),
            LaunchArgs = LaunchArgsBox.Text.Trim()
        };
    }

    // ── File-name auto-suggestion ────────────────────────────────────────

    private void FileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFileNameEvents) return;

        // Once the user types their own name, stop overwriting it.
        _fileNameTouched = !string.IsNullOrWhiteSpace(FileNameBox.Text);
        ValidateFileName();
        UpdateSuggestedNameHint();
        UpdatePreview();
    }

    /// <summary>
    /// Derives a sensible file name from the download URL's last path segment,
    /// appending the artifact kind's default extension when the URL has none.
    /// </summary>
    private string SuggestFileName()
    {
        var url = DownloadUrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return string.Empty;

        var name = Path.GetFileName(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(name)) name = "download";

        if (string.IsNullOrEmpty(Path.GetExtension(name)))
        {
            name += SelectedKind() switch
            {
                "Archive" => ".zip",
                "Binary"  => ".exe",
                "Script"  => ".ps1",
                _         => ".exe"
            };
        }

        return name;
    }

    /// <summary>
    /// Fills the file-name box from the URL while the user has not typed their
    /// own value; otherwise it just refreshes the "use suggested" affordance.
    /// </summary>
    private void AutoSuggestFileName()
    {
        if (_fileNameTouched)
        {
            UpdateSuggestedNameHint();
            return;
        }

        var suggested = SuggestFileName();
        if (!string.IsNullOrEmpty(suggested))
        {
            _suppressFileNameEvents = true;
            FileNameBox.Text = suggested;
            _suppressFileNameEvents = false;
            ValidateFileName();
        }

        UpdateSuggestedNameHint();
    }

    private void UpdateSuggestedNameHint()
    {
        if (UseSuggestedNameLink is null) return;

        var suggested = SuggestFileName();
        if (string.IsNullOrEmpty(suggested) ||
            string.Equals(suggested, FileNameBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            UseSuggestedNameLink.Visibility = Visibility.Collapsed;
            return;
        }

        UseSuggestedNameLink.Content = $"Use suggested name: {suggested}";
        UseSuggestedNameLink.Tag = suggested;
        UseSuggestedNameLink.Visibility = Visibility.Visible;
    }

    private void UseSuggestedName_Click(object sender, RoutedEventArgs e)
    {
        if (UseSuggestedNameLink.Tag is not string suggested || string.IsNullOrEmpty(suggested)) return;

        _suppressFileNameEvents = true;
        FileNameBox.Text = suggested;
        _suppressFileNameEvents = false;
        _fileNameTouched = true;

        ValidateFileName();
        UpdateSuggestedNameHint();
        UpdatePreview();
    }

    // ── Live preview ─────────────────────────────────────────────────────

    /// <summary>
    /// Renders the current form as it would appear on a catalog card, so the
    /// user can sanity-check the name, vendor, category and version before saving.
    /// </summary>
    private void UpdatePreview()
    {
        if (!IsLoaded || PreviewName is null) return;

        var tool = BuildFromForm();

        PreviewName.Text = string.IsNullOrWhiteSpace(tool.Name) ? "Tool name" : tool.Name;

        var vendor = tool.VendorLabel;
        PreviewVendor.Text = vendor == "Unknown source" ? "Custom source" : vendor;

        PreviewDescription.Text = string.IsNullOrWhiteSpace(tool.Description)
            ? "What does this tool do?"
            : tool.Description;

        PreviewCategory.Text = string.IsNullOrWhiteSpace(tool.Category) ? "Uncategorised" : tool.Category;
        PreviewKind.Text = tool.KindLabel;
        PreviewVersion.Text = tool.DisplayVersionWithV;

        var logo = ToolLogoService.GetLogo(tool.LogoUrl);
        if (logo is not null)
        {
            PreviewLogo.Source = logo;
            PreviewLogo.Visibility = Visibility.Visible;
            PreviewGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewLogo.Source = null;
            PreviewLogo.Visibility = Visibility.Collapsed;
            PreviewGlyph.Visibility = Visibility.Visible;
        }
    }

    private void PreviewLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        PreviewLogo.Visibility = Visibility.Collapsed;
        PreviewGlyph.Visibility = Visibility.Visible;
    }

    private void IdBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateId();
        UpdatePreview();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateName();
        UpdatePreview();
    }

    private void VersionBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void HomepageBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void LogoUrlBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void DownloadUrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ValidateUrl();
        AutoSuggestFileName();
        UpdatePreview();
    }

    private void Sha256Box_TextChanged(object sender, TextChangedEventArgs e) => ValidateSha();

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ValidateCategory();
        UpdatePreview();
    }

    private void CategoryBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ValidateCategory();
        UpdatePreview();
    }

    // ── Inline validation ────────────────────────────────────────────────

    private static void SetError(TextBlock? block, string? message)
    {
        if (block is null) return;

        if (string.IsNullOrEmpty(message))
        {
            block.Text = string.Empty;
            block.Visibility = Visibility.Collapsed;
        }
        else
        {
            block.Text = message;
            block.Visibility = Visibility.Visible;
        }
    }

    private void ClearAllErrors()
    {
        SetError(IdError, null);
        SetError(NameError, null);
        SetError(CategoryError, null);
        SetError(FileNameError, null);
        SetError(UrlError, null);
        SetError(ShaError, null);
    }

    private bool ValidateId()
    {
        var id = IdBox.Text.Trim();
        if (string.IsNullOrEmpty(id))
        {
            SetError(IdError, "An id is required.");
            return false;
        }
        if (!IdPattern.IsMatch(id))
        {
            SetError(IdError, "Use lower-case letters, digits, '-', '.' or '_' (2–64 characters).");
            return false;
        }
        SetError(IdError, null);
        return true;
    }

    private bool ValidateName()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            SetError(NameError, "A display name is required.");
            return false;
        }
        SetError(NameError, null);
        return true;
    }

    private bool ValidateCategory()
    {
        if (string.IsNullOrWhiteSpace(CategoryBox.Text))
        {
            SetError(CategoryError, "A category is required.");
            return false;
        }
        SetError(CategoryError, null);
        return true;
    }

    private bool ValidateFileName()
    {
        if (string.IsNullOrWhiteSpace(FileNameBox.Text))
        {
            SetError(FileNameError, "A file name is required.");
            return false;
        }
        SetError(FileNameError, null);
        return true;
    }

    private bool ValidateUrl()
    {
        var url = DownloadUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            SetError(UrlError, "A download URL is required.");
            return false;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            SetError(UrlError, "That is not a valid absolute URL.");
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            SetError(UrlError, "Only HTTPS download URLs are allowed (plain-text HTTP is refused).");
            return false;
        }
        SetError(UrlError, null);
        return true;
    }

    private bool ValidateSha()
    {
        var sha = Sha256Box.Text.Trim();
        if (string.IsNullOrEmpty(sha))
        {
            SetError(ShaError, null); // optional
            return true;
        }
        if (!Sha256Pattern.IsMatch(sha))
        {
            SetError(ShaError, "The SHA-256 must be exactly 64 hex characters.");
            return false;
        }
        SetError(ShaError, null);
        return true;
    }

    /// <summary>Validates every field (without short-circuiting) and reports the result.</summary>
    private bool ValidateAll()
    {
        var ok = true;
        ok &= ValidateId();
        ok &= ValidateName();
        ok &= ValidateCategory();
        ok &= ValidateFileName();
        ok &= ValidateUrl();
        ok &= ValidateSha();
        return ok;
    }

    // ── Compute SHA-256 from URL ─────────────────────────────────────────

    /// <summary>
    /// Optional convenience: downloads the artifact to a temporary file (HTTPS
    /// only), hashes it, and fills in the SHA-256 box. This proves only that the
    /// bytes are unchanged on later downloads — it does NOT prove the file is
    /// authentic or safe. The Authenticode signature check is a separate,
    /// mandatory step at install time and is never skipped by this action.
    /// </summary>
    private async void ComputeSha_Click(object sender, RoutedEventArgs e)
    {
        if (_computingSha) return;

        if (!ValidateUrl())
        {
            ShowNotice("Enter a valid HTTPS download URL before computing the SHA-256.", InfoBarSeverity.Error);
            return;
        }

        var url = DownloadUrlBox.Text.Trim();
        _computingSha = true;
        ComputeShaButton.IsEnabled = false;
        ComputeShaProgress.IsActive = true;
        ComputeShaProgress.Visibility = Visibility.Visible;
        ComputeShaStatus.Text = "Downloading to a temporary file…";

        var tempDir = Path.Combine(Path.GetTempPath(), "DevOpsToolsInstaller", "sha-probe");
        var extension = Path.GetExtension(SuggestFileName());
        if (string.IsNullOrEmpty(extension)) extension = ".bin";
        var tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + extension);

        try
        {
            Directory.CreateDirectory(tempDir);

            using (var response = await ProbeHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var httpStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(tempPath);
                await httpStream.CopyToAsync(fileStream);
            }

            ComputeShaStatus.Text = "Hashing…";
            string hash;
            await using (var hashStream = File.OpenRead(tempPath))
            {
                var hashBytes = await SHA256.HashDataAsync(hashStream);
                hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            Sha256Box.Text = hash;

            // Informational only: report what *this* download's signature looks
            // like, to reinforce that authenticity is still decided by Authenticode.
            var signature = AuthenticodeService.VerifyFile(tempPath);
            var size = new FileInfo(tempPath).Length;

            ShowNotice(
                $"Computed SHA-256 {hash} from a {CustomStacksService.FormatBytes(size)} download. " +
                "This proves only that the file is unchanged on future downloads — it does NOT prove the file is " +
                $"authentic or safe. The Authenticode signature check still runs at install time and is never skipped. " +
                $"This download: {signature.Summary}",
                InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not compute the SHA-256: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort temp cleanup */ }

            _computingSha = false;
            ComputeShaButton.IsEnabled = true;
            ComputeShaProgress.IsActive = false;
            ComputeShaProgress.Visibility = Visibility.Collapsed;
            ComputeShaStatus.Text = string.Empty;
        }
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
        PopulateCategories();
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

    private static HttpClient CreateProbeClient()
    {
        var client = new HttpClient(HttpConfigService.CreateHandler())
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DevOpsToolsInstaller/{AppUpdaterService.CurrentVersion} (Windows NT 10.0; Win64; x64)");
        return client;
    }

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
