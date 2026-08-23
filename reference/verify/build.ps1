<#
.SYNOPSIS
Rebuilds patched/ from the pristine reference/DOSAnacreonSource131 source plus
patches/*.patch, then compiles the runworld driver.

patched/ is a disposable build product (gitignored, deleted and regenerated
every run) -- the patches are the single source of truth, never whatever
patched/ happens to contain on disk. DOSAnacreonSource131 itself is never
touched.
#>

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$src = Join-Path $PSScriptRoot '..\DOSAnacreonSource131'
$out = Join-Path $PSScriptRoot 'patched'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
Copy-Item (Join-Path $src '*.PAS') $out
Copy-Item (Join-Path $PSScriptRoot 'runworld.pas') $out

$patches = Get-ChildItem (Join-Path $PSScriptRoot 'patches') -Filter '*.patch' | Sort-Object Name
Push-Location $out
try {
    foreach ($p in $patches) {
        git apply -p1 --verbose $p.FullName
    }
    fpc -Mtp runworld.pas
} finally {
    Pop-Location
}

Write-Host "Build OK. Run with: $out\runworld.exe"
