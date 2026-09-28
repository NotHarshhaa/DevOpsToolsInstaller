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
///
/// The process exits with code 0 on success, 1 when any requested tool failed.
/// </summary>
public static class CliHost
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "--install", "--install-bundle", "--uninstall", "--update",
        "--download-only", "--download-only-bundle",
        "--export-profile", "--import-profile", "--generate-script",
        "--downloads-folder", "--list", "--status", "--version", "--help", "-h"
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
            else if (arg.Equals("--generate-script", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return GenerateBootstrapScript(args[++i], tools);
            }
        }

        if (requestedTools.Count == 0 && errors.Count == 0)
        {
            PrintUsage();
            return errors.Count > 0 ? 1 : 0;
        }

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        int installedCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        foreach (var tool in requestedTools)
        {
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

            var dlFolder = DownloadService.DefaultDownloadsFolder;
            var download = new DownloadService();
            int installed = 0;
            int skipped = 0;
            int failed = 0;

            foreach (var tool in selected)
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
        WriteLine("  DevOpsToolsInstaller.exe --generate-script <file>     Generate a standalone PowerShell bootstrap script");
        WriteLine("  DevOpsToolsInstaller.exe --downloads-folder <path>    Redirect downloads (e.g. a USB drive)");
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
