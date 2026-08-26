<#
.SYNOPSIS
Proof of concept for the "build almost everything" harness strategy: compiles pristine
reference/DOSAnacreonSource131 units standalone under fpc, one dependency tier at a time,
patching only what fpc actually refuses to compile and eliminating the UI entirely rather
than reproducing it (see chat history -- no console-description layer, real interactive
calls halt loudly instead).

Unlike reference/verify/build.ps1 (which builds one driver against a deliberately minimal,
hand-trimmed unit subset), this lane's goal is the opposite: build as much of the pristine
tree as will compile. See reference/verify/README.md's sibling note (once this lane graduates
past PoC) for the tradeoff.

scratch/ is the disposable build output (gitignored, like ../patched/): pristine source for
these tiers plus patches/*.patch and shims/*.PAS applied/copied fresh every run. Never a
source of truth.
#>

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$src = Join-Path $PSScriptRoot '..\..\DOSAnacreonSource131'
$out = Join-Path $PSScriptRoot 'scratch'

# Tier 0: units with no local USES clause at all (verified by reading each file, not the
# call-graph tool -- see chat history). DLIST.PAS/SORT.PAS/LSORT.PAS are deliberately excluded:
# confirmed by grep that nothing else in the whole source tree references DList/Sort/LSort, and
# DLIST.PAS itself is incomplete (AddListElement's parameter list is truncated mid-declaration,
# references an undeclared ListStructure type) -- dead, unfinished code, not a compile target.
$tier0 = @('INT.PAS', 'TYPES.PAS', 'REAL1.PAS', 'QSORT.PAS', 'WNDTYPES.PAS', 'BATTLE.PAS', 'RESOURCE.PAS', 'STRG.PAS')

# Tier 1: the screen/keyboard primitive layer. SYSTEM2.PAS needs the same inline-asm patch as
# the sibling reference/verify/patches lane (copied here, not referenced there, so this lane
# stays self-contained). EIO.PAS/WND.PAS are the real leverage point for eliminating the UI:
# every screen write in the whole ~80-unit codebase funnels through these two units' handful
# of primitives (WriteString/WriteBlanks/Scroll*, DrawBorder's Mem[] writes) -- patch/no-op
# them once here rather than per-domain like reference/verify/patches does today.
$tier1 = @('SYSTEM2.PAS', 'EIO.PAS', 'WND.PAS')

# Tier 2: menu system, built on tier1's display primitives
$tier2 = @('MENU.PAS')

# Tier 3: DOS/file-path/config helper library. Needed a landmine patch of its own -- see
# "What got patched and why" in this lane's README -- for a real-mode PSP/environment-block walk
# in HomeDirectory, same class of segment:offset landmine as EIO.PAS's original screen aliasing.
$tier3 = @('DOS2.PAS')

# CRT.PAS is not pristine source at all -- see shims/CRT.PAS's own header comment for why this
# lane fakes the whole unit instead of pointing fpc at its real (but differently-behaved) Crt.
# PRINTER.PAS is the same idea: fpc does ship a real Printer unit, but it's not on the default
# unit search path and this lane doesn't want real printer-port output anyway -- see
# shims/PRINTER.PAS's own header comment.
$shims = @('CRT.PAS', 'PRINTER.PAS')

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
foreach ($f in $tier0 + $tier1 + $tier2 + $tier3) { Copy-Item (Join-Path $src $f) $out }
foreach ($f in $shims) { Copy-Item (Join-Path $PSScriptRoot "shims\$f") $out }
Copy-Item (Join-Path $src 'COLORS.INC') $out

$patches = Get-ChildItem (Join-Path $PSScriptRoot 'patches') -Filter '*.patch' -ErrorAction SilentlyContinue | Sort-Object Name
Push-Location $out
try {
    foreach ($p in $patches) {
        git apply -p1 --verbose $p.FullName
    }
    foreach ($f in $tier0 + $tier1 + $tier2 + $tier3) {
        Write-Host "--- $f ---"
        fpc -Mtp -CfSSE2 $f
    }
} finally {
    Pop-Location
}

Write-Host "Build OK: $($tier0.Count + $tier1.Count + $tier2.Count + $tier3.Count) units compiled standalone."
