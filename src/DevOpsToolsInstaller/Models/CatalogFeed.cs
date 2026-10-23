using System.Text.Json.Serialization;

namespace DevOpsToolsInstaller.Models;

/// <summary>
/// Represents a subscription to an external or internal enterprise catalog feed
/// (e.g. corporate approved tools URL or network share).
/// </summary>
public sealed class CatalogFeed
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonPropertyName("lastSyncedUtc")]
    public DateTime? LastSyncedUtc { get; set; }

    [JsonPropertyName("toolCount")]
    public int ToolCount { get; set; }

    [JsonPropertyName("syncError")]
    public string? SyncError { get; set; }

    [JsonPropertyName("isPolicyEnforced")]
    public bool IsPolicyEnforced { get; set; }

    [JsonIgnore]
    public bool HasError => !string.IsNullOrEmpty(SyncError);

    [JsonIgnore]
    public string StatusText => !IsEnabled ? "Disabled" :
        HasError ? $"Sync failed: {SyncError}" :
        LastSyncedUtc.HasValue ? $"Active ({ToolCount} tools, synced {LastSyncedUtc.Value.ToLocalTime():g})" :
        "Pending first sync";
}
