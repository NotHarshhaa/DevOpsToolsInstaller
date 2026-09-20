param(
    [string]$Tag = "v2.1.0",
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

Welcome to the **__TAG__** milestone release of **DevOps Tools Installer**!
This major release transforms DevOps Tools Installer with a completely redesigned **WinUI 3 Gallery aesthetic** featuring **Mica Alt** translucent materials, doubles the catalog from 47 to **90 curated tools** across 10 categories (including 3 brand-new categories), introduces **Curated Stacks** for one-click workstation provisioning and live stack filtering, adds **100% official vector SVG branding**, integrates an **in-app GitHub auto-updater service**, embeds **global sidebar search**, and establishes comprehensive **Security Disclosures and Behavioral Precautions**.

---

### ✨ Major Features & What's New

#### 🎨 WinUI 3 Gallery Redesign & Translucent Mica Alt Backdrop
- **Mica Alt Window Material**: Modernized the application canvas with Windows 11 translucent Mica Alt backdrop styling and custom titlebar bleed-through.
- **Segoe Fluent Icons**: Replaced emojis across all pages, navigation items, and stack cards with clean, native Segoe Fluent Icons (`\uE...`) for a crisp, enterprise-grade feel.
- **Polished Card & Chip Styling**: Refined category filter chips, badge containers, and hover states with smooth micro-animations.

#### 📦 Catalog Expanded to 90 Tools with 3 Brand-New Categories
- **Expanded Coverage**: The catalog has nearly doubled from 47 to **90 essential DevOps, cloud, and engineering tools**.
- **Three New Categories**:
  - **🌐 Networking & Tunneling**: `ngrok`, `cloudflared`, `tailscale`, `wireshark`, `nmap`, `ctop`, `k9s`, etc.
  - **🛡️ Policy, Governance & Compliance**: `trivy`, `checkov`, `tfsec`, `syft`, `grype`, `cosign`, `opa`, etc.
  - **🗄️ Database & Data DevOps**: `flyway`, `liquibase`, `pgcli`, `mycli`, `usql`, `redis-cli`, etc.
- **Updated & Verified Endpoints**: All tool download URLs, checksums, and version manifests refreshed and validated.

#### 🖼️ 100% Official Vector SVG Logos
- Added high-fidelity, scalable official vector SVG logos for **all 90 cataloged tools** in `Assets/logos/`.
- Crisp rendering across all DPI scaling levels and display resolutions (AWS CLI, Azure CLI, Google Cloud SDK, Terraform, Podman, Trivy, Prometheus, Grafana, Ansible, Git, Docker, and more).

#### 🚀 Curated Stacks & Workstation Presets
- **Dedicated Full-Page Stacks Navigation**: Replaced popup dialogs with a dedicated **Curated Stacks** view (`StacksPage`) accessible directly from the sidebar.
- **12 Role-Based Presets**: Ready-to-use bundles including *Kubernetes Platform Engineer*, *Cloud Infrastructure Architect*, *GitOps Specialist*, *SecOps & Compliance Auditor*, *Full-Stack DevOps*, *Linux SysAdmin*, *Observability Specialist*, and more.
- **Dual-Action Workflow**:
  - **Install Stack**: Queue and batch-install all tools in a bundle with a single click.
  - **Select Stack**: Live-filter the Catalog to only show tools matching the chosen stack, accompanied by an interactive active stack banner and quick-clear action.
- **Per-Tool Status Indicators**: Visual checkmarks and live indicators directly on tool chips within each stack card.

#### 🔍 Global Sidebar Search (AutoSuggestBox)
- Integrated an **AutoSuggestBox** directly into the left navigation pane.
- Allows instant searching from anywhere in the application with automatic navigation and live query forwarding into the Catalog view.

#### 🔄 In-App GitHub Auto-Updater Service
- **Automatic & Manual Checks**: Automatically queries the GitHub Releases API on startup, with a manual **"Check for Updates"** button in Settings.
- **Interactive Update Dialog**: Displays release notes, asset sizes, and a real-time progress bar during download.
- **Safe Staging & Restart**: Downloads the latest release, verifies checksums, safely stages the new executable, and restarts seamlessly.

#### 🛡️ Security Disclosures, Behavioral Precautions & Disclaimers
- **Transparent Behavioral Disclosures**: Added dedicated disclosures detailing:
  - UAC privilege escalation requirements for vendor `.exe`/`.msi` installers.
  - User and System `PATH` modifications with idempotency guarantees.
  - Isolated tool sandbox storage in `%LOCALAPPDATA%\DevOpsToolsInstaller\`.
  - Cryptographic Authenticode signature and SHA-256 hash validation before execution.
  - Zero telemetry, zero analytics, and zero personal data collection.
- **Independent Open-Source Disclaimer**: Clearly states third-party tool ownership and vendor software trademarks.
- **Home Page Quick Access**: Dedicated security card on the Home page deep-linking directly into About page disclosures.

#### ⚡ Portable Single-File Compression
- Enabled `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>` in the build configuration, drastically reducing the distribution executable size without sacrificing startup performance.

---

### 📋 Full Commit Changelog (from v2.0.0 to v2.1.0)

- `dac0c1f` - **feat**: Enhance About and Home pages with security disclosures and transparency features
- `bec6fd7` - **feat**: Enhance Catalog and Stacks pages with curated stack functionality
- `8a6053b` - **feat**: Introduce Stacks page and enhance tool bundle navigation
- `49791d9` - **feat**: Enhance navigation and search functionality in MainWindow
- `9eb3c26` - **feat**: Add SVG logos for new tools in DevOpsToolsInstaller
- `0961116` - **feat**: Introduce curated tool bundles and enhance catalog functionality
- `2ac29f3` - **feat**: Add new bundles and tools to catalog
- `cb11390` - **feat**: Add new logos for various tools in DevOpsToolsInstaller
- `cc7d5c7` - **fix**: Update download URLs and version numbers in catalog.json
- `ecc3b54` - **refactor**: Remove unused AccentButtonStyle and update SelectedCategoryChipStyle
- `6473cf2` - **feat**: Implement update checking and dialog for application updates
- `21656f0` - **feat**: Enable compression for single-file publishing

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
2. Run the executable — completely portable with no pre-installation required.
3. In **Settings**, click **"Add to PATH"** to enable command-line access for all portable tools.
'@

$content = $template.Replace("__TAG__", $Tag).Replace("__X64_HASH__", $x64Hash).Replace("__ARM64_HASH__", $arm64Hash)
[System.IO.File]::WriteAllText($OutputFile, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated release notes at $OutputFile"
