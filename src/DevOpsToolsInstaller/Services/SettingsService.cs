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

    public static bool CheckForUpdatesOnStartup { get; set; } = true;
    public static DateTime? LastUpdateCheckTime { get; set; }

    /// <summary>Show Windows toast notifications for background events.</summary>
    public static bool EnableNotifications { get; set; } = true;

    /// <summary>Closing the window hides it to the system tray instead of exiting.</summary>
    public static bool CloseToTray { get; set; } = true;

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

                if (doc.RootElement.TryGetProperty("CheckForUpdatesOnStartup", out var autoUpdateProp))
                {
                    CheckForUpdatesOnStartup = autoUpdateProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("LastUpdateCheckTime", out var lastCheckProp) &&
                    DateTime.TryParse(lastCheckProp.GetString(), out var lastCheck))
                {
                    LastUpdateCheckTime = lastCheck;
                }

                if (doc.RootElement.TryGetProperty("EnableNotifications", out var notifProp))
                {
                    EnableNotifications = notifProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("CloseToTray", out var trayProp))
                {
                    CloseToTray = trayProp.GetBoolean();
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
                DownloadsFolder = DownloadsFolder,
                CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
                LastUpdateCheckTime = LastUpdateCheckTime?.ToString("o"),
                EnableNotifications = EnableNotifications,
                CloseToTray = CloseToTray
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

    // ── Desktop & Start Menu Shortcuts ────────────────────────────────────

    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "DevOps Tools Installer.lnk");

    public static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        "DevOps Tools Installer.lnk");

    public static bool HasDesktopShortcut() => File.Exists(DesktopShortcutPath);
    public static bool HasStartMenuShortcut() => File.Exists(StartMenuShortcutPath);

    public static bool CreateDesktopShortcut() => CreateShortcut(DesktopShortcutPath);
    public static bool CreateStartMenuShortcut() => CreateShortcut(StartMenuShortcutPath);

    private static bool CreateShortcut(string shortcutPath)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "DevOpsToolsInstaller.exe");
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = exePath;
            }

            var escapedShortcut = shortcutPath.Replace("'", "''");
            var escapedTarget = exePath.Replace("'", "''");
            var escapedWorking = (Path.GetDirectoryName(exePath) ?? "").Replace("'", "''");
            var escapedIcon = iconPath.Replace("'", "''");

            var psCommand = $"$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut('{escapedShortcut}'); $s.TargetPath = '{escapedTarget}'; $s.WorkingDirectory = '{escapedWorking}'; $s.IconLocation = '{escapedIcon},0'; $s.Description = 'DevOps Tools Installer'; $s.Save()";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{psCommand}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(3000);
            return File.Exists(shortcutPath);
        }
        catch
        {
            return false;
        }
    }
}
