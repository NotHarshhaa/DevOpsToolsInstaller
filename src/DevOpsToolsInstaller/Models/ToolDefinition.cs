using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;

namespace DevOpsToolsInstaller.Models;

public enum ToolStatus
{
    NotDownloaded,
    Downloading,
    Downloaded,
    Failed,
    Scheduled
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
    /// Optional SPDX license identifier ("MIT", "Apache-2.0", …) declared in
    /// the catalog for reference. Informational only — nothing is enforced.
    /// When omitted, the catalog falls back to the licence the tool's own
    /// repository publishes.
    /// </summary>
    [JsonPropertyName("license")]
    public string License { get; set; } = string.Empty;

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
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    [JsonIgnore]
    public int ProgressPercent => (int)Math.Round(Progress);

    private string _downloadSpeed = string.Empty;
    [JsonIgnore]
    public string DownloadSpeed
    {
        get => _downloadSpeed;
        set
        {
            if (_downloadSpeed != value)
            {
                _downloadSpeed = value;
                OnPropertyChanged();
            }
        }
    }

    // ── Live per-transfer ETA ─────────────────────────────────────────
    // Updated by the download pipeline alongside <see cref="DownloadSpeed"/>
    // (same ~400 ms cadence). Empty whenever the transfer has ended.

    private string _downloadEta = string.Empty;
    [JsonIgnore]
    public string DownloadEta
    {
        get => _downloadEta;
        set
        {
            if (_downloadEta != value)
            {
                _downloadEta = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasEta));
            }
        }
    }

    /// <summary>True while a live ETA estimate is available for display.</summary>
    [JsonIgnore]
    public bool HasEta => !string.IsNullOrEmpty(_downloadEta);

    // ── Live download-speed telemetry (DownloadsPage sparkline) ─────────
    // Numeric speed samples arrive on a download worker thread (~2.5/s,
    // below the 4/s chart throttle); buffer mutations are marshaled to the
    // UI thread where the LiveCharts control observes them.

    private double _downloadSpeedMBps;
    private Helpers.ChartTelemetry.BoundedValues? _speedSamples;
    private System.Collections.ObjectModel.ObservableCollection<ISeries>? _speedSeries;
    private Axis[]? _sparkXAxes;
    private Axis[]? _sparkYAxes;

    [JsonIgnore]
    public double DownloadSpeedMBps
    {
        get => _downloadSpeedMBps;
        set
        {
            if (Math.Abs(_downloadSpeedMBps - value) < 0.0001) return;
            _downloadSpeedMBps = value;
            OnPropertyChanged();

            Services.UiDispatcher.Queue?.TryEnqueue(() =>
            {
                _speedSamples ??= new Helpers.ChartTelemetry.BoundedValues();
                _speedSamples.Push(value);
                HasSpeedSamples = _speedSamples.Count >= 2;
            });
        }
    }

    /// <summary>True once at least two speed samples exist (chart drawable).</summary>
    [JsonIgnore]
    public bool HasSpeedSamples
    {
        get => _hasSpeedSamples;
        private set
        {
            if (_hasSpeedSamples == value) return;
            _hasSpeedSamples = value;
            OnPropertyChanged();
        }
    }
    private bool _hasSpeedSamples;

    /// <summary>Series for the per-transfer speed sparkline (lazy — only for tools that transfer).</summary>
    [JsonIgnore]
    public System.Collections.ObjectModel.ObservableCollection<ISeries> SpeedChartSeries
    {
        get
        {
            if (_speedSeries is null)
            {
                _speedSamples ??= new Helpers.ChartTelemetry.BoundedValues();
                _speedSeries = new System.Collections.ObjectModel.ObservableCollection<ISeries>
                {
                    Helpers.ChartTelemetry.CreateSparkLine(_speedSamples)
                };
            }
            return _speedSeries;
        }
    }

    /// <summary>X axes for the per-transfer speed sparkline (hidden).</summary>
    [JsonIgnore]
    public Axis[] SpeedChartXAxes => _sparkXAxes ??= Helpers.ChartTelemetry.HiddenAxes();

    /// <summary>Y axes for the per-transfer speed sparkline (MB/s ticks).</summary>
    [JsonIgnore]
    public Axis[] SpeedChartYAxes => _sparkYAxes ??= Helpers.ChartTelemetry.CreateSparkAxes(ElementTheme.Default);

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
                OnPropertyChanged(nameof(IsDownloadedOrInstalled));
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(ActionStatusText));
                OnPropertyChanged(nameof(ActionLabel));
                OnPropertyChanged(nameof(InstalledVersionLabel));
                UpdateStatusText();

                // Transfer ended (or restarted): release the sparkline
                // samples so charts dispose their data promptly.
                if (value != ToolStatus.Downloading)
                {
                    Services.UiDispatcher.Queue?.TryEnqueue(() =>
                    {
                        _speedSamples?.ClearSamples();
                        _downloadSpeedMBps = 0;
                        _downloadSpeed = string.Empty;
                        _downloadEta = string.Empty;
                        HasSpeedSamples = false;
                        OnPropertyChanged(nameof(DownloadSpeed));
                        OnPropertyChanged(nameof(DownloadEta));
                        OnPropertyChanged(nameof(HasEta));
                    });
                }
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
                OnPropertyChanged(nameof(IsDownloadedOrInstalled));
                OnPropertyChanged(nameof(ActionStatusText));
                OnPropertyChanged(nameof(ActionLabel));
                OnPropertyChanged(nameof(InstalledVersionLabel));
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
                OnPropertyChanged(nameof(IsNewerThanCatalog));
                OnPropertyChanged(nameof(IsUpToDate));
                OnPropertyChanged(nameof(HasComparableVersion));
                OnPropertyChanged(nameof(VersionStateText));
                OnPropertyChanged(nameof(DisplayDetectedVersion));
                OnPropertyChanged(nameof(InstalledVersionLabel));
                OnPropertyChanged(nameof(HasActionableUpdate));
                OnPropertyChanged(nameof(HasDetectedVersion));
                OnPropertyChanged(nameof(VersionColumnText));
                OnPropertyChanged(nameof(CanToggleUpdateIgnore));
            }
        }
    }

    [JsonIgnore]
    public string DisplayDetectedVersion =>
        string.IsNullOrWhiteSpace(DetectedVersion) ? "Unknown version" : $"v{DetectedVersion}";

    [JsonIgnore]
    public bool HasUpdate => TryCompareVersions(out var c) && c > 0;

    /// <summary>
    /// True when the version installed on this machine is newer than the one
    /// pinned in the catalog. That is a normal outcome when upstream releases
    /// outpace the catalog snapshot — it is not an error, and it must not be
    /// reported as "Up to date" nor as an available update.
    /// </summary>
    [JsonIgnore]
    public bool IsNewerThanCatalog => TryCompareVersions(out var c) && c < 0;

    /// <summary>True when the installed and catalog versions match exactly.</summary>
    [JsonIgnore]
    public bool IsUpToDate => TryCompareVersions(out var c) && c == 0;

    /// <summary>
    /// True when both versions are known and parseable, so a meaningful
    /// comparison could be made.
    /// </summary>
    [JsonIgnore]
    public bool HasComparableVersion => TryCompareVersions(out _);

    /// <summary>Single, human-readable version state for status text.</summary>
    [JsonIgnore]
    public string VersionStateText =>
        IsNewerThanCatalog ? "Newer than catalog"
        : HasUpdate ? "Update available"
        : IsUpToDate ? "Up to date"
        : "Installed";

    /// <summary>
    /// Short "what is on this machine" label for the catalog card's installed
    /// badge, e.g. "Installed v1.9.5". Empty when nothing is installed yet.
    /// </summary>
    [JsonIgnore]
    public string InstalledVersionLabel
    {
        get
        {
            if (!IsDownloadedOrInstalled) return string.Empty;
            if (!string.IsNullOrWhiteSpace(DetectedVersion)) return $"Installed v{DetectedVersion}";
            return Status == ToolStatus.Downloaded ? "Downloaded" : "Installed";
        }
    }

    // ── Update preferences (pinned / ignored) ────────────────────────────
    // The user's decisions about updates, persisted by
    // Services.UpdatePreferenceService and read lazily so nothing has to be
    // hydrated onto the model at construction time.

    /// <summary>True when the user froze this tool at the version it is on.</summary>
    [JsonIgnore]
    public bool IsVersionPinned => Services.UpdatePreferenceService.IsPinned(Id);

    /// <summary>
    /// True when the user dismissed the exact release the catalog still offers.
    /// A pin always wins, so a pinned tool never reports an ignored release.
    /// </summary>
    [JsonIgnore]
    public bool IsUpdateIgnored =>
        !IsVersionPinned &&
        Services.UpdatePreferenceService.VersionsMatch(
            Services.UpdatePreferenceService.GetIgnoredVersion(Id), Version);

    /// <summary>
    /// The update actually worth surfacing: a newer catalog release the user has
    /// neither pinned away nor already dismissed. Everything on the Installed
    /// page (badge, primary action, the Updates filter, Update all) keys off this
    /// rather than the raw <see cref="HasUpdate"/>, which stays a pure
    /// catalog-versus-machine comparison for the rest of the app.
    /// </summary>
    [JsonIgnore]
    public bool HasActionableUpdate => HasUpdate && !IsVersionPinned && !IsUpdateIgnored;

    /// <summary>True when an installed version was detected, so a pin has something to hold.</summary>
    [JsonIgnore]
    public bool HasDetectedVersion => !string.IsNullOrWhiteSpace(DetectedVersion);

    /// <summary>Label for the row menu's pin item.</summary>
    [JsonIgnore]
    public string PinMenuText => IsVersionPinned ? "Unpin version" : "Pin version";

    /// <summary>Label for the row menu's ignore item.</summary>
    [JsonIgnore]
    public string IgnoreUpdateMenuText =>
        IsUpdateIgnored ? "Un-ignore this update" : "Ignore this update";

    /// <summary>
    /// True while there is an update decision to offer or take back: a live
    /// offer to dismiss, or a previous dismissal to clear.
    /// </summary>
    [JsonIgnore]
    public bool CanToggleUpdateIgnore => !IsVersionPinned && (HasUpdate || IsUpdateIgnored);

    /// <summary>
    /// The version column's text, annotated when the user has pinned the tool or
    /// dismissed the release on offer.
    /// </summary>
    [JsonIgnore]
    public string VersionColumnText =>
        IsVersionPinned ? $"{DisplayDetectedVersion} (pinned)"
        : IsUpdateIgnored ? $"{DisplayDetectedVersion} (ignored)"
        : DisplayDetectedVersion;

    /// <summary>
    /// Re-reads the pin/ignore store and re-raises every property that depends on
    /// it. Call after pinning, un-pinning, ignoring, un-ignoring or rescanning.
    /// </summary>
    public void RefreshUpdatePreferences()
    {
        OnPropertyChanged(nameof(IsVersionPinned));
        OnPropertyChanged(nameof(IsUpdateIgnored));
        OnPropertyChanged(nameof(HasActionableUpdate));
        OnPropertyChanged(nameof(HasDetectedVersion));
        OnPropertyChanged(nameof(PinMenuText));
        OnPropertyChanged(nameof(IgnoreUpdateMenuText));
        OnPropertyChanged(nameof(CanToggleUpdateIgnore));
        OnPropertyChanged(nameof(VersionColumnText));
        OnPropertyChanged(nameof(VersionStateText));
    }

    /// <summary>
    /// Runtime flag: the installed executable resolves to a directory that is on
    /// the effective PATH, so the command can be typed into a fresh terminal.
    /// Populated by the Installed page's scan; never persisted. Defaults to true
    /// so a tool that has not been scanned yet is not falsely flagged.
    /// </summary>
    private bool _isOnPath = true;
    [JsonIgnore]
    public bool IsOnPath
    {
        get => _isOnPath;
        set
        {
            if (_isOnPath != value)
            {
                _isOnPath = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Compares the installed version against the catalog version numerically
    /// (so 2.51.0 is correctly newer than 2.9.9). Returns false when either
    /// version is missing or unparseable.
    /// </summary>
    private bool TryCompareVersions(out int catalogComparedToInstalled)
    {
        catalogComparedToInstalled = 0;

        if (string.IsNullOrWhiteSpace(DetectedVersion) || string.IsNullOrWhiteSpace(Version))
            return false;

        if (System.Version.TryParse(CleanVersion(DetectedVersion), out var vDet) &&
            System.Version.TryParse(CleanVersion(Version), out var vCat))
        {
            catalogComparedToInstalled = vCat.CompareTo(vDet);
            return true;
        }

        return false;
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
    public string ActionLabel
    {
        get
        {
            if (IsInstalled)
            {
                return Kind switch
                {
                    ArtifactKind.Installer => "Installed ✓",
                    ArtifactKind.Archive   => "Extracted ✓",
                    ArtifactKind.Binary    => "In Tools\\bin ✓",
                    ArtifactKind.Script    => "Reviewed ✓",
                    _                      => "Installed ✓"
                };
            }

            return Kind switch
            {
                ArtifactKind.Installer => "Install",
                ArtifactKind.Archive   => "Extract",
                ArtifactKind.Binary    => "Add to Tools",
                ArtifactKind.Script    => "Open Folder",
                _                      => "Install"
            };
        }
    }

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
                OnPropertyChanged(nameof(DisplayVersionWithV));
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
    public string DisplayVersionWithV
    {
        get
        {
            var raw = string.IsNullOrWhiteSpace(SelectedVersion) ? Version : SelectedVersion;
            if (string.IsNullOrWhiteSpace(raw)) return "v1.0.0";
            raw = raw.Trim();
            return raw.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? raw : $"v{raw}";
        }
    }

    [JsonIgnore]
    public bool IsDownloadedOrInstalled => IsInstalled || Status == ToolStatus.Downloaded;

    [JsonIgnore]
    public bool IsDownloading => Status == ToolStatus.Downloading;

    [JsonIgnore]
    public bool IsScheduled => Status == ToolStatus.Scheduled;

    [JsonIgnore]
    public string ActionStatusText => IsDownloadedOrInstalled ? "Installed" : "Install";

    [JsonIgnore]
    public bool IsAccentPurple =>
        Category.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase) ||
        Category.Contains("CI/CD", StringComparison.OrdinalIgnoreCase) ||
        Category.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
        Category.Contains("Security", StringComparison.OrdinalIgnoreCase) ||
        Id.Equals("terraform", StringComparison.OrdinalIgnoreCase) ||
        Id.Equals("helm", StringComparison.OrdinalIgnoreCase) ||
        Id.Equals("git", StringComparison.OrdinalIgnoreCase) ||
        Id.Equals("sops", StringComparison.OrdinalIgnoreCase) ||
        Id.Equals("vault", StringComparison.OrdinalIgnoreCase) ||
        Id.Equals("trivy", StringComparison.OrdinalIgnoreCase) ||
        (Math.Abs(Id.GetHashCode()) % 4 == 1);

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
            else if (_originalDownloadUrl.Contains("/releases/latest/", StringComparison.Ordinal))
            {
                // Versioned CDN buckets that expose every release but only
                // advertise a "latest" channel (minikube / skaffold GCS
                // mirrors): rewrite the channel segment to the pinned release.
                DownloadUrl = _originalDownloadUrl.Replace(
                    "/releases/latest/", $"/releases/v{cleanTarget}/");
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

        // The offered catalog version just changed, so any pin/ignore decision
        // recorded against the previous one must be re-evaluated.
        RefreshUpdatePreferences();
    }

    /// <summary>
    /// Applies a specific version exactly like <see cref="SetVersion"/> does,
    /// then overrides the download URL and file name with a concrete release
    /// asset. Used when a live GitHub release lookup identified the exact
    /// artifact for the chosen version, so the file name stays correct even
    /// when vendors rename files between releases.
    /// </summary>
    public void SetVersionWithAsset(string newVersion, string downloadUrl, string fileName)
    {
        SetVersion(newVersion);

        if (!string.IsNullOrWhiteSpace(downloadUrl))
            DownloadUrl = downloadUrl;
        if (!string.IsNullOrWhiteSpace(fileName))
            FileName = fileName;

        OnPropertyChanged(nameof(DownloadUrl));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(NameWithVersion));
    }

    /// <summary>
    /// True when selecting an older version can actually change what gets
    /// downloaded: the catalog URL embeds the version (or a templated
    /// <c>{version}</c>), or the vendor keeps old builds at predictable URLs we
    /// know how to rewrite (GitHub release tags, versioned CDNs such as
    /// HashiCorp/GCS buckets, <c>/releases/latest/</c> channels). Vendors that
    /// serve a single always-latest link (aka.ms redirects, <c>AWSCLIV2.msi</c>,
    /// Docker Desktop's main channel) cannot be pointed at older builds from a
    /// static URL — for those the version picker offers curated versions for
    /// reference only instead of silently downloading the latest release.
    /// </summary>
    [JsonIgnore]
    public bool SupportsVersionTargeting
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(VersionTemplate))
            {
                return true;
            }

            var url = _originalDownloadUrl ?? DownloadUrl;
            var version = _originalVersion ?? Version;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            // Version embedded in the URL itself (GitHub tag URLs, versioned
            // CDNs, vendor archives that keep every build online).
            if (!string.IsNullOrWhiteSpace(version) &&
                url.Contains(version, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Channel URLs with schemes we rewrite in SetVersion.
            if (url.Contains("/releases/latest/", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
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

    /// <summary>
    /// Friendly vendor/source name for the catalog card, derived from the
    /// tool's homepage (falling back to its download host) — for example
    /// "github.com" becomes "GitHub". Unknown hosts are shown verbatim so the
    /// card can always tell the user where a tool comes from.
    /// </summary>
    [JsonIgnore]
    public string VendorLabel
    {
        get
        {
            var host = TryGetHost(Homepage) ?? TryGetHost(DownloadUrl);
            if (host is null) return "Unknown source";

            host = host.TrimStart('.').ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];

            foreach (var (suffix, name) in VendorHosts)
            {
                if (host.Equals(suffix, StringComparison.Ordinal) ||
                    host.EndsWith("." + suffix, StringComparison.Ordinal))
                {
                    return name;
                }
            }

            return host;
        }
    }

    /// <summary>Host-name → friendly vendor name table, matched on domain suffix.</summary>
    private static readonly (string Suffix, string Name)[] VendorHosts =
    {
        ("github.com", "GitHub"),
        ("githubusercontent.com", "GitHub"),
        ("github.io", "GitHub Pages"),
        ("gitlab.com", "GitLab"),
        ("bitbucket.org", "Bitbucket"),
        ("amazonaws.com", "Amazon Web Services"),
        ("aws.amazon.com", "Amazon Web Services"),
        ("microsoft.com", "Microsoft"),
        ("aka.ms", "Microsoft"),
        ("azure.com", "Microsoft"),
        ("azureedge.net", "Microsoft"),
        ("google.com", "Google"),
        ("googleapis.com", "Google"),
        ("k8s.io", "Kubernetes"),
        ("kubernetes.io", "Kubernetes"),
        ("helm.sh", "Helm"),
        ("hashicorp.com", "HashiCorp"),
        ("docker.com", "Docker"),
        ("docker.io", "Docker"),
        ("grafana.com", "Grafana Labs"),
        ("prometheus.io", "Prometheus"),
        ("elastic.co", "Elastic"),
        ("jetbrains.com", "JetBrains"),
        ("apache.org", "Apache"),
        ("nodejs.org", "Node.js"),
        ("python.org", "Python"),
        ("go.dev", "Go"),
        ("golang.org", "Go"),
        ("rust-lang.org", "Rust"),
        ("mozilla.org", "Mozilla"),
        ("postgresql.org", "PostgreSQL"),
        ("mysql.com", "MySQL"),
        ("mongodb.com", "MongoDB"),
        ("redis.io", "Redis"),
        ("nginx.org", "NGINX"),
        ("jetstack.io", "Jetstack"),
        ("snyk.io", "Snyk"),
        ("cloudflare.com", "Cloudflare"),
        ("datadoghq.com", "Datadog"),
        ("newrelic.com", "New Relic"),
        ("atlassian.com", "Atlassian"),
        ("jfrog.com", "JFrog"),
        ("sonarsource.com", "SonarSource"),
        ("sonarqube.org", "SonarSource"),
        ("anchore.com", "Anchore"),
        ("aquasec.com", "Aqua Security"),
        ("openpolicyagent.org", "Open Policy Agent"),
        ("istio.io", "Istio"),
        ("cilium.io", "Cilium"),
        ("bitnami.com", "Bitnami"),
        ("oracle.com", "Oracle"),
        ("ibm.com", "IBM"),
        ("redhat.com", "Red Hat"),
        ("vmware.com", "VMware"),
        ("influxdata.com", "InfluxData"),
        ("min.io", "MinIO"),
        ("cncf.io", "CNCF"),
        ("bundler.io", "Bundler"),
        ("ruby-lang.org", "Ruby"),
        ("gnu.org", "GNU"),
        ("wireshark.org", "Wireshark"),
    };

    /// <summary>Extracts the host of an absolute URL, or null when it is not one.</summary>
    private static string? TryGetHost(string? url)
        => !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;

    private void UpdateStatusText()
    {
        StatusText = Status switch
        {
            ToolStatus.NotDownloaded => "Not downloaded",
            ToolStatus.Downloading => "Downloading...",
            ToolStatus.Downloaded => "Ready to install",
            ToolStatus.Failed => "Download failed",
            ToolStatus.Scheduled => "Scheduled (off-peak)",
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
