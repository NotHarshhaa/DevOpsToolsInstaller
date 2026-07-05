# build.ps1 - DevOps Tools Installer (WinUI 3 + .NET 8)
#
# Usage:
#   .\build.ps1                    - Release build (folder output)
#   .\build.ps1 -Configuration Debug
#   .\build.ps1 -SingleFile        - Produces a merged .exe
#   .\build.ps1 -Run               - Build then launch
#   .\build.ps1 -Clean             - Delete all build artifacts

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SingleFile,
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DevOpsToolsInstaller\DevOpsToolsInstaller.csproj'
$pubDir  = Join-Path $root "src\DevOpsToolsInstaller\bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"
$exe     = Join-Path $pubDir 'DevOpsToolsInstaller.exe'

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "  >> $msg" -ForegroundColor Cyan
}

function Write-Ok([string]$msg)   { Write-Host "  [OK]  $msg" -ForegroundColor Green }
function Write-Err([string]$msg)  { Write-Host "  [ERR] $msg" -ForegroundColor Red }
function Write-Warn([string]$msg) { Write-Host "  [!]   $msg" -ForegroundColor Yellow }

function Find-Dotnet {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { return 'dotnet' }
    $candidates = @(
        'C:\Program Files\dotnet\dotnet.exe',
        'C:\Program Files (x86)\dotnet\dotnet.exe',
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $env:PATH = ([IO.Path]::GetDirectoryName($c)) + ';' + $env:PATH
            return $c
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
# Clean
# ---------------------------------------------------------------------------

if ($Clean) {
    Write-Step "Cleaning build artifacts..."
    foreach ($d in @('src\DevOpsToolsInstaller\bin', 'src\DevOpsToolsInstaller\obj')) {
        $full = Join-Path $root $d
        if (Test-Path $full) {
            Remove-Item -Path $full -Recurse -Force
            Write-Ok "Removed $full"
        }
    }
    Write-Host ""
    Write-Host "  Clean complete." -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "+------------------------------------------------------------------+" -ForegroundColor Cyan
Write-Host "|  DevOps Tools Installer - Build Script (WinUI 3 / .NET 8)       |" -ForegroundColor Cyan
Write-Host "+------------------------------------------------------------------+" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Configuration : $Configuration" -ForegroundColor White
Write-Host "  Single File   : $SingleFile"    -ForegroundColor White
Write-Host "  Project       : $project"        -ForegroundColor Gray
Write-Host ""

# ---------------------------------------------------------------------------
# Prerequisites
# ---------------------------------------------------------------------------

Write-Step "Checking prerequisites..."

$dotnet = Find-Dotnet
if (-not $dotnet) {
    Write-Err ".NET SDK not found."
    Write-Host "  Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    exit 1
}

$dotnetVersion = (& $dotnet --version 2>&1) | Out-String
$dotnetVersion = $dotnetVersion.Trim()
Write-Ok ".NET SDK $dotnetVersion"

$major = [int]($dotnetVersion -split '\.')[0]
if ($major -lt 8) {
    Write-Err ".NET 8.0 or higher required (found $dotnetVersion)"
    exit 1
}

if (-not (Test-Path $project)) {
    Write-Err "Project not found: $project"
    exit 1
}
Write-Ok "Project file found"

$catalogPath = Join-Path $root 'catalog\catalog.json'
if (Test-Path $catalogPath) {
    Write-Ok "catalog.json found"
} else {
    Write-Warn "catalog.json not found at $catalogPath"
}

Write-Ok "Prerequisites passed"

# ---------------------------------------------------------------------------
# Restore
# ---------------------------------------------------------------------------

Write-Step "Restoring NuGet packages..."
$restoreOut = & $dotnet restore $project -r win-x64 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "Restore failed:"
    $restoreOut | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
}
Write-Ok "Packages restored"

# ---------------------------------------------------------------------------
# Publish
# ---------------------------------------------------------------------------

Write-Step "Publishing ($Configuration, win-x64, self-contained)..."

$publishArgs = [System.Collections.Generic.List[string]]@(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '--no-restore',
    '-p:WindowsAppSDKSelfContained=true',
    '-p:Platform=x64'
)

if ($SingleFile) {
    $publishArgs.Add('-p:PublishSingleFile=true')
    $publishArgs.Add('-p:IncludeNativeLibrariesForSelfExtract=true')
    Write-Warn "Single-file: Windows App SDK runtime may still extract DLLs on first run"
}

$buildOut = & $dotnet @publishArgs 2>&1
$buildOut | ForEach-Object {
    $line = "$_"
    if ($line -match ' error ') {
        Write-Host "    $line" -ForegroundColor Red
    } elseif ($line -match ' warning ') {
        Write-Host "    $line" -ForegroundColor Yellow
    }
}

if ($LASTEXITCODE -ne 0) {
    Write-Err "Publish failed - see output above."
    exit 1
}

# ---------------------------------------------------------------------------
# Verify output
# ---------------------------------------------------------------------------

Write-Step "Verifying output..."

if (-not (Test-Path $exe)) {
    Write-Err "Expected exe not found: $exe"
    if (Test-Path $pubDir) {
        Write-Warn "Publish dir contents:"
        Get-ChildItem $pubDir | ForEach-Object { Write-Host "    $($_.Name)" -ForegroundColor Gray }
    }
    exit 1
}

$exeSizeBytes = (Get-Item $exe).Length
$exeSizeMB    = [math]::Round($exeSizeBytes / 1MB, 1)
Write-Ok "DevOpsToolsInstaller.exe ($exeSizeMB MB)"

$copiedCatalog = Join-Path $pubDir 'Assets\catalog.json'
if (Test-Path $copiedCatalog) {
    Write-Ok "catalog.json embedded in output"
} else {
    Write-Warn "catalog.json not in output - app will fetch from GitHub at runtime"
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "+------------------------------------------------------------------+" -ForegroundColor Green
Write-Host "|                    BUILD SUCCEEDED!                              |" -ForegroundColor Green
Write-Host "+------------------------------------------------------------------+" -ForegroundColor Green
Write-Host ""
Write-Host "  Exe  : $exe" -ForegroundColor White
Write-Host "  Size : $exeSizeMB MB" -ForegroundColor Gray
Write-Host ""
Write-Host "  Run:" -ForegroundColor Yellow
Write-Host "    $exe" -ForegroundColor White
Write-Host ""

# ---------------------------------------------------------------------------
# Auto-launch
# ---------------------------------------------------------------------------

if ($Run) {
    Write-Step "Launching..."
    try {
        Start-Process -FilePath $exe
        Write-Ok "Launched - check your taskbar"
    } catch {
        Write-Err "Failed to launch: $_"
    }
}
