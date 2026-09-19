param(
    [string]$Tag = "v2.0.0",
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
This major 2.0 release brings enterprise-grade security with native Authenticode digital signature verification, deep CLI health checks and installed version probing, one-click shell autocompletion snippet generator, rich multi-version catalog management with previous release downloads, dynamic WrapPanel layouts, official vector branding across 45+ DevOps tools, desktop shortcut integration, and an all-new About page.

---

### ✨ Major Features & What's New

#### ⏪ Tool Catalog Version Management & Previous Releases
- **Multi-Version Selector**: Select and install specific previous versions of major DevOps tools (e.g., Terraform, Kubectl, Helm, OpenTofu, Ansible) directly from tool cards.
- **Dynamic Artifact URL Resolution**: Automatically retargets download endpoints and asset filenames when selecting alternative versions.
- **Latest Version Display**: All 47 cataloged tools now display verified latest versions alongside installation statuses.

#### 🛡️ Authenticode Digital Signature Verification
- **Win32 WinVerifyTrust Integration**: Automatically inspects downloaded `.exe` and `.msi` vendor installers for valid cryptographic signatures prior to execution.
- **Tampering & Malware Protection**: Validates certificate chains against Windows Trusted Root Certification Authorities to ensure vendor authenticity.
- **Publisher Verification**: Extracts and logs publisher certificate subjects for full security audit transparency.

#### 🩺 CLI Health Checks & Installed Version Probing (`--version`)
- **Real-Time Version Probing**: Directly executes installed tool binaries (`--version` / `-v` / `version`) to determine the exact runtime version active on your machine.
- **Latency & Status Metrics**: Measures CLI process execution response times in milliseconds, reporting immediate Healthy, Degraded, or Missing statuses.
- **Batch Verification**: Run comprehensive health checks across all installed DevOps tools to confirm PATH readiness and functionality.

#### ⚡ Shell Completion Snippet Generator
- **PowerShell & Bash Autocompletions**: Generates ready-to-use completion snippets for major CLIs (`kubectl`, `helm`, `gh`, `docker`, `terraform`, `podman`, etc.).
- **One-Click PowerShell `$PROFILE` Insertion**: Automatically appends completion initialization commands to your PowerShell profile without manual editing.

#### 🎨 Modern Windows 11 Fluent UI, Responsive WrapPanel & Branding
- **Dynamic WrapPanel Control**: Replaces rigid grid structures with a smooth, responsive wrapping layout that automatically adapts to any window size or monitor resolution.
- **Vector SVG & Brand Logos**: Integrated 45+ crisp official vendor logos for tools like AWS CLI, Azure CLI, Google Cloud SDK, Terraform, ArgoCD, Prometheus, Grafana, and more.
- **Application Icon & Shortcuts**: Windows taskbar and title bar branding with one-click Desktop and Start Menu shortcut creation in Settings.

#### ⭐ Favorites & Real-Time Activity Logging
- **Tool Favoriting**: Pin preferred or frequently installed tools to quickly filter your essential stack.
- **Activity Feed**: Real-time logging of downloads, installations, cancellations, path modifications, and health inspections with exportable audit logs.

#### ℹ️ All-New About & Architecture View
- Detailed system architecture insights (WinUI 3, Windows App SDK 1.6, .NET 8 runtime).
- Direct links to GitHub repository, documentation, bug tracker, and maintainer profile.

---

### 🛡️ Improvements & Bug Fixes

- **Uninstaller Safety**: Word-boundary regex matching prevents accidental uninstalls of similarly named software; corrected MSI uninstallation command arguments (`/X`).
- **Download Integrity**: Enforces Content-Length validation and guarantees non-zero byte payloads before marking downloads complete.
- **Archive Extraction**: Automated scanning and extraction of `.zip` and `.tar.gz` binaries directly into `Tools\bin`.
- **Single-File Deployment**: Hardened path resolution using `AppContext.BaseDirectory` for zero-dependency single-file portable builds.

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
