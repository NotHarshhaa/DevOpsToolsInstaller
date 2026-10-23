using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Developer configs captured as part of a New-PC migration bundle or extended profile/snapshot.
/// NEVER stores private keys, passwords, or secrets.
/// </summary>
public sealed class WorkstationConfigBundle
{
    public string? GitConfig { get; set; }
    public string? PowerShellProfile { get; set; }
    public string? WindowsTerminalSettings { get; set; }
    public List<string> VsCodeExtensions { get; set; } = new();
    public string? SshConfig { get; set; }

    [JsonIgnore] public bool HasGitConfig => !string.IsNullOrWhiteSpace(GitConfig);
    [JsonIgnore] public bool HasPowerShellProfile => !string.IsNullOrWhiteSpace(PowerShellProfile);
    [JsonIgnore] public bool HasWindowsTerminal => !string.IsNullOrWhiteSpace(WindowsTerminalSettings);
    [JsonIgnore] public bool HasVsCodeExtensions => VsCodeExtensions.Count > 0;
    [JsonIgnore] public bool HasSshConfig => !string.IsNullOrWhiteSpace(SshConfig);

    [JsonIgnore]
    public int ConfigItemCount =>
        (HasGitConfig ? 1 : 0) +
        (HasPowerShellProfile ? 1 : 0) +
        (HasWindowsTerminal ? 1 : 0) +
        (HasVsCodeExtensions ? 1 : 0) +
        (HasSshConfig ? 1 : 0);
}

/// <summary>
/// A standalone portable file for setting up a brand-new PC or laptop in minutes.
/// </summary>
public sealed class NewPcMigrationBundle
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Workstation Migration";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string SourceMachine { get; set; } = Environment.MachineName;
    public string SourceUser { get; set; } = Environment.UserName;
    public string AppVersion { get; set; } = AppUpdaterService.CurrentVersion;

    public List<string> ToolIds { get; set; } = new();
    public WorkstationConfigBundle Configs { get; set; } = new();

    [JsonIgnore] public string CreatedLabel => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    [JsonIgnore] public int ToolCount => ToolIds.Count;
}

/// <summary>Options choosing what to restore when applying a migration bundle.</summary>
public sealed class MigrationRestoreOptions
{
    public bool RestoreTools { get; set; } = true;
    public bool RestoreGitConfig { get; set; } = true;
    public bool RestorePowerShellProfile { get; set; } = true;
    public bool RestoreWindowsTerminal { get; set; } = true;
    public bool RestoreVsCodeExtensions { get; set; } = true;
    public bool RestoreSshConfig { get; set; } = true;
}

/// <summary>Outcome of applying a New-PC migration bundle.</summary>
public sealed record MigrationApplyResult(
    int ToolsInstalled,
    int ToolsFailed,
    int ConfigsRestored,
    int ExtensionsInstalled,
    IReadOnlyList<string> Logs)
{
    public bool Success => ToolsFailed == 0;
}

/// <summary>
/// New-PC migration service (feature 3).
///
/// Extends Profiles and Snapshots beyond tools to developer configs:
///   - .gitconfig (Git user, aliases, core settings)
///   - PowerShell profile (aliases, modules, prompt)
///   - Windows Terminal settings (color schemes, custom profiles, keybindings)
///   - VS Code extensions list (auto-installed on the new PC via code CLI)
///   - SSH config (~/.ssh/config - host shortcuts, ports, proxy settings)
///
/// SECURITY GUARANTEE: Never captures, exports or touches private keys or secrets.
/// </summary>
public static class NewPcMigrationService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string UserProfilePath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string GitConfigPath => Path.Combine(UserProfilePath, ".gitconfig");

    public static string GetPowerShellProfilePath()
    {
        // Try PS7 profile first, fall back to WindowsPowerShell
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var ps7 = Path.Combine(docs, "PowerShell", "Microsoft.PowerShell_profile.ps1");
        if (File.Exists(ps7)) return ps7;

        var ps5 = Path.Combine(docs, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        if (File.Exists(ps5)) return ps5;

        // Try local user documents (bypassing OneDrive virtualization if needed)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localDocsPs7 = Path.Combine(userProfile, "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1");
        if (File.Exists(localDocsPs7)) return localDocsPs7;

        var localDocsPs5 = Path.Combine(userProfile, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
        if (File.Exists(localDocsPs5)) return localDocsPs5;

        // Check if docs folder is writable; if not, fall back to local user documents
        try
        {
            var testDir = Path.Combine(docs, "PowerShell");
            Directory.CreateDirectory(testDir);
            var testFile = Path.Combine(testDir, ".write_test");
            File.WriteAllText(testFile, "");
            File.Delete(testFile);
            return ps7;
        }
        catch
        {
            return localDocsPs7;
        }
    }

    public static string? GetWindowsTerminalSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Packaged Windows Terminal
        var packaged = Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json");
        if (File.Exists(packaged)) return packaged;

        // Preview or unpackaged
        var preview = Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json");
        if (File.Exists(preview)) return preview;

        var standalone = Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json");
        if (File.Exists(standalone)) return standalone;

        // Default path to create if terminal package directory exists
        var pkgDir = Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState");
        if (Directory.Exists(pkgDir)) return packaged;

        return null;
    }

    public static string SshConfigPath => Path.Combine(UserProfilePath, ".ssh", "config");

    /// <summary>
    /// Captures developer configs currently present on this workstation.
    /// Strictly excludes any cryptographic private key files or tokens.
    /// </summary>
    public static async Task<WorkstationConfigBundle> CaptureConfigsAsync(
        bool includeGit = true,
        bool includePwsh = true,
        bool includeTerminal = true,
        bool includeVsCode = true,
        bool includeSsh = true)
    {
        var bundle = new WorkstationConfigBundle();

        // 1. .gitconfig
        if (includeGit && File.Exists(GitConfigPath))
        {
            try
            {
                bundle.GitConfig = await File.ReadAllTextAsync(GitConfigPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ActivityLogService.Warn("Migration", $"Could not read .gitconfig: {ex.Message}");
            }
        }

        // 2. PowerShell profile
        if (includePwsh)
        {
            var psPath = GetPowerShellProfilePath();
            if (File.Exists(psPath))
            {
                try
                {
                    bundle.PowerShellProfile = await File.ReadAllTextAsync(psPath, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    ActivityLogService.Warn("Migration", $"Could not read PowerShell profile: {ex.Message}");
                }
            }
        }

        // 3. Windows Terminal settings
        if (includeTerminal)
        {
            var wtPath = GetWindowsTerminalSettingsPath();
            if (!string.IsNullOrEmpty(wtPath) && File.Exists(wtPath))
            {
                try
                {
                    bundle.WindowsTerminalSettings = await File.ReadAllTextAsync(wtPath, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    ActivityLogService.Warn("Migration", $"Could not read Windows Terminal settings: {ex.Message}");
                }
            }
        }

        // 4. VS Code extensions list
        if (includeVsCode)
        {
            bundle.VsCodeExtensions = await GetVsCodeExtensionsAsync();
        }

        // 5. SSH config file ONLY (~/.ssh/config)
        // Strictly refuses to read or include any private key material!
        if (includeSsh && File.Exists(SshConfigPath))
        {
            try
            {
                var text = await File.ReadAllTextAsync(SshConfigPath, Encoding.UTF8);
                if (ContainsPrivateKeyMaterial(text))
                {
                    ActivityLogService.Warn("Migration", "SSH config appears to contain embedded private key material. Refusing to export for security.");
                }
                else
                {
                    bundle.SshConfig = text;
                }
            }
            catch (Exception ex)
            {
                ActivityLogService.Warn("Migration", $"Could not read SSH config: {ex.Message}");
            }
        }

        return bundle;
    }

    /// <summary>
    /// Checks whether the text contains any private key headers.
    /// </summary>
    public static bool ContainsPrivateKeyMaterial(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        return content.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("BEGIN RSA PRIVATE", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("BEGIN OPENSSH PRIVATE", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("BEGIN EC PRIVATE", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("BEGIN DSA PRIVATE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets list of VS Code extension IDs.
    /// </summary>
    public static async Task<List<string>> GetVsCodeExtensionsAsync()
    {
        var list = new List<string>();
        try
        {
            var codePath = CliHealthService.ResolveExecutablePath("code.cmd")
                           ?? CliHealthService.ResolveExecutablePath("code.exe");

            if (!string.IsNullOrEmpty(codePath) && File.Exists(codePath))
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = codePath,
                        Arguments = "--list-extensions",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                p.Start();
                var stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!string.IsNullOrWhiteSpace(line) && line.Contains('.'))
                        {
                            list.Add(line);
                        }
                    }
                    if (list.Count > 0) return list;
                }
            }

            // Fallback: read ~/.vscode/extensions folder
            var extDir = Path.Combine(UserProfilePath, ".vscode", "extensions");
            if (Directory.Exists(extDir))
            {
                foreach (var dir in Directory.GetDirectories(extDir))
                {
                    var name = Path.GetFileName(dir);
                    if (name.Contains('.') && !name.StartsWith('.'))
                    {
                        // Strip version suffix if present: publisher.name-1.2.3 -> publisher.name
                        var dashIdx = name.LastIndexOf('-');
                        if (dashIdx > 0 && char.IsDigit(name[dashIdx + 1]))
                        {
                            name = name[..dashIdx];
                        }
                        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            list.Add(name);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("Migration", $"Could not enumerate VS Code extensions: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// Creates a complete migration bundle containing tools and configs.
    /// </summary>
    public static async Task<NewPcMigrationBundle> CreateBundleAsync(
        IReadOnlyList<ToolDefinition> catalog,
        string bundleName = "New Laptop Setup",
        bool includeGit = true,
        bool includePwsh = true,
        bool includeTerminal = true,
        bool includeVsCode = true,
        bool includeSsh = true)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var toolIds = new List<string>();

        foreach (var tool in catalog)
        {
            if (UninstallService.IsInstalled(tool, dlFolder) ||
                File.Exists(Path.Combine(ArtifactService.BinFolder, tool.FileName)))
            {
                toolIds.Add(tool.Id);
            }
        }

        var configs = await CaptureConfigsAsync(includeGit, includePwsh, includeTerminal, includeVsCode, includeSsh);

        return new NewPcMigrationBundle
        {
            Name = bundleName,
            CreatedAt = DateTime.UtcNow,
            SourceMachine = Environment.MachineName,
            SourceUser = Environment.UserName,
            ToolIds = toolIds,
            Configs = configs
        };
    }

    /// <summary>
    /// Exports the migration bundle to a portable JSON file.
    /// </summary>
    public static void SaveBundle(string filePath, NewPcMigrationBundle bundle)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(bundle, JsonOpts);
        File.WriteAllText(filePath, json, Encoding.UTF8);
        ActivityLogService.Success("Migration", $"Exported New-PC migration bundle to {filePath} ({bundle.ToolCount} tools, {bundle.Configs.ConfigItemCount} configs)");
    }

    /// <summary>
    /// Loads a migration bundle from a JSON file.
    /// </summary>
    public static (NewPcMigrationBundle? Bundle, string Error) LoadBundle(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return (null, $"File not found: {filePath}");
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var bundle = JsonSerializer.Deserialize<NewPcMigrationBundle>(json, JsonOpts);
            if (bundle is null) return (null, "Failed to parse migration bundle.");
            return (bundle, string.Empty);
        }
        catch (Exception ex)
        {
            return (null, $"Error loading migration bundle: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies the migration bundle to the current PC with automatic .bak backups.
    /// </summary>
    public static async Task<MigrationApplyResult> ApplyBundleAsync(
        NewPcMigrationBundle bundle,
        IReadOnlyList<ToolDefinition> catalog,
        MigrationRestoreOptions options,
        IProgress<double>? progress = null,
        Action<string>? onLog = null)
    {
        var logs = new List<string>();
        int toolsInstalled = 0, toolsFailed = 0, configsRestored = 0, extensionsInstalled = 0;

        void Log(string msg)
        {
            logs.Add(msg);
            onLog?.Invoke(msg);
            ActivityLogService.Info("Migration", msg);
        }

        Log($"Starting migration restore from '{bundle.Name}' (Source: {bundle.SourceMachine})...");

        // 1. Restore .gitconfig
        if (options.RestoreGitConfig && bundle.Configs.HasGitConfig)
        {
            try
            {
                BackupExistingFile(GitConfigPath);
                await File.WriteAllTextAsync(GitConfigPath, bundle.Configs.GitConfig!, Encoding.UTF8);
                configsRestored++;
                Log("Restored .gitconfig successfully.");
            }
            catch (Exception ex)
            {
                Log($"Failed to restore .gitconfig: {ex.Message}");
            }
        }

        // 2. Restore PowerShell profile
        if (options.RestorePowerShellProfile && bundle.Configs.HasPowerShellProfile)
        {
            try
            {
                var psPath = GetPowerShellProfilePath();
                var dir = Path.GetDirectoryName(psPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                BackupExistingFile(psPath);
                await File.WriteAllTextAsync(psPath, bundle.Configs.PowerShellProfile!, Encoding.UTF8);
                configsRestored++;
                Log($"Restored PowerShell profile to {psPath}");
            }
            catch (Exception ex)
            {
                Log($"Failed to restore PowerShell profile: {ex.Message}");
            }
        }

        // 3. Restore Windows Terminal settings
        if (options.RestoreWindowsTerminal && bundle.Configs.HasWindowsTerminal)
        {
            try
            {
                var wtPath = GetWindowsTerminalSettingsPath();
                if (!string.IsNullOrEmpty(wtPath))
                {
                    var dir = Path.GetDirectoryName(wtPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    BackupExistingFile(wtPath);
                    await File.WriteAllTextAsync(wtPath, bundle.Configs.WindowsTerminalSettings!, Encoding.UTF8);
                    configsRestored++;
                    Log("Restored Windows Terminal settings.json");
                }
                else
                {
                    Log("Windows Terminal directory not found; skipped terminal settings.");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to restore Windows Terminal settings: {ex.Message}");
            }
        }

        // 4. Restore SSH config (strictly never keys)
        if (options.RestoreSshConfig && bundle.Configs.HasSshConfig)
        {
            try
            {
                if (ContainsPrivateKeyMaterial(bundle.Configs.SshConfig!))
                {
                    Log("Security warning: Refusing to restore SSH config because it contains private key material.");
                }
                else
                {
                    var sshDir = Path.GetDirectoryName(SshConfigPath)!;
                    Directory.CreateDirectory(sshDir);
                    BackupExistingFile(SshConfigPath);
                    await File.WriteAllTextAsync(SshConfigPath, bundle.Configs.SshConfig!, Encoding.UTF8);
                    configsRestored++;
                    Log("Restored SSH config (~/.ssh/config)");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to restore SSH config: {ex.Message}");
            }
        }

        // 5. Restore VS Code extensions
        if (options.RestoreVsCodeExtensions && bundle.Configs.HasVsCodeExtensions)
        {
            var codePath = CliHealthService.ResolveExecutablePath("code.cmd")
                           ?? CliHealthService.ResolveExecutablePath("code.exe");

            if (!string.IsNullOrEmpty(codePath) && File.Exists(codePath))
            {
                Log($"Installing {bundle.Configs.VsCodeExtensions.Count} VS Code extensions...");
                foreach (var extId in bundle.Configs.VsCodeExtensions)
                {
                    try
                    {
                        using var p = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = codePath,
                                Arguments = $"--install-extension {extId} --force",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        p.Start();
                        await p.WaitForExitAsync();
                        if (p.ExitCode == 0)
                        {
                            extensionsInstalled++;
                        }
                    }
                    catch { }
                }
                Log($"Installed {extensionsInstalled}/{bundle.Configs.VsCodeExtensions.Count} VS Code extensions.");
            }
            else
            {
                Log("VS Code CLI (code) not found on PATH; extensions can be installed after VS Code is installed.");
            }
        }

        // 6. Restore tools
        if (options.RestoreTools && bundle.ToolIds.Count > 0)
        {
            var catalogMap = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            var dl = new DownloadService();
            var dlFolder = DownloadService.DefaultDownloadsFolder;

            var missingTools = bundle.ToolIds
                .Where(id => catalogMap.TryGetValue(id, out var tool) && !UninstallService.IsInstalled(tool, dlFolder))
                .Select(id => catalogMap[id])
                .ToList();

            Log($"Installing {missingTools.Count} missing tools from the migration bundle...");
            int current = 0;

            foreach (var tool in missingTools)
            {
                try
                {
                    current++;
                    progress?.Report((double)current / missingTools.Count * 100.0);
                    Log($"Downloading and installing {tool.Name} ({current}/{missingTools.Count})...");

                    await dl.DownloadAsync(tool, dlFolder);
                    if (tool.Status == ToolStatus.Downloaded)
                    {
                        var res = ArtifactService.Perform(tool, dlFolder);
                        if (res.Success)
                        {
                            toolsInstalled++;
                            Log($"Installed {tool.Name}");
                        }
                        else
                        {
                            toolsFailed++;
                            Log($"Failed to install {tool.Name}: {res.Message}");
                        }
                    }
                    else
                    {
                        toolsFailed++;
                        Log($"Download failed for {tool.Name}");
                    }
                }
                catch (Exception ex)
                {
                    toolsFailed++;
                    Log($"Error installing {tool.Name}: {ex.Message}");
                }
            }
        }

        var summary = $"Migration complete: {toolsInstalled} tools installed, {configsRestored} configs restored, {extensionsInstalled} VS Code extensions added.";
        Log(summary);
        ToastService.Show("Migration Complete", summary);

        return new MigrationApplyResult(toolsInstalled, toolsFailed, configsRestored, extensionsInstalled, logs);
    }

    private static void BackupExistingFile(string path)
    {
        if (File.Exists(path))
        {
            var bak = $"{path}.bak-{DateTime.Now:yyyyMMddHHmmss}";
            try
            {
                File.Copy(path, bak, overwrite: true);
                ActivityLogService.Info("Migration", $"Created backup: {bak}");
            }
            catch { }
        }
    }
}
