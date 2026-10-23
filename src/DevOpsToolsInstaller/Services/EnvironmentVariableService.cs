using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

public sealed record PathItem(
    string Path,
    int Index,
    bool Exists,
    bool IsDuplicate,
    bool IsDevOpsBin)
{
    public string DisplayText => Path;
    public string StatusText => !Exists ? "Missing directory" : (IsDuplicate ? "Duplicate" : (IsDevOpsBin ? "DevOps Tools Bin" : "Valid"));
}

public sealed record EnvVariableItem(
    string Name,
    string Value,
    EnvironmentVariableTarget Target,
    bool IsReadOnly)
{
    public string TargetLabel => Target == EnvironmentVariableTarget.User ? "User" : "System";
}

public sealed record DevOpsPreset(
    string Name,
    string Value,
    string Description);

/// <summary>
/// Comprehensive manager for User & System PATH, environment variables,
/// automated timestamped PATH backups, DevOps variable presets, and live broadcast updates.
/// </summary>
public static class EnvironmentVariableService
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private static string BackupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "backups", "path");

    public static readonly IReadOnlyList<DevOpsPreset> Presets = new List<DevOpsPreset>
    {
        new("DOCKER_BUILDKIT", "1", "Enables Docker BuildKit for faster, parallel container builds."),
        new("KUBECONFIG", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config"), "Standard Kubernetes cluster configuration path."),
        new("AWS_SDK_LOAD_CONFIG", "1", "Enforces AWS SDKs to read credentials and region from ~/.aws/config."),
        new("TF_IN_AUTOMATION", "1", "Informs Terraform it is running in an automated or script environment."),
        new("DOCKER_HOST", "npipe:////./pipe/docker_engine", "Directs Docker CLI to the local Windows named pipe."),
        new("DOTNET_CLI_TELEMETRY_OPTOUT", "1", "Disables .NET CLI diagnostic telemetry collection.")
    };

    /// <summary>
    /// Gets all directories in the PATH for the specified target.
    /// </summary>
    public static List<PathItem> GetPathEntries(EnvironmentVariableTarget target = EnvironmentVariableTarget.User)
    {
        var raw = Environment.GetEnvironmentVariable("Path", target) ?? string.Empty;
        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var devOpsBin = ArtifactService.BinFolder;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PathItem>();

        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            bool isDup = !seen.Add(p);
            bool exists = Directory.Exists(p);
            bool isBin = string.Equals(p.TrimEnd('\\'), devOpsBin.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            result.Add(new PathItem(p, i, exists, isDup, isBin));
        }

        return result;
    }

    /// <summary>
    /// Saves the PATH for the specified target, creates an automatic timestamped backup, and broadcasts the change.
    /// </summary>
    public static bool SavePathEntries(IEnumerable<string> paths, EnvironmentVariableTarget target = EnvironmentVariableTarget.User, bool createBackup = true)
    {
        try
        {
            var validList = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();

            if (createBackup)
            {
                CreatePathBackup(validList, target);
            }

            var joined = string.Join(";", validList);
            Environment.SetEnvironmentVariable("Path", joined, target);
            BroadcastChange();
            ActivityLogService.Info("Environment", $"Updated {target} PATH with {validList.Count} entries.");
            return true;
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Environment", $"Failed to save {target} PATH: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates a timestamped JSON backup file of PATH entries before modification.
    /// </summary>
    public static string CreatePathBackup(IEnumerable<string> paths, EnvironmentVariableTarget target = EnvironmentVariableTarget.User)
    {
        try
        {
            Directory.CreateDirectory(BackupFolder);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"path_backup_{target}_{timestamp}.json";
            var fullPath = Path.Combine(BackupFolder, filename);

            var data = new
            {
                Target = target.ToString(),
                Timestamp = DateTime.UtcNow,
                Entries = paths.ToList()
            };

            File.WriteAllText(fullPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            return fullPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Restores PATH from the most recent backup file.
    /// </summary>
    public static (bool Success, string Message) RestoreLastPathBackup(EnvironmentVariableTarget target = EnvironmentVariableTarget.User)
    {
        try
        {
            if (!Directory.Exists(BackupFolder)) return (false, "No backup directory found.");

            var files = Directory.GetFiles(BackupFolder, $"path_backup_{target}_*.json")
                .OrderByDescending(File.GetCreationTimeUtc)
                .ToList();

            if (files.Count == 0) return (false, "No previous PATH backups found.");

            var latest = files[0];
            var json = File.ReadAllText(latest);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Entries", out var entriesProp))
            {
                var entries = entriesProp.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                bool saved = SavePathEntries(entries, target, createBackup: false);
                return (saved, saved ? $"Restored {entries.Count} PATH entries from backup ({Path.GetFileName(latest)})." : "Failed to write restored PATH.");
            }

            return (false, "Malformed backup file format.");
        }
        catch (Exception ex)
        {
            return (false, $"Error restoring backup: {ex.Message}");
        }
    }

    /// <summary>
    /// Deduplicates and removes dead/missing directories from PATH.
    /// </summary>
    public static (int RemovedDups, int RemovedDead) CleanAndDeduplicatePath(EnvironmentVariableTarget target = EnvironmentVariableTarget.User)
    {
        var current = GetPathEntries(target);
        var valid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int removedDups = 0;
        int removedDead = 0;

        foreach (var item in current)
        {
            if (!item.Exists)
            {
                removedDead++;
                continue;
            }

            if (!seen.Add(item.Path))
            {
                removedDups++;
                continue;
            }

            valid.Add(item.Path);
        }

        SavePathEntries(valid, target, createBackup: true);
        return (removedDups, removedDead);
    }

    /// <summary>
    /// Ensures %LOCALAPPDATA%\DevOpsToolsInstaller\Tools\bin is at the top of the user PATH.
    /// </summary>
    public static bool EnsureDevOpsBinOnPath()
    {
        var bin = ArtifactService.BinFolder;
        Directory.CreateDirectory(bin);

        var entries = GetPathEntries(EnvironmentVariableTarget.User).Select(p => p.Path).ToList();
        entries.RemoveAll(p => string.Equals(p.TrimEnd('\\'), bin.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, bin);

        return SavePathEntries(entries, EnvironmentVariableTarget.User, createBackup: true);
    }

    /// <summary>
    /// Retrieves all environment variables (excluding Path).
    /// </summary>
    public static List<EnvVariableItem> GetVariables(EnvironmentVariableTarget target = EnvironmentVariableTarget.User)
    {
        var list = new List<EnvVariableItem>();
        try
        {
            var dict = Environment.GetEnvironmentVariables(target);
            foreach (DictionaryEntry de in dict)
            {
                var key = de.Key?.ToString();
                var val = de.Value?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (string.Equals(key, "Path", StringComparison.OrdinalIgnoreCase)) continue;

                list.Add(new EnvVariableItem(key, val, target, target == EnvironmentVariableTarget.Machine && !IsElevated()));
            }
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("Environment", $"Error reading variables: {ex.Message}");
        }

        return list.OrderBy(v => v.Name).ToList();
    }

    /// <summary>
    /// Sets or deletes an environment variable and broadcasts the change.
    /// </summary>
    public static bool SetVariable(string name, string? value, EnvironmentVariableTarget target = EnvironmentVariableTarget.User)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            Environment.SetEnvironmentVariable(name.Trim(), string.IsNullOrWhiteSpace(value) ? null : value.Trim(), target);
            BroadcastChange();
            ActivityLogService.Info("Environment", $"{(string.IsNullOrWhiteSpace(value) ? "Deleted" : "Set")} variable '{name}' ({target}).");
            return true;
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Environment", $"Failed to update variable '{name}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Applies a DevOps preset variable.
    /// </summary>
    public static bool ApplyPreset(DevOpsPreset preset)
    {
        return SetVariable(preset.Name, preset.Value, EnvironmentVariableTarget.User);
    }

    /// <summary>
    /// Broadcasts WM_SETTINGCHANGE so running shells and applications can reload the environment.
    /// </summary>
    public static void BroadcastChange()
    {
        try
        {
            SendMessageTimeout(
                new IntPtr(HWND_BROADCAST),
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                SMTO_ABORTIFHUNG,
                1500,
                out _);
        }
        catch { }
    }

    public static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(id);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
