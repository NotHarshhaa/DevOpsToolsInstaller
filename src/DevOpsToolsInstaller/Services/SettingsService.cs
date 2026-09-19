using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

public enum AppTheme
{
    Default,
    Light,
    Dark
}

public static class SettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "settings.json");

    public static AppTheme Theme { get; set; } = AppTheme.Default;

    private static string? _customDownloadsFolder;
    public static string DownloadsFolder
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_customDownloadsFolder) && Directory.Exists(_customDownloadsFolder))
                return _customDownloadsFolder;

            var defaultFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevOpsToolsInstaller", "Downloads");
            Directory.CreateDirectory(defaultFolder);
            return defaultFolder;
        }
        set
        {
            _customDownloadsFolder = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                try { Directory.CreateDirectory(value); } catch { }
            }
        }
    }

    public static void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Theme", out var themeProp) &&
                    Enum.TryParse<AppTheme>(themeProp.GetString(), out var theme))
                {
                    Theme = theme;
                }

                if (doc.RootElement.TryGetProperty("DownloadsFolder", out var dlProp))
                {
                    var dl = dlProp.GetString();
                    if (!string.IsNullOrWhiteSpace(dl))
                    {
                        DownloadsFolder = dl;
                    }
                }
            }
        }
        catch
        {
            // Fallback to defaults
        }
    }

    public static void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var data = new
            {
                Theme = Theme.ToString(),
                DownloadsFolder = DownloadsFolder
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    // ── User PATH Environment Variable Management ────────────────────────

    /// <summary>
    /// Checks whether the specified folder is present in the current user's PATH.
    /// </summary>
    public static bool IsFolderOnUserPath(string folder)
    {
        try
        {
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var processPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            var combined = userPath + Path.PathSeparator + processPath;

            var cleanTarget = folder.Trim().TrimEnd('\\', '/');

            return combined
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Any(p => string.Equals(p.Trim().TrimEnd('\\', '/'), cleanTarget, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely appends the specified folder to the User's persistent PATH environment variable.
    /// </summary>
    public static bool AddToUserPath(string folder)
    {
        try
        {
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var cleanTarget = folder.Trim().TrimEnd('\\', '/');

            var entries = userPath
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().TrimEnd('\\', '/'))
                .ToList();

            if (!entries.Any(p => string.Equals(p, cleanTarget, StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(folder.Trim().TrimEnd('\\', '/'));
                var newPath = string.Join(Path.PathSeparator.ToString(), entries);
                Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);

                // Also update process PATH for immediate availability
                var procPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
                Environment.SetEnvironmentVariable("PATH", procPath + Path.PathSeparator + folder, EnvironmentVariableTarget.Process);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely removes the specified folder from the User's persistent PATH environment variable.
    /// </summary>
    public static bool RemoveFromUserPath(string folder)
    {
        try
        {
            var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
            var cleanTarget = folder.Trim().TrimEnd('\\', '/');

            var entries = userPath
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !string.Equals(p.Trim().TrimEnd('\\', '/'), cleanTarget, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var newPath = string.Join(Path.PathSeparator.ToString(), entries);
            Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
