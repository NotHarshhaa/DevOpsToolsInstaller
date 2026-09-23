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
///
/// The process exits with code 0 on success, 1 when any requested tool failed.
/// </summary>
public static class CliHost
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "--install", "--install-bundle", "--list", "--status", "--help", "-h"
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
        // though this is a WinExe (window subsystem) application.
        try { AttachConsole(ATTACH_PARENT_PROCESS); } catch { }

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
                var version = tool.Kind == ArtifactKind.Installer
                    ? UninstallService.GetInstalledVersion(tool)
                    : CliHealthService.ProbeToolAsync(tool, timeoutSeconds: 2).GetAwaiter().GetResult().DetectedVersion;
                WriteLine($"  [x] {tool.Name.PadRight(28)} {tool.Category.PadRight(28)} {(string.IsNullOrWhiteSpace(version) ? "" : $"v{version}")}");
            }
        }

        WriteLine();
        WriteLine($"{installed} of {tools.Count} catalog tools installed.");
        return 0;
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
