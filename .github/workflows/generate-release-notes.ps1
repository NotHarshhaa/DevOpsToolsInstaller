param(
    [string]$Tag = "v2.5.0",
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
# 🚀 DevOps Tools Installer __TAG__ Stable Release

Welcome to the **__TAG__** stable milestone release of **DevOps Tools Installer**!
This major release delivers enterprise deployment packages (**Windows Installer .msi** & **Setup Wizard .exe**), official **WinGet package manager support**, complete **UI & functionality bug fixes**, real-time **download speed tracking & ETA timers**, revamped **Installed page diagnostics with shell completion generator**, **PATH environment health inspection**, and an official **hero showcase banner**.

---

### ✨ Major Features & What's New in __TAG__

#### 📦 Enterprise Windows Installer (.msi) & Setup Wizard (.exe)
- **WiX Toolset v4/v5 MSI Package**: Introduced native `.msi` Windows Installer packages (`DevOpsToolsInstaller_x64.msi`) tailored for enterprise rollouts, Microsoft Intune, SCCM, and Active Directory GPO with silent installation support (`msiexec /i DevOpsToolsInstaller_x64.msi /qn`).
- **Inno Setup Wizard**: Enhanced the standard setup wizard (`DevOpsToolsInstaller_x64_Setup.exe`) with automated Start Menu shortcuts, Desktop icons, and PATH registration.
- **Portable Binaries**: Continue providing zero-install self-contained portable executables for both `x64` and `arm64`.

#### 🪟 WinGet Package Manager Integration
- Added official WinGet package manifests (`NotHarshhaa.DevOpsToolsInstaller`) allowing seamless installation, upgrades, and uninstallation straight from the terminal:
  ```powershell
  winget install NotHarshhaa.DevOpsToolsInstaller
  ```
- Added automated WinGet manifest submission and validation tooling (`scripts/submit-winget.ps1`).

#### 🛠️ Comprehensive UI & Functionality Hardening
- **Context Menus & Version Badges Fixed**: Resolved type-casting issues in `CatalogPage` so right-click menu items (*"Favorite"*, *"Copy Install Command"*, *"Tool Details"*) and version picker badges execute reliably from all UI elements.
- **Category Filter Matching**: Fixed category chip Tag values to strictly match ampersands in `catalog.json` (*"CI/CD & Version Control"*, *"Monitoring & Observability"*, *"Policy & Compliance"*, *"Database & Data"*, *"Editors & Terminals"*).
- **Automated Artifact Extraction on Home Dashboard**: Installing tools or stacks from the Home page now triggers post-download extraction and binary placement into `Tools\bin` via `ArtifactService`.
- **CLI Probing & Shell Completion IDs**: Aligned probe command mapping and autocompletion generators for canonical tool IDs (`argocd`, `snyk`).
- **Clean Percentage Displays & Action Labels**: Formatted download progress percentages into clean whole numbers with responsive post-installation confirmation badges (`Installed ✓`, `Extracted ✓`, `In Tools\bin ✓`).

#### ⚡ Download Speed Tracking & Explorer Integration
- **Real-Time Transfer Metrics**: Active downloads now calculate and display live download speeds (MB/s) and estimated time remaining.
- **File Explorer Integration**: 1-click "Show in Folder" opens the exact downloaded artifact in Windows Explorer.
- **Activity Log Panel**: Collapsible activity log showing timestamps, severity levels, and 1-click clipboard export for troubleshooting.

#### 🩺 Revamped Installed Tools & Shell Completions
- **CLI Health Probing**: Executes binaries (`--version` / `-v`) non-blockingly to measure runtime responsiveness and execution latency (ms).
- **Shell Autocompletion Generator**: Automatically generates completion scripts for PowerShell and Bash with 1-click integration into the user's PowerShell `$PROFILE`.
- **Safe Uninstallation**: Supports vendor Windows uninstallation wizards, archive extraction removal, and binary cleanup from `Tools\bin`.

#### 🔍 PATH Environment Health Diagnostics
- Dedicated PATH diagnostics inspecting user and process environment variables to verify that `Tools\bin` is active.
- 1-click "Add to PATH" helper and quick folder access directly from the Home dashboard and Settings.

#### 🎨 Showcase Branding & Documentation
- Designed and embedded high-resolution hero showcase banner into `README.md`.
- Added comprehensive `PRIVACY.md` privacy policy detailing zero-telemetry and local-only operation.

---

### 📋 Full Commit Changelog (from v2.1.0 to __TAG__)

- `d44e1d6` - **feat**: Add showcase banner to README and include banner image asset
- `f4ebc50` - **feat**: Enhance navigation and tool installation logic in MainWindow and HomeViewModel
- `c9642f5` - **feat**: Add Windows Installer (.msi) support and enhance release notes generation
- `ac69682` - **feat**: Implement File Explorer Integration and Enhance Downloads Page UI
- `3fd2122` - **feat**: Enhance About and Settings Pages with New Features and UI Improvements
- `567e511` - **feat**: Introduce Download Speed Tracking and Path Health Service
- `9283f52` - **feat**: Revamp Installed Page UI with Enhanced Search and Tool Management
- `f6dda61` - **feat**: Enhance Tool Bundles with Categories and UI Improvements
- `4a4881c` - **refactor**: Clean up Styles.xaml and update CatalogPage.xaml layout
- `da48fba` - **feat**: Revamp Catalog Page UI and enhance Tool Definition properties
- `735434b` - **feat**: Add Privacy Policy document for DevOps Tools Installer
- `5e04210` - **fix(winget)**: update LicenseUrl branch to master and add submit-winget script
- `ea26e53` - **feat**: Update README and add WinGet support for DevOpsToolsInstaller
- `cd80ba0` - **feat**: Enhance AppUpdaterService with installation detection and asset selection logic
- `ed5e81d` - **feat**: Add MSIX packaging support and update build process
- `b74c903` - **docs**: Update README with notes and tips for user guidance
- `a933cc7` - **feat**: Add code signing support and update README acknowledgements
- `43a31cb` - **feat**: Add Windows Setup Wizard and update build process
- `0c6177b` - **feat**: Revise README for clarity and detail enhancement

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
- **Option A (WinGet)**:
  ```powershell
  winget install NotHarshhaa.DevOpsToolsInstaller
  ```
- **Option B (Setup Wizard)**: Download and run `DevOpsToolsInstaller_x64_Setup.exe` to install to `C:\Program Files\DevOpsToolsInstaller` with automated shortcuts and PATH integration.
- **Option C (Windows Installer Package)**: Download and run `DevOpsToolsInstaller_x64.msi` for enterprise GPO, Intune, SCCM, or silent rollouts (`msiexec /i DevOpsToolsInstaller_x64.msi /qn`).
- **Option D (Portable)**: Download `DevOpsToolsInstaller_x64.exe` (or `_arm64.exe`) and run directly — no installation required.
'@

$content = $template.Replace("__TAG__", $Tag).Replace("__SETUP_HASH__", $setupHash).Replace("__MSI_HASH__", $msiHash).Replace("__X64_HASH__", $x64Hash).Replace("__ARM64_HASH__", $arm64Hash)
[System.IO.File]::WriteAllText($OutputFile, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated release notes at $OutputFile"
