<#
.SYNOPSIS
    Submits or validates the WinGet package manifest for DevOps Tools Installer.

.DESCRIPTION
    Validates the local WinGet manifest files and optionally submits them to the
    official Microsoft Windows Package Manager Community Repository (microsoft/winget-pkgs).

.PARAMETER Submit
    Submits the manifest directly to microsoft/winget-pkgs using wingetcreate and GitHub auth.

.PARAMETER TestInstall
    Tests installing the package locally using the WinGet manifest.

.EXAMPLE
    .\scripts\submit-winget.ps1
    Validates the manifests.

.EXAMPLE
    .\scripts\submit-winget.ps1 -Submit
    Submits the PR to microsoft/winget-pkgs.
#>

param(
    [switch]$Submit,
    [switch]$TestInstall
)

$ErrorActionPreference = 'Stop'

$version = "2.1.0"
$manifestDir = Join-Path $PSScriptRoot "..\installer\winget\manifests\n\NotHarshhaa\DevOpsToolsInstaller\$version"

if (-not (Test-Path $manifestDir)) {
    Write-Error "Manifest directory not found at $manifestDir"
}

Write-Host "==> Validating WinGet manifests for NotHarshhaa.DevOpsToolsInstaller v$version..." -ForegroundColor Cyan
& winget validate --manifest $manifestDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Manifest validation failed!"
}

Write-Host " Manifest validation succeeded!" -ForegroundColor Green

if ($TestInstall) {
    Write-Host "`n==> Testing local installation via WinGet manifest..." -ForegroundColor Cyan
    & winget install --manifest $manifestDir --accept-source-agreements --accept-package-agreements
}

if ($Submit) {
    Write-Host "`n==> Submitting manifest to microsoft/winget-pkgs..." -ForegroundColor Cyan
    $ghToken = & gh auth token 2>$null
    if (-not $ghToken) {
        Write-Warning "Could not find token from gh CLI. You may be prompted for GitHub credentials."
        & wingetcreate submit $manifestDir --prtitle "New package: NotHarshhaa.DevOpsToolsInstaller version $version"
    } else {
        & wingetcreate submit $manifestDir --token $ghToken --prtitle "New package: NotHarshhaa.DevOpsToolsInstaller version $version"
    }
}
