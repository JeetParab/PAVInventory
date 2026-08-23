$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
dotnet run --project "$root\PAV.Client\PAV.Client.csproj"
