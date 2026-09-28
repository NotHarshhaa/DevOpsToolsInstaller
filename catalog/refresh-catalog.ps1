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

function Get-GitHubRepoFromUrl([string]$url) {
    if ($url -match 'github\.com/([^/]+)/([^/]+)/releases') {
        return @{ Owner = $Matches[1]; Repo = $Matches[2] }
    }
    return $null
}

function Get-ReleaseNotesJson([string]$owner, [string]$repo, [string]$token) {
    $headers = @{
        'User-Agent' = 'DevOpsToolsInstaller-catalog-bot'
        'Accept'     = 'application/vnd.github+json'
    }
    if ($token) { $headers['Authorization'] = "Bearer $token" }

    try {
        return Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$repo/releases/latest" `
            -Headers $headers -TimeoutSec 20
    } catch {
        Write-Warning "  upstream check failed: $($_.Exception.Message)"
        return $null
    }
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
$updated = @()
$stamped = @()
$index = -1

foreach ($tool in $root.AsArray()) {
    $index++
    $id = [string]$tool['id']
    if ($toolFilter.Count -gt 0 -and $id -notin $toolFilter) { continue }

    $url = [string]$tool['downloadUrl']
    $oldVersion = [string]$tool['version']

    # ── 1. Version refresh (GitHub-hosted tools only) ──────────────
    $repo = Get-GitHubRepoFromUrl $url
    if ($repo) {
        $release = Get-ReleaseNotesJson $repo.Owner $repo.Repo $token
        if ($release -and $release.tag_name) {
            $newVersion = ($release.tag_name -replace '^v', '')
            $isOlder = $false
            try {
                if ([version]$newVersion -le [version]$oldVersion) { $isOlder = $true }
            } catch {
                if ($newVersion -eq $oldVersion) { $isOlder = $true }
            }

            if (-not $isOlder -and $newVersion -ne $oldVersion) {
                if ($url.Contains($oldVersion)) {
                    Write-Host "[bump] $id : $oldVersion -> $newVersion"
                    $tool['version'] = $newVersion
                    $tool['downloadUrl'] = $url.Replace($oldVersion, $newVersion)
                    $tool['fileName'] = ([string]$tool['fileName']).Replace($oldVersion, $newVersion)
                    # Clear any stale checksum — the old hash is wrong for the new artifact.
                    $tool['sha256'] = ""
                    $history = @($tool['previousVersions'].AsArray() | ForEach-Object { [string]$_ })
                    $tool['previousVersions'] = @($newVersion) + $history |
                        Select-Object -Unique -First $MaxHistory | ForEach-Object { [System.Text.Json.Nodes.JsonValue]::Create($_) }
                    $updated += "$id ($oldVersion -> $newVersion)"
                } else {
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
