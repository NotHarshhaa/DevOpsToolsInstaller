using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Corporate IT policy definition used to lock down installer settings across workstations.
/// </summary>
public sealed class EnterprisePolicy
{
    [JsonPropertyName("blockUnsignedInstallers")]
    public bool BlockUnsignedInstallers { get; set; }

    [JsonPropertyName("enforceSha256Verification")]
    public bool EnforceSha256Verification { get; set; }

    [JsonPropertyName("allowedCatalogSources")]
    public List<string> AllowedCatalogSources { get; set; } = new();

    [JsonPropertyName("lockSettings")]
    public bool LockSettings { get; set; }

    [JsonPropertyName("disableCustomTools")]
    public bool DisableCustomTools { get; set; }

    [JsonPropertyName("disableExternalDownloads")]
    public bool DisableExternalDownloads { get; set; }

    [JsonPropertyName("allowedBackends")]
    public List<string> AllowedBackends { get; set; } = new() { "direct", "winget", "scoop", "choco" };

    [JsonPropertyName("enforcedFeeds")]
    public List<CatalogFeed> EnforcedFeeds { get; set; } = new();

    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = "Your Organization";
}

/// <summary>
/// Manages enterprise policy discovery, enforcement, and IT deployment script exports.
/// Reads from HKLM Group Policy Registry, %ProgramData%, or local policy files.
/// </summary>
public static class EnterprisePolicyService
{
    private const string PolicyRegistryKey = @"SOFTWARE\Policies\DevOpsToolsInstaller";

    private static readonly string MachinePolicyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "DevOpsToolsInstaller", "policy.json");

    private static readonly string UserPolicyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "policy.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static EnterprisePolicy? _cachedPolicy;
    private static readonly object Gate = new();

    public static EnterprisePolicy Current
    {
        get
        {
            if (_cachedPolicy is not null) return _cachedPolicy;
            lock (Gate)
            {
                _cachedPolicy ??= LoadPolicy();
                return _cachedPolicy;
            }
        }
    }

    public static void InvalidateCache()
    {
        lock (Gate)
        {
            _cachedPolicy = null;
        }
    }

    private static EnterprisePolicy LoadPolicy()
    {
        // 1. Try Group Policy Registry (HKLM)
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyRegistryKey);
            if (key is not null)
            {
                var policy = new EnterprisePolicy
                {
                    BlockUnsignedInstallers = (int?)key.GetValue("BlockUnsignedInstallers", 0) == 1,
                    EnforceSha256Verification = (int?)key.GetValue("EnforceSha256Verification", 0) == 1,
                    LockSettings = (int?)key.GetValue("LockSettings", 0) == 1,
                    DisableCustomTools = (int?)key.GetValue("DisableCustomTools", 0) == 1,
                    DisableExternalDownloads = (int?)key.GetValue("DisableExternalDownloads", 0) == 1,
                    OrganizationName = (string?)key.GetValue("OrganizationName") ?? "Corporate IT"
                };

                if (key.GetValue("AllowedCatalogSources") is string[] sources)
                {
                    policy.AllowedCatalogSources.AddRange(sources);
                }

                if (key.GetValue("AllowedBackends") is string[] backends)
                {
                    policy.AllowedBackends = new List<string>(backends);
                }

                return policy;
            }
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("Policy", $"Registry policy check skipped: {ex.Message}");
        }

        // 2. Try Machine-wide JSON policy (%ProgramData%\DevOpsToolsInstaller\policy.json)
        if (File.Exists(MachinePolicyPath))
        {
            try
            {
                var json = File.ReadAllText(MachinePolicyPath);
                var pol = JsonSerializer.Deserialize<EnterprisePolicy>(json, JsonOptions);
                if (pol is not null) return pol;
            }
            catch (Exception ex)
            {
                ActivityLogService.Warn("Policy", $"Machine policy file error: {ex.Message}");
            }
        }

        // 3. Try User-level JSON policy (%LOCALAPPDATA%\DevOpsToolsInstaller\policy.json)
        if (File.Exists(UserPolicyPath))
        {
            try
            {
                var json = File.ReadAllText(UserPolicyPath);
                var pol = JsonSerializer.Deserialize<EnterprisePolicy>(json, JsonOptions);
                if (pol is not null) return pol;
            }
            catch (Exception ex)
            {
                ActivityLogService.Warn("Policy", $"User policy file error: {ex.Message}");
            }
        }

        return new EnterprisePolicy();
    }

    public static bool IsPolicyActive =>
        Current.BlockUnsignedInstallers ||
        Current.EnforceSha256Verification ||
        Current.LockSettings ||
        Current.DisableCustomTools ||
        Current.DisableExternalDownloads ||
        Current.AllowedCatalogSources.Count > 0;

    public static bool IsUnsignedBlocked => Current.BlockUnsignedInstallers;

    public static bool IsShaVerificationEnforced => Current.EnforceSha256Verification;

    public static bool AreSettingsLocked => Current.LockSettings;

    public static bool AreCustomToolsDisabled => Current.DisableCustomTools;

    public static bool IsBackendAllowed(ToolBackend backend)
    {
        if (Current.AllowedBackends.Count == 0) return true;
        var name = backend.ShortName().ToLowerInvariant();
        return Current.AllowedBackends.Any(b => b.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsUrlAllowed(string url)
    {
        if (Current.AllowedCatalogSources.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(url)) return false;

        foreach (var pattern in Current.AllowedCatalogSources)
        {
            if (MatchesWildcard(url, pattern)) return true;
        }

        return false;
    }

    private static bool MatchesWildcard(string input, string pattern)
    {
        if (string.Equals(pattern, "*", StringComparison.OrdinalIgnoreCase)) return true;
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static List<CatalogFeed> GetEnforcedFeeds() => Current.EnforcedFeeds ?? new List<CatalogFeed>();

    /// <summary>
    /// Generates a ready-to-deploy Microsoft Intune Win32 deployment script (PowerShell).
    /// </summary>
    public static string GenerateIntuneScript(ToolBundle? bundle = null, List<string>? toolIds = null)
    {
        var targetTools = toolIds ?? (bundle?.Tools ?? new List<string> { "kubectl", "terraform", "helm" });
        var targetName = bundle?.Name ?? "Corporate DevOps Toolset";

        return $$"""
<#
.SYNOPSIS
    Microsoft Intune Win32 Application Deployment Script for DevOps Tools Installer.
    Target Toolset: {{targetName}}
    Generated by: DevOps Tools Installer (Enterprise IT Export)
#>
[CmdletBinding()]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$ToolIds = @({{string.Join(", ", targetTools.Select(t => $"\"{t}\""))}})

# Locate installed CLI or packaged binary
$CliPath = "$env:LOCALAPPDATA\Programs\DevOpsToolsInstaller\DevOpsToolsInstaller.exe"
if (-not (Test-Path $CliPath)) {
    $CliPath = (Get-Command DevOpsToolsInstaller.exe -ErrorAction SilentlyContinue)?.Source
}
if (-not $CliPath -or -not (Test-Path $CliPath)) {
    $CliPath = Join-Path $PSScriptRoot "DevOpsToolsInstaller.exe"
}

if (-not (Test-Path $CliPath)) {
    Write-Error "DevOpsToolsInstaller.exe not found. Deploy the application first or package it alongside this script."
    exit 1
}

if ($Uninstall) {
    Write-Host "[Intune] Uninstalling tools: $($ToolIds -join ', ')..."
    & $CliPath --uninstall ($ToolIds -join ',')
    exit $LASTEXITCODE
}

Write-Host "[Intune] Deploying tools for '{{targetName}}': $($ToolIds -join ', ')..."
& $CliPath --install ($ToolIds -join ',')

if ($LASTEXITCODE -eq 0) {
    Write-Host "[Intune] Successfully installed all requested tools."
    exit 0
} else {
    Write-Error "[Intune] Tool deployment completed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}
""";
    }

    /// <summary>
    /// Generates a Group Policy / Registry (.reg) export script to lock settings on company machines.
    /// </summary>
    public static string GenerateRegistryPolicyScript(EnterprisePolicy policy)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\DevOpsToolsInstaller]");
        sb.AppendLine($"\"OrganizationName\"=\"{policy.OrganizationName}\"");
        sb.AppendLine($"\"BlockUnsignedInstallers\"=dword:{(policy.BlockUnsignedInstallers ? 1 : 0):D8}");
        sb.AppendLine($"\"EnforceSha256Verification\"=dword:{(policy.EnforceSha256Verification ? 1 : 0):D8}");
        sb.AppendLine($"\"LockSettings\"=dword:{(policy.LockSettings ? 1 : 0):D8}");
        sb.AppendLine($"\"DisableCustomTools\"=dword:{(policy.DisableCustomTools ? 1 : 0):D8}");
        sb.AppendLine($"\"DisableExternalDownloads\"=dword:{(policy.DisableExternalDownloads ? 1 : 0):D8}");

        if (policy.AllowedCatalogSources.Count > 0)
        {
            var sources = string.Join("\",\"", policy.AllowedCatalogSources);
            sb.AppendLine($"\"AllowedCatalogSources\"=hex(7):{StringToMultiSzHex(policy.AllowedCatalogSources)}");
        }

        return sb.ToString();
    }

    private static string StringToMultiSzHex(List<string> strings)
    {
        var bytes = new List<byte>();
        foreach (var s in strings)
        {
            bytes.AddRange(System.Text.Encoding.Unicode.GetBytes(s));
            bytes.Add(0);
            bytes.Add(0);
        }
        bytes.Add(0);
        bytes.Add(0);
        return string.Join(",", bytes.Select(b => $"{b:x2}"));
    }

    public static void SavePolicy(EnterprisePolicy policy, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(policy, JsonOptions));
        InvalidateCache();
    }
}
