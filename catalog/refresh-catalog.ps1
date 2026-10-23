# =====================================================================
# refresh-catalog.ps1 — keeps catalog.json fresh and trustworthy.
#
#   1. VERSION REFRESH (default): for every tool hosted on GitHub
#      Releases, queries the upstream "latest release" API and bumps
#      the catalog entry when a newer version exists. Version strings
#      are replaced literally in downloadUrl/fileName (the catalog
#      pins versions in its URLs), and the superseded version is
#      pushed onto previousVersions for the app's version switcher.
#
#   2. CHECKSUM STAMPING (-StampChanged / -StampMissing): downloads
#      the pinned artifact and writes its SHA-256 into the entry so
#      the app's hash verification actually runs. Entries whose URL
#      has no version token (always-latest redirects) are skipped —
#      a checksum for a moving target would be wrong.
#
# Requires pwsh 7+ (System.Text.Json). Authentication:
#   set GITHUB_TOKEN to raise the GitHub API rate limit.
#
# Usage:
#   .\refresh-catalog.ps1                                  # refresh versions
#   .\refresh-catalog.ps1 -StampChanged                    # + checksums for changed tools
#   .\refresh-catalog.ps1 -StampMissing                    # backfill checksums (one-off)
#   .\refresh-catalog.ps1 -Ids kubectl,terraform -StampChanged
#   .\refresh-catalog.ps1 -Sign                            # re-sign after changes
# =====================================================================
[CmdletBinding()]
param(
    [string]$Ids = "",
    [switch]$StampChanged,
    [switch]$StampMissing,
    [switch]$Sign,
    [int]$MaxHistory = 6,
    [string]$KeyPath = ""
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Please run with pwsh 7+ (System.Text.Json is required)."
}

$catalogPath = Join-Path $PSScriptRoot "catalog.json"
$toolFilter = if ($Ids) { $Ids.Split(',', [System.StringSplitOptions]::TrimEntries) } else { @() }

function Write-Catalog([System.Text.Json.Nodes.JsonNode]$root) {
    # Match the committed format: 2-space indent, UTF-8 no BOM, LF endings.
    $options = [System.Text.Json.JsonSerializerOptions]::new()
    $options.WriteIndented = $true
    # Keep non-ASCII glyphs (iconGlyph) and characters like & unescaped so the
    # rewritten file matches the committed format byte-for-byte when unchanged.
    $options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
    $text = $root.ToJsonString($options).Replace("`r`n", "`n")
    # Restore raw characters for escaped code points (iconGlyph glyphs, &, +, ')
    # so an unchanged catalog rewrites byte-for-byte identical to the committed file.
    $text = [regex]::Replace($text, '(?<!\\)\\u([0-9a-fA-F]{4})', {
        param($m) [char][Convert]::ToInt32($m.Groups[1].Value, 16)
    })
    [IO.File]::WriteAllText($catalogPath, $text, [System.Text.UTF8Encoding]::new($false))
}

function Get-CleanVersion([string]$tag) {
    if ($tag -match '^\d{8}-') { return $tag.Trim() } # e.g. wezterm
    # Strip prefixes before slash or @ (e.g. kustomize/v5.8.3, core@13.3.1)
    $v = $tag -replace '^[a-zA-Z0-9_\-\.]+[/@]', ''
    # Strip leading 'v' or tool name prefix like 'jq-'
    $v = $v -replace '^[a-zA-Z\-]+(?=\d)', ''
    return $v.Trim()
}

function Find-GitHubWindowsAsset($release, [string]$currentFileName, [string]$id) {
    if (-not $release.assets) { return $null }
    
    # Specific per-tool heuristics
    switch ($id) {
        'git' {
            $a = $release.assets | Where-Object { $_.name -match '64-bit\.exe$' -and $_.name -notmatch 'Portable' } | Select-Object -First 1
            if ($a) { return $a }
        }
        'sops' {
            $a = $release.assets | Where-Object { $_.name -match '(?:exe|amd64\.exe)$' -and $_.name -notmatch 'darwin|linux' } | Select-Object -First 1
            if ($a) { return $a }
        }
        'insomnia' {
            $a = $release.assets | Where-Object { $_.name -match '\.exe$' -and $_.name -notmatch 'nsis' } | Select-Object -First 1
            if ($a) { return $a }
        }
        'kustomize' {
            $a = $release.assets | Where-Object { $_.name -match 'windows_amd64\.(?:zip|tar\.gz)$' } | Select-Object -First 1
            if ($a) { return $a }
        }
        'helmfile' {
            $a = $release.assets | Where-Object { $_.name -match 'windows_amd64\.(?:zip|tar\.gz)$' } | Select-Object -First 1
            if ($a) { return $a }
        }
        'eza' {
            $a = $release.assets | Where-Object { $_.name -match 'windows.*gnu\.(?:zip|tar\.gz)$' } | Select-Object -First 1
            if ($a) { return $a }
        }
        'linkerd' {
            $a = $release.assets | Where-Object { $_.name -match 'windows\.exe$|windows-amd64\.exe$' } | Select-Object -First 1
            if ($a) { return $a }
        }
    }

    # General matching for Windows 64-bit binaries / archives
    $nonWindowsOs = 'linux|darwin|openbsd|freebsd|netbsd|android|ios|macos|apple|solaris'
    $nonX64Arch = 'arm64|aarch64|ia32|386|i386|x86\.|armv'
    $metadataExt = '\.blockmap$|\.sha256$|\.sha512$|\.sig$|\.pem$|\.asc$|\.yml$|\.yaml$|\.txt$|\.json$|\.sbom'

    $candidates = @($release.assets | Where-Object {
        $_.name -notmatch $nonWindowsOs -and
        $_.name -notmatch $nonX64Arch -and
        $_.name -notmatch $metadataExt -and
        ($_.name -match 'windows|win64|_win_|win-x64|x86_64-pc-windows|\.exe$|\.msi$' -or
         ($currentFileName -match 'windows' -and $_.name -match 'windows'))
    })

    # If currentFileName had 'windows', candidate must have 'windows' or 'win'
    if ($currentFileName -match 'windows|_win_|win64') {
        $winCandidates = @($candidates | Where-Object { $_.name -match 'windows|_win_|win64|win-x64' })
        if ($winCandidates.Count -gt 0) { $candidates = $winCandidates }
    }

    # Prefer explicit x64/amd64 if multiple
    $x64 = @($candidates | Where-Object { $_.name -match 'amd64|x86_64|x64|win64' })
    if ($x64.Count -gt 0) { $candidates = $x64 }

    # Prefer matching extension (.exe, .zip, .msi, .tar.gz)
    $curExt = if ($currentFileName -match '\.tar\.gz$') { '.tar.gz' } else { [IO.Path]::GetExtension($currentFileName) }
    $matchExt = @($candidates | Where-Object { $_.name.EndsWith($curExt, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($matchExt.Count -gt 0) { $candidates = $matchExt }

    # If still multiple candidates, find the one closest to currentFileName
    if ($candidates.Count -gt 1) {
        if ($currentFileName -match 'setup') {
            $setupMatch = $candidates | Where-Object { $_.name -match 'setup' -and $_.name -notmatch 'airgap' } | Select-Object -First 1
            if ($setupMatch) { return $setupMatch }
        }
        $nonAirgap = @($candidates | Where-Object { $_.name -notmatch 'airgap|offline' })
        if ($nonAirgap.Count -gt 0) { return $nonAirgap[0] }
        return $candidates[0]
    }
    if ($candidates.Count -eq 1) { return $candidates[0] }

    return $null
}

function Get-GitHubRepoFromUrl([string]$url) {
    if ($url -match 'github\.com/([^/]+)/([^/]+)/releases') {
        return @{ Owner = $Matches[1]; Repo = $Matches[2] }
    }
    return $null
}

function Get-ReleaseNotesJson([string]$owner, [string]$repo, [string]$token) {
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        try {
            $json = gh api "/repos/$owner/$repo/releases/latest" 2>$null
            if ($LASTEXITCODE -eq 0 -and $json) {
                return ($json | ConvertFrom-Json)
            }
        } catch {}
    }

    $headers = @{
        'User-Agent' = 'DevOpsToolsInstaller-catalog-bot'
        'Accept'     = 'application/vnd.github+json'
    }
    if ($token) { $headers['Authorization'] = "Bearer $token" }

    try {
        return Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$repo/releases/latest" `
            -Headers $headers -TimeoutSec 20
    } catch {
        Write-Warning "  upstream check failed for $($owner)/$($repo): $($_.Exception.Message)"
        return $null
    }
}

function Set-ToolVersionHistory($tool, [string]$newVersion, [int]$maxHistory) {
    # Tools may have no previousVersions array yet (e.g. doctl) — read it
    # defensively instead of crashing the whole refresh run.
    $history = @()
    if ($null -ne $tool['previousVersions']) {
        $history = @($tool['previousVersions'].AsArray() | ForEach-Object { [string]$_ })
    }
    # Build an explicit JsonArray: PowerShell cannot bind a multi-element
    # object[] to the JsonObject indexer ("Unable to index into an object of
    # type JsonObject"), which silently aborted whole refresh runs.
    $array = [System.Text.Json.Nodes.JsonArray]::new()
    foreach ($v in (@($newVersion) + $history | Select-Object -Unique | Select-Object -First $maxHistory)) {
        [void]$array.Add([System.Text.Json.Nodes.JsonValue]::Create([string]$v))
    }
    $tool['previousVersions'] = $array
}

function Get-ArtifactSha256([string]$url) {
    $tmp = New-Item -ItemType Directory -Force -Path (Join-Path ([IO.Path]::GetTempPath()) "dti-stamp")
    $file = Join-Path $tmp.FullName ([IO.Path]::GetFileName(([uri]$url).LocalPath))
    try {
        Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing -TimeoutSec 600 | Out-Null
        return (Get-FileHash -Path $file -Algorithm SHA256).Hash.ToLower()
    } catch {
        Write-Warning "  checksum stamp failed: $($_.Exception.Message)"
        return $null
    } finally {
        if (Test-Path $file) { Remove-Item $file -Force -ErrorAction SilentlyContinue }
    }
}

$root = [System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText($catalogPath))
$token = $env:GITHUB_TOKEN
if (-not $token) {
    try {
        $ghToken = (gh auth token 2>$null)
        if ($LASTEXITCODE -eq 0 -and $ghToken) { $token = $ghToken.Trim() }
    } catch {}
}
$updated = @()
$stamped = @()
$index = -1

foreach ($tool in $root.AsArray()) {
    $index++
    $id = [string]$tool['id']
    if ($toolFilter.Count -gt 0 -and $id -notin $toolFilter) { continue }

    $url = [string]$tool['downloadUrl']
    $oldVersion = [string]$tool['version']
    $currentFileName = [string]$tool['fileName']

    # ── 1. JetBrains Tools (IntelliJ IDEA, PyCharm) ──────────────
    if ($id -in @('intellij-idea-community', 'pycharm-community')) {
        $code = if ($id -eq 'intellij-idea-community') { 'IIU' } else { 'PCP' }
        $product = if ($id -eq 'intellij-idea-community') { 'idea' } else { 'python' }
        $prefix = if ($id -eq 'intellij-idea-community') { 'idea' } else { 'pycharm' }
        try {
            $jbRes = Invoke-RestMethod -Uri "https://data.services.jetbrains.com/products/releases?code=$code&latest=true&type=release" -TimeoutSec 15
            $rel = $jbRes.$code[0]
            if ($rel -and $rel.version) {
                $newVersion = [string]$rel.version
                if ($newVersion -ne $oldVersion) {
                    Write-Host "[bump-jetbrains] $id : $oldVersion -> $newVersion"
                    $tool['version'] = $newVersion
                    $newFileName = "$prefix-$newVersion.exe"
                    $newUrl = "https://download.jetbrains.com/$product/$newFileName"
                    $tool['downloadUrl'] = $newUrl
                    $tool['fileName'] = $newFileName
                    try {
                        $shaResp = (Invoke-RestMethod -Uri "$newUrl.sha256" -TimeoutSec 10).Split(' ')[0].Trim().ToLower()
                        if ($shaResp.Length -eq 64) {
                            $tool['sha256'] = $shaResp
                            $stamped += $id
                        }
                    } catch {
                        $tool['sha256'] = ""
                    }
                    Set-ToolVersionHistory $tool $newVersion $MaxHistory
                    $updated += "$id ($oldVersion -> $newVersion)"
                }
            }
        } catch {
            Write-Warning "  JetBrains check failed for $($id): $($_.Exception.Message)"
        }
        continue
    }

    # ── 2. HashiCorp Tools (terraform, packer, vault, consul, nomad, vagrant) ──
    if ($url -match 'releases\.hashicorp\.com/([^/]+)/') {
        $hcTool = $Matches[1]
        try {
            $hcRes = Invoke-RestMethod -Uri "https://api.releases.hashicorp.com/v1/releases/$hcTool/latest" -TimeoutSec 15
            if ($hcRes -and $hcRes.version) {
                $newVersion = [string]$hcRes.version
                if ($newVersion -ne $oldVersion) {
                    Write-Host "[bump-hashicorp] $id : $oldVersion -> $newVersion"
                    $tool['version'] = $newVersion
                    $ext = if ($id -eq 'vagrant') { 'msi' } else { 'zip' }
                    $tool['downloadUrl'] = "https://releases.hashicorp.com/$hcTool/$newVersion/${hcTool}_${newVersion}_windows_amd64.$ext"
                    $tool['fileName'] = "${hcTool}_windows_amd64.$ext"
                    try {
                        $sums = Invoke-RestMethod -Uri "https://releases.hashicorp.com/$hcTool/$newVersion/${hcTool}_${newVersion}_SHA256SUMS" -TimeoutSec 10
                        $match = ($sums -split "`n") | Where-Object { $_ -match "windows_amd64\.$ext" }
                        if ($match -match '^([0-9a-fA-F]{64})') {
                            $tool['sha256'] = $Matches[1].ToLower()
                            $stamped += $id
                        }
                    } catch {
                        $tool['sha256'] = ""
                    }
                    Set-ToolVersionHistory $tool $newVersion $MaxHistory
                    $updated += "$id ($oldVersion -> $newVersion)"
                }
            }
        } catch {
            Write-Warning "  HashiCorp check failed for $($id): $($_.Exception.Message)"
        }
        continue
    }

    # ── 3. Kubernetes kubectl ───────────────────────────────────────
    if ($id -eq 'kubectl') {
        try {
            $latestK8s = (Invoke-RestMethod -Uri "https://dl.k8s.io/release/stable.txt" -TimeoutSec 15).Trim()
            $newVersion = $latestK8s -replace '^v', ''
            if ($newVersion -ne $oldVersion) {
                Write-Host "[bump-k8s] kubectl : $oldVersion -> $newVersion"
                $tool['version'] = $newVersion
                $tool['downloadUrl'] = "https://dl.k8s.io/release/$latestK8s/bin/windows/amd64/kubectl.exe"
                try {
                    $sha = (Invoke-RestMethod -Uri "https://dl.k8s.io/release/$latestK8s/bin/windows/amd64/kubectl.exe.sha256" -TimeoutSec 10).Trim().ToLower()
                    if ($sha.Length -eq 64) {
                        $tool['sha256'] = $sha
                        $stamped += $id
                    }
                } catch {
                    $tool['sha256'] = ""
                }
                Set-ToolVersionHistory $tool $newVersion $MaxHistory
                $updated += "$id ($oldVersion -> $newVersion)"
            }
        } catch {
            Write-Warning "  Kubectl check failed: $($_.Exception.Message)"
        }
        continue
    }

    # ── 4. Helm ────────────────────────────────────────────────────
    if ($id -eq 'helm') {
        try {
            $headers = @{
                'User-Agent' = 'DevOpsToolsInstaller-catalog-bot'
                'Accept'     = 'application/vnd.github+json'
            }
            if ($token) { $headers['Authorization'] = "Bearer $token" }
            $helmRel = Invoke-RestMethod -Uri "https://api.github.com/repos/helm/helm/releases/latest" -Headers $headers -TimeoutSec 15
            if ($helmRel -and $helmRel.tag_name) {
                $newVersion = $helmRel.tag_name -replace '^v', ''
                if ($newVersion -ne $oldVersion) {
                    Write-Host "[bump-helm] helm : $oldVersion -> $newVersion"
                    $tool['version'] = $newVersion
                    $tool['downloadUrl'] = "https://get.helm.sh/helm-v$newVersion-windows-amd64.zip"
                    try {
                        $sha = (Invoke-RestMethod -Uri "https://get.helm.sh/helm-v$newVersion-windows-amd64.zip.sha256" -TimeoutSec 10).Trim().ToLower()
                        if ($sha.Length -eq 64) {
                            $tool['sha256'] = $sha
                            $stamped += $id
                        }
                    } catch {
                        $tool['sha256'] = ""
                    }
                    Set-ToolVersionHistory $tool $newVersion $MaxHistory
                    $updated += "$id ($oldVersion -> $newVersion)"
                }
            }
        } catch {
            Write-Warning "  Helm check failed: $($_.Exception.Message)"
        }
        continue
    }

    # ── 5. Version refresh (GitHub-hosted tools) ────────────────────
    $repo = Get-GitHubRepoFromUrl $url
    if ($repo) {
        $release = Get-ReleaseNotesJson $repo.Owner $repo.Repo $token
        if ($release -and $release.tag_name) {
            $newVersion = Get-CleanVersion $release.tag_name
            $isOlder = $false
            try {
                if ([version]$newVersion -le [version]$oldVersion) { $isOlder = $true }
            } catch {
                if ($newVersion -eq $oldVersion) { $isOlder = $true }
            }

            if (-not $isOlder -and $newVersion -ne $oldVersion) {
                # Check if there is an explicit Windows asset in the release
                $winAsset = Find-GitHubWindowsAsset $release $currentFileName $id
                if ($winAsset) {
                    Write-Host "[bump-asset] $id : $oldVersion -> $newVersion ($($winAsset.name))"
                    $tool['version'] = $newVersion
                    $tool['downloadUrl'] = $winAsset.browser_download_url
                    $tool['fileName'] = $winAsset.name
                    $tool['sha256'] = ""
                    Set-ToolVersionHistory $tool $newVersion $MaxHistory
                    $updated += "$id ($oldVersion -> $newVersion)"
                }
                elseif ($url.Contains($oldVersion)) {
                    Write-Host "[bump] $id : $oldVersion -> $newVersion"
                    $tool['version'] = $newVersion
                    $tool['downloadUrl'] = $url.Replace($oldVersion, $newVersion)
                    $tool['fileName'] = ([string]$tool['fileName']).Replace($oldVersion, $newVersion)
                    $tool['sha256'] = ""
                    Set-ToolVersionHistory $tool $newVersion $MaxHistory
                    $updated += "$id ($oldVersion -> $newVersion)"
                }
                elseif ($url -match '/releases/latest/(?:download/)?' -or $url -match 'aka\.ms|dl\.k8s\.io|kind\.sigs\.k8s\.io') {
                    Write-Host "[bump-latest] $id : $oldVersion -> $newVersion (URL serves the latest channel)"
                    $tool['version'] = $newVersion
                    Set-ToolVersionHistory $tool $newVersion $MaxHistory
                    $updated += "$id ($oldVersion -> $newVersion)"
                }
                else {
                    Write-Warning "[skip] $id : newer version $newVersion found but URL does not pin the version — manual update required."
                }
            }
        }
    }

    # ── 2. Checksum stamping ───────────────────────────────────────
    $shouldStamp = $false
    if ($StampChanged -and $updated -like "$id *") { $shouldStamp = $true }
    if ($StampMissing -and [string]::IsNullOrEmpty([string]$tool['sha256'])) { $shouldStamp = $true }

    if ($shouldStamp) {
        $currentUrl = [string]$tool['downloadUrl']
        # Only stamp URLs pinned to a version; always-latest redirects drift.
        if ($currentUrl -match '\d+\.\d+' -and $currentUrl -notmatch 'latest|aka\.ms|/rapid/') {
            Write-Host "[hash] $id : computing SHA-256..."
            $hash = Get-ArtifactSha256 $currentUrl
            if ($hash) {
                $tool['sha256'] = $hash
                $stamped += $id
            }
        } elseif ($StampMissing) {
            Write-Host "[skip] $id : URL is not version-pinned; checksum omitted."
        }
    }
}

Write-Catalog $root

Write-Host ""
if ($updated.Count -gt 0) {
    Write-Host "Updated $($updated.Count) tool(s):"
    $updated | ForEach-Object { Write-Host "  - $_" }
} else {
    Write-Host "Catalog is already up to date."
}
if ($stamped.Count -gt 0) {
    Write-Host "Stamped checksums for $($stamped.Count) tool(s): $($stamped -join ', ')"
}

if (($updated.Count -gt 0 -or $stamped.Count -gt 0) -and $Sign) {
    & (Join-Path $PSScriptRoot "sign-catalog.ps1") -KeyPath $KeyPath
}
