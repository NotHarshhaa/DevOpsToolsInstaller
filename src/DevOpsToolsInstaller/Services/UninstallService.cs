using System.Diagnostics;
using Microsoft.Win32;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Outcome of an uninstall / removal action, surfaced to the UI.
/// </summary>
public sealed record UninstallResult(bool Success, string Message);

/// <summary>
/// Describes an installed program discovered in the Windows uninstall registry.
/// </summary>
public sealed record InstalledProgram(string DisplayName, string UninstallCommand, bool IsQuietCapable);

/// <summary>
/// Reverses whatever <see cref="ArtifactService"/> did for a tool, matched to
/// its <see cref="ArtifactKind"/>:
///   • Installer → find the vendor entry in the Windows uninstall registry and
///                 launch its own uninstaller (never silent — the vendor UI /
///                 UAC prompt is shown, mirroring how the app runs installers)
///   • Archive   → delete the extracted Tools\&lt;id&gt; folder
///   • Binary    → delete the standalone exe from Tools\bin
///   • Script    → nothing was installed; only the downloaded file is removed
///
/// The cached download in the Downloads folder can optionally be removed too.
/// Nothing here elevates the app itself or runs a vendor uninstaller silently.
/// </summary>
public static class UninstallService
{
    private static readonly string[] UninstallRoots =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    // ── Installed-state detection ────────────────────────────────────────

    /// <summary>
    /// Determines whether the tool currently has something the app can remove:
    /// an extracted folder, a copied binary, or a matching registry entry.
    /// </summary>
    public static bool IsInstalled(ToolDefinition tool, string downloadsFolder)
    {
        try
        {
            return tool.Kind switch
            {
                ArtifactKind.Binary    => File.Exists(Path.Combine(ArtifactService.BinFolder, tool.FileName)),
                ArtifactKind.Archive   => HasExtractedFolder(tool),
                ArtifactKind.Installer => FindInstalledProgram(tool) is not null,
                ArtifactKind.Script    => false, // scripts are never auto-run, so never "installed"
                _                      => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool HasExtractedFolder(ToolDefinition tool)
    {
        var target = Path.Combine(ArtifactService.ToolsRoot, tool.Id);
        return Directory.Exists(target) &&
               Directory.EnumerateFileSystemEntries(target).Any();
    }

    // ── Uninstall / removal ──────────────────────────────────────────────

    /// <summary>
    /// Removes the tool according to its kind and, when
    /// <paramref name="removeDownload"/> is set, also deletes the cached
    /// artifact from the Downloads folder.
    /// </summary>
    public static UninstallResult Uninstall(
        ToolDefinition tool, string downloadsFolder, bool removeDownload = true)
    {
        try
        {
            var result = tool.Kind switch
            {
                ArtifactKind.Binary    => RemoveBinary(tool),
                ArtifactKind.Archive   => RemoveExtracted(tool),
                ArtifactKind.Installer => LaunchVendorUninstaller(tool),
                ArtifactKind.Script    => new UninstallResult(true, $"{tool.Name}: nothing was installed."),
                _                      => new UninstallResult(false, $"{tool.Name}: unsupported kind.")
            };

            if (removeDownload)
            {
                RemoveDownloadedArtifact(tool, downloadsFolder);
            }

            return result;
        }
        catch (Exception ex)
        {
            return new UninstallResult(false, $"{tool.Name}: {ex.Message}");
        }
    }

    private static UninstallResult RemoveBinary(ToolDefinition tool)
    {
        var path = Path.Combine(ArtifactService.BinFolder, tool.FileName);
        if (!File.Exists(path))
            return new UninstallResult(true, $"{tool.Name} is not in Tools\\bin.");

        File.Delete(path);
        return new UninstallResult(true, $"Removed {tool.Name} from Tools\\bin.");
    }

    private static UninstallResult RemoveExtracted(ToolDefinition tool)
    {
        var target = Path.Combine(ArtifactService.ToolsRoot, tool.Id);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        // Also clean up any binary copied to Tools\bin from this archive
        ArtifactService.RemoveArchiveBinaries(tool);

        return new UninstallResult(true, $"Removed extracted files for {tool.Name}.");
    }

    private static UninstallResult LaunchVendorUninstaller(ToolDefinition tool)
    {
        var program = FindInstalledProgram(tool);
        if (program is null)
        {
            return new UninstallResult(
                false,
                $"{tool.Name} wasn't found in the list of installed programs. " +
                "It may not be installed, or was installed under a different name — " +
                "use Windows \"Apps & features\" to remove it.");
        }

        if (!TryParseCommand(program.UninstallCommand, out var exe, out var args))
        {
            return new UninstallResult(
                false, $"Couldn't parse the uninstaller for {tool.Name}.");
        }

        // If msiexec is invoked with /I (install/modify), change to /X (uninstall)
        if (Path.GetFileNameWithoutExtension(exe).Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            args = System.Text.RegularExpressions.Regex.Replace(args, @"(?i)(^|\s)/I(\s|{)", "$1/X$2");
        }

        // UseShellExecute lets the vendor uninstaller surface its own UI / UAC
        // prompt. The app itself is never elevated and nothing runs silently.
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = true
        };
        Process.Start(psi);

        return new UninstallResult(
            true, $"Launched the uninstaller for {program.DisplayName}.");
    }

    private static void RemoveDownloadedArtifact(ToolDefinition tool, string downloadsFolder)
    {
        try
        {
            var path = Path.Combine(downloadsFolder, tool.FileName);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort — a locked cached file shouldn't fail the uninstall.
        }
    }

    // ── Windows uninstall-registry lookup ────────────────────────────────

    /// <summary>
    /// Searches the per-machine (HKLM, 64- and 32-bit views) and per-user
    /// (HKCU) uninstall registries for an entry whose display name matches the
    /// tool, returning the best candidate's uninstall command.
    /// </summary>
    public static InstalledProgram? FindInstalledProgram(ToolDefinition tool)
    {
        var candidates = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, UninstallRoots[0]),
            (Registry.LocalMachine, UninstallRoots[1]),
            (Registry.CurrentUser,  UninstallRoots[0])
        };

        foreach (var (hive, path) in candidates)
        {
            var match = SearchUninstallKey(hive, path, tool);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static InstalledProgram? SearchUninstallKey(
        RegistryKey hive, string path, ToolDefinition tool)
    {
        try
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) return null;

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subKeyName);
                if (sub is null) continue;

                // Skip system components and updates.
                if (sub.GetValue("SystemComponent") is int sc && sc == 1) continue;

                var displayName = sub.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                if (!NameMatches(displayName!, tool)) continue;

                var quiet = sub.GetValue("QuietUninstallString") as string;
                var normal = sub.GetValue("UninstallString") as string;

                var command = !string.IsNullOrWhiteSpace(quiet) ? quiet : normal;
                if (string.IsNullOrWhiteSpace(command)) continue;

                return new InstalledProgram(
                    displayName!, command!, IsQuietCapable: !string.IsNullOrWhiteSpace(quiet));
            }
        }
        catch
        {
            // Inaccessible hive/key — ignore and continue searching.
        }

        return null;
    }

    /// <summary>
    /// Safe matching between a registry DisplayName and the catalog tool.
    /// Prevents substring false-positives (e.g. "Go" matching "Google Chrome",
    /// "Git" matching "Logitech G HUB", or "act" matching "Action!") by enforcing
    /// word boundary matching and handling aliases.
    /// </summary>
    internal static bool NameMatches(string displayName, ToolDefinition tool)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return false;

        var name = tool.Name.Trim();
        if (name.Length == 0) return false;

        // 1. Direct case-insensitive equality
        if (string.Equals(displayName.Trim(), name, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. Known vendor aliases
        if (MatchesKnownAlias(displayName, tool.Id))
            return true;

        // 3. Extract sub-phrases if name has parentheses, e.g. "doctl (DigitalOcean CLI)" -> ["doctl", "DigitalOcean CLI"]
        var phrases = ExtractPhrases(name);

        foreach (var phrase in phrases)
        {
            if (phrase.Length < 2) continue;

            // Use regex word boundaries (\b) so "Go" only matches the whole word "Go", not "Google"
            var escaped = System.Text.RegularExpressions.Regex.Escape(phrase);
            var pattern = $@"\b{escaped}\b";
            if (System.Text.RegularExpressions.Regex.IsMatch(displayName, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesKnownAlias(string displayName, string toolId)
    {
        return toolId.ToLowerInvariant() switch
        {
            "awscli" => displayName.Contains("AWS Command Line Interface", StringComparison.OrdinalIgnoreCase)
                     || displayName.Contains("AWS CLI", StringComparison.OrdinalIgnoreCase),
            "azure-cli" => displayName.Contains("Azure CLI", StringComparison.OrdinalIgnoreCase)
                        || displayName.Contains("Microsoft Azure CLI", StringComparison.OrdinalIgnoreCase),
            "gcloud-cli" => displayName.Contains("Google Cloud SDK", StringComparison.OrdinalIgnoreCase)
                         || displayName.Contains("Google Cloud CLI", StringComparison.OrdinalIgnoreCase),
            "github-cli" => displayName.Contains("GitHub CLI", StringComparison.OrdinalIgnoreCase),
            "git" => displayName.StartsWith("Git", StringComparison.OrdinalIgnoreCase)
                  && System.Text.RegularExpressions.Regex.IsMatch(displayName, @"\bGit\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            "ansible" => displayName.StartsWith("Python 3", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static System.Collections.Generic.List<string> ExtractPhrases(string name)
    {
        var list = new System.Collections.Generic.List<string>();

        // Remove trailing parenthesized parts or split by parens
        var parenMatch = System.Text.RegularExpressions.Regex.Match(name, @"^([^()]+)\s*\(([^()]+)\)");
        if (parenMatch.Success)
        {
            var p1 = parenMatch.Groups[1].Value.Trim();
            var p2 = parenMatch.Groups[2].Value.Trim();
            if (!string.IsNullOrEmpty(p1)) list.Add(p1);
            if (!string.IsNullOrEmpty(p2)) list.Add(p2);
        }
        else
        {
            list.Add(name);
        }

        return list;
    }

    /// <summary>
    /// Splits an uninstall command line into an executable path and its
    /// arguments, handling both quoted paths and bare tokens.
    /// </summary>
    private static bool TryParseCommand(string command, out string exe, out string args)
    {
        exe = string.Empty;
        args = string.Empty;

        command = command.Trim();
        if (command.Length == 0) return false;

        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) return false;

            exe = command.Substring(1, end - 1).Trim();
            args = command[(end + 1)..].Trim();
            return exe.Length > 0;
        }

        // Unquoted: split after the first ".exe" token when present,
        // otherwise on the first space.
        var exeIdx = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx >= 0)
        {
            var splitAt = exeIdx + 4;
            exe = command[..splitAt].Trim();
            args = command[splitAt..].Trim();
            return true;
        }

        var space = command.IndexOf(' ');
        if (space < 0)
        {
            exe = command;
            return true;
        }

        exe = command[..space];
        args = command[(space + 1)..].Trim();
        return true;
    }
}
