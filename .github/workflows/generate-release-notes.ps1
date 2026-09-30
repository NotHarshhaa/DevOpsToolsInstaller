param(
    [string]$Tag = "v3.0.0",
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
# 🚀 DevOps Tools Installer __TAG__ Major Milestone Release

Welcome to the **__TAG__** major release of **DevOps Tools Installer**!
This milestone delivers **Migration & Package Import from WinGet & Chocolatey**, **Corporate Proxy Support**, an **Automated Workstation Markdown Report Generator**, **Motion Helpers with Fluid Micro-Animations**, and an **Expanded Headless CLI Engine** — alongside official **Microsoft Store** availability and our security-first pipeline with signed catalogs.

---

### ✨ Major Features & What's New in __TAG__

#### 🔄 Workstation Migration, Import/Export & Reporting
- **Import from WinGet & Chocolatey**: Easily migrate your workstation by discovering and importing existing developer tools installed via `winget` or `chocolatey` directly into DevOpsToolsInstaller (`--import-winget`, `--import-choco`).
- **Markdown Workstation Health & Audit Reports**: Generate a comprehensive Markdown workstation audit report (`--export-report` or UI button) detailing installed tools, current versions, health status, and environment PATH configuration.
- **Tool Profile Export & Import**: Share and synchronize workstation setups across machines with JSON tool profiles (`--export-profile <file>`, `--import-profile <file>`).
- **Standalone Bootstrap Generator**: Create a self-contained, portable PowerShell bootstrap script (`--generate-bootstrap <file>`) to reprovision an identical developer workstation on new machines with zero dependencies.

#### 🌐 Corporate Proxy & Network Configuration
- **Enterprise Proxy Support**: Configure custom HTTP/HTTPS/SOCKS proxy endpoints with optional credential authentication directly from **Settings → Network**.
- **Proxy-Aware Pipeline**: Tool downloads, catalog updates, and GitHub release checks seamlessly route through your corporate proxy to operate behind enterprise firewalls.

#### 🎨 Fluid Motion Helpers & WinUI 3 Polish
- **Motion System with Micro-Animations**: Introduced lightweight attached animation properties for smooth `HoverLift` elevations and interactive `Pulse` feedback on tool cards, stack items, and primary action buttons.
- **Tool Release Notes Viewer**: View full upstream vendor release notes directly inside the application via a sleek in-app dialog.

#### 🖥️ Expanded Headless CLI Engine
- **`--update-all` & `--update <tools>`**: Automate checking and downloading updates for all or specific managed tools.
- **`--download-only <tools>`**: Pre-cache vendor installers and archives locally without executing them immediately.
- **`--export-report [file]`**: Export an instant markdown workstation inventory for compliance and documentation.
- **`--generate-bootstrap [file]`**: Generate an unattended PowerShell provisioning script from currently installed tools.

#### 🏪 Official Microsoft Store Distribution
- **Microsoft Store App**: Install directly from the Store with silent, automatic background updates:
  ```powershell
  winget install 9PJL0VR3H7VG
  ```
- Also available on the web: [Microsoft Store Page](https://apps.microsoft.com/detail/9PJL0VR3H7VG).

#### 🛡️ Hardened Security & Resilient Downloads
- **Fail-Closed Authenticode Verification**: `WinVerifyTrust` validates the entire certificate chain (`WTD_REVOKE_WHOLECHAIN`) with explicit revocation handling.
- **ECDSA Signed Catalog**: Remote catalogs are cryptographically verified using publisher-pinned ECDSA P-256 public keys.
- **Resumable Transfers**: Interrupted downloads continue via HTTP `Range` with ETag validation.
- **Native Tar Archive Support**: Automatic in-app extraction of `.tar.gz`, `.tar.bz2`, and `.tar.xz` archives via Windows built-in `tar.exe`.

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
