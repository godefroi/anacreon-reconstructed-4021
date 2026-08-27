<#
.SYNOPSIS
Standalone-compile smoke test: compiles pristine reference/DOSAnacreonSource131 units under fpc,
one logical dependency layer at a time (not one file at a time -- units at the same dependency
depth share a tier), against this directory's own patches/*.patch and shims/*.PAS -- the same
patch set build.ps1/runworld.pas/PatchHarness.cs build against. Confirms every unit in
uses-map.json still compiles on its own, tier by tier, independent of whatever subset a given
runworld.pas domain actually links.

Originally a separate "build almost everything" PoC lane (patch only what fpc refuses to
compile, eliminate the UI entirely rather than reproducing it -- real interactive calls halt
loudly instead of hanging or faking an answer); folded into this directory's single patch set
once that strategy replaced the old hand-trimmed-per-domain one. See reference/verify/README.md.

scratch/ is the disposable build output (gitignored, like ./patched/): pristine source for these
tiers plus patches/*.patch and shims/*.PAS applied/copied fresh every run. Never a source of
truth.
#>

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$src = Join-Path $PSScriptRoot '..\DOSAnacreonSource131'
$out = Join-Path $PSScriptRoot 'scratch'

[string[]]$units = @(
    # Tier 0: units with no local USES clause at all (verified by reading each file, not the
    # call-graph tool -- see chat history). DLIST.PAS/SORT.PAS/LSORT.PAS are deliberately excluded:
    # confirmed by grep that nothing else in the whole source tree references DList/Sort/LSort, and
    # DLIST.PAS itself is incomplete (AddListElement's parameter list is truncated mid-declaration,
    # references an undeclared ListStructure type) -- dead, unfinished code, not a compile target.
    @('INT.PAS', 'TYPES.PAS', 'REAL1.PAS', 'QSORT.PAS', 'WNDTYPES.PAS', 'BATTLE.PAS', 'RESOURCE.PAS', 'STRG.PAS')

    # Tier 1: the screen/keyboard primitive layer. SYSTEM2.PAS needs the same inline-asm patch as
    # the sibling reference/verify/patches lane (copied here, not referenced there, so this lane
    # stays self-contained). EIO.PAS/WND.PAS are the real leverage point for eliminating the UI:
    # every screen write in the whole ~80-unit codebase funnels through these two units' handful
    # of primitives (WriteString/WriteBlanks/Scroll*, DrawBorder's Mem[] writes) -- patch/no-op
    # them once here rather than per-domain like reference/verify/patches does today.
    @('SYSTEM2.PAS', 'EIO.PAS', 'WND.PAS')

    # Tier 2: the application/dialog layer built on tier1's display primitives. MENU.PAS (zero
    # patches) and DOS2.PAS (needed a landmine patch, see "What got patched and why" in this lane's
    # README, for a real-mode PSP/environment-block walk in HomeDirectory) both sit at the same
    # dependency depth -- DOS2 additionally USES Menu, so it must come second within this tier.
    @('MENU.PAS', 'DOS2.PAS')

    # Tier 3: the core game data model, built on tier2. GALAXY.PAS (INTERFACE USES Types,
    # IMPLEMENTATION USES Dos2 for WriteVariable/ReadVariable) must come first; CDETYPES.PAS and
    # NPETYPES.PAS are pure TYPE/CONST/VAR declaration units (empty IMPLEMENTATION) that both USES
    # Galaxy. None of the three have asm/memory/interrupt landmines of their own.
    @('GALAXY.PAS', 'CDETYPES.PAS', 'NPETYPES.PAS')

    # Tier 4: standalone dialog/utility units whose full USES closure (interface + implementation)
    # is already satisfied by tier0-3 -- found via a whole-tree USES-clause extraction (see chat
    # history) rather than picking a candidate file and discovering its deps one compile at a time.
    # TMA.PAS (splash-screen/about-box text) and PULLDOWN.PAS (pull-down menu-bar library) are the
    # only two genuinely new units the scan surfaced; the rest of its "ready" list was already-built
    # units or the deliberately-excluded dead DList/Sort/LSort trio.
    @('TMA.PAS', 'PULLDOWN.PAS')

    # Tier 5: shared data-model support utilities, built on tier0-4. A strict dependency chain
    # (TextStrc -> DataStrc -> DataCnst -> Misc), not a flat layer, but grouped into one tier since
    # none needs anything from the other tiers below -- found via uses-map.json's closure toward
    # building Intrface (see chat history), not one-at-a-time discovery.
    @('TEXTSTRC.PAS', 'DATASTRC.PAS', 'DATACNST.PAS', 'MISC.PAS')

    # Tier 6: environment/primitive-interrogation layer. Environ and PrimIntr have a genuine mutual
    # IMPLEMENTATION USES cycle (see uses-map.json's "cycles"), which fpc's unit model should handle
    # since neither's INTERFACE section needs the other -- confirmed empirically the first time this
    # tier actually built (see "What got patched and why" if a patch was needed for the cycle itself).
    @('PRIMINTR.PAS', 'ENVIRON.PAS')

    # Tier 7: order-queue and news-ticker data types, built on tier5-6. No interdependency between
    # the two -- same layer, not a chain.
    @('ORDERS.PAS', 'NEWS.PAS')

    # Tier 8: in-game mail/message system, built on tier7's News.
    @('MESS.PAS')

    # Tier 9: the core game-object interface plus its NPE AI layer -- an 11-unit strongly-connected
    # component (Attack/AttNPE/Fleet/Intrface/NPE/NPE00-04/NPEIntr all mutually reference each other,
    # entirely through IMPLEMENTATION/INTERFACE combinations that never form an INTERFACE-side cycle
    # -- see uses-map.json). This is the actual bet of this whole lane: pristine INTRFACE.PAS
    # (~1700 lines) built in full, not the 3-procedure stand-in reference/verify/patches/
    # INTRFACE.PAS.patch uses instead. Order within the array doesn't reflect a real sequence (they're
    # mutually dependent) -- fpc's own auto-recompile-of-missing-units behavior resolves the cycle
    # when the first member is compiled, per this lane's "let fpc's own error be the authority" rule.
    @('ATTACK.PAS', 'ATTNPE.PAS', 'FLEET.PAS', 'INTRFACE.PAS', 'NPE.PAS', 'NPE00.PAS', 'NPE01.PAS', 'NPE02.PAS', 'NPE03.PAS', 'NPE04.PAS', 'NPEINTR.PAS')

    # Tier 10: window/dialog units, full USES closure satisfied by tier0-9 (found via
    # uses-map.json's closure, not one-at-a-time discovery -- see chat history). No
    # interdependency among these six -- a flat layer, not a chain.
    @('EMPWIND.PAS', 'FLTWIND.PAS', 'HLPWIND.PAS', 'NMSWIND.PAS', 'NWSWIND.PAS', 'STAWIND.PAS', 'EDIT.PAS')

    # Tier 11: core game-logic/data units, also satisfied by tier0-9, also a flat layer with no
    # interdependency among themselves. UPDATE.PAS is the per-turn game update loop -- the same
    # file reference/verify/patches/UPDATE.PAS.patch trims for the sibling lane's driver, built
    # here in full. LOADSAVE.PAS is the save/load system.
    @('BOMBER.PAS', 'DFA.PAS', 'LOADSAVE.PAS', 'SBASE.PAS', 'SCENA.PAS', 'UPDATE.PAS')

    # Tier 12: a genuine 3-unit strongly-connected component that uses-map.json's pairwise-only
    # "cycles" list misses entirely (it only lists MapWind<->SWindows as mutual). The real cycle is
    # a 3-hop chain: MapWind implementation-uses Display, Display interface-uses SWindows, SWindows
    # implementation-uses MapWind -- confirmed by manually tracing each unit's full USES set rather
    # than trusting "cycles" (same lesson as tier9's 11-unit SCC). SWindows's other implementation
    # dependencies (HlpWind/FltWind/NwsWind/NmsWind/EmpWind/StaWind) are all already built in tier10.
    @('MAPWIND.PAS', 'SWINDOWS.PAS', 'DISPLAY.PAS')

    # Tier 13: flat layer of "comm" command units, full USES closure satisfied by tier0-12
    # (found via uses-map.json's closure). No interdependency among these seven. VIEWMAP.PAS
    # is deliberately excluded: it's `PROGRAM ViewMap`, not a UNIT (a standalone dev map-viewer
    # tool, same category as Compile1/Test/Test1), and its LoadGame(Filename,Error) call doesn't
    # even match LOADSAVE.PAS's current LoadGame(FilenameStr):Word signature -- stale dev tooling
    # predating a real signature change, not an fpc-strictness gap. Nothing else depends on it.
    @('ATTCOMM.PAS', 'CLSCOMM.PAS', 'CONSTR.PAS', 'DESIGN.PAS', 'FLTCOMM.PAS', 'MSCCOMM.PAS', 'NAMES.PAS')

    # Tier 14: the main-menu/command-dispatch loop, full USES closure satisfied by tier0-13
    # (found via uses-map.json's closure). Single unit, no interdependency to resolve.
    @('PLAYTURN.PAS')

    # Tier 15: the Artifact<->Code cycle (uses-map.json's last remaining unconfirmed cycle),
    # same shape as the already-confirmed Environ<->PrimIntr and Fleet<->Intrface cycles -- a
    # pure mutual IMPLEMENTATION USES with no INTERFACE-side cycle, every other dependency
    # already satisfied by tier0-14. Resolves the same way: fpc auto-recompiles whichever
    # member isn't built yet when the other needs it.
    @('ARTIFACT.PAS', 'CODE.PAS')

    # Tier 16: world-transaction and new-game-setup units, full USES closure satisfied by
    # tier0-15 plus fpc's own standard Dos unit (NewGame's only non-pristine dependency --
    # not part of this source tree, no shim needed, fpc ships a real one under -Mtp).
    @('TRANSACT.PAS', 'NEWGAME.PAS')

    # Tier 17: game-startup/title-screen unit, full USES closure satisfied by tier0-16
    # (found via uses-map.json's closure). Single unit, no interdependency to resolve.
    @('PROLOG.PAS')
)

# CRT.PAS is not pristine source at all -- see shims/CRT.PAS's own header comment for why this
# lane fakes the whole unit instead of pointing fpc at its real (but differently-behaved) Crt.
# PRINTER.PAS is the same idea: fpc does ship a real Printer unit, but it's not on the default
# unit search path and this lane doesn't want real printer-port output anyway -- see
# shims/PRINTER.PAS's own header comment.
$shims = @('CRT.PAS', 'PRINTER.PAS')

# Include files to be copied to the output directory.
$includes = @('COLORS.INC', 'BITPIC.INC')

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null
foreach ($f in $units) { Copy-Item (Join-Path $src $f) $out }
foreach ($f in $shims) { Copy-Item (Join-Path $PSScriptRoot "shims\$f") $out }
foreach ($f in $includes) { Copy-Item (Join-Path $src $f) $out }

$patches = Get-ChildItem (Join-Path $PSScriptRoot 'patches') -Filter '*.patch' -ErrorAction SilentlyContinue | Sort-Object Name
Push-Location $out
try {
    foreach ($p in $patches) {
        git apply -p1 --verbose $p.FullName
    }
    foreach ($f in $units) {
        Write-Host "--- $f ---"
        fpc -Mtp -CfSSE2 $f
    }
} finally {
    Pop-Location
}

Write-Host "Build OK: $($units.Count) units compiled standalone."
