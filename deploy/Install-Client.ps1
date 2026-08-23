#Requires -RunAsAdministrator
param(
    [string] $InstallDir = "${env:ProgramFiles}\PAV Inventory",
    [string] $DatabasePath = ""
)

$ErrorActionPreference = "Stop"

$source = $PSScriptRoot
if (-not (Test-Path (Join-Path $source "PAV.Client.exe"))) {
    throw "Run this script from the published client folder (it must contain PAV.Client.exe)."
}

Write-Host "Installing PAV Inventory to $InstallDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Get-ChildItem $source -File | Where-Object { $_.Extension -notin ".ps1" } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $InstallDir $_.Name) -Force
}

$settings = @{ DatabasePath = $DatabasePath } | ConvertTo-Json
Set-Content -Path (Join-Path $InstallDir "appsettings.json") -Value $settings -Encoding UTF8

$exe = Join-Path $InstallDir "PAV.Client.exe"
$programs = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $programs "PAV Inventory.lnk"
$wshell = New-Object -ComObject WScript.Shell
$lnk = $wshell.CreateShortcut($shortcutPath)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $InstallDir
$lnk.IconLocation = "$exe,0"
$lnk.Description = "PAV Inventory"
$lnk.Save()

Write-Host ""
Write-Host "Client installed." -ForegroundColor Green
if ($DatabasePath) {
    Write-Host "Shared database: $DatabasePath"
} else {
    Write-Host "Database will be inventory.db next to the app, or pick the share on the sign-in screen."
}
Write-Host "Shortcut: Start Menu → PAV Inventory"
