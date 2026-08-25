#Requires -Version 5.1
# Run once per engineer PC so SmartScreen trusts PAV.Client.exe.
# Right-click PowerShell -> Run as the signed-in user (admin not required for CurrentUser stores).
param(
    [string] $CerPath = ""
)

$ErrorActionPreference = "Stop"

if (-not $CerPath) {
    $here = $PSScriptRoot
    $CerPath = Join-Path $here "PAV-CodeSigning.cer"
    if (-not (Test-Path $CerPath)) {
        $CerPath = Join-Path (Split-Path $here) "dist\client\PAV-CodeSigning.cer"
    }
}
if (-not (Test-Path $CerPath)) {
    throw "Certificate not found. Copy PAV-CodeSigning.cer next to this script."
}

$cer = (Resolve-Path $CerPath).Path
Write-Host "Trusting $cer" -ForegroundColor Cyan

Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\Root | Out-Null

$exe = Join-Path (Split-Path $cer) "PAV.Client.exe"
if (Test-Path $exe) { Unblock-File $exe }

Write-Host "Done. Publisher 'Jeet Parab' is trusted for this Windows user." -ForegroundColor Green
Write-Host "If SmartScreen still appears once, click More info -> Run anyway. Later copies of the signed exe should be quieter."
