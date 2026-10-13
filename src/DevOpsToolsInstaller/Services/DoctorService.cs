using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public enum DoctorStatus
{
    Passed,
    Warning,
    Failed
}

public sealed class DoctorCheckResult
{
    public string Id { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public DoctorStatus Status { get; set; } = DoctorStatus.Passed;
    public string Details { get; init; } = string.Empty;

    public bool HasFixAction { get; init; }
    public string FixButtonText { get; init; } = "Fix";
    public string FixDialogTitle { get; init; } = string.Empty;
    public string FixDialogMessage { get; init; } = string.Empty;
    public Func<Task<bool>>? FixAction { get; init; }

    public bool HasLearnMore { get; init; }
    public string LearnMoreTitle { get; init; } = "Learn more";
    public string LearnMoreDetails { get; init; } = string.Empty;
    public string LearnMoreUrl { get; init; } = string.Empty;
}

public sealed class DoctorReport
{
    public DateTime ScanTime { get; init; } = DateTime.Now;
    public int PassedCount => Checks.Count(c => c.Status == DoctorStatus.Passed);
    public int WarningCount => Checks.Count(c => c.Status == DoctorStatus.Warning);
    public int FailedCount => Checks.Count(c => c.Status == DoctorStatus.Failed);
    public int TotalCount => Checks.Count;
    public IReadOnlyList<DoctorCheckResult> Checks { get; init; } = Array.Empty<DoctorCheckResult>();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DevOps Workstation Doctor Diagnostic Report");
        sb.AppendLine($"**Generated on:** {ScanTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Summary:** {PassedCount} passed, {WarningCount} warning(s), {FailedCount} failed ({TotalCount} checks total)");
        sb.AppendLine();

        foreach (var group in Checks.GroupBy(c => c.Category))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var item in group)
            {
                var icon = item.Status switch
                {
                    DoctorStatus.Passed => "[PASS]",
                    DoctorStatus.Warning => "[WARN]",
                    DoctorStatus.Failed => "[FAIL]",
                    _ => "[INFO]"
                };
                sb.AppendLine($"- **{icon} {item.Title}**");
                sb.AppendLine($"  {item.Explanation}");
                if (!string.IsNullOrWhiteSpace(item.Details))
                {
                    sb.AppendLine($"  > {item.Details.Replace("\n", "\n  > ")}");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public static class DoctorService
{
    private static readonly string[] MonitoredClis =
    {
        "kubectl.exe",
        "terraform.exe",
        "helm.exe",
        "docker.exe",
        "gh.exe",
        "k9s.exe",
        "aws.exe",
        "az.cmd",
        "trivy.exe",
        "git.exe"
    };

    public static async Task<DoctorReport> RunDiagnosticsAsync(
        IReadOnlyList<ToolDefinition>? catalogTools = null,
        CancellationToken ct = default)
    {
        var results = new List<DoctorCheckResult>();

        await Task.Run(async () =>
        {
            // ── 1. PATH Conflicts and Duplicates ────────────────────────
            CheckPathConflictsAndDuplicates(results);

            // ── 2. Missing Prerequisites ────────────────────────────────
            CheckPrerequisites(results, catalogTools);

            // ── 3. Multiple Versions of the Same CLI ────────────────────
            CheckCliBinaryShadowing(results);

            // ── 4. Outdated Installed Tools vs Catalog ──────────────────
            await CheckOutdatedToolsAsync(results, catalogTools, ct);

            // ── 5. Tools Installed but Not on PATH ──────────────────────
            CheckToolsInstalledNotOnPath(results);
        }, ct);

        return new DoctorReport
        {
            ScanTime = DateTime.Now,
            Checks = results
        };
    }

    private static void CheckPathConflictsAndDuplicates(List<DoctorCheckResult> results)
    {
        var doc = PathDoctorService.Diagnose();

        if (doc.DuplicateEntries.Count > 0)
        {
            results.Add(new DoctorCheckResult
            {
                Id = "path-duplicates",
                Category = "Environment & PATH Health",
                Title = "PATH Duplicate Entries",
                Status = DoctorStatus.Warning,
                Explanation = $"{doc.DuplicateEntries.Count} duplicate folder(s) found in User PATH.",
                Details = string.Join("\n", doc.DuplicateEntries.Select(d => $"• {d}")),
                HasFixAction = true,
                FixButtonText = "Remove Duplicates",
                FixDialogTitle = "Clean Duplicate PATH Entries?",
                FixDialogMessage = $"This action will remove {doc.DuplicateEntries.Count} duplicate entries from your User PATH. An automated timestamped backup of your original PATH will be saved to %LOCALAPPDATA%\\DevOpsToolsInstaller\\Backups\\PATH.",
                FixAction = () =>
                {
                    var fixRes = PathDoctorService.FixUserPath(removeDuplicates: true, removeMissing: false, ensureBinFolder: true);
                    return Task.FromResult(fixRes.Success);
                }
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "path-duplicates",
                Category = "Environment & PATH Health",
                Title = "PATH Duplicate Entries",
                Status = DoctorStatus.Passed,
                Explanation = "No duplicate entries detected in User PATH."
            });
        }

        if (doc.MissingDirectories.Count > 0)
        {
            results.Add(new DoctorCheckResult
            {
                Id = "path-missing-dirs",
                Category = "Environment & PATH Health",
                Title = "Non-Existent PATH Directories",
                Status = DoctorStatus.Warning,
                Explanation = $"{doc.MissingDirectories.Count} non-existent folder(s) found on User PATH, slowing down binary searches.",
                Details = string.Join("\n", doc.MissingDirectories.Select(m => $"• {m}")),
                HasFixAction = true,
                FixButtonText = "Prune Dead Paths",
                FixDialogTitle = "Prune Non-Existent Directories from PATH?",
                FixDialogMessage = $"This will remove {doc.MissingDirectories.Count} invalid folder(s) that no longer exist on disk. A backup will be preserved before modification.",
                FixAction = () =>
                {
                    var fixRes = PathDoctorService.FixUserPath(removeDuplicates: true, removeMissing: true, ensureBinFolder: true);
                    return Task.FromResult(fixRes.Success);
                }
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "path-missing-dirs",
                Category = "Environment & PATH Health",
                Title = "Non-Existent PATH Directories",
                Status = DoctorStatus.Passed,
                Explanation = "All directories on User PATH exist on disk."
            });
        }
    }

    private static void CheckPrerequisites(List<DoctorCheckResult> results, IReadOnlyList<ToolDefinition>? catalogTools)
    {
        // Git check
        var gitPath = CliHealthService.ResolveExecutablePath("git.exe");
        if (!string.IsNullOrEmpty(gitPath) && File.Exists(gitPath))
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-git",
                Category = "Prerequisites & System Capabilities",
                Title = "Git for Windows",
                Status = DoctorStatus.Passed,
                Explanation = "Git is installed and available on PATH.",
                Details = $"Location: {gitPath}"
            });
        }
        else
        {
            var gitTool = catalogTools?.FirstOrDefault(t => t.Id.Equals("git", StringComparison.OrdinalIgnoreCase));
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-git",
                Category = "Prerequisites & System Capabilities",
                Title = "Git for Windows",
                Status = DoctorStatus.Warning,
                Explanation = "Git is not detected on PATH. Git is required for version control and repository cloning.",
                HasFixAction = gitTool != null,
                FixButtonText = "Install Git",
                FixDialogTitle = "Install Git for Windows?",
                FixDialogMessage = "DevOpsToolsInstaller will download the official Git for Windows installer from upstream and launch the setup wizard.",
                FixAction = () =>
                {
                    if (gitTool != null)
                    {
                        ToolActions.RunInBackground(gitTool, installAfter: true);
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);
                },
                HasLearnMore = true,
                LearnMoreUrl = "https://git-scm.com",
                LearnMoreDetails = "Download Git for Windows from https://git-scm.com or install directly from the Tool Catalog."
            });
        }

        // PowerShell 7+ check
        var pwshPath = CliHealthService.ResolveExecutablePath("pwsh.exe");
        if (!string.IsNullOrEmpty(pwshPath) && File.Exists(pwshPath))
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-pwsh",
                Category = "Prerequisites & System Capabilities",
                Title = "PowerShell 7+ (pwsh)",
                Status = DoctorStatus.Passed,
                Explanation = "Modern cross-platform PowerShell (pwsh) is installed.",
                Details = $"Location: {pwshPath}"
            });
        }
        else
        {
            var psTool = catalogTools?.FirstOrDefault(t => t.Id.Equals("powershell", StringComparison.OrdinalIgnoreCase));
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-pwsh",
                Category = "Prerequisites & System Capabilities",
                Title = "PowerShell 7+ (pwsh)",
                Status = DoctorStatus.Warning,
                Explanation = "PowerShell 7+ is not installed. Windows PowerShell 5.1 is active, which lacks modern cross-platform modules.",
                HasFixAction = psTool != null,
                FixButtonText = "Install PowerShell 7",
                FixDialogTitle = "Install Modern PowerShell 7?",
                FixDialogMessage = "DevOpsToolsInstaller will download and install the official Microsoft PowerShell 7+ package.",
                FixAction = () =>
                {
                    if (psTool != null)
                    {
                        ToolActions.RunInBackground(psTool, installAfter: true);
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);
                },
                HasLearnMore = true,
                LearnMoreUrl = "https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows"
            });
        }

        // Windows Terminal check
        var wtPath = CliHealthService.ResolveExecutablePath("wt.exe");
        if (!string.IsNullOrEmpty(wtPath) && File.Exists(wtPath))
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-terminal",
                Category = "Prerequisites & System Capabilities",
                Title = "Windows Terminal",
                Status = DoctorStatus.Passed,
                Explanation = "Windows Terminal is installed and ready.",
                Details = $"Location: {wtPath}"
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-terminal",
                Category = "Prerequisites & System Capabilities",
                Title = "Windows Terminal",
                Status = DoctorStatus.Warning,
                Explanation = "Windows Terminal is not found. Recommended for tabbed terminal environments.",
                HasLearnMore = true,
                LearnMoreUrl = "https://apps.microsoft.com/detail/9N0DX20HK701"
            });
        }

        // Windows Long Paths check
        bool longPathsEnabled = false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            if (key?.GetValue("LongPathsEnabled") is int val && val == 1)
            {
                longPathsEnabled = true;
            }
        }
        catch { }

        if (longPathsEnabled)
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-longpaths",
                Category = "Prerequisites & System Capabilities",
                Title = "Windows Long Path Support",
                Status = DoctorStatus.Passed,
                Explanation = "LongPathsEnabled is active in the Windows Registry."
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-longpaths",
                Category = "Prerequisites & System Capabilities",
                Title = "Windows Long Path Support",
                Status = DoctorStatus.Warning,
                Explanation = "Long file paths (> 260 chars) are disabled in Windows. Deep project directories in npm, Go, or Git may fail.",
                HasLearnMore = true,
                LearnMoreTitle = "Enable Instructions",
                LearnMoreDetails = "Run PowerShell as Administrator and execute:\nNew-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\FileSystem' -Name 'LongPathsEnabled' -Value 1 -PropertyType DWORD -Force"
            });
        }

        // Windows Package Manager (WinGet) check
        var wingetPath = CliHealthService.ResolveExecutablePath("winget.exe");
        if (!string.IsNullOrEmpty(wingetPath) && File.Exists(wingetPath))
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-winget",
                Category = "Prerequisites & System Capabilities",
                Title = "Windows Package Manager (WinGet)",
                Status = DoctorStatus.Passed,
                Explanation = "WinGet is installed and functional.",
                Details = $"Location: {wingetPath}"
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "prereq-winget",
                Category = "Prerequisites & System Capabilities",
                Title = "Windows Package Manager (WinGet)",
                Status = DoctorStatus.Warning,
                Explanation = "WinGet is not available on PATH.",
                HasLearnMore = true,
                LearnMoreUrl = "https://learn.microsoft.com/windows/package-manager/winget/"
            });
        }
    }

    private static void CheckCliBinaryShadowing(List<DoctorCheckResult> results)
    {
        var rawUserPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var rawSysPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
        var rawProcPath = Environment.GetEnvironmentVariable("PATH") ?? "";

        var orderedDirs = new List<string>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Process PATH defines the exact lookup order on Windows
        foreach (var dir in rawProcPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var exp = Environment.ExpandEnvironmentVariables(dir).TrimEnd('\\', '/');
            if (Directory.Exists(exp) && seenDirs.Add(exp))
            {
                orderedDirs.Add(exp);
            }
        }

        int conflictsCount = 0;
        var conflictDetails = new List<string>();

        foreach (var cli in MonitoredClis)
        {
            var matches = new List<string>();
            foreach (var dir in orderedDirs)
            {
                var candidate = Path.Combine(dir, cli);
                if (File.Exists(candidate))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count > 1)
            {
                conflictsCount++;
                var toolName = Path.GetFileNameWithoutExtension(cli);
                conflictDetails.Add($"• {toolName}: active at '{matches[0]}', shadowed at {string.Join(", ", matches.Skip(1).Select(m => $"'{m}'"))}");
            }
        }

        if (conflictsCount > 0)
        {
            results.Add(new DoctorCheckResult
            {
                Id = "cli-conflicts",
                Category = "CLI Binary Conflicts & Shadowing",
                Title = "Multiple CLI Versions on PATH",
                Status = DoctorStatus.Warning,
                Explanation = $"{conflictsCount} tool(s) exist in multiple directories on PATH. The earliest path masks subsequent ones.",
                Details = string.Join("\n", conflictDetails),
                HasLearnMore = true,
                LearnMoreTitle = "Conflict Details",
                LearnMoreDetails = "When multiple executables with the same name are present in different PATH folders, Windows executes the first one encountered. Remove the older folder from PATH or rename the shadowed binary to avoid confusion."
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "cli-conflicts",
                Category = "CLI Binary Conflicts & Shadowing",
                Title = "Multiple CLI Versions on PATH",
                Status = DoctorStatus.Passed,
                Explanation = "No shadowed or conflicting CLI binaries detected on PATH."
            });
        }
    }

    private static async Task CheckOutdatedToolsAsync(
        List<DoctorCheckResult> results,
        IReadOnlyList<ToolDefinition>? catalogTools,
        CancellationToken ct)
    {
        if (catalogTools == null || catalogTools.Count == 0)
        {
            results.Add(new DoctorCheckResult
            {
                Id = "tool-freshness",
                Category = "Version Freshness & Outdated Tools",
                Title = "Catalog Tool Versions",
                Status = DoctorStatus.Passed,
                Explanation = "Catalog not loaded. Skipping version freshness check."
            });
            return;
        }

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var outdatedList = new List<string>();

        // Sample installed tools up to 10 common tools to keep check fast
        var installedTools = catalogTools
            .Where(t => UninstallService.IsInstalled(t, dlFolder) || t.Status == ToolStatus.Downloaded)
            .Take(12)
            .ToList();

        foreach (var tool in installedTools)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var probe = await CliHealthService.ProbeToolAsync(tool, timeoutSeconds: 3, ct: ct);
                if (probe.Success && !string.IsNullOrWhiteSpace(probe.DetectedVersion))
                {
                    var detected = probe.DetectedVersion.TrimStart('v', 'V');
                    var target = tool.Version.TrimStart('v', 'V');

                    if (Version.TryParse(detected, out var dVer) &&
                        Version.TryParse(target, out var tVer) &&
                        dVer < tVer)
                    {
                        outdatedList.Add($"{tool.Name} (installed: v{detected}, catalog: v{target})");
                    }
                }
            }
            catch { }
        }

        if (outdatedList.Count > 0)
        {
            results.Add(new DoctorCheckResult
            {
                Id = "tool-freshness",
                Category = "Version Freshness & Outdated Tools",
                Title = "Outdated Installed Tools",
                Status = DoctorStatus.Warning,
                Explanation = $"{outdatedList.Count} installed tool(s) are older than the latest catalog release.",
                Details = string.Join("\n", outdatedList.Select(o => $"• {o}")),
                HasLearnMore = true,
                LearnMoreTitle = "How to Update",
                LearnMoreDetails = "Go to the Installed page or Tool Catalog in DevOpsToolsInstaller to update these tools to their latest vendor releases with verified hashes."
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "tool-freshness",
                Category = "Version Freshness & Outdated Tools",
                Title = "Outdated Installed Tools",
                Status = DoctorStatus.Passed,
                Explanation = "Installed tools are up to date with catalog releases."
            });
        }
    }

    private static void CheckToolsInstalledNotOnPath(List<DoctorCheckResult> results)
    {
        var binFolder = ArtifactService.BinFolder.TrimEnd('\\', '/');
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var procPath = Environment.GetEnvironmentVariable("PATH") ?? "";

        bool isBinOnUserPath = userPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => string.Equals(p.TrimEnd('\\', '/'), binFolder, StringComparison.OrdinalIgnoreCase));

        bool isBinOnProcPath = procPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(p => string.Equals(p.TrimEnd('\\', '/'), binFolder, StringComparison.OrdinalIgnoreCase));

        int binFileCount = 0;
        if (Directory.Exists(binFolder))
        {
            try { binFileCount = Directory.GetFiles(binFolder, "*.exe").Length; } catch { }
        }

        if (!isBinOnUserPath && !isBinOnProcPath)
        {
            results.Add(new DoctorCheckResult
            {
                Id = "tools-bin-on-path",
                Category = "PATH Integration & Discovery",
                Title = "Tools\\bin Directory on PATH",
                Status = DoctorStatus.Failed,
                Explanation = "The DevOpsToolsInstaller Tools\\bin directory is not on your PATH.",
                Details = binFileCount > 0
                    ? $"{binFileCount} CLI executable(s) exist in {binFolder} but cannot be executed from terminal without their full path."
                    : $"Folder: {binFolder}",
                HasFixAction = true,
                FixButtonText = "Add to User PATH",
                FixDialogTitle = "Add Tools\\bin to User PATH?",
                FixDialogMessage = $"This action will append '{binFolder}' to your User PATH and broadcast the update to active terminal sessions.",
                FixAction = () =>
                {
                    bool added = SettingsService.AddToUserPath(binFolder);
                    return Task.FromResult(added);
                }
            });
        }
        else
        {
            results.Add(new DoctorCheckResult
            {
                Id = "tools-bin-on-path",
                Category = "PATH Integration & Discovery",
                Title = "Tools\\bin Directory on PATH",
                Status = DoctorStatus.Passed,
                Explanation = "Tools\\bin directory is properly registered on PATH.",
                Details = $"Folder: {binFolder} ({binFileCount} CLI tool(s) found)"
            });
        }
    }
}
