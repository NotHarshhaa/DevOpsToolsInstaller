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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
