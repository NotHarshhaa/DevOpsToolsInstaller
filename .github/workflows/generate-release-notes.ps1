param(
    [string]$Tag = "v3.1.4",
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
This release brings **Official Windows Package Manager (WinGet) Community Repository integration**, an all-new **Windows Developer Mode Diagnostics & Unprivileged Symlink Suite**, **Expanded CLI Shadowing Detection for Modern Runtimes**, **Dynamic Category Discovery with Dedicated Languages & Runtimes Filtering**, **Command Palette (`Ctrl+K`) Deep-Linking & Smart Actions**, **Robust CLI Health Probes & Version Normalization**, and **Automated WinGet Publishing CI/CD Pipelines**.

---

### ✨ What's New & Highlights in __TAG__

#### 📦 Official WinGet Community Package & Automated CI/CD
- **Live on Windows Package Manager**: DevOps Tools Installer is now officially distributed via the Microsoft WinGet Community Repository (`microsoft/winget-pkgs#438034`). Install or update in a single command:
  ```powershell
  winget install NotHarshhaa.DevOpsToolsInstaller
  # Or shorthand:
  winget install DevOpsToolsInstaller
  ```
- **Automated WinGet Release Pipeline**: Modernized `.github/workflows/winget.yml` using `vedantmgoyal9/winget-releaser@v2` with automated release tag resolution and automatic dispatch on new GitHub releases.

#### 🩺 Workstation Doctor: Developer Mode & Runtime Shadowing
- **Windows Developer Mode Verification**: Workstation Doctor now verifies whether Windows Developer Mode is active (`AllowDevelopmentWithoutDevLicense`), which grants unprivileged symlink creation required by Git, npm, pnpm, cargo, and Terraform tooling. Provides clear instructions and fix commands when disabled.
- **Enhanced CLI Binary Shadowing Detection**: Doctor's shadowing analyzer now actively tracks `pwsh.exe`, `go.exe`, `node.exe`, and `python.exe` across process PATH to alert when conflicting runtimes mask each other (e.g. across Scoop, Chocolatey, WinGet, and AppData).
- **Resilient Version Comparison**: Replaced fragile `System.Version` parsing with a regex-based `CleanVersion` extractor (`\b(\d+(\.\d+){1,3})\b`), preventing version-parsing crashes on multi-segment or prefixed versions (such as `2.56.0.3.windows.1` or `go1.22.5`).

#### 🎛️ Catalog & Command Palette (`Ctrl+K`) Polish
- **Dynamic Category Discovery & Chips**: Catalog category filter chips dynamically discover and generate chips for custom or private feed categories alongside built-in categories. Added the dedicated "Languages & Runtimes" category chip to filter Go, Node.js, Python, PowerShell 7, and Rust.
- **Command Palette Deep-Linking & Smart Actions**:
  - Differentiates between installed, uninstalled, and update-pending tools: installed tools offer "View {Tool} (Installed)" opening their detail pane directly; tools with updates offer "Update {Tool} to v{Version}".
  - Fast utility actions: "Switch Theme (Dark / Light)", "Switch Kubernetes Context (kubectx)", "Run CLI Health Checks", "Fix PATH Duplicates & Dead Entries", and "New-PC Migration Wizard".

#### 🔍 CLI Health Probes & Registry Lifecycle Management
- **Explicit CLI Health Mapping**: Added CLI health probe commands and executables for `golang` (`go.exe`), `nodejs` (`node.exe`), `powershell` (`pwsh.exe`), `python` (`python.exe`), `rust` (`rustc.exe`), `ansible`, `dbeaver`, `postman`, `sublime-text`, `notepad-plus-plus`, `intellij-idea-community`, `pycharm-community`, `visual-studio-community`, and `putty`.
- **Multi-Line & Go Semver Header Parsing**: Upgraded `ExtractVersion` to parse up to 5 multi-line banner lines and recognize `go1.X.Y` prefixes alongside standard semver strings.
- **Expanded Registry Alias Detection**: Extended `UninstallService` aliases for Node.js, Go, Python, PowerShell 7, Rust, IDEs, and container desktops (`docker-desktop`, `podman-desktop`, `rancher-desktop`), resolving install state, installed version, and uninstallation pathways.
- **Semantic Severity Notification Badging**: TitleBar notification flyout icons dynamically resolve theme-aware semantic brushes (`Success`, `Warning`, `Error`, `Update`).

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
