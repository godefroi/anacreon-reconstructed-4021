# Patch-based Pascal ground truth

## What this is

An alternative to the per-procedure transcription pattern used by
`reference/verify/*.pas` (see `docs/ROADMAP.md`'s "Ground-truth harness
generation" section for that baseline). Instead of hand-transcribing a
procedure into a fresh file, this maintains small patches against the real
`reference/DOSAnacreonSource131/*.PAS` source, applies them to a disposable
copy at build time, and calls the real, only-minimally-touched Pascal code
directly against a hand-assembled `Universe^`.

**Status: in production for all ground-truth domains that exist so far
(`techlevel.golden`, `military.golden`, `starbase.golden`, `ambrosia.golden`,
`revolution.golden`, `production.golden`, `empire.golden`, `construction.golden`,
`empirecreate.golden`, `trillumreserves.golden`, `randomplanet.golden`,
`nebula.golden`, `rng.golden` — see `docs/ROADMAP.md`'s "Ground-truth harness generation" section).
`reference/verify/*.pas`'s
per-procedure transcription pattern has had no domains on it since
`production.golden`'s migration — `production.pas` was the last file using
it, and `common.pas` (its only remaining shared dependency) was deleted
alongside it. The pattern itself isn't retired: it's still the right call for
a genuinely isolated, parameter-only procedure (see "Recommendation" below) —
there's just nothing currently using it.**

## Layout

- `patches/*.PAS.patch` — unified diffs against the matching pristine file in
  `reference/DOSAnacreonSource131/`. This is the maintained artifact.
  `reference/DOSAnacreonSource131/` itself is never edited.
- `runworld.pas` — a driver program (not a patch target, a genuinely new
  file): assembles a minimal `Universe^` and calls the real `UpdateWorld`.
  Machine-parseable CLI: `case <domain> <case1> <case2> ...`, where `<domain>`
  selects the case shape/output line (`techlevel`, `military`, `starbase`,
  `ambrosia`, `revolution`, `production`, `empire`, `construction`, or `empirecreate` so far — see
  the file's own header comment). One driver, not one per domain, so
  `PatchHarness.CompileAndRun` only has to copy/patch/compile the whole
  patched tree once per `dotnet test` run regardless of how many domains use it.
- `build.ps1` — deletes and regenerates `patched/` from pristine source +
  patches, then compiles `runworld.pas`. Run it, then run
  `.\patched\runworld.exe case ...` for manual iteration. The C# test suite
  doesn't shell out to this script — `PatchHarness.cs` (in
  `src/ThreeLn.Reconstruction4021.Tests/PascalGroundTruth/`) does the same
  copy/patch/compile/run steps directly, so `GoldenFileTests` can call it
  like any other harness.
- `patched/` — disposable build output, gitignored, never a source of truth.
  If you need to iterate on a patch: run `build.ps1`, edit the file directly
  under `patched/`, verify it compiles/runs, then regenerate that file's
  `.patch` from the diff against the pristine original and overwrite it in
  `patches/`. Never hand-edit a `.patch` file.

## What it took to get UpdateWorld callable

`UPDATE.PAS`'s own `USES` clause lists 14 units; `Intrface` alone further
pulls in `Fleet`/`Orders`/`NPE`. Every blocker hit while getting
`UpdateWorld` (and everything it actually calls) to compile turned out to be
small and mechanical, not a case of "reconstruct a DOS UI stack":

- **Dead UI/config code, deleted outright.** `Environ`'s `FeatureInActive`
  (a demo-nag dialog) and `LoadConfiguration`/`SaveConfiguration` (directory
  prefs), `UPDATE.PAS`'s `UpdateUniverse` (the whole-galaxy tick loop plus its
  screen progress window — the *only* `Crt`/`WND` usage in the entire unit).
  None of it is reachable from `UpdateWorld`.
- **Trivial I/O helpers, duplicated instead of importing a whole unit for
  them.** `WriteVariable`/`ReadVariable` (used by `Galaxy`'s
  `SaveSector`/`LoadSector`, `Environ`'s `LoadEnvironment`/`SaveEnvironment`,
  and `News`'s `LoadNewsData`/`SaveNewsData`) are just
  `BlockRead`/`BlockWrite`+`IOResult` — no real coupling to the `Dos2` unit
  they live in, which pulls in `Printer`/`CRT`/`DOS`/`EIO`/`WND`/`Menu`. Each
  of those three save/load pairs was kept **verbatim** (this is real, wanted
  logic — see "the save/load angle" below) with its own tiny local copy of
  the two helpers instead.
- **Turbo Pascal-isms with an obvious modern equivalent.** `STRG.PAS`'s
  `AllUpCase` used raw 8086 opcodes via `Inline(...)` — replaced with a
  2-line `UpCase` loop. `PRIMINTR.PAS`'s `GetNewName` gated allocation on
  `MaxAvail` (TP's real-mode heap-free check, meaningless under virtual
  memory) — replaced with `IF True THEN`. Several fixed-length-string
  comparisons needed `{$V-}` (fpc's default `$V+` is stricter than TP about
  exact string-length matching).
- **One relocated (not rewritten) procedure.** `GetIndustrialDistribution` is
  pure economy math (only calls `PrimIntr` getters and `Misc`/`DataCnst`
  tables) but lives in `INTRFACE.PAS`, which would drag in `Fleet`/`Orders`/
  `NPE` for one function. Moved verbatim into `UPDATE.PAS` — a real patch
  spanning two files (delete from one, add to the other), documented as such
  in the patch comments rather than silently dropping the provenance.
- **A test-only RNG override**, added to `INT.PAS`: `ForcedRandomValue`,
  when `>=0`, makes every `Rnd(Min,Max)` call return `Min+ForcedRandomValue`
  — deliberately matching the existing C# harnesses' `FixedRandom`/
  `RngFixedValue` convention exactly, so results are comparable to existing
  golden files. `-1` (default) means "use the real RNG," unchanged.

## A real landmine: `ABSOLUTE` overlays compile but lie

`DATASTRC.PAS:235`: `GlobalSets: GlobalSetsRecord ABSOLUTE SetOfActiveFleets;`
overlays a whole record onto the memory address of `SetOfActiveFleets`,
relying on several separately-declared globals in `TYPES.PAS:180-188`
(`SetOfActiveFleets`, `SetOfFleetsOf`, `SetOfActivePlanets`, `SetOfPlanetsOf`,
etc.) being laid out contiguously in declaration order — Turbo Pascal's
segment-based layout, not something any modern compiler guarantees. `fpc`
accepts the syntax silently. Writing through `GlobalSets.X` in `runworld.pas`
corrupted the `Universe` pointer itself (a genuine access violation, runtime
error 216) with no compile-time warning. Fix: never write through
`GlobalSets`; use the real standalone vars directly. This is exactly the
"compiles fine, wrong at runtime" failure mode that makes this whole approach
riskier than transcription in a way that isn't just about compile effort —
worth remembering if this pattern gets reused elsewhere in the source.

## The save/load angle

The original question this approach answered: could test fixtures be built
by driving Pascal's *own* save/load machinery instead of hand-writing field
assignments? Two things worth knowing, found but not used yet:

- `LOADSAVE.PAS`'s `InitializeUniverse(StartingYear, Size, Planets)` zero-
  inits the whole `Universe^` and sets planet count — a better starting point
  than a hand-rolled `FillChar`, but `LOADSAVE.PAS`'s own dependency list
  (`Dos2, Intrface->Fleet/Orders/NPE, News, Mess, TMA, Environ, NPETypes,
  NPE, Galaxy, Orders, Fleet`) is much larger than what `UpdateWorld` alone
  needs, so pulling it in wasn't justified for this scope.
- `LOADSAVE.PAS`'s `LoadGame`/`SaveGame` are the real binary `.SAV` format
  round-trip (`SFSignature = 'Anacreon save file v1.3'`). No `.SAV` file
  ships with the source, so building fixtures this way would mean either
  capturing one from real gameplay (not available) or hand-authoring the
  binary layout — not obviously cheaper than direct field assignment for
  small scenarios.

Neither was pulled into this harness; `runworld.pas` does its own minimal
`New(Universe); FillChar(Universe^,SizeOf(Universe^),0);` instead. Revisit if
a future scenario needs a much larger/more realistic starting `Universe^`
than a couple of hand-set fields can reasonably cover.

## Validation

Original proof of concept: `runworld.pas` reproduced
`TechLevelCases.OwnedWorldBehindCapitalAdvances` (Tech=Warp, owned, capital
ahead at Jump, `RngFixedValue=0`) by calling the real `UpdateWorld` against a
hand-assembled 2-planet `Universe^` — `techlevel=6`, matching
`techlevel.golden`. Rebuilt from scratch (`build.ps1` deletes `patched/`,
reapplies every patch, recompiles) and reran with identical results — the
patches are complete and sufficient on their own, not dependent on whatever
`patched/` happened to contain from prior manual edits.

Generalized from that one case into a CLI driver taking all of
`TechLevelCases`' 8 cases, all 8 reproduced byte-identical to the
already-committed, transcription-based `techlevel.golden` — confirming the
extra fidelity (real `GetCapital`/`GetTech`, real `Emp=Indep` check) didn't
silently change any of the previously-verified outcomes, before wiring it in
as `techlevel.golden`'s new source of truth via `PatchHarness.cs` and
`GoldenFileTests.RegenerateAllGoldenFiles`.

Second domain, `military`: added a domain selector to the same `runworld.pas`
rather than a second driver (see "Layout" above), retiring
`reference/verify/military.pas`'s isolated `UpdateMilitaryScenario` call in
favor of running the real `UpdateWorld` end to end. This also removed
`MilitaryCase`'s old `HarnessPop` field — the isolated harness needed a
hand-derived post-`UpdatePopulation` value fed in separately from the C#
side's pre-tick `PlanetPop` (and per-case reasoning about whether
`UpdateRevolution` could still touch `Cargo.Legions` afterward); running the
real pipeline computes both for free.

Third domain, `starbase`: the first domain needing more than one world in the
`Universe^`. `SupplyLink`/`SurplusLink` resolve neighbors via `GetObject`
(`Sector[x]^[y].Obj`), which techlevel/military's scenarios never touched —
`Galaxy.InitializeSector` (already exported from the already-`USES`d `Galaxy`
unit) plus a direct `Sector[x]^[y].Obj` write for the neighbor planet was
enough, no new patches needed. Covers only `SupplyLink`/`SurplusLink`'s own
arithmetic, not every branch of the starbase economy pipeline — the rest
(eligibility filtering, `Kind`-gating, Rebellion) is pure C# logic with no
separate Pascal formula to cross-check, so `AnnualTickHandlerStarbaseTests`
covers those hardcoded instead (see that class's doc comment). Both cases
(`SupplyLinkPull`, `SurplusLinkPush`) reproduced byte-identical to this
session's own hand-derivation of the formula, confirming the C# port against
the real Pascal source rather than just this session's own reading of it.

Fourth domain, `ambrosia`: same `HarnessPop`-elimination pattern as `military` —
retired `reference/verify/ambrosia.pas`'s isolated `UseUpAmbrosiaScenario` call
in favor of the real `UpdateWorld`, which let `AmbrosiaCase` drop both its old
`HarnessPop` field and its `RevIndexStart` field (the isolated harness's own
`revindex` output was never asserted on by `MatchesGoldenFile` in the first
place, and the real pipeline's own `UpdateRevolution` run determines the
starting value now, not a hand-fed one). All 6 `AmbrosiaCases` reproduced
byte-identical to the prior transcription-based `ambrosia.golden`, aside from
that dropped field.

Fifth domain, `revolution`: same pattern again, retiring `revolution.pas`'s
isolated `UpdateRevolutionScenario`/`RebellionScenario` pair (which chained
`common.pas`'s `UpdateMilitaryScenario` to match `UpdateMilitary`'s real call
order — now dead, deleted along with its `OptMilitary` table). This is the
domain that actually earned its keep: it caught a real gap shared by both the
isolated harness and the C# port (neither modeled `UpdateIndustry`/
`Production`'s `ReportPlanetLack` calls, a real +1 `RevolutionIndex` bump —
see `docs/ROADMAP.md`'s Commit 1 bullet for the fix). Needed a second
test-only observability hook alongside `ForcedRandomValue`:
`GetNewTotalRevIndex(Emp)`, exposing `NewTotalRevIndex` (an `Empire`-indexed
accumulator `Rebellion` writes to, declared in `UPDATE.PAS`'s own
`IMPLEMENTATION` section and normally committed by `UpdateEmpire`, a later
commit unreachable from `UpdateWorld`) — read-only, no behavior change. That
accumulator is also never reset between cases in the same batched CLI
invocation (only the never-called `UpdateUniverse` zeroes it), so
`RunRevolutionCase` reads it before and after `UpdateWorld` and reports the
difference rather than the raw value — a naive absolute read looked like a
second bug (values compounding across cases) until this fixed it. All 4
`RevolutionCases` reproduced byte-identical to the prior transcription-based
`revolution.golden` except `revindex` (+1 in every case, the real fix, not a
regression).

Sixth domain, `production`: retired `production.pas`'s isolated `FullPipeline`
(`ProduceRawMaterial`/`GetIndustrialDistribution`/`UpdateIndustry`/`Production`
only) for the real `UpdateWorld` — the last domain still on the transcription
pattern, so `common.pas` (its only remaining shared dependency) was deleted
alongside it. This one caught two bugs, but both were in the *harness*, not
the C# port or the old transcription: `runworld.pas` initially left the
planet's owning empire with an empty `TechnologySet`, which — because
`UpdateWorld` intersects it with `TechDev[Tech]` to gate production
(`UPDATE.PAS:1367-1368`) — silently zeroed out raw-material production
entirely; and it left the planet's ISSP dial (`ImpExp`) at its
`FillChar`-zeroed value instead of `DefaultISSP` (`$5555`,
`DATACNST.PAS:516`), which every real planet gets at settlement
(`PRIMINTR.PAS:631`) and which `GetIndustrialDistribution`'s sqrt-based
formulas are sensitive to. Both states are unreachable in real gameplay
(`CreateEmpire` always seeds a new empire's `Technology` from
`TechDev[Pred(Tech)]`, `NEWGAME.PAS:1203,1240`) — confirmed before trusting
the fix, not assumed. Once both were set unconditionally, industry levels,
ship counts, and `Cargo.Trillum`/`TrillumReserve` reproduced byte-identical to
the prior transcription-based `production.golden` for all 4 `ProductionCases`.

It also surfaced a real, previously-invisible gap in the *C# port*: the real
`UpdateWorld` calls `UpdateDefenses` (`UPDATE.PAS:1278-1351`), which draws down
`Cargo[che..tri]` building defenses toward a population-driven target —
something the old isolated `FullPipeline` never modeled (it never called
`UpdateDefenses` at all) and `AnnualTickHandler.RunAnnualTick` doesn't call yet
(`UpdateDefenses` is still on the roadmap, unimplemented). This widened
`AnnualTickHandlerProductionTests.MatchesGoldenFile`'s exclusion list:
`Cargo.Chemicals` and `Cargo.Metals` join the already-excluded
`Cargo.Supplies`/`Cargo.Ambrosia`/`Cargo.Legions`, for the same reason each of
those was already excluded — a real UpdateWorld step this tick that the C#
port doesn't yet run.

Seventh domain, `empire`: the first domain that isn't a `UpdateWorld` sub-branch — `UpdateEmpire`
(and its nested `NewTechLevel`/`GetChanceForNewTech`/`GetNewTech`) had been deleted from the patched
`UPDATE.PAS` entirely, back when the patch-based lane first stood up `UpdateWorld` as callable,
since nothing reachable from `UpdateWorld` needed it. Restoring it required regenerating the whole
`UPDATE.PAS.patch` (not hand-splicing hunks — safer to diff the pristine source against a fully
hand-edited target and let `git diff --no-index` produce the new patch, then strip its two
extended-format header lines per the `git apply` gotcha noted elsewhere in this session's memory)
rather than adding a new domain to an unchanged patch set, since this domain's target procedure
wasn't exported yet. `runworld.pas`'s `empire` domain calls the now-exported `UpdateEmpire` directly
— not `UpdateWorld` — since `NewTechLevel` is empire-level, not per-world, so there's no need to run
a full per-planet tick to exercise it. `Empire.Technology` is encoded as a 26-bit mask (bit *i* =
`TechnologyTypes(i+1)`, matching Pascal's own enum-declaration order) since the CLI's field parser
only handles plain integers.

All 5 `EmpireCases` reproduced exactly what hand-derivation against the real `Trunc`/`Rnd` formulas
predicted before any code ran — confirmed case by case against a manually-invoked, freshly-compiled
`runworld.exe` before wiring the C# side, catching one thing along the way: a static-field
initialization-order bug on the C# side (a lazy-vs-eager field-initializer issue across two partial
class files), not a Pascal-side finding, but exactly the kind of thing this session's "verify against
real behavior before trusting a golden value" discipline is meant to catch either direction. Also
surfaced a genuine harness-design limit: a fractional lab `Efficiency` (needed to tell `Trunc` from
`Round` in `GetChanceForNewTech`'s formula — every other case uses `Efficiency=100`, where the two
are indistinguishable) can't be a shared golden-file case, because the C# test can only reach the
private `NewTechLevel` via the full `RunAnnualTick` — which runs Commit 3's `UpdateEfficiency` on
every lab planet first, growing a non-100 `Efficiency` by an RNG-dependent amount before
`NewTechLevel` reads it — while `runworld.pas`'s `empire` domain calls `UpdateEmpire` directly and
never touches `Efficiency` at all. Landed as a hardcoded C# test instead, with its expected value
still confirmed against a real Pascal run first (see `AnnualTickHandlerEmpireTests.
FractionalLabChanceTruncatesNotRounds`), rather than silently trusting the C# formula alone.

Eighth domain, `construction`: `UpdateConstruction` (nested `UseUpRawMaterial`) plus
`ConstructStarbase`/`ConstructStargate` had been deleted from the patched `UPDATE.PAS` for the same
reason `UpdateEmpire` had — unreachable from `UpdateWorld` when the patch-based lane first stood up.
Restoring them required a second whole-`UPDATE.PAS.patch` regeneration, same technique as `empire`'s:
diff the pristine source against a fully hand-edited target, strip the two extended-format header
lines, verify the new patch reapplies byte-identical to the hand-edited target before installing it.
This restoration also relocated five small `Intrface`-only helpers (`NextStarbaseSlot`,
`CreateStarbase`(Pascal), `NextStargateSlot`, `CreateStargate`(Pascal), `GetOptimumIndus`) verbatim
into `UPDATE.PAS`, the same dodge `GetIndustrialDistribution` used earlier to avoid pulling in
`Fleet`/`Orders`/`NPE` via the real `Intrface` unit. `runworld.pas`'s `construction` domain calls the
now-exported `UpdateConstruction` directly, assembling a `Constr[1]` site plus up to two fleets at the
same location. First run crashed with a runtime error 216 (access violation): `PutMine`/
`CreateStarbase`/`CreateStargate` all touch `Sector[x]^[y]`, and unlike `starbase`'s domain this one
never called `Galaxy.InitializeSector` — same class of gotcha `starbase` had already hit and
documented, fixed the same way.

All 6 `ConstructionCases` reproduced exactly what hand-derivation against `ConsCargoNeeded`'s raw
material thresholds predicted, confirmed case by case against a manually-invoked, freshly-compiled
`runworld.exe` before any C# test code was written — including the scratch-copy-discard behavior
(`InsufficientMaterialLeavesEverythingUnchanged`) and the completion branches for all three dispatch
targets (mine, starbase, stargate).

Ninth domain, `empirecreate` (Phase 2 commit 2b): unlike every domain before it, this one needed no
new patch at all — `CreateEmpire` (`PRIMINTR.PAS:982`) was already in that unit's own `INTERFACE`
section, since it's `NEWGAME.PAS`'s own real call target. Rather than pull in all of `NEWGAME.PAS`
(and its much larger `USES` clause — `Crt`/`Dos`/`DOS2`/`EIO`/`WND`/`Menu`/`DFA`/`LoadSave`/`NPE`/
`NPETypes`) just to reach `CreatePlayerEmpire`/`CreateNPEmpire`, `runworld.pas`'s `empirecreate`
domain reproduces their 3-line starting-tech-set formula inline (`KnownTechs:=TechDev[Pred(Tech)];
KnownTechs:=KnownTechs+extras; KnownTechs:=KnownTechs*TechDev[Tech]`) and calls the real `CreateEmpire`
directly with the result — the same "relocate the small formula, not the whole unit" precedent
`GetIndustrialDistribution` set. No RNG anywhere in this domain; both the formula and `CreateEmpire`
are fully deterministic.

13 cases exhaustively cross-check all 11 rows of Pascal's real `TechDev` constant
(`DATACNST.PAS:359-370`) against the C# port's own `TechCatalog` min-tech tables (ten "no extra
techs" cases, one per level from `Primitive` through `Gate` — with no extras, the result always
collapses to exactly `TechDev[Pred(Tech)]`), plus both directions of the final intersect-clamp
(`ExtraTechAtCurrentLevel` survives, `ExtraTechAboveCurrentLevel` gets silently dropped) and one case
confirming `TechDev[Gate]` itself is the full 26-item set (`ExtraTechCompletesTopRow`). Every value
was confirmed by hand against a manually-invoked, freshly-compiled `runworld.exe` before any C# test
code was written, then matched byte-for-byte once wired in. `TechLevel.PreTech` is deliberately not
a case here — `Pred(PreTchLvl)` is an out-of-range `TechDev` index in real Pascal (a range-check
error that would crash the harness, not a well-defined empty set), and no real scenario file ever
creates a player/NPE empire at that level anyway; it's covered by a hardcoded C# test instead.

Tenth domain, `trillumreserves`/`randomplanet`/`nebula` (Phase 2 commit 2d): relocated
`CreatePlanet` (`INTRFACE.PAS`) plus `RandomTrillumReserves`/`RndShips`/`RndCargo`/`RndDefns`/
`SetUpWorld`/`CreateRndPlanet`/`NebulaeBand`/`NebulaePatches` (`NEWGAME.PAS`) verbatim into the
patched `UPDATE.PAS`, same "relocate the small formula, not the whole unit" precedent as every domain
before it — all of them are self-contained `Universe^`/`Misc`/`DataCnst`/`PrimIntr` logic, already
confirmed reachable from `UPDATE.PAS`'s existing `USES` clause with zero new unit imports needed.
`GetRandomXY`/`CreateRandomWorlds` are deliberately **not** relocated: under this harness's
`ForcedRandomValue` convention, every `Rnd` call within one invocation returns the same fixed offset,
so a coordinate blocked on the first roll is blocked on every retry too (the C# side hit the identical
wall — see `GalaxySetupTests.GetRandomXY_ThrowsAfterTooManyRetriesOnAnOccupiedCell`) — there is no way
to construct a golden-file case that reaches "blocked, then a later retry succeeds" on either side of
the comparison, on principle, not from lack of trying. Those two procedures' own logic (percentile
table lookups, the safety-loop compatibility check, occupancy/nebula rejection) is simple enough that
hand-derived hardcoded tests (`GalaxySetupTests`) fully cover it instead — the golden-file domains here
target only the pieces with real formula risk: `CreateRndPlanet`'s `Trunc`/`Round`/tech-adjustment-table
math and `NebulaeBand`/`NebulaePatches`' coordinate-space arithmetic.

All three domains reproduced this session's own hand-derivations exactly on the first real Pascal run
(`randomplanet`'s `EarthLikeWarpRng0` case matched `GalaxySetupTests.
CreateRndPlanet_ComputesPopulationMilitaryIndexAndAppliesTechGate` field-for-field; `nebula`'s band/patch
single-case grids matched their own hardcoded tests) — good confirmation the C# port and the relocated
Pascal agree, but a multi-patch nebula case (`PatchesMultipleRng2`, three patches under one forced
value) caught a real bug in this harness's own `ForcedRandomValue` test convention, not in the C# port:

**A real landmine: `ForcedRandomValue` was checked in the wrong order.** The original `INT.PAS` patch
had `Rnd` check `ForcedRandomValue>=0` *before* the real, unconditional `Max<=Min` degenerate-range
clamp — so a forced value could override that clamp, producing a result outside `[Min,Max]` whenever
`Min=Max` coincided with a nonzero forced offset. `NebulaePatches`' own `Rnd(1,4-Abs(y-InitY))` hits
exactly `Min=Max` (`4-Abs(y-InitY)=1`) at a patch's vertical extremes, so `PatchesMultipleRng2` (forced
value 2) exposed it: Pascal painted a full 6th row that `GalaxySetup.NebulaePatches` (via
`PascalMath.Rnd`, which has always checked the degenerate range first, matching pristine `Rnd`'s own
"If Min>Max then Min is returned" semantics) correctly did not. The fix went into the *Pascal test
patch*, not `PascalMath.Rnd` — pristine `Rnd`'s degenerate-range clamp is real, unconditional behavior;
`ForcedRandomValue` is a test-only device layered on top of it, so the clamp has to win first, the same
order `PascalMath.Rnd`+`FixedRandom` already used. Reordering `INT.PAS.patch` to check `Max<=Min`
before `ForcedRandomValue` fixed `nebula.golden` and, checked directly via `git status`, moved none of
the other eleven already-committed golden files — confirming no earlier domain's cases had
coincidentally depended on the wrong ordering. A tempting-but-wrong fix worth naming explicitly: making
`PascalMath.Rnd` call through to `random.Next()` even when `min==max` (so `FixedRandom`'s override
would "win" the same way the old, buggy `INT.PAS` ordering did) breaks `NewTechLevel`'s
`missingAtCurrentLevel[Rnd(1, missingAtCurrentLevel.Count) - 1]` indexing the moment a shared
`FixedRandom` offset exceeds a small `Count` — real Pascal's own equivalent (`Tech[Rnd(1,TechNumber)]`
against a fixed `ARRAY[1..30]`) never crashes on this, it just reads a stale slot, since the array is
statically over-provisioned; a C# `List` has no such slack. Confirmed empirically (four pre-existing
Phase-1 tests broke instantly when this alternate fix was tried) before reverting it in favor of the
`INT.PAS` reorder above.

## A real Pascal RNG, not a stand-in: `rng.golden` and `PascalRandom`

Every domain above needs only one `Rnd()` value per case, so `ForcedRandomValue` (a fixed offset every
call resolves to) has always been enough. Phase 2 commit 2e's `CREATERANDOMWORLDS` breaks that: it
retries a random coordinate on collision, and a fixed offset always re-rolls the *same* coordinate, so
placing a second world in a zone that already has one always blows through the 100-retry cap on both
sides. Comparing real `.SCN` scenario files end to end needs a real, non-degenerate multi-call sequence
instead — which means matching this project's actual fpc runtime's `Random`/`RandSeed` algorithm, not
a fixed stand-in.

That algorithm isn't the classic Turbo Pascal LCG a DOS-era codebase might suggest, and guessing at it
from memory would have been exactly the kind of unverified recall this project avoids: fpc's RNG
implementation changed over its history, and which one a given installed compiler uses has to be
checked, not assumed. A quick probe program (`RandSeed:=12345; WriteLn(Random(100));` a few times)
compiled with this repo's actual installed fpc (3.2.2) and compared against candidate algorithms pulled
from fpc's own RTL source at matching tags settled it empirically: fpc's `main`/trunk source now uses a
SplitMix64-seeded Xoshiro128** generator (didn't match); the `release_3_2_2` tag's `rtl/inc/system.inc`
uses a Mersenne Twister (MT19937) variant with its own reseed/tempering convention (`mtwist_init`/
`mtwist_update_state`/`mtwist_u32rand` — matched exactly, including a mid-run reseed, a fresh-seed
replay, and a draw crossing the generator's 624-word internal state refill).

`src/ThreeLn.Reconstruction4021.Tests/PascalRandom.cs` is a from-scratch `System.Random` subclass
porting that exact algorithm, test-only (production code has no need for Pascal-bit-exact randomness —
only a golden-file comparison does). `rng.golden`/`RngCases.cs`/`PascalRandomTests.cs` are a standing
regression fixture for it: `runworld.pas`'s `RunRngCase` sets a real `RandSeed` and draws a real
sequence via `Random()` (no `ForcedRandomValue` involved at all), and `PascalRandomTests.MatchesGoldenFile`
checks the C# port reproduces it exactly, including a 701-draw case that crosses the state refill
boundary. This isn't a `UpdateWorld`/`GalaxySetup` domain — it exists so that any future domain needing
a real RNG sequence (2e's `CREATERANDOMWORLDS`, and potentially a genuine end-to-end `.SCN` file load)
can build on an already-verified foundation instead of re-deriving it under pressure.

## `build.ps1`/`PatchHarness.cs` compile with `-CfSSE2`

Phase 2 commit 2e's scenario domain (see below) found that fpc's default i386 codegen keeps chained
`Real` expressions in the x87 FPU's 80-bit extended-precision stack until explicitly stored, while
C#'s `double` is always strict 64-bit IEEE754 — so a borderline expression can round to a different
integer in each language. `-CfSSE2` forces fpc to use strict 64-bit double throughout, aligning the
harness with the only precision C# has. Verified via `git diff --stat` on the regenerated golden
files that it changed none of the 13 golden domains existing at the switch — but that's a snapshot,
not a guarantee: it changes float semantics harness-wide, so a future domain with its own borderline
`Real` expression will get different ground truth under it than under fpc's default. Kept as a
deliberate baseline choice regardless. Full writeup of what this does and doesn't fix: the root
`README.md`'s "Known limitation: scenario golden-file testing can't be bit-exact" section.

## Recommendation

Reach for this approach whenever it would improve testing fidelity, with an
eye toward eventually building up a maximum-fidelity harness — the
per-harness patch-authoring cost is accepted deliberately, in exchange for
being able to run the real Pascal code against known states across wide
slices of the game systems as those slices grow, not just the one procedure
under test:

- That's most clearly the case when a procedure's fidelity risk is high
  enough to justify it: many state-shaped lookups (`GetCapital`/`GetTech`-style
  calls that transcription would otherwise have to simplify into plain
  parameters), or when the thing worth testing is call *ordering* across
  multiple steps in the same real pipeline — exactly the "cross-cutting
  field" bug class hit twice with transcription (`UpdateMilitary` mutating
  `Cargo.Legions` before `UpdateRevolution` reads it, discovered only because
  the isolated harnesses didn't model the mutation).
- Transcription (`reference/verify/*.pas`'s per-procedure pattern) is still
  fine for genuinely isolated, parameter-only procedures with no real-state
  dependency — cheap and bounded, and proven across four roadmap commits
  before `production.golden`'s migration retired its last domain. Nothing
  currently uses it, but reach for it again if a future case fits that
  description better than a hand-assembled `Universe^` would.
- Don't expand this into combat/fleet movement/NPE AI territory by default.
  `Intrface`'s `Fleet`/`Orders`/`NPE` dependency was dodged here by relocating
  one function; the next subsystem's dependency web is an open question, not
  something this session's results generalize to. Treat each new area as its
  own exploration, not an assumed extension of this one.
- A hand-assembled `Universe^` is still only as faithful as the fields it
  remembers to set. `production.golden`'s two harness bugs (an empty
  `TechnologySet`, an unset `ImpExp`) were both "field defaults to zero
  instead of what `CreateEmpire`/settlement actually initializes it to" —
  worth a deliberate check against real init code (`CreateEmpire`,
  `PRIMINTR.PAS`'s `Set*` procedures) for any new domain's setup, not just
  trusting `FillChar` zero to be a reachable state.
