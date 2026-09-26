<#
.SYNOPSIS
Dumps one empire's planets across JSON saves as CSV, one row per planet per save year.

.DESCRIPTION
Reads every *.json save under saves/ and saves/auto/ (or -Path), keeps the newest file for each
year, and flattens each owned planet into one row: its scalar fields, plus each nested object's
fields as <object>.<field> (industry.chemical, cargo.metals, ...). Objects nested deeper than that
(redirection, resupply) and empty arrays are left out; other arrays are joined with '/'. Column
names come from the save itself, so they follow whatever the save format currently holds.

.EXAMPLE
./scripts/saves.ps1 -Empire 'Dol Parem' | Out-File dolparem.csv

.EXAMPLE
./scripts/saves.ps1 -Empire 'Dol Parem' -Year 4077 -Columns loc,type,population,industry.*,cargo.*,shortfallsLastTick | Format-Table

.EXAMPLE
./scripts/saves.ps1 -Empire 'Dol Parem' -Location 7,8 -AsObject | Format-Table year,population,industry.mining,cargo.metals
#>
param(
    [Parameter(Mandatory)] [string] $Empire,
    [string[]] $Path,
    [int[]] $Year,
    # A world as x,y (quoted or not); omit for every world the empire owns.
    [string[]] $Location,
    # Wildcard patterns for the columns to keep, in order; year and loc are always first.
    [string[]] $Columns,
    # Emit objects instead of CSV text, for piping into Format-Table/Where-Object.
    [switch] $AsObject
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$Location = if ($Location) { ($Location -join ',') -replace '\s', '' }
if (-not $Path) { $Path = @((Join-Path $repoRoot 'saves'), (Join-Path $repoRoot 'saves\auto')) }

# Manual and auto saves often both exist for the same year (and "(1)" copies); keep the newest.
$files = $Path | Where-Object { Test-Path $_ } | ForEach-Object { Get-ChildItem $_ -Filter '*.json' -File }
$byYear = @{}
foreach ($f in $files) {
    $json = Get-Content $f.FullName -Raw | ConvertFrom-Json
    $empireIndex = [array]::IndexOf(@($json.empires.name), $Empire)
    if ($empireIndex -lt 0) { continue }
    if ($Year -and $json.year -notin $Year) { continue }
    $prev = $byYear[$json.year]
    if (-not $prev -or $f.LastWriteTimeUtc -gt $prev.File.LastWriteTimeUtc) {
        $byYear[$json.year] = @{ File = $f; Json = $json; EmpireIndex = $empireIndex }
    }
}
if ($byYear.Count -eq 0) { throw "No saves under $($Path -join ', ') contain an empire named '$Empire'." }

function Format-Value($v) {
    if ($v -is [array]) { return ($v -join '/') }
    return $v
}

$rows = foreach ($y in ($byYear.Keys | Sort-Object)) {
    $entry = $byYear[$y]
    foreach ($p in $entry.Json.galaxy.planets) {
        if ($p.owner -ne $entry.EmpireIndex) { continue }
        $loc = "$($p.location.x),$($p.location.y)"
        if ($Location -and $loc -ne $Location) { continue }

        $row = [ordered]@{ year = $y; loc = $loc }
        foreach ($prop in $p.PSObject.Properties) {
            if ($prop.Name -eq 'location') { continue }
            $v = $prop.Value
            if ($v -is [pscustomobject]) {
                # One level of nesting only: skip objects whose own fields are objects.
                if ($v.PSObject.Properties | Where-Object { $_.Value -is [pscustomobject] }) { continue }
                foreach ($sub in $v.PSObject.Properties) { $row["$($prop.Name).$($sub.Name)"] = Format-Value $sub.Value }
            } elseif ($v -is [array] -and $v.Count -gt 0 -and $v[0] -is [pscustomobject]) {
                continue
            } else {
                $row[$prop.Name] = Format-Value $v
            }
        }
        [pscustomobject]$row
    }
}

if ($Columns) {
    $all = @($rows | Select-Object -First 1 | ForEach-Object { $_.PSObject.Properties.Name })
    $keep = @('year', 'loc') + @(foreach ($pattern in $Columns) { $all | Where-Object { $_ -like $pattern -and $_ -notin 'year', 'loc' } })
    $rows = $rows | Select-Object ($keep | Select-Object -Unique)
}

if ($AsObject) { $rows } else { $rows | ConvertTo-Csv -NoTypeInformation }
