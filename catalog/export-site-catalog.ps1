# =====================================================================
# export-site-catalog.ps1 — publishes the tool catalog to the website.
#
# The marketing site (web/) shows the real catalog: every tool with its
# vendor logo, version and category, plus the curated stacks. This script
# keeps that data in sync with catalog/catalog.json + catalog/bundles.json:
#
#   1. Copies the app's logo SVGs into web/assets/logos/.
#   2. Writes web/assets/catalog.json — a trimmed, site-friendly payload
#      (id, name, category, version, kind, description, logo path) that
#      web/catalog.html renders with vanilla JavaScript.
#
# Run after catalog/refresh-catalog.ps1 (the Catalog Refresh Bot workflow
# does this automatically) or by hand after editing the catalog:
#
#   pwsh -File catalog/export-site-catalog.ps1
# =====================================================================
[CmdletBinding()]
param(
    [string]$CatalogPath = "",
    [string]$BundlesPath = "",
    [string]$LogosPath = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $CatalogPath) { $CatalogPath = Join-Path $repoRoot "catalog\catalog.json" }
if (-not $BundlesPath) { $BundlesPath = Join-Path $repoRoot "catalog\bundles.json" }
if (-not $LogosPath)  { $LogosPath  = Join-Path $repoRoot "src\DevOpsToolsInstaller\Assets\logos" }
if (-not $OutDir)     { $OutDir     = Join-Path $repoRoot "web\assets" }

$logoOutDir = Join-Path $OutDir "logos"
$jsonOut    = Join-Path $OutDir "catalog.json"

# ── 1. Copy logos ───────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $logoOutDir | Out-Null
$copied = 0
foreach ($logo in Get-ChildItem -Path (Join-Path $LogosPath "*") -File |
                 Where-Object { $_.Extension -in @('.svg', '.png') }) {
    Copy-Item -Path $logo.FullName -Destination (Join-Path $logoOutDir $logo.Name) -Force
    $copied++
}
Write-Host "Copied $copied logo(s) to $logoOutDir"

# ── 2. Build the site payload ───────────────────────────────────────
$tools = Get-Content -Path $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$bundles = @()
if (Test-Path $BundlesPath) {
    $bundles = Get-Content -Path $BundlesPath -Raw -Encoding UTF8 | ConvertFrom-Json
}

$siteTools = foreach ($t in $tools) {
    $logo = $null
    $logoUrl = [string]$t.logoUrl
    if ($logoUrl -match 'logos/([^/]+\.(?:svg|png))$') {
        $candidate = Join-Path $logoOutDir $Matches[1]
        if (Test-Path $candidate) { $logo = "assets/logos/$($Matches[1])" }
    }

    [ordered]@{
        id          = [string]$t.id
        name        = [string]$t.name
        category    = [string]$t.category
        version     = [string]$t.version
        kind        = [string]$t.kind
        description = [string]$t.description
        homepage    = [string]$t.homepage
        logo        = $logo
    }
}

$siteStacks = foreach ($b in $bundles) {
    [ordered]@{
        id          = [string]$b.id
        name        = [string]$b.name
        category    = [string]$b.category
        description = [string]$b.description
        tools       = @($b.tools)
    }
}

$categories = @($siteTools | ForEach-Object { $_.category } |
    Where-Object { $_ } | Sort-Object -Unique)

$payload = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    toolCount   = @($siteTools).Count
    stackCount  = @($siteStacks).Count
    categories  = $categories
    tools       = @($siteTools)
    stacks      = @($siteStacks)
}

$json = $payload | ConvertTo-Json -Depth 6
# Match the committed format: UTF-8 without BOM, LF endings.
[IO.File]::WriteAllText($jsonOut, $json.Replace("`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $jsonOut ($(@($siteTools).Count) tools, $(@($siteStacks).Count) stacks, $($categories.Count) categories)"
