using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Persists the per-tool update decisions the user makes on the Installed page,
/// so the app stops re-offering something they have already answered:
///
///   * <b>Pinned</b> — "stay on the version I have". Suppresses every update for
///     that tool until the pin is removed.
///   * <b>Ignored</b> — "not this release". Suppresses only the exact catalog
///     version that was on offer; as soon as the catalog moves on, the newer
///     release is reported normally.
///
/// Stored in <c>%LOCALAPPDATA%\DevOpsToolsInstaller\update-preferences.json</c>.
/// </summary>
public static class UpdatePreferenceService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "update-preferences.json");

    private static readonly object Gate = new();

    private static Dictionary<string, string> _pinned = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _ignored = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads pinned versions and ignored releases from disk.</summary>
    public static void Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                var store = JsonSerializer.Deserialize<UpdatePreferenceStore>(File.ReadAllText(FilePath));
                if (store is null) return;

                _pinned = Copy(store.Pinned);
                _ignored = Copy(store.Ignored);
            }
            catch
            {
                // A corrupt or unreadable file must never block startup: fall
                // back to "no preferences", which offers every update.
                _pinned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _ignored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Writes the current preferences to disk, ignoring write errors.</summary>
    private static void Save()
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var store = new UpdatePreferenceStore
                {
                    Pinned = new Dictionary<string, string>(_pinned, StringComparer.OrdinalIgnoreCase),
                    Ignored = new Dictionary<string, string>(_ignored, StringComparer.OrdinalIgnoreCase)
                };

                File.WriteAllText(FilePath, JsonSerializer.Serialize(store,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Preferences are a convenience; never surface an I/O failure.
            }
        }
    }

    private static Dictionary<string, string> Copy(Dictionary<string, string>? source) =>
        source is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);

    // ── Pins ─────────────────────────────────────────────────────────────

    /// <summary>True when the user froze this tool at the version it is on.</summary>
    public static bool IsPinned(string toolId) => GetPinnedVersion(toolId) is not null;

    /// <summary>The version this tool is frozen at, or null when it is not pinned.</summary>
    public static string? GetPinnedVersion(string toolId)
    {
        lock (Gate)
        {
            return _pinned.TryGetValue(toolId, out var version) && !string.IsNullOrWhiteSpace(version)
                ? version
                : null;
        }
    }

    /// <summary>Freezes a tool at <paramref name="version"/> and clears any pending ignore.</summary>
    public static void Pin(string toolId, string version)
    {
        lock (Gate)
        {
            _pinned[toolId] = version;
            _ignored.Remove(toolId);
        }

        Save();
    }

    /// <summary>Removes the freeze so updates are offered again.</summary>
    public static void Unpin(string toolId)
    {
        lock (Gate)
        {
            _pinned.Remove(toolId);
        }

        Save();
    }

    // ── Ignored releases ─────────────────────────────────────────────────

    /// <summary>The release the user dismissed for this tool, or null.</summary>
    public static bool IsIgnored(string toolId, string version) =>
        VersionsMatch(GetIgnoredVersion(toolId), version);

    public static string? GetIgnoredVersion(string toolId)
    {
        lock (Gate)
        {
            return _ignored.TryGetValue(toolId, out var version) && !string.IsNullOrWhiteSpace(version)
                ? version
                : null;
        }
    }

    /// <summary>Dismisses one specific catalog release.</summary>
    public static void IgnoreUpdate(string toolId, string version)
    {
        lock (Gate)
        {
            _ignored[toolId] = version;
            _pinned.Remove(toolId);
        }

        Save();
    }

    /// <summary>Forgets the dismissed release so it is offered again.</summary>
    public static void ClearIgnoredUpdate(string toolId)
    {
        lock (Gate)
        {
            _ignored.Remove(toolId);
        }

        Save();
    }

    // ── Decision helper ──────────────────────────────────────────────────

    /// <summary>Compares versions ignoring a leading "v" and surrounding space.</summary>
    public static bool VersionsMatch(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) &&
        !string.IsNullOrWhiteSpace(b) &&
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string version) =>
        version.Trim().TrimStart('v', 'V').Trim();

    /// <summary>On-disk shape of the preference file.</summary>
    private sealed class UpdatePreferenceStore
    {
        public Dictionary<string, string>? Pinned { get; set; }
        public Dictionary<string, string>? Ignored { get; set; }
    }
}
