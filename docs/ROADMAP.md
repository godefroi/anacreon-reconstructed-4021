# Port roadmap

Bottom-up order: simulation core first, UI last. Completed phases aren't listed here — the git log
is the record of what's done. Each phase gets its own plan/design pass when it's picked up; this
file only tracks the sequence and the design decisions/scope calls that need to be made up front.

## 1. Economy / annual tick

Implement `IAnnualTickHandler`. `UpdateUniverse` (UPDATE.PAS:1440-1488) runs, in order: `Year++`,
then `UpdateWorld` over every planet and starbase, then `UpdateConstruction`, then `UpdateEmpire`.
This phase lands `UpdateWorld` for planets first; starbases, construction, and empire-level updates
follow once that's working.

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
   UPDATE.PAS:715-735).
2. ✅ **Industry and production** (planets only) — `ProduceRawMaterial`, `GetIndustrialDistribution`,
   `UpdateIndustry`, `Production` (UPDATE.PAS:1375-1379; production formula at 844-925), inserted
   before `UpdateEfficiency` in `UpdateWorld` (Pascal runs the whole production pipeline first).
   Ship/cargo tech-gating is TechLevel-derived (via a per-item "minimum tech level" table built from
   `TechDev`'s monotonic structure) rather than modeling per-empire incremental research — nothing
   seeds or grows `Empire.Technology.Ships` yet (that needs both new-game setup, Phase 2, and
   `NewTechLevel`'s per-tick research rolls, Commit 5 below), so ship production is correct but inert
   until those land. `AnnualTickHandlerProductionTests` — Pop=1000/Class=EthCls/Tech=Gate cases
   deliberately exercise the sqrt/pow cascade in `GetIndustrialDistribution`, infeasible to hand-trace
   reliably — is golden-file-backed via `reference/verify/production.pas`'s `FullPipeline` (see
   below); this originally caught two real bugs: `SelfSufficiencySettings` defaulted to 0 instead of
   Pascal's `InitializeISSP` default of 5 (badly distorting `GetIndustrialDistribution`'s sqrt terms),
   and FreePascal's built-in `Round()` is banker's rounding, not Turbo Pascal's round-half-away-from-
   zero (fixed with a `PascalRound` helper in `common.pas`) — both missed by hand-tracing.
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
   - ✅ **Golden-file ground truth.** `reference/verify/{ambrosia,revolution,production}.pas` are
     from-source transcriptions (not copied from the C# port — an early `ambrosia.pas` draft slipped
     into doing that, caught before landing, see its header comment) of `UseUpAmbrosia`,
     `UpdateRevolution`/`Rebellion`, and the production pipeline (`FullPipeline`), sharing tables/
     helpers in `common.pas`. `GoldenFileTests` ([Explicit] + `Category("PascalGroundTruth")`,
     requires fpc — excluded from the default `dotnet test` run) runs each harness via a shared
     `GoldenFile.Regenerate` helper and writes a committed `reference/verify/golden/*.golden` file
     (`case=Name;key=value;...` lines). The always-on `AnnualTickHandler{Ambrosia,Revolution,
     Production}Tests.MatchesGoldenFile` tests (data-driven via each domain's `*Cases.AsDataSource`
     and TUnit's `[MethodDataSource]`) read that file and assert the C# port against it — so no
     hand-typed expected value can silently agree with the same mistake on both sides of a check.
     Regenerate a golden file (review the diff, then commit) whenever its `*Cases.All` or its
     harness's transcription changes. `production.golden` excludes `Cargo.Supplies`/`Cargo.Ambrosia`/
     `Cargo.Legions` (all mutated later in the same tick by steps `FullPipeline` doesn't model) — see
     each domain's test-class doc comment for the exact scope.
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
3. **Tech advancement** (planets only) — `UpdateTechLevel` (UPDATE.PAS:1032-1072), random
   advancement/regression toward the empire's capital tech level.
4. **Starbase economy** — same `UpdateWorld` sequence, but with `SupplyLink`/`SurplusLink`
   (UPDATE.PAS:1408,1413 — raw-material redistribution across the empire) and industrial-complex-only
   production and economy (`STyp=cmp` branches, lines 1403-1415 for production and 1420-1427 for
   population/food/ambrosia/military/revolution — starbases gate the *entire* economy pipeline,
   including Commit 2c's `UpdateMilitary`, on being an industrial complex, not just production).
5. **Construction and empire-level updates** — `UpdateConstruction` (UPDATE.PAS:103-220, countdown
   and completion) and `UpdateEmpire` (UPDATE.PAS:222+, applies accumulated revolution index). This
   is also the earliest point `NewTechLevel`/`GetChanceForNewTech` (empire-level research: rolls a
   chance each tick to unlock one more `TechnologyTypes` item into `Empire.Technology`, then a
   separate roll to advance `Empire.TechnologyLevel` once the current level's full `TechDev` set is
   unlocked — UPDATE.PAS:~320-420) can land; nothing populates `Empire.Technology.Ships` before this,
   so Commit 2's ship production stays correct-but-inert until it's implemented.

**Constants to extract:** `MaxPop` array (UPDATE.PAS:1077-1098), `BasePop` lookup, `TechAdj[]`,
`TechAdj2[]`, `K6`, `SuppliesPerBillion`, `DrugsPerBillion`, `ThgLmt()` clamp function.

## 2. Galaxy / new-game setup

Galaxy generation or scenario loading, empire creation, initial fleet/planet placement. Needed
before the economy and visibility handlers can run against a real game instead of hand-built test
fixtures.

## 3. NPE AI

Implement an `ITurnHandler` for computer empires. Start with one "classic" implementation — the
handler-per-empire design (`Game.TurnHandlers`) already supports adding an "advanced" variant later
without any changes to `TurnEngine`.

## 4. Probe movement and visibility

Implement `UpdateProbes` (fleet-movement-style advance toward a destination). Once probes move,
wire their scouting radius into `VisibilityHandler.RefreshVisibility` — probes grant scouting around
their current location the same way owned planets/fleets do.

## 5. Combat

Attack resolution, fleet/starbase destruction, capital loss and empire elimination. The turn loop
currently assumes empires never leave `Game.Empires` mid-game (`TurnEngine.AdvanceOneTurn`,
`Game.NextEmpire`) — this phase removes that assumption. Also lands minefield mechanics:
`SectorRecord.MineScout` is per-cell (not per-entity, no `EntityVisibility<Minefield>` exists),
so minefield visibility needs its own tracking structure, not a sixth `EntityVisibility<T>` on `Empire`.

Needs `UpdateDefenses` (UPDATE.PAS:1278-1351) landed first as baseline defensive state for attack
resolution to read: builds up a `Defns` array (new `DefnsTypes` enum — LAM/def/ion-style planetary
defenses) from raw materials, gated by researched `Technology`. Bigger than Commit 2c's
`UpdateMilitary` — needs three new tables (`DefAdj`, `DefBuildRate`, `RawM` per defense type) not yet
extracted, and it branches on `WorldID.ObjTyp=Pln` vs `=Base` (runs unconditionally for both, per
UPDATE.PAS:1387,1429) — exactly the planet/starbase overlap question deferred to economy phase
Commit 4, so land the planet-only version no earlier than that.

## 6. Save/load

Translation layer to read/write original `.SAV` files. Explicitly does not shape the in-memory
model — the on-disk format is a serialization concern, not a design input.

## 7. Human interactive turn handler + Terminal.Gui UI

The last `ITurnHandler` implementation, plus the actual windowed interface (map view, fleet orders,
construction, etc.) per `TUI_LIBRARY_RECOMMENDATION.md`.

## 8. Async/hotseat turn mode

Deferred multiplayer option — sequential mode (already built) is the only mode a solo player sees.

---

Not scheduled, pull in only if/when needed: v2 gameplay changes and new features from
`PASCAL_V1_VS_V2_DIFF.md` (all opt-in, none are baseline).

## Ground-truth harness generation: patch-vs-transcribe (second lane added)

Current practice (`reference/verify/*.pas`) transcribes each procedure into a fresh file, hand-read
from source every time (`[[feedback_transcribe_pascal_harness_from_source]]`). Added an alternative
in `reference/verify/patch-based/`: maintain small patches against the real
`reference/DOSAnacreonSource131/*.PAS` files, apply them to a disposable copy at build time
(`build.ps1`), and call the real, only-minimally-touched `UpdateWorld` directly against a
hand-assembled `Universe^` instead of a simplified/parameterized stand-in.

**Outcome: validated end to end, kept as a second lane, not adopted as the default.** Every blocker
hit getting `UpdateWorld` (and everything it actually calls) to compile turned out to be small and
mechanical — dead UI/demo code deleted (`Environ`'s `FeatureInActive`/`LoadConfiguration`, `UPDATE.PAS`'s
whole `UpdateUniverse`), trivial I/O helpers (`WriteVariable`/`ReadVariable` — just
`BlockRead`/`BlockWrite`+`IOResult`) duplicated locally instead of importing `Dos2`'s
`Printer`/`CRT`/`EIO`/`WND` chain for them, a couple of TP-isms (`STRG.PAS`'s inline-8086-opcode
`AllUpCase`, `PRIMINTR.PAS`'s real-mode `MaxAvail` heap check, fpc's stricter `$V+` string-length
matching), and one pure-math procedure (`GetIndustrialDistribution`) relocated verbatim out of
`INTRFACE.PAS` to avoid pulling in `Fleet`/`Orders`/`NPE` for a single function. A driver
(`runworld.pas`) assembling a real 2-planet `Universe^` and calling the real `UpdateWorld` reproduced
`TechLevelCases.OwnedWorldBehindCapitalAdvances` exactly (`techlevel=6`, matching `techlevel.golden`).
Also surfaced a genuine landmine: `DATASTRC.PAS:235`'s `GlobalSets ABSOLUTE SetOfActiveFleets` overlay
compiles cleanly under fpc but doesn't preserve Turbo Pascal's declaration-order memory layout it
depends on — writing through it silently corrupted the `Universe` pointer (a real access-violation
crash, not a compile error). Full writeup: `reference/verify/patch-based/README.md`.

**Recommendation.** Transcription stays the default for new isolated-procedure golden cases — cheap,
bounded, proven across four commits. Reach for the patch-based real-`Universe^` approach only when a
procedure's fidelity risk is high enough to justify it: many state-shaped lookups transcription would
otherwise have to simplify into plain parameters (`GetCapital`/`GetTech`-style calls), or call
*ordering* across a real pipeline is the thing worth checking — the exact "cross-cutting field" bug
class hit twice this session with transcription (`UpdateMilitary` mutating `Cargo.Legions` before
`UpdateRevolution` reads it). Don't expand into combat/fleet movement/NPE AI by default — `Intrface`'s
`Fleet`/`Orders`/`NPE` dependency was dodged here by relocating one function; the next subsystem's
dependency web is an open question, not something this result generalizes to.
