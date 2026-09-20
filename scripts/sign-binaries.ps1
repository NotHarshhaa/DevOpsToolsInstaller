param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,

    [string]$Base64Cert,
    [string]$CertPath,
    [string]$Password,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    if (Get-Command signtool.exe -ErrorAction SilentlyContinue) { return 'signtool.exe' }
    $kits = @(
        'C:\Program Files (x86)\Windows Kits\10\bin',
        'C:\Program Files\Windows Kits\10\bin'
    )
    foreach ($kit in $kits) {
        if (Test-Path $kit) {
            $st = Get-ChildItem -Path $kit -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like '*\x64\*' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1
            if ($st) { return $st.FullName }
        }
    }
    return $null
}

$tempPfx = $null
try {
    $pfxFile = $CertPath
    if (-not $pfxFile -and $Base64Cert) {
        $tempPfx = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), [System.Guid]::NewGuid().ToString('N') + '.pfx')
        $certBytes = [System.Convert]::FromBase64String($Base64Cert)
        [System.IO.File]::WriteAllBytes($tempPfx, $certBytes)
        $pfxFile = $tempPfx
    }

    if (-not $pfxFile -or (-not (Test-Path $pfxFile))) {
        Write-Warning "No certificate file provided or found. Skipping code signing."
        exit 0
    }

    $signtool = Find-SignTool

    foreach ($file in $Files) {
        if (-not (Test-Path $file)) {
            Write-Warning "File not found for signing: $file"
            continue
        }

        Write-Host "Signing $file..." -ForegroundColor Cyan

        if ($signtool) {
            $argsList = @(
                'sign',
                '/fd', 'sha256',
                '/tr', $TimestampServer,
                '/td', 'sha256',
                '/f', $pfxFile
            )
            if ($Password) {
                $argsList += @('/p', $Password)
            }
            $argsList += $file

            $proc = Start-Process -FilePath $signtool -ArgumentList $argsList -NoNewWindow -PassThru -Wait
            if ($proc.ExitCode -ne 0) {
                Write-Warning "signtool failed with exit code $($proc.ExitCode). Trying Set-AuthenticodeSignature fallback..."
                $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxFile, $Password)
                Set-AuthenticodeSignature -FilePath $file -Certificate $cert -TimestampServer $TimestampServer -HashAlgorithm SHA256 | Out-Null
            }
        } else {
            Write-Host "signtool.exe not found. Using Set-AuthenticodeSignature..." -ForegroundColor Gray
            $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfxFile, $Password)
            Set-AuthenticodeSignature -FilePath $file -Certificate $cert -TimestampServer $TimestampServer -HashAlgorithm SHA256 | Out-Null
        }

        $sig = Get-AuthenticodeSignature -FilePath $file
        Write-Host "  Status  : $($sig.Status)" -ForegroundColor $(if ($sig.Status -eq 'Valid') { 'Green' } else { 'Yellow' })
        Write-Host "  Signer  : $($sig.SignerCertificate.Subject)" -ForegroundColor Gray
    }
}
finally {
    if ($tempPfx -and (Test-Path $tempPfx)) {
        Remove-Item -Path $tempPfx -Force -ErrorAction SilentlyContinue
    }
}
