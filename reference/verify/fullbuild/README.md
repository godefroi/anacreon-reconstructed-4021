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

**Nothing in this lane is committed to git yet** -- `git status` on `reference/verify/fullbuild/`
will show it as untracked. Check current state before assuming anything below is stale.

All units in `build.ps1`'s tier arrays compile clean from pristine source via `.\build.ps1` (run
from this directory):

- **Tier 0** (no local `USES` clause at all): `INT`, `TYPES`, `REAL1`, `QSORT`, `WNDTYPES`,
  `BATTLE`, `RESOURCE` -- untouched, zero patches. `STRG` needed one (see below).
- **Tier 1** (the screen/keyboard primitive layer): `SYSTEM2`, `EIO`, `WND` -- see "What got patched
  and why" below for what each needed.
- **Tier 2** (menu system): `MENU` -- zero patches needed. Pure logic built entirely on tier1's
  display primitives (`WriteString`, `ScrollUp`, `ScrollDown`, `ActivateWindow`, `OpenWindow`,
  `CloseWindow`), no assembly or direct memory access.
- **Tier 3** (DOS/file-path/config helpers): `DOS2` -- needed a real-mode-pointer patch and a new
  `Printer` shim, see "What got patched and why" below.

Verified working end to end from a clean checkout: `build.ps1` deletes and repopulates `scratch/`
from pristine + `patches/*.patch` + `shims/*.PAS` every run, exactly like `reference/verify/build.ps1`
does for `patched/`.

## Layout

- `build.ps1` -- rebuilds `scratch/` from pristine source, this lane's own `patches/*.patch`, and
  `shims/*.PAS`, then compiles each tier's units standalone (`fpc -Mtp -CfSSE2 <unit>.PAS` per file,
  same flags as `reference/verify/build.ps1` -- see that file's README for why `-CfSSE2` matters).
  The `$tier0`/`$tier1`/`$tier2`/`$tier3` arrays at the top are the actual source of truth for what's
  in scope; add a unit to the right tier's array (and copy any `.INC` it needs, see `COLORS.INC`'s
  handling) rather than hand-editing `scratch/`.
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

## Dependency map (informal, not committed anywhere else)

A first-pass USES dependency map across all ~80 pristine units was built via a subagent, then
partially spot-checked by hand (the subagent's tiering had some real inconsistencies -- duplicate
tier placement for a few units, some `IMPLEMENTATION USES` clauses marked "not shown" meaning it
didn't finish reading those files -- treat it as a discovery aid, not verified ground truth). Confirmed
facts worth keeping, beyond what's already encoded in `build.ps1`'s tier arrays:

- `WND.PAS` USES `Strg, Int, CRT, EIO`. `EIO.PAS` USES `Int, Strg, System2, DOS, CRT`.
- `CdeTypes`/`NPETypes` (structurally tier-0 candidates, no other local unit dependency) both fail to
  compile standalone with `Fatal: Can't find unit Galaxy` -- they genuinely need `Galaxy` built first.
  Not a bug in either file; just means they're a later tier, not tier 0.
- **Correction to an earlier claim in this file**: `Galaxy`'s `INTERFACE` only `USES Types`, but its
  `IMPLEMENTATION` section separately `USES Dos2` (for `ReadVariable`/`WriteVariable`) -- confirmed by
  reading `GALAXY.PAS` directly. So `Galaxy` is not the cheap one-dependency unit it looked like; it
  needs `Dos2` built first, same as everything else waiting on `Dos2`.
- One circular dependency the subagent flagged and this session did not verify by hand:
  `Artifact` <-> `Code` (mutual `IMPLEMENTATION USES`). Will need joint compilation or an interface
  split whenever this lane reaches that tier -- confirm the cycle is real before spending time on it.
- Since the map's reliability is mixed, the fastest way to extend a tier is still what this lane has
  done twice now: pick a candidate unit, try to compile it standalone against what's already in
  `scratch/`, and let fpc's own error ("can't find unit X" / "identifier not found" / a syntax error)
  say exactly what's missing next -- not the map, and not `reference/verify/dos_131_callgraph.json`
  (which has its own documented `{$IFDEF}` blind spot, see `reference/verify/README.md`).

## Suggested next steps

Pick whichever unblocks what you actually need next, in roughly ascending cost:
- **`Galaxy`** (`INTERFACE USES Types`, `IMPLEMENTATION USES Dos2`) unblocks `CdeTypes`/`NPETypes`,
  both otherwise-clean tier-0 units. `Dos2` is now built, so this should be cheap.
- Window/comm units that depend on `Menu` or `Dos2` (`MapWind`, `FltWind`, `StaWind`, `Display`,
  `SWindows`, ...) should now be unblocked.

Whatever's picked: add it to `build.ps1`'s tier arrays, run `.\build.ps1`, patch whatever fpc actually
complains about (following the "halt loudly on real interactivity, no-op on pure display" split
established above), regenerate the patch via `regenerate-patch.ps1`, and update this README's
"Status" section -- it should always reflect what's actually in `patches/`+`shims/`+`build.ps1`, not
what's aspirational.
