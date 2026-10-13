param(
    [string]$Tag = "v3.0.2",
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
This release introduces an overhauled **HomePage with Core Features Showcase & Quick Start guide**, the interactive **Custom Stack Builder**, the comprehensive **Workstation Doctor**, a universal **Collapsible Live Log Drawer**, **PATH Environment Doctor & Fixer**, **VS Code DevContainer & Dockerfile Generator**, **Off-Peak Download Scheduler**, and key **UI & shortcut fixes**.

---

### ✨ What's New & Highlights in __TAG__

#### 🎨 Redesigned HomePage & Core Features Showcase
- **Immersive Hero Header**: Authentic in-app Acrylic backdrop, one-line value proposition, and instant "Browse catalog" & "Build a stack" actions.
- **3-Step Quick Start Stepper**: Visual workstation onboarding path (*Pick tools* → *Verify & download* → *Install*).
- **9-Card Responsive Showcase Grid**: Dynamic live catalog counts (~90+ tools, 8 role bundles, detected installed tools), with hover and focus elevation lift.
- **30-Second First-Run Tour**: Guided TeachingTip onboarding chain for new users with Settings toggle.
- **Headless CLI Documentation**: In-app modal dialog with instant copy for CI and automation scripts.

#### 🧰 Custom Stack Builder (`StacksPage`)
- **Interactive 2-Column Composer**: Fluent Segmented switcher between "Presets" and "Stack Builder".
- **Visual Stack Assembly**: Drag-and-drop tool cards into "My Stack", reorder on the fly, with keyboard accessible controls.
- **Live Metrics**: Real-time tool count and estimated download size calculations.
- **Portability**: Save locally, Export/Import `.json` stack manifests with catalog validation.
- **1-Click Installation**: Batch install complete custom toolchains through the unified download queue.

#### 🩺 Workstation Doctor (`DoctorPage`)
- **5-Point Health Scan**: Inspects PATH conflicts/duplicates, missing prerequisites (WSL2, Hyper-V, virtualization), multi-version CLI conflicts, outdated tools, and tools missing from PATH.
- **Live Diagnostics Header**: Scan progress ring with Pass, Warning, and Fail count indicators.
- **Confirmed Fix Actions**: Guided 1-click remediation dialogs requiring explicit user confirmation before modifying system settings.
- **Report Export**: Copy Markdown workstation health report to clipboard or re-run checks anytime.

#### 📜 Universal Collapsible Log Drawer
- **Global Drawer Control**: Slide-up activity drawer accessible across all pages with toggle button and <kbd>Ctrl</kbd>+<kbd>`</kbd> shortcut.
- **Live Event Stream**: Real-time chunk progress, SHA-256 validation, Authenticode signature verification, and installation logs.
- **Diagnostics Controls**: Severity filtering (*All, Info, Warning, Error*), text search, pause auto-scroll, clear, and copy.
- **Accessibility & Settings**: Virtualized `ListView` for massive logs; height remembered across sessions; honors Windows *Reduce animations*.

#### 🛣️ PATH Environment Doctor & Fixer
- **Deep Inspection**: Audit User and System PATH for duplicates, dead directories, and missing binary locations.
- **Safety First**: Automated timestamped backups to `%LOCALAPPDATA%` prior to any adjustments.
- **1-Click Optimization**: Prune invalid paths, deduplicate, configure `Tools\bin`, and broadcast environment updates (`WM_SETTINGCHANGE`).

#### 🐳 VS Code DevContainer & Dockerfile Generator
- **Containerized Workstations**: Export any stack or installed toolchain to production-grade `.devcontainer/devcontainer.json` and multi-stage `Dockerfile`.
- **Automated Mapping**: Maps catalog tools to official DevContainer features and Linux CLI binaries.
- **Headless CLI Support**: Run `--generate-devcontainer <output>` in CI/CD pipelines.

#### ⏰ Off-Peak Scheduler & Auto-Retry Engine
- **Bandwidth Management**: Configure scheduled download windows under **Settings → Network** to protect daytime bandwidth.
- **Automatic Recovery**: Exponential backoff retry engine for failed downloads during network disruptions.

#### 🐛 UI Polish & Shortcut Fixes
- **Floating Badge Fix**: Resolved the persistent "Ctrl+K" accelerator tooltip badge overlay by setting `KeyboardAcceleratorPlacementMode="Hidden"`.
- **Keyboard & Narrator Access**: Full AutomationProperties and focus visual polish across all new pages and dialogs.

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
    if (-not $prevTag) { $prevTag = "v3.0.1" }
    
    $targetRef = if (git rev-parse --verify --quiet "$Tag" 2>$null) { $Tag } else { "HEAD" }
    $log = (git log --pretty=format:'- `%h` - **%s**' "$prevTag..$targetRef" 2>$null | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($log)) {
        $changelog = "Changes since ``$prevTag``:"
        $changelog += [Environment]::NewLine + [Environment]::NewLine + $log
    }
} catch { }

if ([string]::IsNullOrWhiteSpace($changelog)) {
    $changelog = @"
Changes since ``v3.0.1``:

- ``ed5c5d0`` - **feat: Enhance HomePage and ViewModel with new features and styles**
- ``c5773ae`` - **feat: Expand Custom Stack Builder and Workstation Doctor features**
- ``b62f6bd`` - **feat: Implement custom stack management and builder functionality**
- ``2eb5dac`` - **feat: Implement live log drawer with enhanced logging features**
- ``b210a49`` - **feat: Add Workstation Doctor for diagnostics and health checks**
- ``98b29c3`` - **feat: Introduce PATH Environment Doctor, DevContainer & Download Scheduler**
- ``4bf8921`` - **Merge pull request #1 from NotHarshhaa/bot/catalog-refresh**
- ``43126c8`` - **chore: refresh tool catalog versions and checksums**
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
