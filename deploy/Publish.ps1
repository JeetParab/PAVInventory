#Requires -Version 5.1
param(
    [switch] $SelfContained,
    [switch] $SingleFile,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$rid = "win-x64"
$clientOut = Join-Path $root "dist\client"

if (Test-Path $clientOut) {
    Remove-Item $clientOut -Recurse -Force
}

# Single-file implies self-contained runtime
if ($SingleFile) { $SelfContained = $true }

$sc = [bool]$SelfContained
$mode = if ($SingleFile) { "single-file self-contained" }
        elseif ($SelfContained) { "self-contained folder" }
        else { "framework-dependent (needs .NET 8 Desktop Runtime)" }

Write-Host "Publishing PAV Inventory ($Configuration, $rid, $mode)..." -ForegroundColor Cyan

$args = @(
    (Join-Path $root "PAV.Client\PAV.Client.csproj"),
    "-c", $Configuration,
    "-r", $rid,
    "--self-contained", $sc.ToString().ToLowerInvariant(),
    "-o", $clientOut,
    "-p:PublishReadyToRun=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

if ($SingleFile) {
    $args += @(
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    )
}

dotnet publish @args
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Tiny config next to the exe so each PC can point at the share if needed
$cfg = Join-Path $clientOut "appsettings.json"
if (-not (Test-Path $cfg)) {
    Set-Content -Path $cfg -Value "{`n  ""DatabasePath"": """",`n  ""SidebarCollapsed"": false,`n  ""DarkMode"": false,`n  ""FreezeIdentityColumns"": false,`n  ""ColumnOrder"": []`n}`n" -Encoding UTF8
}

if (Test-Path (Join-Path $PSScriptRoot "Install-Client.ps1")) {
    Copy-Item (Join-Path $PSScriptRoot "Install-Client.ps1") $clientOut -Force
}

Write-Host ""
Write-Host "Published: $clientOut" -ForegroundColor Green
Get-ChildItem $clientOut | Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Create a shared folder, e.g. \\fileserver\IT\PAV"
Write-Host "  2. Copy everything from dist\client into that folder"
Write-Host "  3. Unblock:  Get-ChildItem \\fileserver\IT\PAV -Recurse | Unblock-File"
Write-Host "  4. Run PAV.Client.exe  (first person creates the admin account)"
Write-Host "  5. inventory.db + Backups\ are created next to the exe on first run"
