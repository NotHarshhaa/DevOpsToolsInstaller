using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.ApplicationModel;

namespace DevOpsToolsInstaller.Services;

public sealed record AppReleaseInfo(
    string Version,
    string TagName,
    string Title,
    string Body,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    bool IsUpdateAvailable,
    string AssetName,
    string AssetDownloadUrl,
    long AssetSizeBytes,
    string? ExpectedSha256
);

public sealed record AppUpdateProgress(
    long BytesReceived,
    long TotalBytes,
    double Percent,
    double SpeedMBps,
    string StatusText
);

/// <summary>
/// Handles checking for updates against GitHub releases, downloading
/// architecture-appropriate binaries, verifying checksums, and executing seamless
/// in-place application updates.
/// </summary>
public static class AppUpdaterService
{
    public const string GitHubRepoOwner = "NotHarshhaa";
    public const string GitHubRepoName = "DevOpsToolsInstaller";
    public const string GitHubReleasesPageUrl = $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}/releases";
    private const string GitHubApiLatestReleaseUrl =
        $"https://api.github.com/repos/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";

    private static readonly HttpClient Http;

    static AppUpdaterService()
    {
        Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DevOpsToolsInstaller/{CurrentVersion} (Windows NT 10.0; Win64; x64)");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
    }

    /// <summary>
    /// Gets the current running application version.
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "2.1.0";
        }
    }

    /// <summary>
    /// Returns true when the app is running as a packaged MSIX application
    /// (e.g. installed via the Microsoft Store). In this case the OS handles
    /// all updates automatically through the Store and GitHub self-updates
    /// should be suppressed.
    /// </summary>
    public static bool IsRunningAsPackagedApp
    {
        get
        {
            try
            {
                // Package.Current throws InvalidOperationException for unpackaged apps.
                var _ = Package.Current;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Returns true when the application was installed via the Windows Setup Wizard
    /// (Inno Setup) rather than running as a standalone portable single-file binary.
    /// </summary>
    public static bool IsInstalledViaSetup
    {
        get
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                // Inno Setup places unins000.exe in the application installation directory.
                if (File.Exists(Path.Combine(baseDir, "unins000.exe")))
                {
                    return true;
                }

                // Also check if running from standard Program Files directory paths
                var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(progFiles) && baseDir.StartsWith(progFiles, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(progFilesX86) && baseDir.StartsWith(progFilesX86, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Checks the GitHub Releases API for the latest published release.
    /// Returns null immediately when the app is running as a packaged MSIX
    /// (Microsoft Store manages updates in that case).
    /// </summary>
    public static async Task<AppReleaseInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        // When installed from the Microsoft Store, updates are managed by the
        // Store automatically — suppress the GitHub updater entirely.
        if (IsRunningAsPackagedApp)
            return null;

        try
        {
            var response = await Http.GetAsync(GitHubApiLatestReleaseUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
            var title = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : tagName;
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString() ?? GitHubReleasesPageUrl
                : GitHubReleasesPageUrl;
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            DateTimeOffset? publishedAt = null;
            if (root.TryGetProperty("published_at", out var pubProp) &&
                pubProp.TryGetDateTimeOffset(out var pubDate))
            {
                publishedAt = pubDate;
            }

            var latestCleanVersion = CleanVersionString(tagName);
            var currentCleanVersion = CleanVersionString(CurrentVersion);
            var isUpdateAvailable = IsNewerVersion(latestCleanVersion, currentCleanVersion);

            // Asset discovery: find binary matching machine architecture and installation flavor
            var isArm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            var targetKeyword = isArm64 ? "arm64" : "x64";
            var wantsSetup = IsInstalledViaSetup;

            string bestAssetName = "";
            string bestAssetUrl = "";
            long bestAssetSize = 0;
            string? checksumsUrl = null;

            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                var exeAssets = new List<(string Name, string Url, long Size)>();

                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var aName = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                    var aUrl = asset.TryGetProperty("browser_download_url", out var uProp) ? uProp.GetString() ?? "" : "";
                    var aSize = asset.TryGetProperty("size", out var sProp) ? sProp.GetInt64() : 0L;

                    if (aName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                        aName.Contains("sum", StringComparison.OrdinalIgnoreCase))
                    {
                        checksumsUrl = aUrl;
                        continue;
                    }

                    if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exeAssets.Add((aName, aUrl, aSize));
                    }
                }

                // Flavor selection strategy:
                // - Setup installation: prioritizes setup wizard installer (*setup*.exe)
                // - Portable application: prioritizes standalone binary (excludes *setup*.exe)
                (string Name, string Url, long Size) chosen = default;

                if (wantsSetup)
                {
                    chosen = exeAssets.FirstOrDefault(a =>
                        a.Name.Contains(targetKeyword, StringComparison.OrdinalIgnoreCase) &&
                        a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));

                    if (string.IsNullOrEmpty(chosen.Url))
                    {
                        chosen = exeAssets.FirstOrDefault(a =>
                            a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));
                    }
                }
                else
                {
                    chosen = exeAssets.FirstOrDefault(a =>
                        a.Name.Contains(targetKeyword, StringComparison.OrdinalIgnoreCase) &&
                        !a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));

                    if (string.IsNullOrEmpty(chosen.Url))
                    {
                        chosen = exeAssets.FirstOrDefault(a =>
                            !a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase));
                    }
                }

                // Universal fallback if preferred flavor was not present in the release assets
                if (string.IsNullOrEmpty(chosen.Url))
                {
                    chosen = exeAssets.FirstOrDefault(a =>
                        a.Name.Contains(targetKeyword, StringComparison.OrdinalIgnoreCase));
                }
                if (string.IsNullOrEmpty(chosen.Url) && exeAssets.Count > 0)
                {
                    chosen = exeAssets[0];
                }

                if (!string.IsNullOrEmpty(chosen.Url))
                {
                    bestAssetName = chosen.Name;
                    bestAssetUrl = chosen.Url;
                    bestAssetSize = chosen.Size;
                }
            }

            // If a checksums file is provided, download and parse the SHA256 for the chosen asset
            string? expectedSha256 = null;
            if (!string.IsNullOrEmpty(checksumsUrl) && !string.IsNullOrEmpty(bestAssetName))
            {
                expectedSha256 = await FetchExpectedSha256Async(checksumsUrl, bestAssetName, ct);
            }

            // Record check timestamp in settings
            SettingsService.LastUpdateCheckTime = DateTime.Now;
            SettingsService.SaveSettings();

            return new AppReleaseInfo(
                Version: latestCleanVersion,
                TagName: tagName,
                Title: string.IsNullOrWhiteSpace(title) ? $"Release {tagName}" : title,
                Body: body,
                HtmlUrl: htmlUrl,
                PublishedAt: publishedAt,
                IsUpdateAvailable: isUpdateAvailable,
                AssetName: bestAssetName,
                AssetDownloadUrl: bestAssetUrl,
                AssetSizeBytes: bestAssetSize,
                ExpectedSha256: expectedSha256
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the updated binary to a dedicated update cache folder, reporting progress.
    /// </summary>
    public static async Task<string> DownloadUpdateAsync(
        AppReleaseInfo release,
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(release.AssetDownloadUrl))
        {
            throw new InvalidOperationException("No download asset is available for this release.");
        }

        var updateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevOpsToolsInstaller", "Updates", release.Version);

        Directory.CreateDirectory(updateDir);

        var targetFileName = !string.IsNullOrWhiteSpace(release.AssetName)
            ? release.AssetName
            : $"DevOpsToolsInstaller_{release.Version}.exe";

        var targetFilePath = Path.Combine(updateDir, targetFileName);

        using var response = await Http.GetAsync(release.AssetDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? release.AssetSizeBytes;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        var sw = Stopwatch.StartNew();
        long lastReportBytes = 0;
        var lastReportTime = sw.ElapsedMilliseconds;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalBytesRead += bytesRead;

            var elapsed = sw.ElapsedMilliseconds;
            if (elapsed - lastReportTime >= 200 || totalBytesRead == totalBytes)
            {
                var deltaBytes = totalBytesRead - lastReportBytes;
                var deltaTimeSec = (elapsed - lastReportTime) / 1000.0;
                var speedMBps = deltaTimeSec > 0 ? (deltaBytes / (1024.0 * 1024.0)) / deltaTimeSec : 0;

                double percent = totalBytes > 0 ? ((double)totalBytesRead / totalBytes) * 100.0 : 0;
                var readMB = totalBytesRead / (1024.0 * 1024.0);
                var totalMB = totalBytes / (1024.0 * 1024.0);

                var status = totalBytes > 0
                    ? $"{readMB:F1} MB / {totalMB:F1} MB ({percent:F0}%) • {speedMBps:F1} MB/s"
                    : $"{readMB:F1} MB downloaded • {speedMBps:F1} MB/s";

                progress?.Report(new AppUpdateProgress(totalBytesRead, totalBytes, percent, speedMBps, status));

                lastReportBytes = totalBytesRead;
                lastReportTime = elapsed;
            }
        }

        await fileStream.FlushAsync(ct);
        fileStream.Close();

        // Verify SHA256 checksum if provided
        if (!string.IsNullOrWhiteSpace(release.ExpectedSha256))
        {
            var calculatedHash = await ComputeSha256Async(targetFilePath, ct);
            if (!string.Equals(calculatedHash, release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(targetFilePath); } catch { }
                throw new InvalidOperationException(
                    $"Integrity verification failed for {targetFileName}. Expected SHA256 {release.ExpectedSha256} but computed {calculatedHash}.");
            }
        }

        progress?.Report(new AppUpdateProgress(totalBytesRead, totalBytes, 100, 0, "Download and integrity verification complete!"));
        return targetFilePath;
    }

    /// <summary>
    /// Executes the update and restarts the application.
    /// If the running app was installed via Setup Wizard (or downloaded a Setup installer),
    /// the installer is launched with administrative elevation to upgrade Program Files cleanly.
    /// For portable standalone binaries, a background script replaces the executable.
    /// </summary>
    public static void ApplyUpdateAndRestart(string downloadedExePath)
    {
        if (!File.Exists(downloadedExePath))
        {
            throw new FileNotFoundException("Downloaded update executable not found.", downloadedExePath);
        }

        var isSetup = IsInstalledViaSetup ||
                      Path.GetFileName(downloadedExePath).Contains("setup", StringComparison.OrdinalIgnoreCase);

        if (isSetup)
        {
            var psi = new ProcessStartInfo
            {
                FileName = downloadedExePath,
                Arguments = "/CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(downloadedExePath) ?? ""
            };

            try
            {
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // The user cancelled the UAC elevation prompt (ERROR_CANCELLED = 1223).
                // Do not terminate the application.
                return;
            }
            catch
            {
                // If elevation fails or is not supported, attempt standard launch
                psi.Verb = "";
                try
                {
                    Process.Start(psi);
                }
                catch
                {
                    return;
                }
            }

            // Terminate current application cleanly so the setup wizard can update files without locks
            try
            {
                Microsoft.UI.Xaml.Application.Current?.Exit();
            }
            catch
            {
                Environment.Exit(0);
            }
            return;
        }

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
        {
            currentExe = Path.Combine(AppContext.BaseDirectory, "DevOpsToolsInstaller.exe");
        }

        var currentPid = Process.GetCurrentProcess().Id;
        var tempScript = Path.Combine(Path.GetTempPath(), $"dti_updater_{Guid.NewGuid():N}.cmd");

        // Write batch script that safely waits for this process to exit, swaps the exe,
        // launches the new version, and self-deletes.
        var scriptContent =
            "@echo off\r\n" +
            "setlocal enabledelayedexpansion\r\n" +
            "chcp 65001 >nul\r\n" +
            $"set PID={currentPid}\r\n" +
            $"set NEW_EXE=\"{downloadedExePath}\"\r\n" +
            $"set TARGET_EXE=\"{currentExe}\"\r\n" +
            "set RETRIES=0\r\n" +
            "\r\n" +
            ":WAIT_PID\r\n" +
            "tasklist /fi \"PID eq %PID%\" 2>nul | findstr /i \"%PID%\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "    timeout /t 1 /nobreak >nul\r\n" +
            "    goto WAIT_PID\r\n" +
            ")\r\n" +
            "\r\n" +
            "timeout /t 1 /nobreak >nul\r\n" +
            "\r\n" +
            ":RETRY_COPY\r\n" +
            "copy /y %NEW_EXE% %TARGET_EXE% >nul 2>&1\r\n" +
            "if errorlevel 1 (\r\n" +
            "    timeout /t 1 /nobreak >nul\r\n" +
            "    set /a RETRIES+=1\r\n" +
            "    if !RETRIES! lss 12 goto RETRY_COPY\r\n" +
            ")\r\n" +
            "\r\n" +
            "start \"\" %TARGET_EXE%\r\n" +
            "del %NEW_EXE% >nul 2>&1\r\n" +
            "(goto) 2>nul & del \"%~f0\"\r\n";

        File.WriteAllText(tempScript, scriptContent);

        var psiPortable = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{tempScript}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };

        Process.Start(psiPortable);

        // Terminate current application cleanly
        try
        {
            Microsoft.UI.Xaml.Application.Current?.Exit();
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Cleans and formats version strings (e.g. 'v2.0.0' -> '2.0.0').
    /// </summary>
    public static string CleanVersionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "0.0.0";
        var match = Regex.Match(raw, @"\d+(\.\d+)+");
        return match.Success ? match.Value : raw.TrimStart('v', 'V').Trim();
    }

    /// <summary>
    /// Performs semantic version comparison. Returns true if <paramref name="latest"/>
    /// is strictly newer than <paramref name="current"/>.
    /// </summary>
    public static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var vLatest) &&
            Version.TryParse(current, out var vCurrent))
        {
            return vLatest > vCurrent;
        }

        // Fallback segment comparison (e.g. for versions with extra segments)
        var latestParts = latest.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var currentParts = current.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

        var maxLen = Math.Max(latestParts.Length, currentParts.Length);
        for (int i = 0; i < maxLen; i++)
        {
            var l = i < latestParts.Length ? latestParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;
            if (l > c) return true;
            if (l < c) return false;
        }

        return false;
    }

    private static async Task<string?> FetchExpectedSha256Async(string checksumsUrl, string assetName, CancellationToken ct)
    {
        try
        {
            var text = await Http.GetStringAsync(checksumsUrl, ct);
            using var reader = new StringReader(text);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                // Format: <hash>  <filename>
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1].Trim().Equals(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    return parts[0].Trim();
                }
            }
        }
        catch
        {
            // Optional checksum verification - continue if checksum file cannot be fetched
        }
        return null;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
