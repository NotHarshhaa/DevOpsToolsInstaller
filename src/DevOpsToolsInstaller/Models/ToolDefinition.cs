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

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

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

    /// <summary>Name and version combined for display, e.g. "Terraform 1.9.5".</summary>
    [JsonIgnore]
    public string NameWithVersion =>
        string.IsNullOrWhiteSpace(Version) ? Name : $"{Name}  ·  v{Version}";

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
