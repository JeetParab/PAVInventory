#Requires -Version 5.1
# One signed EXE: dist\client\PAV.Client.exe
param(
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$rid = "win-x64"
$clientOut = Join-Path $root "dist\client"
$exe = Join-Path $clientOut "PAV.Client.exe"
$cer = Join-Path $clientOut "PAV-CodeSigning.cer"

Write-Host "Publishing single-file PAV.Client.exe..." -ForegroundColor Cyan

if (Test-Path $clientOut) {
    Get-ChildItem $clientOut -Force | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $clientOut | Out-Null

dotnet publish (Join-Path $root "PAV.Client\PAV.Client.csproj") `
    -c $Configuration -r $rid --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:EnableWindowsTargeting=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $clientOut
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Drop leftover publish junk so the folder is exe + cert (+ optional appsettings).
Get-ChildItem $clientOut -File | Where-Object {
    $_.Name -notin @("PAV.Client.exe", "appsettings.json", "PAV-CodeSigning.cer")
} | Remove-Item -Force -ErrorAction SilentlyContinue

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -like "*PAV*" -or $_.Subject -like "*Jeet Parab*" } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating self-signed code-signing certificate..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject "CN=Jeet Parab, O=PAV IT Inventory" `
        -KeyUsage DigitalSignature `
        -FriendlyName "PAV Code Signing" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(5)
}

Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null

$sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256 `
    -TimestampServer "http://timestamp.digicert.com"
if ($sig.Status -ne "Valid" -and $sig.Status -ne "UnknownError") {
    Write-Host "Timestamp failed ($($sig.Status)). Signing without timestamp..." -ForegroundColor Yellow
    $sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -HashAlgorithm SHA256
}
if ($sig.Status -ne "Valid") {
    throw "Signing failed: $($sig.Status) $($sig.StatusMessage)"
}

Write-Host ""
Write-Host "Ready: $exe" -ForegroundColor Green
Write-Host "Signed: $($sig.SignerCertificate.Subject)"
Write-Host "Cert:   $cer"
Write-Host ""
Write-Host "Copy only PAV.Client.exe to the office PC."
Write-Host "First time on that PC, run Trust-PavCert.ps1 as that user (once), then Unblock-File the exe."
