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
