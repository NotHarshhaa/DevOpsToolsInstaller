using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DevOpsToolsInstaller.Services;

public sealed record TerminalProfileItem(
    string? Guid,
    string Name,
    string? Commandline,
    bool IsDefault);

public sealed record TerminalSettingsSummary(
    bool IsInstalled,
    string? SettingsPath,
    string? DefaultProfileName,
    string? CurrentFont,
    IReadOnlyList<TerminalProfileItem> Profiles,
    IReadOnlyList<string> AvailableColorSchemes);

public sealed record PowerShellProfileStatus(
    string ProfilePath,
    bool Exists,
    bool HasDevOpsCompletions,
    bool HasDevOpsAliases,
    bool HasStarship,
    bool HasOhMyPosh);

/// <summary>
/// Service for Windows Terminal profiles, themes, font configuration,
/// and PowerShell profile completion & DevOps alias management.
/// </summary>
public static class TerminalPowerShellSetupService
{
    private const string CompletionsHeader = "# >>> DevOpsToolsInstaller shell completion >>>";
    private const string CompletionsFooter = "# <<< DevOpsToolsInstaller shell completion <<<";
    private const string AliasesHeader = "# >>> DevOpsToolsInstaller DevOps Aliases >>>";
    private const string AliasesFooter = "# <<< DevOpsToolsInstaller DevOps Aliases <<<";

    public static readonly Dictionary<string, string> DevOpsAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["k"] = "kubectl",
        ["tf"] = "terraform",
        ["d"] = "docker",
        ["dc"] = "docker compose",
        ["g"] = "git",
        ["kns"] = "kubens",
        ["ktx"] = "kubectx",
        ["ll"] = "Get-ChildItem -Force"
    };

    /// <summary>
    /// Discovers Windows Terminal settings and parses installed profiles and schemes.
    /// </summary>
    public static async Task<TerminalSettingsSummary> GetTerminalSettingsAsync()
    {
        var settingsPath = NewPcMigrationService.GetWindowsTerminalSettingsPath();
        if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath))
        {
            return new TerminalSettingsSummary(false, null, null, null, Array.Empty<TerminalProfileItem>(), Array.Empty<string>());
        }

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath, Encoding.UTF8);
            var root = JsonNode.Parse(json);
            if (root is null) return new TerminalSettingsSummary(true, settingsPath, null, null, Array.Empty<TerminalProfileItem>(), Array.Empty<string>());

            var defaultProfileGuid = root["defaultProfile"]?.GetValue<string>();
            string? currentFont = null;

            // Check profiles.defaults.font.face
            var defaultFontFace = root["profiles"]?["defaults"]?["font"]?["face"]?.GetValue<string>()
                ?? root["profiles"]?["defaults"]?["fontFace"]?.GetValue<string>();
            currentFont = defaultFontFace;

            var profileItems = new List<TerminalProfileItem>();
            var profileList = root["profiles"]?["list"]?.AsArray();
            if (profileList != null)
            {
                foreach (var p in profileList)
                {
                    if (p is null) continue;
                    var name = p["name"]?.GetValue<string>() ?? "Shell";
                    var guid = p["guid"]?.GetValue<string>();
                    var cmd = p["commandline"]?.GetValue<string>();
                    bool isDef = (!string.IsNullOrEmpty(defaultProfileGuid) && string.Equals(defaultProfileGuid, guid, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(defaultProfileGuid) && string.Equals(defaultProfileGuid, name, StringComparison.OrdinalIgnoreCase));

                    profileItems.Add(new TerminalProfileItem(guid, name, cmd, isDef));
                }
            }

            var defaultProf = profileItems.FirstOrDefault(p => p.IsDefault);

            var schemes = new List<string> { "Campbell", "One Half Dark", "Dracula", "Solarized Dark", "Vintage" };
            var schemesArray = root["schemes"]?.AsArray();
            if (schemesArray != null)
            {
                foreach (var s in schemesArray)
                {
                    var sName = s?["name"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(sName) && !schemes.Contains(sName))
                    {
                        schemes.Add(sName);
                    }
                }
            }

            return new TerminalSettingsSummary(true, settingsPath, defaultProf?.Name ?? "Default", currentFont, profileItems, schemes);
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("Terminal", $"Could not parse Windows Terminal settings: {ex.Message}");
            return new TerminalSettingsSummary(true, settingsPath, null, null, Array.Empty<TerminalProfileItem>(), Array.Empty<string>());
        }
    }

    /// <summary>
    /// Sets the default profile in Windows Terminal by profile GUID or Name.
    /// </summary>
    public static async Task<bool> SetTerminalDefaultProfileAsync(string profileGuidOrName)
    {
        var settingsPath = NewPcMigrationService.GetWindowsTerminalSettingsPath();
        if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath, Encoding.UTF8);
            var root = JsonNode.Parse(json);
            if (root is null) return false;

            root["defaultProfile"] = profileGuidOrName;
            await File.WriteAllTextAsync(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            ActivityLogService.Info("Terminal", $"Set default Windows Terminal profile to '{profileGuidOrName}'.");
            return true;
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Terminal", $"Failed to update default profile: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Updates the default font face across Windows Terminal profiles (e.g. Cascadia Code NF, JetBrains Mono).
    /// </summary>
    public static async Task<bool> SetTerminalFontFaceAsync(string fontFace)
    {
        var settingsPath = NewPcMigrationService.GetWindowsTerminalSettingsPath();
        if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath)) return false;

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath, Encoding.UTF8);
            var root = JsonNode.Parse(json);
            if (root is null) return false;

            var profilesNode = root["profiles"]?.AsObject();
            if (profilesNode is null)
            {
                profilesNode = new JsonObject();
                root["profiles"] = profilesNode;
            }

            var defaultsNode = profilesNode["defaults"]?.AsObject();
            if (defaultsNode is null)
            {
                defaultsNode = new JsonObject();
                profilesNode["defaults"] = defaultsNode;
            }

            var fontNode = defaultsNode["font"]?.AsObject();
            if (fontNode is null)
            {
                fontNode = new JsonObject();
                defaultsNode["font"] = fontNode;
            }

            fontNode["face"] = fontFace;
            await File.WriteAllTextAsync(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            ActivityLogService.Info("Terminal", $"Updated Windows Terminal font face to '{fontFace}'.");
            return true;
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Terminal", $"Failed to update font face: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks the user's PowerShell profile state.
    /// </summary>
    public static async Task<PowerShellProfileStatus> GetPowerShellProfileStatusAsync()
    {
        var path = NewPcMigrationService.GetPowerShellProfilePath();
        bool exists = File.Exists(path);
        bool completions = false;
        bool aliases = false;
        bool starship = false;
        bool omp = false;

        if (exists)
        {
            try
            {
                var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                completions = content.Contains(CompletionsHeader);
                aliases = content.Contains(AliasesHeader);
                starship = content.Contains("starship init powershell", StringComparison.OrdinalIgnoreCase);
                omp = content.Contains("oh-my-posh init pwsh", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }

        return new PowerShellProfileStatus(path, exists, completions, aliases, starship, omp);
    }

    /// <summary>
    /// Applies shell completions, DevOps aliases, and prompt hooks to the user's PowerShell profile.
    /// </summary>
    public static async Task<bool> ApplyPowerShellSetupAsync(
        bool enableCompletions,
        bool enableAliases,
        bool enableStarship)
    {
        var profilePath = NewPcMigrationService.GetPowerShellProfilePath();
        var dir = Path.GetDirectoryName(profilePath)!;
        Directory.CreateDirectory(dir);

        string current = "";
        if (File.Exists(profilePath))
        {
            current = await File.ReadAllTextAsync(profilePath, Encoding.UTF8);
            // Backup before modifying
            try { File.Copy(profilePath, profilePath + ".bak", overwrite: true); } catch { }
        }

        var sb = new StringBuilder();

        // 1. Remove existing managed blocks
        current = RemoveManagedBlock(current, CompletionsHeader, CompletionsFooter);
        current = RemoveManagedBlock(current, AliasesHeader, AliasesFooter);

        sb.Append(current.TrimEnd());
        if (sb.Length > 0) sb.AppendLine().AppendLine();

        // 2. Append completions block
        if (enableCompletions)
        {
            sb.AppendLine(CompletionsHeader);
            sb.AppendLine(ShellCompletionService.GeneratePowerShellSnippet(Array.Empty<Models.ToolDefinition>(), includeAllSupported: true));
            sb.AppendLine(CompletionsFooter);
            sb.AppendLine();
        }

        // 3. Append aliases block
        if (enableAliases)
        {
            sb.AppendLine(AliasesHeader);
            sb.AppendLine("# Curated DevOps Aliases");
            foreach (var (alias, cmd) in DevOpsAliases)
            {
                if (cmd.Contains(' '))
                {
                    sb.AppendLine($"function global:{alias} {{ {cmd} @args }}");
                }
                else
                {
                    sb.AppendLine($"Set-Alias -Name {alias} -Value {cmd} -Option AllScope -ErrorAction SilentlyContinue");
                }
            }
            sb.AppendLine(AliasesFooter);
            sb.AppendLine();
        }

        // 4. Append starship prompt if requested
        if (enableStarship && !sb.ToString().Contains("starship init powershell", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("# Starship Prompt");
            sb.AppendLine("if (Get-Command starship -ErrorAction SilentlyContinue) {");
            sb.AppendLine("    Invoke-Expression (&starship init powershell)");
            sb.AppendLine("}");
        }

        try
        {
            await File.WriteAllTextAsync(profilePath, sb.ToString(), Encoding.UTF8);
            ActivityLogService.Info("PowerShell", $"Configured PowerShell profile at {profilePath}");
            return true;
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("PowerShell", $"Failed to update profile: {ex.Message}");
            return false;
        }
    }

    private static string RemoveManagedBlock(string source, string startMarker, string endMarker)
    {
        var startIdx = source.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return source;

        var endIdx = source.IndexOf(endMarker, startIdx, StringComparison.OrdinalIgnoreCase);
        if (endIdx < 0) return source;

        endIdx += endMarker.Length;
        return source.Remove(startIdx, endIdx - startIdx).Trim();
    }
}
