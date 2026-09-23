# =====================================================================
# sign-catalog.ps1 — signs catalog.json and bundles.json with the
# publisher's ECDSA P-256 private key so shipped app versions accept
# them as trusted catalog updates (fail-closed verification).
#
# Usage:
#   .\sign-catalog.ps1
#   .\sign-catalog.ps1 -KeyPath C:\secure\catalog-signing-key.pem
#
# The private key is NOT committed. Keep it in a safe location (GitHub
# Actions secret CATALOG_SIGNING_KEY / local secure storage). Anyone
# holding it can push trusted catalog updates — guard it accordingly.
# =====================================================================
[CmdletBinding()]
param(
    [string]$KeyPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($KeyPath)) {
    $KeyPath = Join-Path $PSScriptRoot "keys\catalog-signing-key.pem"
}

if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
    # Git for Windows ships openssl; try the usual locations.
    $candidates = @(
        "C:\Program Files\Git\usr\bin\openssl.exe",
        "C:\Program Files\Git\mingw64\bin\openssl.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { Set-Alias openssl $c; break }
    }
    if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
        throw "openssl not found on PATH or standard Git locations."
    }
}

if (-not (Test-Path $KeyPath)) {
    throw "Signing key not found at '$KeyPath'. Provide it with -KeyPath."
}

$files = @(
    (Join-Path $PSScriptRoot "catalog.json"),
    (Join-Path $PSScriptRoot "bundles.json")
)

# Files must be committed with exactly the bytes that were signed:
# normalize to UTF-8 (no BOM) + LF line endings before signing.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

foreach ($file in $files) {
    if (-not (Test-Path $file)) { throw "Missing $($file)" }

    $text = [IO.File]::ReadAllText($file)
    $text = $text.Replace("`r`n", "`n")
    [IO.File]::WriteAllText($file, $text, $utf8NoBom)

    $sigPath = "$file.sig"
    $derPath = "$file.sig.der"

    & openssl dgst -sha256 -sign $KeyPath -out $derPath $file
    if ($LASTEXITCODE -ne 0) { throw "openssl failed signing $file" }

    $sig = [Convert]::ToBase64String([IO.File]::ReadAllBytes($derPath))
    [IO.File]::WriteAllText($sigPath, $sig, $utf8NoBom)
    Remove-Item $derPath

    Write-Host "Signed: $file -> $sigPath"
}

Write-Host ""
Write-Host "Commit catalog.json, bundles.json and their .sig files together."
Write-Host "The app will only accept the remote catalog when the .sig matches."
