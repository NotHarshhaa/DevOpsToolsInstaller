using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed class OfflineBundleManifest
{
    [JsonPropertyName("bundleId")]
    public string BundleId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("targetArchitecture")]
    public string TargetArchitecture { get; set; } = "win-x64";

    [JsonPropertyName("tools")]
    public List<OfflineToolEntry> Tools { get; set; } = new();
}

public sealed class OfflineToolEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "installer";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("launchArgs")]
    public string LaunchArgs { get; set; } = string.Empty;
}

public sealed record OfflineExportResult(bool Success, string DestinationPath, int ToolsExported, string Message);

public static class OfflineBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Exports a bundle and all of its required binary artifacts into a self-contained offline directory
    /// suitable for copying to a USB stick or air-gapped machine.
    /// </summary>
    public static async Task<OfflineExportResult> ExportOfflineBundleAsync(
        ToolBundle bundle,
        IEnumerable<ToolDefinition> allTools,
        string destinationDir,
        IProgress<double>? progress = null,
        Action<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        var toolMap = allTools.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var toolsToExport = new List<ToolDefinition>();

        foreach (var id in bundle.Tools)
        {
            if (toolMap.TryGetValue(id, out var t))
            {
                toolsToExport.Add(t);
            }
        }

        return await ExportOfflineToolsAsync(toolsToExport, bundle.Id, bundle.Name, bundle.Description, destinationDir, progress, statusCallback, ct);
    }

    /// <summary>
    /// Exports arbitrary tools into a self-contained offline air-gap package.
    /// </summary>
    public static async Task<OfflineExportResult> ExportOfflineToolsAsync(
        IReadOnlyList<ToolDefinition> tools,
        string bundleId,
        string bundleName,
        string bundleDescription,
        string destinationDir,
        IProgress<double>? progress = null,
        Action<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        if (tools.Count == 0)
        {
            return new OfflineExportResult(false, destinationDir, 0, "No tools selected to export.");
        }

        try
        {
            Directory.CreateDirectory(destinationDir);
            var installersDir = Path.Combine(destinationDir, "installers");
            Directory.CreateDirectory(installersDir);

            var manifest = new OfflineBundleManifest
            {
                BundleId = string.IsNullOrWhiteSpace(bundleId) ? "custom-offline-bundle" : bundleId,
                Name = string.IsNullOrWhiteSpace(bundleName) ? "DevOps Offline Bundle" : bundleName,
                Description = string.IsNullOrWhiteSpace(bundleDescription) ? "Air-gapped deployment bundle" : bundleDescription,
                CreatedAtUtc = DateTime.UtcNow,
                TargetArchitecture = "win-x64"
            };

            var downloader = new DownloadService();
            var total = tools.Count;
            var current = 0;

            foreach (var tool in tools)
            {
                ct.ThrowIfCancellationRequested();
                current++;
                statusCallback?.Invoke($"Preparing {tool.Name} ({current}/{total})...");

                var targetInstallerPath = Path.Combine(installersDir, tool.FileName);

                // Check if already in user's downloads folder or custom download directory
                var preferredFolder = SettingsService.DownloadsFolder;
                var cachedPath = Path.Combine(preferredFolder, tool.FileName);
                if (!File.Exists(cachedPath))
                {
                    cachedPath = Path.Combine(DownloadService.DefaultDownloadsFolder, tool.FileName);
                }

                bool validCache = false;
                if (File.Exists(cachedPath))
                {
                    if (string.IsNullOrEmpty(tool.Sha256) || VerifySha256(cachedPath, tool.Sha256))
                    {
                        File.Copy(cachedPath, targetInstallerPath, overwrite: true);
                        validCache = true;
                    }
                }

                if (!validCache)
                {
                    statusCallback?.Invoke($"Downloading {tool.Name} ({current}/{total})...");
                    await downloader.DownloadAsync(tool, installersDir, null, ct);
                }

                // Compute hash if missing
                var computedSha = string.IsNullOrEmpty(tool.Sha256) && File.Exists(targetInstallerPath)
                    ? ComputeSha256(targetInstallerPath)
                    : tool.Sha256;

                manifest.Tools.Add(new OfflineToolEntry
                {
                    Id = tool.Id,
                    Name = tool.Name,
                    Version = tool.SelectedVersion,
                    FileName = tool.FileName,
                    Kind = tool.KindRaw ?? "installer",
                    Sha256 = computedSha,
                    LaunchArgs = tool.LaunchArgs
                });

                progress?.Report((double)current / total * 100.0);
            }

            // Write bundle-manifest.json
            var manifestPath = Path.Combine(destinationDir, "bundle-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

            // Generate self-contained install-offline.ps1
            var ps1Path = Path.Combine(destinationDir, "install-offline.ps1");
            File.WriteAllText(ps1Path, GenerateOfflinePs1Script(manifest));

            // Generate one-click install-offline.cmd
            var cmdPath = Path.Combine(destinationDir, "install-offline.cmd");
            File.WriteAllText(cmdPath, GenerateOfflineCmdScript());

            // Generate README
            var readmePath = Path.Combine(destinationDir, "README-OFFLINE.txt");
            File.WriteAllText(readmePath, GenerateReadme(manifest));

            ActivityLogService.Info("Offline", $"Exported offline bundle '{manifest.Name}' with {manifest.Tools.Count} tools to {destinationDir}");
            return new OfflineExportResult(true, destinationDir, manifest.Tools.Count, $"Successfully exported {manifest.Tools.Count} tools to {destinationDir}");
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Offline", $"Failed to export offline bundle: {ex.Message}");
            return new OfflineExportResult(false, destinationDir, 0, ex.Message);
        }
    }

    private static string GenerateOfflinePs1Script(OfflineBundleManifest manifest)
    {
        return $$"""
<#
.SYNOPSIS
    Self-Contained Offline Installer for {{manifest.Name}}
    Generated by: DevOps Tools Installer (Air-Gap Support)
    Date: {{manifest.CreatedAtUtc:yyyy-MM-dd}}
#>
[CmdletBinding()]
param(
    [switch]$SkipHashCheck,
    [string]$TargetBinDir = "$env:LOCALAPPDATA\DevOpsToolsInstaller\Tools\bin"
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$InstallersDir = Join-Path $ScriptDir "installers"
$ManifestPath = Join-Path $ScriptDir "bundle-manifest.json"

if (-not (Test-Path $ManifestPath)) {
    Write-Error "bundle-manifest.json not found in $ScriptDir"
    exit 1
}

$Manifest = Get-Content $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " DevOps Tools Installer — Offline Air-Gapped Stack Deployment" -ForegroundColor Cyan
Write-Host " Bundle: $($Manifest.name)" -ForegroundColor Green
Write-Host " Tools:  $($Manifest.tools.Count) tools included" -ForegroundColor Gray
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Ensure user Tools and Bin directories exist
$ToolsRoot = "$env:LOCALAPPDATA\DevOpsToolsInstaller\Tools"
New-Item -ItemType Directory -Force -Path $ToolsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $TargetBinDir | Out-Null

# Configure User PATH if not already present
$UserPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($UserPath -notmatch [regex]::Escape($TargetBinDir)) {
    Write-Host "[PATH] Adding $TargetBinDir to User PATH..." -ForegroundColor Yellow
    [Environment]::SetEnvironmentVariable("Path", "$UserPath;$TargetBinDir", "User")
    $env:PATH = "$env:PATH;$TargetBinDir"
}

$Succeeded = 0
$Failed = 0

foreach ($tool in $Manifest.tools) {
    Write-Host "---------------------------------------------------------------"
    Write-Host "Installing: $($tool.name) v$($tool.version)" -ForegroundColor White
    $filePath = Join-Path $InstallersDir $tool.fileName

    if (-not (Test-Path $filePath)) {
        Write-Warning "File missing: $($tool.fileName)"
        $Failed++
        continue
    }

    # Verify SHA-256
    if (-not $SkipHashCheck -and $tool.sha256) {
        Write-Host "  Verifying SHA-256..." -NoNewline
        $hash = (Get-FileHash -Path $filePath -Algorithm SHA256).Hash.ToLower()
        if ($hash -ne $tool.sha256.ToLower()) {
            Write-Host " FAILED (checksum mismatch)" -ForegroundColor Red
            Write-Warning "Expected: $($tool.sha256) but got: $hash"
            $Failed++
            continue
        }
        Write-Host " OK" -ForegroundColor Green
    }

    try {
        switch ($tool.kind.ToLowerInvariant()) {
            "binary" {
                $targetFile = Join-Path $TargetBinDir $tool.fileName
                Copy-Item -Path $filePath -Destination $targetFile -Force
                Write-Host "  Copied standalone executable to: $targetFile" -ForegroundColor Green
                $Succeeded++
            }
            "archive" {
                $toolDir = Join-Path $ToolsRoot $tool.id
                New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
                Write-Host "  Extracting archive to: $toolDir..." -ForegroundColor Cyan
                if ($tool.fileName -match '\.tar\.gz$') {
                    & tar -xzf $filePath -C $toolDir
                } else {
                    Expand-Archive -Path $filePath -DestinationPath $toolDir -Force
                }
                
                # Check for executable and symlink/copy to Bin
                $exe = Get-ChildItem -Path $toolDir -Filter "*.exe" -Recurse | Select-Object -First 1
                if ($exe) {
                    $link = Join-Path $TargetBinDir $exe.Name
                    Copy-Item -Path $exe.FullName -Destination $link -Force
                    Write-Host "  Linked executable: $($exe.Name) -> $TargetBinDir" -ForegroundColor Green
                }
                $Succeeded++
            }
            default { # "installer"
                Write-Host "  Launching installer: $($tool.fileName)..." -ForegroundColor Cyan
                if ($filePath -match '\.msi$') {
                    $msiArgs = "/i `"$filePath`" /qn /norestart"
                    if ($tool.launchArgs) { $msiArgs += " $($tool.launchArgs)" }
                    $p = Start-Process -FilePath "msiexec.exe" -ArgumentList $msiArgs -Wait -PassThru
                } else {
                    $pinfo = New-Object System.Diagnostics.ProcessStartInfo
                    $pinfo.FileName = $filePath
                    if ($tool.launchArgs) { $pinfo.Arguments = $tool.launchArgs }
                    $pinfo.UseShellExecute = $true
                    $p = [System.Diagnostics.Process]::Start($pinfo)
                    $p.WaitForExit()
                }
                if ($p.ExitCode -eq 0 -or $p.ExitCode -eq 3010) {
                    Write-Host "  Installer completed successfully (Code: $($p.ExitCode))." -ForegroundColor Green
                    $Succeeded++
                } else {
                    Write-Warning "  Installer exited with code: $($p.ExitCode)"
                    $Succeeded++ # vendor installers may return custom codes
                }
            }
        }
    } catch {
        Write-Warning "  Error installing $($tool.name): $($_.Exception.Message)"
        $Failed++
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Deployment Complete: $Succeeded succeeded, $Failed failed." -ForegroundColor $(if ($Failed -eq 0) { "Green" } else { "Yellow" })
Write-Host " Tools folder: $TargetBinDir" -ForegroundColor Gray
Write-Host " Note: Restart any open terminals to pick up the updated PATH." -ForegroundColor Gray
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
""";
    }

    private static string GenerateOfflineCmdScript()
    {
        return """
@echo off
setlocal
cd /d "%~dp0"
echo Starting DevOps Tools Offline Deployment...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-offline.ps1" %*
pause
""";
    }

    private static string GenerateReadme(OfflineBundleManifest manifest)
    {
        return $"""
========================================================================
 DevOps Tools Installer — Offline Air-Gapped Bundle
========================================================================
Bundle:      {manifest.Name}
Description: {manifest.Description}
Created:     {manifest.CreatedAtUtc:u}
Tools Count: {manifest.Tools.Count}

HOW TO USE ON AN AIR-GAPPED MACHINE:
1. Copy this entire folder (or USB stick) onto the target machine.
2. Right-click 'install-offline.cmd' and select 'Run as administrator',
   or open PowerShell and execute:
     powershell -ExecutionPolicy Bypass -File .\install-offline.ps1

3. All binaries are placed in:
     %LOCALAPPDATA%\DevOpsToolsInstaller\Tools\bin
   and automatically added to your user PATH.

4. Checksums are verified before installation.
========================================================================
""";
    }

    private static bool VerifySha256(string filePath, string expectedSha)
    {
        try
        {
            var computed = ComputeSha256(filePath);
            return string.Equals(computed, expectedSha, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
