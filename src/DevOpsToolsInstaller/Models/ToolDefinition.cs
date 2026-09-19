using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DevOpsToolsInstaller.Models;

public enum ToolStatus
{
    NotDownloaded,
    Downloading,
    Downloaded,
    Failed
}

/// <summary>
/// Describes what a downloaded artifact is, which determines the action the
/// app takes after download (see <c>ArtifactService</c>).
/// </summary>
public enum ArtifactKind
{
    /// <summary>Self-contained setup (.exe/.msi) that installs itself.</summary>
    Installer,
    /// <summary>Compressed archive (.zip) that must be extracted.</summary>
    Archive,
    /// <summary>Standalone executable to be placed on the user's PATH.</summary>
    Binary,
    /// <summary>Script (.ps1 etc.) the user should review before running.</summary>
    Script
}

public sealed class ToolDefinition : INotifyPropertyChanged
{
    // ── JSON-serialized catalog properties ───────────────────────────────

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("iconGlyph")]
    public string IconGlyph { get; set; } = "\uE74C";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("launchArgs")]
    public string LaunchArgs { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string? KindRaw { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("previousVersions")]
    public System.Collections.Generic.List<string>? PreviousVersions { get; set; }

    [JsonPropertyName("versionTemplate")]
    public string? VersionTemplate { get; set; }

    [JsonPropertyName("fileNameTemplate")]
    public string? FileNameTemplate { get; set; }

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    /// <summary>
    /// Optional URL to the tool's brand logo (SVG or raster). When present the
    /// catalog renders it in place of the <see cref="IconGlyph"/>. If the image
    /// fails to load (e.g. offline) the glyph is shown as a graceful fallback.
    /// </summary>
    [JsonPropertyName("logoUrl")]
    public string LogoUrl { get; set; } = string.Empty;

    // ── Runtime-only state (not from JSON) ───────────────────────────────

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.001)
            {
                _progress = value;
                OnPropertyChanged();
            }
        }
    }

    private ToolStatus _status = ToolStatus.NotDownloaded;
    public ToolStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDownloaded));
                UpdateStatusText();
            }
        }
    }

    private string _statusText = "Not downloaded";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Runtime flag indicating the app has something it can remove for this
    /// tool (an extracted folder, a copied binary, or a matching Windows
    /// uninstall entry). Populated by <c>UninstallService.IsInstalled</c>;
    /// never persisted to the catalog.
    /// </summary>
    private bool _isInstalled;
    [JsonIgnore]
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Whether the user has starred/favorited this tool.
    /// Persisted via <c>FavoritesService</c>.
    /// </summary>
    private bool _isFavorite;
    [JsonIgnore]
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite != value)
            {
                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteGlyph));
            }
        }
    }

    /// <summary>Star icon glyph: filled when favorited, outlined otherwise.</summary>
    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    private string? _detectedVersion;
    [JsonIgnore]
    public string? DetectedVersion
    {
        get => _detectedVersion;
        set
        {
            if (_detectedVersion != value)
            {
                _detectedVersion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUpdate));
                OnPropertyChanged(nameof(DisplayDetectedVersion));
            }
        }
    }

    [JsonIgnore]
    public string DisplayDetectedVersion =>
        string.IsNullOrWhiteSpace(DetectedVersion) ? "Unknown version" : $"v{DetectedVersion}";

    [JsonIgnore]
    public bool HasUpdate
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DetectedVersion) || string.IsNullOrWhiteSpace(Version))
                return false;

            var cleanDet = CleanVersion(DetectedVersion);
            var cleanCat = CleanVersion(Version);

            if (System.Version.TryParse(cleanDet, out var vDet) &&
                System.Version.TryParse(cleanCat, out var vCat))
            {
                return vCat > vDet;
            }

            return false;
        }
    }

    private static string CleanVersion(string v)
    {
        var match = System.Text.RegularExpressions.Regex.Match(v, @"\b(\d+(\.\d+){1,3})\b");
        return match.Success ? match.Groups[1].Value : v.Trim().TrimStart('v', 'V');
    }

    private string _healthStatus = "Not Tested";
    [JsonIgnore]
    public string HealthStatus
    {
        get => _healthStatus;
        set
        {
            if (_healthStatus != value)
            {
                _healthStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHealthSuccess));
            }
        }
    }

    private string _healthOutput = string.Empty;
    [JsonIgnore]
    public string HealthOutput
    {
        get => _healthOutput;
        set
        {
            if (_healthOutput != value)
            {
                _healthOutput = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
    public bool IsHealthSuccess => HealthStatus == "Healthy";

    private Services.AuthenticodeResult? _signatureResult;
    [JsonIgnore]
    public Services.AuthenticodeResult? SignatureResult
    {
        get => _signatureResult;
        set
        {
            if (_signatureResult != value)
            {
                _signatureResult = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SignatureSummary));
                OnPropertyChanged(nameof(HasValidSignature));
            }
        }
    }

    [JsonIgnore]
    public string SignatureSummary =>
        SignatureResult?.Summary ?? (Kind == ArtifactKind.Archive ? "Archive (no signature check)" : "Not verified");

    [JsonIgnore]
    public bool HasValidSignature => SignatureResult?.IsValid == true;

    // ── Computed helpers ─────────────────────────────────────────────────

    public string DisplayName => $"{Name}";

    public bool IsDownloaded => Status == ToolStatus.Downloaded;

    /// <summary>
    /// The artifact kind. Uses the catalog's explicit "kind" when present,
    /// otherwise infers a sensible default from the file extension so older
    /// catalogs (without the field) still behave reasonably.
    /// </summary>
    [JsonIgnore]
    public ArtifactKind Kind
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(KindRaw) &&
                Enum.TryParse<ArtifactKind>(KindRaw, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            var ext = System.IO.Path.GetExtension(FileName).ToLowerInvariant();
            return ext switch
            {
                ".zip" or ".7z" or ".tar" or ".gz" or ".tgz" => ArtifactKind.Archive,
                ".ps1" or ".sh" or ".bat" or ".cmd"           => ArtifactKind.Script,
                ".msi" or ".msix" or ".msixbundle" or ".appx" => ArtifactKind.Installer,
                _                                              => ArtifactKind.Installer
            };
        }
    }

    /// <summary>Short label describing the kind, for a catalog badge.</summary>
    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        ArtifactKind.Installer => "Installer",
        ArtifactKind.Archive   => "Archive",
        ArtifactKind.Binary    => "Binary",
        ArtifactKind.Script    => "Script",
        _                      => "Installer"
    };

    /// <summary>
    /// The label shown on the post-download action button, per artifact kind.
    /// </summary>
    [JsonIgnore]
    public string ActionLabel => Kind switch
    {
        ArtifactKind.Installer => "Install",
        ArtifactKind.Archive   => "Extract",
        ArtifactKind.Binary    => "Add to Tools",
        ArtifactKind.Script    => "Open Folder",
        _                      => "Install"
    };

    /// <summary>
    /// Label for the removal button, matched to how the tool was actioned.
    /// Installers get their vendor uninstaller; archives/binaries are just
    /// deleted from the Tools folder.
    /// </summary>
    [JsonIgnore]
    public string UninstallLabel => Kind switch
    {
        ArtifactKind.Installer => "Uninstall",
        ArtifactKind.Archive   => "Remove",
        ArtifactKind.Binary    => "Remove",
        ArtifactKind.Script    => "Delete",
        _                      => "Remove"
    };

    /// <summary>
    /// Whether a removal action is meaningful for this kind. Scripts are never
    /// installed, so there is nothing to uninstall for them.
    /// </summary>
    [JsonIgnore]
    public bool SupportsUninstall => Kind != ArtifactKind.Script;

    private string? _originalVersion;
    private string? _originalDownloadUrl;
    private string? _originalFileName;

    private string? _selectedVersion;
    [JsonIgnore]
    public string SelectedVersion
    {
        get => _selectedVersion ?? Version;
        set
        {
            if (_selectedVersion != value)
            {
                _selectedVersion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayVersion));
                OnPropertyChanged(nameof(NameWithVersion));
                OnPropertyChanged(nameof(IsPreviousVersionSelected));
            }
        }
    }

    [JsonIgnore]
    public bool IsPreviousVersionSelected =>
        !string.IsNullOrWhiteSpace(_selectedVersion) &&
        !string.Equals(_selectedVersion, _originalVersion ?? Version, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayVersion =>
        !string.IsNullOrWhiteSpace(SelectedVersion) ? $"v{SelectedVersion}" : "Latest";

    [JsonIgnore]
    public System.Collections.Generic.List<string> AllAvailableVersions
    {
        get
        {
            var list = new System.Collections.Generic.List<string>();
            var defaultVer = _originalVersion ?? Version;
            if (!string.IsNullOrWhiteSpace(defaultVer))
            {
                list.Add($"{defaultVer} (Latest)");
            }
            if (PreviousVersions != null)
            {
                foreach (var pv in PreviousVersions)
                {
                    if (!string.IsNullOrWhiteSpace(pv) && !list.Contains(pv) && pv != defaultVer)
                    {
                        list.Add(pv);
                    }
                }
            }
            return list;
        }
    }

    /// <summary>
    /// Updates the tool to target a specific version, recalculating the download URL and file name.
    /// </summary>
    public void SetVersion(string newVersion)
    {
        if (string.IsNullOrWhiteSpace(newVersion)) return;

        // Cache original values on first switch
        _originalVersion ??= Version;
        _originalDownloadUrl ??= DownloadUrl;
        _originalFileName ??= FileName;

        var cleanTarget = newVersion.Trim().TrimStart('v', 'V');
        var cleanOriginal = _originalVersion.Trim().TrimStart('v', 'V');

        SelectedVersion = cleanTarget;

        if (string.Equals(cleanTarget, cleanOriginal, StringComparison.OrdinalIgnoreCase))
        {
            // Restoring latest
            DownloadUrl = _originalDownloadUrl;
            FileName = _originalFileName;
            Version = _originalVersion;
        }
        else
        {
            // 1. If explicit VersionTemplate exists
            if (!string.IsNullOrWhiteSpace(VersionTemplate))
            {
                DownloadUrl = VersionTemplate.Replace("{version}", cleanTarget);
            }
            else if (!string.IsNullOrWhiteSpace(cleanOriginal) && _originalDownloadUrl.Contains(cleanOriginal))
            {
                DownloadUrl = _originalDownloadUrl.Replace(cleanOriginal, cleanTarget);
            }
            else if (_originalDownloadUrl.Contains("/releases/latest/download/"))
            {
                DownloadUrl = _originalDownloadUrl.Replace(
                    "/releases/latest/download/", $"/releases/download/v{cleanTarget}/");
            }
            else if (_originalDownloadUrl.Contains("/releases/download/"))
            {
                // Replace release tag in URL
                DownloadUrl = System.Text.RegularExpressions.Regex.Replace(
                    _originalDownloadUrl,
                    @"/releases/download/[^/]+/",
                    $"/releases/download/v{cleanTarget}/");
            }

            // 2. Adjust FileName
            if (!string.IsNullOrWhiteSpace(FileNameTemplate))
            {
                FileName = FileNameTemplate.Replace("{version}", cleanTarget);
            }
            else if (!string.IsNullOrWhiteSpace(cleanOriginal) && _originalFileName.Contains(cleanOriginal))
            {
                FileName = _originalFileName.Replace(cleanOriginal, cleanTarget);
            }
        }

        // Reset download state so user can download the new version
        Status = ToolStatus.NotDownloaded;
        Progress = 0;
        SignatureResult = null;
        OnPropertyChanged(nameof(DownloadUrl));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(NameWithVersion));
    }

    /// <summary>Name and version combined for display, e.g. "Terraform 1.9.5".</summary>
    [JsonIgnore]
    public string NameWithVersion
    {
        get
        {
            var ver = SelectedVersion;
            if (string.IsNullOrWhiteSpace(ver)) return Name;
            return IsPreviousVersionSelected ? $"{Name}  ·  v{ver} (custom)" : $"{Name}  ·  v{ver}";
        }
    }

    /// <summary>True when a brand logo URL is available for this tool.</summary>
    [JsonIgnore]
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);

    private void UpdateStatusText()
    {
        StatusText = Status switch
        {
            ToolStatus.NotDownloaded => "Not downloaded",
            ToolStatus.Downloading => "Downloading...",
            ToolStatus.Downloaded => "Ready to install",
            ToolStatus.Failed => "Download failed",
            _ => "Unknown"
        };
    }

    // ── Property Notification ────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        var handler = PropertyChanged;
        if (handler is null) return;

        // Progress/status are mutated from download worker threads. WinUI
        // bindings must be notified on the UI thread, so marshal via the
        // captured dispatcher.
        Services.UiDispatcher.Run(() => handler(this, new PropertyChangedEventArgs(name)));
    }
}
