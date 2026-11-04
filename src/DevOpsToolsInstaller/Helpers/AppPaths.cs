using System;
using System.IO;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Single source of truth for the app's per-user data locations under
/// %LOCALAPPDATA%\DevOpsToolsInstaller. Services build their storage paths
/// from here instead of repeating the base-path pattern (which historically
/// drifted between "backups" and "Backups" style spellings).
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller");

    // ── Folders ─────────────────────────────────────────────────────────
    public static string LogsFolder => Path.Combine(Root, "logs");
    public static string ToolsFolder => Path.Combine(Root, "Tools");
    public static string BinFolder => Path.Combine(ToolsFolder, "bin");
    public static string SnapshotsFolder => Path.Combine(Root, "snapshots");
    public static string UpdatesRoot => Path.Combine(Root, "Updates");

    // ── Data files ──────────────────────────────────────────────────────
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string FavoritesFile => Path.Combine(Root, "favorites.json");
    public static string HooksFile => Path.Combine(Root, "hooks.json");
    public static string CustomStacksFile => Path.Combine(Root, "custom_stacks.json");
    public static string CustomToolsFile => Path.Combine(Root, "custom_tools.json");
    public static string HistoryFile => Path.Combine(Root, "history.jsonl");
    public static string ProfilesFile => Path.Combine(Root, "profiles.json");
    public static string UpdatePreferencesFile => Path.Combine(Root, "update-preferences.json");
    public static string ActiveVersionsFile => Path.Combine(Root, "active-versions.json");
    public static string PolicyFile => Path.Combine(Root, "policy.json");

    /// <summary>Creates (if needed) and returns the given folder.</summary>
    public static string EnsureFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        return folder;
    }
}
