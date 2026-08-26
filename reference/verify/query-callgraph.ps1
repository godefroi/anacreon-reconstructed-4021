<#
.SYNOPSIS
Look up one symbol in dos_131_callgraph.json: its definition and every call site,
plus a same-name collision check across the whole source tree.

build-callgraph.ps1's index keys definitions by name only, case-insensitively, with no
per-unit scoping -- a name declared in more than one unit collapses to one arbitrary
definition (whichever ctags line was processed last) with a refCount/reference list
blended across every same-named symbol. This is a real, confirmed risk, not
theoretical: GetBestTarget is two unrelated procedures (ATTNPE.PAS and NPEINTR.PAS),
and NPE00-04.PAS each define their own UpdateFleets/ReviewNews/GetTarget/
GetFleetComposition -- exactly the Phase 6 NPE procedures this tool exists to help
scope. This script greps every *.PAS file for the declaration itself and warns when
more than one file declares the name, so a collision is never silently trusted.

.EXAMPLE
./query-callgraph.ps1 GetBestTarget
#>
param(
    [Parameter(Mandatory)]
    [string]$Name
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$indexFile = Join-Path $PSScriptRoot 'dos_131_callgraph.json'
if (-not (Test-Path $indexFile)) { throw "No $indexFile -- run build-callgraph.ps1 first." }

$index = Get-Content $indexFile -Raw | ConvertFrom-Json
$entry = $index.$Name
if (-not $entry) { throw "'$Name' not found in the call-graph index (check spelling/case)." }

Write-Host "$Name -- $($entry.kind) $($entry.signature)"
Write-Host "  index definition: $($entry.file):$($entry.line)"
Write-Host "  index refCount:   $($entry.refCount)"

# Collision check: does more than one *.PAS file declare a symbol with this name?
$srcDir = Join-Path $PSScriptRoot '..\DOSAnacreonSource131'
$declFiles = Get-ChildItem $srcDir -Filter '*.PAS' |
    Select-String -Pattern "^\s*(PROCEDURE|FUNCTION)\s+$Name\b" -CaseSensitive:$false |
    Select-Object -ExpandProperty Path -Unique |
    ForEach-Object { Split-Path $_ -Leaf }

if ($declFiles.Count -gt 1) {
    Write-Warning "'$Name' is declared in $($declFiles.Count) files ($($declFiles -join ', ')) -- the index merges same-named symbols across units, so the definition/refCount above may belong to a DIFFERENT '$Name' than the one you meant. Read each file's own declaration directly instead of trusting this index for this name."
}

Write-Host ""
Write-Host "References ($($entry.references.Count)):"
foreach ($ref in $entry.references) {
    Write-Host "  $($ref.file):$($ref.line)  $($ref.context)"
}
