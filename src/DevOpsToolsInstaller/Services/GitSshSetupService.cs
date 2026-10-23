using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevOpsToolsInstaller.Services;

public sealed record GitConfigSettings(
    string? UserName,
    string? UserEmail,
    string? DefaultBranch,
    string? AutoCrlf,
    string? PullRebase,
    string? CredentialHelper,
    string? Editor,
    bool GpgSign);

public sealed record SshKeyItem(
    string Name,
    string Algorithm,
    string PublicKeyPath,
    string PrivateKeyPath,
    string? Fingerprint,
    string? Comment,
    DateTime CreatedUtc)
{
    public string DisplayTitle => $"{Name} ({Algorithm})";
    public string FingerprintShort => string.IsNullOrWhiteSpace(Fingerprint) ? "—" : Fingerprint;
}

public sealed record SshKeyGenResult(
    bool Success,
    string KeyPath,
    string PublicKeyPath,
    string PublicKeyContent,
    string Message);

/// <summary>
/// Service for Git workstation configuration, SSH key generation,
/// public key retrieval, and Windows OpenSSH Agent setup.
/// </summary>
public static class GitSshSetupService
{
    private static readonly string SshDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    /// <summary>
    /// Reads user's global Git configuration.
    /// </summary>
    public static async Task<GitConfigSettings> GetGitConfigAsync()
    {
        string? name = await RunGitConfigAsync("user.name");
        string? email = await RunGitConfigAsync("user.email");
        string? branch = await RunGitConfigAsync("init.defaultBranch");
        string? autocrlf = await RunGitConfigAsync("core.autocrlf");
        string? rebase = await RunGitConfigAsync("pull.rebase");
        string? helper = await RunGitConfigAsync("credential.helper");
        string? editor = await RunGitConfigAsync("core.editor");
        string? gpg = await RunGitConfigAsync("commit.gpgsign");

        return new GitConfigSettings(
            string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim(),
            string.IsNullOrWhiteSpace(autocrlf) ? "input" : autocrlf.Trim(),
            string.IsNullOrWhiteSpace(rebase) ? "false" : rebase.Trim(),
            string.IsNullOrWhiteSpace(helper) ? "manager" : helper.Trim(),
            string.IsNullOrWhiteSpace(editor) ? "code --wait" : editor.Trim(),
            string.Equals(gpg?.Trim(), "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Updates global Git configuration values.
    /// </summary>
    public static async Task<bool> SaveGitConfigAsync(GitConfigSettings config)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(config.UserName))
                await RunGitSetAsync("user.name", config.UserName.Trim());

            if (!string.IsNullOrWhiteSpace(config.UserEmail))
                await RunGitSetAsync("user.email", config.UserEmail.Trim());

            if (!string.IsNullOrWhiteSpace(config.DefaultBranch))
                await RunGitSetAsync("init.defaultBranch", config.DefaultBranch.Trim());

            if (!string.IsNullOrWhiteSpace(config.AutoCrlf))
                await RunGitSetAsync("core.autocrlf", config.AutoCrlf.Trim());

            if (!string.IsNullOrWhiteSpace(config.PullRebase))
                await RunGitSetAsync("pull.rebase", config.PullRebase.Trim());

            if (!string.IsNullOrWhiteSpace(config.CredentialHelper))
                await RunGitSetAsync("credential.helper", config.CredentialHelper.Trim());

            if (!string.IsNullOrWhiteSpace(config.Editor))
                await RunGitSetAsync("core.editor", config.Editor.Trim());

            ActivityLogService.Info("Git", "Global Git configuration updated successfully.");
            return true;
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Git", $"Error saving Git config: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Lists all SSH key pairs found in the user's ~/.ssh folder.
    /// </summary>
    public static List<SshKeyItem> GetExistingSshKeys()
    {
        var list = new List<SshKeyItem>();
        if (!Directory.Exists(SshDirectory)) return list;

        try
        {
            var pubFiles = Directory.GetFiles(SshDirectory, "*.pub");
            foreach (var pub in pubFiles)
            {
                var priv = pub.Substring(0, pub.Length - 4); // strip .pub
                if (!File.Exists(priv)) continue;

                var name = Path.GetFileName(priv);
                var content = File.ReadAllText(pub).Trim();
                var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                var algo = parts.Length > 0 ? parts[0] : "ssh-key";
                var comment = parts.Length > 2 ? parts[2] : null;

                // Run ssh-keygen -lf to get fingerprint
                var fingerprint = GetKeyFingerprint(pub);
                var created = File.GetCreationTimeUtc(pub);

                list.Add(new SshKeyItem(name, algo, pub, priv, fingerprint, comment, created));
            }
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("SSH", $"Error scanning SSH keys: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// Generates a new SSH key pair (Ed25519 or RSA) using ssh-keygen.exe.
    /// </summary>
    public static async Task<SshKeyGenResult> GenerateSshKeyAsync(
        string algorithm = "ed25519",
        string? emailComment = null,
        string? keyName = null,
        string? passphrase = null)
    {
        Directory.CreateDirectory(SshDirectory);

        var safeAlgo = algorithm.ToLowerInvariant() switch
        {
            "rsa" => "rsa",
            _ => "ed25519"
        };

        var defaultName = safeAlgo == "rsa" ? "id_rsa" : "id_ed25519";
        var finalName = string.IsNullOrWhiteSpace(keyName) ? defaultName : keyName.Trim();
        var privPath = Path.Combine(SshDirectory, finalName);
        var pubPath = privPath + ".pub";

        if (File.Exists(privPath))
        {
            return new SshKeyGenResult(false, privPath, pubPath, "", $"Key file '{finalName}' already exists in ~/.ssh.");
        }

        var comment = string.IsNullOrWhiteSpace(emailComment) ? "devops-workstation" : emailComment.Trim();
        var passArg = string.IsNullOrEmpty(passphrase) ? "-N \"\"" : $"-N \"{passphrase}\"";
        var bitsArg = safeAlgo == "rsa" ? "-b 4096" : "";

        var args = $"-t {safeAlgo} {bitsArg} -C \"{comment}\" -f \"{privPath}\" {passArg}";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh-keygen.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return new SshKeyGenResult(false, privPath, pubPath, "", "Failed to start ssh-keygen.exe");

            await proc.WaitForExitAsync();
            if (proc.ExitCode == 0 && File.Exists(pubPath))
            {
                var pubContent = await File.ReadAllTextAsync(pubPath);
                ActivityLogService.Info("SSH", $"Generated new {safeAlgo.ToUpperInvariant()} SSH key at {privPath}");
                return new SshKeyGenResult(true, privPath, pubPath, pubContent.Trim(), $"Successfully created {safeAlgo.ToUpperInvariant()} key ({finalName}).");
            }
            else
            {
                var err = await proc.StandardError.ReadToEndAsync();
                return new SshKeyGenResult(false, privPath, pubPath, "", $"ssh-keygen failed: {err.Trim()}");
            }
        }
        catch (Exception ex)
        {
            return new SshKeyGenResult(false, privPath, pubPath, "", $"Error generating key: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads public key content ready for copying to GitHub, GitLab, etc.
    /// </summary>
    public static string? ReadPublicKey(string pubPath)
    {
        try
        {
            if (File.Exists(pubPath))
            {
                return File.ReadAllText(pubPath).Trim();
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Checks whether Windows OpenSSH Authentication Agent (ssh-agent service) is running.
    /// </summary>
    public static async Task<bool> IsSshAgentRunningAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"(Get-Service ssh-agent -ErrorAction SilentlyContinue).Status\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            var output = await proc.StandardOutput.ReadToEndAsync();
            return output.Contains("Running", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Script to enable and start Windows OpenSSH Authentication Agent.
    /// </summary>
    public static string GetEnableSshAgentScript()
    {
        return """
# Elevated script to enable and start Windows ssh-agent service
Set-Service -Name ssh-agent -StartupType Automatic
Start-Service -Name ssh-agent
Get-Service -Name ssh-agent
Write-Host "Windows OpenSSH Agent is now enabled and running." -ForegroundColor Green
""";
    }

    private static string? GetKeyFingerprint(string pubPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh-keygen.exe",
                Arguments = $"-lf \"{pubPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var outText = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(outText))
            {
                var parts = outText.Trim().Split(' ');
                if (parts.Length >= 2) return parts[1]; // SHA256:...
            }
        }
        catch { }
        return null;
    }

    private static async Task<string?> RunGitConfigAsync(string key)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git.exe",
                Arguments = $"config --global --get {key}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var text = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? text.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task RunGitSetAsync(string key, string val)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git.exe",
            Arguments = $"config --global {key} \"{val}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc != null) await proc.WaitForExitAsync();
    }
}
