using System.Diagnostics;
using System.Runtime.InteropServices;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Headless command-line interface. Allows provisioning scripts and CI jobs to
/// install tools without opening the UI:
///
///   DevOpsToolsInstaller.exe --list
///   DevOpsToolsInstaller.exe --status
///   DevOpsToolsInstaller.exe --install kubectl,terraform,helm
///   DevOpsToolsInstaller.exe --install-bundle k8s-starter
///   DevOpsToolsInstaller.exe --uninstall kubectl,terraform
///   DevOpsToolsInstaller.exe --update                          (all outdated)
///   DevOpsToolsInstaller.exe --download-only kubectl,terraform
///   DevOpsToolsInstaller.exe --export-profile profile.json
///   DevOpsToolsInstaller.exe --import-profile profile.json
///   DevOpsToolsInstaller.exe --generate-script bootstrap.ps1
///   DevOpsToolsInstaller.exe --import-winget winget-export.json
///   DevOpsToolsInstaller.exe --report workstation.md
///
/// The process exits with code 0 on success, 1 when any requested tool failed.
/// </summary>
public static class CliHost
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "--install", "--install-bundle", "--uninstall", "--update",
        "--download-only", "--download-only-bundle",
        "--export-profile", "--import-profile",
        "--import-winget", "--import-choco",
        "--generate-script", "--generate-devcontainer", "--report",
        "--path-doctor", "--check-path", "--fix-path",
        "--history", "--snapshot", "--restore-snapshot",
        "--profiles", "--capture-profile", "--apply-profile", "--sync-profiles",
        "--downloads-folder", "--run-hooks", "--list", "--status", "--version", "--help", "-h",
        "--manifest", "--install-manifest", "--check", "--check-manifest", "--export-manifest", "--lock-manifest",
        "--versions", "--switch", "--use", "--detect-project",
        "--export-migration", "--apply-migration",
        "--check-updates", "--cloud-status", "--kube-context",
        "--list-feeds", "--add-feed", "--remove-feed", "--sync-feeds",
        "--export-offline-bundle", "--install-offline-bundle",
        "--check-policy", "--apply-policy", "--export-policy-template", "--export-intune-script",
        "--backends", "--backend",
        "--wsl", "--wsl-fix", "--wsl-tools",
        "--env", "--set-env", "--del-env",
        "--git-status", "--setup-git", "--generate-ssh-key",
        "--setup-powershell"
    };

    public static bool IsCliRequest(IReadOnlyList<string> args)
        => args.Any(a => Commands.Contains(a));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    /// <summary>Runs the CLI flow and returns the process exit code.</summary>
    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        // Attach to the calling terminal so Console.WriteLine is visible even
        // though this is a WinExe (window subsystem) application. When output
        // is redirected (CI logs, pipes, files) the inherited std handle is
        // already correct — attaching would re-point output at the console
        // screen buffer and swallow it.
        if (!Console.IsOutputRedirected)
        {
            try { AttachConsole(ATTACH_PARENT_PROCESS); } catch { }
        }

        try
        {
            var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdOut);
            var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stdErr);
        }
        catch { }

        try
        {
            return await ExecuteAsync(args);
        }
        catch (Exception ex)
        {
            WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(IReadOnlyList<string> args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
        {
            return await PrintCatalogAsync();
        }

        if (args.Contains("--status", StringComparer.OrdinalIgnoreCase))
        {
            return await PrintStatusAsync();
        }

        if (args.Contains("--version", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-v", StringComparer.OrdinalIgnoreCase))
        {
            WriteLine($"DevOpsToolsInstaller v{AppUpdaterService.CurrentVersion}");
            return 0;
        }

        // Global option: redirect downloads to a custom folder (e.g. a USB
        // drive for offline caching) for the remainder of this run.
        var dlOverride = GetOptionValue(args, "--downloads-folder");
        if (!string.IsNullOrWhiteSpace(dlOverride))
        {
            try
            {
                SettingsService.DownloadsFolder = dlOverride;
                WriteLine($"Downloads folder: {DownloadService.DefaultDownloadsFolder}");
            }
            catch (Exception ex)
            {
                WriteLine($"[warn] could not use downloads folder '{dlOverride}': {ex.Message}");
            }
        }

        // Global option: allow user-authored pre/post-install hooks to run in
        // headless mode. Off by default so a script never triggers shell
        // commands the user did not explicitly enable.
        if (args.Contains("--run-hooks", StringComparer.OrdinalIgnoreCase))
        {
            SettingsService.HooksEnabled = true;
            WriteLine("Hooks enabled for this run (--run-hooks).");
        }

        var catalog = new CatalogService();
        var download = new DownloadService();
        var tools = await catalog.LoadCatalogAsync();

        var requestedTools = new List<ToolDefinition>();
        var knownIds = tools.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Equals("--install", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                foreach (var rawId in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (knownIds.TryGetValue(rawId, out var tool))
                    {
                        requestedTools.Add(tool);
                    }
                    else
                    {
                        errors.Add($"unknown tool id '{rawId}' (use --list to see available ids)");
                    }
                }
            }
            else if (arg.Equals("--install-bundle", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                var bundleId = args[++i];
                var bundles = await catalog.LoadBundlesAsync();
                var bundle = bundles.FirstOrDefault(b => string.Equals(b.Id, bundleId, StringComparison.OrdinalIgnoreCase));
                if (bundle is null)
                {
                    errors.Add($"unknown bundle id '{bundleId}' (available: {string.Join(", ", bundles.Take(10).Select(b => b.Id))})");
                    continue;
                }

                foreach (var tid in bundle.Tools)
                {
                    if (knownIds.TryGetValue(tid, out var tool))
                    {
                        requestedTools.Add(tool);
                    }
                    else
                    {
                        errors.Add($"bundle '{bundleId}' references unknown tool id '{tid}'");
                    }
                }
            }
            else if (arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await UninstallToolsAsync(
                    args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    knownIds);
            }
            else if (arg.Equals("--update", StringComparison.OrdinalIgnoreCase))
            {
                // With explicit ids: force-refresh those tools. Without: update
                // every installed tool that is behind the catalog version.
                List<string>? ids = null;
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--"))
                {
                    ids = args[++i]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                }
                return await UpdateToolsAsync(ids, tools);
            }
            else if (arg.Equals("--download-only", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await DownloadOnlyAsync(
                    args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    tools);
            }
            else if (arg.Equals("--download-only-bundle", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                var bundleId = args[++i];
                var bundles = await catalog.LoadBundlesAsync();
                var bundle = bundles.FirstOrDefault(b => string.Equals(b.Id, bundleId, StringComparison.OrdinalIgnoreCase));
                if (bundle is null)
                {
                    WriteLine($"error: unknown bundle id '{bundleId}' (available: {string.Join(", ", bundles.Take(10).Select(b => b.Id))})");
                    return 1;
                }
                return await DownloadOnlyAsync(bundle.Tools, tools);
            }
            else if (arg.Equals("--export-profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return ExportProfile(args[++i], tools);
            }
            else if (arg.Equals("--import-profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await ImportProfileAsync(args[++i], tools);
            }
            else if (arg.Equals("--import-winget", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await ImportFromPackageManagerAsync(args[++i], tools, PackageManagerSource.Winget);
            }
            else if (arg.Equals("--import-choco", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await ImportFromPackageManagerAsync(args[++i], tools, PackageManagerSource.Chocolatey);
            }
            else if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await ExportReportAsync(args[++i], tools);
            }
            else if (arg.Equals("--generate-script", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return GenerateBootstrapScript(args[++i], tools);
            }
            else if (arg.Equals("--generate-devcontainer", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return GenerateDevContainer(args[++i], tools);
            }
            else if (arg.Equals("--history", StringComparison.OrdinalIgnoreCase))
            {
                string? toolFilter = null;
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--"))
                {
                    toolFilter = args[++i];
                }
                return PrintHistory(toolFilter);
            }
            else if (arg.Equals("--snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return CaptureSnapshot(args[++i], tools);
            }
            else if (arg.Equals("--restore-snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await RestoreSnapshotAsync(args[++i], tools);
            }
            else if (arg.Equals("--profiles", StringComparison.OrdinalIgnoreCase))
            {
                return PrintProfiles();
            }
            else if (arg.Equals("--capture-profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return CaptureProfile(args[++i], tools);
            }
            else if (arg.Equals("--apply-profile", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await ApplyProfileAsync(args[++i], tools);
            }
            else if (arg.Equals("--sync-profiles", StringComparison.OrdinalIgnoreCase))
            {
                return RunSyncProfiles();
            }
            else if (arg.Equals("--path-doctor", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--check-path", StringComparison.OrdinalIgnoreCase))
            {
                return RunPathDoctor();
            }
            else if (arg.Equals("--fix-path", StringComparison.OrdinalIgnoreCase))
            {
                return RunFixPath();
            }
            else if (arg.Equals("--check", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--check-manifest", StringComparison.OrdinalIgnoreCase))
            {
                string manifestPath = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : ".";
                return RunManifestCheck(manifestPath, tools);
            }
            else if (arg.Equals("--manifest", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--install-manifest", StringComparison.OrdinalIgnoreCase))
            {
                string manifestPath = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : ".";
                return await RunInstallManifestAsync(manifestPath, tools);
            }
            else if (arg.Equals("--export-manifest", StringComparison.OrdinalIgnoreCase))
            {
                string destPath = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : "devtools.json";
                return RunExportManifest(destPath, tools);
            }
            else if (arg.Equals("--lock-manifest", StringComparison.OrdinalIgnoreCase))
            {
                string manifestPath = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : "devtools.json";
                return RunLockManifest(manifestPath, tools);
            }
            else if (arg.Equals("--versions", StringComparison.OrdinalIgnoreCase))
            {
                string? toolFilter = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : null;
                return RunListVersions(toolFilter, tools);
            }
            else if (arg.Equals("--switch", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--use", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 < args.Count)
                {
                    var toolId = args[++i];
                    var ver = args[++i];
                    return await RunSwitchVersionAsync(toolId, ver, tools);
                }
                WriteLine("error: --switch requires <toolId> <version> (e.g. --switch kubectl 1.29.2)");
                return 1;
            }
            else if (arg.Equals("--detect-project", StringComparison.OrdinalIgnoreCase))
            {
                string folder = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : ".";
                return await RunDetectProjectAsync(folder, tools);
            }
            else if (arg.Equals("--export-migration", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await RunExportMigrationAsync(args[++i], tools);
            }
            else if (arg.Equals("--apply-migration", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await RunApplyMigrationAsync(args[++i], tools);
            }
            else if (arg.Equals("--check-updates", StringComparison.OrdinalIgnoreCase))
            {
                return await RunCheckUpdatesAsync();
            }
            else if (arg.Equals("--cloud-status", StringComparison.OrdinalIgnoreCase))
            {
                return await RunCloudStatusAsync();
            }
            else if (arg.Equals("--kube-context", StringComparison.OrdinalIgnoreCase))
            {
                string? targetContext = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : null;
                return RunKubeContext(targetContext);
            }
            else if (arg.Equals("--backends", StringComparison.OrdinalIgnoreCase))
            {
                return RunBackends();
            }
            else if (arg.Equals("--list-feeds", StringComparison.OrdinalIgnoreCase))
            {
                return RunListFeeds();
            }
            else if (arg.Equals("--add-feed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                if (EnterprisePolicyService.AreSettingsLocked)
                {
                    WriteLine("error: subscribing to private feeds is locked by enterprise IT policy.");
                    return 1;
                }
                var feedName = args[++i];
                var feedUrl = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : feedName;
                if (feedUrl == feedName) feedName = "";
                return await RunAddFeedAsync(feedName, feedUrl);
            }
            else if (arg.Equals("--remove-feed", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return RunRemoveFeed(args[++i]);
            }
            else if (arg.Equals("--sync-feeds", StringComparison.OrdinalIgnoreCase))
            {
                return await RunSyncFeedsAsync();
            }
            else if (arg.Equals("--export-offline-bundle", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                var bundleId = args[++i];
                var dest = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : Path.Combine(".", $"bundle_{bundleId}");
                return await RunExportOfflineBundleAsync(bundleId, dest, tools);
            }
            else if (arg.Equals("--install-offline-bundle", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return await RunInstallOfflineBundleAsync(args[++i]);
            }
            else if (arg.Equals("--check-policy", StringComparison.OrdinalIgnoreCase))
            {
                return RunCheckPolicy();
            }
            else if (arg.Equals("--export-policy-template", StringComparison.OrdinalIgnoreCase))
            {
                var dest = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : "DevOpsTools-EnterprisePolicy.reg";
                return RunExportPolicyTemplate(dest);
            }
            else if (arg.Equals("--export-intune-script", StringComparison.OrdinalIgnoreCase))
            {
                string? bundleId = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : null;
                string dest = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : "deploy-intune.ps1";
                return await RunExportIntuneScriptAsync(bundleId, dest);
            }
            else if (arg.Equals("--wsl", StringComparison.OrdinalIgnoreCase))
            {
                return await RunWslAsync(checkTools: false);
            }
            else if (arg.Equals("--wsl-tools", StringComparison.OrdinalIgnoreCase))
            {
                return await RunWslAsync(checkTools: true);
            }
            else if (arg.Equals("--wsl-fix", StringComparison.OrdinalIgnoreCase))
            {
                return RunWslFix();
            }
            else if (arg.Equals("--env", StringComparison.OrdinalIgnoreCase))
            {
                return RunEnv();
            }
            else if (arg.Equals("--set-env", StringComparison.OrdinalIgnoreCase) && i + 2 < args.Count)
            {
                return RunSetEnv(args[++i], args[++i]);
            }
            else if (arg.Equals("--del-env", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return RunDelEnv(args[++i]);
            }
            else if (arg.Equals("--git-status", StringComparison.OrdinalIgnoreCase))
            {
                return await RunGitStatusAsync();
            }
            else if (arg.Equals("--setup-git", StringComparison.OrdinalIgnoreCase) && i + 2 < args.Count)
            {
                return await RunSetupGitAsync(args[++i], args[++i]);
            }
            else if (arg.Equals("--generate-ssh-key", StringComparison.OrdinalIgnoreCase))
            {
                string? email = (i + 1 < args.Count && !args[i + 1].StartsWith("--")) ? args[++i] : null;
                return await RunGenerateSshKeyAsync(email);
            }
            else if (arg.Equals("--setup-powershell", StringComparison.OrdinalIgnoreCase))
            {
                return await RunSetupPowerShellAsync();
            }
        }

        if (requestedTools.Count == 0 && errors.Count == 0)
        {
            PrintUsage();
            return errors.Count > 0 ? 1 : 0;
        }

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var backendArg = GetOptionValue(args, "--backend");
        var backend = !string.IsNullOrEmpty(backendArg) ? ToolBackendExtensions.Parse(backendArg) : SettingsService.DefaultBackend;
        if (backend != ToolBackend.Direct)
        {
            WriteLine($"Using alternative backend: {backend.DisplayName()}");
        }

        int installedCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        foreach (var tool in requestedTools)
        {
            if (backend != ToolBackend.Direct)
            {
                try
                {
                    WriteLine($"[{backend.ShortName().ToLowerInvariant()}] {tool.Name} ({ToolBackendService.GetPackageId(tool, backend)})");
                    var res = await ToolBackendService.InstallWithBackendAsync(tool, backend, dlFolder, msg => WriteLine($"       {msg}"));
                    if (res.Success)
                    {
                        installedCount++;
                        WriteLine($"[ok  ] {tool.Name}: {res.Message}");
                    }
                    else
                    {
                        failedCount++;
                        WriteLine($"[fail] {tool.Name}: {res.Message}");
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    WriteLine($"[fail] {tool.Name}: {ex.Message}");
                }
                continue;
            }

            if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded)
            {
                skippedCount++;
                WriteLine($"[skip] {tool.Name} ({tool.Id}) already installed");
                continue;
            }

            try
            {
                WriteLine($"[get ] {tool.Name} ({tool.FileName})");
                var progress = new Progress<double>(pct =>
                {
                    TryWrite($"\r       {tool.Name}: {pct:F0}%");
                });

                await download.DownloadAsync(tool, dlFolder, progress);

                if (tool.Status == ToolStatus.Downloaded)
                {
                    TryWrite("\r" + new string(' ', 60) + "\r");
                    var res = ArtifactService.Perform(tool, dlFolder);
                    if (res.Success)
                    {
                        installedCount++;
                        WriteLine($"[ok  ] {tool.Name}: {res.Message}");
                    }
                    else
                    {
                        failedCount++;
                        WriteLine($"[fail] {tool.Name}: {res.Message}");
                    }
                }
                else
                {
                    failedCount++;
                    WriteLine($"[fail] {tool.Name}: download did not complete");
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                TryWrite("\r" + new string(' ', 60) + "\r");
                WriteLine($"[fail] {tool.Name}: {ex.Message}");
            }
        }

        foreach (var err in errors)
        {
            WriteLine($"[warn] {err}");
        }

        WriteLine(string.Empty);
        WriteLine($"Done. {installedCount} installed, {skippedCount} already present, {failedCount} failed.");

        // Note: vendor installers for Installer-kind tools open their own UI.
        if (requestedTools.Any(t => t.Kind == ArtifactKind.Installer))
        {
            WriteLine("Note: vendor installers opened in their own window and may need your confirmation.");
        }

        return failedCount == 0 && errors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> PrintCatalogAsync()
    {
        var tools = await new CatalogService().LoadCatalogAsync();
        WriteLine($"DevOps Tools Installer v{AppUpdaterService.CurrentVersion} — {tools.Count} tools available");
        WriteLine();
        foreach (var tool in tools.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            WriteLine($"  {tool.Id.PadRight(24)} {tool.Name.PadRight(28)} {tool.Category}");
        }
        return 0;
    }

    private static async Task<int> PrintStatusAsync()
    {
        var tools = await new CatalogService().LoadCatalogAsync();
        var dlFolder = DownloadService.DefaultDownloadsFolder;

        WriteLine($"DevOps Tools Installer v{AppUpdaterService.CurrentVersion} — workstation status");
        WriteLine();

        int installed = 0;
        foreach (var tool in tools.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            bool isInstalled = await Task.Run(() => UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded);
            if (isInstalled)
            {
                installed++;
                string? version;
                if (tool.Kind == ArtifactKind.Installer)
                {
                    version = await Task.Run(() => UninstallService.GetInstalledVersion(tool));
                }
                else
                {
                    var probe = await CliHealthService.ProbeToolAsync(tool, timeoutSeconds: 2);
                    version = probe.DetectedVersion;
                }
                WriteLine($"  [x] {tool.Name.PadRight(28)} {tool.Category.PadRight(28)} {(string.IsNullOrWhiteSpace(version) ? "" : $"v{version}")}");
            }
        }

        WriteLine();
        WriteLine($"{installed} of {tools.Count} catalog tools installed.");
        return 0;
    }

    /// <summary>
    /// Handles the --uninstall command: removes each requested tool's artifacts
    /// (and launches the vendor uninstaller for Installer-kind tools).
    /// </summary>
    private static Task<int> UninstallToolsAsync(
        IEnumerable<string> toolIds,
        Dictionary<string, ToolDefinition> knownIds)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        int removedCount = 0;
        int missingCount = 0;
        int failedCount = 0;

        foreach (var rawId in toolIds)
        {
            if (!knownIds.TryGetValue(rawId, out var tool))
            {
                missingCount++;
                WriteLine($"[warn] unknown tool id '{rawId}' (use --list to see available ids)");
                continue;
            }

            if (!UninstallService.IsInstalled(tool, dlFolder))
            {
                missingCount++;
                WriteLine($"[skip] {tool.Name} ({tool.Id}) is not installed");
                continue;
            }

            var result = UninstallService.Uninstall(tool, dlFolder);
            if (result.Success)
            {
                removedCount++;
                WriteLine($"[ok  ] {tool.Name}: {result.Message}");
            }
            else
            {
                failedCount++;
                WriteLine($"[fail] {tool.Name}: {result.Message}");
            }
        }

        WriteLine(string.Empty);
        WriteLine($"Done. {removedCount} uninstalled, {missingCount} not present, {failedCount} failed.");
        return Task.FromResult(failedCount == 0 ? 0 : 1);
    }

    /// <summary>
    /// Handles the --update command. With explicit ids, force-refreshes those
    /// tools; without ids, updates every installed tool whose detected version
    /// is behind the catalog.
    /// </summary>
    private static async Task<int> UpdateToolsAsync(List<string>? toolIds, List<ToolDefinition> tools)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var knownIds = tools.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var targets = new List<ToolDefinition>();
        var errors = new List<string>();

        if (toolIds is not null)
        {
            foreach (var rawId in toolIds)
            {
                if (knownIds.TryGetValue(rawId, out var tool))
                {
                    targets.Add(tool);
                }
                else
                {
                    errors.Add($"unknown tool id '{rawId}' (use --list to see available ids)");
                }
            }
        }
        else
        {
            WriteLine("Scanning installed tools for updates...");
            var installed = tools
                .Where(t => UninstallService.IsInstalled(t, dlFolder) || t.Status == ToolStatus.Downloaded)
                .ToList();

            var updates = await new ToolUpdateService().CheckForUpdatesAsync(installed);
            if (updates.Count == 0)
            {
                WriteLine($"All {installed.Count} installed tools are up to date.");
                return 0;
            }

            foreach (var upd in updates)
            {
                targets.Add(upd.Tool);
                WriteLine($"[upd ] {upd.Name}: {upd.CurrentVersion} → {upd.NewVersion}");
            }
        }

        var download = new DownloadService();
        int updated = 0;
        int failed = 0;

        foreach (var tool in targets)
        {
            try
            {
                // Drop the previously downloaded artifact so version-pinned
                // file names fetch the new release instead of short-circuiting.
                DownloadService.DiscardDownloadedArtifact(tool, dlFolder);

                WriteLine($"[get ] {tool.Name} ({tool.FileName})");
                await download.DownloadAsync(tool, dlFolder);

                if (tool.Status == ToolStatus.Downloaded)
                {
                    var res = ArtifactService.Perform(tool, dlFolder);
                    if (res.Success)
                    {
                        updated++;
                        tool.IsInstalled = true;
                        WriteLine($"[ok  ] {tool.Name}: {res.Message}");
                    }
                    else
                    {
                        failed++;
                        WriteLine($"[fail] {tool.Name}: {res.Message}");
                    }
                }
                else
                {
                    failed++;
                    WriteLine($"[fail] {tool.Name}: download did not complete");
                }
            }
            catch (Exception ex)
            {
                failed++;
                WriteLine($"[fail] {tool.Name}: {ex.Message}");
            }
        }

        foreach (var err in errors)
        {
            WriteLine($"[warn] {err}");
        }

        WriteLine(string.Empty);
        WriteLine($"Done. {updated} updated, {failed} failed.");
        return failed == 0 && errors.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Handles the --download-only / --download-only-bundle commands: fetches
    /// artifacts into the downloads folder without installing anything —
    /// useful for caching a stack on a USB drive for offline machines.
    /// </summary>
    private static async Task<int> DownloadOnlyAsync(IEnumerable<string> toolIds, List<ToolDefinition> tools)
    {
        var knownIds = tools.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var download = new DownloadService();

        var targets = new List<ToolDefinition>();
        var errors = new List<string>();

        foreach (var rawId in toolIds)
        {
            if (knownIds.TryGetValue(rawId, out var tool))
            {
                if (!targets.Contains(tool))
                {
                    targets.Add(tool);
                }
            }
            else
            {
                errors.Add($"unknown tool id '{rawId}' (use --list to see available ids)");
            }
        }

        if (targets.Count == 0)
        {
            WriteLine("error: no valid tool ids provided (use --list to see available ids)");
            return 1;
        }

        WriteLine($"Downloading {targets.Count} tool(s) to {dlFolder} (no install)…");
        int cached = 0;
        int failed = 0;

        foreach (var tool in targets)
        {
            try
            {
                if (DownloadService.IsAlreadyDownloaded(tool, dlFolder))
                {
                    cached++;
                    WriteLine($"[skip] {tool.Name} ({tool.FileName}) already cached");
                    continue;
                }

                WriteLine($"[get ] {tool.Name} ({tool.FileName})");
                await download.DownloadAsync(tool, dlFolder);
                if (tool.Status == ToolStatus.Downloaded)
                {
                    cached++;
                    WriteLine($"[ok  ] {tool.Name}: cached for offline use");
                }
                else
                {
                    failed++;
                    WriteLine($"[fail] {tool.Name}: download did not complete");
                }
            }
            catch (Exception ex)
            {
                failed++;
                WriteLine($"[fail] {tool.Name}: {ex.Message}");
            }
        }

        foreach (var err in errors)
        {
            WriteLine($"[warn] {err}");
        }

        WriteLine(string.Empty);
        WriteLine($"Done. {cached} cached in {dlFolder}, {failed} failed.");
        return failed == 0 && errors.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// Handles the --export-profile command: writes the set of installed tools
    /// to a JSON profile that --import-profile can replay on another machine.
    /// </summary>
    private static int ExportProfile(string profilePath, List<ToolDefinition> tools)
    {
        try
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            var installed = tools
                .Where(t => UninstallService.IsInstalled(t, dlFolder) || t.Status == ToolStatus.Downloaded)
                .ToList();

            if (installed.Count == 0)
            {
                WriteLine("No installed tools found — nothing to export.");
                return 1;
            }

            foreach (var tool in installed)
            {
                tool.IsSelected = true;
            }

            var profileName = $"{Environment.MachineName} workstation — {DateTime.Now:yyyy-MM-dd}";
            var json = ProfileService.Export(installed, profileName);
            File.WriteAllText(profilePath, json);
            WriteLine($"Exported {installed.Count} installed tools to {Path.GetFullPath(profilePath)}");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"error: could not export profile: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Handles the --import-profile command: installs every tool referenced by
    /// a previously exported profile, skipping tools that are already present.
    /// </summary>
    private static async Task<int> ImportProfileAsync(string profilePath, List<ToolDefinition> tools)
    {
        try
        {
            if (!File.Exists(profilePath))
            {
                WriteLine($"error: profile file not found: {profilePath}");
                return 1;
            }

            var json = File.ReadAllText(profilePath);
            var matched = ProfileService.Import(json, tools);
            var selected = tools.Where(t => t.IsSelected).ToList();

            if (matched == 0)
            {
                WriteLine("error: the profile does not reference any tools from this catalog.");
                return 1;
            }

            WriteLine($"Profile loaded: {matched} tool(s) recognized.");

            var (installed, skipped, failed) = await InstallMissingAsync(selected);

            WriteLine(string.Empty);
            WriteLine($"Done. {installed} installed, {skipped} already present, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            WriteLine($"error: could not import profile: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Shared install loop: downloads and installs every tool that is not
    /// already present, printing per-tool progress. Used by --install-style
    /// commands and profile/package-manager imports alike.
    /// </summary>
    private static async Task<(int Installed, int Skipped, int Failed)> InstallMissingAsync(
        List<ToolDefinition> targets)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var download = new DownloadService();
        int installed = 0;
        int skipped = 0;
        int failed = 0;

        foreach (var tool in targets)
        {
            if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded)
            {
                skipped++;
                WriteLine($"[skip] {tool.Name} ({tool.Id}) already installed");
                continue;
            }

            try
            {
                WriteLine($"[get ] {tool.Name} ({tool.FileName})");
                await download.DownloadAsync(tool, dlFolder);
                if (tool.Status == ToolStatus.Downloaded)
                {
                    var res = ArtifactService.Perform(tool, dlFolder);
                    if (res.Success)
                    {
                        installed++;
                        tool.IsInstalled = true;
                        WriteLine($"[ok  ] {tool.Name}: {res.Message}");
                    }
                    else
                    {
                        failed++;
                        WriteLine($"[fail] {tool.Name}: {res.Message}");
                    }
                }
                else
                {
                    failed++;
                    WriteLine($"[fail] {tool.Name}: download did not complete");
                }
            }
            catch (Exception ex)
            {
                failed++;
                WriteLine($"[fail] {tool.Name}: {ex.Message}");
            }
        }

        return (installed, skipped, failed);
    }

    private enum PackageManagerSource
    {
        Winget,
        Chocolatey
    }

    /// <summary>
    /// Handles the --import-winget / --import-choco commands: maps package
    /// identifiers from another package manager onto the catalog and installs
    /// everything that matched.
    /// </summary>
    private static async Task<int> ImportFromPackageManagerAsync(
        string path, List<ToolDefinition> tools, PackageManagerSource source)
    {
        try
        {
            if (!File.Exists(path))
            {
                WriteLine($"error: file not found: {path}");
                return 1;
            }

            var text = File.ReadAllText(path);
            var result = source == PackageManagerSource.Winget
                ? MigrationService.FromWingetExport(text, tools)
                : MigrationService.FromChocoList(text, tools);

            WriteLine($"{result.SourceDescription}: {result.MatchedTools.Count} package(s) matched this catalog.");
            if (result.UnmatchedIds.Count > 0)
            {
                var preview = string.Join(", ", result.UnmatchedIds.Take(20));
                var more = result.UnmatchedIds.Count > 20 ? " ..." : "";
                WriteLine($"[warn] {result.UnmatchedIds.Count} package(s) not in the catalog: {preview}{more}");
            }

            if (result.MatchedTools.Count == 0)
            {
                WriteLine("error: nothing from the import could be mapped to this catalog.");
                return 1;
            }

            var (installed, skipped, failed) = await InstallMissingAsync(result.MatchedTools);
            WriteLine(string.Empty);
            WriteLine($"Done. {installed} installed, {skipped} already present, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            WriteLine($"error: could not import from package manager: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Handles the --report command: writes a markdown inventory of the
    /// workstation (installed tools, versions, pending updates, settings).
    /// </summary>
    private static async Task<int> ExportReportAsync(string path, List<ToolDefinition> tools)
    {
        try
        {
            var count = await WorkstationReportService.WriteReportAsync(tools, path);
            WriteLine($"Report for {count} installed tool(s) written to {Path.GetFullPath(path)}");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"error: could not write report: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Handles the --generate-script command: writes a standalone PowerShell
    /// bootstrap script that re-provisions the current tool set on a fresh
    /// Windows machine — no DevOpsToolsInstaller installation required.
    /// </summary>
    private static int GenerateBootstrapScript(string scriptPath, List<ToolDefinition> tools)
    {
        try
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            var installed = tools
                .Where(t => UninstallService.IsInstalled(t, dlFolder) || t.Status == ToolStatus.Downloaded)
                .ToList();

            if (installed.Count == 0)
            {
                WriteLine("No installed tools found — install tools first, or use --install to build a set.");
                return 1;
            }

            var script = BootstrapScriptService.Generate(installed, DownloadService.DefaultDownloadsFolder);
            // UTF-8 with BOM: Windows PowerShell 5.1 assumes ANSI for BOM-less
            // scripts, which corrupts any non-ASCII character in the output.
            File.WriteAllText(scriptPath, script, System.Text.Encoding.UTF8);
            WriteLine($"Bootstrap script for {installed.Count} tools written to {Path.GetFullPath(scriptPath)}");
            WriteLine("Run it on the target machine with:");
            WriteLine($"  powershell -ExecutionPolicy Bypass -File \"{Path.GetFullPath(scriptPath)}\"");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"error: could not generate bootstrap script: {ex.Message}");
            return 1;
        }
    }

    private static int PrintHistory(string? toolFilter)
    {
        var events = string.IsNullOrWhiteSpace(toolFilter)
            ? InstallHistoryService.Load(100)
            : InstallHistoryService.LoadForTool(toolFilter, 100);

        if (events.Count == 0)
        {
            WriteLine("No workstation history recorded yet.");
            return 0;
        }

        var scope = string.IsNullOrWhiteSpace(toolFilter) ? "" : $" for '{toolFilter}'";
        WriteLine($"Workstation history — {events.Count} event(s){scope} (newest first):");
        WriteLine();
        foreach (var e in events)
        {
            var version = string.IsNullOrWhiteSpace(e.Version) ? "" : "v" + e.Version.TrimStart('v', 'V');
            WriteLine($"  {e.Timestamp:yyyy-MM-dd HH:mm}  {e.Action.PadRight(10)} {e.ToolName.PadRight(26)} {version}");
        }
        return 0;
    }

    private static int CaptureSnapshot(string name, IReadOnlyList<ToolDefinition> tools)
    {
        var snapshot = SnapshotService.Capture(name, tools);
        WriteLine($"Snapshot '{snapshot.Name}' captured: {snapshot.ToolCount} tool(s) on {snapshot.MachineName}.");
        WriteLine($"Saved to {SnapshotService.StorageFolder}");
        return 0;
    }

    private static async Task<int> RestoreSnapshotAsync(string name, IReadOnlyList<ToolDefinition> tools)
    {
        var snapshot = SnapshotService.Get(name);
        if (snapshot is null)
        {
            WriteLine($"error: snapshot '{name}' not found.");
            return 1;
        }

        var missing = SnapshotService.GetMissingTools(snapshot, tools);
        if (missing.Count == 0)
        {
            WriteLine($"Snapshot '{snapshot.Name}' already matches this workstation — nothing to restore.");
            return 0;
        }

        WriteLine($"Restoring snapshot '{snapshot.Name}': installing {missing.Count} missing tool(s)...");
        var (installed, skipped, failed) = await InstallMissingAsync(missing);
        WriteLine(string.Empty);
        WriteLine($"Done. {installed} installed, {skipped} already present, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int PrintProfiles()
    {
        var profiles = ProfileStoreService.LoadAll();
        if (profiles.Count == 0)
        {
            WriteLine("No saved workstation profiles. Create one from the app or with --capture-profile <name>.");
            return 0;
        }

        WriteLine($"Saved workstation profiles ({profiles.Count}):");
        WriteLine();
        foreach (var p in profiles)
        {
            WriteLine($"  {p.Id.PadRight(20)} {p.Name.PadRight(32)} {p.ToolCount} tool(s)  [{p.SourceLabel}]");
        }
        return 0;
    }

    private static int CaptureProfile(string name, IReadOnlyList<ToolDefinition> tools)
    {
        var profile = ProfileStoreService.CaptureFromInstalled(name, "Captured from headless CLI", tools);
        WriteLine($"Profile '{profile.Name}' saved with {profile.ToolCount} tool(s) to {ProfileStoreService.FilePath}.");
        return 0;
    }

    private static async Task<int> ApplyProfileAsync(string name, IReadOnlyList<ToolDefinition> tools)
    {
        var profile = ProfileStoreService.Get(name);
        if (profile is null)
        {
            WriteLine($"error: profile '{name}' not found (run --profiles to list saved profiles).");
            return 1;
        }

        var resolved = ProfileStoreService.ResolveTools(profile, tools);
        if (resolved.Count == 0)
        {
            WriteLine($"error: profile '{profile.Name}' does not reference any tools in this catalog.");
            return 1;
        }

        WriteLine($"Applying profile '{profile.Name}': {resolved.Count} tool(s)...");
        var (installed, skipped, failed) = await InstallMissingAsync(resolved);
        WriteLine(string.Empty);
        WriteLine($"Done. {installed} installed, {skipped} already present, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static int RunSyncProfiles()
    {
        var result = ProfileSyncService.Sync();
        if (result.Success)
        {
            WriteLine($"[OK] {result.Message}");
            return 0;
        }

        WriteLine($"[ERR] {result.Message}");
        return 1;
    }

    private static int RunPathDoctor()
    {
        WriteLine("Running PATH Environment Doctor...\n");
        var report = PathDoctorService.Diagnose();

        WriteLine($"Status: {(report.IsHealthy ? "[OK] Healthy" : "[WARN] Issues Detected")}");
        WriteLine($"Summary: {report.Summary}\n");

        WriteLine($"Total User PATH entries: {report.TotalEntries}");
        WriteLine($"Valid existing directories: {report.CleanedEntries.Count}");
        WriteLine($"Tools\\bin on User PATH: {(report.IsBinOnUserPath ? "Yes" : "No (Missing)")}");

        if (report.DuplicateEntries.Count > 0)
        {
            WriteLine($"\n[!] Duplicate Entries ({report.DuplicateEntries.Count}):");
            foreach (var dup in report.DuplicateEntries)
            {
                WriteLine($"    - {dup}");
            }
        }

        if (report.MissingDirectories.Count > 0)
        {
            WriteLine($"\n[!] Non-Existent / Broken Directories ({report.MissingDirectories.Count}):");
            foreach (var miss in report.MissingDirectories)
            {
                WriteLine($"    - {miss}");
            }
        }

        if (!report.IsHealthy)
        {
            WriteLine("\nRun 'DevOpsToolsInstaller.exe --fix-path' to automatically backup and clean your PATH.");
        }

        return report.IsHealthy ? 0 : 1;
    }

    private static int RunFixPath()
    {
        WriteLine("Optimizing User PATH Environment Variable...");
        var result = PathDoctorService.FixUserPath();

        if (result.Success)
        {
            WriteLine($"[OK] {result.Message}");
            WriteLine($"     Backup created at: {result.BackupFilePath}");
            WriteLine($"     Removed duplicates: {result.RemovedDuplicatesCount}");
            WriteLine($"     Removed broken paths: {result.RemovedMissingCount}");
            if (result.AddedBinFolder)
            {
                WriteLine($"     Added Tools\\bin: {ArtifactService.BinFolder}");
            }
            return 0;
        }
        else
        {
            WriteLine($"[ERR] {result.Message}");
            return 1;
        }
    }

    private static int GenerateDevContainer(string destinationPath, IReadOnlyList<ToolDefinition> allTools)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var installedTools = allTools
            .Where(t => UninstallService.IsInstalled(t, dlFolder) || t.Status == ToolStatus.Downloaded)
            .ToList();

        var toolsToExport = installedTools.Count > 0 ? installedTools : allTools.ToList();
        WriteLine($"Generating DevContainer configuration for {toolsToExport.Count} tool(s)...");

        try
        {
            if (destinationPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                DevContainerGeneratorService.ExportDevContainerZip(toolsToExport, destinationPath);
            }
            else
            {
                DevContainerGeneratorService.ExportDevContainerFolder(toolsToExport, destinationPath);
            }

            WriteLine($"[OK] DevContainer package generated successfully at: {destinationPath}");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"[ERR] Failed to generate DevContainer: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Returns the value following a global option flag, or null when absent.
    /// </summary>
    private static string? GetOptionValue(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static int RunManifestCheck(string manifestPath, IReadOnlyList<ToolDefinition> tools)
    {
        var res = ProjectManifestService.Check(manifestPath, tools);
        foreach (var line in res.Lines)
        {
            WriteLine(line);
        }
        return res.ExitCode;
    }

    private static async Task<int> RunInstallManifestAsync(string manifestPath, IReadOnlyList<ToolDefinition> tools)
    {
        var (manifest, err) = ProjectManifestService.Load(manifestPath);
        if (manifest == null)
        {
            WriteLine($"error: {err}");
            return 1;
        }

        WriteLine($"Project manifest: {manifest.Project ?? "devtools.json"} ({manifest.Tools.Count} tools specified)");
        var plan = ProjectManifestService.BuildPlan(manifest, tools, manifestPath);
        WriteLine($"Plan: {plan.InstallCount} to install/align, {plan.SatisfiedCount} already satisfied.");

        if (plan.UnknownIds.Count > 0)
        {
            WriteLine($"[warn] Unknown tool IDs in manifest: {string.Join(", ", plan.UnknownIds)}");
        }

        foreach (var item in plan.Items)
        {
            var stateStr = item.State switch
            {
                ManifestToolState.Satisfied => "[OK  ]",
                ManifestToolState.Missing => "[MISS]",
                ManifestToolState.VersionMismatch => "[MISMATCH]",
                _ => "[INFO]"
            };
            WriteLine($"  {stateStr} {item.Tool.Name,-20} {item.Detail}");
        }

        if (plan.InstallCount == 0)
        {
            WriteLine("All manifest tools are already satisfied.");
            return 0;
        }

        WriteLine();
        WriteLine("Installing required tools...");
        var result = await ProjectManifestService.ApplyAsync(plan, downloadOnly: false, onLog: msg => WriteLine($"  {msg}"));

        // Write/update lockfile
        ProjectManifestService.WriteLock(manifestPath, manifest, tools);

        WriteLine();
        WriteLine($"Done: {result.Installed} installed, {result.Failed} failed.");
        return result.AllOK ? 0 : 1;
    }

    private static int RunExportManifest(string destPath, IReadOnlyList<ToolDefinition> tools)
    {
        try
        {
            var manifest = ProjectManifestService.ExportManifest(tools);
            ProjectManifestService.WriteManifest(destPath, manifest);
            ProjectManifestService.WriteLock(destPath, manifest, tools);
            WriteLine($"[OK] Exported project manifest to {destPath} and generated devtools.lock.json ({manifest.Tools.Count} tools).");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"[ERR] Failed to export manifest: {ex.Message}");
            return 1;
        }
    }

    private static int RunLockManifest(string manifestPath, IReadOnlyList<ToolDefinition> tools)
    {
        var (manifest, err) = ProjectManifestService.Load(manifestPath);
        if (manifest == null)
        {
            WriteLine($"error: {err}");
            return 1;
        }
        var lockPath = ProjectManifestService.WriteLock(manifestPath, manifest, tools);
        WriteLine($"[OK] Generated lockfile: {lockPath}");
        return 0;
    }

    private static int RunListVersions(string? toolFilter, IReadOnlyList<ToolDefinition> tools)
    {
        var targets = string.IsNullOrWhiteSpace(toolFilter)
            ? tools
            : tools.Where(t => t.Id.Equals(toolFilter, StringComparison.OrdinalIgnoreCase) ||
                               t.Name.Equals(toolFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (targets.Count == 0)
        {
            WriteLine($"Tool '{toolFilter}' not found in catalog.");
            return 1;
        }

        int foundTotal = 0;
        foreach (var tool in targets)
        {
            var versions = VersionSwitcherService.GetInstalledVersions(tool);
            if (versions.Count > 0)
            {
                foundTotal++;
                WriteLine($"{tool.Name} ({tool.Id}):");
                foreach (var v in versions)
                {
                    var activeTag = v.IsActive ? " [ACTIVE]" : "         ";
                    WriteLine($"  {activeTag} v{v.Version,-12} ({v.FileSizeFormatted}, {v.InstalledAt:yyyy-MM-dd})");
                }
            }
            else if (!string.IsNullOrWhiteSpace(toolFilter))
            {
                WriteLine($"{tool.Name} has no side-by-side versions stored yet.");
            }
        }

        if (foundTotal == 0 && string.IsNullOrWhiteSpace(toolFilter))
        {
            WriteLine("No side-by-side versions recorded yet. Use '--switch <tool> <version>' to install and activate specific versions.");
        }

        return 0;
    }

    private static async Task<int> RunSwitchVersionAsync(string toolId, string version, IReadOnlyList<ToolDefinition> tools)
    {
        var tool = tools.FirstOrDefault(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
        {
            WriteLine($"error: unknown tool id '{toolId}'.");
            return 1;
        }

        WriteLine($"Switching {tool.Name} to version v{version}...");
        var (ok, msg) = await VersionSwitcherService.SwitchActiveVersionAsync(tool, version, tools);
        WriteLine(ok ? $"[OK] {msg}" : $"[ERR] {msg}");
        return ok ? 0 : 1;
    }

    private static async Task<int> RunDetectProjectAsync(string folder, IReadOnlyList<ToolDefinition> tools)
    {
        WriteLine($"Scanning directory '{Path.GetFullPath(folder)}' for project version files...");
        var res = await VersionSwitcherService.DetectAndSwitchProjectVersionsAsync(folder, tools);
        if (res.Results.Count == 0)
        {
            WriteLine("No project version files detected (.terraform-version, .kubectl-version, .tool-versions, devtools.json).");
            return 0;
        }

        foreach (var r in res.Results)
        {
            var tag = r.Switched ? "[OK  ]" : "[SKIP]";
            WriteLine($"  {tag} {r.ToolId} -> v{r.Version}: {r.Message}");
        }
        return 0;
    }

    private static async Task<int> RunExportMigrationAsync(string filePath, IReadOnlyList<ToolDefinition> tools)
    {
        WriteLine("Capturing workstation developer tools and configurations (.gitconfig, PowerShell, Windows Terminal, VS Code, SSH config)...");
        var bundle = await NewPcMigrationService.CreateBundleAsync(tools);
        NewPcMigrationService.SaveBundle(filePath, bundle);
        WriteLine($"[OK] Migration package saved to: {filePath}");
        WriteLine($"     Included: {bundle.ToolCount} tool(s), {bundle.Configs.ConfigItemCount} developer configs, {bundle.Configs.VsCodeExtensions.Count} VS Code extensions.");
        return 0;
    }

    private static async Task<int> RunApplyMigrationAsync(string filePath, IReadOnlyList<ToolDefinition> tools)
    {
        var (bundle, err) = NewPcMigrationService.LoadBundle(filePath);
        if (bundle == null)
        {
            WriteLine($"error: {err}");
            return 1;
        }

        WriteLine($"Applying migration from '{bundle.Name}' (Source: {bundle.SourceMachine}, User: {bundle.SourceUser})...");
        var options = new MigrationRestoreOptions();
        var res = await NewPcMigrationService.ApplyBundleAsync(bundle, tools, options, onLog: msg => WriteLine($"  {msg}"));

        WriteLine();
        WriteLine($"Migration applied: {res.ToolsInstalled} tools installed, {res.ConfigsRestored} configs restored, {res.ExtensionsInstalled} VS Code extensions installed.");
        return res.Success ? 0 : 1;
    }

    private static async Task<int> RunCheckUpdatesAsync()
    {
        WriteLine("Checking installed tools for updates...");
        var updates = await UpdateAutomationService.Instance.CheckNowAsync();
        if (updates.Count == 0)
        {
            WriteLine("All installed tools are up-to-date.");
            return 0;
        }

        WriteLine($"Found {updates.Count} available update(s):");
        foreach (var u in updates)
        {
            var status = u.IsPinned ? "[PINNED]" : u.IsIgnored ? "[IGNORED]" : "[UPDATE]";
            WriteLine($"  {status,-9} {u.Tool.Name,-20} v{u.InstalledVersion} -> v{u.AvailableVersion}");
            if (!string.IsNullOrWhiteSpace(u.ReleaseNotesPreview))
            {
                WriteLine($"             Note: {u.ReleaseNotesPreview}");
            }
        }
        return 0;
    }

    private static async Task<int> RunCloudStatusAsync()
    {
        WriteLine("Reading local cloud & Kubernetes configurations (local state only)...");
        var state = await CloudConnectionsService.ScanAllAsync();

        WriteLine();
        WriteLine("── Kubernetes (kubectl) ──────────────────────────");
        if (state.Kube.HasKubeConfig)
        {
            WriteLine($"  Active Context  : {state.Kube.ActiveContextName}");
            WriteLine($"  Active Cluster  : {state.Kube.ActiveCluster}");
            WriteLine($"  Namespace       : {state.Kube.ActiveNamespace}");
            WriteLine($"  Total Contexts  : {state.Kube.Contexts.Count}");
        }
        else
        {
            WriteLine("  No kubeconfig found at ~/.kube/config");
        }

        WriteLine();
        WriteLine("── AWS (Amazon Web Services) ─────────────────────");
        WriteLine($"  Status          : {state.Aws.StatusBadge}");
        WriteLine($"  Active Profile  : {state.Aws.ActiveProfile}");
        if (!string.IsNullOrWhiteSpace(state.Aws.Region))
            WriteLine($"  Default Region  : {state.Aws.Region}");
        if (!string.IsNullOrWhiteSpace(state.Aws.AccountSummary))
            WriteLine($"  Account / SSO   : {state.Aws.AccountSummary}");
        WriteLine($"  Saved Profiles  : {string.Join(", ", state.Aws.Profiles.Select(p => p.Name))}");

        WriteLine();
        WriteLine("── Microsoft Azure ───────────────────────────────");
        WriteLine($"  Status          : {state.Azure.StatusBadge}");
        if (state.Azure.IsLoggedIn)
        {
            WriteLine($"  Account         : {state.Azure.UserAccount}");
            WriteLine($"  Subscription    : {state.Azure.SubscriptionName} ({state.Azure.SubscriptionId})");
            WriteLine($"  Tenant ID       : {state.Azure.TenantId}");
        }

        WriteLine();
        WriteLine("── Google Cloud (GCP) ────────────────────────────");
        WriteLine($"  Status          : {state.Gcp.StatusBadge}");
        if (state.Gcp.IsLoggedIn)
        {
            WriteLine($"  Account         : {state.Gcp.AccountEmail}");
            WriteLine($"  Project ID      : {state.Gcp.ProjectId}");
            if (!string.IsNullOrWhiteSpace(state.Gcp.DefaultZone))
                WriteLine($"  Default Zone    : {state.Gcp.DefaultZone}");
        }

        return 0;
    }

    private static int RunKubeContext(string? targetContext)
    {
        var kube = CloudConnectionsService.GetKubeStatus();
        if (!kube.HasKubeConfig)
        {
            WriteLine("No kubeconfig found at ~/.kube/config.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(targetContext))
        {
            WriteLine($"Active Context: {kube.ActiveContextName}");
            WriteLine("Available Contexts:");
            foreach (var ctx in kube.Contexts)
            {
                var marker = ctx.IsActive ? "* " : "  ";
                WriteLine($"  {marker}{ctx.Name,-25} (cluster: {ctx.Cluster}, ns: {ctx.Namespace})");
            }
            return 0;
        }

        var match = kube.Contexts.FirstOrDefault(c => c.Name.Equals(targetContext, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            WriteLine($"error: context '{targetContext}' not found in ~/.kube/config.");
            return 1;
        }

        bool ok = CloudConnectionsService.SwitchKubeContext(match.Name);
        WriteLine(ok ? $"Switched active context to '{match.Name}'." : "Failed to switch context.");
        return ok ? 0 : 1;
    }

    private static int RunBackends()
    {
        WriteLine("Installation Backends & Package Managers:");
        WriteLine("  [x] Direct Vendor Download (Default, zero package-manager bloat)");
        WriteLine($"  [{(ToolBackendService.IsWingetAvailable ? "x" : " ")}] winget (Windows Package Manager) : {(ToolBackendService.IsWingetAvailable ? "Available" : "Not installed")}");
        WriteLine($"  [{(ToolBackendService.IsScoopAvailable ? "x" : " ")}] Scoop                            : {(ToolBackendService.IsScoopAvailable ? "Available" : "Not installed")}");
        WriteLine($"  [{(ToolBackendService.IsChocolateyAvailable ? "x" : " ")}] Chocolatey                       : {(ToolBackendService.IsChocolateyAvailable ? "Available" : "Not installed")}");
        WriteLine();
        WriteLine($"Current Default Preference: {SettingsService.DefaultBackend.DisplayName()}");
        return 0;
    }

    private static int RunListFeeds()
    {
        var feeds = PrivateFeedService.LoadFeeds();
        WriteLine($"Private & Corporate Feeds ({feeds.Count}):");
        if (feeds.Count == 0)
        {
            WriteLine("  No private catalog feeds subscribed. Add one with --add-feed <name> <url>.");
            return 0;
        }

        foreach (var f in feeds)
        {
            var en = f.IsEnabled ? "Enabled" : "Disabled";
            var pol = f.IsPolicyEnforced ? " [IT-Enforced]" : "";
            WriteLine($"  • {f.Name,-25} ({en}{pol})");
            WriteLine($"    URL    : {f.Url}");
            WriteLine($"    Status : {f.StatusText}");
        }
        return 0;
    }

    private static async Task<int> RunAddFeedAsync(string name, string url)
    {
        try
        {
            WriteLine($"Subscribing to feed: {url}...");
            var feed = await PrivateFeedService.AddOrUpdateFeedAsync(name, url);
            WriteLine($"Successfully subscribed to '{feed.Name}' ({feed.ToolCount} tools loaded).");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"error: failed to subscribe: {ex.Message}");
            return 1;
        }
    }

    private static int RunRemoveFeed(string idOrUrl)
    {
        bool ok = PrivateFeedService.RemoveFeed(idOrUrl);
        WriteLine(ok ? $"Unsubscribed from feed '{idOrUrl}'." : $"Feed '{idOrUrl}' not found or locked by policy.");
        return ok ? 0 : 1;
    }

    private static async Task<int> RunSyncFeedsAsync()
    {
        WriteLine("Syncing private catalog feeds...");
        var results = await PrivateFeedService.SyncAllFeedsAsync();
        foreach (var r in results)
        {
            var status = r.Success ? "OK" : "FAIL";
            WriteLine($"  [{status,-4}] {r.Feed.Name,-20} : {r.Message}");
        }
        return results.Any(r => !r.Success) ? 1 : 0;
    }

    private static async Task<int> RunExportOfflineBundleAsync(string bundleId, string dest, List<ToolDefinition> allTools)
    {
        var catalog = new CatalogService();
        var bundles = await catalog.LoadBundlesAsync();
        var bundle = bundles.FirstOrDefault(b => b.Id.Equals(bundleId, StringComparison.OrdinalIgnoreCase));
        if (bundle is null)
        {
            WriteLine($"error: bundle '{bundleId}' not found. Available: {string.Join(", ", bundles.Select(b => b.Id))}");
            return 1;
        }

        WriteLine($"Packaging offline bundle '{bundle.Name}' to '{dest}'...");
        var res = await OfflineBundleService.ExportOfflineBundleAsync(
            bundle, allTools, dest,
            statusCallback: s => WriteLine($"  {s}"));

        if (res.Success)
        {
            WriteLine($"Successfully exported {res.ToolsExported} tools to '{res.DestinationPath}'.");
            WriteLine("Offline package contains installers, bundle-manifest.json, install-offline.ps1, and install-offline.cmd.");
            return 0;
        }
        else
        {
            WriteLine($"error: offline packaging failed: {res.Message}");
            return 1;
        }
    }

    private static async Task<int> RunInstallOfflineBundleAsync(string bundleDir)
    {
        var manifestPath = Path.Combine(bundleDir, "bundle-manifest.json");
        if (!File.Exists(manifestPath))
        {
            WriteLine($"error: bundle-manifest.json not found in '{bundleDir}'.");
            return 1;
        }

        WriteLine($"Executing offline installation from '{bundleDir}'...");
        var ps1 = Path.Combine(bundleDir, "install-offline.ps1");
        if (File.Exists(ps1))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"",
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                await p.WaitForExitAsync();
                return p.ExitCode;
            }
        }

        return 1;
    }

    private static int RunCheckPolicy()
    {
        WriteLine("Enterprise Policy Posture:");
        WriteLine($"  Status                    : {(EnterprisePolicyService.IsPolicyActive ? $"Active (Managed by {EnterprisePolicyService.Current.OrganizationName})" : "Unmanaged (Standard)")}");
        WriteLine($"  Block Unsigned Installers : {EnterprisePolicyService.IsUnsignedBlocked}");
        WriteLine($"  Enforce SHA-256 Check     : {EnterprisePolicyService.IsShaVerificationEnforced}");
        WriteLine($"  Lock Settings             : {EnterprisePolicyService.AreSettingsLocked}");
        WriteLine($"  Disable Custom Tools      : {EnterprisePolicyService.AreCustomToolsDisabled}");
        WriteLine($"  Allowed Catalog Sources   : {(EnterprisePolicyService.Current.AllowedCatalogSources.Count > 0 ? string.Join(", ", EnterprisePolicyService.Current.AllowedCatalogSources) : "All (Unrestricted)")}");
        WriteLine($"  Allowed Backends          : {string.Join(", ", EnterprisePolicyService.Current.AllowedBackends)}");
        return 0;
    }

    private static int RunExportPolicyTemplate(string path)
    {
        try
        {
            var policy = EnterprisePolicyService.Current;
            var reg = EnterprisePolicyService.GenerateRegistryPolicyScript(policy);
            File.WriteAllText(path, reg);
            WriteLine($"Exported Group Policy registry template to '{path}'.");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"error: failed to export policy template: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunExportIntuneScriptAsync(string? bundleId, string path)
    {
        try
        {
            ToolBundle? bundle = null;
            if (!string.IsNullOrWhiteSpace(bundleId))
            {
                var catalog = new CatalogService();
                var bundles = await catalog.LoadBundlesAsync();
                bundle = bundles.FirstOrDefault(b => b.Id.Equals(bundleId, StringComparison.OrdinalIgnoreCase));
            }
            var script = EnterprisePolicyService.GenerateIntuneScript(bundle);
            await File.WriteAllTextAsync(path, script);
            WriteLine($"Exported Microsoft Intune deployment script to '{path}'.");
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine($"error: failed to export Intune script: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunWslAsync(bool checkTools)
    {
        WriteLine("WSL & Virtualization Health Check:");
        var prereqs = await WslService.CheckPrerequisitesAsync();
        WriteLine($"  WSL Installed             : {prereqs.IsWslInstalled} ({(prereqs.WslVersion ?? "N/A")})");
        WriteLine($"  Virtual Machine Platform : {prereqs.IsVirtualMachinePlatformEnabled}");
        WriteLine($"  WSL Optional Feature     : {prereqs.IsWslFeatureEnabled}");
        WriteLine($"  Summary                  : {prereqs.Summary}");
        WriteLine();

        var distros = await WslService.ListDistrosAsync();
        WriteLine($"Installed Linux Distributions ({distros.Count}):");
        if (distros.Count == 0)
        {
            WriteLine("  No distributions found. Install one with: wsl --install -d Ubuntu");
        }
        else
        {
            foreach (var d in distros)
            {
                WriteLine($"  {(d.IsDefault ? "[*]" : "[ ]")} {d.Name.PadRight(24)} State: {d.State.PadRight(10)} WSL {d.Version}");
            }
        }

        if (checkTools && distros.Count > 0)
        {
            var target = distros.FirstOrDefault(d => d.IsDefault)?.Name ?? distros[0].Name;
            WriteLine();
            WriteLine($"Tools Availability inside '{target}':");
            var tools = await WslService.CheckToolsInsideDistroAsync(target);
            foreach (var t in tools)
            {
                var status = t.IsInstalledInWsl ? $"Installed (v{t.WslVersion ?? "unknown"}) -> {t.WslPath}" : "Not installed";
                WriteLine($"  {t.Name.PadRight(16)}: {status}");
            }
        }

        return 0;
    }

    private static int RunWslFix()
    {
        WriteLine("WSL2 & Virtual Machine Platform Prerequisite Fix Script:");
        WriteLine(WslService.GeneratePrerequisiteFixScript());
        return 0;
    }

    private static int RunEnv()
    {
        WriteLine("User PATH Directories (ordered by search priority):");
        var paths = EnvironmentVariableService.GetPathEntries(EnvironmentVariableTarget.User);
        for (int i = 0; i < paths.Count; i++)
        {
            var p = paths[i];
            var flag = !p.Exists ? "[MISSING] " : (p.IsDuplicate ? "[DUP]     " : (p.IsDevOpsBin ? "[BIN]     " : "          "));
            WriteLine($"  {flag} #{i + 1:D2} {p.Path}");
        }

        WriteLine();
        WriteLine("User Environment Variables:");
        var vars = EnvironmentVariableService.GetVariables(EnvironmentVariableTarget.User);
        foreach (var v in vars)
        {
            WriteLine($"  {v.Name.PadRight(28)} = {v.Value}");
        }

        return 0;
    }

    private static int RunSetEnv(string name, string value)
    {
        if (EnvironmentVariableService.SetVariable(name, value, EnvironmentVariableTarget.User))
        {
            WriteLine($"Set User environment variable '{name}={value}' and broadcasted live change.");
            return 0;
        }
        WriteLine($"error: failed to set environment variable '{name}'.");
        return 1;
    }

    private static int RunDelEnv(string name)
    {
        if (EnvironmentVariableService.SetVariable(name, null, EnvironmentVariableTarget.User))
        {
            WriteLine($"Deleted User environment variable '{name}'.");
            return 0;
        }
        WriteLine($"error: failed to delete environment variable '{name}'.");
        return 1;
    }

    private static async Task<int> RunGitStatusAsync()
    {
        WriteLine("Global Git Configuration:");
        var cfg = await GitSshSetupService.GetGitConfigAsync();
        WriteLine($"  user.name          : {cfg.UserName ?? "(not set)"}");
        WriteLine($"  user.email         : {cfg.UserEmail ?? "(not set)"}");
        WriteLine($"  init.defaultBranch : {cfg.DefaultBranch}");
        WriteLine($"  core.autocrlf      : {cfg.AutoCrlf}");
        WriteLine($"  credential.helper  : {cfg.CredentialHelper}");

        WriteLine();
        WriteLine("Existing SSH Keys (~/.ssh):");
        var keys = GitSshSetupService.GetExistingSshKeys();
        if (keys.Count == 0)
        {
            WriteLine("  No SSH key pairs found in ~/.ssh.");
        }
        else
        {
            foreach (var k in keys)
            {
                WriteLine($"  • {k.Name} ({k.Algorithm}) - Fingerprint: {k.FingerprintShort} - Comment: {k.Comment ?? "none"}");
            }
        }

        bool agentRunning = await GitSshSetupService.IsSshAgentRunningAsync();
        WriteLine();
        WriteLine($"Windows OpenSSH Agent Service: {(agentRunning ? "Running" : "Stopped/Disabled")}");
        return 0;
    }

    private static async Task<int> RunSetupGitAsync(string name, string email)
    {
        var cfg = new GitConfigSettings(name, email, "main", "input", "false", "manager", "code --wait", false);
        if (await GitSshSetupService.SaveGitConfigAsync(cfg))
        {
            WriteLine($"Saved global Git configuration: {name} <{email}>, default branch: main, autocrlf: input.");
            return 0;
        }
        WriteLine("error: failed to configure Git.");
        return 1;
    }

    private static async Task<int> RunGenerateSshKeyAsync(string? email)
    {
        WriteLine($"Generating modern Ed25519 SSH key...");
        var res = await GitSshSetupService.GenerateSshKeyAsync("ed25519", email);
        if (res.Success)
        {
            WriteLine(res.Message);
            WriteLine($"Public Key Content ({res.PublicKeyPath}):");
            WriteLine(res.PublicKeyContent);

            if (File.Exists(res.KeyPath))
            {
                var (agentSuccess, agentMsg) = await GitSshSetupService.AddKeyToSshAgentAsync(res.KeyPath);
                if (agentSuccess)
                {
                    WriteLine($"[ssh-agent] {agentMsg}");
                }
            }

            return 0;
        }
        WriteLine($"error: {res.Message}");
        return 1;
    }

    private static async Task<int> RunSetupPowerShellAsync()
    {
        WriteLine("Applying recommended DevOps completions and aliases to PowerShell profile...");
        if (await TerminalPowerShellSetupService.ApplyPowerShellSetupAsync(enableCompletions: true, enableAliases: true, enableStarship: true))
        {
            var status = await TerminalPowerShellSetupService.GetPowerShellProfileStatusAsync();
            WriteLine($"Successfully configured PowerShell profile at '{status.ProfilePath}'.");
            WriteLine("Aliases: k=kubectl, tf=terraform, d=docker, dc='docker compose', g=git, kns=kubens, ktx=kubectx");
            return 0;
        }
        WriteLine("error: failed to update PowerShell profile.");
        return 1;
    }

    private static void PrintUsage()
    {
        WriteLine($"DevOps Tools Installer v{AppUpdaterService.CurrentVersion} (headless mode)");
        WriteLine();
        WriteLine("Usage:");
        WriteLine("  DevOpsToolsInstaller.exe --list                       List all tools in the catalog");
        WriteLine("  DevOpsToolsInstaller.exe --status                     Show which tools are installed");
        WriteLine("  DevOpsToolsInstaller.exe --install <id,id,...>        Download and install specific tools");
        WriteLine("  DevOpsToolsInstaller.exe --install-bundle <bundleId>  Install all tools in a curated stack");
        WriteLine("  DevOpsToolsInstaller.exe --update [id,id,...]         Update outdated tools (or force-refresh specific ids)");
        WriteLine("  DevOpsToolsInstaller.exe --uninstall <id,id,...>      Uninstall specific tools");
        WriteLine("  DevOpsToolsInstaller.exe --download-only <id,id,...>  Download artifacts without installing");
        WriteLine("  DevOpsToolsInstaller.exe --download-only-bundle <id>  Download a curated stack for offline caching");
        WriteLine("  DevOpsToolsInstaller.exe --export-profile <file>      Export installed tools to a JSON profile");
        WriteLine("  DevOpsToolsInstaller.exe --import-profile <file>      Install tools from a JSON profile");
        WriteLine("  DevOpsToolsInstaller.exe --import-winget <file>       Migrate packages from a winget export JSON");
        WriteLine("  DevOpsToolsInstaller.exe --import-choco <file>        Migrate packages from chocolatey list output");
        WriteLine("  DevOpsToolsInstaller.exe --report <file>              Write a markdown workstation report (for audits)");
        WriteLine("  DevOpsToolsInstaller.exe --generate-script <file>     Generate a standalone PowerShell bootstrap script");
        WriteLine("  DevOpsToolsInstaller.exe --generate-devcontainer <zip/dir> Generate VS Code DevContainer & Dockerfile");
        WriteLine("  DevOpsToolsInstaller.exe --path-doctor                Diagnose User PATH duplicates and broken paths");
        WriteLine("  DevOpsToolsInstaller.exe --fix-path                   Clean, deduplicate, and backup User PATH");
        WriteLine("  DevOpsToolsInstaller.exe --history [toolId]           Show workstation install/update/uninstall history");
        WriteLine("  DevOpsToolsInstaller.exe --snapshot <name>            Capture a workstation snapshot (restore point)");
        WriteLine("  DevOpsToolsInstaller.exe --restore-snapshot <name>    Reinstall missing tools from a snapshot");
        WriteLine("  DevOpsToolsInstaller.exe --profiles                   List saved named workstation profiles");
        WriteLine("  DevOpsToolsInstaller.exe --capture-profile <name>     Save a profile from the installed toolset");
        WriteLine("  DevOpsToolsInstaller.exe --apply-profile <name|id>    Install every tool in a named profile");
        WriteLine("  DevOpsToolsInstaller.exe --sync-profiles              Merge profiles/snapshots with the sync folder");
        WriteLine("  DevOpsToolsInstaller.exe --manifest [file]            Install what a project needs from devtools.json");
        WriteLine("  DevOpsToolsInstaller.exe --check [file]               CI check: verify drift against devtools.lock.json");
        WriteLine("  DevOpsToolsInstaller.exe --export-manifest [file]     Export current tools as devtools.json & lockfile");
        WriteLine("  DevOpsToolsInstaller.exe --versions [toolId]          List side-by-side installed CLI versions");
        WriteLine("  DevOpsToolsInstaller.exe --switch <tool> <version>    Switch active version (like tfenv/nvm)");
        WriteLine("  DevOpsToolsInstaller.exe --detect-project [dir]       Auto-switch active tools from project version files");
        WriteLine("  DevOpsToolsInstaller.exe --export-migration <file>    Export New-PC package (tools + .gitconfig, etc.)");
        WriteLine("  DevOpsToolsInstaller.exe --apply-migration <file>     Restore New-PC package on this machine");
        WriteLine("  DevOpsToolsInstaller.exe --check-updates              Check for tool updates with notes");
        WriteLine("  DevOpsToolsInstaller.exe --cloud-status               Local cloud login & kubectl context status");
        WriteLine("  DevOpsToolsInstaller.exe --kube-context [name]        List or switch kubectl context (kubectx)");
        WriteLine("  DevOpsToolsInstaller.exe --run-hooks                  Allow user-authored pre/post-install hooks");
        WriteLine("  DevOpsToolsInstaller.exe --downloads-folder <path>    Redirect downloads (e.g. a USB drive)");
        WriteLine("  DevOpsToolsInstaller.exe --wsl                        Check WSL2 & Virtualization health");
        WriteLine("  DevOpsToolsInstaller.exe --wsl-tools                  Probe DevOps tools availability inside WSL");
        WriteLine("  DevOpsToolsInstaller.exe --wsl-fix                    Output script to enable Virtual Machine Platform");
        WriteLine("  DevOpsToolsInstaller.exe --env                        Display User PATH order and environment variables");
        WriteLine("  DevOpsToolsInstaller.exe --set-env <name> <val>       Set a User environment variable with live broadcast");
        WriteLine("  DevOpsToolsInstaller.exe --git-status                 Inspect Git config, SSH keys, and ssh-agent status");
        WriteLine("  DevOpsToolsInstaller.exe --setup-git <name> <email>   Configure global Git user identity");
        WriteLine("  DevOpsToolsInstaller.exe --generate-ssh-key [email]   Generate modern Ed25519 SSH key");
        WriteLine("  DevOpsToolsInstaller.exe --setup-powershell           Inject DevOps CLI completions and aliases into $PROFILE");
        WriteLine("  DevOpsToolsInstaller.exe --version                    Print the application version");
        WriteLine("  DevOpsToolsInstaller.exe --help                       Show this help");
        WriteLine();
        WriteLine("Exit codes: 0 = success, 1 = one or more tools failed.");
    }

    private static void WriteLine(string message = "")
    {
        try { Console.WriteLine(message); } catch { /* no console attached */ }
    }

    private static void TryWrite(string message)
    {
        try { Console.Write(message); } catch { /* no console attached */ }
    }
}
