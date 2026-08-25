# Port roadmap

Bottom-up order: simulation core first, UI last. Completed phases aren't listed here — the git log
is the record of what's done. Each phase gets its own plan/design pass when it's picked up; this
file only tracks the sequence and the design decisions/scope calls that need to be made up front.

## 1. Economy / annual tick — ✅ done, all 5 commits landed

Implement `IAnnualTickHandler`. `UpdateUniverse` (UPDATE.PAS:1440-1488) runs, in order: `Year++`,
then `UpdateWorld` over every planet and starbase, then `UpdateConstruction`, then `UpdateEmpire`.
This phase lands `UpdateWorld` for planets first; starbases, construction, and empire-level updates
follow once that's working. Next up: Phase 2, Galaxy / new-game setup, below.

**Design decisions, resolved:**

- **RNG strategy.** Inject a `Random` (same pattern as `VisibilityHandler`'s constructor param, no
  default). Tests use a `Random` subclass fixing `Next`'s return for deterministic branches, and
  assert bounds/invariants (e.g. "efficiency never exceeds 100") for the genuinely-random magnitude
  ones. Translation trap: Pascal's `Rnd(lo,hi)` is inclusive on both ends — `Rnd(2,5)` is
  `random.Next(2, 6)` in C#, not `random.Next(2, 5)`. Dozens of these calls are coming; check every one.
- **Planet/starbase overlap — deferred, not decided now.** Read the starbase branch of `UpdateWorld`
  (UPDATE.PAS:1417-1430): every starbase runs `UpdateEfficiency`/`UpdateTechLevel`/`UpdateDefenses`
  unconditionally, but the population/food/industry/revolution pipeline only runs for
  industrial-complex starbases (`STyp=cmp`) — other kinds never touch population or economy at all,
  and even complexes get a hardcoded `ArtCls` in place of a real world-class field. The overlap is
  much smaller than "one shared procedure" suggests. Write Commits 1-3 directly against `Planet`,
  no interface — decide the shared shape at Commit 4, once the actual starbase duplication is visible
  instead of guessed at from one call site.
- **`Empire.TotalRevolutionIndex` — genuine stored state, not derived.** `UpdateRevolution` reads
  `TotalRevIndex(Emp)` mid-tick (last year's committed value) to influence the current world's
  revolution-index change, while `Rebellion` writes deltas into a separate scratch accumulator
  `NewTotalRevIndex[Emp]` (independent ± counter driven by rebellion outcomes, not a sum of per-world
  indices). `UpdateEmpire` commits the scratch value at the end of the tick. This is a
  snapshot/accumulate/commit pattern across the whole world loop, not something a live sum could
  reproduce (reading a "current total" partway through would be order-dependent on which worlds had
  already been processed). Implement `TotalRevolutionIndex` as real state updated the same way.
- **`Empire.Technology` (`UnlockedTechnology`) gains a `Resources` bucket (Commit 5a).** Originally
  split into `Ships`/`Defenses`/`Constructions` only, on the reasoning that "a cargo type is
  produced, not unlocked." That was true only because nothing yet grew the set incrementally —
  `NewTechLevel`'s outer guard is a real equality check against Pascal's `TechSet`, which spans
  resource types too, so a 4th bucket (`HashSet<CargoType>`) is required for that guard to ever be
  correct, not a speculative widening. Production's own `ShipTechAvailable`/`CargoTechAvailable`
  still use the pre-existing TechLevel-derived heuristic, deliberately not rewired to this set yet —
  see Commit 5a's own notes below for why.

**Commits, in order:**

1. ✅ **Population, efficiency, revolution** (planets only) — `UpdateEfficiency` (UPDATE.PAS:1381,
   capped at 100) → `UpdatePopulation` (UPDATE.PAS:1074-1116, `MaxPop` table per world class,
   exponential/linear growth) → `UseUpFood` (UPDATE.PAS:1118-1161 — starvation reduces population
   *and* raises revolution index; these two are coupled, don't split them) → `UpdateRevolution`
   (full body, including the military-suppression branch and `Rebellion`). `HostileLife` shipped
   with this commit too (cheap, sits right after `UpdateRevolution` in Pascal) though it isn't its
   own roadmap line. Untested branch, faithful to source but never exercised: `HostileLife` — add
   coverage if a future change touches it. `AnnualTickHandlerTests` covers population/efficiency/food
   (hardcoded — no sqrt/pow cascade or other formula that's error-prone to hand-verify);
   `AnnualTickHandlerRevolutionTests` covers revolution/rebellion, golden-file-backed (see below),
   including the previously-untested military-suppression path (`Military>OptimumMilitary`,
   UPDATE.PAS:715-735). Migrating `revolution.golden` to the patch-based lane (2026-08-22, see below)
   surfaced a real gap here, dormant since this commit shipped: `UpdateIndustry`/`Production`'s
   raw-material-shortfall checks call Pascal's `ReportPlanetLack` (UPDATE.PAS:39-56), which bumps
   `RevolutionIndex` by 1 the first time a given resource type is reported short in a tick (a real
   state mutation, not just a skipped `AddNews` call) — never implemented in the port. Fixed via
   `ReportResourceShortfall` (`AnnualTickHandler.Production.cs`), a per-tick `HashSet<CargoType>`
   mirroring Pascal's `OtherReports`, threaded through `UpdateIndustry` (fires for both planets and
   starbases) and `ApplyRawMaterialConstraint`/`Production` (planets only, per `IEconomicWorld.IsPlanet`
   — UPDATE.PAS:904's guard on that specific call site).
2. ✅ **Industry and production** (planets only) — `ProduceRawMaterial`, `GetIndustrialDistribution`,
   `UpdateIndustry`, `Production` (UPDATE.PAS:1375-1379; production formula at 844-925), inserted
   before `UpdateEfficiency` in `UpdateWorld` (Pascal runs the whole production pipeline first).
   Ship/cargo tech-gating is TechLevel-derived (via a per-item "minimum tech level" table built from
   `TechDev`'s monotonic structure) rather than modeling per-empire incremental research — nothing
   seeds or grows `Empire.Technology.Ships` yet (that needs both new-game setup, Phase 2, and
   `NewTechLevel`'s per-tick research rolls, Commit 5 below), so ship production is correct but inert
   until those land. `AnnualTickHandlerProductionTests` — Pop=1000/Class=EthCls/Tech=Gate cases
   deliberately exercise the sqrt/pow cascade in `GetIndustrialDistribution`, infeasible to hand-trace
   reliably — is golden-file-backed (see below), originally via `reference/verify/production.pas`'s
   `FullPipeline`, migrated to the patch-based lane 2026-08-22 (see below) once it was the only domain
   left on transcription. The original transcription-based setup caught two real bugs:
   `SelfSufficiencySettings` defaulted to 0 instead of Pascal's `InitializeISSP` default of 5 (badly
   distorting `GetIndustrialDistribution`'s sqrt terms), and FreePascal's built-in `Round()` is banker's
   rounding, not Turbo Pascal's round-half-away-from-zero (fixed with a `PascalRound` helper) — both
   missed by hand-tracing.
   - ✅ **Commit 2b, ambrosia addiction** — `UseUpAmbrosia` (UPDATE.PAS:1163-1276), inserted between
     `UseUpFood` and `UpdateRevolution`. Makes `IsAddictedToAmbrosia` real state instead of a
     permanently-false flag Commit 2's own production pipeline already read (`AmbrosiaAdj` in
     `GetIndustrialDistribution`/`UpdateIndustry`). `AnnualTickHandlerAmbrosiaTests` covers addiction
     onset and the shortage-death/efficiency/riot/tech-regression branches, golden-file-backed (see
     below); two guard tests (never-decrement-below-`PreTchLvl`, no-ambrosia-cargo no-op) stay
     hardcoded since they assert control flow, not arithmetic. One sub-branch (industrial sabotage,
     UPDATE.PAS:1226-1234 — destroys `Trunc(level*Rnd(0,20)/100)` off every industry type) is
     faithful to source but not covered by any case: `Industry` starts at 0 on every test planet, and
     `RunProductionPipeline`'s own industry growth earlier in the same tick is infeasible to
     hand-trace on top of the shortage math (same reason the production tests lean on the Pascal
     harness instead of hand-derivation) — add coverage if it's ever touched.
   - ✅ **Golden-file ground truth.** All six ground-truth domains (`techlevel`, `military`, `starbase`,
     `ambrosia`, `revolution`, `production`) now run through the patch-based lane's real `UpdateWorld`
     (`reference/verify/`, see the "Ground-truth harness generation" section below) rather
     than a from-source transcription — `production.golden` was the last domain migrated (2026-08-22),
     retiring `reference/verify/production.pas`'s `FullPipeline` and its shared `common.pas` dependency.
     `GoldenFileTests` (runs in the default `dotnet test` suite, dynamically skipped when fpc/git aren't
     on PATH) runs each domain via a shared `GoldenFile.Regenerate` helper and writes a committed
     `reference/verify/golden/*.golden` file (`case=Name;key=value;...` lines). The always-on
     `AnnualTickHandler{Ambrosia,Revolution,Production,...}Tests.MatchesGoldenFile` tests (data-driven
     via each domain's `*Cases.AsDataSource` and TUnit's `[MethodDataSource]`) read that file and assert
     the C# port against it — so no hand-typed expected value can silently agree with the same mistake
     on both sides of a check. Regenerate a golden file (review the diff, then commit) whenever its
     `*Cases.All` or the patch-based driver's relevant domain changes. `production.golden` excludes
     `Cargo.Supplies`/`Cargo.Ambrosia`/`Cargo.Legions`/`Cargo.Chemicals`/`Cargo.Metals` (all mutated
     later in the same real `UpdateWorld` tick by steps `AnnualTickHandler.RunAnnualTick` doesn't run,
     or doesn't run yet — the last two because `UpdateDefenses`, UPDATE.PAS:1278-1351, isn't ported) —
     see each domain's test-class doc comment for the exact scope.
   - ✅ **Commit 2c, military buildup** — `UpdateMilitary` (UPDATE.PAS:606-617), inserted between
     `UseUpAmbrosia` and `UpdateRevolution` (runs unconditionally for planets — the same insertion
     point `AnnualTickHandler.UpdateWorld`'s doc comment already marks). Small and self-contained:
     reuses the `OptimumMilitary` formula already implemented inline in `UpdateRevolution`'s
     suppression branch, and just grows `Cargo.Legions` toward it — no new tables needed. Not a
     combat-phase concern despite the name; it's a plain per-tick economy step that happens to feed
     numbers combat will later read. Golden-file-backed (`military.pas`/`military.golden`,
     `MilitaryCases`/`AnnualTickHandlerMilitaryTests`), including a case that exercises the
     `MaxResources` clamp on `OptimumMilitary` itself (`OptMilitary[Base]`=200% pushes the raw figure
     to 20000 before clamping). Landing this exposed a real cross-cutting gap in `revolution.pas`'s
     ground truth: it modeled `Cargo[men]` as untouched between population growth and
     `UpdateRevolution`, which stopped being true once `UpdateMilitary` runs in between in the real
     pipeline (UPDATE.PAS:1386-1388) — fixed by moving `UpdateMilitaryScenario` into `common.pas` and
     having `revolution.pas` chain it first, matching the real call order (see `RevolutionCases`'s doc
     comment). Also prompted a naming cleanup while in this file: the transcribed-from-Pascal helper
     names `ThgLmt`/`RndVar` read as noise to anyone without the Pascal source open, so
     `AnnualTickHandler`'s private helpers are now `ClampResource`/`Jitter`, with the original Pascal
     names preserved in each one's xmldoc instead of in the identifier.
3. ✅ **Tech advancement** (planets only) — `UpdateTechLevel` (UPDATE.PAS:1032-1072), inserted between
   `UpdateEfficiency` and `UpdatePopulation` (the same insertion point `AnnualTickHandler.UpdateWorld`'s
   doc comment already marked). Independent worlds drift upward on their own (1-in-50/tick); owned
   worlds chase their empire's capital tech level up (`TechLvlInc`=16% chance/tick, DATACNST.PAS:61)
   or down (1-in-15/tick) — `Empire.Capital` already existed as a `Planet?` from the data-model phase,
   so no new state was needed. A null `Owner.Capital` (a state Pascal's `GetCapital` can't produce for
   a real, founded empire) is a defensive no-op, covered by one hardcoded guard test; every other
   branch is golden-file-backed (`techlevel.golden`, `TechLevelCases`/`AnnualTickHandlerTechLevelTests`)
   since UpdateTechLevel's own formula never reads Population, unlike Revolution/Military — no
   `PlanetPop`/`HarnessPop` split was needed here. `techlevel.golden` was the first domain moved to the
   patch-based lane (below) once it landed — the real `GetCapital`/`GetTech` lookup and `Emp=Indep`
   check it depends on are exactly the kind of real-state dependency a per-procedure transcription has
   to fake.
4. ✅ **Starbase economy** — same `UpdateWorld` sequence, but with `SupplyLink`/`SurplusLink`
   (UPDATE.PAS:517-604 — raw-material redistribution with adjacent same-empire raw-material planets)
   and industrial-complex-only production and economy (`STyp=cmp` branches, lines 1403-1415 for
   production and 1420-1427 for population/food/ambrosia/military/revolution — starbases gate the
   *entire* economy pipeline, including Commit 2c's `UpdateMilitary`, on being an industrial complex,
   not just production). `UpdateDefenses` stays deferred to the combat phase (see there). Landing this
   resolved the planet/starbase overlap question flagged above: introduced `IEconomicWorld`
   (`Entities/IEconomicWorld.cs`), implemented by both `Planet` and `Starbase`, so
   `AnnualTickHandler`'s ~15 private pipeline methods run either through the same code — as its own
   pure-mechanical commit first (103/103 green, golden files diffed empty) before adding starbase
   behavior. Four members are explicit-interface-only, each encoding one PRIMINTR.PAS accessor's
   Base-case behavior a shared property can't express: `EffectiveClass` (ArtCls, no world-class field),
   `SelfSufficiencyIndex` (always index 0, no ImpExp field), `TrillumReserve` (MaxResources, writes
   discarded, no TriReserve field), `InitializeSelfSufficiency` (InitializeISSP's CASE has no Base
   branch). `AnnualTickHandlerStarbaseTests` covers `SupplyLink`/`SurplusLink` (adjacency, ownership,
   world-type eligibility, the >250/>MaxResources boundaries), `Kind`-gating, and Rebellion turning a
   complex independent while it keeps its `Kind` — all hardcoded/hand-verified, since neither Link
   procedure has a sqrt/pow cascade and `Cargo.Metals` starting at 0 makes `GetIndustrialDistribution`'s
   own cascade irrelevant to what these tests check. `SupplyLink`/`SurplusLink`'s own arithmetic — the
   one part of Commit 4 with real Pascal-transcription risk — is additionally golden-file-backed via
   `runworld.pas`'s `starbase` domain (`Galaxy.InitializeSector` + a direct `Sector[x]^[y].Obj` write
   for the neighbor planet, so `GetObject` can resolve it; no new patches needed, `InitializeSector` was
   already exported from the already-`USES`d `Galaxy` unit): both cases (`SupplyLinkPull`,
   `SurplusLinkPush`) reproduced byte-identical to this session's own hand-derivation, confirming the
   C# translation against the real Pascal formula rather than just this session's reading of it.
5. **Construction and empire-level updates** — `UpdateConstruction` and `UpdateEmpire`
   (UPDATE.PAS:103-434). Split into two sub-commits once investigation showed they bundle very
   different dependency risk: `UpdateEmpire` is landable with existing infra, same shape as every
   prior commit; `UpdateConstruction` needs new entity-creation helpers (Starbase/Stargate) that
   nothing before it required.
   - ✅ **Commit 5a, empire-level tech research** — `UpdateEmpire`'s other line
     (`SetTotalRevIndex(Emp,NewTotalRevIndex[Emp])`) was already pulled forward into Commit 1's
     `RunAnnualTick`; this commit lands `UpdateEmpire`'s only remaining behavior, `NewTechLevel`
     (nested `GetChanceForNewTech`/`GetNewTech`, UPDATE.PAS:224-428) — a per-empire roll each tick to
     unlock one more item into `Empire.Technology`, then a separate roll to advance
     `Empire.TechnologyLevel` once the current level's full `TechDev` set is unlocked.
     `Empire.Technology` (`UnlockedTechnology`) gained a 4th bucket, `HashSet<CargoType> Resources` —
     Pascal's `TechSet` spans ships/defenses/constructions *and* resource types in one set, and
     `NewTechLevel`'s outer guard is a real equality check across all four; without a resource bucket
     the guard could never detect "still missing a resource-type unlock," a genuine correctness gap,
     not a speculative widening. `TechDev[Tech]` per category is reconstructed from two new min-tech-
     level tables (`_minTechForDefense`/`_minTechForConstruction`, `AnnualTickHandler.Production.cs`,
     alongside the existing `_minTechForCargo`/`_minTechForShip`) rather than storing all 11 raw
     Pascal sets — valid only because `TechDev` is genuinely monotonic in `TechLevel`, verified by
     expanding all 11 rows to explicit enum-position membership (not assumed; an earlier pass
     mis-expanded one range and got `ion`'s first appearance wrong as a result — recomputed
     rigorously and cross-checked against the two tables that already existed). Deliberately does
     **not** rewire the existing `ShipTechAvailable`/`CargoTechAvailable` production gating
     (`AnnualTickHandler.Production.cs`) to consume this new real set — they stay on the TechLevel-
     derived heuristic for now, so `Empire.Technology.Ships`/`Defenses`/`Constructions` grow via real
     research but production doesn't read them yet; a legitimate, explicitly deferred follow-up, kept
     out of this commit to avoid re-touching Commits 2-4's already golden-file-verified code.
     Golden-file-backed (`empire.golden`, `EmpireCases`/`AnnualTickHandlerEmpireTests`) via a new
     `empire` domain in `runworld.pas` that calls the now-restored, exported `UpdateEmpire` directly
     (restored in `UPDATE.PAS.patch` — it had been deleted as unreachable from `UpdateWorld` back when
     `techlevel.golden` first established the patch-based lane). One hardcoded test
     (`FractionalLabChanceTruncatesNotRounds`) covers the one thing the golden-file harness
     structurally can't: a non-100 lab `Efficiency`, needed to tell `Trunc` from `Round` in
     `GetChanceForNewTech`'s `Trunc(percent*eff/100)` — `runworld.pas`'s `empire` domain calls
     `UpdateEmpire` directly and never runs `UpdateEfficiency`, but the C# test can only reach the
     private `NewTechLevel` via the full `RunAnnualTick`, which *does* run `UpdateEfficiency` on every
     lab planet first, growing a non-100 starting `Efficiency` by a RNG-dependent amount before
     `NewTechLevel` ever reads it — makes a shared Pascal-CLI-arg/C#-fixture `Efficiency` field
     impossible for that one scenario, so it's hardcoded instead (its expected value still confirmed
     against a real Pascal run, independently, before being asserted).
   - ✅ **Commit 5b, construction** — `UpdateConstruction` (UPDATE.PAS:103-220, nested
     `UseUpRawMaterial`, UPDATE.PAS:113-170) plus `ConstructStarbase`/`ConstructStargate`
     (UPDATE.PAS:58-100, entity creation on completion). Each tick, every `ConstructionSite` draws its
     `ConsCargoNeeded` raw materials (new `_constructionCargoNeeded` table, DATACNST.PAS:539-548 — only
     Chemicals/Metals/Trillum ever nonzero) from every fleet co-located with and owned by the site, in
     Pascal's `amb TO tri` order. `UseUpRawMaterial` preserves a genuine Pascal quirk: it draws against
     a local scratch copy of each fleet's cargo first, and only commits that copy back to the real
     fleets if every cargo type clears its threshold — a shortfall partway through consumes *nothing*
     at all, not even the types already found sufficient (`goto ExitLoop` discards the whole scratch
     copy). On success the site's countdown decrements; at zero, the site is removed and dispatches on
     `ConstructionType`: `Minefield` calls the existing `Galaxy.SetMine`; `Gate`/`WarpLink`/`Disrupter`
     create a `Stargate` (`CreateStargate`); everything else (`CommandBase`/`Fortress`/
     `IndustrialComplex`/`Outpost`) creates a `Starbase` (`CreateStarbase`), with `IndustrialComplex`
     additionally populating `Industry` via a new `GetOptimumIndustry` helper (`GetOptimumIndus`,
     INTRFACE.PAS:1264-1287 — composes the existing `GetIndustrialDistribution`/`TotalProd`, and
     deliberately does **not** clamp TIP to 999 the way `GetIndustrialDistribution`/`UpdateIndustry` do,
     a real preserved Pascal asymmetry). `Stargate.LinkedTo` changed from `required Coordinate` to
     `Coordinate?`, `null` at creation — matches Pascal's `Dest:=Limbo` sentinel; establishing a real
     link is a separate, not-yet-ported mechanic. `ConstructStarbase`/`ConstructStargate` and their
     small `NextStarbaseSlot`/`CreateStarbase`(Pascal)/`NextStargateSlot`/`CreateStargate`(Pascal)/
     `GetOptimumIndus` helpers were relocated (not rewritten) from INTRFACE.PAS into the patched
     UPDATE.PAS, same rationale as `GetIndustrialDistribution`'s own earlier relocation — avoids
     pulling in `Fleet`/`Orders`/`NPE` via the real `Intrface` unit for a handful of small,
     `PrimIntr`/`DataStrc`/`Misc`-only helpers. Golden-file-backed (`construction.golden`,
     `ConstructionCases`/`AnnualTickHandlerConstructionTests`) via a new `construction` domain in
     `runworld.pas` that calls the now-restored, exported `UpdateConstruction` directly — required a
     second whole-`UPDATE.PAS.patch` regeneration (same technique as 5a's) to restore code that had
     been fully deleted, not just excluded. Hit and fixed a runtime error 216 (access violation) from
     `PutMine`/`CreateStarbase`/`CreateStargate` touching `Sector[x]^[y]` before `InitializeSector` had
     been called in the new domain — same class of gotcha the earlier `starbase` domain had already
     documented. One hardcoded test (`OnlyFleetsAtSameLocationAndOwnerContribute`) covers the pure
     C#-side LINQ filtering (no separate Pascal formula to cross-check).

**Constants to extract:** `MaxPop` array (UPDATE.PAS:1077-1098), `BasePop` lookup, `TechAdj[]`,
`TechAdj2[]`, `K6`, `SuppliesPerBillion`, `DrugsPerBillion`, `ThgLmt()` clamp function.

## 2. Galaxy / new-game setup — in progress, 2a-2d landed

Nothing in the port creates a `Game`/`Galaxy`/`Empire` from scratch yet — every existing test
hand-builds fixtures. Pascal has no procedural galaxy generator: `NEWGAME.PAS`'s `LoadScenario`
(1650-1812) reads a `.SCN` text scenario file and dispatches world/starbase/stargate/nebula/mine
placement and empire-creation commands one at a time — the scenario script *is* the generation
pipeline. `CreateEmpire` itself lives in `PRIMINTR.PAS:982-1016`, not `NEWGAME.PAS`.

Real scenario files exist under `reference/scenarios/` (`dos_131`, `dos_20`, `pack_1`, found
2026-08-23): `dos_131` is the canonical set for this phase — it matches the DOS 1.31 baseline this
whole port targets, and every one of its files is `ScenaVersion` 10, 12, or 13, squarely inside
what `NEWGAME.PAS` (1.31) is confirmed to handle. `dos_20`/`pack_1` are the 2.0 port's own set and
a later superset of it (versions up to 20, beyond even the 2.0 source's explicit branches) —
staying out of scope until an opt-in v2-parity pass is taken on. Heavy duplication exists across
the three sets (13 of ~18 unique scenarios are byte-identical between `dos_131`/`dos_20`), not
pruned yet — a candidate follow-up once 2e's parser exists to confirm duplicates programmatically.

Broken into five sub-commits:

- ✅ **2a, shared helpers.** Extracted `Rnd`/`Jitter`/`PascalRound`/`ClampResource`
  (`AnnualTickHandler.cs`) into `Core/PascalMath.cs`, and the tech-catalog (the old
  `_techCatalog`/`MissingTechAt` plus the four `_minTechFor*` tables) into
  `Core/Entities/TechCatalog.cs` — both are now genuinely shared between the annual-tick handler
  and new-game empire creation (2b), not a speculative extraction. Pure refactor, zero behavior
  change (all 124 existing tests pass, golden files byte-identical).
- ✅ **2b, empire creation** (`Core/NewGame/EmpireFactory.cs`). Ports `CreateEmpire`
  (`PRIMINTR.PAS:982-1016`)'s field-copy plus `CreatePlayerEmpire`/`CreateNPEmpire`
  (`NEWGAME.PAS:1186-1259`)'s starting tech-set formula (`TechDev[Pred(Tech)]` ∪ extras, ∩
  `TechDev[Tech]`, via `TechCatalog`) and `DefenseSettings.Fleets` seeding from `InitDefenseRecord`
  (`DATACNST.PAS:373-379` — `Starbases` stays all-zero, matching Pascal's own typed constant, which
  never sets that field either). "Extra techs" are typed against this port's own enums
  (`TechCatalog.Grant(ShipType.HunterKiller)`, etc.), not any file format's raw ordinal — a future
  scenario-file parser (2e) decodes into these, not the other way around, so the domain model never
  couples to the legacy `.SCN` numbering (see the design decisions above and the "Long-term future
  possibilities" note on a less number-heavy scenario format). Golden-file-backed
  (`empirecreate.golden`): `runworld.pas`'s new `empirecreate` domain reproduces the 3-line tech-set
  formula inline and calls the real, already-exported `CreateEmpire` directly — no need for a new
  patch, since `CreateEmpire` was already in `PRIMINTR.PAS`'s `INTERFACE`. 13 cases exhaustively
  cross-check all 11 rows of Pascal's real `TechDev` constant against `TechCatalog`'s min-tech
  tables, plus both intersect-clamp directions (an at-level extra survives, an above-level extra gets
  dropped) — every value confirmed against a real compiled `runworld.exe` run before being wired into
  the C# test. `TechLevel.PreTech` itself (no valid `Pred`) is hardcoded instead, since Pascal itself
  can't safely execute that case (an out-of-range array index, not a well-defined empty set).
- ✅ **2c, explicit-coordinate placement** (`Core/NewGame/GalaxySetup.cs`). Ports `SetUpWorld`/
  `CreateWorld`/`CreateBase`/`CreateGate`/`CreateSRMs`/`CreateNebula`, reusing
  `IEconomicWorld.InitializeSelfSufficiency()` and the existing `GetOptimumIndustry`/
  `GetIndustrialDistribution` helpers (both widened from `private` to `internal static` — no instance
  state involved, and new-game setup is a genuine second consumer). Faithfully preserves `CreateBase`'s
  real `WorldType` dispatch (`Outpost`/`CommandBase`/`Fortress` derive it; every other kind —
  `IndustrialComplex` included — reads it straight from an explicit parameter, matching
  `NEWGAME.PAS:1079-1084` exactly rather than hardcoding `Base` for every starbase kind). Surfaced a
  real type-modeling gap while doing so: Pascal's `CreateBase` can make a *starbase* the empire's
  capital (`NEWGAME.PAS:1095-1096`), which `Empire.Capital: Planet?` couldn't represent — widened to
  `Empire.Capital: IEconomicWorld?` (plus a `Location` getter on `IEconomicWorld`, since both `Planet`
  and `Starbase` already had it concretely) rather than silently dropping that case. Hardcoded tests
  only for this commit — `SetUpWorld`/`RndShips`/`RndCargo`/`RndDefns`/`RandomTrillumReserves` live in
  `NEWGAME.PAS`, whose `USES` clause is far larger than any patch has needed to reach into so far;
  relocating just those five procedures is deferred to 2d, which needs the identical relocation anyway
  (`CreateRndPlanet` calls `SetUpWorld` too) — one relocation covering both commits' golden-file needs
  beats doing it twice.
- ✅ **2d, randomized placement** (`Core/NewGame/GalaxySetup.cs`, continued). Ports `GetRandomXY`
  (transient per-call `IsGoodForRandomWorld` check — no permanent `Galaxy` spatial index, matching the
  design decision above), `CreateRndPlanet`, `CreateRandomWorlds`, and `NebulaeBand`/`NebulaePatches`.
  `NebulaeBand`/`NebulaePatches` keep their arithmetic in Pascal's own 1-based coordinate space
  (matching `Rnd` call values exactly) and shift to this port's 0-based `Coordinate` only at the point
  of painting a cell. Relocated `CreatePlanet`/`RandomTrillumReserves`/`RndShips`/`RndCargo`/
  `RndDefns`/`SetUpWorld` verbatim into the patched `UPDATE.PAS` (deferred from 2c), landing 2c's own
  golden-file coverage alongside 2d's three new domains (`trillumreserves.golden`,
  `randomplanet.golden`, `nebula.golden`) — see `reference/verify/README.md`'s "Tenth domain" note for
  why `GetRandomXY`/`CreateRandomWorlds` themselves stay hardcoded-only (the `ForcedRandomValue`
  convention can't express "blocked, then a later retry succeeds"). That investigation surfaced a real
  bug in the golden-file harness's own `ForcedRandomValue` test convention (wrong check order in the
  `INT.PAS` patch, letting a forced value override `Rnd`'s real degenerate-range clamp) — fixed in the
  Pascal patch, not `PascalMath.Rnd` (whose existing behavior was already correct), confirmed via
  `git status` on `reference/verify/golden/` that no other domain's committed values moved.
- ✅ **2e, `.SCN` scenario file loading** — the capstone integration test: `LoadScenario`'s
  tokenizer/dispatch loop minus its DOS UI (`Core/NewGame/ScenarioLoader.cs`), golden-file-verified
  against 11 real `dos_131/*.SCN` files run through the real, patched `LoadScenario`
  (`PRINCES.SCN` excluded — real 1.31 itself can't load it either, see `ScenarioCases.cs`).
  `CREATERANDOMWORLDS`'s collision-retry loop degenerates under the `ForcedRandomValue` convention (a
  fixed offset always re-rolls the same blocked coordinate), so this needed a real, non-degenerate RNG
  sequence: `PascalRandom` (`src/ThreeLn.Reconstruction4021.Tests/PascalRandom.cs`), a from-scratch,
  empirically verified port of this project's actual fpc runtime's `Random`/`RandSeed` algorithm (a
  Mersenne Twister variant, not the classic Turbo Pascal LCG a DOS-era codebase might suggest), backed
  by a new `rng.golden` standing regression fixture — see `reference/verify/README.md`'s own section
  on it.

  Running real scenario files end to end surfaced a structural limit, not a bug: any field touched by
  a random draw anywhere in the file is fragile to RNG-stream-position drift between two
  independently-written implementations (confirmed concretely for a starbase population computed from
  only its file's *explicit* `CreateWorld` commands, nowhere near any randomized generation) — a
  floating-point boundary flip anywhere upstream changes how many draws a call consumes, desyncing
  every later draw in the same file. Not fixable by matching floating-point precision (verified:
  forcing fpc to `-CfSSE2`/strict double just trades one set of boundary flips for another). Resolved
  by narrowing `ScenarioLoaderGoldenTests` to exact-match only fields with no random draw anywhere in
  their computation, plus a domain-invariant smoke test (bounds from type/domain constraints, not
  game-balance assumptions) for everything else — full writeup in the root `README.md`'s "Known
  limitation" section, since this is exactly the kind of investigation a future reader shouldn't have
  to redo.

## 3. Probe movement and visibility — ✅ done

Corrected from the original one-line description ("fleet-movement-style advance toward a
destination"), which doesn't match source: `ProbeRecord` (`DATASTRC.PAS:170-175`) has no position
field, only a destination and status — a probe doesn't move incrementally at all. `UpdateProbes`
(`INTRFACE.PAS:1346-1359`) runs once per empire at the start of that empire's own turn, resolved as
the last step of `VisibilityHandler.RefreshVisibility` (`TurnEngine` needed no changes — matches
Pascal's `SetUpTurn`, which calls `ClearScoutSet`/`ScoutFleets`/`ScoutObjects`/`UpdateProbes` as one
sequence). `ProbeScout` resolves an in-transit probe immediately against its destination, then marks
it ready again — one "trip" is exactly one full turn-cycle, not a distance-based journey.

`Empire.Probes`/`Probe`/`ProbeStatus` (a pre-existing, unused stub literally mirroring Pascal's
4-state `ProbeRecord`) was simplified to `Empire.ProbesInTransit: List<Coordinate>` plus
`TryLaunchProbe`/`MaxProbesInTransit` — `ProbeStatus.AtDestination`/`.Lost` were confirmed dead code
(never assigned anywhere in either the 1.31 or 2.0 source trees), so `Status` was fully derivable from
`Destination`'s nullability and slot identity had no observable meaning (`GetProbe` just grabs
whichever slot is `Ready`). See the root `README.md`'s "Ideas noticed but not chased down" section for
what that dead code might hint at.

Golden-file-backed (`probescout.golden`, `ProbeScoutCases`/`VisibilityHandlerProbeTests.MatchesGoldenFile`)
via a new `probescout` domain in `runworld.pas`. `ProbeScout`/`UpdateProbes` had to be relocated
(verbatim, not rewritten) from `INTRFACE.PAS` into the patched `UPDATE.PAS`, same "relocate the small
dependency, not the whole unit" precedent as `GetOptimumIndus` — confirmed directly (not assumed) that
importing `Intrface` itself pulls in `Fleet`/`Orders`/`NPE` via its own `IMPLEMENTATION USES`, exactly
the combat/NPE-AI blast radius every earlier phase has deliberately stayed out of. That investigation
also produced a validated, ready-to-use `SYSTEM2.PAS.patch` (fpc can't compile its `$V+`-mode string
mismatch or its x86 `INLINE` assembly — replaced with a plain Pascal loop) for whenever a future phase
(Combat/NPE AI) actually needs that `Intrface`→`EIO`/`Fleet`/`Orders`/`NPE` chain to compile.

## 4. News — ✅ done

Ported `NEWS.PAS`'s per-empire event log. Not a UI nicety: confirmed from source that `ReviewNews`
(present in every NPE personality — `NPE00.PAS`/`NPE01.PAS`/`NPE04.PAS`) calls `GetNewsList` and walks
it as the AI's primary "what happened to me this turn" signal — Phase 6 (NPE AI) has a real data
dependency on this, not just a display one.

`GetNewsList`/`GetNewsItem`/`EraseNews` collapse to a plain `Empire.News: List<NewsItem>` — Pascal's
hand-rolled linked list plus its `MaxAvail>20` heap guard exist only because of DOS's 640KB heap, not a
game rule. `EraseNews`'s per-turn reset (Pascal clears it right after whichever consumer reads it) is
deliberately *not* wired into `TurnEngine` yet: neither consumer (NPE AI, human UI) exists yet to
validate that timing against — `empire.News.Clear()` is sitting there ready for whichever phase adds
the first real reader.

`NewsItem`'s `Loc: Location` union (an entity reference almost always, but a bare `Coordinate` for
`ConstructionCompleted` specifically — `UPDATE.PAS:215` discards the newly-built object's own ID and
uses the raw XY instead) became two real nullable fields (`Subject: ISectorObject?`/
`Position: Coordinate?`), typed via a new `ISectorObject` interface (`Coordinate Location`/
`Empire Owner`) rather than `object` — earned by a genuine third/fourth occurrence, not invented for
this: `VisibilityHandler.FindProbeTarget` (Phase 3) already dispatched across the same four concrete
types by hand. The recurring Pascal `(Loc,Emp)` shape (most combat/interaction headlines carry a second
empire via `Ord(Emp)`, since `Parm1..3` were its only generic slots) became a real `Empire? OtherEmpire`
field for the same reason. `NCapTech`'s `Ord(NewTech)` needed the same treatment: this port has no flat
`TechnologyTypes` ordinal to port (already deliberately split into four typed enums), so
`TechCatalog.MissingTechAt` now pairs each grant with a `TechGrantIdentity` (category + that category's
own ordinal) alongside its unlock delegate.

Investigating every real `AddNews`/`AddGlobalNews` call site in code this port had already written
turned up more than the six `AnnualTickHandler.*` sites plus Probes' own two that earlier phases had
flagged as dropped — `UpdateRevolution`'s rebellion-warning tier cascade (`RevIndex>75/>70/>66/>43/>30`,
`UPDATE.PAS:737-753`) had collapsed into a single boolean condition with no representation at all for
the tiers below 75 or for the `RevIndex>75`-but-the-roll-failed sub-case, and `Rebellion`'s "world goes
independent" branch needed a from-scratch `Game.AddGlobalNews` (broadcast to every empire that's
scouted the source, filtered by an `Exclude` set) since nothing had needed it before. Restructuring the
cascade into an explicit if/else-if chain (see `AnnualTickHandler.Revolution.cs`) was verified with
boundary tests at each tier transition, including one proving `RevIndex>75` genuinely has two outcomes
(`Rebellion` vs. a lone `RebellionWarning4`), not one.

## 5. Combat — in progress, 5a-5e landed

Attack resolution, fleet/starbase destruction, capital loss and empire elimination. The original
one-paragraph version of this section undersold the real scope, the same way "8 known News sites"
undersold Phase 4 — a research pass reading `ATTACK.PAS`/`ATTCOMM.PAS`/`ATTNPE.PAS`/`BATTLE.PAS`/
`BOMBER.PAS`/`FLEET.PAS`/`FLTCOMM.PAS`/`SBASE.PAS`/`ORDERS.PAS` plus the relevant `UPDATE.PAS`/
`DATACNST.PAS`/`DATASTRC.PAS`/`ANACREON.PAS`/`INTRFACE.PAS` sections found:

- **`ATTCOMM.PAS`/`FLTCOMM.PAS`/`ORDERS.PAS` are ~entirely DOS UI or a human order-compiler**, not
  simulation logic — Phase 8's job. The one thing worth keeping from them: `ATTCOMM.PAS`'s
  `AutoAttackCommand` confirms the real non-interactive entry point every non-human attack goes
  through is `ATTNPE.PAS`'s `NPEAttack(fleetId, targetId, intent, retreatIndex)` — the one
  primitive this phase needs to expose for Phase 6 (NPE AI) and Phase 8 (human auto-resolve) to
  call later, with nothing wired up to call it yet (same "build the primitive first" precedent as
  Phases 3/4).
- **`ATTNPE.PAS`'s "NPE" means "no player experience" (auto-resolved), not "AI decision logic."**
  It's the round-by-round resolution driver (`FleetEngage`/`WorldEngage`/`GroupEngage`) both a
  human's auto-resolve and a computer empire's attack funnel through — no strategic targeting logic
  lives here for Phase 6 to reimplement.
- **`BATTLE.PAS`/`BOMBER.PAS` are confirmed dead code** — no callers anywhere in the source tree.
  Not ported; recorded as a design-history lead in the root `README.md`'s "Ideas noticed but not
  chased down" section (an earlier, simpler non-group combat formula, superseded by `ATTACK.PAS`'s
  group/shell system).
- **Empire elimination is plain `Game.Empires.Remove(eliminated)`, not a ported `InUse` flag.**
  Real Pascal marks a fixed array slot `InUse:=False` (`INTRFACE.PAS:1657`) because it has no real
  list to remove from. This port does have one, and `Game.NextEmpire`/`Game.IsFirstEmpire` already
  re-derive everything live (`Empires.IndexOf(current)`, `Empires[0]`) rather than caching a
  position — hand-traced (see the Phase 5 plan, retained in conversation history) that plain
  removal self-heals the "one annual tick per lap" invariant regardless of when in a lap the
  removal happens, with no new `IsEliminated` field and no guards needed anywhere `Game.Empires`
  is already iterated (`AddGlobalNews`, `RunAnnualTick`'s per-empire loop). Only humans are exempt
  from removal, matching Pascal's own `EmpirePlayer` branch in `ConquerEmpire`.
- **Human-empire defeat: `Empire.DefeatedBy: Empire?`, not Pascal's capital-sentinel trick.**
  `ConquerEmpire` (`ATTACK.PAS:1120-1131`) never calls `DestroyEmpire` on a human — instead it
  overloads `Capital: IDNumber` with a sentinel (`ObjTyp:=Void`, empire ordinal in `Index`) to
  record who won. Same shape as `NewsItem.Loc`'s pre-Phase-4 union — split into two real fields
  instead: `Capital = null` (already means "no capital" unambiguously) plus a new `DefeatedBy`
  field for the other fact. The write is real Pascal behavior this phase's `ConquerEmpire` port
  needs; nothing reads `DefeatedBy` yet (no human `ITurnHandler` exists — Phase 8's job).

Sub-commits:

- ✅ **5a, this section.** Recorded the corrected scope and design calls above before writing any
  code, matching the "verify don't recall" discipline from every prior phase's own planning.
- ✅ **5b, `UpdateDefenses` + `Defns` state** (`UPDATE.PAS:1278-1351`) — `DefenseType`-indexed growth
  on `IEconomicWorld` (`AnnualTickHandler.Defenses.cs`), wired into `AnnualTickHandler.UpdateWorld`/
  `UpdateStarbase` the way `UpdateMilitary` was in Commit 2c — unconditionally for a starbase, after
  `UpdateMilitary` for a planet. `DefenseCounts` (already existed, never written to) becomes real
  state. Surfaced a real gate this port hadn't modeled anywhere for defenses yet: Pascal's
  `Technology:=Technology*TechDev[Tech]` (UPDATE.PAS:1367-1369) intersects the empire's *researched*
  defenses with what the *world's own* `TechLevel` makes available at all — caught by the new
  patch-based `defenses` golden domain (`reference/verify/runworld.pas`), which disagreed with a
  first pass that gated purely on `Empire.Technology.Defenses.Contains`. Fixed via
  `DefenseTechAvailable`, reusing `Production.cs`'s existing `EffectiveTechnologyLevel`/
  `TechCatalog.MinTechForDefense` exactly the way `ShipTechAvailable` already does for ships — an
  independent world needs only the tech-level gate (at one level below its own), no empire research
  at all. `_rawMaterialForDefenses` is a new table (`RawM`'s `LAM..ion` rows) rather than an
  extension of an existing shared one — confirmed no C# mirror of Pascal's `RawM` spans
  ships+defenses today; each consumer (ships, cargo products, construction, now defenses) keeps its
  own dictionary, matching this port's existing pattern rather than one array indexed by Pascal's
  single flat `ResourceTypes`. Golden-file-backed (`defenses.golden`, `DefensesCases`/
  `AnnualTickHandlerDefensesTests`) for the planet-only formula/tech-gate/raw-material-shortfall
  behavior; the three starbase-specific multipliers (Outpost quarters `Optimum` and zeroes
  `DefenseSatellite`; CommandBase/Fortress quadruple both `Optimum` and `BuildRate`) and the
  independent-world tech gate are hardcoded instead — `UpdateMilitary` doesn't even run for a
  non-complex starbase, so `TroopStrength` has no RNG-dependent growth to account for, and neither
  branch involves a sqrt/pow cascade.
- ✅ **5c, combat constants + `AttackType`** (`Types/AttackType.cs`, `Combat/CombatConstants.cs`) —
  new unified enum mirroring Pascal's `AttackTypes = NoRes..nnj` (spans
  `DefenseType ∪ ShipType ∪ {Legion, NinjaLegion}` — a genuine single cross-product axis for
  `CombatTable[Attacker,Defender]`, unlike `TechGrantIdentity`'s four independent identity spaces
  from Phase 4), declared in Pascal's own LAM..nnj order so every table below reads off source
  without reordering; the leading `NoRes` sentinel is dropped rather than ported — its row/column in
  `CombatTable` is all zero and never legitimately read, so "no target" is represented as
  `AttackType?` at whichever call site needs it (5d/5e), not as an enum member. `AttackTypeExtensions`
  maps to/from `DefenseType`/`ShipType` (both stay real per-type stored state on
  `DefenseCounts`/`ShipCounts`; `AttackType` is purely a combat-engine indexing concern). Combat
  constant tables as pure data, transcription-lane (isolated, parameter-only, no real-state
  dependency): `CombatTable`, `WeapEff`, `ShipValue`, `CombatPower` (all `AttackType`-keyed);
  `ProtecOffered`/`ProtecNeeded`/`TrnAdj`/`GdmKill` (`ShipType`-keyed); `CargoSpace`
  (`CargoType`-keyed); `CombatTechAdj` (`(AttackerTech,DefenderTech)`-keyed — confirmed the index
  order from ATTACK.PAS's two real call sites, not just the table's own comment); `CombatClassAdj`
  (`WorldClass`-keyed); `CombatBaseAdj` (`StarbaseKind`-keyed); `GdmLaunch` (`TechLevel`-keyed). Every
  key enum's declaration order was verified against TYPES.PAS directly to match Pascal's ordinal
  order before trusting positional array transcription. Tests: enum order, full key-space coverage
  per table, a handful of known values, and the `AttackType`↔`DefenseType`/`ShipType` mapping
  round-trips — no combat logic exists yet to exercise these tables against.
- ✅ **5d, group/shell combat engine core** (`Combat/CombatState.cs`, `Combat/CombatEngine.cs`) — the
  actual per-round damage math: `CalculateCombatData`, `GetEnemy`, `DefaultDistribution`/
  `DefaultGroup`, `ForcesUnknown`, `Battle` (`GetConflict`/`TotalProtection`/`GetTargetArray`+
  `BuildPriority1`/`BuildPriority2`/`BuildTargetArray`/`GroupAttack`/`EnemyAttack`/
  `UpdateGroupsDestroyed`/`UpdateEnemyDestroyed`), `EnemySurrenders`. Deliberately stops short of
  the multi-round/multi-shell driver (`AdvanceGroups` and the rest of `ATTNPE.PAS`) and outcome
  application (`ResolveAttack`/`ConquerWorld`/`ConquerEmpire`) — both later commits. A group's own
  `Trg` staying unset (`AttackType?` null) is real, expected Pascal behavior here, not a degenerate
  case: nothing in this commit's scope ever assigns it (that's 5e/8's targeting logic), so
  `GroupAttack`'s own output is 0 for every group by default and only the defender's counterattack
  does real damage — exactly what the golden cases below exercise, plus one case that manually
  forces a group's `Trg` to exercise `GroupAttack`'s real-damage path too.

  Needed a real `ATTACK.PAS.patch` (mirroring `UPDATE.PAS.patch`'s own precedent) to link at all:
  `Attack`'s `INTERFACE` pulls in `Intrface`, whose own `IMPLEMENTATION` pulls in `Fleet`/`Orders`/
  `NPE`, which pull in `Crt`/`EIO`/`WND` — a real GUI blast radius, confirmed directly by a throwaway
  link attempt before writing any C#. Every procedure this commit ports only ever needs `PrimIntr`'s
  getters, already in scope; the patch drops `Intrface`/`Fleet` from `Attack`'s `USES` and comments
  out the procedures that genuinely need them (`ConquerWorld`/`ConquerEmpire`/`RestoreCombatant`/
  `ResolveAttack`/`HolocaustWorld`/`HolocaustEffectiveness`/`LAMAttack`/`DestroyConstructionOrGate`),
  each with a `PATCH` comment pointing at the future commit that restores it. The new `combat` golden
  domain (`reference/verify/runworld.pas`) exercises `CalculateCombatData`/`GetEnemy`/
  `DefaultDistribution`/`Battle`/`EnemySurrenders` together for one round at the DpSpc shell (ground-
  troop groups only exist after `AdvanceGroups`, 5e, so this domain never reaches `Grnd`);
  `ShipsDestroyed`/`ForcesUnknown`/the advancing-group damage bonus/hunter-killer cloak/
  in-vs-out-of-range defenses are hardcoded C# tests instead, isolating small hand-derivable branches
  the golden domain's own fixed fleet/target shape doesn't reach.

  **Real bug found and fixed along the way, with wide blast radius**: `PascalMath.PascalRound` was
  half-away-from-zero; FreePascal's actual `Round` is half-to-even (banker's rounding) at exact `.5`
  boundaries — confirmed directly against the ground-truth compiler (`Round(2.5)=2`, `Round(3.5)=4`,
  `Round(-2.5)=-2`), not assumed. `GetEnemy`'s clean-percentage shell split (5%/10%/15% of a round
  ship count) was the first formula in the whole port to land exactly on a `.5` boundary, so this went
  undetected through every earlier phase's own golden-file coverage until now. Fixed to
  `Math.Round(x, MidpointRounding.ToEven)` (C#'s own default rounding, not an approximation) and
  re-ran the *entire* golden-file suite across every phase to check for fallout — none: no earlier
  phase's formula ever depended on the old, wrong tie-breaking direction, confirmed empirically, not
  assumed.
- ✅ **5e, resolution loop + `NPEAttack` entry point** (`Combat/CombatResolution.cs`) —
  `ATTNPE.PAS`'s round driver: `AdvanceGroups`, `AllAdvance`/`TrnAdvance`, `FleetRetreats` (a fixed
  30-round timeout, not actually parametrized by its own `RetrIndex` argument — confirmed by reading;
  dropped that unused parameter entirely, same "confirmed by reading" precedent as 5d's
  `CalculateCombatData`), `TransportsLeft`, `GroupEngage` (one round: `Battle` at every shell, then
  `AdvanceGroups`, then the termination checks), `FleetEngage`/`WorldEngage`'s own `Targetting`
  (`GetTargetArray`/`PrioritizeTargetArray`/`GetBestTarget`, a different, per-round-driver-only
  `TargetArray` shape from `ATTACK.PAS`'s own), culminating in `NPEAttack` — trimmed to its
  resolution half (`Result`/`Casualties`/`Killed`), the same scope boundary the golden harness itself
  needed (see below). `Consolidate` (ATTNPE.PAS) is confirmed dead code (declared, empty body, never
  called anywhere including from within its own unit) — not ported, same treatment as
  `BATTLE.PAS`/`BOMBER.PAS`.

  Needed a second patch, `ATTNPE.PAS.patch`: `AttNPE`'s own `INTERFACE` unconditionally lists `CRT` in
  its `USES` (needed only by a debug window behind `{$IFDEF Debug}` — confirmed inert: the enabling
  `{DEFINE Debug}` line above it is missing its leading `$`, so it's a plain comment, not a real
  directive), and Pascal doesn't resolve `USES` lazily, so it still had to be dropped to link. Also
  trims `NPEAttack`'s own body to what this commit covers — its full real body calls
  `RestoreCombatant`/`ResolveAttack` (5f) and, for a construction-site/stargate target,
  `DestroyConstructionOrGate` (5g), none of which exist yet; each elided piece is a `PATCH` comment
  pointing at 5f/5g. New `npeattack` golden domain runs real `NPEAttack` end to end (not one round in
  isolation, already covered by 5d's `combat` domain) across 5 cases spanning both termination paths
  (30-round retreat timeout vs. real surrender/destruction), both dispatch targets
  (`WorldEngage`/`FleetEngage`), and the intent-driven targeting bias; a 6th, hardcoded C# case covers
  `AttackerDestroyed`, the one termination path no golden case's parameter space happened to reach.
- **5f, outcome application + empire elimination** — `ResolveAttack`/`ConquerWorld`/
  `ConquerEmpire`/`DestroyEmpire` using the plain-`List.Remove`/`DefeatedBy` model above, plus
  `DestroyFleet`/`AbortFleet`.
- **5g, standalone mechanics** — `HolocaustWorld`, `LAMAttack`, `DestroyConstructionOrGate`,
  `SelfDestructObject` (ported with no caller wired up yet, same precedent as `Empire.News.Clear()`).
- **5h, minefield damage + disrupter blocking + minefield visibility** — folded additively into
  `FleetMovementHandler`'s existing step loop; new per-cell `Galaxy` mine-visibility structure
  (can't reuse `EntityVisibility<T>`, which is keyed by entity reference, not coordinate).
- **5i, `HostileLife` news fix + roadmap wrap-up** — a small Phase 1/4 gap this investigation
  caught (`HostileLife`, shipped Phase 1, calls no `AddNews` despite the relevant `NewsType`
  members already existing), bundled here since it's combat-adjacent and cheap.

### Known gaps in already-shipped movement (surfaced by Phase 5, not fixed by it)

Two places where the shipped `FleetMovementHandler` (Phase 2/3) is a deliberately simplified
straight-line stepper next to what real Pascal does — found while scoping Combat, but movement
fidelity, not a combat mechanic, so explicitly not fixed in Phase 5 (would have roughly doubled
its scope):

- **Stargate teleportation / fortress pass-through jumps.** Real `UpdateFleet`
  (`FLEET.PAS:646-860`) handles a fleet passing through a friendly stargate or "jumping" through a
  fortress (up to 5 steps at once) mid-move; `FleetMovementHandler.AdvanceFleet` only steps
  straight toward the destination.
- **Starbase obstacle-avoidance and fuel cost.** `SBASE.PAS:88-263`'s `MovePlayerStarbases`/
  `GetNewBasePos` route a mobile starbase around obstacles and charge fuel;
  `FleetMovementHandler.AdvanceStarbases` steps straight toward the destination with no fuel cost
  and no obstacle check.

## 6. NPE AI

Implement an `ITurnHandler` for computer empires. Start with one "classic" implementation — the
handler-per-empire design (`Game.TurnHandlers`) already supports adding an "advanced" variant later
without any changes to `TurnEngine`.

Moved after Probes/News/Combat (was Phase 3 originally): confirmed directly from source that NPE
decision-making is written against combat primitives from the start, not layered on after —
`NPE01.PAS`'s `USES` clause pulls in `Attack`/`AttNPE`, and its own `FindTarget` procedure is
combat-targeting logic. It also reads the News feed as a real sensory input, not just a display
concern — see Phase 4. Building the NPE turn handler before Combat or News exists would mean an AI
that can't act on its own core decisions or perceive what happened to it.

## 7. Save/load

Translation layer to read/write original `.SAV` files. Explicitly does not shape the in-memory
model — the on-disk format is a serialization concern, not a design input.

## 8. Human interactive turn handler + Terminal.Gui UI

The last `ITurnHandler` implementation, plus the actual windowed interface (map view, fleet orders,
construction, etc.) per `TUI_LIBRARY_RECOMMENDATION.md`.

## 9. Async/hotseat turn mode

Deferred multiplayer option — sequential mode (already built) is the only mode a solo player sees.

---

Not scheduled, pull in only if/when needed: v2 gameplay changes and new features from
`PASCAL_V1_VS_V2_DIFF.md` (all opt-in, none are baseline).

## Ground-truth harness generation

Two ways to get real-Pascal ground truth for a C# behavior, so a check can't silently agree with the
same hand-derivation mistake on both sides.

**Patch-based (`reference/verify/`) — the default**, used by all nine domains that exist today
(`techlevel`, `military`, `starbase`, `ambrosia`, `revolution`, `production`, `empire`,
`construction`, `empirecreate`). Small maintained patches (`reference/verify/patches/*.PAS.patch`)
apply to a disposable copy of the real `reference/DOSAnacreonSource131/*.PAS` source
(`reference/verify/patched/`, gitignored, rebuilt every run); `runworld.pas` then calls the real,
only-minimally-touched Pascal procedure(s) directly against a hand-assembled `Universe^`. Full
mechanics, layout, and the domain-by-domain investigation history live in `reference/verify/README.md`
— read that (not this section) before touching the harness itself.

**Transcription (`reference/verify/*.pas`)** — the older pattern: hand-copy a procedure into an
isolated file, called with plain parameters instead of a real `Universe^`. Unused today (the last
domain on it, `production`, migrated 2026-08-22) but not retired — still the right call for a
genuinely isolated, parameter-only procedure with no real-state dependency. Re-read the Pascal source
fresh every time; never transcribe from the existing C# port
(`[[feedback_transcribe_pascal_harness_from_source]]`).

**Which one to reach for.** Patch-based whenever a procedure touches real state transcription would
otherwise have to fake (`GetCapital`/`GetTech`-style lookups, other empires/planets/worlds) or where
call *ordering* across a real pipeline is what's being checked — transcription missed a real bug this
way once (`UpdateMilitary` mutating `Cargo.Legions` before `UpdateRevolution` reads it). Transcription
only for a procedure with no such dependency. Don't gate the choice on authoring cost — lean into
patch-based broadly (`[[feedback_prefer_patch_based_ground_truth]]`). Dependency-blast-radius judgment
still applies: don't drag combat/fleet movement/NPE AI (`Intrface`'s `Fleet`/`Orders`/`NPE`) into scope
prematurely — each new subsystem's dependency web is its own investigation, not something the domains
above generalize to.

**Adding a new patch-based domain:**
1. If the target procedure isn't already exported from its unit's `INTERFACE`, add/extend a patch —
   see `reference/verify/README.md`'s "Layout" for the patch-editing workflow (never hand-edit a
   `.patch` file directly; edit the applied copy under `patched/`, verify it compiles/runs, then
   regenerate the diff).
2. Give `runworld.pas` a new `case <domain>` branch (see its own header comment for the CLI
   convention: comma-separated fields in, one `key=value;...` line out per case).
3. Add a `<Domain>Cases.cs` — a case record plus `All`/`AsDataSource`, with a doc comment explaining
   that domain's own rationale (why patch-based, what the case shape encodes). Domain-specific detail
   belongs here, not in `GoldenFileTests.cs`.
4. One `GoldenFile.Regenerate(...)` call in `GoldenFileTests.RegenerateAllGoldenFiles` — the
   format-string lambda's field order must match `runworld.pas`'s parser field-for-field.
5. `dotnet test`, then review the new/changed `.golden` file's diff field-by-field before committing.

**Landmines worth checking for on every new domain** (each has bitten more than once already):
- **Sector-grid access** — anything touching `Sector[x]^[y]` (`PutMine`/`CreateStarbase`/
  `CreateStargate`, ...) needs `Galaxy.InitializeSector` called first, or it's a runtime error 216
  (access violation). Ask whether the domain's call graph touches the sector grid before wiring it up.
- **A hand-assembled `Universe^` is only as faithful as the fields it remembers to set.** "Defaults to
  zero" is not "reachable in real gameplay" — check the real initialization/seeding code
  (`CreateEmpire`, `PRIMINTR.PAS`'s `Set*` procedures, `DefaultISSP`) for what a genuinely new object
  actually gets set to.
- **`ABSOLUTE` overlays compile but lie.** `DATASTRC.PAS:235`'s `GlobalSets ABSOLUTE
  SetOfActiveFleets` relies on Turbo Pascal's declaration-order memory layout, which fpc doesn't
  guarantee — writing through it silently corrupts unrelated memory. Never write through a
  `GlobalSets`-style overlay; use the real standalone variable.
- **A `git diff --no-index`-generated patch needs its extended-format header stripped** (`diff --git`
  / `index` lines, and CRLF fixed) before `git apply` will actually apply it inside a real repo —
  otherwise it silently no-ops (0 files changed, no error).
- **Relocate the small dependency, not the whole unit**, when a pure procedure/formula lives in a unit
  with a much larger `USES` clause than the domain needs (`GetIndustrialDistribution` out of
  `INTRFACE.PAS`; `empirecreate`'s inline copy of `NEWGAME.PAS`'s 3-line tech-set formula, avoiding
  that unit's `Crt`/`Dos`/`NPE`/... chain entirely).
- **A discrepancy that looks architectural is usually the harness, not the port.** Verify against the
  real initialization/seeding code before concluding the C# port (or a doc comment) is wrong — the
  harness's own hand-assembled `Universe^` is the newest, least-trusted part of the whole chain.

---

# Long-term future possibilities

- options-based enable/disable v2 features, as well as other future enhancements
- fleet orders to build a minefield across an entire area, either a list of coordinates, or a bounded area
- improved scenario format without so many "magic" numbers
