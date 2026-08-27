# Full-build harness strategy (PoC, in progress)

## What this is

A third strategy for the runworld.pas ground-truth harness, sibling to (not a replacement for)
`reference/verify/`'s existing patch-based lane. The prior two strategies -- copy source verbatim,
then eliminate USES clauses by copying referenced code between units -- and the current production
lane (`reference/verify/patches/INTRFACE.PAS.patch` etc: patch each unit down to just what's needed
*right now*) all share a cost: every time a new domain needs one more function, someone re-trims a
patch and re-verifies it compiles. That's real, deliberate, accepted cost for the production lane
(see `reference/verify/README.md`'s "Recommendation" section) -- but it means the *linked surface*
grows one grudging function at a time.

This lane tries the opposite bet: build almost the **entire** pristine `reference/DOSAnacreonSource131`
tree (everything except the real DOS entry point / overlay manager) under fpc once, replacing only
the two things that categorically can't compile or can't mean anything under a modern OS -- inline
8086 assembly, and the DOS-era screen/keyboard I/O layer -- and leave everything else untouched. If
it works, a new domain needing "one more function" just needs that function's unit added to a build
tier, not a fresh multi-day patch-trim-verify cycle.

**The UI is being eliminated, not reproduced.** Early planning considered replacing screen writes
with console lines describing what would have been displayed ("display message box with text
XYZ"). Decided against that (see chat history): the actual goal is a *testable* engine, and the
most that's ever needed from the screen/keyboard layer is `WriteLn`-level visibility, if that. So:
- Non-interactive screen output (window borders, status lines, `WriteString`/`WriteBlanks`/etc.)
  becomes a no-op.
- Genuinely interactive input (waiting for a keypress, a yes/no confirm prompt) becomes a loud
  `WriteLn` + `Halt(1)` instead of either hanging forever or fabricating a fake answer. A build that
  reaches one of these is exercising real interactive UI, which this harness doesn't support --
  finding out immediately beats a silent hang or a silently-wrong forced answer.

## Status as of this writing

This lane lives on branch `fullbuild-poc`, not yet merged to `main`. Check `git log --oneline --
reference/verify/fullbuild/` and `git status` before assuming anything below is current.

All units in `build.ps1`'s tier arrays compile clean from pristine source via `.\build.ps1` (run
from this directory):

- **Tier 0** (no local `USES` clause at all): `INT`, `TYPES`, `REAL1`, `QSORT`, `WNDTYPES`,
  `BATTLE`, `RESOURCE` -- untouched, zero patches. `STRG` needed one (see below).
- **Tier 1** (the screen/keyboard primitive layer): `SYSTEM2`, `EIO`, `WND` -- see "What got patched
  and why" below for what each needed.
- **Tier 2** (application/dialog layer, built on tier1): `MENU` (zero patches -- pure logic built
  entirely on tier1's display primitives: `WriteString`, `ScrollUp`, `ScrollDown`,
  `ActivateWindow`, `OpenWindow`, `CloseWindow`) and `DOS2` (needed a real-mode-pointer patch and a
  new `Printer` shim, see "What got patched and why" below; also `USES Menu`, so it comes second).
- **Tier 3** (core game data model, built on tier2): `GALAXY` (zero patches; `INTERFACE USES
  Types` only, `IMPLEMENTATION USES Dos2` for `ReadVariable`/`WriteVariable`), `CDETYPES`, and
  `NPETYPES` (both zero patches -- pure `TYPE`/`CONST`/`VAR` declaration units with empty
  `IMPLEMENTATION` sections, both `USES Galaxy`).
- **Tier 4** (standalone dialog/utility units, full USES closure satisfied by tier0-3): `TMA`
  (zero patches -- splash-screen/about-box text) and `PULLDOWN` (needed a `{$V-}` patch, see "What
  got patched and why" below).
- **Tier 5** (shared data-model support utilities, a strict dependency chain grouped as one
  tier): `TEXTSTRC`, `DATASTRC`, `DATACNST` (all zero patches), `MISC` (zero patches).
- **Tier 6** (environment/primitive-interrogation layer, a genuine mutual `IMPLEMENTATION USES`
  cycle -- see `uses-map.json`): `PRIMINTR` (needed the same `{$V-}` plus a `MaxAvail` fix already
  established in the sibling `reference/verify/patches/PRIMINTR.PAS.patch`) and `ENVIRON` (zero
  patches). **The cycle compiled with zero special handling** -- fpc auto-recompiled the other
  member the first time either was reached, confirming the "implementation-only cycles just work"
  read in "Dependency map" below.
- **Tier 7** (order-queue and news-ticker data types, no interdependency between the two):
  `ORDERS` (needed the `{$V-}` fix plus one `Move`-based rewrite of a hard type-cast fpc rejects,
  see "What got patched and why") and `NEWS` (needed the same `MaxAvail` fix as `PrimIntr`).
- **Tier 8** (in-game mail/message system, built on tier7's `News`): `MESS` -- zero patches.
- **Tier 9** (the core game-object interface plus its NPE AI layer -- an 11-unit strongly
  connected component: `ATTACK`, `ATTNPE`, `FLEET`, `INTRFACE`, `NPE`, `NPE00`-`NPE04`, `NPEINTR`):
  **pristine `INTRFACE.PAS` (~1700 lines) now compiles in full** -- the actual bet of this whole
  lane, see "Suggested next steps" below for what that means relative to the sibling lane's
  3-procedure stand-in. Needed: `{$V-}` on `INTRFACE` itself (18 string-length mismatches);
  `{$V-}` was tried and found unnecessary on the other ten -- only `NPE00` needed a real fix (see
  "What got patched and why"). The cycle itself needed zero special handling, same as tier6's.
- **Tier 10** (window/dialog units, no interdependency among the seven): `EMPWIND`, `HLPWIND`,
  `NWSWIND`, `STAWIND` (all zero patches), `NMSWIND` and `EDIT` (both needed the `{$V-}` fix).
  `FLTWIND` -- zero patches; this is the window/comm unit originally guessed (wrongly) as the
  natural "next tier" back before the closure computation -- see "Dependency map" below.
- **Tier 11** (core game-logic/data units, also a flat layer with no interdependency among the
  six): `BOMBER`, `DFA`, `LOADSAVE`, `SBASE` (all zero patches), `SCENA` (needed `{$V-}`), and
  `UPDATE` -- zero patches. `UPDATE` is the per-turn game update loop, the same file
  `reference/verify/patches/UPDATE.PAS.patch` trims for the sibling lane's driver -- built here in
  full, same convergence as `INTRFACE` in tier9.
- **Tier 12** (`MAPWIND`, `SWINDOWS`, `DISPLAY` -- a genuine 3-unit strongly connected component
  that `uses-map.json`'s pairwise-only `cycles` list misses entirely; see "Dependency map" below
  for how it was found). `SWINDOWS` and `DISPLAY` each needed the standard `{$V-}` fix. `MAPWIND`
  needed `{$V-}` plus a real fpc-strictness fix: a set constructor with two pairs of duplicate
  literal elements (`HorzChar`/`VertChar` both `#250`, `CrossChar1`/`CrossChar3` both `#196` --
  defined identically at MAPWIND.PAS:38-42) that Turbo Pascal silently accepted but fpc rejects as
  `duplicate set element`; fixed by dropping the duplicates, which yields the identical resulting
  set (see "What got patched and why"). All three cycle members' *other* dependencies were already
  built -- notably `SWINDOWS`'s own implementation-uses of `HlpWind`/`FltWind`/`NwsWind`/`NmsWind`/
  `EmpWind`/`StaWind`, all six already in tier10.
- **Tier 13** (`ATTCOMM`, `CLSCOMM`, `CONSTR`, `DESIGN`, `FLTCOMM`, `MSCCOMM`, `NAMES` -- a flat
  layer, full USES closure satisfied by tier0-12, no interdependency among the seven).
  `CLSCOMM`/`CONSTR`/`DESIGN`/`MSCCOMM`/`NAMES` each needed only the routine `{$V-}` fix.
  `ATTCOMM` and `FLTCOMM` each needed `{$V-}` plus real landmines of their own -- `ATTCOMM`'s
  attack-screen animation wrote combat sprites via `Mem[ScrSeg:...]` at ten separate sites across
  six procedures (same "real-mode segment:offset access, categorically uncompilable" class as
  `WND.PAS`'s border-drawing in tier1), all removed as pure display/SFX; `FLTCOMM` had two more
  `Mem[]` sites (same fix) plus a genuine subrange-comparison landmine in `InputNewDistribution`
  (see "What got patched and why"). `VIEWMAP.PAS` -- structurally in this tier's candidate set --
  is deliberately excluded: it's `PROGRAM ViewMap`, not a unit, and its `LoadGame(Filename,Error)`
  call doesn't match `LOADSAVE.PAS`'s current `LoadGame(FilenameStr):Word` signature (stale dev
  tooling, not an fpc-strictness gap).

Verified working end to end from a clean checkout: `build.ps1` deletes and repopulates `scratch/`
from pristine + `patches/*.patch` + `shims/*.PAS` every run, exactly like `reference/verify/build.ps1`
does for `patched/`.

## Layout

- `build.ps1` -- rebuilds `scratch/` from pristine source, this lane's own `patches/*.patch`, and
  `shims/*.PAS`, then compiles each tier's units standalone (`fpc -Mtp -CfSSE2 <unit>.PAS` per file,
  same flags as `reference/verify/build.ps1` -- see that file's README for why `-CfSSE2` matters).
  `$units` -- one `[string[]]` literal built from nested per-tier `@(...)` arrays (PowerShell
  auto-flattens them into a single list) -- is the actual source of truth for what's in scope, each
  tier's array preceded by a comment explaining why those units share a tier. Each tier is a
  logical dependency layer, not a single file -- add a unit to whichever tier's array matches its
  actual dependency depth (a new unit doesn't automatically get its own tier; only introduce a new
  tier block when a unit genuinely
  needs something later than the last tier provides), and copy any `.INC` it needs (see
  `COLORS.INC`'s handling), rather than hand-editing `scratch/`.
- `patches/*.PAS.patch` -- unified diffs against pristine `reference/DOSAnacreonSource131/`, same
  format and same generation tool as `reference/verify/patches/`. **Always regenerate these via
  `reference/verify/regenerate-patch.ps1`, never hand-write a diff** -- see "Tooling" below for why
  (a hand-rolled `git diff --no-index` was tried once in this lane and produced a patch with the
  wrong path prefix, exactly the failure mode that script exists to prevent).
  `patches/SYSTEM2.PAS.patch` is a byte-for-byte copy of `reference/verify/patches/SYSTEM2.PAS.patch`
  (same pristine file, same inline-asm fix needed) kept local so this lane doesn't reach into its
  sibling's directory at build time.
- `shims/CRT.PAS` -- **not a patch against anything pristine.** There is no `CRT.PAS` in
  `reference/DOSAnacreonSource131` -- Turbo Pascal's `Crt` is Borland's own unit. The real fpc `Crt`
  unit does exist on this machine (`units/i386-win32/rtl-console/crt.ppu`, one `-Fu` flag away from
  the default search path) but was deliberately **not** used: it does real terminal manipulation,
  which is exactly what this lane doesn't want. `shims/CRT.PAS` gives every `Crt` symbol the pristine
  source actually calls a no-op/plain-variable definition instead. Grown on demand -- add a symbol
  the moment fpc reports it missing while compiling a real unit, not speculatively. Its own header
  comment repeats this.
- `shims/PRINTER.PAS` -- same idea, for Borland's `Printer` unit (the one pristine code uses for the
  `Lst` text-file variable). fpc does ship a real `Printer` unit
  (`units/i386-win32/rtl-extra/printer.ppu`) but it isn't on the default unit search path, and this
  lane doesn't want it anyway -- talking to a real printer port is meaningless on a modern OS and out
  of scope for a headless harness. The shim assigns `Lst` to the `NUL` device: a real `Text` file with
  real `IOResult` semantics, so pristine `Write(Lst,...)`/`WriteLn(Lst,...)` calls stay valid, but
  output goes nowhere.
- `scratch/` -- disposable build output (gitignored via the repo root `.gitignore`, same convention
  as `reference/verify/patched/`). Never hand-edit files here as a source of truth; `build.ps1`
  deletes and repopulates it every run. If iterating on a new patch, the workflow is the same as the
  sibling lane's "Restoring a removed procedure": run `build.ps1`, hand-edit the file under `scratch/`,
  confirm it compiles, then run `regenerate-patch.ps1 -PatchedDir fullbuild\scratch -OutDir
  fullbuild\patches -File FILE.PAS` from `reference/verify/`.

## What got patched and why

- **`STRG.PAS`**: `AllUpCase`'s body was a raw 8086 opcode `Inline(...)` block -- replaced with a
  2-line `FOR i:=1 TO Length(Strg) DO Strg[i]:=UpCase(Strg[i]);` loop. Same fix
  `reference/verify/README.md` documents doing for the production lane's own `UPDATE.PAS` path.
  **Grep landmine found while scoping this**: a single-line regex for `INLINE\(` misses this call
  entirely -- the source writes `Inline` and its opening `(` on separate lines. Confirmed 6 files
  actually have `Inline(...)` blocks (`STRG`, `EIO`, `LSORT`, `PROLOG`, `SORT`, `SYSTEM2`) versus only
  2 that a naive single-line grep finds (`EIO`, `SYSTEM2`). Use `multiline: true` (or equivalent) when
  scanning for these, the same lesson as the sibling call-graph tool's own documented `{$IFDEF}` blind
  spot.
- **`SYSTEM2.PAS`**: same class of `Inline(...)` fix, already solved by the production lane -- patch
  copied over rather than re-derived.
- **`EIO.PAS`** -- the actual screen/keyboard I/O primitive layer for the entire ~80-unit codebase:
  - `TurnScreenOn`/`TurnScreenOff` (raw port-$3DA snow-suppression `Inline(...)` toggles) -> no-ops.
  - `WriteString`/`WriteBlanks`/`ScrollUp`/`ScrollDown`/`ScrollLeft`/`ScrollRight` were `EXTERNAL`,
    linked from `FASTSCR.ASM`/`FASTSCR.OBJ` (real assembly, not even Pascal) -> no-op Pascal bodies.
    This is the actual leverage point of the whole strategy: nearly every window/menu/comm unit in
    the codebase only touches the screen through these six routines (plus `WND.PAS`'s border-drawing,
    below) -- patching them once here means every unit built on top needs no UI patch of its own.
  - Unit init block used to do `RealScreen:=Ptr(ScrSeg,0); VirtualScreen:=RealScreen;` -- aliasing a
    pointer directly onto the real `$B800:0` VGA text-mode segment (TP's text mode has no separate
    framebuffer, so real code read/wrote video memory directly with no double-buffering). That
    address means nothing under a modern OS/fpc's flat memory model. Replaced with
    `New(VirtualScreen); RealScreen:=VirtualScreen;` -- a real heap buffer instead.
  - `SaveArea`/`RestoreArea` reconstructed a moving pointer via `Segm:=Seg(Area^); Offs:=Ofs(Area^);
    ...; temp:=Ptr(Segm,Offs)` -- real-mode segment:offset pointer arithmetic. Since `Area` is a plain
    `GetMem` heap pointer under fpc, replaced with direct pointer increments (`temp:=Area; ...
    Inc(temp,Width)`) -- same byte-for-byte copy semantics, just without the segment reconstruction
    that made no sense once `Area` isn't a real-mode pointer.
  - `SetCursor`'s real BIOS `Intr($10,R)` cursor-shape call -> no-op (unsafe/meaningless outside
    real/v86-mode DOS; not just uncompilable, actively wrong to attempt).
  - `GetChoice`/`PressAnyKey` (the two primitives everything else's "wait for a keypress" routes
    through -- `InputString`/`InputPassword`/`EditString` all call `GetChoice` internally, so they
    inherit this for free, no separate patch needed) -> `WriteLn(...); Halt(1);` instead of the real
    `REPEAT ... UNTIL Ch IN LegalSet` / `REPEAT UNTIL Keypressed` loops, which would spin forever
    against `shims/CRT.PAS`'s `ReadKey`/`KeyPressed` stubs (always `#0`/`False`) -- see "No UI" note
    at the top of this file for why halting beats hanging or faking an answer.
- **`WND.PAS`** -- window-stack/border-drawing library built on `EIO`. The real `DrawBorder`
  (and its five nested `Draw*` helpers) drew borders via direct `Mem[ScrSeg:offset] := ...` writes --
  same "raw video memory means nothing here" problem as `EIO.PAS`, found because it doesn't compile
  (`Mem` isn't a real identifier under fpc without a Crt/System equivalent providing it). Rather than
  redirect those writes at the new heap buffer, the **entire window-stack/border-drawing machinery
  was deleted**: confirmed nothing outside `WND.PAS` ever reads `WNDW`/`WindowStack`/`CurWind` or
  inspects a `WindowHandle` for anything but passing it back into this same unit's own procedures --
  it's pure display bookkeeping with no path into game logic. What's left:
  - `ActivateWindow`/`ChangeWindowColor`/`ChangeWindowTitle`/`CloseWindow`/`MessageWindow` -> no-ops.
  - `OpenWindow`'s `VAR WNum` out-param and `ActiveWindow`'s return value -> a fixed constant
    (`FixedWindowHandle = 1`) instead of left undefined -- callers only ever feed the handle back into
    this unit, so *a* stable value is all correctness requires, not a real allocator.
  - `AttentionWindow` (a real interactive confirm/cancel prompt, same category as `GetChoice`) ->
    `WriteLn` both message lines + `Halt(1)`, same treatment as `GetChoice`/`PressAnyKey`.
    `DOSErrorWindow` still computes its real error-message text (the `CASE Error OF ...` table is
    plain string formatting, ported unchanged) and calls `AttentionWindow` exactly as pristine code
    does, so a real I/O error still halts with an informative message instead of silently continuing.
- **`DOS2.PAS`** -- file-path/config/shell-escape helpers. Needed `shims/PRINTER.PAS` (see "Layout"
  above) plus one real landmine in `HomeDirectory`:
  - Pristine code branched on `Lo(DosVersion)>=3` to walk the PSP's raw environment-block bytes via
    `Ptr(PSP^.EnvironSeg,0)` -- real-mode segment:offset addressing, same category as `EIO.PAS`'s
    original screen aliasing. fpc's Windows `Dos` unit doesn't even provide `PrefixSeg` to seed it
    (`Fatal: Identifier not found "PrefixSeg"`), so the unit's init block (`PSP:=Ptr(PrefixSeg,0);`)
    couldn't compile either. Since that branch existed only to work around older DOS versions not
    reliably reporting the current directory through `GetDir` -- a distinction with no meaning under
    a modern OS -- `HomeDirectory` now always takes the `GetDir`-based path pristine code already used
    as its own fallback, and the now-dead `PSP`/`PSPStructure` declarations were removed (confirmed via
    whole-tree grep that nothing outside `DOS2.PAS` ever read `PSP`).
  - Everything else in `DOS2.PAS` -- `DOSShell` (real `Exec`), `DOSSetDeviceBinaryMode` (`Registers`/
    `MSDos` IOCTL call), `DOSCopyFile`/`DOSCopyLine` (`BlockRead`/`BlockWrite`), the `DMS*` directory-menu
    procedures (`FindFirst`/`FindNext`/`FExpand`) -- compiled unpatched: fpc's Windows `Dos` unit
    genuinely implements `Registers`/`MSDos`/`Exec`/the file-search API as compatibility shims, so none
    of it needed touching.
- **`PULLDOWN.PAS`** -- pull-down menu-bar library. `AdjustString`'s `VAR` parameter is declared
  `MaxStr` (`STRING[255]`), but two call sites pass a `String32` (`STRING[32]`) actual parameter --
  a real fixed-string-length mismatch that fpc's default `$V+` rejects (`Error: String types have
  to match exactly in $V+ mode`). The original DOS build's `TPC.CFG` sets `/$V-` project-wide, so
  this never mattered under real Turbo Pascal; same fix and same rationale as
  `reference/verify/patches/PRIMINTR.PAS.patch`'s `{$V-}` (see that lane's README). Added `{$V-}`
  right after `UNIT PullDown;`.
- **`PRIMINTR.PAS`** -- needed exactly the same two fixes already established in
  `reference/verify/patches/PRIMINTR.PAS.patch`, copied over rather than re-derived: `{$V-}` for
  seven string-length mismatches, and `GetNewName`'s `IF MaxAvail>SizeOf(NewSlot) THEN` -> `IF True
  THEN` (`MaxAvail` is TP's real-mode heap-free check; fpc doesn't provide it at all under a
  virtual-memory target, and the "not enough heap" branch it guarded is unreachable regardless).
- **`NEWS.PAS`** -- same `MaxAvail` landmine, same fix, in `GetOpenSlot`'s `IF MaxAvail>20 THEN` ->
  `IF True THEN`.
- **`ORDERS.PAS`** -- needed `{$V-}` (three string-length mismatches in `AllUpCase` calls) plus one
  real landmine: `GetFleetCode` hard-cast a `FleetRecord`'s `OrderData` field
  (`ARRAY[1..6] OF Byte`) directly to `OrderStructure` (`Word`+`MemoryArrayPtr`, also 6 bytes on
  this target) -- a raw byte-layout reinterpretation TP's looser type-cast rules allowed but fpc's
  stricter type checker rejects (`Illegal type conversion`). Replaced with `Move(...,Code,
  SizeOf(Code))`, mirroring `SetFleetCode`'s own existing `Move`-based pattern for the reverse
  direction in the same file -- not a new technique, just applying the file's own established one
  the other way.
- **`NPE00.PAS`** -- one real landmine in `GetClosestCargoWorld`: a local `ResI` was declared
  `ResourceTypes` (the subrange `NoRes..tri`, values 0-18 of the wider `TechnologyTypes` enum), but
  the loop deliberately drives it to `Succ(tri)` (value 19, a real `TechnologyTypes` value one past
  the subrange) as a "scanned every resource type without a match" sentinel -- valid under TP's
  default relaxed subrange checking (`TPC.CFG` sets `/$V-`... `/$R-`) but fpc flags `Succ(tri)` as
  a compile-time range violation regardless of the `$R` directive (confirmed empirically: adding
  `{$R-}` to the unit did not suppress it -- this is a stricter, always-on compile-time constant
  check, not a runtime one). Fixed by widening `ResI`'s declared type to `TechnologyTypes`, the
  full base enum -- every actual array-index use of `ResI` still only happens while `ResI<=tri`, a
  legitimate `ResourceTypes` value, so this doesn't change behavior, only what the compiler is
  willing to let the sentinel value sit in.
- **`INTRFACE.PAS`** -- needed only `{$V-}` (18 string-length mismatches). This is the actual bet
  of the whole lane: pristine `INTRFACE.PAS` (~1700 lines) compiling in full, not the 3-procedure
  stand-in `reference/verify/patches/INTRFACE.PAS.patch` uses instead (see "Suggested next steps").
- **`ATTACK`/`ATTNPE`/`FLEET`/`NPE`/`NPE01`-`NPE04`/`NPEINTR`** -- all compiled unpatched. The
  11-unit strongly-connected component these plus `Intrface`/`NPE00` form (see `uses-map.json`'s
  `cycles`) needed zero special build handling: fpc's own unit loader auto-recompiled whichever
  cycle member wasn't yet built the moment the first one needed it, exactly as predicted for a
  cycle running entirely through `IMPLEMENTATION USES` (never through either side's `INTERFACE
  USES`). This confirms the "Dependency map" section's read on all four originally-flagged cycles,
  not just `Environ`<->`PrimIntr` (tier6, also confirmed working with zero handling).
- **`NMSWIND.PAS`/`EDIT.PAS`/`SCENA.PAS`** -- each needed only the by-now-routine `{$V-}` fix
  (string-length mismatches). `EMPWIND`/`HLPWIND`/`NWSWIND`/`STAWIND`/`FLTWIND` compiled unpatched.
- **`BOMBER.PAS`/`DFA.PAS`/`LOADSAVE.PAS`/`SBASE.PAS`/`UPDATE.PAS`** -- all compiled unpatched.
  `UPDATE.PAS` (~1000+ lines, the per-turn game update loop) and `LOADSAVE.PAS` (the save/load
  system) are both real convergence points with the sibling `reference/verify/` lane, which trims
  `UPDATE.PAS` down to a hand-picked subset for its own driver -- both build here in full with
  zero patches needed.
- **`SWINDOWS.PAS`/`DISPLAY.PAS`** -- each needed only the routine `{$V-}` fix.
- **`MAPWIND.PAS`** -- needed `{$V-}` plus one real landmine: `NameFits`'s `Map2^[y,x1] IN [...]`
  set constructor lists `BlankChar,CrossChar1,CrossChar2,CrossChar3,HorzChar,VertChar,
  NebulaChar[Nebula],NebulaChar[DarkNebula]`, but `HorzChar`/`VertChar` are both literally `#250`
  and `CrossChar1`/`CrossChar3` are both literally `#196` (defined identically at MAPWIND.PAS:38-42
  -- not a typo introduced here, pristine source really does define two names for the same
  character twice). Turbo Pascal silently accepted the resulting duplicate set elements; fpc
  rejects them (`Error: range check error in set constructor or duplicate set element`). Fixed by
  dropping `CrossChar3` and `VertChar` from the literal -- the resulting set value is identical
  either way, so this is a no-op for behavior, purely a compiler-strictness accommodation.
- **`CLSCOMM.PAS`/`CONSTR.PAS`/`DESIGN.PAS`/`MSCCOMM.PAS`/`NAMES.PAS`** -- each needed only the
  routine `{$V-}` fix.
- **`ATTCOMM.PAS`** -- needed `{$V-}` plus removal of ten `Mem[ScrSeg:...]` real-mode
  segment:offset writes across six procedures (`AttReport`'s decorative underline dots; the
  whole bodies of `WarpIn`/`WarpOut`, which existed only to slide a ship icon via `Mem[]` and
  never read or wrote any field of the `GroupArray` passed in; `DrawEnemyShips`/`DrawGroupShips`,
  both pure icon-rendering with no other effect; `GroupsDestroyedSFX`'s and `GroupRetreat`'s
  blink/slide effects, both left the surrounding `AttReport` message calls and control flow
  intact; `DrawGrid`/`DrawStars`, pure background decoration) -- same "real-mode segment:offset
  access means nothing under fpc's flat i386 memory model" class as `WND.PAS`'s border-drawing in
  tier1, not an fpc-strictness gap. Every site was confirmed to have no effect on `Casualties`/
  `Killed`/`Result`/`EndBattle`, all of which `Battle`/`GroupEngage` compute independently.
- **`FLTCOMM.PAS`** -- needed `{$V-}` plus the same two-site `Mem[]` removal as `ATTCOMM`
  (`EraseOldPointer`/`UpdatePointer`'s cursor-highlight writes, both self-documented "WARNING!
  MACHINE SPECIFIC!" in the pristine source) plus one genuine landmine in
  `InputNewDistribution`: `ThgI` is declared `ResourceTypes` (`NoRes..tri`, ordinals 0-18) but
  compared against `SRM` (ordinal 19, the next value in the shared base enum `TechnologyTypes`
  and not a member of `ResourceTypes` at all). Turbo Pascal allowed the cross-subrange comparison
  unchecked; fpc statically range-checks a compared constant against the variable's declared
  subrange and rejects it (`range check error while evaluating constants`). Fixed by comparing
  `Ord(ThgI)<>Ord(SRM)` instead of the enum values directly -- sidesteps the static check without
  changing the comparison's meaning (range checking is off in this build regardless, so `Inc`
  advancing `ThgI` past its declared range was never going to fault at runtime either way).

## Encoding incident: Read/Edit tool corrupted CP437 bytes in two patches (found and fixed)

While patching `PULLDOWN.PAS` in this session, the regenerated patch came back with an extra,
unintended hunk changing box-drawing characters unrelated to the actual edit. Root cause: this
codebase's `.PAS` files are CP437-encoded (DOS-era extended ASCII for box-drawing/graphics
characters, e.g. `WriteString('³',...)`). The AI assistant's Read/Edit tools decode files as
UTF-8; any byte outside the ASCII range that isn't valid UTF-8 gets silently replaced with U+FFFD
on the next Edit-triggered write-back -- and per this incident's evidence, that round-trip
apparently re-serializes the *whole file*, not just the edited region, so damage can land anywhere
in the file, not only near the intended change (confirmed here: the edit was near the top of
`PULLDOWN.PAS`, the corruption showed up ~200 lines later).

**Scope confirmed via `grep`/byte-scan for the `EF BF BD` (U+FFFD) signature across every patch in
this lane** (`reference/verify/fullbuild/patches/*.patch`) and, separately, the sibling
`reference/verify/patches/*.patch` lane: `EIO.PAS.patch` (one corrupted byte, a password-mask
block character in `InputPassword`) and `PULLDOWN.PAS.patch` (from this session's own edit) were
affected; `DOS2.PAS.patch`/`STRG.PAS.patch`/`SYSTEM2.PAS.patch`/`WND.PAS.patch` in this lane and
the entire sibling lane were not (either no non-ASCII bytes touched, or the box-drawing bytes
present survived intact). Both corrupted patches were regenerated from byte-level-corrected
`scratch/` copies (built via a small Python script operating on raw bytes, never through Read/Edit)
and reverified clean.

**Rule going forward**: never use the Read/Edit tools to touch a `.PAS` file that contains, or is
near, a CP437 extended-ASCII byte (box-drawing/graphics characters, byte values 128-255) -- use a
raw-byte script (Python/PowerShell reading/writing bytes directly) for the edit instead, even for
an otherwise-trivial insertion. After *any* Edit touches such a file for any reason, re-scan the
regenerated patch for the `EF BF BD` signature before trusting it.

## Dead code found (excluded, not patched)

`DLIST.PAS`/`SORT.PAS`/`LSORT.PAS` are in `$tier0`'s candidate set structurally (no local `USES`) but
excluded from `build.ps1`'s actual `$tier0` array:
- Confirmed via `grep -i '\bDList\b'` / `\bLSort\b'` / `\bSort\b'` across the whole
  `reference/DOSAnacreonSource131` tree that nothing outside each file's own source references
  `DList`/`Sort`/`LSort` -- dead, unreferenced utility units.
- `DLIST.PAS` is additionally **incomplete**: `AddListElement`'s parameter list is truncated
  mid-declaration (`PROCEDURE AddListElement(VAR List: ListStructure; ElmPtr: Pointer; Pos: ` followed
  immediately by `END.`), and `ListStructure` -- used in `InitializeList`'s own signature -- is never
  declared anywhere in the file. This isn't a UI or asm problem; it's abandoned/unfinished source that
  cannot compile regardless of strategy.
- `QSORT.PAS` (also tier 0, no `Inline`/asm) is genuinely used (`FLTWIND.PAS`, `STAWIND.PAS`) and
  compiles clean untouched -- kept.

## Tooling change: `regenerate-patch.ps1` now takes `-PristineDir`/`-PatchedDir`

`reference/verify/regenerate-patch.ps1` was extended (defaults unchanged, regression-checked against
the real `reference/verify/patched/` lane) so this lane could reuse its byte-exact diff generation
instead of duplicating ~140 lines of careful CRLF/path-prefix handling. Call it from
`reference/verify/` with `-PatchedDir fullbuild\scratch -OutDir fullbuild\patches` to target this
lane. **Known gotcha already fixed**: a relative `-PatchedDir`/`-OutDir`/`-PristineDir` used to
resolve against .NET's stale process working directory rather than PowerShell's `Set-Location`,
silently pointing at the wrong folder -- the script now converts all three to absolute paths via
`GetUnresolvedProviderPathFromPSPath` before using them. If this script is extended further, watch
for the same class of bug: anything that hands a **relative** path straight to a `[System.IO.File]`
static method rather than a PowerShell cmdlet.

## Dependency map: `uses-map.json`

`uses-map.json` is a committed, verified USES-clause map across all ~80 pristine units (plus
`BITCOMP.PAS`, a standalone `PROGRAM` rather than a unit, included for completeness). It replaces
an earlier first-pass map built by a subagent and only partially spot-checked by hand, which had
real inconsistencies (duplicate tier placement, some `IMPLEMENTATION USES` clauses marked "not
shown" because it hadn't finished reading those files) -- that map is gone from history, not just
superseded, since it was never trustworthy enough to keep around.

**How it was built, and why this one's trustworthy**: a fresh regex/read pass over the pristine
source (never `reference/verify/dos_131_callgraph.json`, which has its own documented `{$IFDEF}`
blind spot, see `reference/verify/README.md`), recording each unit's `interfaceUses` and
`implementationUses` **separately** -- the two are frequently different sets (e.g. `Galaxy`:
`INTERFACE USES Types` only, `IMPLEMENTATION USES Dos2`) and a unit's real compile dependency is
the union of both. Two parse artifacts from the first extraction pass (`DFA`/`Misc` each showed a
garbage token in `implementationUses`) were caught and hand-corrected by re-reading those two files
directly before this was committed -- confirmed both actually have no `IMPLEMENTATION USES` clause
at all.

**How to use it for tiering**: a unit is ready for the next tier once every name in both its
`interfaceUses` and `implementationUses` is either already built (in `build.ps1`'s tier arrays) or
a shim/rtl unit (`CRT`, `DOS`, `Printer`). This is a closure computation over the JSON, not
something to eyeball by re-reading source files one at a time -- that one-at-a-time approach is
what led to picking `FltWind`/`MapWind`/`StaWind` as the "next layer" when they actually sit two or
three layers higher (they need `DataStrc`, `DataCnst`, `Misc`, `PrimIntr`, `Environ`, and
`Intrface` first). **Still verify by actually compiling** -- this map says what fpc *should* need,
not a substitute for `build.ps1` actually succeeding.

**Circular dependencies found** (`cycles` in the JSON): `MapWind`<->`SWindows`,
`Artifact`<->`Code`, `Environ`<->`PrimIntr`, `Fleet`<->`Intrface` -- all four run entirely through
`IMPLEMENTATION USES` on both sides, never through either unit's `INTERFACE USES`. Turbo
Pascal/fpc's unit model elaborates interface sections first, and an interface section only needs
its own `INTERFACE USES` satisfied -- so an implementation-only cycle like these is expected to
compile fine. **Confirmed empirically, twice**: `Environ`<->`PrimIntr` (tier6) and the much bigger
`Fleet`<->`Intrface` case (tier9) both compiled with zero special handling -- fpc's own unit loader
auto-recompiled whichever cycle member wasn't built yet the moment the first one needed it. The
earlier claim in this file that `Artifact`<->`Code` would need "joint compilation or an interface
split" was wrong and has been removed; that pair hasn't been reached by a tier yet but the same
mechanism should apply.

**The `cycles` list itself undersold `Fleet`<->`Intrface`**: it only checked direct pairwise
mutual references, so it missed that `Intrface`'s `IMPLEMENTATION USES` reaches `NPE`, whose
`IMPLEMENTATION USES` reaches `NPE01`, whose `INTERFACE USES` reaches back to `Intrface` -- a
3-hop cycle no pairwise check finds. A proper strongly-connected-component computation (Tarjan's
algorithm over the full graph) turned up an **11-unit SCC**: `Attack`, `AttNPE`, `Fleet`,
`Intrface`, `NPE`, `NPE00`-`NPE04`, `NPEIntr` -- all mutually reachable, all compiled together as
tier9 with zero special handling, same result as the simpler pairwise cycles.

**It undersold `MapWind`<->`SWindows` the same way.** The listed pair is real, but the actual SCC
(confirmed by manually tracing each unit's full `USES` set, not by trusting `cycles`) is a 3-unit
chain: `MapWind` implementation-uses `Display`, `Display` interface-uses `SWindows`, `SWindows`
implementation-uses `MapWind` -- so `Display` is part of the cycle even though it has no *direct*
mutual edge with either of the other two. Built as tier12 with zero special handling, same as
tier6 and tier9. **Lesson holds a second time**: don't trust a pairwise `cycles` check alone for a
new suspected cycle -- multi-hop chains through `IMPLEMENTATION USES` (or a mix of interface and
implementation edges, as here) are real and this map's `cycles` field doesn't detect them; only a
manual SCC pass does.

If the pristine source ever changes, regenerate this file with a fresh extraction pass (and
recheck for new parse artifacts by spot-reading a couple of files) rather than hand-editing entries
in place.

## Suggested next steps

**Pristine `INTRFACE.PAS` and `UPDATE.PAS` now both compile in full (tiers 9 and 11)** -- 61 units
total, up from 18. Building `Intrface` was the core bet of this whole lane (see the note above
about the sibling lane's 3-procedure stand-in); `Update.PAS` is the same kind of convergence with
that lane's own driver target. Tiers 5-13 built with only the already-established patch classes
(`{$V-}`, the `MaxAvail` heap-check fix, `Mem[ScrSeg:...]` removal) plus one-off landmines
(`ORDERS.PAS` `GetFleetCode`'s hard type-cast, `NPE00.PAS`'s out-of-subrange sentinel,
`MAPWIND.PAS`'s duplicate set elements, `FLTCOMM.PAS`'s cross-subrange constant comparison) --
see "What got patched and why".

Recomputing the closure against all of tier0-13 (61 units, via a fresh script over
`uses-map.json` rather than eyeballing it -- see "Dependency map" above for why that matters) turns
up exactly **one** newly-ready unit: `PlayTurn` (its full `USES` closure, both interface and
implementation, lands entirely in tier0-13). Everything else is blocked on the still-unconfirmed
`Artifact`<->`Code` cycle: `NewGame`/`Transact` need `Code` directly, `Prolog` needs `NewGame`.
Run a manual SCC pass on `Artifact`<->`Code` before tiering it -- don't trust `cycles` alone, same
lesson as `Fleet`<->`Intrface` and `MapWind`/`SWindows`/`Display` above. `Test`/`Test1` (standalone
`PROGRAM`s, same category as the already-excluded `Compile1`/`ViewMap`) and `Anacreon` itself (the
real DOS entry point) stay excluded per this lane's stated scope.

Whatever's picked: try compiling it standalone against what's already in `scratch/` first (fpc's
own error says exactly what's missing). If it lands at the same dependency depth as an existing
tier, add it to that tier's array; only start a new tier if it genuinely needs something later
than tier3 provides. Run `.\build.ps1`, patch whatever fpc actually complains about (following the
"halt loudly on real interactivity, no-op on pure display" split established above), regenerate
the patch via `regenerate-patch.ps1`, and update this README's "Status" section -- it should
always reflect what's actually in `patches/`+`shims/`+`build.ps1`, not what's aspirational.
