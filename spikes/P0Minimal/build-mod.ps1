$ErrorActionPreference = "Stop"

# Assembles the P0 spike mod folder: manifest + compiled DLL + generated PCK.
# Run from anywhere; paths are resolved relative to this script.

$here = $PSScriptRoot
$root = Split-Path (Split-Path $here -Parent) -Parent
$modDir = Join-Path $here "mod"
$outDir = Join-Path $root "out\mods\P0Minimal"

Write-Host "== building mod assembly ==" -ForegroundColor Cyan
dotnet build (Join-Path $here "P0Minimal.csproj") -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $here "bin\Release\net9.0\P0Minimal.dll"
if (-not (Test-Path $dll)) { throw "assembly not found: $dll" }

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Copy-Item (Join-Path $modDir "P0Minimal.json") $outDir
Copy-Item $dll $outDir

Write-Host "== packing localization ==" -ForegroundColor Cyan
$locRoot = Join-Path $here "loc"
$pck = Join-Path $outDir "P0Minimal.pck"
$pckgen = Join-Path $root "tools\pckgen\bin\Debug\net9.0\pckgen.dll"
dotnet $pckgen $pck $locRoot --prefix P0Minimal
if ($LASTEXITCODE -ne 0) { throw "pckgen failed" }

Write-Host ""
Write-Host "== mod staged at ==" -ForegroundColor Green
Write-Host $outDir
Get-ChildItem -Recurse $outDir | Select-Object -ExpandProperty FullName
