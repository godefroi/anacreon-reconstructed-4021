# Patch-based Pascal ground truth

## What this is

An alternative to the per-procedure transcription pattern used by `reference/verify/*.pas` (see
`docs/PORT_DESIGN.md`'s "Ground-truth harness generation" section for that baseline). Instead of
hand-transcribing a procedure into a fresh file, this maintains small patches against the real
`reference/DOSAnacreonSource131/*.PAS` source, applies them to a disposable copy at build time, and
calls the real, only-minimally-touched Pascal code directly against a hand-assembled `Universe^`.

**Status: in production for every ground-truth domain that exists — see the catalog below.**
`reference/verify/*.pas`'s per-procedure transcription pattern has had no domains on it since
`production.golden`'s migration (`production.pas` was the last file using it, and `common.pas`, its
only remaining shared dependency, was deleted alongside it). The pattern itself isn't retired: it's
still the right call for a genuinely isolated, parameter-only procedure (see "Recommendation" below)
— there's just nothing currently using it.

## Layout

- `patches/*.PAS.patch` — unified diffs against the matching pristine file in
  `reference/DOSAnacreonSource131/`. This is the maintained artifact. `reference/DOSAnacreonSource131/`
  itself is never edited.
- `runworld.pas` — a driver program (not a patch target, a genuinely new file): assembles a minimal
  `Universe^` and calls the real Pascal procedure(s) under test. Machine-parseable CLI:
  `case <domain> <case1> <case2> ...`, where `<domain>` selects the case shape/output line — see the
  file's own header comment for the exact field list of every domain. One driver, not one per domain,
  so `PatchHarness.CompileAndRun` only has to copy/patch/compile the whole patched tree once per
  `dotnet test` run regardless of how many domains use it.
- `build.ps1` — deletes and regenerates `patched/` from pristine source + patches, then compiles
  `runworld.pas`. Run it, then run `.\patched\runworld.exe case ...` for manual iteration. The C# test
  suite doesn't shell out to this script — `PatchHarness.cs` (in
  `src/ThreeLn.Reconstruction4021.Tests/PascalGroundTruth/`) does the same copy/patch/compile/run steps
  directly, so `GoldenFileTests` can call it like any other harness.
- `patched/` — disposable build output, gitignored, never a source of truth. If you need to iterate on
  a patch: run `build.ps1`, edit the file directly under `patched/`, verify it compiles/runs, then run
  `./regenerate-patch.ps1 -File FILE.PAS` to regenerate that file's `.patch` and overwrite it in
  `patches/` (see "Restoring a removed procedure" below for the full workflow). Never hand-edit a
  `.patch` file.
- `regenerate-patch.ps1` — regenerates one `patches/<File>.patch` from a hand-edited `patched/<File>`,
  self-verifying that the result applies cleanly to pristine source and reproduces `patched/<File>`
  byte-for-byte before writing it. Pass `-OutDir` to write somewhere other than `patches/` to try a
  regeneration without touching the committed patches.
- `build-callgraph.ps1` / `dos_131_callgraph.json` / `query-callgraph.ps1` — a call-graph index over
  all of `reference/DOSAnacreonSource131/*.PAS`, built for deciding what a unit actually needs before
  trimming/patching it (the "trim a unit down to size" workflow below used to mean grepping call sites
  by hand). `build-callgraph.ps1` runs `ctags` (needs Universal Ctags on `PATH`) to find every
  procedure/function definition, then scans every source line itself for call sites (ctags/`global`
  don't extract Pascal references, only definitions), and writes one JSON index,
  `dos_131_callgraph.json` (gitignored, regenerate on demand — it's ~1MB and derived entirely from
  already-committed source, not itself a source of truth). Format: `{ SymbolName: { file, line, kind,
  signature, refCount, references: [{file, line, context}, ...] } }`.

  **Known limitation, confirmed real, not theoretical**: the index keys definitions by name only,
  case-insensitively, with no per-unit scoping — Pascal allows the same procedure/function name in
  unrelated units (confirmed: `GetBestTarget` is two different procedures, `ATTNPE.PAS` and
  `NPEINTR.PAS`; also `UpdateFleets`/`ReviewNews`/`GetTarget`/`GetFleetComposition`, each redefined per
  NPE personality file — exactly the Phase 6 procedures this tool is meant to help scope). For a
  colliding name, the index keeps only one arbitrary definition (whichever ctags line was processed
  last) and blends every same-named symbol's call sites into one `references` list — silently wrong if
  trusted as-is. Use `query-callgraph.ps1 <Name>` rather than reading the JSON directly: it looks up
  the same index but also greps the source tree itself for every file declaring that name and warns
  when there's more than one, so a collision is never silently trusted. For a name it flags, read each
  file's own declaration/call sites directly instead of the index's blended one.

To add a new domain: give `runworld.pas` a new `case <domain>` branch (document its field shape in the
file's own header comment, matching the convention every existing domain already follows), add a
`GoldenFile.Regenerate(...)` call in `GoldenFileTests.cs`, and if the target procedure isn't reachable
from the patched build yet, restore it — see "Restoring a removed procedure" below.

## Domain catalog

What each domain calls, what its `Universe^` setup needs to know, and anything non-obvious found while
building it. Grouped by the roadmap phase that introduced it; field shapes and output keys live in
`runworld.pas`'s own header comment, not repeated here.

### Phase 1 — economy / annual tick (all via the real `UpdateWorld`, run end to end)

- **`techlevel`** — `UpdateTechLevel`. The original proof of concept: reproduced
  `TechLevelCases.OwnedWorldBehindCapitalAdvances` against a hand-assembled 2-planet `Universe^` before
  any other domain existed, confirming the patched build was complete and self-sufficient (a from-scratch
  `build.ps1` run reproduced it identically). All 8 `TechLevelCases` matched the prior transcription-based
  `techlevel.golden` byte-for-byte.
- **`military`** — `UpdateMilitary`. Running the real `UpdateWorld` end to end (instead of the retired
  isolated harness) let `MilitaryCase` drop its old `HarnessPop` field — a hand-derived
  post-`UpdatePopulation` value the isolated harness needed fed in separately; the real pipeline computes
  it for free.
- **`starbase`** — `SupplyLink`/`SurplusLink`. The first domain needing more than one world in the
  `Universe^`: these resolve neighbors via `GetObject` (`Sector[x]^[y].Obj`), which `techlevel`/`military`
  never touch — needs `Galaxy.InitializeSector` plus a direct `Sector[x]^[y].Obj` write for the neighbor
  planet. Covers only `SupplyLink`/`SurplusLink`'s own arithmetic, not the rest of the starbase economy
  pipeline (eligibility filtering, `Kind`-gating, Rebellion have no separate Pascal formula to cross-check
  — `AnnualTickHandlerStarbaseTests` covers those hardcoded instead).
- **`ambrosia`** — `UseUpAmbrosia`. Same `HarnessPop`-elimination as `military`; also dropped
  `RevIndexStart` (the isolated harness's own `revindex` output was never asserted on by
  `MatchesGoldenFile` in the first place).
- **`revolution`** — `UpdateRevolution`/`Rebellion`. Caught a real gap shared by both the old isolated
  harness and the C# port: neither modeled `UpdateIndustry`/`Production`'s `ReportPlanetLack` calls, a
  real +1 `RevolutionIndex` bump (see `docs/PORT_DESIGN.md`'s "Resource-shortfall reporting" section for
  the fix). Needed a second test-only observability hook alongside `ForcedRandomValue`:
  `GetNewTotalRevIndex(Emp)`, exposing `NewTotalRevIndex` (an `Empire`-indexed accumulator `Rebellion`
  writes to, normally committed by `UpdateEmpire`, unreachable from `UpdateWorld` alone) — read-only, no
  behavior change. That accumulator never resets between cases in one batched CLI invocation (only the
  never-called `UpdateUniverse` zeroes it), so `RunRevolutionCase` reads it before and after `UpdateWorld`
  and reports the *difference*, not the raw value.
- **`production`** — `ProduceRawMaterial`/`GetIndustrialDistribution`/`UpdateIndustry`/`Production`. The
  last domain still on the transcription pattern before this one retired it. Found two bugs, both in the
  *harness*, not the C# port: an empty `TechnologySet` (silently zeroed all raw-material production, since
  `UpdateWorld` intersects it with `TechDev[Tech]`) and an unset ISSP dial (`ImpExp` left at its
  `FillChar`-zeroed value instead of `DefaultISSP`, `$5555`) — both states are unreachable in real
  gameplay (`CreateEmpire` always seeds `Technology`/`ImpExp` correctly), confirmed before trusting the
  fix. Also surfaced a real C#-port gap: the real `UpdateWorld` calls `UpdateDefenses`, which
  `AnnualTickHandler.RunAnnualTick` didn't call yet at the time — widened
  `AnnualTickHandlerProductionTests.MatchesGoldenFile`'s exclusion list (`Cargo.Chemicals`/`Metals`, on
  top of the already-excluded `Supplies`/`Ambrosia`/`Legions`).
- **`empire`** — `UpdateEmpire` (not `UpdateWorld` — this is empire-level, not per-world) →
  `NewTechLevel`/`GetChanceForNewTech`/`GetNewTech`. The first domain whose target procedure had been
  deleted from the patched `UPDATE.PAS` entirely (unreachable from `UpdateWorld`) — required regenerating
  the whole `UPDATE.PAS.patch` (see "Restoring a removed procedure" below). `Empire.Technology` is
  encoded as a 26-bit mask (bit *i* = `TechnologyTypes(i+1)`) since the CLI's field parser only handles
  plain integers. A fractional lab `Efficiency` case (needed to tell `Trunc` from `Round` in
  `GetChanceForNewTech`'s formula) can't be a shared golden-file case here: the C# side can only reach the
  private `NewTechLevel` via the full `RunAnnualTick`, which runs `UpdateEfficiency` first and perturbs
  `Efficiency` by an RNG-dependent amount, while this domain calls `UpdateEmpire` directly and never
  touches `Efficiency` at all — covered by a hardcoded C# test instead
  (`AnnualTickHandlerEmpireTests.FractionalLabChanceTruncatesNotRounds`), its expected value confirmed
  against a real Pascal run first.
- **`construction`** — `UpdateConstruction` (nested `UseUpRawMaterial`) plus
  `ConstructStarbase`/`ConstructStargate`. Same "deleted from the patched build, needs a full
  `UPDATE.PAS.patch` regeneration" situation as `empire`. `ConstructStarbase`/`ConstructStargate` call
  five small `Intrface`-only helpers (`NextStarbaseSlot`, `CreateStarbase`(Pascal), `NextStargateSlot`,
  `CreateStargate`(Pascal), `GetOptimumIndus`) — originally relocated verbatim into this file, later
  folded into their real home in `patches/INTRFACE.PAS.patch` once that unit was trimmed and linked
  (Phase 6, 6a follow-up — see "Trimming a unit down to size" below). Hit the same `Sector[x]^[y]`
  access-violation gotcha `starbase` already found (`PutMine`/`CreateStarbase`/`CreateStargate` all
  touch it; needs `Galaxy.InitializeSector`).

### Phase 2 — galaxy / new-game setup

- **`empirecreate`** — `CreateEmpire` directly. Unlike every domain before it, needed no new patch at
  all: `CreateEmpire` was already exported from `PRIMINTR.PAS`'s own `INTERFACE` section. Rather than
  pull in all of `NEWGAME.PAS` (and its much larger `USES` clause) just to reach
  `CreatePlayerEmpire`/`CreateNPEmpire`, this domain reproduces their 3-line starting-tech-set formula
  inline and calls `CreateEmpire` directly with the result. Fully deterministic, no RNG. 13 cases
  exhaustively cross-check every row of `TechDev` plus both directions of the extra-tech intersect-clamp.
- **`trillumreserves` / `randomplanet` / `nebula`** — `CreateRndPlanet` calls `CreatePlanet`, which
  lives in the real (trimmed) `Intrface` unit (see "Trimming a unit down to size" below) — originally
  relocated verbatim into this file alongside `RandomTrillumReserves`/`RndShips`/`RndCargo`/`RndDefns`/
  `SetUpWorld`/`CreateRndPlanet`/`NebulaeBand`/`NebulaePatches` (`NEWGAME.PAS`, still relocated here:
  `NEWGAME.PAS` itself was never trimmed/linked, so these eight stay verbatim copies) — all
  self-contained `Universe^`/`Misc`/`DataCnst`/`PrimIntr` logic, reachable with zero new unit imports.
  `GetRandomXY`/`CreateRandomWorlds` are deliberately **not** covered here: under
  `ForcedRandomValue`, every `Rnd` call in one invocation returns the same fixed offset, so a coordinate
  blocked on the first roll is blocked on every retry too — there's no way to construct a case that
  reaches "blocked, then a later retry succeeds" on either side of the comparison. Those two procedures'
  own logic is simple enough that hardcoded `GalaxySetupTests` cover it instead; these three domains
  target only the pieces with real formula risk (`CreateRndPlanet`'s `Trunc`/`Round`/tech-adjustment-table
  math, `NebulaeBand`/`NebulaePatches`' coordinate-space arithmetic). This is where the
  `ForcedRandomValue`-ordering bug was found — see "Landmines and gotchas" below.
- **`scenario`** — `LoadScenario`, a real `.SCN` file loaded end to end. Output is an aggregate checksum
  over the whole loaded `Universe^`, not a per-entity dump — a real `dos_131` file has up to ~160 worlds,
  and a mismatch anywhere perturbs at least one sum. The C# side
  (`ScenarioLoaderGoldenTests.MatchesGoldenFile`) only exact-matches a subset of these fields — every
  field touched by a random draw anywhere in the file is fragile to RNG-stream-position drift between two
  independently-written implementations, even fields that look deterministic on their face. See
  `docs/PASCAL_ARCHITECTURE_NOTES.md`'s "Scenario golden-file testing can't be bit-exact, and why" section
  for the full investigation, and "`-CfSSE2`" below for the float-precision half of that story.

### Phase 3 — probes

- **`probescout`** — `ProbeScout` directly, against one planet at the probe's destination and one at the
  next ring cell in Pascal's own fixed offset order. Covers `ISqrt(Cargo[men])` and the
  `Rnd(1,100)<ChanceToDestroy` threshold plus its Exit-before-`ScoutObject` sequencing; ring
  ordering/early-exit control flow itself has no separate Pascal formula to cross-check, so
  `VisibilityHandlerProbeTests` covers that hardcoded instead.

### Phase 5 — combat

- **`defenses`** — `UpdateDefenses` (commit 5b), via the real `UpdateWorld`. `DefenseType`-indexed
  (`lam`/`def`/`gdm`/`ion`) growth on a world, gated by the same 26-bit `TechnologyBitmask` encoding as
  `empire`.
- **`combat`** — one round of the real `Battle` at the `DpSpc` shell (commit 5d) — the group/shell
  combat engine's own per-round damage math, not a multi-round engagement (that's `npeattack`, below).
  This is where the `PascalRound` banker's-rounding bug was found: `Round(2.5)` is `2`, not `3`, under
  real FreePascal — see `docs/PASCAL_ARCHITECTURE_NOTES.md` for the full story.
- **`npeattack`** — the full `NPEAttack` end to end (commits 5e/5f): its own multi-round
  `FleetRetreats`/`Targetting`/`GroupEngage`/`AdvanceGroups` loop, then outcome application
  (`ResolveAttack`/`ConquerWorld`/`ConquerEmpire`/`RestoreCombatant`). The attacker is always Empire1's
  fleet (200 fighters, 200 hunter-killers, plus an optional troop-carrying jumptransport group); Empire1's
  capital sits at (0,0), Empire2's (the usual target) at (50,50), fixed by the domain itself so
  `ConquerEmpire`'s own distance-from-capital math is exercised meaningfully. An optional third world lets
  a case drive any one of `ConquerEmpire`'s four per-planet branches deliberately.
- **`lamattack`** — `LAMAttack` directly (commit 5g), not through `NPEAttack` — `LAMAttack` has no `Rnd`
  call anywhere in its body, so there's no combat-engine setup to exercise, only the proportional-
  distribution formula itself (`Round`/`Trunc` against `ProtecNeeded`/`CombatTable`, the same class of
  arithmetic that produced the `PascalRound` bug in `combat`). The target is always Empire2's (a Fleet or
  a Planet, whichever the case selects); `DestroyFleet` reuses the same no-op stand-in
  `ATTACK.PAS.patch` already carries for `npeattack`, so this domain asserts `LAMAttack`'s own
  `ShipsDest`/`DefnsDest` VAR out-params directly, not whatever state that stand-in would leave behind.
  Two of commit 5g's other three procedures have no domain here at all:
  `HolocaustWorld`/`HolocaustEffectiveness` are confirmed dead code (not ported — see
  `Core/Combat/CombatStandalone.cs`'s own doc comment), and `DestroyConstructionOrGate` — live, wired
  into this port's `NPEAttack` — needs `DestroyConstruction`/`DestroyStargate`, which live in `Intrface`
  (not linked in this build); it's covered by hardcoded C# tests instead (`CombatStandaloneTests.cs`),
  same treatment as `SelfDestructObject` (no Pascal-side ground truth built for it either, since
  `SBASE.PAS` was never patched into this harness).

### Phase 6 — NPE AI (movement-fidelity prerequisite, 6a)

- **`fleetlogistics`** — `FuelCapacity`/`FuelConsumption`/`FleetCargoSpace`/`BalanceFleet` (MISC.PAS/
  INTRFACE.PAS), called directly against a hand-built `ShipArray`/`CargoArray` — no `Universe^` state
  needed, these are pure functions over their own parameters. Found (by comparing against these real
  constants while scoping 6a) that `FleetMovementHandler`'s prior fuel model was invented, not ported —
  replaced with `Core/Entities/FleetLogistics.cs`. `FleetCargoSpace`'s own `Round` call is the same
  arithmetic-risk class that produced the `PascalRound` bug in `combat.golden`.
- **`fleetmove`** — `GetNewPos` (`FLEET.PAS`) and `PassingThroughGate`/`PassingThroughFortress`
  (relocated in place into `patches/INTRFACE.PAS.patch`, see "Trimming a unit down to size" below),
  against one hand-placed fleet. Covers dense-nebula step-blocking and stargate/fortress teleport
  determination — `FleetMovementHandler.GetNewPos`/`IsPassingThroughGate`/`IsAtFortress` are `public
  static` specifically so this domain (and `FleetMoveTests`) can call them in isolation, the same
  reason `CombatEngine`'s own formula methods are public statics rather than private instance helpers.
  `GetNewBasePos`/`XY2Dir` (`SBASE.PAS`, starbase obstacle-avoidance) have no domain here yet — `SBase`
  was never patched into this harness; `FleetMovementHandlerTests.cs` covers that hardcoded instead,
  same "harness can't reach it yet, cover it directly" precedent as `DestroyConstructionOrGate` (5g).

### Not a `UpdateWorld`/`GalaxySetup` domain

- **`rng`** — a standing regression fixture for `PascalRandom.cs`, the from-scratch port of fpc's actual
  `Random`/`RandSeed` algorithm (needed once `nebula`'s multi-patch cases and `scenario`'s real `.SCN`
  loads required a genuine multi-call RNG sequence, not just `ForcedRandomValue`'s fixed offset). See "A
  real Pascal RNG" below for the full story of how that algorithm was identified.

## Restoring a removed procedure

When the patched `UPDATE.PAS`/`ATTACK.PAS` doesn't export the procedure a new domain needs (it was
deleted back when the patch-based lane first stood up that unit, since nothing reachable at the time
needed it), don't hand-splice a new hunk into the existing patch — regenerate the whole file's patch:

1. Run `build.ps1` to get a fresh `patched/` tree, then hand-edit the target file under `patched/` to
   restore the procedure (forward declaration in `INTERFACE`, body in `IMPLEMENTATION`).
2. Compile it directly (`fpc -Mtp -CfSSE2 runworld.pas` from inside `patched/`) and confirm it compiles
   and the new domain's manual test case runs.
3. Run `./regenerate-patch.ps1 -File FILE.PAS` to regenerate `patches/FILE.PAS.patch` from the
   hand-edited `patched/FILE.PAS`. This used to be a hand-driven `git diff --no-index` with two gotchas
   that repeatedly had to be re-fixed after the fact: `git diff --no-index` always prepends its own
   `a/`/`b/` prefix on top of whatever path you give it (producing headers like `a/a/FILE.PAS` unless
   worked around), and driving the diff through the wrong tool can silently rewrite CRLF↔LF (some
   Bash/MSYS pipes do this; a plain PowerShell `>`/`Out-File` redirect defaults to UTF-16LE) — either
   one produces a patch that looks fine but won't `git apply` against the real (CRLF) Pascal source, or
   applies but reproduces the wrong bytes. `regenerate-patch.ps1` stages both files under literal
   `a/FILE.PAS`/`b/FILE.PAS` with `--no-prefix`, reads git's stdout as raw bytes via
   `System.Diagnostics.Process`, and — before writing anything to `patches/` — applies its own output to
   a scratch copy of pristine source and byte-compares the result against `patched/FILE.PAS`, refusing to
   write a patch that doesn't round-trip. Pass `-OutDir` to write somewhere other than `patches/` (a
   scratch directory) to try a regeneration without touching the committed patches.
4. Run `build.ps1` fresh (deletes `patched/`, reapplies every patch, recompiles) and confirm the whole
   patch set still applies together and still compiles/runs — `regenerate-patch.ps1`'s own self-check
   only proves the one file round-trips in isolation, not that it composes with every other patch.

Done for `empire`/`construction` (`UPDATE.PAS.patch`) and `lamattack` (`ATTACK.PAS.patch`) so far.
Relocating a small, self-contained procedure/formula out of a unit with a much larger `USES` clause than
the domain needs (rather than pulling in the whole unit) is the same "don't drag in `Fleet`/`Orders`/
`NPE`/`Crt`/... for one function" move used repeatedly above (`GetIndustrialDistribution`,
`empirecreate`'s inline tech-set formula, `construction`'s five `Intrface` helpers, `trillumreserves`/
`randomplanet`/`nebula`'s relocated `NEWGAME.PAS` procedures) — check whether the target procedure needs
relocating at all before assuming a whole-unit `USES` pull is necessary.

## Trimming a unit down to size

A related but distinct move from relocation above, first needed for `fleetmove` (Phase 6, 6a): when the
domain genuinely needs the procedure to live in its *real* unit — because that unit is about to become a
real, growing dependency for later domains anyway, not a one-off — trim the unit itself down to just
what's reachable, rather than copying the procedure out to somewhere already-linked. `FLEET.PAS` is
exactly this case: `NPEINTR.PAS`'s own `USES` clause already needs the real `Fleet` unit for Phase 6's
later commits (6c onward), so relocating `GetNewPos` out to `Misc` now would only have delayed building
the real `FLEET.PAS` patch, not avoided it — decided explicitly this way rather than defaulted into,
after weighing both.

The process is the same discovery loop as "Restoring a removed procedure" above, but the goal is
deletion, not restoration: add the unit to `runworld.pas`'s own `USES`, compile, and delete whatever the
compiler complains about next (an unreachable dependency's own missing sub-dependency, a genuinely dead
`USES` entry, a procedure that needs a unit nothing else does) until it links. Two rounds of this got
`FLEET.PAS`/`INTRFACE.PAS` working:

- `FLEET.PAS` lost `Orders` from its own `USES` and every procedure that only existed to execute a
  fleet's queued orders (`ExecuteDestCOM`/`ExecuteTransCOM`/`ExecuteFleetOrders`, plus `DestroyFleet`'s
  order-disposal lines) — real Pascal, but Phase 8's job (the order compiler), and this harness's test
  fleets never carry orders regardless, so nothing observable changes by removing the dead branch.
- `INTRFACE.PAS` (normally ~1700 lines) turned out to need `EIO` (DOS console I/O, needs `CRT` — not
  available under this fpc target at all) transitively through `Mess` (the in-game mail system), and
  `Orders`/`NPE` directly in its own `IMPLEMENTATION USES` — none of which anything `FLEET.PAS` actually
  calls. Rather than trim procedure-by-procedure through 1700 lines, `patches/INTRFACE.PAS.patch`
  replaces the whole file with just the three procedures `FLEET.PAS` needs
  (`PassingThroughGate`/`PassingThroughFortress`/`BalanceFleet`, copied verbatim, unchanged, from the
  real source line ranges cited in the patched file's own header comment) — everything else this unit
  ever declared is gone from this harness's copy. `GetObject`/`GetStatus`/`GetBaseType`/`GetGateType`/
  `Known` (which those three still call) already live in the already-linked `PrimIntr`, so nothing else
  needed pulling in.

  Nine more procedures joined these three later, same commit's own follow-up: `GetIndustrialDistribution`/
  `CreatePlanet`/`NextStarbaseSlot`/`CreateStarbase`/`NextStargateSlot`/`CreateStargate`/`GetOptimumIndus`/
  `ProbeScout`/`UpdateProbes` had all been relocated verbatim into `patches/UPDATE.PAS.patch` back when
  `INTRFACE.PAS` wasn't linked at all (see `construction`/`trillumreserves`/`randomplanet`/`nebula` above)
  — once `INTRFACE.PAS` was trimmed and linked anyway for `FLEET.PAS`'s sake, keeping a second verbatim
  copy of code that really lives there was pure duplicate-copy drift risk with no upside, so they moved
  back. All twelve are kept in their **original relative pristine order** (both `INTERFACE` declarations
  and `IMPLEMENTATION` bodies) rather than clustered wherever's convenient — a first pass appended the
  nine at the end of the file, and the regenerated patch showed them as full delete/re-add pairs relative
  to pristine even though the bodies were byte-identical, because moving code to a different position in
  the file is a real edit as far as a diff is concerned. Preserving pristine order fixes that specific
  problem (the file itself is genuinely edited in place, not rewritten-with-a-move), but it does **not**
  make the regenerated `patches/INTRFACE.PAS.patch` a set of pure deletions — checked directly, not
  assumed: 10 of the 12 kept procedures still render as delete/add pairs rather than context, because
  this file has a lot of short, generic, repeated Pascal lines (`BEGIN`/`END;`/`WITH Universe^ DO`)
  scattered across its 1660 lines, which confuses LCS-based diffing on small kept "islands" regardless of
  diff tool or algorithm (confirmed identical across all four `git diff --diff-algorithm` options and
  plain GNU `diff -u`, so this isn't a git quirk). The correctness that matters is verified a different
  way — `regenerate-patch.ps1` applies its own output to a clean pristine copy and byte-compares the
  result against `patched/INTRFACE.PAS` before writing anything — not by how the diff happens to render.

`FleetMovementHandler.GetNewPos`/`IsPassingThroughGate`/`IsAtFortress` are `public static` on the C# side
specifically so `fleetmove`'s own `FleetMoveTests` can call them in isolation this same way — check
whether a new domain's C# counterpart needs the same visibility bump before assuming a golden-file
comparison has to route through a much larger public entry point.

## Landmines and gotchas

Cross-cutting lessons, not specific to browsing one domain — read before touching *any* patch or adding
a new domain.

- **`ABSOLUTE` overlays compile but lie.** `DATASTRC.PAS:235`:
  `GlobalSets: GlobalSetsRecord ABSOLUTE SetOfActiveFleets;` overlays a whole record onto the memory
  address of `SetOfActiveFleets`, relying on several separately-declared globals (`TYPES.PAS:180-188`)
  being laid out contiguously in declaration order — Turbo Pascal's segment-based layout, not something
  any modern compiler guarantees. `fpc` accepts the syntax silently. Writing through `GlobalSets.X` in
  `runworld.pas` corrupted the `Universe` pointer itself (a genuine access violation, runtime error 216)
  with no compile-time warning. Fix: never write through `GlobalSets`; use the real standalone vars
  directly. This is exactly the "compiles fine, wrong at runtime" failure mode that makes this whole
  approach riskier than transcription in a way that isn't just about compile effort.
- **A hand-assembled `Universe^` is only as faithful as the fields it remembers to set.**
  `production.golden`'s two harness bugs (an empty `TechnologySet`, an unset `ImpExp`) were both "field
  defaults to zero instead of what `CreateEmpire`/settlement actually initializes it to" — worth a
  deliberate check against real init code (`CreateEmpire`, `PRIMINTR.PAS`'s `Set*` procedures) for any new
  domain's setup, not just trusting `FillChar` zero to be a reachable state.
- **`ForcedRandomValue` was checked in the wrong order** (found via `nebula`'s `PatchesMultipleRng2`
  case). The original `INT.PAS` patch had `Rnd` check `ForcedRandomValue>=0` *before* the real,
  unconditional `Max<=Min` degenerate-range clamp — so a forced value could override that clamp, producing
  a result outside `[Min,Max]` whenever `Min=Max` coincided with a nonzero forced offset.
  `NebulaePatches`' own `Rnd(1,4-Abs(y-InitY))` hits exactly `Min=Max` at a patch's vertical extremes,
  so a forced value of 2 exposed it: Pascal painted a full 6th row that `GalaxySetup.NebulaePatches` (via
  `PascalMath.Rnd`, which has always checked the degenerate range first) correctly did not. The fix went
  into the *Pascal test patch*, not `PascalMath.Rnd` — pristine `Rnd`'s degenerate-range clamp is real,
  unconditional behavior; `ForcedRandomValue` is a test-only device layered on top of it, so the clamp has
  to win first. Reordering `INT.PAS.patch` moved none of the other eleven already-committed golden files
  at the time — confirming no earlier domain's cases had coincidentally depended on the wrong ordering. A
  tempting-but-wrong fix worth naming explicitly: making `PascalMath.Rnd` call through to `random.Next()`
  even when `min==max` (so `FixedRandom`'s override would "win" the same way the old, buggy ordering did)
  breaks `NewTechLevel`'s `missingAtCurrentLevel[Rnd(1, missingAtCurrentLevel.Count) - 1]` indexing the
  moment a shared `FixedRandom` offset exceeds a small `Count` — real Pascal's own equivalent never
  crashes on this (its array is statically over-provisioned; a C# `List` has no such slack). Confirmed
  empirically (four pre-existing Phase-1 tests broke instantly) before reverting in favor of the
  `INT.PAS` reorder.
- **`-CfSSE2`** (`build.ps1`/`PatchHarness.cs`'s compile flag). fpc's default i386 codegen keeps chained
  `Real` expressions in the x87 FPU's 80-bit extended-precision stack until explicitly stored, while C#'s
  `double` is always strict 64-bit IEEE754 — so a borderline expression can round to a different integer
  in each language. `-CfSSE2` forces fpc to use strict 64-bit double throughout, aligning the harness with
  the only precision C# has. Verified via `git diff --stat` on the regenerated golden files that it
  changed none of the domains existing at the time it was added — but that's a snapshot, not a guarantee:
  it changes float semantics harness-wide, so a future domain with its own borderline `Real` expression
  will get different ground truth under it than under fpc's default. Kept as a deliberate baseline choice
  regardless. Full writeup: `docs/PASCAL_ARCHITECTURE_NOTES.md`'s "Scenario golden-file testing can't be
  bit-exact, and why" section.

## What it took to get UpdateWorld callable

`UPDATE.PAS`'s own `USES` clause lists 14 units; `Intrface` alone further pulls in `Fleet`/`Orders`/`NPE`.
Every blocker hit while getting `UpdateWorld` (and everything it actually calls) to compile turned out to
be small and mechanical, not a case of "reconstruct a DOS UI stack":

- **Dead UI/config code, deleted outright.** `Environ`'s `FeatureInActive` (a demo-nag dialog) and
  `LoadConfiguration`/`SaveConfiguration` (directory prefs), `UPDATE.PAS`'s `UpdateUniverse` (the
  whole-galaxy tick loop plus its screen progress window — the *only* `Crt`/`WND` usage in the entire
  unit). None of it is reachable from `UpdateWorld`.
- **Trivial I/O helpers, duplicated instead of importing a whole unit for them.**
  `WriteVariable`/`ReadVariable` (used by `Galaxy`'s `SaveSector`/`LoadSector`, `Environ`'s
  `LoadEnvironment`/`SaveEnvironment`, and `News`'s `LoadNewsData`/`SaveNewsData`) are just
  `BlockRead`/`BlockWrite`+`IOResult` — no real coupling to the `Dos2` unit they live in, which pulls in
  `Printer`/`CRT`/`DOS`/`EIO`/`WND`/`Menu`. Each of those three save/load pairs was kept **verbatim**
  (real, wanted logic — see "The save/load angle" below) with its own tiny local copy of the two helpers
  instead.
- **Turbo Pascal-isms with an obvious modern equivalent.** `STRG.PAS`'s `AllUpCase` used raw 8086 opcodes
  via `Inline(...)` — replaced with a 2-line `UpCase` loop. `PRIMINTR.PAS`'s `GetNewName` gated allocation
  on `MaxAvail` (TP's real-mode heap-free check, meaningless under virtual memory) — replaced with
  `IF True THEN`. Several fixed-length-string comparisons needed `{$V-}` (fpc's default `$V+` is stricter
  than TP about exact string-length matching).
- **One relocated (not rewritten) procedure, later moved home.** `GetIndustrialDistribution` is pure
  economy math (only calls `PrimIntr` getters and `Misc`/`DataCnst` tables) but lives in `INTRFACE.PAS`,
  which at the time would have dragged in `Fleet`/`Orders`/`NPE` for one function — moved verbatim into
  `UPDATE.PAS` instead, a real patch spanning two files (delete from one, add to the other), documented
  as such in the patch comments rather than silently dropping the provenance. Once `INTRFACE.PAS` was
  trimmed and linked anyway (Phase 6, 6a — see "Trimming a unit down to size"), this relocation (and
  eight others like it: `CreatePlanet`/`NextStarbaseSlot`/`CreateStarbase`/`NextStargateSlot`/
  `CreateStargate`/`GetOptimumIndus`/`ProbeScout`/`UpdateProbes`) moved back to its real home.
- **A test-only RNG override**, added to `INT.PAS`: `ForcedRandomValue`, when `>=0`, makes every
  `Rnd(Min,Max)` call return `Min+ForcedRandomValue` — deliberately matching the existing C# harnesses'
  `FixedRandom`/`RngFixedValue` convention exactly, so results are comparable to existing golden files.
  `-1` (default) means "use the real RNG," unchanged.

## The save/load angle

The original question this approach answered: could test fixtures be built by driving Pascal's *own*
save/load machinery instead of hand-writing field assignments? Two things worth knowing, found but not
used yet:

- `LOADSAVE.PAS`'s `InitializeUniverse(StartingYear, Size, Planets)` zero-inits the whole `Universe^` and
  sets planet count — a better starting point than a hand-rolled `FillChar`, but `LOADSAVE.PAS`'s own
  dependency list (`Dos2, Intrface->Fleet/Orders/NPE, News, Mess, TMA, Environ, NPETypes, NPE, Galaxy,
  Orders, Fleet`) is much larger than what `UpdateWorld` alone needs, so pulling it in wasn't justified
  for this scope.
- `LOADSAVE.PAS`'s `LoadGame`/`SaveGame` are the real binary `.SAV` format round-trip
  (`SFSignature = 'Anacreon save file v1.3'`). No `.SAV` file ships with the source, so building fixtures
  this way would mean either capturing one from real gameplay (not available at the time) or
  hand-authoring the binary layout — not obviously cheaper than direct field assignment for small
  scenarios.

Neither was pulled into this harness; `runworld.pas` does its own minimal
`New(Universe); FillChar(Universe^,SizeOf(Universe^),0);` instead. Revisit if a future scenario needs a
much larger/more realistic starting `Universe^` than a couple of hand-set fields can reasonably cover —
a real TP 1.31 `.SAV` file now sits at `reference/saves/INTRO_1.SAV` for whenever the save/load phase
(Phase 7) is picked up, addressing the "no `.SAV` file ships with the source" gap above.

## A real Pascal RNG, not a stand-in: `rng.golden` and `PascalRandom`

Every domain above `rng` needs only one `Rnd()` value per case, so `ForcedRandomValue` (a fixed offset
every call resolves to) has always been enough. `nebula`'s multi-patch cases and a genuine end-to-end
`.SCN` load (`scenario`) both break that: they retry/redraw multiple times per run, and a fixed offset
always re-rolls the *same* value, so (for `CreateRandomWorlds`) placing a second world in a zone that
already has one always blows through the retry cap on both sides. Comparing real multi-draw sequences
needs matching this project's actual fpc runtime's `Random`/`RandSeed` algorithm, not a fixed stand-in.

That algorithm isn't the classic Turbo Pascal LCG a DOS-era codebase might suggest, and guessing at it
from memory would have been exactly the kind of unverified recall this project avoids: fpc's RNG
implementation changed over its history, and which one a given installed compiler uses has to be checked,
not assumed. A quick probe program (`RandSeed:=12345; WriteLn(Random(100));` a few times) compiled with
this repo's actual installed fpc (3.2.2) and compared against candidate algorithms pulled from fpc's own
RTL source at matching tags settled it empirically: fpc's `main`/trunk source now uses a SplitMix64-seeded
Xoshiro128** generator (didn't match); the `release_3_2_2` tag's `rtl/inc/system.inc` uses a Mersenne
Twister (MT19937) variant with its own reseed/tempering convention (`mtwist_init`/`mtwist_update_state`/
`mtwist_u32rand` — matched exactly, including a mid-run reseed, a fresh-seed replay, and a draw crossing
the generator's 624-word internal state refill).

`src/ThreeLn.Reconstruction4021.Tests/PascalRandom.cs` is a from-scratch `System.Random` subclass porting
that exact algorithm, test-only (production code has no need for Pascal-bit-exact randomness — only a
golden-file comparison does). `rng.golden`/`RngCases.cs`/`PascalRandomTests.cs` are a standing regression
fixture for it: `runworld.pas`'s `RunRngCase` sets a real `RandSeed` and draws a real sequence via
`Random()` (no `ForcedRandomValue` involved at all), and `PascalRandomTests.MatchesGoldenFile` checks the
C# port reproduces it exactly, including a 701-draw case that crosses the state refill boundary.

## Recommendation

Reach for this approach whenever it would improve testing fidelity, with an eye toward eventually
building up a maximum-fidelity harness — the per-harness patch-authoring cost is accepted deliberately,
in exchange for being able to run the real Pascal code against known states across wide slices of the
game systems as those slices grow, not just the one procedure under test:

- That's most clearly the case when a procedure's fidelity risk is high enough to justify it: many
  state-shaped lookups (`GetCapital`/`GetTech`-style calls that transcription would otherwise have to
  simplify into plain parameters), or when the thing worth testing is call *ordering* across multiple
  steps in the same real pipeline — exactly the "cross-cutting field" bug class transcription hit twice
  (`UpdateMilitary` mutating `Cargo.Legions` before `UpdateRevolution` reads it, discovered only because
  the isolated harnesses didn't model the mutation).
- Transcription (`reference/verify/*.pas`'s per-procedure pattern) is still fine for genuinely isolated,
  parameter-only procedures with no real-state dependency — cheap and bounded, and proven across four
  roadmap commits before `production.golden`'s migration retired its last domain. Nothing currently uses
  it, but reach for it again if a future case fits that description better than a hand-assembled
  `Universe^` would.
- Don't expand the *linked* surface by default just because Phase 6 pulled in `Fleet`/`Intrface`.
  `Intrface`'s own `Fleet`/`Orders`/`NPE` dependency was dodged for years by relocating one function at a
  time; Phase 6, 6a decided (deliberately, not by default) that `Fleet` was worth linking in full because
  later NPE AI commits genuinely need it, and trimmed `Intrface` down to size for the same reason —
  `Orders`/`NPE`/`EIO`/`Mess` are still not linked, and still shouldn't be pulled in just because two of
  their neighbors now are. Treat each new area as its own exploration, weighed against what later commits
  actually need, not an assumed extension of whatever's already linked.
