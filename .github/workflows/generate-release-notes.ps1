param(
    [string]$Tag = "v2.9.0",
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
This release delivers a **self-healing resumable download engine** with automatic retry and ETag-validated resume, a **fail-closed Authenticode security pipeline** with certificate revocation checking, an **extended headless CLI** with uninstall automation, native **tar.gz / tar.bz2 / tar.xz archive extraction**, and **full-catalog health checks** — on top of the existing security-first architecture with cryptographically signed catalogs, system tray integration, and native WinUI 3 polish.

---

### ✨ Major Features & What's New in __TAG__

#### 🛡️ Hardened Security Pipeline
- **Fail-Closed Signature Verification**: The installer launch policy now treats undetermined signature checks (`Unknown`) as untrusted — only a positively verified Authenticode signature satisfies the "Block unsigned" policy, closing a verification-bypass edge case.
- **Certificate Revocation Checking**: `WinVerifyTrust` now validates the entire certificate chain (`WTD_REVOKE_WHOLECHAIN`) with explicit handling for revoked certificates and unreachable CRL/OCSP endpoints — revoked vendor certificates are no longer silently accepted.
- **ECDSA P-256 Signed Catalog & Fail-Closed Trust**: All remote catalog updates (`catalog.json` and `bundles.json`) are cryptographically verified using publisher-pinned ECDSA P-256 public keys (`CatalogSignatureService`), protecting against MITM attacks and mirror tampering.
- **Persistent Daily Audit Logging**: Complete post-incident traceability with append-only daily audit logs tracking downloads, verification statuses, and tool launches.
- **Security-First Transport & Tagging**: Strict HTTPS-only transport enforcement and automatic Windows Mark-of-the-Web (`Zone.Identifier`) tagging on all downloaded artifacts.

#### ⚡ Resilient Resumable Download Engine
- **Automatic Retry with Exponential Backoff**: Transient network failures (dropped Wi-Fi, DNS blips, connection resets) are retried up to 3 times — each attempt resumes from the `.partial` file instead of starting over.
- **ETag-Validated Resume**: Interrupted downloads record the server's ETag alongside the partial file; if the remote artifact has changed (e.g. a version-pinned URL now serves a newer build), the stale partial is discarded and the download restarts cleanly — bytes from two different artifacts can never be spliced together.
- **Resumable Transfers**: Interrupted downloads (dropped connections, cancelled batches) continue via HTTP `Range` requests — large installers like Docker Desktop never restart from zero.

#### 🖥️ Extended Headless CLI Mode & Automation
- **New `--uninstall <tool1,tool2>`**: Scriptable removal of tools — extracts and binaries are cleaned from `Tools\`, and vendor uninstallers are launched for installer-kind tools, with CI-friendly exit codes.
- **New `--version`**: Print the application version for scripting and diagnostics.
- **Deadlock-Free `--status`**: Tool version probing during status reporting is now fully asynchronous (no sync-over-async blocking).
- Existing commands remain available: `--list`, `--install <id,id,...>`, `--install-bundle <bundleId>`, and `--help`.

#### 📦 Native Tar Archive Extraction
- **`.tar.gz` / `.tgz` / `.tar.bz2` / `.tar.xz` / `.tar` Support**: Tar-based archives are now extracted in-app using the `tar.exe` (bsdtar) bundled with Windows 10 1803+ — `nerdctl`, `kustomize`, and `velero` no longer require manual extraction in Explorer.
- Contained executables are still auto-copied to `Tools\bin` so a single PATH entry covers every extracted CLI.

#### 🔍 Full-Catalog Health Checks
- **79 Mapped CLI Probes (up from ~30)**: Version detection and health status now cover essentially every command-line tool in the 90-tool catalog — previously two-thirds of tools always reported "unhealthy" because their executables had to be guessed.

#### 🧹 Release Engineering & Consistency
- **Centralized Version String**: HTTP User-Agent strings and version displays now derive from the assembly version at runtime — future releases can no longer drift out of sync across services.
- **Version 2.9.0 Everywhere**: Application, Inno Setup, WiX MSI, winget manifests, and release workflows all aligned on the new version.

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
- **Option A (WinGet)**:
  ```powershell
  winget install NotHarshhaa.DevOpsToolsInstaller
  ```
- **Option B (Setup Wizard)**: Download and run `DevOpsToolsInstaller_x64_Setup.exe` to install to `C:\Program Files\DevOpsToolsInstaller` with automated shortcuts and PATH integration.
- **Option C (Windows Installer Package)**: Download and run `DevOpsToolsInstaller_x64.msi` for enterprise GPO, Intune, SCCM, or silent rollouts (`msiexec /i DevOpsToolsInstaller_x64.msi /qn`).
- **Option D (Portable)**: Download `DevOpsToolsInstaller_x64.exe` (or `_arm64.exe`) and run directly — no installation required.

---

### 🖥️ Provision a Workstation from the Terminal
```powershell
# Provision a curated Kubernetes stack in one command
DevOpsToolsInstaller.exe --install-bundle k8s-starter

# Or pick tools individually
DevOpsToolsInstaller.exe --install kubectl,terraform,helm,gh

# Audit what's on the machine
DevOpsToolsInstaller.exe --status

# Clean up
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
$outputDir = [System.IO.Path]::GetDirectoryName((Resolve-Path -Path $OutputFile -ErrorAction SilentlyContinue)?.Path ?? (Join-Path $PWD $OutputFile))
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
[System.IO.File]::WriteAllText($OutputFile, $content, [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated release notes at $OutputFile"
