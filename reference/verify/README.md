# Patch-based Pascal ground truth

Real, only-minimally-patched Turbo Pascal source (`reference/DOSAnacreonSource131/`), compiled
under FreePascal and called directly against a hand-assembled `Universe^`, as ground truth for the
C# port's tests. This is the port's main source of truth for "does the C# code actually do what
the original Pascal did" — read this before touching a patch, adding a ground-truth domain, or
investigating a mismatch between the C# port and the real game.

For the *C# port's own* design decisions (how Pascal's data/behavior gets modeled in C#), see
`docs/PORT_DESIGN.md`. For a map of the Pascal source itself (subsystem by subsystem, dead code,
quirks, bugs), see `docs/PASCAL_ARCHITECTURE_NOTES.md`. This file is about the harness in between:
how the pristine source gets built, what's patched and why, and what each ground-truth domain
covers.

## Layout

- `reference/DOSAnacreonSource131/` — pristine source. Never edited directly; every change to it
  goes through a `.patch` file in `patches/`.
- `patches/*.PAS.patch` — unified diffs against the matching pristine file. This is the maintained
  artifact for every touched unit. See "What gets patched, and why" below for what's in scope.
- `shims/*.PAS` — `CRT.PAS`, `PRINTER.PAS`: stand-ins for two Borland-supplied units that don't
  exist in `reference/DOSAnacreonSource131/` at all (they shipped with Turbo Pascal itself, not
  this project's source). Give every symbol the pristine source actually calls a no-op or plain
  variable — `Crt`'s real fpc equivalent does genuine terminal manipulation, which this harness
  doesn't want; `Printer`'s equivalent talks to a real printer port, meaningless here. Grown on
  demand — add a symbol the moment `fpc` reports it missing, not speculatively.
- `runworld.pas` — the driver program (not a patch target, a genuinely new file): assembles a
  minimal `Universe^` and calls the real Pascal procedure(s) under test. Machine-parseable CLI:
  `case <domain> <case1> <case2> ...`, where `<domain>` selects the case shape/output line — see
  the file's own header comment for the exact field list of every domain. One driver, not one per
  domain, so `PatchHarness.CompileAndRun` only has to copy/patch/compile the whole tree once per
  `dotnet test` run regardless of how many domains use it.
- `runload.pas` — a second, deliberately separate driver (Phase 7g): calls the real, unmodified
  `LOADSAVE.PAS` `LoadGame` against a `.SAV` file on disk and emits a structural checksum of what
  it loaded. Not folded into `runworld.pas` as a new `case <domain>` — `LOADSAVE.PAS`'s own unit
  chain (`Mess`/`News`/`NPETypes`/`NPE`/`Orders`) isn't in `runworld`'s `USES` clause, and adding it
  would run every one of those units' initialization sections before all 20 existing domains too,
  several of which depend on exact `RandSeed`/other global state at startup. See the file's own
  header comment for the checksum's exact field list and why it's sorted by empire name.
- `build.ps1` — deletes and regenerates `patched/` from pristine source + `patches/` + `shims/`,
  then compiles `runworld.pas`. Run it, then run `.\patched\runworld.exe case ...` for manual
  iteration. The C# test suite doesn't shell out to this script — `PatchHarness.cs` (in
  `src/Reconstructed4021.Tests/PascalGroundTruth/`) does the same copy/patch/compile/run
  steps directly, so `GoldenFileTests` can call it like any other harness.
- `build-all-units.ps1` — a separate, broader smoke test: compiles every pristine unit standalone
  under `fpc`, one dependency tier at a time (see `uses-map.json` below), against this same
  `patches/`/`shims/`. Confirms the *whole* tree still links, not just whatever subset
  `runworld.pas` currently calls into. Run it after changing any patch that touches a
  widely-depended-on unit.
- `uses-map.json` — a verified `USES`-clause dependency map across all ~80 pristine units (plus
  `BITCOMP.PAS`, a standalone `PROGRAM`, included for completeness). Records each unit's
  `interfaceUses`/`implementationUses` separately (they're frequently different sets), used to
  compute `build-all-units.ps1`'s tiers and to reason about what a new domain would actually pull
  in before linking a unit. If the pristine source ever changes, regenerate this with a fresh
  extraction pass rather than hand-editing entries.
- `patched/`, `scratch/` — disposable build output (gitignored, deleted and regenerated every
  run), for `build.ps1` and `build-all-units.ps1` respectively. Never a source of truth — never
  hand-edit either as anything but a scratch space for the workflow in "Adding or changing a
  patch" below.
- `regenerate-patch.ps1` — regenerates one `patches/<File>.patch` from a hand-edited
  `patched/<File>` (or any `-PatchedDir`), self-verifying that the result applies cleanly to
  pristine source and reproduces the hand-edited file byte-for-byte before writing it. Pass
  `-OutDir` to write somewhere other than `patches/` to try a regeneration without touching the
  committed patches. **Never hand-write a `.patch` file** — see "Adding or changing a patch" for
  why (a hand-rolled `git diff --no-index` has two real gotchas that repeatedly bit this project
  before this script existed).
- `build-callgraph.ps1` / `query-callgraph.ps1` / `dos_131_callgraph.json` — a call-graph index
  over the pristine source, for deciding what a unit actually needs before touching it. See
  "Call-graph tooling" below.
- `golden/*.golden` — one file per ground-truth domain, each line a `case=Name;key=value;...`
  record. Committed, but treated as a cache: `dotnet test` regenerates every file from a real
  `fpc` run whenever `fpc`/`git` are both on `PATH` (dynamically skipped otherwise, with a clear
  reason), so a test run either verifies fully against live Pascal or visibly skips that coverage
  — never silently trusting a possibly-stale snapshot.

## What gets patched, and why

The guiding rule: **patch as little as possible.** Business logic, formulas, and control flow stay
untouched pristine Pascal; a patch exists only to make a unit compile under `fpc`, or (rarely) to
fix a real Turbo-Pascal-specific runtime assumption `fpc` doesn't honor, or to add narrow,
clearly-marked test-only observability. Every patch falls into one of these categories — knowing
which one a given patch is tells you whether it's safe to ignore, safe to extend, or something to
read carefully:

1. **Categorically uncompilable under a modern OS/compiler — deleted or made a no-op, not
   "ported."** Inline 8086 machine code (`Inline(...)` opcode blocks — e.g. `STRG.PAS`'s
   `AllUpCase`, replaced with a plain `UpCase` loop); raw video-memory writes
   (`Mem[ScrSeg:offset]`, `WND.PAS`'s border-drawing, `ATTCOMM.PAS`'s combat-sprite animation) —
   confirmed to have no effect on any field real game logic reads before removal, never assumed;
   real-mode segment:offset pointer reconstruction (`EIO.PAS`'s original video-segment aliasing,
   `DOS2.PAS`'s PSP environment-block walk); DOS BIOS interrupt calls (`EIO.PAS`'s cursor-shape
   `Intr($10,R)`). This class is never "fixed" to reproduce behavior — the removed code meant
   something only under real-mode DOS, and means nothing (or would be actively wrong) under a flat
   32-bit process.
2. **Genuinely interactive DOS UI — made loud instead of silently wrong.** `GetChoice`/
   `PressAnyKey`/`AttentionWindow` and everything that calls them (`InputString`/`InputPassword`/
   `EditString` inherit this for free) become `WriteLn(...); Halt(1);` instead of either hanging
   forever against a keyboard that never reports a keypress, or fabricating a fake answer. A
   ground-truth run that reaches one of these is exercising real interactive UI this harness
   doesn't support — finding out immediately (a crash with a message) beats a silent hang or a
   silently-wrong forced answer. Purely cosmetic/non-interactive screen output (`WriteString`,
   `ScrollUp`/`ScrollDown`, `OpenWindow`/`CloseWindow`, `ClrScr`) is a plain no-op instead, since
   nothing about *drawing* a border or window has behavior worth preserving.
3. **`fpc`-strictness-only — Turbo Pascal was looser, same behavior either way.** By far the most
   common class, present in most of `patches/`' 33 files:
   - `{$V-}` relaxes `fpc`'s exact fixed-string-length matching (`fpc`'s default `$V+` rejects
     e.g. a `String32` passed where `String16` is declared; the original `TPC.CFG` set `/$V-`
     project-wide, so this never mattered under real Turbo Pascal).
   - `MaxAvail` (Turbo Pascal's real-mode heap-free check, gating some allocation) has no `fpc`
     equivalent under a virtual-memory target — the "not enough heap" branch it guarded is
     unreachable regardless, replaced with `IF True THEN`.
   - A hard type-cast or cross-subrange comparison Turbo Pascal's looser checker allowed but
     `fpc`'s stricter one rejects (`ORDERS.PAS`'s raw byte-layout reinterpretation, replaced with
     the same `Move`-based pattern the file's own reverse-direction case already used;
     `NPE00.PAS`'s sentinel value one past its declared subrange, fixed by widening the variable's
     declared type, not its logic; `FLTCOMM.PAS`'s cross-subrange comparison, fixed by comparing
     `Ord()` values instead of the enum values directly).
   - A `SET` constructor with duplicate literal elements (`MAPWIND.PAS`'s `HorzChar`/`VertChar`
     both literally `#250` — not a typo, pristine source really does define two names for the same
     character — Turbo Pascal accepted the resulting duplicate set elements silently, `fpc`
     doesn't; dropping the duplicate yields an identical resulting set).
4. **Test-only observability hooks — deliberately add behavior, not just enable compilation.**
   Every one of these follows the same convention: a sentinel default value (`-1`, or a `Boolean`
   defaulting false) means "behave exactly like real Pascal," and only a harness that explicitly
   sets it away from that default sees any different behavior at all. None of these change what
   real gameplay does.
   - `ForcedRandomValue: Integer` (`INT.PAS`) — when `>=0`, every `Rnd(Min,Max)` call returns
     `Min+ForcedRandomValue` instead of drawing. Matches the C# side's own `FixedRandom`/
     `RngFixedValue` convention exactly, so results are directly comparable.
   - `GetNewTotalRevIndex(Emp): Integer` (`UPDATE.PAS`) — read-only getter exposing an
     `Empire`-indexed accumulator (`NewTotalRevIndex`) that's normally only committed by
     `UpdateEmpire`, unreachable from `UpdateWorld` alone.
   - `GetLastKilled`/`GetLastCasualties: AttackArray` (`ATTNPE.PAS`) — read-only getters exposing
     `NPEAttack`'s own local `Killed`/`Casualties` values after a call, reset to empty at the top
     of every call so a Con/Gate-target call (which never computes them) can't leak a stale value
     from a previous call in the same process. `NPEAttack`'s real signature is untouched
     deliberately — `NPEINTR.PAS` calls it for real now that the fuller tree links the whole
     11-unit SCC, so widening the signature would break a real caller.
   - `TestNumPlayers: Integer` / `LastFirstWorld`, `LastFirstBase: Word` (`NEWGAME.PAS`) — see
     "The scenario domain" under Domain catalog below for the full mechanism.
   - `UpdateWorld`/`UpdateEmpire`/`UpdateConstruction` (`UPDATE.PAS`) and `LoadScenario`
     (`NEWGAME.PAS`) are promoted from `IMPLEMENTATION`-private to `INTERFACE`-exported —
     visibility only, same procedures, unchanged bodies, so this harness can call them directly
     instead of only through their real (UI-laden or whole-galaxy-loop) callers. Not really a
     "hook," but the same spirit: real behavior, made reachable.
5. **A confirmed real Turbo-Pascal-runtime assumption `fpc` doesn't honor — behavior genuinely
   changes, rare, always investigated empirically first.** Two so far: `DATASTRC.PAS`'s
   `GlobalSets: GlobalSetsRecord ABSOLUTE SetOfActiveFleets` overlay, and `fpc`'s default record
   packing not matching Turbo Pascal's byte-packed layout even under `-Mtp` (`{$PACKRECORDS 1}`,
   added to `DATASTRC.PAS`/`GALAXY.PAS`/`MESS.PAS`/`NEWS.PAS`/`NPETYPES.PAS`/`ORDERS.PAS`/
   `TEXTSTRC.PAS`, Phase 7g). See "Landmines and gotchas" below for the full story — this is the
   one class of patch where "what changed and why" matters
   enough that skimming this list isn't a substitute for reading that section.

## Adding or changing a patch

1. Run `build.ps1` to get a fresh `patched/` tree, then hand-edit the target file directly under
   `patched/`.
2. Compile it directly (`fpc -Mtp -CfSSE2 runworld.pas` from inside `patched/`) and confirm it
   compiles and whatever domain you're touching still runs correctly.
3. Run `./regenerate-patch.ps1 -File FILE.PAS` to regenerate `patches/FILE.PAS.patch` from the
   hand-edited copy. Pass `-OutDir` to try a regeneration without touching the committed patch.
4. Run `build.ps1` fresh (deletes `patched/`, reapplies every patch, recompiles) to confirm the
   whole patch set still applies together and still compiles — step 3's own self-check only
   proves *this* file round-trips in isolation, not that it composes with every other patch. If
   the touched unit is widely depended-on, also run `build-all-units.ps1`.

**A regenerated patch rendering as a big delete/re-add block is not a correctness signal one way
or the other.** Files with a lot of short, generic, repeated Pascal lines (`BEGIN`/`END;`/`WITH
Universe^ DO`) confuse LCS-based diffing on small "kept" islands regardless of diff tool or
algorithm — confirmed once by comparing all four `git diff --diff-algorithm` options plus plain
GNU `diff -u` against `INTRFACE.PAS` and finding the same rendering across every one, not a git
quirk. The correctness that matters is `regenerate-patch.ps1`'s own round-trip check (apply the
patch to pristine, byte-compare against the hand-edited file) — not how the diff happens to
render in a viewer.

**Adding a new ground-truth domain**: give `runworld.pas` a new `case <domain>` branch (document
its field shape in the file's own header comment, matching every existing domain's convention),
add a `GoldenFile.Regenerate(...)` call in `GoldenFileTests.cs`, and if the target procedure isn't
visible outside its unit yet, promote it (see category 4 above) rather than reaching for a new
patch pattern.

## Landmines and gotchas

Cross-cutting lessons, not specific to one domain — read before touching *any* patch.

- **A Pascal `ABSOLUTE` overlay across two separately-declared globals compiles but can lie under
  `fpc`.** `DATASTRC.PAS` used to declare `GlobalSets: GlobalSetsRecord ABSOLUTE
  SetOfActiveFleets` — aliasing a 9-field record onto a var block actually declared in a
  *different* unit (`TYPES.PAS`), relying on Turbo Pascal's contiguous same-unit global layout.
  `fpc` accepted the syntax silently. Confirmed broken via a probe compiled against this repo's
  actual `fpc` (3.2.2, `-CfSSE2`): `SizeOf(GlobalSetsRecord)` came back 792 bytes against the 9
  real vars' actual 777-byte span, and the record's field offsets don't match the real vars'
  addresses at all (`fpc` pads top-level globals and record fields differently) — a real,
  measured mismatch, not a theoretical one. This had two independent symptoms found in this
  project's own history: writing a single field through the overlay once corrupted the real
  `Universe` pointer (a genuine access violation); a whole-record `FillChar` through it left part
  of the last field un-zeroed instead of overrunning into the next global. **Fixed by deleting the
  alias outright** (only two declaration sites and one real use-site referenced it at all) and
  rewriting that one use-site (`LOADSAVE.PAS`'s `InitializeUniverse`) as 9 explicit per-variable
  zeroes — turns any future reintroduction into a compile error instead of silent bad state, the
  right trade for a lane whose whole point is linking real code and not re-deriving what's safe
  every time. If you ever find another `ABSOLUTE` overlay spanning two different units'
  declarations, treat it as guilty until proven innocent the same way — same-unit/same-variable
  overlays (a few exist: `DOS2.PAS`, `PROLOG.PAS`, `QSORT.PAS`) are a fundamentally safer category
  since they don't depend on cross-unit link-order layout.
- **`fpc`'s default record packing doesn't match real Turbo Pascal's byte-packed layout, even
  under `-Mtp`.** `docs/SAV_FILE_FORMAT.md` already confirmed empirically (against real `.SAV`
  bytes, independent of this harness) that Turbo Pascal packs `PlanetRecord` etc. with zero
  inter-field padding. Phase 7g's `.SAV` write-back was the first thing in this harness to ever run
  a real file through the unmodified `LOADSAVE.PAS`'s `LoadGame` (every earlier domain either
  builds `Universe^` in memory directly or, for `scenario`, loads a `.SCN` — never a `.SAV`), and
  it crashed (runtime 216) on the very first real save it tried, then desynced (IOResult 100) once
  that was fixed. `SizeOf(PlanetRecord)` measured 90, not the real 89 — `fpc` was inserting a
  padding byte to word-align a field despite `-Mtp`. Same root cause as the `GlobalSets` overlay
  landmine above (a real TP assumption `fpc` doesn't honor), different mechanism (packing, not
  `ABSOLUTE` aliasing). **Fixed with `{$PACKRECORDS 1}`**, added to every pristine unit that
  declares a record type `LoadGame` reads/writes as a raw byte block (`DATASTRC.PAS`, `GALAXY.PAS`,
  `MESS.PAS`, `NEWS.PAS`, `NPETYPES.PAS`, `ORDERS.PAS`, `TEXTSTRC.PAS`) — confirmed one file at a
  time with a direct `SizeOf()` probe against `docs/SAV_FILE_FORMAT.md`'s own already-verified byte
  counts, not assumed to be fixed just because the crash went away. If a future domain starts
  reading/writing a record type by raw `SizeOf()` block instead of field-by-field, check its actual
  compiled size the same way before trusting it — this class of mismatch produces no compile
  warning and can silently succeed with wrong values instead of erroring, depending on what garbage
  happens to sit in the extra padding. One real, if narrow, side effect of this fix:
  `scenario.golden`'s `Awaken` case's `sumstarbaseeff` changed (89 → 143) — root-caused, not just an
  RNG-stream-position shift (ruled out via a packed-vs-unpacked A/B: same `RandSeed`, same final
  `Pop` on both starbases either way, only one starbase's final `Eff` differs between the two
  builds). The real cause: `AWAKEN.SCN` creates 212 planets against `TYPES.PAS`'s own
  `MaxNoOfPlanets = 200` (confirmed the only golden case that does — the other 10 all sit at or
  under the ceiling) — its last `CreateRandomWorlds` writes 12 planet indices past the end of the
  `Planet` array, and with range checking off (confirmed by grep: no `{$R+}` anywhere in the
  pristine tree) that overrun silently spills into `Starbase` (the very next field in
  `DATASTRC.PAS`'s `UniverseRecord`), corrupting Starbase 1 and 2's leading bytes — both starbases'
  literal `.SCN` `Eff`/`Pop` values are overwritten by the spillover, not just `Eff`. Both
  `PlanetRecord`'s size (89 vs. 90 bytes) and `StarbaseRecord`'s own layout — declared in the same
  file, also repacked — shift under `{$PACKRECORDS 1}`, together changing exactly which spillover
  bytes land where in `Starbase`'s first two slots. Same category of finding as `ScenarioCases.cs`'s
  own `PRINCES.SCN` note (a real reference-scenario defect, not a reconstruction gap) — see
  `docs/PASCAL_ARCHITECTURE_NOTES.md` for the full trace, including why
  `ScenarioLoaderGoldenTests.MatchesGoldenFile` stays green for Awaken despite the overrun reaching
  12 of its planet records too.
- **A hand-assembled `Universe^` is only as faithful as the fields it remembers to set.** Two
  early harness bugs were both "field defaults to zero instead of what `CreateEmpire`/settlement
  actually initializes it to" (an empty `TechnologySet`, an unset `ImpExp` dial) — worth a
  deliberate check against real init code (`CreateEmpire`, `PRIMINTR.PAS`'s `Set*` procedures) for
  any new domain's setup, not just trusting `FillChar` zero to be a reachable state.
- **`InitializeSector` isn't automatic — a domain that reaches `Sector[x]^[y]` for the first time
  needs to call it, and forgetting to is easy to miss until something real starts writing through
  it.** Two ground-truth domains (`npeattack`, `lamattack`) went years without calling
  `InitializeSector` at all, because the code path they exercised (`DestroyFleet`) used to be a
  no-op stand-in that never touched `Sector`. Once the fuller tree made `DestroyFleet` real, both
  crashed with a NIL-pointer access violation the moment it tried to update
  `Sector[FltPos.x]^[FltPos.y].Flts`. If a domain's `Universe^` setup places anything at a
  coordinate, and anything reachable from that domain might touch `Sector` at that coordinate,
  call `InitializeSector` up front — sized to cover every coordinate the setup actually uses, not
  just whatever a copy-pasted `InitializeSector(20)` from another domain happens to cover.
- **A harness that runs multiple cases in one process must track its own cleanup liveness — don't
  assume a resource untouched by an earlier stand-in is still safe to unconditionally free.** Same
  root cause as the `InitializeSector` gap above: `RunNpeAttackCase`/`RunLamAttackCase` used to
  unconditionally `Dispose` the fleets they allocated at the end of each case, safe only because
  the old `DestroyFleet` stand-in never actually freed anything itself. Once `DestroyFleet` became
  real, a case whose fleet was destroyed mid-resolution left its cleanup code double-freeing an
  already-freed pointer — heap corruption that doesn't crash the case that caused it, only
  whichever *later* allocation in the same process trips over the corrupted free-list (in this
  case, the very next case's own `New(Universe)`). Fixed by checking `SetOfActiveFleets` liveness
  before disposing, the same guard `DestroyFleet` itself uses. If a domain's cleanup frees
  anything real Pascal code might also free during the call, guard it the same way.
- **`ForcedRandomValue` must be checked *after* the real degenerate-range clamp, not before.**
  `Rnd`'s real, unconditional behavior returns `Min` outright when `Max<=Min` (pristine source's
  own documented behavior). An earlier ordering had `ForcedRandomValue>=0` win first, so a forced
  value could override that clamp and produce a result outside `[Min,Max]` whenever `Min=Max`
  coincided with a nonzero forced offset — found via a real scenario case
  (`NebulaePatches`' own `Rnd(1,4-Abs(y-InitY))` hits exactly `Min=Max` at a patch's vertical
  extremes). The fix belongs in the *test patch* (`INT.PAS`), not in the C# port's own `Rnd`
  — pristine `Rnd`'s clamp is real, unconditional behavior; `ForcedRandomValue` is a test-only
  device layered on top of it, so the clamp has to win first, matching what the C# port's own
  `PascalMath.Rnd` already did correctly. A tempting-but-wrong alternative fix (make the C# `Rnd`
  call through to the real random generator even when `min==max`, so a forced value would "win"
  the same way the buggy ordering did) breaks index-into-a-statically-sized-array logic the moment
  a shared forced offset exceeds a small collection's `Count` — confirmed empirically (several
  pre-existing tests broke instantly) before reverting in favor of the `INT.PAS` reorder.
- **`-CfSSE2` (the compile flag `build.ps1`/`build-all-units.ps1`/`PatchHarness.cs` all use).**
  `fpc`'s default i386 codegen keeps chained `Real` expressions in the x87 FPU's 80-bit
  extended-precision stack until explicitly stored, while C#'s `double` is always strict 64-bit
  IEEE754 — a borderline expression can round to a different integer in each language.
  `-CfSSE2` forces `fpc` to use strict 64-bit double throughout, aligning the harness with the
  only precision C# has. This is a deliberate baseline choice, not a guarantee that stays true
  forever — a future domain with its own borderline `Real` expression could still get different
  ground truth under it than under `fpc`'s default, so don't assume float-precision issues are
  categorically solved just because this flag is set.
- **Not every field a real Pascal procedure produces is safe to exact-match against a C#-side
  golden-file comparison, even when it looks deterministic.** Once a case chains through several
  real `Rnd()` draws (a genuine `.SCN` file load is the extreme example), *any* single
  `Trunc`/`Round` anywhere upstream landing on a different side of an exact-integer boundary
  changes how many draws that call consumes — desyncing the shared RNG stream for every later
  draw in the same run, even fields that come from an explicit file command rather than a random
  formula (confirmed concretely: a scenario's starbase population desynced from just its 10
  preceding explicit `CreateWorld` commands, well before any actual random placement ran). This
  is not corruption and not fixable by matching floating-point precision — two independently
  written formulas under identical precision can still differ by an ULP. See
  `ScenarioLoaderGoldenTests.cs`'s own doc comment for the full list of fields this affects and
  why only genuinely draw-independent fields are exact-matched there.

## Domain catalog

What each domain calls, what its `Universe^` setup needs to know, and anything non-obvious found
while building it. Grouped by game subsystem; field shapes and output keys live in `runworld.pas`'s
own header comment, not repeated here.

### Economy / annual tick (all via the real `UpdateWorld`, run end to end)

- **`techlevel`** — `UpdateTechLevel`. The original proof of concept for this whole lane.
- **`military`** — `UpdateMilitary`.
- **`starbase`** — `SupplyLink`/`SurplusLink`. The first domain needing more than one world in the
  `Universe^`: these resolve neighbors via `GetObject` (`Sector[x]^[y].Obj`), which needs
  `Galaxy.InitializeSector` plus a direct `Sector[x]^[y].Obj` write for the neighbor planet.
  Covers only `SupplyLink`/`SurplusLink`'s own arithmetic, not the rest of the starbase economy
  pipeline — `AnnualTickHandlerStarbaseTests` covers eligibility filtering/`Kind`-gating/
  Rebellion hardcoded instead, since those have no separate Pascal formula to cross-check.
- **`ambrosia`** — `UseUpAmbrosia`.
- **`revolution`** — `UpdateRevolution`/`Rebellion`. Uses `GetNewTotalRevIndex` (see category 4
  above) to read `NewTotalRevIndex` before and after `UpdateWorld` and report the *difference* —
  that accumulator never resets between cases in one batched CLI invocation (only the never-called
  `UpdateUniverse` zeroes it).
- **`production`** — `ProduceRawMaterial`/`GetIndustrialDistribution`/`UpdateIndustry`/
  `Production`.
- **`empire`** — `UpdateEmpire` (empire-level, not per-world) → `NewTechLevel`/
  `GetChanceForNewTech`/`GetNewTech`. `Empire.Technology` is encoded as a 26-bit mask (bit *i* =
  `TechnologyTypes(i+1)`) since the CLI's field parser only handles plain integers.
- **`construction`** — `UpdateConstruction` (nested `UseUpRawMaterial`) plus
  `ConstructStarbase`/`ConstructStargate`.

### Galaxy / new-game setup

- **`empirecreate`** — `CreateEmpire` directly. Reproduces `CreatePlayerEmpire`/`CreateNPEmpire`'s
  3-line starting-tech-set formula inline rather than pulling in all of `NEWGAME.PAS` for it.
  Fully deterministic, no RNG. 13 cases exhaustively cross-check every row of `TechDev` plus both
  directions of the extra-tech intersect-clamp.
- **`trillumreserves` / `randomplanet` / `nebula`** — `CreateRndPlanet` (calls `CreatePlanet`,
  `INTRFACE.PAS`), `RandomTrillumReserves`, `NebulaeBand`, `NebulaePatches` — all promoted to
  `NEWGAME.PAS`'s `INTERFACE` (self-contained `Universe^`/`Misc`/`DataCnst`/`PrimIntr` logic, no
  interactive-UI chain to worry about). `GetRandomXY`/`CreateRandomWorlds` are deliberately **not**
  covered here: under `ForcedRandomValue`, every `Rnd` call in one invocation returns the same
  fixed offset, so a coordinate blocked on the first roll is blocked on every retry too — there's
  no way to construct a case that reaches "blocked, then a later retry succeeds." Those two
  procedures' own logic is covered by hardcoded `GalaxySetupTests` instead; these three domains
  target only the pieces with real formula risk.
- **`scenario`** — the real `LoadScenario`, a genuine `.SCN` file loaded end to end (not a
  hand-reimplementation of its parsing loop — see below for why that distinction matters). Output
  is an aggregate checksum over the whole loaded `Universe^`, not a per-entity dump — a real
  `dos_131` file has up to ~200 worlds, and a mismatch anywhere perturbs at least one sum. The C#
  side (`ScenarioLoaderGoldenTests.MatchesGoldenFile`) only exact-matches fields with no `Rnd()`
  dependency anywhere in their computation, for the reason explained in Landmines above.

  **`LoadScenario` is called directly**, not reimplemented, via `NEWGAME.PAS`'s `TestNumPlayers`
  test-only override (category 4 above). Two real UI touchpoints needed bypassing:
  `ScenarioIntroduction`'s page-pause/player-count prompt is skipped when `TestNumPlayers>=0`
  (`NumPlayers` comes from the harness's own CLI input instead); `InputEmpireName`'s name/
  gender/password prompts become a deterministic function of its own `NewEmp` parameter
  (`test_player_N`/`test_pass_N`, gender alternating starting male) — no queue needed, since
  `NewEmp` is already the natural per-player index. A scenario file's own `Report "message"`
  command (a real, confirmed-live scenario-authoring feature — `ARRONAX.SCN` has one) still
  consumes its token to keep the file cursor in sync, just doesn't write it in test mode.

  `LastFirstWorld`/`LastFirstBase` (category 4 above) stand in for "how many planets/starbases
  this scenario actually created," because **no live Pascal code tracks that as a Set at all** —
  confirmed by grep: `SetOfActivePlanets`/`SetOfActiveStarbases` are written only by
  `LOADSAVE.PAS`'s `LoadGame`/`SaveGame` (for their own save-file round-trip bookkeeping), and
  nothing else in the whole source tree even *reads* `SetOfActivePlanets` besides a file literally
  named `DEADCODE.PAS`. Real gameplay code instead relies on planets/starbases occupying a
  contiguous range of slots starting at 1 (the same assumption `LastFirstWorld-1`/
  `LastFirstBase-1` encode) — unlike fleets, which get a real tracked set (`SetOfActiveFleets`)
  because fleet slots genuinely get freed and reused mid-game.

  `LoadScenario`'s `Filename` parameter is Pascal's `LineStr` (`STRING[80]`) — this repo's own
  absolute scenario-file path already runs ~96 characters (silent truncation under Pascal's fixed
  string type, not a compile error), so the C# side passes a path relative to the harness's own
  working directory instead.

### Probes

- **`probescout`** — `ProbeScout` directly, against one planet at the probe's destination and one
  at the next ring cell in Pascal's own fixed offset order. Ring ordering/early-exit control flow
  itself has no separate Pascal formula to cross-check, so `VisibilityHandlerProbeTests` covers
  that hardcoded instead.

### Combat

- **`defenses`** — `UpdateDefenses`, via the real `UpdateWorld`. `DefenseType`-indexed
  (`lam`/`def`/`gdm`/`ion`) growth on a world, gated by the same 26-bit `TechnologyBitmask`
  encoding as `empire`.
- **`combat`** — one round of the real `Battle` at the `DpSpc` shell — the group/shell combat
  engine's own per-round damage math, not a multi-round engagement (that's `npeattack`, below).
  This is where the `PascalRound` banker's-rounding bug was found: `Round(2.5)` is `2`, not `3`,
  under real FreePascal — see `docs/PASCAL_ARCHITECTURE_NOTES.md` for the full story.
- **`npeattack`** — the full `NPEAttack` end to end: its own multi-round `FleetRetreats`/
  `Targetting`/`GroupEngage`/`AdvanceGroups` loop, then outcome application
  (`ResolveAttack`/`ConquerWorld`/`ConquerEmpire`/`RestoreCombatant`) — all real, including a real
  `DestroyFleet` (see Landmines above for what that surfaced). The attacker is always Empire1's
  fleet (200 fighters, 200 hunter-killers, plus an optional troop-carrying jumptransport group);
  Empire1's capital sits at (0,0), Empire2's (the usual target) at (50,50), fixed by the domain
  itself so `ConquerEmpire`'s own distance-from-capital math is exercised meaningfully. An
  optional third world lets a case drive any one of `ConquerEmpire`'s four per-planet branches
  deliberately.
- **`lamattack`** — `LAMAttack` directly, not through `NPEAttack` — `LAMAttack` has no `Rnd` call
  anywhere in its body, so there's no combat-engine setup to exercise, only the
  proportional-distribution formula itself (`Round`/`Trunc` against `ProtecNeeded`/`CombatTable`,
  the same arithmetic-risk class that produced the `PascalRound` bug in `combat`). The target is
  always Empire2's (a Fleet or a Planet, whichever the case selects). Two of the same Pascal
  file's other combat-adjacent procedures have no domain here at all: `HolocaustWorld`/
  `HolocaustEffectiveness` are confirmed dead code (see
  `Core/Combat/CombatStandalone.cs`'s own doc comment), and `DestroyConstructionOrGate` — live,
  wired into this port's `NPEAttack` — needs `DestroyConstruction`/`DestroyStargate`
  (`Intrface`), covered by hardcoded C# tests instead (`CombatStandaloneTests.cs`), same
  treatment as `SelfDestructObject` (`SBASE.PAS`, never patched into this harness).

### NPE AI (movement-fidelity prerequisite)

- **`fleetlogistics`** — `FuelCapacity`/`FuelConsumption`/`FleetCargoSpace`/`BalanceFleet`
  (`MISC.PAS`/`INTRFACE.PAS`), called directly against a hand-built `ShipArray`/`CargoArray` — no
  `Universe^` state needed, these are pure functions over their own parameters.
  `FleetCargoSpace`'s own `Round` call is the same arithmetic-risk class that produced the
  `PascalRound` bug in `combat`.
- **`fleetmove`** — `GetNewPos` (`FLEET.PAS`) and `PassingThroughGate`/`PassingThroughFortress`
  (`INTRFACE.PAS`), against one hand-placed fleet. Covers dense-nebula step-blocking and
  stargate/fortress teleport determination — `FleetMovementHandler.GetNewPos`/
  `IsPassingThroughGate`/`IsAtFortress` are `public static` on the C# side specifically so this
  domain (and `FleetMoveTests`) can call them in isolation. `GetNewBasePos`/`XY2Dir` (`SBASE.PAS`,
  starbase obstacle-avoidance) have no domain here yet — `SBase` is never patched into this
  harness; `FleetMovementHandlerTests.cs` covers that hardcoded instead.

### Not a `UpdateWorld`/`GalaxySetup` domain

- **`rng`** — a standing regression fixture for `PascalRandom.cs`, a from-scratch port of `fpc`'s
  actual `Random`/`RandSeed` algorithm. See "A real Pascal RNG" below.

## Call-graph tooling

`build-callgraph.ps1` runs Universal Ctags (needs `ctags` on `PATH`) to find every
procedure/function definition across `reference/DOSAnacreonSource131/*.PAS`, then scans every
source line itself for call sites (ctags/`global` don't extract Pascal references, only
definitions), writing one JSON index, `dos_131_callgraph.json` (gitignored, regenerate on
demand — it's ~1MB and derived entirely from already-committed source, not itself a source of
truth). Format: `{ SymbolName: { file, line, kind, signature, refCount, references: [{file, line,
context}, ...] } }`. Use this before trimming/patching a unit, or before assuming a procedure is
dead, instead of grepping call sites by hand.

**Known limitation, confirmed real, not theoretical**: the index keys definitions by name only,
case-insensitively, with no per-unit scoping — Pascal allows the same procedure/function name in
unrelated units (confirmed: `GetBestTarget` is two different procedures, `ATTNPE.PAS` and
`NPEINTR.PAS`; also `UpdateFleets`/`ReviewNews`/`GetTarget`/`GetFleetComposition`, each redefined
per NPE personality file). For a colliding name, the index keeps only one arbitrary definition and
blends every same-named symbol's call sites into one `references` list — silently wrong if trusted
as-is. **Use `query-callgraph.ps1 <Name>` rather than reading the JSON directly**: it looks up the
same index but also greps the source tree itself for every file declaring that name and warns when
there's more than one, so a collision is never silently trusted.

**Second known limitation, also confirmed real**: the scan has no `{$IFDEF}`/`{$IFNDEF}`
awareness — confirmed 85 conditional-compilation directives across 53 of the source's ~90 files. A
call site inside an excluded region still counts toward `refCount`, so a nonzero count is evidence
of a real call site in the text, not proof it's compiled into any particular build — read the
`{$IFDEF}` context by hand before concluding something is (or isn't) live.

## A real Pascal RNG, not a stand-in: `rng.golden` and `PascalRandom`

Every domain above `rng` needs only one `Rnd()` value per case, so `ForcedRandomValue` (a fixed
offset every call resolves to) has always been enough. `nebula`'s multi-patch cases and a genuine
end-to-end `.SCN` load (`scenario`) both break that: they retry/redraw multiple times per run, and
a fixed offset always re-rolls the *same* value, so placing a second world in a zone that already
has one always blows through the retry cap on both sides. Comparing real multi-draw sequences
needs matching this project's actual `fpc` runtime's `Random`/`RandSeed` algorithm, not a fixed
stand-in.

That algorithm isn't the classic Turbo Pascal LCG a DOS-era codebase might suggest, and guessing at
it from memory would have been exactly the kind of unverified recall this project avoids: `fpc`'s
RNG implementation changed over its history, and which one a given installed compiler uses has to
be checked, not assumed. A quick probe program (`RandSeed:=12345; WriteLn(Random(100));` a few
times) compiled with this repo's actual installed `fpc` (3.2.2) and compared against candidate
algorithms pulled from `fpc`'s own RTL source at matching tags settled it empirically: `fpc`'s
`main`/trunk source now uses a SplitMix64-seeded Xoshiro128** generator (didn't match); the
`release_3_2_2` tag's `rtl/inc/system.inc` uses a Mersenne Twister (MT19937) variant with its own
reseed/tempering convention — matched exactly, including a mid-run reseed, a fresh-seed replay,
and a draw crossing the generator's 624-word internal state refill.

`src/Reconstructed4021.Tests/PascalRandom.cs` is a from-scratch `System.Random` subclass
porting that exact algorithm, test-only (production code has no need for Pascal-bit-exact
randomness — only a golden-file comparison does). `rng.golden`/`RngCases.cs`/`PascalRandomTests.cs`
are a standing regression fixture for it: `runworld.pas`'s `RunRngCase` sets a real `RandSeed` and
draws a real sequence via `Random()` (no `ForcedRandomValue` involved at all), and
`PascalRandomTests.MatchesGoldenFile` checks the C# port reproduces it exactly, including a
701-draw case that crosses the state refill boundary.

## History

This section is background on *why* the harness looks the way it does, not current practice.
Nothing here describes a workflow to follow today — see the sections above for that.

**Two earlier strategies, both retired.** `reference/verify/*.pas`'s per-procedure transcription
pattern (hand-copy one procedure into a fresh file, call it in isolation) was this project's
original ground-truth approach, and is still structurally fine for a genuinely isolated,
parameter-only procedure with no real-state dependency — nothing currently uses it, but it's not
wrong to reach for again if a future case fits that description better than a hand-assembled
`Universe^` would. This patch-based lane itself went through an earlier stretch of trimming and
relocating: rather than patch a unit's `USES` clause down to size, a needed procedure was often
copied verbatim into whatever unit was *already* linked
(`GetIndustrialDistribution`, several `NEWGAME.PAS` procedures relocated into `UPDATE.PAS`), and a
unit that genuinely needed linking (`INTRFACE.PAS`, `FLEET.PAS`) was trimmed down to just the
procedures something else actually called, deleting the rest rather than patching around it. That
whole discipline — deciding whether to relocate one function or trim a whole unit, tracked in this
file's own now-removed "Restoring a removed procedure"/"Trimming a unit down to size" sections —
no longer applies: since replacing this lane's patches with a fuller, more-pristine tree
(the change described next), nearly the entire ~90-unit source tree links and compiles as-is, so
there's essentially nothing left to trim or relocate around.

**The fuller-tree replacement.** For most of this project's history, this lane trimmed each unit
down to just what the *current* set of ground-truth domains needed — cheap per domain, but meant
the linked surface grew one grudging function at a time, and each new domain risked re-deriving a
trim decision a previous one had already made differently. A parallel experiment
(`fullbuild-poc` branch) tried the opposite bet: patch out only the two categorically-uncompilable
things (inline assembly, DOS screen/keyboard I/O) and build almost the *entire* pristine tree once
— 67 of ~90 units, including full `INTRFACE.PAS` and full `UPDATE.PAS`, not trimmed stand-ins. That
bet paid off enough that this lane fully replaced its own trimmed patches with it: `ATTACK.PAS`/
`ENVIRON.PAS`/`FLEET.PAS`/`GALAXY.PAS` need no patch at all now (the old trims existed only to
dodge `Dos2`/`Orders`/`EIO`/`WND`, unnecessary once the fuller tree links them anyway), and units
that used to carry a 3-procedure stand-in (`INTRFACE.PAS`) now carry the real, complete file. The
20 pre-existing ground-truth domains were reverified byte-identical against their previously
committed output across this switch; the `scenario` domain went further, switching from a
hand-reimplementation of `LoadScenario`'s own parsing loop to calling the real procedure directly
(see the Domain catalog entry above) — every deterministic field matched exactly, confirming the
switch changed nothing about what real Pascal computes, only how faithfully this harness reaches
it.
