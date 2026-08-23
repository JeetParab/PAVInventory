#Requires -Version 5.1
param(
    [switch] $SelfContained,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$rid = "win-x64"
$clientOut = Join-Path $root "dist\client"

Write-Host "Publishing PAV Inventory ($Configuration, $rid)..." -ForegroundColor Cyan

$sc = if ($SelfContained) { $true } else { $false }

dotnet publish (Join-Path $root "PAV.Client\PAV.Client.csproj") `
    -c $Configuration -r $rid --self-contained $sc `
    -o $clientOut
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Copy-Item (Join-Path $PSScriptRoot "Install-Client.ps1") $clientOut -Force

Write-Host ""
Write-Host "Published: $clientOut" -ForegroundColor Green
Write-Host ""
Write-Host "Copy dist\client to the shared folder, e.g. \\fileserver\IT\PAV"
Write-Host "Then run PAV.Client.exe from that folder."
Write-Host "First sign-in creates inventory.db in the same folder."
