param(
    [string]$Tag = "v1.5.0",
    [string]$AssetsDir = "release_assets",
    [string]$OutputFile = "release_notes.md"
)

$x64Hash = "N/A"
$arm64Hash = "N/A"

if (Test-Path "$AssetsDir\DevOpsToolsInstaller_x64.exe") {
    $x64Hash = (Get-FileHash -Path "$AssetsDir\DevOpsToolsInstaller_x64.exe" -Algorithm SHA256).Hash.ToLower()
}
if (Test-Path "$AssetsDir\DevOpsToolsInstaller_arm64.exe") {
    $arm64Hash = (Get-FileHash -Path "$AssetsDir\DevOpsToolsInstaller_arm64.exe" -Algorithm SHA256).Hash.ToLower()
}

if (Test-Path $AssetsDir) {
    # Generate SHA256SUMS.txt
    Get-ChildItem -Path "$AssetsDir\*.exe" -ErrorAction SilentlyContinue | ForEach-Object {
        $h = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLower()
        "$h  $($_.Name)"
    } | Out-File -FilePath "$AssetsDir\SHA256SUMS.txt" -Encoding utf8
}

$template = @'
# 🚀 DevOps Tools Installer __TAG__

Welcome to the **__TAG__** release of **DevOps Tools Installer**!
This release brings modern Fluent 2 animations, powerful developer productivity features, critical uninstaller/download reliability fixes, and expanded stack presets.

---

### ✨ What's New & Key Highlights

#### 🎨 Modern UI & Smooth Windows 11 Animations
- **Fluid Page Transitions**: Gliding entrance navigation transitions between Home, Catalog, Downloads, and Settings.
- **Staggered Card Animations**: Tool cards and downloads now cascade into view seamlessly with Fluent staggering.
- **Elevated Hero Dashboard**: Modern acrylic/glassmorphic header with dynamic 3-metric statistics (Available Tools, Categories, Installed Tools).
- **Redesigned Downloads Page**: Polished cards with tool logos, smooth progress indicators, and animated empty states.

#### ⚡ One-Click User PATH Integration
- Configure `Tools\bin` into your User `PATH` environment variable with a single click in **Settings**.
- Live status indicator badge (**"Configured in PATH"** / **"Not in PATH"**) with zero admin privileges required.
- Dynamically updates active application process environment.

#### 📂 Custom Download Folder Selector
- Choose any custom drive, SSD, or folder location for tool downloads via native Windows folder picker.
- Persistent configuration across app restarts with 1-click reset to default.

#### 📦 Curated DevOps Stack Presets
- Filter tools instantly in the **Catalog** via the new **Presets** dropdown:
  - ☸ **Kubernetes Stack**: `kubectl`, `helm`, `k9s`, `minikube`, `kind`
  - ☁ **Cloud CLIs**: `aws-cli`, `azure-cli`, `gcloud-cli`, `doctl`
  - 🏗 **Infrastructure as Code (IaC)**: `terraform`, `terragrunt`, `ansible`, `pulumi`, `packer`
  - 🛡 **Security & Scanning**: `trivy`, `snyk`, `grype`, `syft`, `cosign`
  - ⚡ **Essential CLI Utilities**: `gh`, `jq`, `yq`, `fzf`, `ripgrep`, `bat`

#### 🔍 Deep Tool Details Modal Inspector
- Inspect full tool metadata right from any catalog card: version, license, publisher, official website, direct download URI, silent installer flags, and execution test command.

#### 🛑 Download Cancellation Support
- Active downloads can now be cancelled immediately with graceful HTTP stream termination.

---

### 🛡️ Bug Fixes & Reliability Improvements

- **Safe Registry Uninstaller**: Replaced loose substring matching with strict word-boundary regex (`\b`) and vendor alias resolution to prevent unintended uninstalls. Fixed MSI uninstall arguments (`/X` instead of `/I`).
- **Download Integrity & 0-Byte Prevention**: Validates HTTP Content-Length and ensures downloaded files are non-zero before marking as completed.
- **Archive Executable Deployment**: Automatically scans extracted `.zip` and `.tar.gz` archives and copies executables into `Tools\bin`.
- **Single-File Packaging Compatibility**: Replaced `Assembly.Location` with `AppContext.BaseDirectory` for single-file self-contained deployment.

---

### 📦 Verification & Checksums

| Asset | SHA256 Hash |
| :--- | :--- |
| **`DevOpsToolsInstaller_x64.exe`** | `__X64_HASH__` |
| **`DevOpsToolsInstaller_arm64.exe`** | `__ARM64_HASH__` |

*(All asset checksums are also downloadable in `SHA256SUMS.txt`)*

---

### 🚀 Quick Start
1. Download `DevOpsToolsInstaller_x64.exe` (or `_arm64.exe` for ARM64 devices).
2. Run the executable — no installation required (portable single-file app).
3. Open **Settings** and click **"Add to PATH"** to enable command-line access for all portable tools.
'@

$content = $template.Replace("__TAG__", $Tag).Replace("__X64_HASH__", $x64Hash).Replace("__ARM64_HASH__", $arm64Hash)
$content | Out-File -FilePath $OutputFile -Encoding utf8
Write-Host "Generated release notes at $OutputFile"
