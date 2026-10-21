param(
    [string]$Tag = "v3.1.1",
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
This release introduces **Live Version History (install any of the last 5 releases)**, **Microsoft Store Update Support**, **Working Download ETAs**, a **Live Signed Catalog**, and a **Rebuilt Website with the Full Tool Catalog**.

---

### ✨ What's New & Highlights in __TAG__

#### 🕘 Live Version History — Install Older Releases
- **Last 5 GitHub Releases, Live**: the per-tool version picker now queries the vendor's GitHub Releases API and lists the five most recent releases with publish date and pre-release markers.
- **Exact Artifact Matching**: the release asset matching Windows and your CPU architecture (ARM64-aware) is selected and installed with its real download URL and file name, so renamed artifacts no longer 404.
- **Vendor-CDN Coverage**: helm, kind, minikube and dbeaver — whose catalog URLs point at their own CDN — get the same live list through verified GitHub mirrors, and versioned CDNs that only advertise a `latest` channel now resolve to the pinned release.
- **Honest Version Targeting**: vendors that serve a single always-latest link (AWS CLI, aka.ms redirects, Docker Desktop, Postman, VS Code) are now flagged in the picker instead of silently downloading the newest release while claiming to pin an old one.

#### 🛒 Microsoft Store Update Support
- **Store-Aware Update Checks**: Store-installed builds now query the Microsoft Store for updates instead of GitHub Releases — the “Unable to reach GitHub Releases” error is gone for Store installs.
- **In-App Store Update Prompt**: pending package updates surface in the update banner with the Store's own install prompt, falling back to opening the product page.
- **Deadlock-Free Store Queries**: all read-only Store queries run on a dedicated MTA thread, avoiding the known `StoreContext` UI-thread deadlock.

#### 📥 Downloads That Tell The Truth
- **Visible Transfer ETA**: download rows now show a live estimate (`~42 s`) alongside throughput, backed by a proper `HasEta` flag instead of a string-to-bool binding that hid it.
- **Segmented Status Filter & Hydration Rings**: a Fluent segmented control filters the queue, and Profiles, Installed and Stacks show progress rings while data loads.

#### 🔐 A Live, Signed Catalog
- **Correct Remote Branch**: the catalog is fetched from the repository's default branch via `/HEAD/`, so the app finally loads the live, signature-verified catalog instead of falling back to the embedded snapshot.
- **Reliable Catalog Refresh Bot**: the refresh script no longer crashes on tools without version history and now bumps `releases/latest` channels too — the bot's PRs finally carry real version updates.
- **Catalog → Site Export**: `web/assets/catalog.json` and the 90 vendor logos are generated from the signed catalog, so the website can never drift from what the app installs.

#### 🌐 A Rebuilt Website
- **Tool Catalog Page**: all 90 tools with real vendor logos, live versions, category filters, search and the 12 curated stacks.
- **Product Tour**: all 10 application screens as a floating, lazily-loaded WebP gallery, plus a live stats band, an endless logo marquee and an interactive Stack Explorer.

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
