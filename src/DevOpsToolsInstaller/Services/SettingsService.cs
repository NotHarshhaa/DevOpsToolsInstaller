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

/// <summary>
/// Window material (system backdrop) applied to the title bar, sidebar and
/// window chrome. The window content itself always sits on an opaque,
/// theme-aware surface so the wallpaper never bleeds through behind pages.
/// </summary>
public enum WindowMaterial
{
    /// <summary>Neutral, subtle Mica tint (default). Least wallpaper influence.</summary>
    Mica,
    /// <summary>Deeper, more saturated Mica Alt tint.</summary>
    MicaAlt,
    /// <summary>Translucent acrylic blur.</summary>
    Acrylic,
    /// <summary>No system material — a fully opaque, solid theme background.</summary>
    None
}

/// <summary>
/// How strictly the app treats vendor installers with missing or untrusted
/// digital signatures before they are launched.
/// </summary>
public enum SignaturePolicy
{
    /// <summary>Show a confirmation warning before launching (default).</summary>
    Warn,
    /// <summary>Refuse to launch installers without a verified trusted signature.</summary>
    BlockUnsigned
}

public static class SettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "settings.json");

    public static AppTheme Theme { get; set; } = AppTheme.Default;

    /// <summary>
    /// Window material for the title bar and sidebar. Defaults to the neutral
    /// <see cref="WindowMaterial.Mica"/> so the chrome does not pick up a strong
    /// wallpaper tint that clashes with the opaque content surface.
    /// </summary>
    public static WindowMaterial WindowMaterial { get; set; } = WindowMaterial.Mica;

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

    /// <summary>How unsigned/untrusted vendor installers are handled before launch.</summary>
    public static SignaturePolicy SignaturePolicy { get; set; } = SignaturePolicy.Warn;

    /// <summary>Route all HTTP(S) traffic (downloads, catalog, update checks) through a proxy.</summary>
    public static bool ProxyEnabled { get; set; } = false;

    // ── Window & session state ───────────────────────────────────────────
    /// <summary>Persisted window bounds as "x,y,w,h" in Win32 screen pixels.</summary>
    public static string WindowBounds { get; set; } = string.Empty;
    /// <summary>Last visited page tag (Home/Catalog/…), restored on launch.</summary>
    public static string LastPage { get; set; } = string.Empty;
    /// <summary>Whether the first-run TeachingTip onboarding should be offered.</summary>
    public static bool ShowOnboardingTips { get; set; } = true;
    /// <summary>Set once the onboarding sequence has been fully shown.</summary>
    public static bool OnboardingCompleted { get; set; } = false;

    /// <summary>Proxy address, e.g. "http://proxy.corp.local:8080".</summary>
    public static string ProxyUrl { get; set; } = string.Empty;

    /// <summary>Optional proxy credentials. Stored in the local settings file.</summary>
    public static string ProxyUsername { get; set; } = string.Empty;
    public static string ProxyPassword { get; set; } = string.Empty;

    /// <summary>Maximum download bandwidth limit in MB/s. 0 means unlimited.</summary>
    public static int BandwidthLimitMBps { get; set; } = 0;

    /// <summary>Optional custom or private catalog feed URL or local json file path.</summary>
    public static string CustomCatalogSource { get; set; } = string.Empty;

    /// <summary>
    /// Master switch for user-authored pre/post-install hooks. Off by default so
    /// no shell command ever runs unless the user explicitly opts in.
    /// </summary>
    public static bool HooksEnabled { get; set; } = false;

    /// <summary>
    /// Optional sync folder (OneDrive/Dropbox/network share) used to merge
    /// profiles, custom tools, hooks and snapshots across machines.
    /// </summary>
    public static string SyncFolder { get; set; } = string.Empty;

    /// <summary>Timestamp of the last successful profile sync.</summary>
    public static DateTime? LastSyncTime { get; set; }

    /// <summary>Schedule artifact downloads during off-peak hours only.</summary>
    public static bool DownloadScheduleEnabled { get; set; } = false;

    /// <summary>Off-peak schedule start hour (0-23, 24-hour format). Default is 23 (11 PM).</summary>
    public static int ScheduleStartHour { get; set; } = 23;

    /// <summary>Off-peak schedule end hour (0-23, 24-hour format). Default is 7 (7 AM).</summary>
    public static int ScheduleEndHour { get; set; } = 7;

    /// <summary>Automatically retry failed downloads after network drops or transient errors.</summary>
    public static bool AutoRetryFailedDownloads { get; set; } = true;

    /// <summary>Interval in minutes between automatic download retries.</summary>
    public static int AutoRetryIntervalMinutes { get; set; } = 10;

    /// <summary>Collapsible bottom log drawer height in pixels (default 260).</summary>
    public static double LogDrawerHeight { get; set; } = 260.0;

    /// <summary>Whether the bottom log drawer is currently open.</summary>
    public static bool LogDrawerIsOpen { get; set; } = false;

    /// <summary>Whether live log drawer auto-scroll is enabled.</summary>
    public static bool LogDrawerAutoScroll { get; set; } = true;

    /// <summary>Active level filter in the live log drawer ("All", "Info", "Warning", "Error").</summary>
    public static string LogDrawerLevelFilter { get; set; } = "All";

    public static bool IsWithinScheduleWindow()
    {
        if (!DownloadScheduleEnabled) return true;
        var nowHour = DateTime.Now.Hour;
        if (ScheduleStartHour <= ScheduleEndHour)
        {
            return nowHour >= ScheduleStartHour && nowHour < ScheduleEndHour;
        }
        else
        {
            return nowHour >= ScheduleStartHour || nowHour < ScheduleEndHour;
        }
    }

    public static string GetScheduleWindowDescription()
    {
        var startAmPm = DateTime.Today.AddHours(ScheduleStartHour).ToString("h:mm tt");
        var endAmPm = DateTime.Today.AddHours(ScheduleEndHour).ToString("h:mm tt");
        return $"{startAmPm} – {endAmPm}";
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

                if (doc.RootElement.TryGetProperty("WindowMaterial", out var materialProp) &&
                    Enum.TryParse<WindowMaterial>(materialProp.GetString(), out var material))
                {
                    WindowMaterial = material;
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

                if (doc.RootElement.TryGetProperty("SignaturePolicy", out var sigProp) &&
                    Enum.TryParse<SignaturePolicy>(sigProp.GetString(), out var policy))
                {
                    SignaturePolicy = policy;
                }

                if (doc.RootElement.TryGetProperty("ProxyEnabled", out var proxyEnabledProp))
                {
                    ProxyEnabled = proxyEnabledProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("ProxyUrl", out var proxyUrlProp))
                {
                    ProxyUrl = proxyUrlProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("ProxyUsername", out var proxyUserProp))
                {
                    ProxyUsername = proxyUserProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("ProxyPassword", out var proxyPassProp))
                {
                    ProxyPassword = proxyPassProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("WindowBounds", out var boundsProp))
                {
                    WindowBounds = boundsProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("LastPage", out var lastPageProp))
                {
                    LastPage = lastPageProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("ShowOnboardingTips", out var onboardingProp))
                {
                    ShowOnboardingTips = onboardingProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("OnboardingCompleted", out var onboardingDoneProp))
                {
                    OnboardingCompleted = onboardingDoneProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("BandwidthLimitMBps", out var bwProp))
                {
                    BandwidthLimitMBps = bwProp.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("CustomCatalogSource", out var catSrcProp))
                {
                    CustomCatalogSource = catSrcProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("HooksEnabled", out var hooksProp))
                {
                    HooksEnabled = hooksProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("SyncFolder", out var syncFolderProp))
                {
                    SyncFolder = syncFolderProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("LastSyncTime", out var lastSyncProp) &&
                    DateTime.TryParse(lastSyncProp.GetString(), out var lastSync))
                {
                    LastSyncTime = lastSync;
                }

                if (doc.RootElement.TryGetProperty("DownloadScheduleEnabled", out var schedProp))
                {
                    DownloadScheduleEnabled = schedProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("ScheduleStartHour", out var sStartProp))
                {
                    ScheduleStartHour = sStartProp.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("ScheduleEndHour", out var sEndProp))
                {
                    ScheduleEndHour = sEndProp.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("AutoRetryFailedDownloads", out var retryProp))
                {
                    AutoRetryFailedDownloads = retryProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("AutoRetryIntervalMinutes", out var rIntProp))
                {
                    AutoRetryIntervalMinutes = rIntProp.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("LogDrawerHeight", out var ldHProp))
                {
                    LogDrawerHeight = Math.Clamp(ldHProp.GetDouble(), 120.0, 700.0);
                }

                if (doc.RootElement.TryGetProperty("LogDrawerIsOpen", out var ldOpenProp))
                {
                    LogDrawerIsOpen = ldOpenProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("LogDrawerAutoScroll", out var ldScProp))
                {
                    LogDrawerAutoScroll = ldScProp.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("LogDrawerLevelFilter", out var ldFiltProp))
                {
                    var filt = ldFiltProp.GetString();
                    if (!string.IsNullOrWhiteSpace(filt)) LogDrawerLevelFilter = filt;
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
                WindowMaterial = WindowMaterial.ToString(),
                DownloadsFolder = DownloadsFolder,
                CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
                LastUpdateCheckTime = LastUpdateCheckTime?.ToString("o"),
                EnableNotifications = EnableNotifications,
                CloseToTray = CloseToTray,
                SignaturePolicy = SignaturePolicy.ToString(),
                ProxyEnabled = ProxyEnabled,
                ProxyUrl = ProxyUrl,
                ProxyUsername = ProxyUsername,
                ProxyPassword = ProxyPassword,
                WindowBounds = WindowBounds,
                LastPage = LastPage,
                ShowOnboardingTips = ShowOnboardingTips,
                OnboardingCompleted = OnboardingCompleted,
                BandwidthLimitMBps = BandwidthLimitMBps,
                CustomCatalogSource = CustomCatalogSource,
                HooksEnabled = HooksEnabled,
                SyncFolder = SyncFolder,
                LastSyncTime = LastSyncTime?.ToString("o"),
                DownloadScheduleEnabled = DownloadScheduleEnabled,
                ScheduleStartHour = ScheduleStartHour,
                ScheduleEndHour = ScheduleEndHour,
                AutoRetryFailedDownloads = AutoRetryFailedDownloads,
                AutoRetryIntervalMinutes = AutoRetryIntervalMinutes,
                LogDrawerHeight = LogDrawerHeight,
                LogDrawerIsOpen = LogDrawerIsOpen,
                LogDrawerAutoScroll = LogDrawerAutoScroll,
                LogDrawerLevelFilter = LogDrawerLevelFilter
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
