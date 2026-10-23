using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// One tool line inside a <c>devtools.json</c> project manifest.
/// </summary>
public sealed class ProjectManifestTool
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Exact required version ("1.30.5"), or "latest" for "any current build".</summary>
    public string Version { get; set; } = "latest";
}

/// <summary>
/// The project manifest checked into a repository: the exact tools and versions
/// the project needs. Committed alongside the code so a new teammate can get
/// ready in minutes and CI can detect drift.
/// </summary>
public sealed class ProjectManifest
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Optional human project name recorded in lockfiles for traceability.</summary>
    public string Project { get; set; } = string.Empty;

    public List<ProjectManifestTool> Tools { get; set; } = new();
}

/// <summary>One resolved tool inside a <c>devtools.lock.json</c>.</summary>
public sealed class ProjectManifestLockEntry
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>SHA-256 of the installed binary (or the catalog's published hash when the file is not locally available).</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Where the hash came from: "computed" (from the installed file) or "catalog" (published checksum).</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// The lockfile: the manifest resolved to concrete file names, URLs and hashes.
/// Committed next to devtools.json so CI can verify a workstation's binaries
/// byte-for-byte, not just by version string.
/// </summary>
public sealed class ProjectManifestLock
{
    public int SchemaVersion { get; set; } = 1;
    public string Project { get; set; } = string.Empty;
    public string GeneratedAt { get; set; } = string.Empty;
    public string InstallerVersion { get; set; } = string.Empty;
    public List<ProjectManifestLockEntry> Tools { get; set; } = new();
}

/// <summary>Status of one link between the manifest and this workstation.</summary>
public enum ManifestToolState
{
    /// <summary>Required version is already on the machine (verified by version string or file hash).</summary>
    Satisfied,

    /// <summary>The tool is not installed at all.</summary>
    Missing,

    /// <summary>The tool is installed, but a different version than the manifest requires.</summary>
    VersionMismatch,

    /// <summary>Required version cannot be pinned because the vendor serves a single always-latest link.</summary>
    VersionNotTargetable,
}

/// <summary>One row of the plan: what the manifest wants versus what the machine has.</summary>
public sealed record ManifestPlanItem(ToolDefinition Tool, string RequiredVersion, ManifestToolState State, string Detail)
{
    public bool NeedsInstall =>
        State is ManifestToolState.Missing or ManifestToolState.VersionMismatch or ManifestToolState.VersionNotTargetable;
}

/// <summary>The complete plan before anything is applied.</summary>
public sealed record ManifestPlan(
    IReadOnlyList<ManifestPlanItem> Items,
    IReadOnlyList<string> UnknownIds,
    string ProjectName)
{
    public int InstallCount => Items.Count(i => i.NeedsInstall);
    public int SatisfiedCount => Items.Count(i => i.State == ManifestToolState.Satisfied);
}

/// <summary>Outcome of applying a plan.</summary>
public sealed record ManifestApplyResult(
    int Installed,
    int DownloadedOnly,
    int Failed,
    IReadOnlyList<string> Notes,
    List<ProjectManifestTool> InstalledToolSpecs)
{
    public bool AllOK => Failed == 0;
}

/// <summary>Outcome of a CI drift check.</summary>
public sealed record ManifestCheckResult(
    IReadOnlyList<string> Lines,
    int OK,
    int Drifted,
    int Unknown)
{
    public int ExitCode => Drifted > 0 || Unknown > 0 ? 1 : 0;
    public bool AllOK => Drifted == 0 && Unknown == 0;
}

/// <summary>
/// Reads, writes, plans and verifies project manifests (devtools.json + devtools.lock.json).
///
/// This is the feature that turns the installer into a team tool: a repository
/// lists the exact tools and versions its work needs, "install what this project
/// needs" brings a new machine up in minutes, and CI fails when a workstation
/// drifts from the lockfile.
/// </summary>
public static class ProjectManifestService
{
    public const string ManifestFileName = "devtools.json";
    public const string LockFileName = "devtools.lock.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Accepts a file path, a repo folder, or empty for the current directory.</summary>
    public static string ResolveManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Equals(".", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(ManifestFileName);
        }
        if (Directory.Exists(path))
        {
            return Path.Combine(path, ManifestFileName);
        }
        return path;
    }

    /// <summary>
    /// Loads the manifest without touching workstation state. Returns a
    /// descriptive error instead of throwing so both UI and CLI can show it.
    /// </summary>
    public static (ProjectManifest? Manifest, string Error) Load(string pathArg)
    {
        try
        {
            var path = ResolveManifestPath(pathArg);
            if (!File.Exists(path))
            {
                return (null,
                    $"No {ManifestFileName} found at '{path}'. Keep one in the repository root — " +
                    $"the 'Export manifest' action in the app writes one from your current install.");
            }

            var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(path), ReadOptions);
            if (manifest is null || manifest.Tools.Count == 0)
            {
                return (null, $"'{path}' exists but lists no tools.");
            }

            // Deduplicate by id, keep the first occurrence, drop blank ids.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            manifest.Tools = manifest.Tools
                .Where(t => !string.IsNullOrWhiteSpace(t.Id) && seen.Add(t.Id))
                .ToList();

            return (manifest, string.Empty);
        }
        catch (Exception ex)
        {
            return (null, $"Could not read {ManifestFileName}: {ex.Message}");
        }
    }

    /// <summary>Strips a leading "v"/"V" and whitespace so "v1.31.2" matches "1.31.2".</summary>
    public static string NormalizeVersion(string version) =>
        (version ?? string.Empty).Trim().TrimStart('v', 'V').Trim();

    /// <summary>True when the manifest deliberately does not pin this tool.</summary>
    public static bool IsUnpinned(string version)
    {
        var v = (version ?? string.Empty).Trim();
        return v.Length == 0 || v.Equals("latest", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Best-effort installed version for a tool: registry (installer-kind
    /// tools) first, then the install history (binaries installed by the app).
    /// </summary>
    public static string? TryGetInstalledVersion(ToolDefinition tool)
    {
        var fromRegistry = UninstallService.GetInstalledVersion(tool);
        if (!string.IsNullOrWhiteSpace(fromRegistry))
        {
            return NormalizeVersion(fromRegistry);
        }

        var history = InstallHistoryService.GetInstalledVersions(tool.Id);
        if (history.Count > 0)
        {
            return NormalizeVersion(history[0]);
        }

        var exeName = VersionSwitcherService.GetExecutableName(tool);
        var binPath = Path.Combine(ArtifactService.BinFolder, exeName);
        if (File.Exists(binPath) || File.Exists(Path.Combine(ArtifactService.BinFolder, tool.FileName)))
        {
            // Present on disk but the app has no version record (installed
            // manually or by another tool) — unknown rather than missing.
            return null;
        }

        return null;
    }

    /// <summary>
    /// Builds the install plan: every manifest tool compared against the
    /// current workstation, plus any manifest ids that are not in the catalog.
    /// When <paramref name="manifestPath"/> is given, lockfile hashes sharpen
    /// the verdict byte-for-byte.
    /// </summary>
    public static ManifestPlan BuildPlan(ProjectManifest manifest, IReadOnlyList<ToolDefinition> catalog, string? manifestPath = null)
    {
        var items = new List<ManifestPlanItem>();
        var unknown = new List<string>();
        var downloadService = new DownloadService();
        var dlFolder = DownloadService.DefaultDownloadsFolder;

        foreach (var entry in manifest.Tools)
        {
            var tool = catalog.FirstOrDefault(t => t.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (tool is null)
            {
                unknown.Add(entry.Id);
                continue;
            }

            var required = NormalizeVersion(entry.Version);
            var pinned = !IsUnpinned(required) && !required.Equals("latest", StringComparison.OrdinalIgnoreCase);
            var onDisk = UninstallService.IsInstalled(tool, dlFolder);
            var current = VersionSwitcherService.GetActiveVersion(tool) ?? TryGetInstalledVersion(tool);

            ManifestToolState state;
            string detail;

            if (!onDisk)
            {
                state = ManifestToolState.Missing;
                detail = pinned ? $"needs {required}" : "not installed";
            }
            else if (!pinned)
            {
                state = ManifestToolState.Satisfied;
                detail = string.IsNullOrWhiteSpace(current) ? "installed (unpinned)" : $"installed {current}";
            }
            else if (tool.SupportsVersionTargeting)
            {
                // Version-level comparison first (fast, works without a lockfile).
                var satisfiedByHistory = current is not null && current.Equals(required, StringComparison.OrdinalIgnoreCase);
                var hashVerdict = manifestPath is null ? null : CheckLockHash(entry.Id, tool, manifestPath);   // null when no lock entry/file; true/false when comparable

                if (satisfiedByHistory && hashVerdict is null)
                {
                    state = ManifestToolState.Satisfied;
                    detail = $"installed {current}";
                }
                else if (satisfiedByHistory && hashVerdict == true)
                {
                    state = ManifestToolState.Satisfied;
                    detail = $"installed {current} (hash matches lockfile)";
                }
                else if (satisfiedByHistory && hashVerdict == false)
                {
                    state = ManifestToolState.VersionMismatch;
                    detail = $"installed {current} but binary differs from the lockfile hash";
                }
                else if (current is null)
                {
                    state = ManifestToolState.VersionMismatch;
                    detail = $"installed but version unknown (requires {required})";
                }
                else
                {
                    state = ManifestToolState.VersionMismatch;
                    detail = $"installed {current}, requires {required}";
                }
            }
            else
            {
                // Vendor serves a single always-latest link: pinning is not possible.
                state = ManifestToolState.VersionNotTargetable;
                detail = pinned
                    ? $"requires {required} but this vendor only serves the latest build — will install latest"
                    : "will install latest (vendor link is always-current)";
            }

            items.Add(new ManifestPlanItem(tool, required, state, detail));
        }

        return new ManifestPlan(items, unknown, manifest.Project);
    }

    /// <summary>
    /// Applies a plan: downloads and installs every item that needs it
    /// (or only downloads when <paramref name="downloadOnly"/>). Non-pinned
    /// items install the catalog's current build; pinned items are targeted via
    /// the version picker machinery (<see cref="ToolDefinition.SetVersion"/>).
    /// </summary>
    public static async Task<ManifestApplyResult> ApplyAsync(
        ManifestPlan plan,
        bool downloadOnly = false,
        IProgress<double>? progress = null,
        Action<string>? onLog = null)
    {
        var download = new DownloadService();
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var notes = new List<string>();
        var installedSpecs = new List<ProjectManifestTool>();
        int installed = 0, downloadedOnly = 0, failed = 0;

        foreach (var item in plan.Items.Where(i => i.NeedsInstall))
        {
            var tool = item.Tool;

            // Target the exact version before downloading.
            if (!IsUnpinned(item.RequiredVersion) && tool.SupportsVersionTargeting)
            {
                tool.SetVersion(item.RequiredVersion);
            }
            if (item.State == ManifestToolState.VersionNotTargetable)
            {
                notes.Add($"{tool.Id}: version cannot be pinned (vendor serves a single always-latest link); installing the latest build.");
            }

            void Log(string message)
            {
                notes.Add(message);
                onLog?.Invoke(message);
                ActivityLogService.Info(tool.Name, $"[manifest] {message}");
            }

            try
            {
                onLog?.Invoke($"Downloading {tool.Name} {(IsUnpinned(item.RequiredVersion) ? "(latest)" : item.RequiredVersion)}…");
        // Avoid expensive full-file reads while downloading; log at 25% steps.
                var toolProgress = new Progress<double>(pct => { if (progress is not null) progress.Report(pct); });
                await download.DownloadAsync(tool, dlFolder, toolProgress);

                if (tool.Status != ToolStatus.Downloaded)
                {
                    failed++;
                    Log($"{tool.Id}: download did not complete.");
                    continue;
                }

                if (downloadOnly)
                {
                    downloadedOnly++;
                    installedSpecs.Add(new ProjectManifestTool { Id = tool.Id, Version = tool.SelectedVersion });
                    Log($"{tool.Id}: downloaded to the Downloads folder (no install).");
                    continue;
                }

                var result = ArtifactService.Perform(tool, dlFolder);
                if (result.Success)
                {
                    installed++;
                    installedSpecs.Add(new ProjectManifestTool { Id = tool.Id, Version = tool.SelectedVersion });
                    Log($"{tool.Id}: installed.");
                }
                else
                {
                    failed++;
                    Log($"{tool.Id}: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                Log($"{tool.Id}: {ex.Message}");
            }
        }

        return new ManifestApplyResult(installed, downloadedOnly, failed, notes, installedSpecs);
    }

    /// <summary>
    /// Writes a devtools.json to disk (the export path: capture the tools the
    /// machine currently has in the exact shape a repository expects).
    /// </summary>
    public static void WriteManifest(string path, ProjectManifest manifest)
    {
        if (manifest.Tools.Count == 0)
        {
            throw new InvalidOperationException(
                $"Nothing to export: no installed tools detected. Install something first.");
        }

        if (Directory.Exists(path))
        {
            path = Path.Combine(path, ManifestFileName);
        }
        var parentDir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, WriteOptions));
        ActivityLogService.Success("Project manifest", $"Wrote {path} ({manifest.Tools.Count} tools).");
    }

    /// <summary>
    /// Builds the current workstation manifest: every installed tool with its
    /// best-known version ("latest" when the version cannot be determined).
    /// </summary>
    public static ProjectManifest ExportManifest(
        IReadOnlyList<ToolDefinition> catalog, string projectName = "")
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var manifest = new ProjectManifest { Project = projectName };

        foreach (var tool in catalog)
        {
            var exeName = VersionSwitcherService.GetExecutableName(tool);
            bool inBin = !string.IsNullOrEmpty(exeName) && File.Exists(Path.Combine(ArtifactService.BinFolder, exeName));

            if (!UninstallService.IsInstalled(tool, dlFolder) && !inBin && !File.Exists(Path.Combine(ArtifactService.BinFolder, tool.FileName)))
            {
                continue;
            }

            var version = VersionSwitcherService.GetActiveVersion(tool) ?? TryGetInstalledVersion(tool);
            manifest.Tools.Add(new ProjectManifestTool
            {
                Id = tool.Id,
                Version = string.IsNullOrWhiteSpace(version) ? "latest" : version,
            });
        }

        return manifest;
    }

    /// <summary>
    /// Writes devtools.lock.json next to the manifest: concrete file names,
    /// download URLs and SHA-256 hashes for everything the plan installed.
    /// </summary>
    public static string WriteLock(string manifestPath, ProjectManifest manifest, IReadOnlyList<ToolDefinition> catalog)
    {
        var lockObj = new ProjectManifestLock
        {
            Project = manifest.Project,
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            InstallerVersion = AppUpdaterService.CurrentVersion,
        };

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        foreach (var entry in manifest.Tools)
        {
            var tool = catalog.FirstOrDefault(t => t.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            var lockEntry = new ProjectManifestLockEntry
            {
                Id = entry.Id,
                Version = NormalizeVersion(entry.Version),
            };

            if (tool is not null)
            {
                lockEntry.DownloadUrl = tool.DownloadUrl;
                lockEntry.FileName = tool.FileName;

                var exeName = VersionSwitcherService.GetExecutableName(tool);
                var binFile = Path.Combine(ArtifactService.BinFolder, exeName);
                var dlFile = Path.Combine(dlFolder, tool.FileName);

                if (File.Exists(binFile))
                {
                    lockEntry.Sha256 = ComputeSha256(binFile);
                    lockEntry.Source = "computed";
                }
                else if (File.Exists(dlFile))
                {
                    lockEntry.Sha256 = ComputeSha256(dlFile);
                    lockEntry.Source = "computed";
                }
                else if (!string.IsNullOrWhiteSpace(tool.Sha256))
                {
                    lockEntry.Sha256 = tool.Sha256;
                    lockEntry.Source = "catalog";
                }
            }

            lockObj.Tools.Add(lockEntry);
        }

        var dir = Directory.Exists(manifestPath) ? manifestPath : Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, LockFileName);
        File.WriteAllText(lockPath, JsonSerializer.Serialize(lockObj, WriteOptions));
        ActivityLogService.Success("Project manifest", $"Wrote {lockPath} ({lockObj.Tools.Count} hashed tools).");
        return lockPath;
    }

    /// <summary>Loads the lockfile, returns null (with no error) when it doesn't exist yet.</summary>
    public static ProjectManifestLock? LoadLock(string pathArg)
    {
        try
        {
            var path = ResolveManifestPath(pathArg);
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;
            var lockPath = Path.Combine(dir, LockFileName);
            if (!File.Exists(lockPath))
            {
                return null;
            }
            return JsonSerializer.Deserialize<ProjectManifestLock>(File.ReadAllText(lockPath), ReadOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The CI drift check: compares the manifest (and lockfile hashes) against
    /// what is actually installed. Never downloads or installs anything.
    /// </summary>
    public static ManifestCheckResult Check(string pathArg, IReadOnlyList<ToolDefinition> catalog)
    {
        var lines = new List<string>();
        int ok = 0, drifted = 0, unknown = 0;

        var (manifest, error) = Load(pathArg);
        if (manifest is null)
        {
            return new ManifestCheckResult(new[] { $"devtools check FAILED: {error}" }, 0, 1, 0);
        }

        var path = ResolveManifestPath(pathArg);
        var lockObj = LoadLock(path);
        lines.Add($"devtools check: {Path.GetFileName(path)}{(string.IsNullOrWhiteSpace(manifest.Project) ? "" : $" (project '{manifest.Project}')")}");

        foreach (var entry in manifest.Tools)
        {
            var tool = catalog.FirstOrDefault(t => t.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (tool is null)
            {
                lines.Add($"  {entry.Id,-22} UNKNOWN   id not in the catalog");
                unknown++;
                continue;
            }

            var required = NormalizeVersion(entry.Version);
            var pinned = !IsUnpinned(entry.Version);
            var name = tool.Name;
            var onDisk = UninstallService.IsInstalled(tool, DownloadService.DefaultDownloadsFolder);

            if (!onDisk)
            {
                lines.Add($"  {name,-22} MISSING   install it to match the project");
                drifted++;
                continue;
            }

            if (!pinned)
            {
                lines.Add($"  {name,-22} OK        installed (unpinned)");
                ok++;
                continue;
            }

            if (!tool.SupportsVersionTargeting)
            {
                lines.Add($"  {name,-22} OK        installed (vendor link is always-latest; pinned {required} not enforceable)");
                ok++;
                continue;
            }

            var current = TryGetInstalledVersion(tool);
            var versionOk = current is not null && current.Equals(required, StringComparison.OrdinalIgnoreCase);
            var lockHashOk = CheckLockHash(entry.Id, tool, path);

            if (versionOk && lockHashOk is not false)
            {
                lines.Add($"  {name,-22} OK        installed {current}");
                ok++;
            }
            else if (lockHashOk == false && versionOk)
            {
                lines.Add($"  {name,-22} DRIFT     installed {current} but binary differs from the lockfile hash");
                drifted++;
            }
            else if (current is null)
            {
                lines.Add($"  {name,-22} DRIFT     version could not be verified (requires {required})");
                drifted++;
            }
            else
            {
                lines.Add($"  {name,-22} DRIFT     installed {current}, requires {required}");
                drifted++;
            }
        }

        lines.Add("---");
        lines.Add(lockObj is null
            ? $"  (no {LockFileName} yet — pin one with --export-manifest or the Catalog > Project manifest dialog for stronger CI checks)"
            : $"  lockfile {LockFileName}: {lockObj.Tools.Count} tools hashed, generated {lockObj.GeneratedAt}");
        lines.Add($"Result: {ok} OK, {drifted} drifted, {unknown} unknown → exit {((drifted > 0 || unknown > 0) ? 1 : 0)}");

        return new ManifestCheckResult(lines, ok, drifted, unknown);
    }

    // ────────────────────────────────────────────────────────────── lock hash

    private static string? _lockHashesDir;
    private static Dictionary<string, string>? _lockHashes;

    /// <summary>Loads (and caches) the lockfile's hash table for fast iterative checks.</summary>
    private static Dictionary<string, string>? GetLockHashes(string manifestPathArg)
    {
        try
        {
            var path = ResolveManifestPath(manifestPathArg);
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (string.Equals(_lockHashesDir, dir, StringComparison.OrdinalIgnoreCase) && _lockHashes is not null)
            {
                return _lockHashes;
            }

            var lockObj = LoadLock(path);
            if (lockObj is null)
            {
                _lockHashesDir = dir;   // remember the miss so repeated checks don't re-read the disk
                _lockHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return null;
            }

            _lockHashesDir = dir;
            _lockHashes = lockObj.Tools
                .Where(t => !string.IsNullOrWhiteSpace(t.Sha256))
                .ToDictionary(t => t.Id, t => t.Sha256, StringComparer.OrdinalIgnoreCase);
            return _lockHashes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies the tool's installed binary against the lockfile hash.
    /// Returns null when there is nothing to compare (no lockfile, no hash
    /// entry, or the file is not on disk) — a "cannot verify" verdict, not a drift.
    /// </summary>
    private static bool? CheckLockHash(string toolId, ToolDefinition tool, string manifestPath)
    {
        try
        {
            var hashes = GetLockHashes(manifestPath);
            if (hashes is null || !hashes.TryGetValue(toolId, out var expected))
            {
                return null;
            }

            var binFile = Path.Combine(ArtifactService.BinFolder, tool.FileName);
            if (!File.Exists(binFile))
            {
                return null;
            }

            var actual = ComputeSha256(binFile);
            return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
