using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>One installed side-by-side version on disk.</summary>
public sealed class SideBySideVersionInfo
{
    public string ToolId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string BinaryPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.Now;

    public string FileSizeFormatted =>
        FileSizeBytes > 1024 * 1024
            ? $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
            : $"{FileSizeBytes / 1024.0:F0} KB";
}

/// <summary>Result of detecting and applying project version files.</summary>
public sealed record ProjectVersionDetectionResult(
    string ProjectFolder,
    List<(string ToolId, string Version, bool Switched, string Message)> Results);

/// <summary>CLI binary shadowing diagnostics and resolution.</summary>
public sealed record CliShadowingReport(
    string ToolName,
    string ExecutableName,
    bool IsShadowed,
    string? ActivePath,
    string? ManagedPath,
    IReadOnlyList<string> AllPaths,
    string Advice);

/// <summary>
/// Side-by-side versions and version switcher (feature 2).
///
/// Keeps multiple versions of a CLI (such as kubectl 1.29 and 1.31, or two
/// Terraform versions) installed side-by-side and allows switching active versions
/// globally or per project (like tfenv, nvm, or asdf). Integrates with Doctor
/// shadowing checks to detect when another PATH directory masks the managed binary.
/// </summary>
public static class VersionSwitcherService
{
    private static readonly string VersionsBaseFolder = Path.Combine(
        ArtifactService.ToolsRoot, "versions");

    private static readonly string ActiveVersionsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "active-versions.json");

    private static readonly object Gate = new();

    public static string VersionsFolder => VersionsBaseFolder;

    /// <summary>
    /// Gets the folder where side-by-side binaries for a tool (and optionally a specific version) are stored.
    /// </summary>
    public static string GetVersionDir(string toolId, string? version = null)
    {
        var cleanId = toolId.Trim().ToLowerInvariant();
        var baseDir = Path.Combine(VersionsBaseFolder, cleanId);
        if (string.IsNullOrWhiteSpace(version))
        {
            Directory.CreateDirectory(baseDir);
            return baseDir;
        }

        var cleanVer = ProjectManifestService.NormalizeVersion(version);
        var verDir = Path.Combine(baseDir, cleanVer);
        Directory.CreateDirectory(verDir);
        return verDir;
    }

    /// <summary>
    /// Enumerates all side-by-side versions currently saved on disk for the given tool.
    /// </summary>
    public static List<SideBySideVersionInfo> GetInstalledVersions(ToolDefinition tool)
    {
        var list = new List<SideBySideVersionInfo>();
        try
        {
            var baseDir = Path.Combine(VersionsBaseFolder, tool.Id.ToLowerInvariant());
            var activeVer = GetActiveVersion(tool);
            var binPath = Path.Combine(ArtifactService.BinFolder, tool.FileName);
            string? activeHash = File.Exists(binPath) ? ComputeHash(binPath) : null;

            if (Directory.Exists(baseDir))
            {
                foreach (var verDir in Directory.GetDirectories(baseDir))
                {
                    var verName = Path.GetFileName(verDir);
                    var candidateExe = Path.Combine(verDir, tool.FileName);
                    if (File.Exists(candidateExe))
                    {
                        var fi = new FileInfo(candidateExe);
                        var candHash = ComputeHash(candidateExe);
                        bool isActive = (!string.IsNullOrWhiteSpace(activeVer) &&
                                         verName.Equals(ProjectManifestService.NormalizeVersion(activeVer), StringComparison.OrdinalIgnoreCase))
                                        || (activeHash != null && activeHash.Equals(candHash, StringComparison.OrdinalIgnoreCase));

                        list.Add(new SideBySideVersionInfo
                        {
                            ToolId = tool.Id,
                            Version = verName,
                            BinaryPath = candidateExe,
                            IsActive = isActive,
                            FileSizeBytes = fi.Length,
                            InstalledAt = fi.LastWriteTime
                        });
                    }
                }
            }

            // Also check the live Tools\bin executable if not yet represented
            if (File.Exists(binPath) && !string.IsNullOrWhiteSpace(activeVer))
            {
                var normActive = ProjectManifestService.NormalizeVersion(activeVer);
                if (!list.Any(v => v.Version.Equals(normActive, StringComparison.OrdinalIgnoreCase)))
                {
                    var fi = new FileInfo(binPath);
                    list.Add(new SideBySideVersionInfo
                    {
                        ToolId = tool.Id,
                        Version = normActive,
                        BinaryPath = binPath,
                        IsActive = true,
                        FileSizeBytes = fi.Length,
                        InstalledAt = fi.LastWriteTime
                    });
                }
            }
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn(tool.Name, $"Could not enumerate side-by-side versions: {ex.Message}");
        }

        return list.OrderByDescending(v => v.Version).ToList();
    }

    /// <summary>
    /// Gets the currently active version for a tool.
    /// </summary>
    public static string? GetActiveVersion(ToolDefinition tool)
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(ActiveVersionsFile))
                {
                    var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ActiveVersionsFile));
                    if (map != null && map.TryGetValue(tool.Id, out var saved) && !string.IsNullOrWhiteSpace(saved))
                    {
                        return ProjectManifestService.NormalizeVersion(saved);
                    }
                }
            }
            catch { }
        }

        return ProjectManifestService.TryGetInstalledVersion(tool);
    }

    /// <summary>
    /// Records a newly downloaded or installed version into the side-by-side storage.
    /// </summary>
    public static string StoreVersion(ToolDefinition tool, string version, string sourceFilePath)
    {
        var cleanVer = ProjectManifestService.NormalizeVersion(version);
        var destDir = GetVersionDir(tool.Id, cleanVer);
        var destFile = Path.Combine(destDir, tool.FileName);

        try
        {
            File.Copy(sourceFilePath, destFile, overwrite: true);
            ActivityLogService.Info(tool.Name, $"Stored side-by-side version v{cleanVer} at {destFile}");
            return destFile;
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn(tool.Name, $"Failed to store version v{cleanVer}: {ex.Message}");
            return sourceFilePath;
        }
    }

    /// <summary>
    /// Switches the active version of a tool by copying the target side-by-side version
    /// into Tools\bin so any terminal or script immediately sees the new version.
    /// </summary>
    public static async Task<(bool Success, string Message)> SwitchActiveVersionAsync(
        ToolDefinition tool,
        string targetVersion,
        IReadOnlyList<ToolDefinition>? catalog = null)
    {
        var cleanVer = ProjectManifestService.NormalizeVersion(targetVersion);
        var verDir = GetVersionDir(tool.Id, cleanVer);
        var versionExe = Path.Combine(verDir, tool.FileName);

        // If not already in side-by-side storage, download and store it!
        if (!File.Exists(versionExe))
        {
            if (!tool.SupportsVersionTargeting)
            {
                return (false, $"{tool.Name} does not support version targeting (vendor serves a single always-latest link).");
            }

            try
            {
                ActivityLogService.Info(tool.Name, $"Version v{cleanVer} is not stored locally. Downloading...");
                tool.SetVersion(cleanVer);
                var dl = new DownloadService();
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                await dl.DownloadAsync(tool, dlFolder);

                var dlFile = Path.Combine(dlFolder, tool.FileName);
                if (File.Exists(dlFile))
                {
                    versionExe = StoreVersion(tool, cleanVer, dlFile);
                }
                else
                {
                    return (false, $"Download for {tool.Name} v{cleanVer} did not produce expected file.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Failed to fetch version v{cleanVer}: {ex.Message}");
            }
        }

        // Copy into Tools\bin
        var binPath = Path.Combine(ArtifactService.BinFolder, tool.FileName);
        try
        {
            // Back up current version in side-by-side store if not already there
            var currentVer = GetActiveVersion(tool);
            if (File.Exists(binPath) && !string.IsNullOrWhiteSpace(currentVer))
            {
                var curNorm = ProjectManifestService.NormalizeVersion(currentVer);
                var curBackup = Path.Combine(GetVersionDir(tool.Id, curNorm), tool.FileName);
                if (!File.Exists(curBackup))
                {
                    try { File.Copy(binPath, curBackup, overwrite: true); } catch { }
                }
            }

            File.Copy(versionExe, binPath, overwrite: true);

            // Record active version in store
            lock (Gate)
            {
                Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(ActiveVersionsFile))
                    {
                        map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ActiveVersionsFile))
                              ?? new(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch { }

                map[tool.Id] = cleanVer;
                Directory.CreateDirectory(Path.GetDirectoryName(ActiveVersionsFile)!);
                File.WriteAllText(ActiveVersionsFile, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
            }

            InstallHistoryService.Record(HistoryAction.Update, tool.Id, tool.Name, cleanVer, "version-switch");
            ActivityLogService.Success(tool.Name, $"Switched active version to v{cleanVer}");

            ToastService.Show(
                $"{tool.Name} Switched",
                $"Active version switched to v{cleanVer} in {ArtifactService.BinFolder}.");

            return (true, $"Switched {tool.Name} to v{cleanVer}");
        }
        catch (Exception ex)
        {
            var msg = $"Could not switch active version for {tool.Name}: {ex.Message}. Close running instances and retry.";
            ActivityLogService.Error(tool.Name, msg);
            return (false, msg);
        }
    }

    /// <summary>
    /// Detects project version files in a repository folder (e.g. devtools.json,
    /// .terraform-version, .kubectl-version, or .tool-versions) and switches the
    /// active versions to match.
    /// </summary>
    public static async Task<ProjectVersionDetectionResult> DetectAndSwitchProjectVersionsAsync(
        string projectFolder,
        IReadOnlyList<ToolDefinition> catalog)
    {
        var results = new List<(string ToolId, string Version, bool Switched, string Message)>();
        if (!Directory.Exists(projectFolder))
        {
            return new ProjectVersionDetectionResult(projectFolder, results);
        }

        var catalogMap = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        // 1. devtools.json
        var devtoolsPath = Path.Combine(projectFolder, "devtools.json");
        if (File.Exists(devtoolsPath))
        {
            var (manifest, _) = ProjectManifestService.Load(devtoolsPath);
            if (manifest != null)
            {
                foreach (var item in manifest.Tools)
                {
                    if (ProjectManifestService.IsUnpinned(item.Version)) continue;
                    if (catalogMap.TryGetValue(item.Id, out var tool))
                    {
                        var (ok, msg) = await SwitchActiveVersionAsync(tool, item.Version, catalog);
                        results.Add((tool.Id, item.Version, ok, msg));
                    }
                }
            }
        }

        // 2. .terraform-version (used by tfenv)
        var tfVerFile = Path.Combine(projectFolder, ".terraform-version");
        if (File.Exists(tfVerFile))
        {
            var ver = File.ReadAllText(tfVerFile).Trim();
            if (!string.IsNullOrWhiteSpace(ver) && catalogMap.TryGetValue("terraform", out var tfTool))
            {
                var (ok, msg) = await SwitchActiveVersionAsync(tfTool, ver, catalog);
                results.Add((tfTool.Id, ver, ok, $"(.terraform-version) {msg}"));
            }
        }

        // 3. .kubectl-version
        var k8sVerFile = Path.Combine(projectFolder, ".kubectl-version");
        if (File.Exists(k8sVerFile))
        {
            var ver = File.ReadAllText(k8sVerFile).Trim();
            if (!string.IsNullOrWhiteSpace(ver) && catalogMap.TryGetValue("kubectl", out var k8sTool))
            {
                var (ok, msg) = await SwitchActiveVersionAsync(k8sTool, ver, catalog);
                results.Add((k8sTool.Id, ver, ok, $"(.kubectl-version) {msg}"));
            }
        }

        // 4. .tool-versions (asdf-style)
        var asdfFile = Path.Combine(projectFolder, ".tool-versions");
        if (File.Exists(asdfFile))
        {
            foreach (var line in File.ReadAllLines(asdfFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && catalogMap.TryGetValue(parts[0], out var tool))
                {
                    var ver = parts[1];
                    var (ok, msg) = await SwitchActiveVersionAsync(tool, ver, catalog);
                    results.Add((tool.Id, ver, ok, $"(.tool-versions) {msg}"));
                }
            }
        }

        return new ProjectVersionDetectionResult(projectFolder, results);
    }

    /// <summary>
    /// Checks if an external PATH entry is shadowing the managed binary.
    /// </summary>
    public static CliShadowingReport InspectCliShadowing(string toolName, string executableName)
    {
        var rawProcPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var orderedDirs = new List<string>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in rawProcPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exp = Environment.ExpandEnvironmentVariables(dir).TrimEnd('\\', '/');
            if (Directory.Exists(exp) && seenDirs.Add(exp))
            {
                orderedDirs.Add(exp);
            }
        }

        var matches = new List<string>();
        foreach (var dir in orderedDirs)
        {
            var candidate = Path.Combine(dir, executableName);
            if (File.Exists(candidate))
            {
                matches.Add(candidate);
            }
        }

        var managedBinPath = Path.Combine(ArtifactService.BinFolder, executableName);
        var managedExists = File.Exists(managedBinPath);

        if (matches.Count == 0)
        {
            return new CliShadowingReport(
                toolName, executableName, false, null, managedExists ? managedBinPath : null, matches,
                "Tool executable is not present on PATH.");
        }

        var active = matches[0];
        bool isShadowed = managedExists && !string.Equals(active, managedBinPath, StringComparison.OrdinalIgnoreCase);

        string advice = isShadowed
            ? $"External executable at '{active}' takes precedence over managed binary at '{managedBinPath}'. Reorder PATH or use 'Fix PATH' to make DevOpsToolsInstaller active."
            : matches.Count > 1
                ? $"Active at '{active}'. Subsequent duplicates at {string.Join(", ", matches.Skip(1))} are masked."
                : "Active on PATH with no shadowing.";

        return new CliShadowingReport(
            toolName, executableName, isShadowed, active, managedExists ? managedBinPath : null, matches, advice);
    }

    private static string ComputeHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}
