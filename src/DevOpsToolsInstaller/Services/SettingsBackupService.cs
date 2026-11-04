using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Helpers;

namespace DevOpsToolsInstaller.Services;

/// <summary>Outcome of a settings import.</summary>
public sealed record SettingsImportResult(bool Success, string Message, IReadOnlyList<string> RestoredFiles);

/// <summary>
/// Exports and restores the app's complete configuration as a single
/// versioned JSON bundle: settings, favorites, update pins, hooks, custom
/// stacks and custom tools. Complements the New-PC migration feature with
/// full application-config portability.
///
/// The proxy password is a secret: it is excluded by default and only
/// embedded when the caller explicitly opts in.
/// </summary>
public static class SettingsBackupService
{
    private const string SchemaName = "devops-tools-installer-settings-backup";
    private const int SchemaVersion = 1;
    private const string SecretKey = "ProxyPassword";

    private static IEnumerable<string> FilesToBundle => new[]
    {
        "settings.json",
        "favorites.json",
        "update-preferences.json",
        "hooks.json",
        "custom_stacks.json",
        "custom_tools.json"
    };

    /// <summary>
    /// Writes a settings bundle to <paramref name="filePath"/>.
    /// </summary>
    /// <param name="includeSecrets">
    /// When false (the default) the proxy password is stripped from the bundle.
    /// </param>
    public static Task<(bool Ok, string? Error)> ExportAsync(string filePath, bool includeSecrets)
    {
        return Task.Run(() =>
        {
            try
            {
                var files = new JsonObject();
                foreach (var name in FilesToBundle)
                {
                    var path = Path.Combine(AppPaths.Root, name);
                    if (!File.Exists(path)) continue; // optional by design
                    try
                    {
                        var node = JsonNode.Parse(File.ReadAllText(path));
                        if (node is null) continue;

                        if (name == "settings.json" && !includeSecrets && node is JsonObject obj)
                        {
                            obj.Remove(SecretKey);
                        }
                        files[name] = node;
                    }
                    catch (Exception ex)
                    {
                        // One unreadable file must not fail the whole export.
                        ActivityLogService.Warn("Backup", $"Skipped '{name}' while exporting: {ex.Message}");
                    }
                }

                var bundle = new JsonObject
                {
                    ["schema"] = SchemaName,
                    ["version"] = SchemaVersion,
                    ["exportedAt"] = DateTime.UtcNow.ToString("o"),
                    ["appVersion"] = AppUpdaterService.CurrentVersion,
                    ["includesSecrets"] = includeSecrets,
                    ["files"] = files
                };

                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, bundle.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                ActivityLogService.Success("Backup", $"Exported settings bundle to {filePath} ({files.Count} file(s), secrets: {(includeSecrets ? "included" : "excluded")}).");
                return (true, (string?)null);
            }
            catch (Exception ex)
            {
                ActivityLogService.Error("Backup", $"Settings export failed: {ex.Message}");
                return (false, (string?)ex.Message);
            }
        });
    }

    /// <summary>
    /// Validates a bundle and restores its files. Favorites and update
    /// preferences reload into the running session immediately; a restart is
    /// recommended for settings/hooks/stacks that are cached by pages.
    /// </summary>
    public static Task<SettingsImportResult> ImportAsync(string filePath, bool importSecrets)
    {
        return Task.Run(() =>
        {
            var restored = new List<string>();
            try
            {
                var bundle = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject;
                if (bundle is null)
                {
                    return new SettingsImportResult(false, "The file is not a valid settings bundle.", restored);
                }

                if (!string.Equals(bundle["schema"]?.GetValue<string>(), SchemaName, StringComparison.OrdinalIgnoreCase))
                {
                    return new SettingsImportResult(false, "This file was not exported by DevOpsToolsInstaller (schema mismatch).", restored);
                }

                var version = bundle["version"]?.GetValue<int>() ?? 0;
                if (version > SchemaVersion)
                {
                    return new SettingsImportResult(false, $"The bundle was created by a newer app version (schema {version} > {SchemaVersion}). Update the app and retry.", restored);
                }

                if (bundle["files"] is not JsonObject files || files.Count == 0)
                {
                    return new SettingsImportResult(false, "The bundle contains no backed-up files.", restored);
                }

                bool bundleHasSecrets = bundle["includesSecrets"]?.GetValue<bool>() ?? false;
                if (!bundleHasSecrets) importSecrets = false;

                Directory.CreateDirectory(AppPaths.Root);

                foreach (var (name, node) in files)
                {
                    if (string.IsNullOrWhiteSpace(name) || node is null) continue;
                    // Only ever write into the app's own data folder — never a
                    // path that came from inside the bundle.
                    var safeName = Path.GetFileName(name);
                    var target = Path.Combine(AppPaths.Root, safeName);

                    if (safeName == "settings.json" &&
                        node is JsonObject settings &&
                        !importSecrets)
                    {
                        settings.Remove(SecretKey);
                    }

                    File.WriteAllText(target, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    restored.Add(safeName);
                }

                // Reload what can safely hot-apply in this session.
                if (restored.Contains("favorites.json")) FavoritesService.Load();
                if (restored.Contains("update-preferences.json")) UpdatePreferenceService.Load();

                ActivityLogService.Success("Backup", $"Imported settings bundle from {filePath}: {string.Join(", ", restored)}.");
                return new SettingsImportResult(true,
                    restored.Contains("settings.json")
                        ? "Settings restored. Restart the app so every section picks them up."
                        : "Backup restored.",
                    restored);
            }
            catch (Exception ex)
            {
                ActivityLogService.Error("Backup", $"Settings import failed: {ex.Message}");
                return new SettingsImportResult(false, ex.Message, restored);
            }
        });
    }
}
