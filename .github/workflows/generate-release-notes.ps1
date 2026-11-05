param(
    [string]$Tag = "v3.1.3",
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
This release introduces a major **Windows 11 Fluent 2 & Trending Modern UI Overhaul** alongside an all-new **Environment & Setup Suite (WSL, PATH, SSH)**, **Multi-Cloud Connections Dashboard**, **SBOM & Compliance Export (CycloneDX & SPDX)**, **Storage & Download Cache Manager**, and **Catalog expansion to 95+ tools**.

---

### ✨ What's New & Highlights in __TAG__

#### 🎨 Windows 11 Fluent 2 & Trending Modern UI Design
- **Harmonious Translucent Pill Badges**: Replaced flat monotone gray tags with modern tinted pill tags (`CornerRadius="10"`, `Padding="7,2"`) featuring soft semi-transparent backdrops, complementary micro-borders, and crisp typography for `Success` (mint/emerald), `Warning` (amber), `Accent` (azure), `Purple` (violet), and `Neutral` (slate) across Dark and Light themes.
- **Pill Category Chips**: Filter chips modernized from rectangular boxes to sleek pill buttons (`CornerRadius="14"`, `Padding="14,6"`).
- **Fluent 2 Card Corner Radii & Elevation**: Standardized 10px–12px corner radii across all card surfaces (`CardStyle`, `ToolCardStyle`, `QuickActionCardStyle`, `FeatureCardStyle`, `StatCardStyle`).
- **Dev Home / Widget Style Metric Dashboard**: Added stylish rounded badge icon containers (`30×30px`, `CornerRadius="8"`) with dedicated background and foreground brushes for Installed Tools, Updates Available, Active Downloads, and Workstation Health.
- **Raycast / Linear-Style Command Palette (`Ctrl+K`)**: Upgraded command palette with rich XAML item templates featuring glyph icon containers, bold titles, categorized pill badges, instant suggestions, and dual title/category search.
- **TitleBar Chrome Polish**: Modernized global search box with pill corners (`CornerRadius="8"`, `Width="320"`), pill download indicator (`CornerRadius="14"`), and rounded action buttons.
- **Friendly Empty States**: Upgraded placeholder iconography with accent-tinted squircle containers (`CornerRadius="18"`).
- **Dynamic Tool Status Badges**: Contextual badge styles and colors on Tool Detail, Catalog, and Installed pages reflecting installed, downloaded, or catalog-available states.

#### 💻 Workstation Environment & Setup Suite
- **WSL Distribution Inspector**: Inspect installed distributions, default WSL version, running status, and health directly in-app.
- **Visual PATH Manager**: Review User and Machine PATH entries, identify duplicates and broken references, and resolve conflicts with one click.
- **SSH Key Management & Backup**: Generate, inspect, and safely back up SSH keys for GitHub, GitLab, and custom Git remotes.
- **Git & Shell Profile Setup**: Manage Git identities and generate PowerShell and terminal shell completion scripts.

#### ☁️ Cloud Connections & Kubernetes Dashboard
- **Multi-Cloud Authentication Status**: Automatic login detection and credential status for AWS CLI, Azure CLI, and Google Cloud SDK.
- **Interactive Kubernetes Contexts**: Discover and switch active kubectl contexts and namespaces directly from the UI.

#### 📦 SBOM & Enterprise Compliance Export
- **CycloneDX 1.5 & SPDX 2.3**: One-click and headless CLI export (`--sbom <file> [--sbom-format spdx]`) generating standardized Software Bill of Materials for vulnerability scanning and enterprise auditing.
- **Comprehensive Component Metadata**: Includes detected versions, download provenance, SHA-256 checksums, package URLs (`pkg:generic`), and licenses.

#### 💾 Storage & Download Cache Manager
- **Disk Usage Breakdown**: Interactive visualization of completed vendor artifacts, partial downloads, and audit logs.
- **In-Flight Safe Cleanup**: Clean old or stale artifacts without interrupting active downloads, plus opt-in scheduled auto-cleaning.
- **Headless CLI Commands**: Manage workstation disk space headlessly with `--storage` and `--clean-downloads [partial|old|all]`.

#### ⚙️ Settings Portability & DevContainer Import
- **Settings Backup & Restore**: Export and restore settings, custom stacks, and tool pins to a portable JSON bundle (`--export-settings`, `--import-settings`) with secrets isolation.
- **devcontainer.json Import**: Map features and VS Code extensions from existing project repositories directly to catalog tools.

#### 🛠️ Catalog Expansion (95+ Official Tools)
- **New Languages & Runtimes**: PowerShell 7, Go, Node.js LTS, Python 3, and Rust with automated health checks.
- **Copy as Markdown**: Instant markdown table export from the catalog, stacks, and installed tools for READMEs and documentation.
- **In-App Notification Center & Active Download Pill**: Live event feed and title bar progress ring.

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

# Run the 5-point workstation doctor health check
DevOpsToolsInstaller.exe --path-doctor

# Discover and import existing tools from winget or chocolatey
DevOpsToolsInstaller.exe --import-winget

# Generate an instant Markdown workstation health & audit report
DevOpsToolsInstaller.exe --report workstation-audit.md

# Generate a VS Code DevContainer & Dockerfile bundle
DevOpsToolsInstaller.exe --generate-devcontainer ./my-devcontainer.zip

# Clean up or uninstall tools
DevOpsToolsInstaller.exe --uninstall kubectl,terraform
```
'@

# Resolve repository base URL for clickable markdown links
$repoUrl = "https://github.com/NotHarshhaa/DevOpsToolsInstaller"
if ($env:GITHUB_REPOSITORY) {
    $server = if ($env:GITHUB_SERVER_URL) { $env:GITHUB_SERVER_URL.TrimEnd('/') } else { "https://github.com" }
    $repoUrl = "$server/$($env:GITHUB_REPOSITORY)"
} else {
    $origin = (git config --get remote.origin.url 2>$null)
    if ($origin) {
        $origin = $origin.Trim() -replace '\.git$', ''
        $origin = $origin -replace '^git@github\.com:', 'https://github.com/'
        if ($origin -match '^https?://') {
            $repoUrl = $origin
        }
    }
}

# Build the commit changelog dynamically from git history (previous tag -> current tag)
# Automatically unshallow if running in shallow CI checkouts so all commit objects are reachable.
$changelog = ""
try {
    git fetch --tags --quiet 2>$null
    if ((git rev-parse --is-shallow-repository 2>$null) -eq "true") {
        git fetch --unshallow --quiet 2>$null
    }

    # Determine target reference (the release tag or HEAD)
    $targetRef = if (git rev-parse --verify --quiet "$Tag" 2>$null) { $Tag } else { "HEAD" }

    # Find the immediate previous tag leading up to targetRef
    $prevTag = (git describe --tags --abbrev=0 "$targetRef^" 2>$null)
    if (-not $prevTag) {
        $prevTag = (git tag --sort=-creatordate 2>$null | Where-Object { $_ -ne $Tag -and $_ -ne "v$Tag" } | Select-Object -First 1)
    }

    $revRange = if ($prevTag) { "$prevTag..$targetRef" } else { $targetRef }

    # Query git log with tab delimiter for robust parsing: shortHash, fullHash, subject, author
    $rawLog = (git log --pretty=format:"%h%x09%H%x09%s%x09%an" $revRange 2>$null | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($rawLog)) {
        $lines = @()
        foreach ($line in ($rawLog -split "`r?`n")) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $parts = $line -split "`t"
            if ($parts.Length -ge 4) {
                $shortHash = $parts[0]
                $fullHash  = $parts[1]
                $subject   = $parts[2]
                $author    = $parts[3]

                # Link PR numbers like #123 if present
                $linkedSubject = $subject -replace '#(\d+)', "[$&]($repoUrl/pull/`$1)"
                $lines += "- [``$shortHash``]($repoUrl/commit/$fullHash) - **$linkedSubject** ($author)"
            } elseif ($parts.Length -ge 1) {
                $lines += "- $line"
            }
        }

        if ($lines.Count -gt 0) {
            $header = if ($prevTag) { "Changes since ``$prevTag`` ($($lines.Count) commits):" } else { "Commits in this release ($($lines.Count) commits):" }
            $changelog = "$header`n`n" + ($lines -join "`n")
            if ($prevTag) {
                $changelog += "`n`n**Full Diff**: [$prevTag...$Tag]($repoUrl/compare/$prevTag...$Tag)"
            }
        }
    }
} catch {
    Write-Warning "Could not dynamically query git log: $_"
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
