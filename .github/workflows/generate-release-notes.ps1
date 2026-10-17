param(
    [string]$Tag = "v3.1.0",
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
This release introduces **Workstation Profiles, Snapshots & Install History**, **Cloud-Synced Profiles & Pre/Post-Install Hooks**, an **In-App Custom Tool Catalog Editor**, **Live Download ETA & Telemetry**, **Dynamic Window Material Settings (Mica, Mica Alt, Acrylic)**, **Workstation Doctor Health Score & Safe PATH Remediation**, **Installed Tools Update Preferences & Actions**, **Unified Accent SplitButtons**, and **Subpixel Crisp Text & Corner Scrollbar Layout Alignment**.

---

### ✨ What's New & Highlights in __TAG__

#### 🗃️ Workstation Profiles, Snapshots & History (`ProfilesPage`)
- **Named Workstation Profiles**: Capture currently installed toolsets or catalog selections into portable named profiles; 1-click **Apply** or **Install** all referenced tools.
- **Point-in-Time Snapshots**: Snapshot installed tools, versions, and User PATH; diff against workstation changes and restore missing tools.
- **Install History & Rollback**: Persistent JSONL activity trail (`history.jsonl`) recording installs, updates, uninstalls, and guided version rollbacks.
- **Cloud-Synced Profiles**: Opt-in sync/push/pull of profiles, snapshots, custom tools, and hooks via OneDrive, Dropbox, or network shares.
- **Starter Templates**: Profiles empty state includes curated templates (Kubernetes Developer, Cloud Engineer, IaC & Platform).

#### 🛠️ In-App Custom Tool Catalog Editor (`CatalogEditorPage`)
- **Author & Manage Custom Tools**: Add your own HTTPS-hosted tools to the catalog with full SHA-256 and Authenticode validation.
- **Live Preview Card**: Renders real-time mockup of custom tool card matching the official catalog styling.
- **Catalog Feed Import/Export**: Easily share team tool feeds via JSON manifests (`custom_tools.json`).

#### ⚡ Telemetry & Asynchronous Loading States
- **Live Download ETA**: Real-time ETA indicator calculated alongside transfer throughput.
- **Segmented Filter Controls**: Modern Fluent segmented chips for instant download queue filtering.
- **ProgressRing Hydration Overlays**: Smooth progress overlays across Profiles, Installed, and Stacks to eliminate empty-state flashing during async catalog hydration.

#### 🩺 Workstation Doctor & Health Score (`DoctorPage`)
- **Weighted 0-100 Health Score**: Real-time workstation health rating reflected on both Doctor and Home dashboards.
- **Safe PATH Remediation**: Preview and safely deduplicate / remove dead PATH entries with automated User PATH backup and 1-click Undo.
- **Report Redaction**: One-click redaction of local usernames and machine names when copying Markdown diagnostics reports for IT tickets.

#### ⚙️ Settings Workspace Modernization (`SettingsPage`)
- **Fluent SettingsCard / Expander Architecture**: Redesigned on CommunityToolkit Settings controls with bold headers, descriptions, and collapsed value summaries.
- **Section & Card Search**: Instant keyword search filtering across all settings categories.
- **Security Posture Overview**: Real-time snapshot of signature verification policy, hooks status, proxy configuration, and audit log paths.

#### 🎯 Crisp DirectWrite Text & Corner-Pinned Scrollbars
- **Subpixel Text Clarity**: Eliminated GPU bilinear interpolation blur by removing composition visual scaling on hover/focus across all cards.
- **Corner-Pinned Scrollbars**: Arranged scroll containers to the window boundary so vertical scrollbars sit flush against the far-right corner with 32px inner gutter.
- **Unified Accent SplitButtons**: Consistent Fluent SplitButton styling with custom accent divider brushes across Catalog, Stacks, and Tool Details.

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

# Build the commit changelog dynamically from git history (previous tag → current tag),
# with a graceful fallback when tags are unavailable (shallow CI checkout, first release).
$changelog = ""
try {
    git fetch --tags --quiet 2>$null
    $prevTag = (git tag --sort=-creatordate 2>$null | Where-Object { $_ -ne $Tag } | Select-Object -First 1)
    if (-not $prevTag) { $prevTag = "v3.0.2" }
    
    $targetRef = if (git rev-parse --verify --quiet "$Tag" 2>$null) { $Tag } else { "HEAD" }
    $log = (git log --pretty=format:'- `%h` - **%s**' "$prevTag..$targetRef" 2>$null | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($log)) {
        $changelog = "Changes since ``$prevTag``:"
        $changelog += [Environment]::NewLine + [Environment]::NewLine + $log
    }
} catch { }

if ([string]::IsNullOrWhiteSpace($changelog)) {
    $changelog = @"
Changes since ``v3.0.2``:

- ``e1cd895`` - **feat: Refine layout and padding across various pages for improved UI consistency**
- ``996fac4`` - **feat: Introduce custom accent styles and enhance SplitButton functionality**
- ``ff54b18`` - **feat: Enhance Downloads and Profiles pages with new features and loading states**
- ``002e833`` - **feat: Enhance Workstation Doctor functionality and UI**
- ``0a91275`` - **feat: Revamp Settings workspace with enhanced layout and functionality**
- ``6a98b32`` - **feat: Enhance CatalogEditorPage with improved layout and validation features**
- ``0bfb0a7`` - **feat: Enhance Stacks workspace with dynamic card sizing and detailed tool previews**
- ``21e22ad`` - **feat: Implement update preferences and enhance installed tools management**
- ``186d1c7`` - **feat: Revamp Catalog page with enhanced tool details and UI improvements**
- ``adb90bb`` - **feat: Revamp Home dashboard with new metrics and responsive design**
- ``06b457c`` - **feat: Enhance UI layout and window material settings**
- ``5de9f67`` - **fix: Synchronize sidebar navigation and improve version handling**
- ``f999dd4`` - **fix: Restore event handlers for segmented controls in LogDrawer and StacksPage**
- ``954b657`` - **Merge pull request #2 from NotHarshhaa/bot/catalog-refresh**
- ``4bc87cd`` - **chore: refresh tool catalog versions and checksums**
- ``4552bc3`` - **fix: Improve error handling and navigation logging**
- ``23b386a`` - **feat: Enhance UI/UX with new controls and keyboard shortcuts**
- ``a0b2e47`` - **feat: Introduce workstation profiles, snapshots, and install history features**
- ``01b76d5`` - **style: Update CSS for improved layout and responsiveness**
- ``58aae3c`` - **feat: Add static marketing website for DevOps Tools Installer**
- ``336e5cf`` - **fix: Update README and banner image**
"@
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
