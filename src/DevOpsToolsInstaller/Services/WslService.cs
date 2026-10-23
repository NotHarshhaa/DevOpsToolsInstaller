using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevOpsToolsInstaller.Services;

public sealed record WslDistroInfo(
    string Name,
    string State,
    int Version,
    bool IsDefault)
{
    public string DisplayState => $"{State} (WSL {Version})";
    public bool IsRunning => string.Equals(State, "Running", StringComparison.OrdinalIgnoreCase);
    public string BadgeText => IsDefault ? $"{Name} (Default)" : Name;
}

public sealed record WslPrerequisiteReport(
    bool IsWslInstalled,
    string? WslVersion,
    bool IsVirtualMachinePlatformEnabled,
    bool IsWslFeatureEnabled,
    bool IsHyperVEnabled,
    bool IsVirtualizationInFirmware,
    string Summary,
    IReadOnlyList<string> Recommendations);

public sealed record WslToolStatus(
    string ToolId,
    string Name,
    string Command,
    bool IsInstalledInWsl,
    string? WslPath,
    string? WslVersion,
    string? WindowsVersion);

/// <summary>
/// Service for WSL awareness, distribution detection, inside-WSL tool checks and installation,
/// and virtualization/WSL2 prerequisite remediation.
/// </summary>
public static class WslService
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    /// <summary>
    /// Checks whether wsl.exe is available on the machine.
    /// </summary>
    public static bool IsWslAvailable()
    {
        try
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(system32, "wsl.exe"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enumerates installed WSL distributions, parsing wsl -l -v output.
    /// Safely handles UTF-16LE unicode encoding and distro states.
    /// </summary>
    public static async Task<List<WslDistroInfo>> ListDistrosAsync()
    {
        var list = new List<WslDistroInfo>();
        if (!IsWslAvailable()) return list;

        try
        {
            var raw = await ExecuteWslCommandAsync("-l -v");
            if (string.IsNullOrWhiteSpace(raw)) return list;

            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = CleanWslString(rawLine).Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("NAME", StringComparison.OrdinalIgnoreCase)) continue;

                bool isDefault = line.StartsWith("*");
                var trimmed = line.TrimStart('*', ' ');

                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var name = parts[0].Trim();
                    var state = parts[1].Trim();
                    _ = int.TryParse(parts[2].Trim(), out int ver);
                    if (ver == 0) ver = 2;

                    list.Add(new WslDistroInfo(name, state, ver, isDefault));
                }
            }
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("WSL", $"Error listing distributions: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// Evaluates WSL2 and Virtualization prerequisites without requiring admin elevation:
    /// uses wsl.exe status, CIM hypervisor presence, and registry service state.
    /// </summary>
    public static async Task<WslPrerequisiteReport> CheckPrerequisitesAsync()
    {
        bool hasWsl = IsWslAvailable();
        string? wslVer = null;
        var recs = new List<string>();

        if (hasWsl)
        {
            try
            {
                var statusOutput = await ExecuteWslCommandAsync("--status");
                var cleanStatus = CleanWslString(statusOutput);
                if (!string.IsNullOrWhiteSpace(cleanStatus))
                {
                    var m = Regex.Match(cleanStatus, @"Default Version:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        wslVer = $"WSL {m.Groups[1].Value}";
                    }
                }
            }
            catch { }
        }

        bool vmpEnabled = false;
        bool wslFeatureEnabled = false;
        bool hypervEnabled = false;
        bool firmwareVirt = true;

        try
        {
            // 1. Check HypervisorPresent via CIM (non-elevated)
            var hyperOutput = await RunPowerShellAsync("(Get-CimInstance Win32_ComputerSystem).HypervisorPresent");
            if (bool.TryParse(hyperOutput.Trim(), out bool isHyper) && isHyper)
            {
                vmpEnabled = true;
                hypervEnabled = true;
            }

            // 2. Check Virtualization in firmware
            var fwOutput = await RunPowerShellAsync("(Get-CimInstance Win32_Processor | Select-Object -First 1).VirtualizationFirmwareEnabled");
            if (bool.TryParse(fwOutput.Trim(), out bool fw) && !fw && !vmpEnabled)
            {
                firmwareVirt = false;
            }

            // 3. Inspect Registry service flags for WSL (read-only, no elevation needed)
            var regCheck = await RunPowerShellAsync("Test-Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\lxss'");
            if (bool.TryParse(regCheck.Trim(), out bool hasLxss) && hasLxss)
            {
                wslFeatureEnabled = true;
            }
        }
        catch { }

        // If wsl.exe --status returned a default version, WSL2 is definitely operational
        if (!string.IsNullOrEmpty(wslVer))
        {
            wslFeatureEnabled = true;
            vmpEnabled = true;
        }

        if (!hasWsl || !wslFeatureEnabled)
        {
            recs.Add("Enable the 'Microsoft-Windows-Subsystem-Linux' optional feature.");
        }
        if (!vmpEnabled)
        {
            recs.Add("Enable 'Virtual Machine Platform' (required for WSL2, Docker Desktop, and minikube).");
        }
        if (!firmwareVirt)
        {
            recs.Add("Hardware virtualization (VT-x / AMD-V) appears disabled in your PC BIOS/UEFI firmware.");
        }
        if (recs.Count == 0)
        {
            recs.Add("All WSL2 and virtualization prerequisites are met.");
        }

        var summary = (hasWsl && vmpEnabled)
            ? "WSL2 and hardware virtualization platform are enabled and healthy."
            : "Some WSL2 or virtualization components need configuration.";

        return new WslPrerequisiteReport(
            hasWsl,
            wslVer ?? (hasWsl ? "WSL 2" : null),
            vmpEnabled,
            wslFeatureEnabled,
            hypervEnabled,
            firmwareVirt,
            summary,
            recs);
    }

    /// <summary>
    /// Probes DevOps command-line tools inside a specific WSL distribution.
    /// </summary>
    public static async Task<List<WslToolStatus>> CheckToolsInsideDistroAsync(string distroName)
    {
        var toolsToCheck = new (string Id, string Name, string Cmd)[]
        {
            ("docker", "Docker", "docker"),
            ("kubectl", "kubectl", "kubectl"),
            ("helm", "Helm", "helm"),
            ("terraform", "Terraform", "terraform"),
            ("git", "Git", "git"),
            ("k9s", "k9s", "k9s"),
            ("aws", "AWS CLI", "aws"),
            ("minikube", "minikube", "minikube"),
            ("kind", "kind", "kind"),
            ("gh", "GitHub CLI", "gh")
        };

        var results = new List<WslToolStatus>();
        if (!IsWslAvailable() || string.IsNullOrWhiteSpace(distroName)) return results;

        foreach (var (id, name, cmd) in toolsToCheck)
        {
            string? wslPath = null;
            string? wslVer = null;
            bool installed = false;

            try
            {
                var whichOut = await ExecuteWslCommandAsync($"-d \"{distroName}\" -- which {cmd}");
                var cleanWhich = CleanWslString(whichOut).Trim();
                if (!string.IsNullOrWhiteSpace(cleanWhich) && !cleanWhich.Contains("no ") && cleanWhich.StartsWith("/"))
                {
                    installed = true;
                    wslPath = cleanWhich.Split('\n').FirstOrDefault()?.Trim();

                    // Probe version inside WSL
                    var verOut = await ExecuteWslCommandAsync($"-d \"{distroName}\" -- {cmd} --version");
                    var cleanVer = CleanWslString(verOut).Trim();
                    if (!string.IsNullOrWhiteSpace(cleanVer))
                    {
                        var firstLine = cleanVer.Split('\n').FirstOrDefault()?.Trim();
                        var vm = Regex.Match(firstLine ?? "", @"(\d+\.\d+[\.\d\w\-]*)");
                        wslVer = vm.Success ? vm.Groups[1].Value : firstLine;
                    }
                }
            }
            catch { }

            results.Add(new WslToolStatus(id, name, cmd, installed, wslPath, wslVer, null));
        }

        return results;
    }

    /// <summary>
    /// Launches an interactive terminal session inside the specified WSL distribution to execute tool installation.
    /// Opens in a dedicated visible terminal so the user can review package manager output and provide sudo credentials.
    /// </summary>
    public static bool InstallToolInDistro(string distroName, string toolId, bool interactive = true)
    {
        if (!IsWslAvailable() || string.IsNullOrWhiteSpace(distroName)) return false;

        var installCmd = GetInstallCommandForDistro(distroName, toolId);

        try
        {
            if (interactive)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = $"-d \"{distroName}\" bash -ic \"echo 'Installing {toolId} in {distroName}...'; {installCmd}; echo; echo 'Done. Press Enter to exit.'; read -r\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
                ActivityLogService.Info("WSL", $"Launched interactive installer for '{toolId}' in '{distroName}'.");
                return true;
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = $"-d \"{distroName}\" -- bash -c \"{installCmd}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                return proc != null;
            }
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("WSL", $"Failed to launch installation in WSL: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens an interactive bash terminal inside the selected WSL distribution.
    /// </summary>
    public static bool LaunchDistroTerminal(string distroName)
    {
        if (!IsWslAvailable() || string.IsNullOrWhiteSpace(distroName)) return false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = $"-d \"{distroName}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates an elevated remediation script to enable VirtualMachinePlatform and WSL2.
    /// </summary>
    public static string GeneratePrerequisiteFixScript()
    {
        return """
# Elevated PowerShell script to enable WSL2 and Virtual Machine Platform
Write-Host "Enabling WSL and Virtual Machine Platform..." -ForegroundColor Cyan

dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart

wsl.exe --set-default-version 2
wsl.exe --update

Write-Host "Prerequisites configured successfully. If prompted, please restart your PC." -ForegroundColor Green
""";
    }

    /// <summary>
    /// Generates shell installation commands for a tool inside a Debian/Ubuntu-based WSL distribution.
    /// </summary>
    public static string GetInstallCommandForDistro(string distroName, string toolId)
    {
        return toolId.ToLowerInvariant() switch
        {
            "docker" => "sudo apt-get update && sudo apt-get install -y docker.io && sudo usermod -aG docker $USER",
            "kubectl" => "curl -LO \"https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl\" && sudo install -o root -g root -m 0755 kubectl /usr/local/bin/kubectl && rm kubectl",
            "helm" => "curl https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash",
            "terraform" => "sudo apt-get update && sudo apt-get install -y gnupg software-properties-common && wget -O- https://apt.releases.hashicorp.com/gpg | sudo gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg && echo \"deb [signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] https://apt.releases.hashicorp.com $(lsb_release -cs) main\" | sudo tee /etc/apt/sources.list.d/hashicorp.list && sudo apt-get update && sudo apt-get install -y terraform",
            "git" => "sudo apt-get update && sudo apt-get install -y git",
            "k9s" => "curl -sS https://webi.sh/k9s | sh",
            "gh" => "type -p curl >/dev/null || sudo apt install curl -y; curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | sudo dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg && sudo chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg && echo \"deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main\" | sudo tee /etc/apt/sources.list.d/github-cli.list > /dev/null && sudo apt update && sudo apt install gh -y",
            _ => $"sudo apt-get update && sudo apt-get install -y {toolId}"
        };
    }

    private static async Task<string> ExecuteWslCommandAsync(string arguments, int timeoutMs = 8000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null) return string.Empty;

        using var outMs = new MemoryStream();
        using var errMs = new MemoryStream();

        var outTask = proc.StandardOutput.BaseStream.CopyToAsync(outMs);
        var errTask = proc.StandardError.BaseStream.CopyToAsync(errMs);

        var completed = await Task.WhenAny(Task.WhenAll(outTask, errTask), Task.Delay(timeoutMs));
        if (completed != Task.Delay(timeoutMs))
        {
            await proc.WaitForExitAsync();
            var outBytes = outMs.ToArray();
            return DecodeProcessOutput(outBytes);
        }

        try { proc.Kill(); } catch { }
        return string.Empty;
    }

    private static string DecodeProcessOutput(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;

        // Auto-detect UTF-16LE vs UTF-8
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[1] == 0x00)
        {
            return Encoding.Unicode.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<string> RunPowerShellAsync(string script, int timeoutMs = 10000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null) return string.Empty;

        var task = proc.StandardOutput.ReadToEndAsync();
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));
        if (completed == task)
        {
            await proc.WaitForExitAsync();
            return await task;
        }

        try { proc.Kill(); } catch { }
        return string.Empty;
    }

    private static string CleanWslString(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var s = input.Replace("\0", "");
        return AnsiRegex.Replace(s, "").Trim();
    }
}
