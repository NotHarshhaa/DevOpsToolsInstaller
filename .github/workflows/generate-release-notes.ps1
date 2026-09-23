param(
    [string]$Tag = "v2.8.0",
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

Welcome to the **__TAG__** milestone release of **DevOps Tools Installer**!
This release introduces a **security-first architecture** with cryptographic catalog signing and Authenticode verification policies, full **Headless CLI automation** for terminal workflows, **System Tray minimization with background toast notifications**, native **WinUI 3 CommandBars** with fluent theme alignment, and modernized **Windows 11 setup wizard experiences**.

---

### ✨ Major Features & What's New in __TAG__

#### 🛡️ Enterprise Security & Integrity Hardening
- **ECDSA P-256 Signed Catalog & Fail-Closed Trust**: All remote catalog updates (`catalog.json` and `bundles.json`) are cryptographically verified using publisher-pinned ECDSA P-256 public keys (`CatalogSignatureService`), protecting against MITM attacks and mirror tampering.
- **Authenticode Signature Policy**: Added configurable signature verification policies in Settings (`WarnUnsigned` / `BlockUnsigned`) to verify digital signatures of downloaded vendor installers before execution.
- **Persistent Daily Audit Logging**: Complete post-incident traceability with append-only daily audit logs (`audit_YYYY-MM-DD.log`) tracking downloads, verification statuses, and tool launches.
- **Security-First Transport & Tagging**: Strict HTTPS-only transport enforcement and automatic Windows Mark-of-the-Web (`Zone.Identifier`) tagging on all downloaded artifacts.

#### ⚡ Headless CLI Mode & Automation
- **Terminal Automation**: Run `DevOpsToolsInstaller.exe` directly from PowerShell, CMD, or CI/CD scripts without launching the graphical UI:
  - `--list`: Discover all available tools and categories with clear formatting.
  - `--install <tool1,tool2>`: Non-interactive batch installation of developer tools.
  - `--install-bundle <bundle-id>`: Provision entire curated stacks (e.g. `k8s-starter`, `aws-devops`).
  - `--check-updates`: Quick CLI verification for newer application releases.

#### 🔔 System Tray Integration & Background Notifications
- **System Tray Minimization**: Keeps the installer accessible in the Windows taskbar notification area without cluttering your workspace.
- **Native Toast Notifications**: Real-time notifications for background download completions, installations, and update availability.
- **Smart Window Close Handling**: Option in Settings to minimize to the tray instead of terminating when closing the main window.

#### 🎨 Modern UI & Native CommandBar Enhancements
- **Native WinUI 3 CommandBars**: Replaced custom action bars with native `CommandBar` controls across `CatalogPage`, `DownloadsPage`, `InstalledPage`, and `StacksPage` for consistent Windows 11 styling and responsive action overflows.
- **Theme & Accent Color Alignment**: Refined `Styles.xaml` to align with Windows accent colors, ensuring high-contrast readability in both Dark and Light themes.
- **Navigation Performance**: Enabled `NavigationCacheMode` across pages for instant tab switching with zero redraw flicker.
- **Deduplication & Error Reporting**: Prevent duplicate concurrent downloads and display informative error diagnostics with unhandled exception logging.

#### 📦 Setup Wizard & Installer Polishing
- **Windows 11 Setup Experience**: Updated Inno Setup wizard with modern high-resolution branding graphics (`wizardlarge.bmp` and `wizardsmall.bmp`).
- **Process Protection**: Automatically detects and prompts to close active running instances during installation and updates.
- **Environment & PATH Integration**: Improved PATH variable handling and clean uninstallation routines.
- **Apache-2.0 License**: Streamlined legal and license notices across the application, installer, and repository.

---

### 📋 Full Commit Changelog (from v2.5.0 to __TAG__)

- `cc8990f` - **feat**: Revise README and installer setup for clarity and enhanced features
- `60082e8` - **feat**: Implement catalog signing and verification for enhanced security
- `6b1288c` - **feat**: Implement security features for installer with signature verification and audit logging
- `93c7f41` - **feat**: Add wizard images to installer for improved user interface
- `fbd27c3` - **feat**: Update installer script for enhanced user experience and functionality
- `73903af` - **feat**: Implement headless CLI mode and system tray functionality
- `c37e474` - **fix**: Adjust layout dimensions and improve description handling in StacksPage
- `ec75ee0` - **feat**: Refactor UI components to utilize native CommandBar and improve theme integration
- `f589058` - **feat**: Enhance error handling and UI improvements across various components

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
$outputDir = [System.IO.Path]::GetDirectoryName((Resolve-Path -Path $OutputFile -ErrorAction SilentlyContinue)?.Path ?? (Join-Path $PWD $OutputFile))
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
[System.IO.File]::WriteAllText($OutputFile, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated release notes at $OutputFile"
