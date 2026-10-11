param(
    [string]$Tag = "v3.0.1",
    [string]$AssetsDir = "release_assets",
    [string]$OutputFile = "release_notes.md"
)

$setupHash = "N/A"
$msiHash = "N/A"
$x64Hash = "N/A"
$arm64Hash = "N/A"

$setupFile = Get-ChildItem -Path "$AssetsDir" -Filter "*Setup*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($setupFile) {
    $setupHash = (Get-FileHash -Path $setupFile.FullName -Algorithm SHA256).Hash.ToLower()
}
$msiFile = Get-ChildItem -Path "$AssetsDir" -Filter "*x64*.msi" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($msiFile) {
    $msiHash = (Get-FileHash -Path $msiFile.FullName -Algorithm SHA256).Hash.ToLower()
}
$x64File = Get-ChildItem -Path "$AssetsDir" -Filter "*x64*.exe" -Exclude "*Setup*" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($x64File) {
    $x64Hash = (Get-FileHash -Path $x64File.FullName -Algorithm SHA256).Hash.ToLower()
}
$arm64File = Get-ChildItem -Path "$AssetsDir" -Filter "*arm64*.exe" -Exclude "*Setup*" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($arm64File) {
    $arm64Hash = (Get-FileHash -Path $arm64File.FullName -Algorithm SHA256).Hash.ToLower()
}

if (Test-Path $AssetsDir) {
    # Generate SHA256SUMS.txt
    Get-ChildItem -Path "$AssetsDir\*.exe", "$AssetsDir\*.msi" -ErrorAction SilentlyContinue | ForEach-Object {
        $h = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLower()
        "$h  $($_.Name)"
    } | Out-File -FilePath "$AssetsDir\SHA256SUMS.txt" -Encoding utf8
}

$template = @'
# 🚀 DevOps Tools Installer __TAG__ Release

Welcome to the **__TAG__** release of **DevOps Tools Installer**!
This release brings a redesigned **HomePage with modern Hero Banner & quick action navigation**, **comprehensive UI smoothness & lag reduction**, **Catalog Quick Status Filters**, **Download Bandwidth Throttling / Speed Limiter**, **Custom Tool Catalog Feeds**, **Workstation Profile & PowerShell Bootstrap exports from the UI**, upgraded **Windows App SDK 2.5.1 runtime**, and live **telemetry sparklines**.

---

### ✨ What's New & Highlights in __TAG__

#### 🎨 Redesigned HomePage & Modern Hero Banner
- **Isometric DevOps Hero Banner**: Sleek developer workstation visual banner with alpha-blended gradients and dark glassmorphic styling.
- **Quick Action Navigation**: Direct 1-click pills to jump into *Curated Stacks*, *Installed Tools*, and *Download Queue*.
- **Fluent WinUI 3 Polish**: Upgraded across all pages with unified system accent colors and accessible text contrast.

#### ⚡ Performance, Smoothness & Lag Elimination
- **Search & Filter Debouncing**: 150ms debouncing across Catalog, Installed, Downloads, and TitleBar search boxes prevents UI freezes on rapid typing.
- **In-Place Collection Diffing**: Replaced full list reloads with smart in-place mutations, eliminating grid flickers and scroll resets.
- **Zero-Stutter Grid Scrolling**: Removed heavy staggered entrance transitions on catalog cards for 60+ FPS smooth scrolling.
- **Startup Disk I/O Removed**: Pre-cached local SVG/PNG tool logos in-memory and constrained texture decodes (`DecodePixelWidth/Height = 48`) to eliminate UI-thread disk bottlenecks.
- **Chart Telemetry Optimization**: Eliminated CPU interpolation overhead on sparklines with `AnimationsSpeed = TimeSpan.Zero`.
- **Bounded Background Probing**: Throttled CLI version detection to 3 concurrent worker tasks with instant cancellation on page unload.

#### 🎛️ Catalog Status Filters & Batch Presets
- **Quick Status Filter**: Instant filtering by `All`, `Not Installed`, `Installed`, `Updates Available`, and `Favorites Only`.
- **Batch Selection Presets**: Quick select all uninstalled tools or outdated tools with one click.
- **Tool Detail Enhancements**: Copy direct vendor download mirror URLs directly from the detail view.

#### 🌐 Enterprise Network & Bandwidth Limiter
- **Download Speed Throttling**: Configure download bandwidth limits (*Unlimited, 2, 5, 10, 25, 50 MB/s*) under **Settings → Network** with high-precision token rate limiting.
- **Custom / Private Catalog Feeds**: Subscribe to private tool catalogs via HTTPS URL or local JSON files under **Settings → Updates** with automatic fail-closed security.
- **Corporate Proxy Engine**: Full HTTP/HTTPS/SOCKS proxy support with authentication.

#### 🔄 Workstation Migration, Export & Provisioning
- **Export from Installed Page**: One-click flyout to export Markdown Audit Reports, JSON Workstation Profiles, and Standalone PowerShell Bootstrap Scripts.
- **WinGet & Chocolatey Discovery**: Import already-installed packages directly into the catalog.
- **Headless CLI Capabilities**: Unattended installations, batch updates, and profile generation via CLI flags.

#### 🛠️ Upgraded Runtime & Platform
- **Windows App SDK 2.5.1**: Modern desktop framework with Windows 11 TitleBar control, in-app Mica/Acrylic backdrops, and modern toast notifications.

---

### 📋 Full Commit Changelog

__CHANGELOG__

---

### 📦 Verification & Checksums

| Asset | Type | SHA256 Hash |
| :--- | :--- | :--- |
| **`DevOpsToolsInstaller_x64_Setup.exe`** | Windows Setup Wizard | `__SETUP_HASH__` |
| **`DevOpsToolsInstaller_x64.msi`** | Windows Installer (.msi) | `__MSI_HASH__` |
| **`DevOpsToolsInstaller_x64.exe`** | Portable Single Binary | `__X64_HASH__` |
| **`DevOpsToolsInstaller_arm64.exe`** | Portable Single Binary | `__ARM64_HASH__` |

*(All asset checksums are also downloadable in `SHA256SUMS.txt`)*

---

### 🚀 Quick Start
- **Option A (Microsoft Store)**: Install directly from the Microsoft Store with automated background updates:
  ```powershell
  winget install 9PJL0VR3H7VG
  ```
- **Option B (WinGet Community)**:
  ```powershell
  winget install NotHarshhaa.DevOpsToolsInstaller
  ```
- **Option C (Setup Wizard)**: Download and run `DevOpsToolsInstaller_x64_Setup.exe` to install with automated desktop shortcuts and PATH integration.
- **Option D (Windows Installer MSI)**: Download and run `DevOpsToolsInstaller_x64.msi` for enterprise GPO, Intune, or silent rollouts (`msiexec /i DevOpsToolsInstaller_x64.msi /qn`).
- **Option E (Portable)**: Download `DevOpsToolsInstaller_x64.exe` (or `_arm64.exe`) and run directly — zero installation required.

---

### 🖥️ Provision a Workstation from the Terminal
```powershell
# Provision a curated Kubernetes stack in one command
DevOpsToolsInstaller.exe --install-bundle k8s-starter

# Discover and import existing tools from winget or chocolatey
DevOpsToolsInstaller.exe --import-winget

# Generate an instant Markdown workstation health & audit report
DevOpsToolsInstaller.exe --export-report workstation-audit.md

# Generate a portable standalone PowerShell bootstrap script
DevOpsToolsInstaller.exe --generate-bootstrap setup-workstation.ps1

# Clean up or uninstall tools
DevOpsToolsInstaller.exe --uninstall kubectl,terraform
```
'@

# Build the commit changelog dynamically from git history (previous tag → current tag),
# with a graceful fallback when tags are unavailable (shallow CI checkout, first release).
$changelog = ""
try {
    git fetch --tags --quiet 2>$null
    $prevTag = (git tag --sort=-creatordate 2>$null | Where-Object { $_ -ne $Tag } | Select-Object -First 1)
    if ($prevTag) {
        $log = (git log --pretty=format:'- `%h` - **%s**' "$prevTag..$Tag" 2>$null | Out-String).Trim()
        if (-not [string]::IsNullOrWhiteSpace($log)) {
            $changelog = "Changes since ``$prevTag``:"
            $changelog += [Environment]::NewLine + [Environment]::NewLine + $log
        }
    }
} catch { }
if ([string]::IsNullOrWhiteSpace($changelog)) {
    $changelog = "See the [commit history](https://github.com/NotHarshhaa/DevOpsToolsInstaller/commits/$Tag) for the complete changelog."
}

$content = $template.Replace("__TAG__", $Tag).Replace("__CHANGELOG__", $changelog).Replace("__SETUP_HASH__", $setupHash).Replace("__MSI_HASH__", $msiHash).Replace("__X64_HASH__", $x64Hash).Replace("__ARM64_HASH__", $arm64Hash)
$resolvedPath = $null
try {
    $resolvedPath = (Resolve-Path -Path $OutputFile -ErrorAction SilentlyContinue).Path
} catch { }
if ([string]::IsNullOrWhiteSpace($resolvedPath)) {
    $resolvedPath = Join-Path (Get-Location).Path $OutputFile
}
$outputDir = [System.IO.Path]::GetDirectoryName($resolvedPath)
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
[System.IO.File]::WriteAllText($OutputFile, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated release notes at $OutputFile"
