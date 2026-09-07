# Development log

How this port of Anacreon got built, bottom-up: simulation core first, UI last. This is a history,
not a status tracker — everything below without a note to the contrary is built and tested. The
"why does the C# port look like this" design decisions live in [`PORT_DESIGN.md`](PORT_DESIGN.md);
findings about the *original* Pascal source (dead code, quirks, bugs) live in
[`PASCAL_ARCHITECTURE_NOTES.md`](PASCAL_ARCHITECTURE_NOTES.md); known limitations in this port live
in [`OPEN_GAPS.md`](OPEN_GAPS.md). This doc links out to those where there's a real story rather than
duplicating it; the git log is the record of exactly what changed, commit by commit.

## 1. Economy / annual tick

`IAnnualTickHandler`. `UpdateUniverse` (UPDATE.PAS:1440-1488): `Year++`, then `UpdateWorld` over every
planet and starbase, then `UpdateConstruction`, then `UpdateEmpire`.

1. **Population, efficiency, revolution** (planets only) — `UpdateEfficiency` → `UpdatePopulation`
   → `UseUpFood` → `UpdateRevolution` (incl. `Rebellion`). `HostileLife` shipped with this commit too.
   `AnnualTickHandlerTests`/`AnnualTickHandlerRevolutionTests` (golden-file-backed for revolution).
2. **Industry and production** (planets only) — `ProduceRawMaterial`, `GetIndustrialDistribution`,
   `UpdateIndustry`, `Production`. Golden-file-backed (`AnnualTickHandlerProductionTests`).
   - **2b, ambrosia addiction** — `UseUpAmbrosia`. `IsAddictedToAmbrosia` becomes real state.
     Golden-file-backed (`AnnualTickHandlerAmbrosiaTests`).
   - **Golden-file ground truth, all six domains migrated to the patch-based lane** (`techlevel`,
     `military`, `starbase`, `ambrosia`, `revolution`, `production`) — see `PORT_DESIGN.md`'s
     ground-truth harness section for how the two lanes work and when to reach for each.
   - **2c, military buildup** — `UpdateMilitary`. Golden-file-backed (`AnnualTickHandlerMilitaryTests`).
3. **Tech advancement** (planets only) — `UpdateTechLevel`. Golden-file-backed
   (`AnnualTickHandlerTechLevelTests`) — the first domain moved to the patch-based lane.
4. **Starbase economy** — `SupplyLink`/`SurplusLink` plus industrial-complex-only production/economy.
   Introduced `IEconomicWorld` (see `PORT_DESIGN.md`). `AnnualTickHandlerStarbaseTests`, partly
   golden-file-backed (`runworld.pas`'s `starbase` domain).
5. **Construction and empire-level updates** — `UpdateConstruction` and `UpdateEmpire`.
   - **5a, empire-level tech research** — `NewTechLevel` (nested `GetChanceForNewTech`/`GetNewTech`).
     `Empire.Technology` gained a 4th bucket (`Resources`) — see `PORT_DESIGN.md`. Golden-file-backed
     (`AnnualTickHandlerEmpireTests`).
   - **5b, construction** — `UpdateConstruction` plus `ConstructStarbase`/`ConstructStargate` entity
     creation. Golden-file-backed (`AnnualTickHandlerConstructionTests`).

**Constants to extract:** `MaxPop` array (UPDATE.PAS:1077-1098), `BasePop` lookup, `TechAdj[]`,
`TechAdj2[]`, `K6`, `SuppliesPerBillion`, `DrugsPerBillion`, `ThgLmt()` clamp function.

## 2. Galaxy / new-game setup

`NEWGAME.PAS`'s `LoadScenario` reads a `.SCN` text scenario file and dispatches world/starbase/
stargate/nebula/mine placement and empire-creation commands one at a time — the scenario script *is*
the generation pipeline. `dos_131` is the canonical scenario set for this phase (matches the DOS 1.31
baseline this port targets); `dos_20`/`pack_1` (the 2.0 port's own sets) stay out of scope until an
opt-in v2-parity pass.

- **2a, shared helpers** — extracted `Rnd`/`Jitter`/`PascalRound`/`ClampResource`
  (`Core/PascalMath.cs`) and the tech-catalog (`Core/Entities/TechCatalog.cs`). Pure refactor.
- **2b, empire creation** (`Core/NewGame/EmpireFactory.cs`) — `CreateEmpire`/`CreatePlayerEmpire`/
  `CreateNPEmpire`'s starting tech-set formula, `DefenseSettings.Fleets` seeding. See `PORT_DESIGN.md`
  for the typed-enum "extra techs" design. Golden-file-backed (`empirecreate.golden`, 13 cases).
- **2c, explicit-coordinate placement** (`Core/NewGame/GalaxySetup.cs`) — `SetUpWorld`/`CreateWorld`/
  `CreateBase`/`CreateGate`/`CreateSRMs`/`CreateNebula`. `Empire.Capital` widened to
  `IEconomicWorld?` — see `PORT_DESIGN.md`.
- **2d, randomized placement** (`Core/NewGame/GalaxySetup.cs`, continued) — `GetRandomXY`,
  `CreateRndPlanet`, `CreateRandomWorlds`, `NebulaeBand`/`NebulaePatches`. Golden-file-backed
  (`trillumreserves.golden`, `randomplanet.golden`, `nebula.golden`).
- **2e, `.SCN` scenario file loading** (`Core/NewGame/ScenarioLoader.cs`) — the capstone integration
  test, golden-file-verified against 11 real `dos_131/*.SCN` files (`PRINCES.SCN` excluded — real
  1.31 itself can't load it either). Needed a from-scratch, empirically-verified port of fpc's actual
  `Random`/`RandSeed` algorithm (`PascalRandom.cs`) — see `PASCAL_ARCHITECTURE_NOTES.md`'s scenario
  golden-file section for why exact-match assertions are scoped to non-randomized fields only.

## 3. Probe movement and visibility

`UpdateProbes` runs once per empire at the start of that empire's own turn, resolved as the last step
of `VisibilityHandler.RefreshVisibility`. `Empire.ProbesInTransit: List<Coordinate>` replaces a
pre-existing 4-state `Probe`/`ProbeStatus` stub — see `PORT_DESIGN.md` and
`PASCAL_ARCHITECTURE_NOTES.md`'s dead-code notes for why. Golden-file-backed (`probescout.golden`,
`VisibilityHandlerProbeTests.MatchesGoldenFile`).

## 4. News

Ported `NEWS.PAS`'s per-empire event log — a real data dependency for NPE AI (below), which reads it
as its "what happened to me this turn" signal, not just a UI nicety. `NewsItem`'s design (splitting
Pascal's `Loc` union into typed fields, `ISectorObject`, `OtherEmpire`) is in `PORT_DESIGN.md`.
`EraseNews`'s per-turn reset wasn't wired into `TurnEngine` at the time — no consumer existed yet to
validate the timing against; `TurnEngine.AdvanceOneTurn` picked this up later, once Kingdom's
`ReviewNews` became that first real consumer (see §6d below).

## 5. Combat

Attack resolution, fleet/starbase destruction, capital loss and empire elimination, plus the
standalone mechanics (LAM strikes, minefield/disrupter movement, self-destruct) that sit outside the
main group-combat round loop. Started with a scoping pass (5a) over `ATTACK.PAS`/`ATTCOMM.PAS`/
`ATTNPE.PAS`/`BATTLE.PAS`/`BOMBER.PAS`/`FLEET.PAS`/`FLTCOMM.PAS`/`SBASE.PAS`/`ORDERS.PAS` plus relevant
`UPDATE.PAS`/`DATACNST.PAS`/`DATASTRC.PAS`/`ANACREON.PAS`/`INTRFACE.PAS` sections. Design decisions
(empire-elimination model, `Empire.DefeatedBy`, `AttackType`) are in `PORT_DESIGN.md`; dead-code and
quirk findings (`BATTLE.PAS`/`BOMBER.PAS`, `ATTNPE.PAS` naming, `HolocaustWorld`, etc.) are in
`PASCAL_ARCHITECTURE_NOTES.md`.

- **5b, `UpdateDefenses` + `Defns` state** (`AnnualTickHandler.Defenses.cs`) — `DefenseType`-indexed
  growth on `IEconomicWorld`. Golden-file-backed (`defenses.golden`, `AnnualTickHandlerDefensesTests`).
- **5c, combat constants + `AttackType`** (`Types/AttackType.cs`, `Combat/CombatConstants.cs`) — see
  `PORT_DESIGN.md` for the unified-enum design. Tables: `CombatTable`, `WeapEff`, `ShipValue`,
  `CombatPower`, `ProtecOffered`/`ProtecNeeded`/`TrnAdj`/`GdmKill`, `CargoSpace`, `CombatTechAdj`,
  `CombatClassAdj`, `CombatBaseAdj`, `GdmLaunch`.
- **5d, group/shell combat engine core** (`Combat/CombatState.cs`, `Combat/CombatEngine.cs`) —
  `CalculateCombatData`, `GetEnemy`, `DefaultDistribution`/`DefaultGroup`, `ForcesUnknown`, `Battle`,
  `EnemySurrenders`. Needed `ATTACK.PAS.patch` (see `PORT_DESIGN.md`'s ground-truth section) to link.
  Golden-file-backed (`combat.golden`, `CombatEngineTests`). Found and fixed the `PascalRound`
  banker's-rounding bug here — see `PASCAL_ARCHITECTURE_NOTES.md`.
- **5e, resolution loop + `NPEAttack` entry point** (`Combat/CombatResolution.cs`) — `AdvanceGroups`,
  `AllAdvance`/`TrnAdvance`, `FleetRetreats`, `TransportsLeft`, `GroupEngage`, `FleetEngage`/
  `WorldEngage`'s `Targetting`. Needed `ATTNPE.PAS.patch`. Golden-file-backed (`npeattack.golden`,
  `NpeAttackTests`).
- **5f, outcome application + empire elimination** (`Combat/CombatOutcome.cs`) — `ConquerWorld`,
  `ConquerEmpire`, `RestoreCombatant`, `ResolveAttack`, `DestroyEmpire`/`DestroyFleet`/`AbortFleet`.
  See `PORT_DESIGN.md` for the empire-elimination/`DefeatedBy` model and the `NewsItem.Defender`
  addition. Extended `npeattack.golden` with a second Empire2 world to exercise `ConquerEmpire`'s
  per-planet cascade directly; two hardcoded tests (`CombatOutcomeTests.cs`) cover the human-defeat
  branch the harness can't reach.
- **5g, standalone mechanics** (`Combat/CombatStandalone.cs`) — `LAMAttack` and
  `DestroyConstructionOrGate` are live Pascal (`ATTCOMM.PAS`/`DESIGN.PAS`/`NPE00`/`NPE03`/`NPEINTR`
  callers), ported and wired: `DestroyConstructionOrGate` is now `CombatResolution.NPEAttack`'s own
  Con/Gate branch, matching `ATTNPE.PAS`'s real dispatch (5e had deferred it — no `ConstructionSite`/
  `Stargate` target type existed yet at the time). `HolocaustWorld`/`HolocaustEffectiveness` are
  **not ported** — confirmed genuinely dead code, not "no caller wired up yet": `MSCCOMM.PAS`'s
  `HolocaustCommand` is wrapped in a Pascal comment (forward declaration included), and its
  `PLAYTURN.PAS` dispatch entry sits inside a separate disabled `(*ARTIFACTS*)` block, in both the
  1.31 and 2.0 source trees — see `CombatStandalone.cs`'s own doc comment and
  `docs/QUESTIONS_FOR_GEORGE.md`. `SelfDestructObject` is ported with no port-side caller yet (Phase 8,
  same `Empire.News.Clear()` precedent) — its own Pascal caller (`MSCCOMM.PAS`'s
  `SelfDestructCommand`) *is* live, an earlier research-pass claim to the contrary was wrong and has
  been corrected. `LAMAttack` is golden-file-backed (`lamattack.golden`, `LamAttackTests` — the only
  Phase 5 procedure with zero `Rnd` calls, a pure deterministic check of its proportional-distribution
  formula); `DestroyConstructionOrGate`/`SelfDestructObject` are covered by hardcoded C# tests instead
  (`CombatStandaloneTests.cs` — `DestroyConstruction`/`DestroyStargate` need `Intrface`, not linked
  into this harness's patched build; `SBASE.PAS` was never patched in at all). See
  `reference/verify/README.md`'s domain catalog for the full harness-coverage story.
- **5h, minefield damage + disrupter blocking + minefield visibility** (`Turns/FleetMovementHandler.cs`,
  `Galaxy/Galaxy.cs`) — `MineFieldDamage`/`InRangeOfDisrupter` folded additively into
  `FleetMovementHandler.AdvanceFleet`'s step loop, now a real per-cell walk for `JumpFleet`/
  `HunterKillerFleet` (Pascal's own `FltTyp IN [JumpFleet,HKFleet]` gate) instead of the single bulk
  step other fleet types keep; a mine or disrupter hit stops movement for the turn but the fleet still
  lands on the cell it was hit at, matching source exactly (verified by reading `FLEET.PAS:646-859`'s
  `GOTO ExitMoveLoop` fallthrough directly, not assumed). New `Galaxy._mineScoutedBy` (`MarkMineScouted`/
  `ClearMineScouted`/`IsMineScoutedBy`) — can't reuse `EntityVisibility<T>`, which is keyed by entity
  reference, not coordinate. `FleetMovementHandler` now takes a constructor-injected `Random`, matching
  `AnnualTickHandler`'s own convention. Covered by hardcoded tests (`FleetMovementHandlerTests.cs`) —
  same precedent as 5g's `CombatStandaloneTests.cs`; no golden-file domain, since `FleetMovementHandler`
  was never linked into the patch-based harness in the first place. While tracing the exact move loop, a
  second movement-fidelity gap surfaced beyond the two already tracked below: dense-nebula movement
  blocking (`GetNewPos`'s own `Pos:=Limbo` branch, `FLEET.PAS:437-450`) isn't ported either — found but
  not fixed here, since it's a terrain effect applying to every fleet type, not a combat mechanic; added
  to 6a's list below alongside the other two.
- **5i, `HostileLife` news fix** (`Turns/AnnualTickHandler.Revolution.cs`) — `HostileLife`
  (UPDATE.PAS:458-515, Phase 1) had the real population/troop/revolution-index arithmetic but called
  no `AddNews` at all, despite `HostileLifeKilledPopulation`/`HostileLifeAttackedTroops`/
  `HostileLifeJoinedTroops` already existing in `NewsType.cs` — a dropped call site Phase 4 should
  have caught (it's in `AnnualTickHandler.*`, Phase 4's own stated scope) but didn't, surfaced instead
  during Phase 5's scoping pass. `HLfCls` is a real, live-reachable world class, not dead code — its
  `UpdateWorld` call site is unconditional (`IF Cls=HLfCls THEN HostileLife(World)`, no disabled
  block), and `dos_131/INTRO.SCN`'s own `ClassTable` gives it a nonzero weight (1, same tier as
  Ambrosia/Paradise/Ruins) — about 1-in-100 for any randomly-generated world. Covered by hardcoded
  tests (`AnnualTickHandlerHostileLifeTests`) exploiting `FixedRandom(N)`'s `min+N` resolution to
  deterministically pick each of the three branches.

## 6. NPE AI (Kingdom)

Implement an `ITurnHandler` for computer empires. The roadmap's original one-line framing here
("start with one classic implementation") undersold this phase the way "8 known News sites"
undersold Phase 4: six parallel research forks read every NPE-adjacent Pascal file in full
(`NPE.PAS`/`NPETYPES.PAS`/`NPE00`-`NPE04.PAS`/`NPEINTR.PAS`, ~4,400 lines of real decision logic)
before planning. Design rationale is in `PORT_DESIGN.md`; this section narrates the build.

Moved after Probes/News/Combat (was Phase 3 originally): NPE decision-making is written against
combat primitives from the start (`NPE01.PAS`'s `USES` clause pulls in `Attack`/`AttNPE`) and reads
the News feed as a real sensory input, not just a display concern.

**Corrected scope**: real Pascal has **four structurally distinct NPE personalities** (Pirate,
Kingdom, Berserker, Guardian), each its own file with its own private data record, dispatched via
`NPE.PAS`'s `NPEData[Emp].Typ: NPEmpireTypes` — not one AI with a "classic" and "advanced" tier.
The one place that framing actually holds is `Kingdom1NPE`/`Kingdom2NPE`, which share one real
implementation (`NPE02.PAS`) and differ only in their `NPECharacterRecord` persona seed (passive
vs. aggressive). Reachability, checked against all 12 `dos_131/*.SCN` files' `CreateNPEmpire`
directives (`NEWGAME.PAS:1219-1259` reads the type as a raw ordinal):

| Ordinal | Type | Scenarios using it |
|---|---|---|
| 1 | Pirate | ARRONAX, GAUNTLET, JAKARTA |
| 2 | Kingdom1 (passive) | AWAKEN, GAUNTLET, PERIPHER |
| 3 | Kingdom2 (aggressive) | every scenario with any NPE at all |
| 4 | Berserker | ARRONAX only |
| 5 | Guardian | never — not used by any `dos_131` scenario |
| 6 | Trader | never, and confirmed dead code (see below) |

This phase builds **Kingdom only** (both persona presets, one implementation) — the personality
that actually dominates real scenario content — plus the shared infrastructure (dispatch,
per-empire/per-fleet AI state, the `NPEINTR.PAS` toolkit) sized so Pirate/Berserker/Guardian can
slot in later without rework, per the user's explicit direction when this was scoped. Each gets
its own future roadmap entry when picked up rather than being built speculatively now (Guardian
in particular — zero `dos_131` usage — would be scope built ahead of any demonstrated need).
`TraderNPE` is confirmed dead, not just unreachable: zero case arms in any of `NPE.PAS`'s 5
dispatch procedures (every one falls through to Pirate behavior), no `TraderDataRecord` in
`NPETYPES.PAS`, and zero scenario usage — same treatment as Phase 5's `BATTLE.PAS`/`BOMBER.PAS`,
not ported. `DeployHarassFleet` (`NPEINTR.PAS:777`) and `ImplementDefendBMS` (`NPE04.PAS`) are
confirmed empty `BEGIN END` stubs in both the 1.31 and 2.0 source trees — intentional no-ops the
original developers shipped incomplete, not a port gap.

- **6a, fleet/starbase movement fidelity** (`Turns/FleetMovementHandler.cs`, `Entities/
  FleetLogistics.cs`) — `AdvanceFleet`/`AdvanceStarbases` (shipped pre-Phase-1, deliberately
  simplified straight-line steppers) are now the real thing. Fuel/cargo model: the prior
  `_fuelBurnPerShip` table was invented, not ported — compared against DATACNST.PAS's real
  `FuelCons`/`FuelCap` while scoping and found it backwards in places (Jumpships cheaper than
  Starships, the reverse of source); replaced with `FleetLogistics` (`FuelCapacity`/
  `FuelConsumption`/`FleetCargoSpace`/`BalanceFleet`, MISC.PAS/INTRFACE.PAS verbatim) and
  `Fleet.Fuel` widened to `double` to match Pascal's own Real-typed arithmetic. Movement: dense-nebula
  blocking is now a real per-step check for every fleet type (not just Jump/HK); stargate
  teleportation and fortress pass-through jumps are real, including the non-obvious case where a
  fortress hop landing short of teleport range still gets its ordinary per-turn movement allotment
  on top, same turn (`FLEET.PAS:646-861`'s `UpdateFleet`, verified line-by-line); `UpdateAllFleets`'s
  dispatch now checks gate occupancy owner-blind, via new `Galaxy.GetObjectAt`. Starbase movement
  (`MovePlayerStarbases`/`GetNewBasePos`) is now obstacle-avoiding and fuel-costed instead of a
  straight-line stepper — removed `YearsUntilNextMove`, an invented throttle with no source
  counterpart (confirmed via `MovePlayerStarbases`'s actual `ANACREON.PAS` call sites: always once
  per empire per turn, same cadence as fleets). Golden-file-backed (`fleetlogistics.golden`,
  `fleetmove.golden`) — `FLEET.PAS` and (trimmed to just what it needs, not relocated elsewhere)
  `INTRFACE.PAS` are now patched into `reference/verify/`, the first NPE-adjacent units this port's
  ground-truth harness links; see that directory's own README for the "trim a unit down to size"
  story (`EIO`/`Mess`/`Orders`/`NPE` all turned out to be reachable-but-unused from `FLEET.PAS`'s
  real needs). `GetNewBasePos`/`XY2Dir` (`SBASE.PAS`, starbase obstacle-avoidance) have no golden
  domain yet — `SBase` isn't patched in — covered by hardcoded tests in `FleetMovementHandlerTests.cs`
  instead, same "harness can't reach it yet" precedent as 5g's `DestroyConstructionOrGate`.
- **6b, core NPE dispatch + state** (`Types/NpeEmpireType.cs`, `Turns/KingdomTurnHandler.cs`) —
  `Empire.NpeType: NpeEmpireType?`, wiring `ScenarioLoader.RunCreateNPEmpire` (previously read and
  discarded the ordinal) to record it and construct a `KingdomTurnHandler` for Kingdom1/Kingdom2
  empires only — other types stay unregistered in `Game.TurnHandlers`, matching the existing "ai
  has no entry" precedent in `TurnEngineTests.cs`. `KingdomTurnHandler.PlayTurn` is a shell
  (`NotImplementedException`) until 6d builds its real logic. `RndVar` (`INT.PAS:120-131`) turned
  out to already be ported — `PascalMath.Jitter` is the exact same formula
  (`Trunc(Value*(Variation/100))` then `Rnd(Value-temp,Value+temp)`), just named for what it does
  rather than transcribed; no new method needed. `TraderNPE`'s dead-code finding and
  `DeployHarassFleet`/`ImplementDefendBMS`'s confirmed-no-op finding are in
  `PASCAL_ARCHITECTURE_NOTES.md`.
- **6c, `NPEINTR.PAS` toolkit — read-and-compute half** (`Core/Npe/NpeToolkit.cs`,
  `Core/Npe/NpeConstants.cs`, `Core/Npe/NpeTypes.cs`) — `MilitaryPower`, targeting
  (`GetBestTarget`/`GetBestRaiderTarget`/`GetBestBase`/`GetBestPlanetToProtect`/
  `MinimumDefense`/`AverageMilitaryPower`), regional bookkeeping (`CreateRegionArray`/
  `GetRegionalCapital`/`EnforceNpeDataLinks`), world (re)designation (`GetNewDesignation`/
  `ReDesignateEmpire`), `GetFleetComposition`, `SetEmpireDefenses`, `PlunderWorld` — everything in
  NPEINTR.PAS that reads state and computes a value rather than creating/moving a fleet. Includes
  the `FleetDataRecord` field audit (see `PORT_DESIGN.md`'s derive-don't-duplicate note — `Waiting`
  turned out real, not derivable as an earlier pass guessed; `Midway` confirmed dead;
  `BlockX`/`BlockY` Pirate-only). Hardcoded-tested (`NpeToolkitTests.cs`), not golden-file — see
  `PORT_DESIGN.md`'s own note on why (now that save/load exists, a real test universe built from a
  saved game is cheap enough that this could be revisited, but hasn't been). Split off from this
  commit, not bundled in: the five `Deploy*Fleet`/eight `Implement*MSN`
  procedures, which need fleet-lifecycle primitives (`DeployFleet`/`ChangeCompositionOfFleet`/
  `EstimatedDateOfArrival`/`EstimatedRange`/`RefuelFleet`/`SetFleetDestination`) that don't exist
  anywhere in this port yet — see 6c-2.
- **6c-2, `NPEINTR.PAS` toolkit — fleet-lifecycle half.** Split into two landings: the six
  fleet-lifecycle primitives first (independently verifiable), then the `Deploy*Fleet`/
  `Implement*MSN` layer on top (unverifiable until the primitives are right) — same reasoning that
  split 6c itself.
  - **Primitives** (`Entities/FleetLifecycle.cs`) — `DeployFleet`/`ChangeCompositionOfFleet`/
    `RefuelFleet`/`SetFleetDestination` (FLEET.PAS) and `EstimatedDateOfArrival`/`EstimatedRange`
    (INTRFACE.PAS, both `Fleet` and `Starbase` arms ported). Genuinely new primitives — every prior
    phase only ever moved or destroyed fleets scenario loading or human setup already created,
    never made one from a world's own stock. `FltMovementRate` moved from
    `Turns/FleetMovementHandler.cs` into `Entities/FleetLogistics.cs` (`MovementRate`) once
    `EstimatedDateOfArrival` became a second real consumer of the same DATACNST.PAS table.
    `CombatOutcome.AbortFleet` promoted `private`→`internal` (same precedent as `DestroyFleet`) so
    `ChangeCompositionOfFleet`'s self-destruct branches can call it — no behavior change.
    `Game.HasScouted`→`Scouted`, promoted `private`→`public` (roslyn-renamed), mirroring `Known`,
    for 6c-2's second half's `DestroyAllFleetsInSector`. Two real quirks found and ported verbatim,
    not fixed: `ChangeCompositionOfFleet`'s non-fleet-ground branch reads `GetTrillum(GroundID)`
    *after* `PutCargo(GroundID,NewGCr)` already overwrote it (FLEET.PAS:356-365) — "how much trillum
    is available to convert" is whatever the new ground composition says, not what was there before;
    and `DeployFleet`'s `IF GetFleetFuel(FltID)=0 THEN SetFleetFuel(FltID,10)` is a genuine free
    top-up when a small enough fuel deficit rounds down to "0 tons needed" even with zero trillum on
    hand. `CombatOutcome.AbortFleet`'s pre-6a "no fuel-capacity system exists" excuse no longer holds
    now that `FleetLogistics.Fuel`/`FuelCapacity` exist — a real, confirmed gap (leftover fuel
    vanishes instead of becoming trillum), deliberately left for its own follow-up commit rather than
    bundled here (see `PASCAL_ARCHITECTURE_NOTES.md`). Hardcoded-tested (`FleetLifecycleTests.cs`),
    same rationale as 6c.
  - **`Deploy*Fleet`/`Implement*MSN` layer** (`Core/Npe/NpeToolkit.cs`, alongside 6c's
    read-and-compute half) — the five `Deploy*Fleet` procedures (`DeployBattleFleet`/
    `DeployCargoFleet`/`DeployJumpAttack`/`DeployHKRaiders`/`DeploySlowAttack`; `DeployHarassFleet`
    is the confirmed no-op stub, not ported) and all eight `Implement*MSN` mission executors
    (`ImplementReturnMSN`/`SupplyMSN`/`RefuelMSN`/`ConquerMSN`/`RaidTrnMSN`/`JumpAttackMSN`/
    `StackMSN`/`GuardMSN`) plus their shared helpers (`SetFleetReturn`/`SetRaidingFleetNewTarget`/
    `DestroyAllFleetsInSector`). `GetBestPlanetToProtect` widened from `IEconomicWorld` to
    `ISectorObject` — real Pascal's own `BaseID: IDNumber` is generic, and `ImplementStackMSN`'s
    real call site passes a `Fleet`, not a world. Two more verbatim quirks found and documented (see
    `PASCAL_ARCHITECTURE_NOTES.md`): `DeploySlowAttack`'s fallback branch deploys with
    `JumpAttackMSN` instead of `SlowAttackMSN` (an adjacent-branch copy/paste slip in the 1988
    source), and `ImplementRaidTrnMSN`'s `TargetID` parameter is confirmed unused (overwritten by
    `GetObject` before ever being read). `SetRaidingFleetNewTarget` has no real caller yet (6d) —
    ported anyway, same "primitive ready for whoever needs it" precedent as Phase 5g's
    `SelfDestructObject`. Two genuine C# translation mistakes here, both caught by a pre-commit
    review pass and fixed with regression tests that fail against the buggy code, not Pascal quirks:
    `DeployBattleFleet`'s probe loop re-drew its RNG bound every iteration (`for (var i = 0; i <
    Rnd(random, 1, 4); i++)` calls `Rnd` on every condition check, where Pascal's `FOR i:=1 TO
    Rnd(1,4) DO` evaluates the bound once at loop entry — fixed by hoisting the draw out of the loop
    header); and `ImplementJumpAttackMSN`'s post-LAM-strike power gate read live `target.Ships`
    instead of the pre-strike snapshot Pascal actually compares against, so it compared the attacker
    against the wrong enemy-ships value whenever the LAM branch fired — fixed by snapshotting
    `target.Ships` before the strike. Hardcoded-tested (`NpeToolkitDeployImplementTests.cs`), same
    rationale as 6c/6c-2's primitives.
- **6d, Kingdom core loop** (`Core/Npe/NpeToolkit.cs`, `Core/Npe/NpeTypes.cs`,
  `Core/Turns/KingdomTurnHandler.cs`) — `NPE00.PAS`'s `DefendEmpire`/`ImperialExpansion`/
  `NPEConquest`/`CargoSupplyFleet`/`ExplorationAndProbing` (plus their own nested helpers —
  `AttackEnemyFleets`/`NoOfGuardsAtBase`/`GetBestBaseToProtect`/`ModifyPersona`/
  `GetClosestCargoWorld`/`SendRescueFleet`/`RNIndustryLack`) land on `NpeToolkit`, confirmed shared
  with Pirate/Berserker (grepped: all three call into `NPE00.PAS`, same "shared toolkit" reasoning
  as `NPEINTR.PAS`) rather than folded into `KingdomTurnHandler`. `NPEINTR.PAS`'s
  `MidCourseCorrection` — missed by both 6c and 6c-2's own passes over that file — also lands here,
  first real caller. `KingdomTurnHandler`'s constructor is now real Pascal's own `InitializeNPE`
  call (`NEWGAME.PAS:1250`, immediately after `CreateEmpire`, during scenario load itself, not
  lazily on first turn): `InitializeKingdom1NPE`/`InitializeKingdom2NPE`'s persona-seed presets,
  seeded from the exact same `Random` instance `ScenarioLoader` already uses, so these draws land at
  the real position in the scenario-load RNG stream (`ScenarioLoader.RunCreateNPEmpire` updated to
  pass `(empire, npeType, random)` through). `PlayTurn` is `ImplementKingdom1NPE`'s real per-turn
  sequence: `EnforceNPEDataLinks` → `CreateRegionArray` → `UpdateFleets` → `ReviewNews` →
  (wars/foreign affairs — see below) → `DefendEmpire` → `ImperialExpansion` → (every-7th-turn
  `ReDesignateEmpire`) → `ExplorationAndProbing`.
  - **No diplomacy yet (Phase 6e).** `StateDepartment`/`StateDeptReport`/`WarCabinet` are real
    Pascal but 100% diplomacy state — `ReviewNews` is scoped to its non-diplomacy branches (NoFuel/
    IndLack) only; its enemy-attack case arm (the `Policy`/`Aggressiveness` state-machine plus the
    News-driven `Balance` decrement) is deferred alongside them, since nothing else ever advances
    `StateDeptRecord.Policy` off its initial seed. `StateDeptRecord`/`PolicyType`
    (`Core/Npe/NpeTypes.cs`) are real now regardless — `Kingdom1DataRecord`'s own `State` field
    needs to exist for the persona-seed presets to populate, even with the procedures that act on it
    deferred.
  - **`StateDeptRecord` lookup is create-on-demand, not pre-seeded**, keyed by the *other* empire —
    including `Empire.Independent`. Real Pascal's `StateDeptArray = ARRAY[Empire]` (Empire1..Empire8
    plus Indep) always has all 9 slots allocated; this port's empires don't all exist yet when a
    `KingdomTurnHandler` is constructed (later `CreateNPEmpire`/`CreatePlayerEmpire` commands can
    still be pending in the same scenario file), so a `Dictionary` snapshot at construction time
    would miss them. Confirmed load-bearing, not speculative: `UpdateFleets`' `ConquerMSN` case does
    `Inc(State[EnemyEmp].Balance)` where `EnemyEmp` is `Empire.Independent` for the overwhelmingly
    common "conquered an independent world" case — a `KeyNotFoundException` without this, caught by
    `KingdomTurnHandlerTests.PlayTurn_ConquersIndependentWorld_HandlesBalanceForIndependentTarget`.
  - **`TurnEngine.AdvanceOneTurn` now clears `Empire.News` right after `PlayTurn`**, matching
    `ANACREON.PAS:246-248`'s own `ImplementNPE(Emp); EraseNews(Emp);` sequencing — the Phase 4 gap
    ("no consumer exists to validate the timing against") this phase's `ReviewNews` is the first real
    consumer for: without it, a standing `NoFuel` item re-fires `SendRescueFleet` every turn forever
    (no `AlreadyTargetted` guard on that path).
  - **`ExplorationAndProbing`'s `REPEAT/UNTIL` hangs forever on an empty region-capital list** in
    real Pascal (`NoMoreProbes` is only ever set inside the `FOR` loop's own body) — not reproduced;
    an empty `regionCapitals` returns immediately instead, matching this port's own precedent for
    not reproducing a genuine Pascal hang (see `GetRegionalCapital`'s null-return doc comment). Also
    genuinely hangs with a *non-empty* region-capital list under a constant-valued RNG stub
    (`FixedRandom`) if the capital sits at a galaxy edge — a property of the algorithm needing RNG
    progress to terminate, not a porting bug; `KingdomTurnHandlerTests` places its capitals away from
    the edge for exactly this reason.
  - **`IEconomicWorld`/`Fleet`'s shared "has Ships and Cargo" duck-typing became a real interface**,
    `IShipCargoHolder` (`Core/Entities/IShipCargoHolder.cs`) — every `object`-typed parameter whose
    real type union was exactly `Fleet | IEconomicWorld` (`FleetLifecycle.DeployFleet`/
    `ChangeCompositionOfFleet`/`RefuelFleet`, `CombatEngine.CalculateCombatData`/`GetEnemy`,
    `CombatStandalone.LAMAttack`) is now compile-time checked instead of a runtime `switch`/`is`
    dispatch. Deliberately *not* applied to `object` params with a wider real union
    (`CombatOutcome.AbortFleet`'s `ground`, which also accepts a `ConstructionSite`/`Stargate` as a
    no-op; `CombatResolution.ResolveAttack`'s `target`, which dispatches on concrete type for
    different behavior, not uniform `Ships`/`Cargo` access) — see the type's own doc comment.
  - Covered by `KingdomTurnHandlerTests.cs` — the first commit where a whole NPE turn runs end to
    end, so unlike every prior 6x commit's per-procedure hardcoded tests, this exercises real
    cross-procedure dispatch a single method's own test can't reach (the `State[Independent]` case
    above, `UpdateFleets`' fleet-liveness guard, `ExplorationAndProbing`'s empty-list guard).
- **6e, Kingdom diplomacy** (`Core/Npe/NpeToolkit.cs`, `Core/Turns/KingdomTurnHandler.cs`) —
  `StateDepartment`/`StateDeptReport`/`WarCabinet`, and `ReviewNews`'s enemy-attack case arm
  (`AttackSeverity`/`RespondToEnemyAttack`, its own nested procedures). `KingdomTurnHandler.PlayTurn`
  now matches `ImplementKingdom1NPE`'s real sequence in full: `StateDeptReport` once at `Clock=0` →
  `UpdateFleets` → `ReviewNews` → `StateDepartment` → `WarCabinet` → `DefendEmpire` →
  `ImperialExpansion` → (every-7th-turn `StateDeptReport` + `ReDesignateEmpire`) →
  `ExplorationAndProbing`.
  - **Found and fixed a real Phase 6c bug before this landed: `NpeToolkit.MilitaryPower` was
    weighting by the wrong Pascal table.** `MISC.PAS`'s real `MilitaryPower` weights by
    `DATACNST.PAS`'s `MPower` (LAM=100, def=100, GDM=10, ion=50, fgt=1, hkr=20, jmp=12, jtn=1,
    pen=25, str=100, trn=0); the port instead read `CombatConstants.CombatPower`,
    `ATTACK.PAS`'s own, separately-declared, differently-valued table for the surrender algorithm
    (LAM=80, def=75, ..., trn=1) — confirmed distinct by reading both declarations directly, not
    assumed from the similar name/role. Found while porting `AttackSeverity`, which indexes
    `MPower` directly (`NPE00.PAS:128`) and so couldn't be written correctly against the wrong
    table. Fixed by adding the real `CombatConstants.MPower` and repointing `MilitaryPower` at it —
    this had been silently wrong since 6c for `AverageMilitaryPower`/`GetBestTarget`/
    `DeployJumpAttack`/`SlowAttack` budgets/`AttackEnemyFleets`/`ImperialExpansion`. Full suite
    passed 408/408 both before and after the fix — no golden-file domain routes through this
    arithmetic, so the fix is isolated to NPE decision logic with no other attributable movement.
  - **`StateDeptReport`'s per-enemy loop reads its own capital, not the enemy's, for the tech-based
    threat multiplier** — a real Pascal defect (`NPEINTR.PAS:1609`, already flagged at
    `PASCAL_ARCHITECTURE_NOTES.md`'s 4.5 section), ported verbatim rather than fixed: `Tech` is
    always identical to `EmpireTech`, so `IF Tech>EmpireTech`/`IF Tech<EmpireTech` can never fire.
  - **`WarCabinet` has no `EnemyEmp<>Emp` guard**, unlike `StateDepartment`'s otherwise-identical
    loop — `EmpireActive(Emp)` is trivially true, so an empire whose default Policy seeds at or
    above `HarassPLT` (Kingdom2's does) has a real per-turn chance of deploying raiders/battle
    fleets against its own gates/construction sites/planets, since `GetBestRaiderTarget`/
    `GetBestTarget` filter candidates only by ownership. Gated on the attacker having scouted its
    own worlds (`Game.Known`), which full turn sequencing normally already provides by the time NPE
    turns run — a real, latent defect rather than one that fires every turn regardless of state.
    Ported verbatim, not guarded against; see `NpeToolkit.WarCabinet`'s own doc comment.
  - **Extracted `IndustryConstants.cs`** (`Core/Entities/`) for TechAdj2 — `StateDeptReport`'s own
    `GetEmpireStatus` needs the same IP formula `AnnualTickHandler.Production.cs` already had as a
    private table; a second real reader is exactly the "derive, don't duplicate" bar for pulling a
    9-row balance table out rather than copying it.
  - `StateDeptRecord.Worlds`/`TotalMilitary`/`ThreatAssess` (seeded but unused since 6d) are now
    real, live fields — `GetEmpireStatus` (`INTRFACE.PAS:654-719`, scoped to just what
    `StateDeptReport` reads: no `TotalPop`, since nothing downstream consumes it) sums owned
    planets/starbases/fleets' ship counts and — for planets and `IndustrialComplex`-kind starbases
    only — shipyard industry.
  - Covered by `NpeDiplomacyTests.cs`, exercised through `ReviewNews`'s public entry point since
    `AttackSeverity`/`RespondToEnemyAttack` are private nested procedures in real Pascal too: one
    test confirms the severity scan stops at the next headline rather than reading into a second
    attacker's own `DestructionDetail` entries, the other confirms `NeutralPLT`'s arm always
    escalates (never stays `Neutral`). No new golden-file domain — `StateDeptReport` needs the same
    full hand-assembled universe as `NpeToolkit.cs`'s other methods above; see 6c's own note.
- **6f, roadmap wrap-up.** Kingdom (both persona presets) is fully ported: dispatch/state (6b),
  the shared `NPEINTR.PAS` toolkit's read-and-compute half (6c) and fleet-lifecycle half (6c-2),
  the core per-turn loop (6d), and diplomacy (6e). Disposition of every other NPE personality, so
  picking this phase back up doesn't require re-deriving the reachability table above:
  - **Pirate** (`NPE01.PAS`) — real, reachable (ARRONAX/GAUNTLET/JAKARTA), not built. Its own future
    roadmap entry; shares `NPEINTR.PAS`'s toolkit (already built) but has its own separate
    `Implement`/persona-free dispatch, not a Kingdom variant.
  - **Berserker** (`NPE04.PAS`) — real, reachable (ARRONAX only), not built. Own future entry;
    distinguishing mechanic is that its starbases are the mobile roaming unit, driven by a separate
    `BaseMissionTypes` state machine — a genuinely different shape from Kingdom/Pirate's fleet-based
    missions, not a drop-in reuse of `KingdomFleetState`.
  - **Guardian** (`NPE03.PAS`) — real but zero `dos_131` scenario usage. Deliberately not built
    ahead of demonstrated need (would be speculative scope); smallest of the four to port when it's
    actually needed (no fleet movement, no diplomacy — just a per-turn LAM-defense loop).
  - **Trader** — confirmed dead code, not merely unreachable: zero case arms in any of `NPE.PAS`'s 5
    dispatch procedures, no `TraderDataRecord` in `NPETYPES.PAS`, zero scenario usage. Not planned.
  - `DeployHarassFleet` (`NPEINTR.PAS:777`)/`ImplementDefendBMS` (`NPE04.PAS`) stay confirmed empty
    stubs for whichever personality eventually needs them (Pirate/Berserker respectively) — nothing
    to port, already verified in both the 1.31 and 2.0 trees.
  - `LoadNPE`/`SaveNPE` (binary `.SAV` serialization, including the `Version<12` legacy-format
    branches) — Phase 7, not this phase, regardless of which personalities exist by then.

## 7. Save/load

`docs/SAV_FILE_FORMAT.md` documents the real on-disk `.SAV` byte layout in full (every section,
file:line cited into `reference/DOSAnacreonSource131/`, checked against 13 real save files in
`reference/saves/`), with `scripts/savtool.py`/`.ps1` as an independently-built, byte-verified
`.SAV` ⟷ JSON oracle. This phase builds on that:

**Scope, decided with the user, revising this entry's original two-line blurb**: DOS `.SAV`
*import* (`LoadGame`) is the real priority — real captured/played saves are a valuable ground-truth
and testing asset, and user-facing import convenience. This port's actual long-term native save
format will be a **new JSON format** (direct object-graph serialization of `Game`, not the DOS
binary shape, and not the "higher-level semantic" format `SAV_FILE_FORMAT.md`'s closing section
sketches — that's more design/maintenance effort than warranted while the entity model is still
moving). `SaveGame`-to-`.SAV` is downgraded to a minimal, test-only tool whose only job is
confirming real Pascal `LoadGame` accepts this port's output — not a maintained, byte-faithful
feature.

- **7a, merge + scope + primitives.** Merged `main` (Phase 6 Kingdom AI, landed after this
  branch split off `sav_file_format_doc` at `28afe88` — confirmed zero file overlap before
  merging, so a clean merge) so the NPE Data section has real `KingdomTurnHandler` state to read.
  Byte-level primitive readers for `SAV_FILE_FORMAT.md`'s documented conventions (fixed-width
  little-endian ints, `STRING[N]`, `SET OF T` bitsets, `IDNumber`, opaque-byte passthrough).
- **7b, Header + Environment + Sector `LoadGame`** (`Core/SaveFormat/SavGameLoader.cs`). Eight
  placeholder `Empire` slots allocated up front resolve every empire ordinal read before Empire
  Data actually names/activates them (Environment's `Player`, Sector's mine owner/`MineScout`) —
  same object identity Empire Data (7d) later fills in with real fields, never added to
  `Game.Empires` unless that slot turns out `InUse`. Confirmed directly from `GALAXY.PAS`/
  `INTRFACE.PAS` (grepped every `.Obj:=`/`Flts:=` write site in the 1.31 tree) that `Sector.Obj`
  is never a fleet reference — only `CreatePlanet`/`CreateStarbase`/`CreateStargate`/construction
  -site creation write it — so `Obj`/`Flts` are fully redundant with what Planets/Starbases/
  Fleets/Stargates/ConstructionSites (7c) independently provide and are read-and-discarded;
  `Special`'s nibbles (nebula type, mine-placing empire — sentinel `Ord(Indep)`=8 meaning "no
  mine," `GALAXY.PAS`'s own `NoSRMField` constant) and `MineScout` reconstruct `Galaxy`'s sparse
  nebula/minefield/mine-scouted-by dictionaries, the only place this port's model has to put them.
  Confirmed the on-disk row loop is X-outer/Y-inner (Pascal's own loop variable is misleadingly
  named `y` but indexes `Sector[XY.x]`, per `SetMineScout`'s usage) by reading `GALAXY.PAS`
  directly, not assumed from the format doc's looser "Row 0.. Row SizeOfGalaxy" phrasing.
  `EmpiresToMove`/`TimePerTurn`/`AutoSave`/`AsyncTurns`/`PauseActive`/`ReEnterGame` read and
  discarded, `ScenaFilename` kept via the new `Game.ScenarioFilename`. Tested against real bytes
  from `INTRO_1.SAV`, ground truth cross-checked against `scripts/savtool.py`'s own JSON parse
  (year 4021, player ordinal 0, `INTRO.SCN`, `SizeOfGalaxy=21`, 90 nebula cells, zero minefields).
- **7c, Planets/Starbases/Fleets/Stargates/Constructions `LoadGame`** (`SavGameLoader.cs`).
  Fleet's `FuelHigh`/`Fuel` split → single `double` (`FuelHigh*32767+Fuel`); `CommandRecord` order
  queues read and discarded (no in-memory representation exists — Phase 8's job once a human turn
  handler needs one), confirmed not to desync the byte cursor against `FLEET_ORDERS.SAV` (a real
  4-order queue). `STyp`/`GTyp`/`CTyp` all confirmed (via `docs/SAV_FILE_FORMAT.md`'s own worked
  examples, e.g. `STARGATE_DONE.SAV`'s `GTyp=24`) to be the full `TechnologyTypes` ordinal, not a
  0-based subrange index — each decodes as a constant offset (`StarbaseKind`-20, `StargateKind`
  -24, `ConstructionType`-19) since `TYPES.PAS:83-85` declares all three as genuine Pascal
  subranges, which preserve the base enum's ordinals rather than renumbering. **Found and fixed a
  real semantic gap while implementing this, not caught by the format doc alone**: `Fleet.
  Destination`/`Starbase.Destination` being `null` means "not moving" in this port's own model
  (`FleetMovementHandler`), but real Pascal's `Dest` field is never optional — a non-moving
  fleet/starbase's `Dest` is just its own current `XY` on disk (confirmed via `INTRO_2.SAV`: every
  fleet with `Dest==XY` has `Status=Ready`, every fleet with `Dest≠XY` has `Status=InTransit`, no
  exceptions across all 9). Translated as `Destination = (Dest==XY) ? null : Dest` for both types
  — *not* the same as `Stargate.LinkedTo`'s actual `(0,0)`-`Limbo` sentinel, confirmed a genuinely
  different convention by reading `CreateStarbase`/`CreateStargate` directly (the former
  initializes `Dest:=XY`, the latter `Dest:=Limbo`). Tested against real bytes from `INTRO_2.SAV`
  (9 fleets, including one already-arrived), `GAUNTLET_1.SAV` (10 starbases), `FLEET_ORDERS.SAV`
  (13 fleets, one with a real discarded order queue), `STARGATE_DONE.SAV`, `Confront_2.SAV`.
- **7d, Messages/Empire Data/News `LoadGame`.** Messages read and discarded — no in-memory
  message concept exists anywhere in this port (a human-UI feature, same `ATTCOMM`/`FLTCOMM`
  -adjacent Phase 8 cluster). Empire Data fills the same 8 placeholder slots earlier sections
  already resolved references against; only `InUse` slots join `Game.Empires`.
  **`DefeatedBy` decode confirms a real ambiguity in the on-disk sentinel, resolved correctly**:
  `ConquerEmpire`'s human-defeat trick (`ATTACK.PAS:1120-1131`) writes `Capital.ObjTyp:=Void,
  Index:=Ord(Player)` — the conqueror's raw 0-based ordinal, which can legitimately be `0`
  (Empire1). Gating on `Index>0` (as `IDNumber`'s usual "empty" convention would suggest) would
  silently misread a real Empire1-conquered-you case as "no capital data" — gated on `ObjTyp==Void`
  alone instead, since an `InUse` empire's `Capital` is never legitimately `EmptyQuadrant`
  otherwise. `Technology`/`EmpireGainedTechnology`'s `TechGrant` share one 27-entry ordinal table
  (`TechCatalog.Grant` already existed; this is its `.SAV`-side ordinal mapping, transcribed
  separately from `ScenarioLoader`'s own — same Pascal declaration order, different parsing
  boundary, not shared code). `NameRecord`'s `Location` union (raw XY or a resolved object
  reference — same shape as `CommandRecord`'s `DestCOM` variant) resolves to a plain `Coordinate`
  for `LocationBookmark`. News's `Loc1` decodes the same way, per-item not per-headline — the only
  real per-headline exceptions are `OtherEmpire`/`TechGrant`, confirmed (by reading the exact real
  `AddNews` call site, not guessed) for exactly the headlines this phase's ground truth exercises:
  `FleetDestroyedByLams`/`FleetDamagedByLams`/`ProbeDestroyedByYou` (`Parm1`=`Ord(Player)`) and
  `EmpireGainedTechnology` (`Parm1`=`Ord(NewTech)`, the granted item's full ordinal). Every other
  headline (including `MessageReceived`, which has no real call site anywhere in this port) keeps
  `Parm1-3` as plain ints, matching `NewsItem`'s own shape. Tested against `INTRO_1.SAV` (5 active
  empire slots, 3 correctly-excluded inactive ones), `IMPERIUM_1.SAV` (all 8 slots active, a real
  `Technology` bitset decode), `FLEET_ORDERS.SAV` and `Confront_2.SAV` (real `TechGrantIdentity`
  and `OtherEmpire` decodes, cross-checked against `scripts/savtool.py`'s JSON parse of the same
  bytes). `DefeatedBy`'s decode branch has no exercising reference save (none of the 13 capture a
  defeated human empire) — implemented and reasoned through directly from `ConquerEmpire`, not
  covered by a ground-truth test; flagged for whoever next captures or hand-builds one.
- **7e, NPE Data `LoadGame` + `KingdomTurnHandler` state seam.** New `internal` constructor on
  `KingdomTurnHandler` builds a handler straight from a saved persona/`State`/`FleetStates` (no
  fresh persona roll, no `SetEmpireDefenses` re-seed) plus matching internal read-back accessors
  (`Persona`/`State`/`FleetStates`/`DefaultPolicy`) for 7f's JSON serializer to reuse — kept
  `internal`, not exposed to tests, matching this port's standing precedent of testing Kingdom's
  private state indirectly through public entry points (here, actually running `PlayTurn` against
  loaded state rather than reaching into fields). `FleetDataRecord`'s field order confirmed
  directly against `NPETYPES.PAS` (`Mission`/`TargetID`/`HomeBaseID`/`Midway`(dead)/`Waiting`/
  `BlockX`/`BlockY`(Pirate-only)/`Index`), same for `StateDeptRecord`/`NPECharacterRecord` — all
  three enums (`MissionTypes`, `PolicyTypes`, and `NPEmpireTypes` itself) confirmed in exact
  Pascal ordinal order, direct cast. Opaque per-empire blob storage
  (`Game.UnimplementedNpeBlobs`) for Pirate/Berserker/Guardian/Trader/unrecognized (no
  `ITurnHandler` exists for any of them yet, but real scenarios mix them with Kingdom empires —
  their blobs must still round-trip, not be silently dropped). **Completes `.SAV` import end to
  end** — every reference save loads cleanly start to finish, no truncated/partial reads.
  Tested against `INTRO_1.SAV` (4 real `KingdomTurnHandler`s constructed from saved persona/state,
  one of them actually run through a live `PlayTurn` from that loaded state — the strongest
  available check on `State`'s 9-entry `Empire`+`Indep` completeness and `FleetStates`
  well-formedness, since a gap there throws during real NPE decision logic, not silently),
  `GAUNTLET_1.SAV` (Pirate blob alongside Kingdom2 empires), `Confront_1.SAV` (Guardian + Berserker
  blobs, correct byte lengths).
- **7f, native JSON save format.** `Core/SaveFormat/GameJson.cs`: `Serialize(Game) → string` /
  `Deserialize(string) → Game`. The plan called for `ReferenceHandler.Preserve` on the real
  reference cycles (`Owner`, `Empire`'s `EntityVisibility` sets, `NewsItem`) — dropped after hitting
  four separate hard `System.Text.Json` incompatibilities, each confirmed by a real failing test,
  not by reading docs: `JsonObjectCreationHandling.Populate` flatly rejects any `ReferenceHandler`
  (global or per-property, same exception either way); a converter that delegates via a nested
  `JsonSerializer.Serialize(writer, ...)` call starts a *fresh* `WriteStack` that doesn't share the
  outer reference-tracking session, so a real cycle blows through the 64-level depth guard instead
  of resolving via `$ref`; `Preserve`'s metadata wrapping is unsupported on constructor-bound
  parameters, which both `Game`'s and `NewsItem`'s constructors are. All four trace to the same
  root cause — `ReferenceHandler` doesn't compose with the rest of STJ's object model — and fixing
  it by reshaping `Game`'s constructor or `NewsItem`'s positional shape would've inverted the
  dependency this phase is supposed to respect. Replaced with the same fix `.SAV`'s own format
  already uses: every entity reference is a stable per-kind integer id, resolved through a
  write-side `EntityIndex`/read-side `EntityLookup` pair (mirroring `.SAV`'s `IDNumber`). `Empire`
  is the one type that never goes through automatic reflection at all — it's referenced before its
  own data is known, so `WriteEmpires`/`FillEmpireFields` hand-write every field, reusing
  `SavGameLoader`'s own placeholder-Empire-slots trick for the forward reference.
  **Real domain finding, not a bug**: `CombatOutcome.DestroyEmpire` drops a defeated empire from
  `Game.Empires` but never clears references to it elsewhere (that method's own doc comment:
  `CleanUpNPE isn't ported`) — so a Kingdom handler's own `State` diplomacy dictionary can hold a
  live `Empire` no longer listed anywhere. `EntityIndex` auto-registers such "orphan" empires the
  first time anything asks for their id, and a `realEmpireCount` field tells `Deserialize` how many
  of the reconstructed placeholder `Empire`s to actually add to `Game.Empires` — the rest stay
  alive, referenced only through the lookup, matching original state exactly. Verified with
  `DeepGraphComparer` (new test-only exhaustive reflection-walk structural comparer, matching
  entities across the two independently-built graphs by list position rather than reference
  equality) round-tripping every reference `.SAV` plus one freshly-built `ScenarioLoader` game.
  Confirmed the orphan path is actually exercised (`INTRO_1.SAV` has 3) and, after extending the
  comparer to also walk `KingdomTurnHandler`'s `internal` `Persona`/`State`/`FleetStates` (it's
  invisible to public-only reflection — the first pass over this silently checked nothing), that
  Kingdom's saved diplomacy/mission state genuinely survives the round trip, not just its presence.
- **7g, minimal `.SAV` write-back + real-Pascal acceptance check.** `Core/SaveFormat/
  SavGameWriter.cs`: `WriteGame(Game) → byte[]`, the exact section-by-section mirror of
  `SavGameLoader`'s own `Load*` methods. `EmpireSlotIndex` compacts `Game.Empires` down to as many
  of the 8 real on-disk slots as it needs, then lazily discovers "orphan" empires (reachable only
  via a Kingdom's own `State` dictionary, `Empire.DefeatedBy`, a `NewsItem`'s `OtherEmpire`/
  `Defender`, or — a real gap this writer's own first real-Pascal run caught, not a hypothetical
  the design anticipated — a minefield's owner/scouted-by set, which `CombatOutcome.DestroyEmpire`
  never clears) and gives each one a free slot so a Kingdom's fixed 9-entry `State` array still
  round-trips correctly. News is written faithfully (cheap, and puts 7d's decode table under real
  test); Messages and fleet order queues are not (no in-memory representation of either exists in
  this port at all).

  **Real, pre-existing landmine found and fixed, not introduced by this commit**: the very first
  real-Pascal run of ANY `.SAV`-shaped record through `fpc` (`LoadGame` itself was never previously
  exercised against a real file by this harness — 7a-7f's own tests only ever checked the C# port's
  understanding against itself) crashed with a runtime 216, then desynced with IOResult 100 once
  that first crash was fixed. Root cause: `fpc`'s default record alignment doesn't match real Turbo
  Pascal's lack of inter-field padding — see `PASCAL_ARCHITECTURE_NOTES.md`. Fixed with
  `{$PACKRECORDS 1}` on the seven pristine units declaring an on-disk record type reachable from
  `LoadGame`. `build-all-units.ps1`'s full 67-unit smoke test and the entire existing `dotnet test`
  suite both still pass unchanged after the patch.

  One side effect, fully root-caused rather than left as an unexplained golden-file change:
  `scenario.golden`'s `Awaken` case shows a different `sumstarbaseeff` (89 → 143) after the packing
  fix. Not an RNG-stream-position shift (ruled out by a direct A/B trace); the real cause is a
  genuine, pre-existing bug in `AWAKEN.SCN` itself — it creates 212 planets against `TYPES.PAS`'s
  hardcoded ceiling of 200, and the overrun silently corrupts two starbase records since Turbo
  Pascal ships with array-bounds checking off. Full trace in `PASCAL_ARCHITECTURE_NOTES.md`; the
  short version is that the packing fix changed the overrun's exact byte alignment, which changed
  which corrupted bytes landed where, surfacing a scenario-file bug that predates this port and
  would corrupt the same two starbases in the genuine DOS 1.31 binary too.
  `ScenarioLoaderGoldenTests.MatchesGoldenFile` stays green for Awaken regardless — every field the
  overrun actually touches was already excluded from exact-match for unrelated reasons.

  Verification is **differential, not golden-file**: a new `reference/verify/runload.pas` driver
  (deliberately its own driver, not a new `runworld.pas` domain, since `LOADSAVE.PAS`'s own unit
  chain isn't in `runworld`'s `USES` and folding it in would re-run all 20 existing domains' unit
  -initialization sections needlessly) calls the real, unmodified `LoadGame` and emits a structural
  checksum (per-empire planet/starbase/fleet/construction-site counts, tech/revolution/founding/
  news-count, sorted by empire name rather than on-disk slot ordinal since this writer's own slot
  compaction doesn't preserve a real save's ordinal gaps; also fleet count plus aggregate
  `sumfleetx`/`sumfleety`/`sumdestx`/`sumdesty`, and a galaxy-wide `minedcellcount` — added
  specifically to keep `WriteFleets`' null-`Destination`-becomes-`XY` convention and `WriteSector`'s
  mine-owner-nibble sentinel actually covered by this test, not just reasoned about at design time;
  both mutation-tested by deliberately reintroducing each bug and confirming the acceptance test
  fails on exactly that field before reverting) — run once against each reference `.SAV`
  unmodified, once against `SavGameWriter`'s rewrite of what `SavGameLoader` loaded from it, and
  diffed directly. Same real Pascal code both sides, no `Rnd()` anywhere in `LoadGame`, so an exact
  match is safe in a way it wasn't for the `scenario` domain's own golden file. Passes for all 13
  reference saves plus a smoke check (`error=0` only, no original file to diff against) for one
  freshly built `ScenarioLoader` game.
- **7h, wrap-up.** Re-read every gap 7a-7g flagged along the way and confirmed each is accurately
  tracked, nothing silently closed or forgotten. `.SAV` write's scope (`SavGameWriter`, test-only,
  no fidelity effort beyond "real Pascal accepts it") stays as stated in this section's own intro —
  nothing calls it a maintained feature anywhere. Order queues, messages, UI/session Environment
  fields, and the untested `DefeatedBy` decode branch are tracked in `OPEN_GAPS.md`.

## 8. Human interactive turn handler + Terminal.Gui UI

The last `ITurnHandler` implementation, plus the actual windowed interface (map view, fleet orders,
construction, etc.), built on Terminal.Gui v2 per `TUI_LIBRARY_RECOMMENDATION.md`. `TUI_SURFACES_
MAPPING.md` surveys every player-facing window/menu/dialog/editor in the real Pascal and maps each
to a Terminal.Gui primitive; this section narrates the build against that map, in the pre-map
screens' own real order (`ANACREON.PAS`'s `Introduction`, then `PROLOG.PAS`'s `MainTitle`/
`SetUpPlayer`) before the map itself.

- **8a, project scaffold + galaxy map viewport.** New `Reconstructed4021.Tui` project.
  `GalaxyView` renders the scrollable galaxy map as a custom `View`: glyphs and colors come from
  `MAPWIND.PAS`'s `CellRecord` and `DATACNST.PAS`'s `TypeStr`/`BaseTypeData`/`GateTypeData`, not
  invented, and a cursor overlay matches `DrawMapCursor`'s corner-bracket style. Fleet indicators
  are the player-fleet/enemy-fleet columns flanking each world glyph, per the original's 3-column
  sector layout.
- **8b, navigation shell.** `GameShell` replaces the bare `GalaxyView` window with a `MenuBar`
  (System/Game/Empire/Worlds/Fleet/Build/Ministry of War, every leaf item stubbed to a
  `MessageBox`) and a `StatusBar` (F1/F3/F5/F7/F8/F9, also stubbed) around the permanent
  `GalaxyView` base, per the shell design in `TUI_SURFACES_MAPPING.md`'s "Deliberate deviation"
  section. Colors come from `COLORS.INC`'s `ColorScrColor` (`SYSMenuBar`/`SYSMenu`/`SYSHelpLine`);
  the coordinate readout matches `PRIMINTR.PAS`'s `GetCoordName` (cursor position relative to the
  player's capital). Esc toggles focus between the map and the menu bar rather than quitting
  outright; Quit lives behind Game > Quit with its own confirm.
- **8c, startup logo, title/orbit animation, and turn-start greeting.** Three pre-map screens.
  `TmaLogoWindow` decodes `TMA.PAS`'s `TMALogo` splash byte-accurately from its CP437 source, with
  a growing-suffix reveal matching the real prepend loop rather than a naive wipe.
  `AnacreonTitleWindow` covers `PROLOG.PAS`'s `MainTitle`/`ZoomOutSFX` plus the ambient orbiting
  stars (`InitStarArray`/`UpdateStarArray`); deliberately not a literal port of the animation
  mechanics — replaces the original's 4-frame `BITPIC.INC` bitmap zoom and separate menu-loop-only
  orbit with one continuous, formula-driven system, the same 12 stars orbiting from frame one,
  radius easing out from 0 as the fly-in, reprojected as an obliquely-viewed vertical ring so it
  passes convincingly behind/in front of the text. The white highlight band sweep is unchanged
  from source. `TurnStartGreetingWindow` covers `PROLOG.PAS`'s `DisplayIntroScreen`, one of the 3
  real greeting lines plus `PRIMINTR.PAS`'s `MyLord` title, independently randomized.
- **8d, mouse-draggable map, interactive main menu, quit hotkeys.** `GalaxyView` gained
  left-button drag panning. `AnacreonTitleWindow` became the actual main menu rather than a timed
  splash: New Game/Load Game/Options/Quit as framed, arrow-navigable, hotkeyable buttons above the
  still-running orbit animation, plus the version/copyright lines from `MainTitle`. Quit's
  confirmation dialog takes Y/N as hotkeys on its Yes/No buttons.
- **8e, New Game flow.** `ScenarioPickerWindow` scans `reference/scenarios/dos_131/*.SCN` and
  lists every title (`ScenarioLoader.ReadHeader`), replacing the previous eager fixed-scenario load
  at startup. `IntroTextWindow` shows each scenario's `BEGINTEXT`/`ENDTEXT` narrative;
  `PlayerCountWindow`/`PlayerSetupWindow` collect player count (skipped when a scenario's
  Min/MaxPlayers are equal) and per-player name/gender — all on `COLORS.INC`'s `SYSDispWind` blue
  backdrop, matching `NEWGAME.PAS`'s own full-screen `OpenWindow` around this whole flow, with
  gender collected in a small `CommWind`-colored popup box matching `InputEmpireName`'s own
  separate window for that one prompt. `ScenarioLoader.ReadScenarioFile` detects CP437 vs. UTF-8
  per file (strict UTF-8 first, falls back to CP437 on failure) since `AFTERMAT.SCN`'s box-drawing
  banner is CP437 but most scenarios are plain ASCII. `GameShell`'s Quit now returns to the main
  menu instead of exiting the app outright, matching `PLAYTURN.PAS`'s own `XXXCom`
  (`ExitGame:=True` unwinds back to `Prologue`, not a full process exit); a separate "Exit to OS"
  item covers the full exit as a TUI-only convenience.
- **8f, intro pagination and New Game screen polish.** `ScenarioLoader.ReadIntroPages` splits
  strictly on real `NEWPAGE` markers instead of a fixed-line-count chunker — falsified against
  real Pascal (`EASTWEST.SCN`'s genuine 22-line page renders as one screen, not two) and against
  `NEWGAME.PAS`'s own `ReadPage` loop, which has no line-count check at all. Every New Game screen
  now renders inside a shared `NewGameWindow` base (a centered 80x24 box over a black backdrop with
  a twinkling starfield, matching `NEWGAME.PAS`'s own fixed-size `OpenWindow`) instead of a
  full-terminal blue fill. Gender selection also responds directly to M/F keypresses.

TUI keystroke/redraw lag on Windows turned out to be Windows Terminal's own renderer
([tui-cs/Terminal.Gui#4588](https://github.com/tui-cs/Terminal.Gui/issues/4588), still open
upstream), not this project's draw code — ruled out via `Application.Iteration` timing (mean
14ms/iteration, no busy-loop) before the actual fix (switching Windows Terminal's renderer to
Direct2D) surfaced; see the README's Known Issues section.

- **8g, a real playable turn loop.** First vertical slice all the way through: deploy a fleet, send
  it to an enemy, attack once it arrives, against the already-fully-ported Kingdom NPE. Two real
  gaps found while scoping this, fixed rather than routed around: `ScenarioLoader.
  RunCreatePlayerEmpire` never registered anything in `Game.TurnHandlers` for a human empire (new
  `Turns/HumanTurnHandler.cs`, `PlayTurn` a deliberate no-op — a human's actions already happened
  synchronously through the UI before End Turn was pressed, matching `PLAYTURN.PAS`'s own
  interactive command loop running entirely before `UpdateTurn`); and `GameJson.WriteTurnHandlers`
  threw on anything but a `KingdomTurnHandler` once a human had an entry at all — fixed to skip a
  `HumanTurnHandler` on write (nothing to persist) and reconstruct a fresh one on read for every
  `NpeType is null` empire, on both `GameJson` and (found empirically, via a broken round-trip test
  the first fix's own asymmetry caused) `SavGameLoader`.
  - **`Program.cs` now owns a real per-empire session loop**, not just a single `GameShell` run:
    `TurnEngine.AdvanceOneTurn` plays exactly one empire's turn; the loop decides, for whichever
    empire is current, whether to show a `TurnStartGreetingWindow` + interactive `GameShell` session
    (a human, `Active`) or just auto-play it with no UI (an NPE, or a human `TurnEngine` is quietly
    finishing off via `PendingElimination`/`Eliminated`) — matching `ANACREON.PAS`'s own main loop,
    which cycles every empire's slot the same way regardless of who owns it. `GameShell` itself
    shrank to *one human's one turn*: its constructor now takes the human `Empire` explicitly
    instead of hardcoding `game.Empires[0]`, and `Game > Next Turn` calls `TurnEngine.
    AdvanceOneTurn` exactly once and hands control back to `Program.cs`'s loop via a new
    `ExitChoice.EndTurn`, rather than looping across empires itself. This is already
    `Status`/`ITurnHandler.IsHuman`-driven per empire, not hardcoded to one `Empire` reference, so a
    second human would already get its own greeting+`GameShell` cycle when its slot comes up — real
    hotseat protection (password prompt, Capital Fallen Report/Empire Status Report) is still
    missing, tracked in `docs/OPEN_GAPS.md`, since nothing exercises it yet.
  - **`assets/saves/Border Skirmish.json`** — new sibling to `reference/saves/` (real captured
    `.SAV` fixtures) for port-authored content, matching the convention the unmerged
    `kdl-scenario-format` branch already started for scenarios. Built directly against the same Core
    APIs `ScenarioLoader` itself calls (`EmpireFactory.CreateEmpire`, `GalaxySetup.CreateWorld`,
    `KingdomTurnHandler`'s own `InitializeNPE`-equivalent constructor) rather than an authored
    `.SCN` scenario — the user's own explicit redirect, since a scenario file would mean routing
    through the whole New Game flow just to reach a testable state. Three planets, all pairwise
    within 5 sectors: a heavily-armed human capital (3,000 ships, exactly 10x the NPE's combined
    total — sized with room for several real engagements, not just one), and two lightly-defended
    Kingdom2 (aggressive) worlds with no LAMs at either. The
    generating test (`GameJsonFixtureTests.cs`) doubles as the fixture's own regression check —
    byte-identical against the committed file, plus a full `DeepGraphComparer` round trip — same
    bootstrap-then-pin pattern this repo's other golden-file tests already use. New `--load <path>`
    flag on the TUI (`Program.cs`) deserializes it and drops straight into the session loop above,
    skipping the whole pre-game flow. **Kingdom's starting population/efficiency retuned down after
    a real playtest, not guessed**: a first pass (1500 population/70% efficiency capital, 500/50%
    outpost — proportional to the human capital's own) let Kingdom's real, aggressive `WarCabinet`/
    `DefendEmpire` AI grow its capital from 10 GDMs to 494 over the ~4 turns the human fleet's
    transit took, erasing the 10x ship-count edge entirely by the time of contact — confirmed via a
    hand-run diagnostic, not assumed, and not a bug (the NPE's defensive buildup is real, ported
    behavior reacting correctly to an incoming threat). Lowered to 150/20% (capital) and 100/15%
    (outpost), keeping 4-turn growth modest (capital tops out around 161 fighters/54 jumpships/14
    GDMs) so the human's edge actually holds by the time the fleet arrives — confirmed end-to-end in
    a real playtest afterward: destroy the escort fleet Kingdom deploys in response, then the capital
    itself falls and converts to an independent world, eliminating the Kingdom empire.
  - **`GalaxyView` gained `Refresh()`/`CursorLocation`.** Its location-cache index was built once in
    the constructor on the (until now correct) assumption that nothing changes it mid-session — a
    real turn engine invalidates that, so `Refresh()` (called after End Turn, Deploy, and Attack)
    rebuilds it. `CursorLocation` (named distinctly from `View.Cursor`, Terminal.Gui's own unrelated
    text-cursor concept it would otherwise hide) exposes the one real selection mechanism the map
    has, for the new commands below to read.
  - **`Worlds > Close Up`, and Enter on the map** (`CloseUpWindow.cs`) — `CLSCOMM.PAS: CloseUpCom`.
    Reproduces its real 80x21 fixed-size display window (`DISPLAY.PAS`'s own shared
    `OpenWindow(1,4,80,21,ThinBRD,...)`, the panel every command including the original galaxy map
    itself drew into) centered over the map instead of pinned under the menu bar, since this port's
    map is the permanent shell rather than one of several panels sharing that fixed slot; field
    layout (every label's row/column) is transcribed directly from `DisplayBasicInfo`/
    `DisplayCargoInfo`/`DisplayMilitaryInfo` (a world) and `DisplayFleetInfo`/
    `DisplayFleetComplement` (a fleet) — confirmed with the user after an initial single-`MessageBox`
    pass didn't resemble the real screen at all. Enter (or the menu item) resolves every object at
    the cursor: 0 is a no-op, 1 opens `CloseUpWindow` directly, 2+ opens a small `ListView` picker
    overlay first (`TUI_SURFACES_MAPPING.md`'s "Sector Selected Popup"). Both are added/removed
    directly as children of the running `GameShell`, not a nested `Application.Run` — matching this
    project's established habit (`PlayerSetupWindow`'s gender box) of toggling child-view visibility
    for in-session overlays rather than nesting Dialogs. Real Pascal's `Known`/`Scouted` redaction
    isn't reproduced, matching `GalaxyView`'s own already-accepted no-fog-of-war simplification.
  - **`Fleet > Deploy`** (`FLTCOMM.PAS: LaunchFleetCommand`) — deploys the launch world's entire
    current `Ships` (no cargo, no fleet name), destination picked by reusing the map cursor (move
    then Enter, Esc cancels) via a small `GameShell`-level "pending pick" state machine. The
    Resource Distribution Editor and a name prompt are real UI `TUI_SURFACES_MAPPING.md`'s own
    "Suggested build order" defers past this branch's actual job (proving the combat loop end to
    end) — deliberately not built here. **Real bug found in the first pass, not a Core issue**:
    passing the launch world's own live `Ships` object straight through as `FleetLifecycle.
    DeployFleet`'s `ships` argument deployed an empty fleet every time — `ChangeCompositionOfFleet`
    (which `DeployFleet` calls internally) overwrites the launch world's own `Ships` in place
    *before* copying the "new fleet" composition onto the fleet, so the aliased object had already
    been zeroed by the time it was read for the fleet's own side. Every real Core caller
    (`NpeToolkit`'s `Deploy*Fleet` procedures) avoids this by building the composition off
    `GetFleetComposition`, a fresh, independent `ShipCounts` — never a world's own live `Ships` —
    fixed the same way here (a snapshot copy, not the live reference).
  - **`Ministry of War > Attack`** (`ATTCOMM.PAS: GetTarget`/`AttackCommand`) — requires the
    attacking fleet and an enemy target already in the same sector (no range/adjacency rule
    re-derived from source), auto-resolved through `CombatResolution.NPEAttack` — the same entry
    point Kingdom's own AI calls — with `AttackIntentionType.Conquer` and `CombatEngine`'s own
    default distribution/grouping standing in for the deferred Fleet Group Configuration/Tactical
    Battle Display screens. No target picker for 2+ enemy fleets in one sector (whichever is found
    first wins) — nothing this branch's own fixture can produce ever hits that case. **Target
    selection order transcribed from `GetTarget`'s own nested `CreateMenu` (`ATTCOMM.PAS:663-715`),
    not guessed**: every enemy fleet in the sector is offered first, and the world itself (Planet/
    Starbase) is only ever offered when there are none — a world defended by any fleet can't be
    attacked directly, confirmed with the user after a first pass got this backwards (checked the
    world before any defending fleet) and produced a bizarre result investigated below.
  - **Real bug found and fixed in `CombatEngine.DefaultDistribution`, not a Core issue specific to
    this branch's own new code.** A hand-built playtest (deploy 3,000 ships, wait several turns,
    attack) surfaced it directly: the attacking fleet was reported destroyed while inflicting no
    visible damage, against a Kingdom capital whose own war economy had ballooned over the turns the
    slow-moving attack took to arrive. Investigating (with a throwaway diagnostic test, not guessed)
    turned up two separate real causes layered together:
    1. **The world's own defending fleet should have been the forced target, not the world itself**
       — see the `Attack` targeting-order fix just above. Attacking the wrong thing meant the
       fight was against a world whose military had grown far past its starting stats, not the
       small, genuinely beatable defense fleet actually guarding it.
    2. **`DefaultDistribution` mutated the live `Fleet.Ships`/`Fleet.Cargo` objects directly**
       (`fleet.Ships[ship]=0` inside `DefaultGroup`) instead of a local snapshot. Real Pascal's own
       `DefaultDistribution` (`ATTACK.PAS:1344-1367`) declares `Sh`/`Cr` as local `VAR` arrays,
       populated via `GetShips`/`GetCargo` (Pascal array assignment copies by value) and never
       `PutShips`/`PutCargo`'d back — the fleet's own persistent Ships/Cargo are never touched by
       this step in real Pascal. With the bug, every attacking fleet's live Ships silently zeroed
       out for the rest of the engagement, so `CombatOutcome.RestoreCombatant`'s later `ships[t] -=
       casualties[t]` always computed `0 - casualties` instead of the real survivor count — an
       attacking fleet ended up with zero ships left after *any* attack, win or lose, regardless of
       actual losses. Fixed by cloning `Ships`/`Cargo` before handing them to `DefaultGroup`. No
       existing test caught this — `CombatEngineTests.MatchesGoldenFile` only ever asserts on the
       returned `GroupRecord`s/tallies, never on the fleet's own post-call `Ships` — new
       `DefaultDistribution_DoesNotMutateTheFleetsOwnLiveShipsOrCargo` is that missing assertion.
       Full suite (`combat.golden`/`npeattack.golden` included) stayed green before and after —
       this bug was invisible to every existing assertion, isolated purely to a real playing
       session actually checking a fleet's own ships after it fought.
- **8h, Deploy Fleet done properly, plus Transfer/Abort-Join/Refuel.** 8g's Deploy Fleet was
  explicitly an MVP stand-in (dump a world's entire `Ships`, no cargo, no name prompt) to prove the
  combat loop end to end; this entry replaces it with the real thing and reuses the resulting widget
  for the rest of the Fleet menu.
  - **Sector picker rebuilt to match source.** `GameShell.ShowSectorPicker` now mirrors
    `DisplayMenu`/`GetIDMenuChoice` (`DISPLAY.PAS:51-74`, `MENU.PAS:116-156`): fixed 45x7 size,
    `SYSWBorder` border against plain `Col`-attribute (light gray on black) content, `SYSDispSelect`
    highlight on the focused row, no in-window hint text (real Pascal puts that on the shared help
    line instead, not reproduced here either).
  - **Empire Status Report** (`EmpireStatusWindow.cs`, `Core.EmpireStatusReport`) — `PROLOG.PAS:
    EmpireStatus`, shown right after the turn-start greeting in `Program.cs`'s per-empire loop.
    Totals worlds/population/averaged efficiency and industry across a player's Planets + Starbases
    + Fleets, plus technologies mastered beyond `EmpireFactory`'s own automatic
    `TechDev[Pred(Tech)]` baseline (`TechCatalog.UnlockedBeyond`) and a per-ship-type military tally.
    Word-wraps its tech list at column 77, matching `DisplayMenu`'s own wrap rule; a plain `Label`
    rather than `TextView`, which this Terminal.Gui version has deprecated in favor of a package
    this project doesn't reference. `AnnualTickHandler.TotalProd` widened from `private` to
    `internal` so the report reuses the real production formula instead of duplicating it.
  - **The real Resource Distribution Editor** (`ResourceDistributionEditor.cs`, transfer math in
    `Core.Entities.ResourceDistribution`) — `FLTCOMM.PAS: InputNewDistribution`, the interactive
    ship/cargo split grid `TUI_SURFACES_MAPPING.md`'s own "Suggested build order" had deferred to
    last. Built for Deploy specifically, but kept generic from the start (`ResourceDistribution`'s
    transfer math — `TryTransfer`/`FillFleet`/`EmptyFleet`, one column at a time between a fleet and
    a "ground": a world or another fleet) rather than Deploy-specific, per the user's own framing
    ("deploying fleets is the first use case, but we'll also need it for fleet transfers") — which
    paid off directly in the Transfer/Abort-Join work below. `ResourceDistribution.MaxResources`
    names `TYPES.PAS:40`'s own constant rather than a repeated literal `9999`, per an explicit
    mid-task request not to hardcode it.
  - **`Fleet > Deploy`** now runs the real `LaunchFleetCommand` sequence: pick the source world via
    the map cursor, name the fleet (a small `TextField` popup, `FleetName[1]:=UpCase` transcribed),
    choose ships/cargo through the new editor, then pick the destination the same way it already
    did. `GameShell.AddModal` gained a `dismissOnOutsideClick` opt-out for this: the editor and name
    prompt have no click-to-cancel in real Pascal (keyboard-only), and a stray click outside them
    used to silently discard whatever the player had already entered with no feedback at all.
  - **Mouse and quick-step controls for the editor**, all called out as having no real Pascal
    equivalent (that widget is keyboard-only) rather than silently added: clicking a column's cell
    selects it, the mouse wheel over a cell steps that column by 1 (up = ground→fleet, matching the
    existing `+###` direction; down = fleet→ground), and Shift/Ctrl+Up/Down step by 100/1000. These
    step helpers clamp to whatever's actually available on each side but don't check fleet cargo
    capacity — matching `GetChange`'s own real behavior (capacity is only enforced at Esc/X, via
    `TryFinish`), not `FillFleet`/`EmptyFleet`'s. **Real focus-stealing bug found and fixed along the
    way**: Terminal.Gui's `MenuBar` tracks mouse hover to keep its own highlight in sync even without
    a click, which was silently stealing focus off the editor the moment the pointer crossed the
    menu row — no click involved at all. `AddModal` now disables the bar for as long as any popup is
    open, tracked with a depth counter rather than a bare bool, since Deploy Fleet chains two popups
    back to back (name prompt, then the editor) and a naive "re-enable on dismiss" would misfire on
    the inner one closing while the outer was still up.
  - **`Fleet > Transfer`/`Abort/Join`/`Refuel`** (`FLTCOMM.PAS: TransferFleetCommand`/
    `AbortFleetCommand`/`RefuelFleetCommand`) — a shared `GameShell.PickGround` (`GetGround`,
    `FLTCOMM.PAS:51-132`) resolves the "other side": every fleet at the source fleet's own location
    (minus itself unless `includeFleet`) plus the world/base there, filtered to the player's own
    when `playerOnly`. Transfer reuses the Resource Distribution Editor exactly like Deploy does,
    just fleet-vs-ground instead of ground-vs-fleet; `FleetLifecycle.ChangeCompositionOfFleet`
    (already real, built for the NPE toolkit) applies the result and may destroy either side, per
    its own doc comment. Abort/Join has no grid at all, matching `AbortFleetCommand` exactly — it
    dumps everything via a new `FleetLifecycle.AbortFleet` public wrapper around the mutation
    `CombatOutcome.AbortFleet`/`DestroyFleet` already did internally for `ChangeCompositionOfFleet`
    and `DestroyEmpire`, gated by two confirmations transcribed from source (non-empire ground, and
    any single ship type's combined total exceeding `MaxResources` — "some will be lost", though
    this port's plain `int` counters never actually overflow on the write itself, unlike real
    Pascal's fixed-size arrays). Refuel is a `TextField` numeric prompt (`GetTrillumToUse`
    transcribed: 0 or blank defaults to the max, out-of-range re-prompts with an error) driving a
    new `FleetLifecycle.MaxTrillumToRefuel` (the "how much could the player ask for" math split out
    from that same procedure) and the already-real `FleetLifecycle.RefuelFleet`.
  - **Real gap found and fixed, not hypothetical**: every Fleet-menu command (plus Attack, already
    in 8g) resolved "the player's own fleet under the cursor" with a bare `FirstOrDefault`, silently
    picking one if two of the player's own fleets ever shared a sector. Real Pascal disambiguates
    this by having the player type the target fleet's own name (`PLAYTURN.PAS`'s
    `GetParameters`/`InterpretObj`), which this port's cursor-driven UI has no equivalent for; fixed
    by reusing `ExamineCursor`'s own "exactly one auto-picks, 2+ opens a picker" rule
    (`GameShell.PickOwnFleetAtCursor`), sharing the same `ShowObjectPicker` the Sector Selected
    Popup and `PickGround` already use — the third real call site, which is what actually justified
    factoring `ShowSectorPicker`'s inline picker code into that shared helper in the first place.
- **8i, real fog-of-war on the map.** The Core-side visibility engine (`VisibilityHandler`,
  `EntityVisibility<T>`, `Game.Known`/`Scouted`) already existed and was already fully tested; this
  entry is TUI wiring plus one real sequencing bug the wiring surfaced.
  - **Turn-sequencing bug found and fixed.** `TurnEngine.AdvanceOneTurn` refreshed a human's
    fog-of-war right before `PlayTurn` — fine for an NPE's atomic turn, wrong for a human, whose
    `GameShell` session runs entirely *between* those two points. `Program.cs`'s loop deferred the
    whole call into `GameShell`'s own End Turn button, so a human's map reflected whatever was left
    over from the end of their *previous* turn, stale by a full round of enemy movement, for the
    entire session. Fixed by restoring `ANACREON.PAS`'s own two-procedure structure
    (`TurnEngine.BeginTurn`/`SetUpTurn` vs. `TurnEngine.EndTurn`/`UpdateTurn`) instead of inventing a
    new split: `Program.cs` now calls `BeginTurn` before showing the greeting/status/`GameShell`, and
    `GameShell.EndTurn()` calls only `TurnEngine.EndTurn`. `AdvanceOneTurn` (still `BeginTurn` then
    `EndTurn` back to back) is unchanged for every AI turn and every existing `TurnEngineTests` case.
    News-erasure timing matters here too: `EraseNews` is `UpdateTurn`'s own line in Pascal, not
    `SetUpTurn`'s, so it stays in `EndTurn` — moving it into `BeginTurn` would wipe a human's news
    right before they ever see it.
  - **`Game.Visible(empire, source)`** (`Game.cs`, beside the already-existing `Known`/`Scouted`) —
    `Known`, or owned outright. Real Pascal seeds `KnownBy:=[NewEmp]` the moment an object is created
    (`INTRFACE.PAS:384-385,419-420,507-508`); this port has no creation hook for that (no
    new-game/construction setup calls an equivalent), so a freshly-owned object can sit un-Known
    behind `VisibilityHandler`'s 50%-roll discovery path — gating on bare `Known` would make a
    player's own starbase vanish from their own map.
  - **`GalaxyView`** (`MAPWIND.PAS: UMSector`/`UMFleets`/`EnemyFleetInSector`) — a world glyph only
    draws once `Game.Visible` says so, otherwise the sector falls through to nebula/grid/blank exactly
    as if nothing were there; the enemy-fleet indicator needs the identical per-fleet check, the
    player-fleet indicator needs none (owning it is trivially visible). `UnkPlanetChar` (the distinct
    "something's here" glyph for an unscouted planet specifically inside a nebula) wasn't reproduced
    here yet — see 8j below.
  - **`GameShell.ObjectsAt`** gated the same way, so Close Up/the sector picker can't surface or open
    anything the map itself would hide. `GameShell.PickGround` (Transfer/Abort-Join/Refuel's own
    picker) needed no equivalent change: its source fleet is always physically at the location being
    queried, and `VisibilityHandler`'s per-fleet `ScoutAdjacent` call always scouts a fleet's own
    sector, so anything sharing that exact cell is already guaranteed visible by construction.
  - **Verified against the actual fixture before writing any rendering code**, not assumed: with
    `assets/saves/Border Skirmish.json` (no starbases), the two NPE worlds sit Chebyshev 4 from the
    human capital — outside adjacency, and outside starbase-roll range since none exists — so the
    human is genuinely flying blind on turn 1 until a fleet closes to adjacency. Confirmed by loading
    the real fixture and calling `VisibilityHandler.RefreshVisibility` directly rather than guessing
    from the scoring rules alone. Faithful to a no-starbase scenario, not a bug — but exactly the kind
    of consequence worth checking before, not after, wiring the renderer to it.
  - Left open at the time, closed in 8j below: `UnkPlanetChar` and `CloseUpWindow`'s own per-field
    `Known`-vs-`Scouted` redaction.
- **8j, `UnkPlanetChar` and Close Up's field-level redaction.** The two gaps 8i's own map/Close Up
  gating deliberately left open.
  - **`GalaxyView`'s `UnkPlanetChar`** (`MAPWIND.PAS:36,1097-1101`, CP437 #112, `p`) — an unscouted
    planet specifically inside a nebula now draws that distinct glyph in `UnscoutedColor`
    (`COLORS.INC`'s `UnscoutedColor: 4`, red on black via the existing `DosColors.Red` correction)
    rather than reading as plain nebula.
  - **`Game.ScoutedOrOwned(empire, source)`** (`Game.cs`, beside `Visible`) — Scouted, or owned
    outright; same ownership-fallback reasoning as `Visible` (real Pascal's `ScoutFleets`
    unconditionally Scouts every one of an empire's own fleets, a guarantee this port's
    `VisibilityHandler` can't offer before it's actually run once for that owner).
  - **`CloseUpWindow`'s real per-field redaction**, transcribed from `CLSCOMM.PAS`'s
    `DisplayBasicInfo`/`DisplayCargoInfo`/`DisplayMilitaryInfo` (worlds) and
    `DisplayFleetInfo`/`DisplayFleetComplement` (fleets) — three genuinely different rules per source,
    not one rule reused: a world's Cls/Tech/Pop/Eff/Amb/Rev block needs `ScoutedOrOwned` or stays
    blank; its amb/che/met/sup/tri cargo line is gated on ownership *alone*, never upgraded by
    Scouted (`DisplayCargoInfo` has no Scouted branch at all); its ships/legions/defenses line falls
    back to `MISC.PAS`'s `YesNo` — a coarse magnitude bucket ("yes-".."yes+", not an exact count), not
    a boolean — when Scouted-but-not-owned. A fleet's header type/owner name needs `ScoutedOrOwned`
    (stricter than a world's, which only needs Known); its Status/Destination/Range/complement each
    have their own real gate transcribed from source, including `DisplayFleetInfo`'s odd
    Destination rule (a non-owned fleet's destination only shows while it's `Ready`, hidden while
    still in transit even if Scouted) and `DisplayFleetComplement`'s `ResI IN [fgt..trn]` guard (only
    ship-type counts get the `YesNo` upgrade; legions/cargo redact to "????" for anyone but the owner,
    same as a world's cargo line). Left open at the time, closed in 8k below:
    `CloseUpCom`'s own `DisplayBackground` racial/artifact panel.
- **8k, `DisplayBackground` (scenario flavor text) for Close Up's `conquer:false` case.** `SCENA.PAS`'s
  `WorldBackgroundIndex`/`TEXT n` blocks, real content in 6 of the 12 shipped `dos_131` scenarios,
  previously discarded whole by `ScenarioLoader`'s own `SkipDescriptions`.
  - **Index parity verified empirically before writing a parser**, not assumed: loaded `ARRONAX.SCN`
    through this port's real `ScenarioLoader`/`PascalRandom` and cross-checked several planet-list
    positions against that file's own explicit `CreateWorld` ownership data — every one matched
    exactly, confirming `Galaxy.Planets[i-1]` really does correspond to Pascal's 1-based `Planet[i]`,
    both structurally (both sides assign indices via one sequential counter across
    `CreateWorld`/`CreateRandomWorlds` in file order) and now empirically. Some of ARRONAX's own
    `WorldBackgroundIndex` row *comments* (`; Player 4 Capital`) don't actually match that row's real
    owner condition — a pre-existing inconsistency in the 1988 file itself, harmless in real play
    since Pascal discards everything after `;` and the condition just silently never matches; not a
    porting bug, and not something worth "fixing."
  - **`BackgroundConditionKind` is an enum (`OwnedBy`/`ConqueredBy`), not a bool.** `SatisfiesConditions`
    dispatches a row's own clauses by their leading character (`CASE Parm[i][1] OF 'E': ... 'A':
    ...`), not a boolean's polarity, and that `CASE` has no `ELSE` — i.e. it isn't declaring a closed
    two-state set on purpose. A `RequiresConquer: bool` first draft conflated "which condition" with
    "what it requires"; the enum matches the real dispatch shape and leaves room for a
    never-yet-seen third clause letter without renegotiating what an existing value means.
  - **`ScenarioLoader.ParseDescriptions`/`ResolveWorldBackground`** replace `SkipDescriptions`: every
    real scenario places `BEGINDESCRIPTION` before the `CreateWorld`/`CreatePlayerEmpire`/
    `CreateNPEmpire` commands that would create the objects/empires a row references, so nothing
    exists yet to resolve against at parse time — rows and condition slot numbers are collected raw,
    then resolved once against the fully-populated `Galaxy`/empire-slot table right before `Load`
    returns (both its `ENDSCENARIO` and early-EOF exit points).
  - **`Game.WorldBackgroundIndex`/`BackgroundTexts`/`FindWorldBackgroundText`** — parsed once at load
    instead of real Pascal's own "reopen and rescan the original `.SCN` file every single Close Up
    call" (`DisplayBackground` literally re-`Assign`s/`Reset`s the file each time); same content,
    cheaper lookup. `[JsonIgnore]`d like `TurnHandlers` (an `ISectorObject` reference, not
    reflection-serializable as-is) — also matches real Pascal, which never persists this in a `.SAV`
    either, always re-deriving it from `ScenaFilename` on demand. A game loaded via `.SAV`/native
    JSON (no equivalent "re-open the original .SCN" step exists) simply has none, same as a
    hand-built fixture never parsed from a real scenario at all — `Border Skirmish.json`'s own
    playtest path doesn't exercise any of this.
  - **`[C:id]`/`[N:id]` placeholder substitution implemented for file-format completeness, confirmed
    unexercised by real content** — grepped every scenario pack in this repo (`dos_131`, `dos_20`,
    `pack_1`); zero uses. Real Pascal's own `ParseLine` only ever substitutes the first bracket pair
    on a line, never loops for a second — reproduced faithfully (a second marker on the same line is
    left untouched) rather than "improved," and covered by a dedicated test since no shipped scenario
    can exercise it. `CoordinateName`'s relative-to-capital formula duplicates `GalaxyView`'s own
    cursor-readout formula rather than sharing it — that one also falls back to the galaxy's center
    with no capital, a UI-only concern this query doesn't need — and `DescribeBackgroundKind`
    duplicates `CloseUpWindow.DescribeKind`/`ObjectListItem`'s naming convention rather than sharing
    it, since Core can't reference the Tui project. Both are deliberate small copies for genuinely
    dead-content code paths, not drift risk.
  - **Checked the (stale, unwired) `kdl-scenario-format` branch before implementing**, at the user's
    prompt — its own `docs/KSCN_FILE_FORMAT.md` design doc independently confirmed the same
    `DisplayBackground`/`SatisfiesConditions` semantics (many-to-many worlds↔text, conditioned on
    owner and on which screen triggered it) derived here straight from `SCENA.PAS`. That branch's own
    `ScenarioParser` is still a stub with no `world-backgrounds` parsing at all, so nothing to
    reconcile — confirms this design rather than duplicating or superseding it.
  - **Excluded, staying with the Attack-command work instead of here**: `DisplayBackground`'s other
    caller, `ATTCOMM.PAS: EnemyConquered`'s post-attack report (`conquer:true`) — `GameShell.Attack()`
    still shows its old generic `MessageBox`. Reproducing `EnemyConquered` properly also means
    `AskToCapture` (deciding whether to capture a defeated fleet's ships), an unrelated mechanic that
    belongs with combat work, not visibility/redaction work (docs/OPEN_GAPS.md).
- **8l, nebula actually affects scouting.** `INTRFACE.PAS`'s three real `GetNebula` call sites in the
  visibility code (`Scout`, `ProbeScout`, `InRangeOfPlanet`) grepped and checked one by one;
  `ProbeScout`'s own dark-nebula exit was already ported, the other two weren't.
  - **`VisibilityHandler.IsInRangeOfPlanet`** now requires the *target* cell to have no nebula at all
    (`GetNebula(ObjXY)=NoNeb`, INTRFACE.PAS:1411) — any of Nebula/DarkNebula/DenseNebula blocks a
    planet's own passive detection range, not just DarkNebula's stronger ring-stopping rule below.
  - **`VisibilityHandler.ScoutAdjacent`** (the `Scout(Emp,XY)` primitive `CombatOutcome.ConquerWorld`
    also calls) now iterates the same fixed clockwise `_probeScoutOffsets` order `ScoutFromProbe`
    already used, instead of an unordered set — order matters once a Dark Nebula cell can stop the
    scan partway through the ring (INTRFACE.PAS:132-133): the cell itself still gets scouted, nothing
    later in the fixed order does.
  - **`POk` news on first contact, found and fixed alongside the nebula work, at the user's own
    prompt** ("why not simply implement this now?") rather than left as a second documented gap the
    way `DisplayBackground`'s `conquer:true` caller was. Not nebula-specific, but small, already using
    infrastructure `ScoutFromProbe` exercises the same way, and genuinely in the code being touched —
    confirmed `POk` is one real shared news constant between `Scout` and `ProbeScout` (`NEWS.PAS:34,126`),
    not a coincidentally similar name; real Pascal's own message text ("Imperial probe has scouted
    *.") stays literally about a probe even when `Scout` fires it, transcribed as-is. New
    `VisibilityHandler.ScoutOneEntity<T>` centralizes the "not yet Scouted → fire news if first
    contact with someone else's object, then mark Scouted" check so it isn't duplicated across the
    four entity-kind loops in `ScoutAdjacent`.
  - Confirmed via full source grep that these three call sites are the *only* nebula-and-visibility
    intersection in real Pascal — no other file references `GetNebula` from scouting/detection logic,
    and no NPE AI file (`NPE00.PAS`/`NPE03.PAS`) reads nebula state at all. Dense-nebula fleet/starbase
    movement blocking was already real (`FleetMovementHandler`), unaffected by this entry.
- **8m, Fleet Group Configuration / Tactical Battle Display, `DisplayBackground`'s `conquer:true`
  caller, `AskToCapture`, Capital Fallen Report.** `combat-work` branch. User chose the full
  interactive option over two smaller alternatives (an auto-resolved animated replay, or
  configuration-only with a text report) after being asked how deep to go — `ATTCOMM.PAS`'s real
  `Engage` is genuinely player-interactive per round (Move/Target/Retreat between shells), not just
  a viewer, and this reproduces that rather than replaying the automatic engine.
  - **Two wrong assumptions caught before they shipped, both from mis-ordering `CleanUp`'s real
    body.** `CleanUp` (`ATTCOMM.PAS:1564-1588`) runs `RestoreCombatant` for both sides, *then*
    (only on `DefConqueredART`) `OldShipsFound`/`EnemyConquered`, *then* `ResolveAttack` — meaning
    `EnemyConquered`'s own `DisplayBackground(Target,...,True,...)` call, and its `GetType(Target)=
    CapTyp` check, both read the target's **pre-conquest** state, since `ResolveAttack`'s own
    `ConquerWorld` (ownership reassignment) hasn't run yet. `SatisfiesConditions`'s `'A'` clause is
    just `GetStatus(ID) IN Empires` — the object's current owner, same field `'E'` reads — so an
    `A:` scenario index row means "when *this owner* loses the world," keyed on the loser, not the
    conqueror. The 8k-era test `FindWorldBackgroundText_ConqueredByRowOnlyMatchesWhileConquering`
    had reassigned the world to the conqueror *before* checking `conquer:true`, backwards from this
    real ordering — fixed, plus a new test pinning down that the match stops firing once ownership
    *is* reassigned. `AskToCapture`'s `GetShips(Target,Sh2)`/`NoShips` check similarly reads
    post-`RestoreCombatant` survivors, not the pre-battle complement — confirmed by the same
    `CleanUp` read, not assumed.
  - **`InteractiveCombat.cs`** (Core) — `InteractiveCombatState` wraps the exact same
    `CombatEngine.Battle`/`EnemySurrenders`/`CombatResolution.AdvanceGroups` primitives the automatic
    engine uses (`AdvanceGroups` made `internal`, reused rather than duplicated), but exposes one
    round per `Engage()`/`Retreat()` call instead of looping to completion. Confirmed against source
    that the interactive path **never auto-targets** — `ATTCOMM.PAS`'s own `GroupEngage` has no
    counterpart to `ATTNPE.PAS`'s `Targetting`, so a group with no player-chosen `Trg` does zero
    damage while still taking return fire, indefinitely; added to `docs/QUESTIONS_FOR_GEORGE.md`
    since it's surprising enough to be worth asking about. `Retreat` forces `Result` *before* the
    round runs (suppressing `EnemySurrenders` but not `AllGroupsDestroyed`, which can still override
    it) — a `Result`-forcing wrapper around one ordinary round, not a mass-move command; `GroupRetreat`
    doesn't set every group to `Retreating` at all, confirmed by reading it in full.
  - **`FleetGroupConfiguration.cs`** (Core) — `GetGroups`' pool↔group bookkeeping
    (`ChangeGroupType`/`LoadShips`/`LoadTroops`/`Finalize`), separate from `DefaultDistribution`
    (which starts groups already-full; this starts empty, matching `GetGroups`' own
    `FillChar`+`GetShips`/`GetCargo` starting state). Caught two source-vs-assumption gaps while
    writing it: `LoadTroops` initially threw for a non-transport group — real `LoadTransports` has no
    such guard, `TrnAdj` is just 0 for anything else, harmlessly loading zero — fixed to match.
    `Finalize`'s own auto-load-remaining-troops pass checks `Cr[men]` *before* `Cr[nnj]`
    (`ATTCOMM.PAS:1055-1068`) — confirmed the *opposite* priority from `DefaultGroup`'s own
    ninja-first order, a real asymmetry between the two screens, not a bug in either.
  - **`FleetGroupConfigurationWindow.cs`/`TacticalBattleDisplayWindow.cs`** (Tui) — same
    `Window`-built-from-`Label`-children shape as `ResourceDistributionEditor`; the battle display is
    genuinely full-screen (`Dim.Fill()`, matching `AttackWindow`/`GroupWindow`/`EnemyWindow` together
    filling the screen in source) with no Esc at all, matching `Engage`'s own menu exactly. Confirmed
    via `dotnet-inspect` that `View.App` walks the `SuperView` chain (`_app ?? SuperView?.App`) rather
    than needing explicit propagation, so a child view added via `AddModal` can still call
    `App!.AddTimeout` for the message-flash timer — no special wiring needed. One deliberate
    adaptation: `AttReport`'s real `Delay(1000)` blocks the whole game loop for a second per message;
    reproduced as an immediate show + one-shot timer clear instead, not a literal block.
  - **`GameShell.Attack()`** rewritten end to end: a real target picker (`ShowObjectPicker`) when
    multiple enemy fleets share a sector, not just "whichever is found first"; "Standard battle
    configuration (Y/n)?" fork (Y skips straight to `DefaultDistribution`, same as before this
    branch; N opens Fleet Group Configuration); `InteractiveCombat.BeginEngagement` →
    `TacticalBattleDisplayWindow`; on `BattleEnded`, the real `CleanUp` sequence (`RestoreCombatant`
    ×2, `OldShipsFound`, `AskToCapture`, `DisplayBackground(conquer:true)` — all *before*
    `ResolveAttack`, per the ordering fix above — then `ResolveAttack` itself). Zero configured
    groups now skips the battle entirely (`AttackCommand`'s own `IF NoOfGroups>0` gate,
    `ATTCOMM.PAS:1619`), matching real Pascal rather than fighting an empty engagement.
  - **`OldShipsFound` built, `EmpireConquestReport` deliberately not** — asked the user how to
    handle two remaining real (Pascal-has-it-port-doesn't) gaps found while scoping this;
    `OldShipsFound` was cheap and directly adjacent to code already being touched, so built;
    `EmpireConquestReport` (which would need `Booty` tracking un-dropped first) was tracked as an
    `OPEN_GAPS.md` bullet instead. (The battle screen's own decorative art was in this same
    "track instead of build" bucket at first too — see the correction entry below; it isn't
    anymore.)
  - **Capital Fallen Report** (`PROLOG.PAS: EmpireNews`) — genuinely independent of hotseat (unlike
    `GetPassword`, still deferred): the mechanical `PendingElimination`→`Eliminated` teardown was
    already fully ported and tested at the `TurnEngine`/`CombatOutcome` layer, only the narrative
    letter was missing. `Program.cs`'s `RunGame` now shows it once, right where that transition
    actually happens (inside the `AdvanceOneTurn` call `TurnEngine.BeginTurn` runs for a
    `PendingElimination` empire), replacing that empire's own turn session for the one cycle it has
    nothing left to do in. `Honorifics.MyLord` extracted as a shared helper once this made a third
    call site want the exact same randomized-honorific array `TurnStartGreetingWindow`/`GameShell`
    each had their own private copy of.
  - Zero Tui automated coverage exists (`Reconstructed4021.Tests` doesn't reference `Reconstructed4021.Tui`
    at all) — every bit of correctness confidence for this entry comes from Core-level tests
    (`InteractiveCombatTests`, `FleetGroupConfigurationTests`, the `ScenarioLoaderWorldBackgroundTests`
    ordering fix) plus manual playtest, same as every other Tui surface in this port.
  - **Correction, found by the user's own playtest**: `DrawScreen`'s whole visual complex
    (`DrawGrid`/`DrawStars`/`DrawObject`/`DrawEnemyShips`/`DrawGroupShips`) was judged "decorative,
    zero information content" above and left unbuilt — wrong, on two counts caught only once a real
    screenshot of the actual DOS game was compared against this port's own screen. First:
    `TYPES.PAS`'s `ObjectTypes` is declared `(Void,Con,Pln,Base,Gate,...)`, so `DrawScreen`'s own
    `IF Obj IN [Con..Gate] THEN DrawGrid ELSE DrawStars` actually means "attacking a Planet or Base"
    (the common case, and the case in the user's screenshot) draws the range grid — only a Fleet
    target gets the rare starfield branch. Second, and more important: `DrawGroupShips` (called
    every round from `GroupEngage`, right alongside `Battle`) draws each live group's own marker at
    its *current shell*, continuously — real information a player needs to track the battle with,
    not a decoration, and not equivalent to fetching it via `G`roup status on demand. Fixed by
    building a custom-drawn `BattleMapView` (`TacticalBattleDisplayWindow.cs`) reproducing all of
    `DrawScreen`: the range grid/starfield, the target's own ASCII silhouette (`DrawObject`, its
    exact CP437 bytes extracted via a raw codepage-437 byte read of `ATTCOMM.PAS` — this project's
    own established caveat that normal file reads corrupt those bytes), the abstracted
    enemy-ship-cluster glyphs (`DrawEnemyShips`), and the group-position markers, all positioned
    using real Pascal's own byte-offset constant tables (`OrbLoc`/`PlayerOffset`/`EnemyOffset`/
    `Disp`/`Disp2`, ATTCOMM.PAS:46-65,1136-1142) decoded to plain row/column deltas. Explicitly told
    not to defer any of this a second time once asked — built all four pieces in this same pass
    rather than re-triaging by perceived value again.
- **8n, opt-in auto-targeting for the Tactical Battle Display.** Not real Pascal — user feedback after
  playtesting 8m: manually aiming every group every round (the interactive engine's own faithfully-
  reproduced lack of auto-aim, see 8m above) is real tedium once it's not just a design curiosity but
  something being actually played. `InteractiveCombatState.AutoTarget` (off by default, preserving
  8m's own fully-manual behavior exactly) reuses the fully automatic NPE engine's own per-group
  priority rule (`GetTargetCandidates`/`PrioritizeTargetCandidates`/`GetBestTarget`) via a new
  `CombatResolution.AssignAutoTargets`, called once per round for every group the player hasn't
  manually pinned via `SetTarget`. Deliberately a new method rather than factoring
  `FleetEngageTargetting`/`WorldEngageTargetting`'s own assignment loop out for reuse — those two are
  `NpeAttackTests`' own golden-file subjects, not worth refactoring-for-reuse risk — and deliberately
  omits their own `AllAdvance`/`TrnAdvance` no-target-anywhere fallback, since that fallback also
  drives movement, which the interactive engine's own Move command already owns; auto-targeting
  shouldn't silently override a player's queued Advance/Retreat. `<A>uto-target` in the Tactical
  Battle Display toggles it, with an always-visible "Auto-target: ON/OFF" label so the state is never
  ambiguous mid-battle. Attack-pattern profiles (per-ship-type targeting rules, saved/reapplied) were
  raised as a further layer on top of this but explicitly deferred — see the "Long-term future
  possibilities" list at the end of this document.
  - Also fixed while building this: the command box's `Height` left only 8 interior rows for 6
    command lines (rows 1-6) plus a `Command` prompt at `AnchorEnd(2)` (also row 6) — the two
    collided, overwriting the tail of `<R>etreat` on screen (`Commandat` instead of `Command`).
    Caught by the user's own playtest, not self-caught; fixed with real margin (not just +1 row) once
    the 7th command (`<A>uto-target`) needed room too.
  - Also fixed: a `NullReferenceException` crash immediately after answering "Standard battle
    configuration?" — `TacticalBattleDisplayWindow`'s own constructor called `FlashMessage`
    (`App!.AddTimeout`) for the opening flavor line before `GameShell.StartEngagement` had actually
    added the window via `AddModal`; `View.App` resolves by walking the `SuperView` chain, and
    there's no `SuperView` yet during construction. Deferred to the `Initialized` event, same fix
    `TmaLogoWindow` already applies to its own `AddTimeout` call for the identical reason.
  - Also fixed: the Target picker's `ListView` never had its initial selection set, so the first
    `Down` keypress only activated the selection at the top instead of moving off it — two `Down`
    presses landed one item short of where they looked like they should. Same fix
    `GameShell.ShowObjectPicker` already documents for the identical issue, missed here the first
    time.
- **8o, another playtest round: focus leak, Deploy Fleet's real prompt order, and Move/Retreat/
  Target/Group Status moved into GroupWindow itself.** The biggest correction: re-reading
  `GroupMove`/`GroupRetreat`/`GroupTarget`/`GroupStatus` fresh (ATTCOMM.PAS:352-574) at the user's own
  prompt showed all four open with `ActivateWindow(GroupWindow); ClrScr;` and write their own prompts
  directly into that persistent window, accumulating lines without clearing between steps — not
  separate popups, which is what 8m/8n had built. Rewired around a single swappable-content Label
  (`commandBoxContent`) plus an `activePrompt` field routing the next keypress to whichever
  sub-interaction is live; `GroupTarget`'s own single-ATSymb-keypress selection replaced the
  `ListView` picker 8m built (real Pascal types one letter, never navigates a list).
  `AttackDetails` stays a separate popup — it really is one in source too (over `AttackWindow`, not
  `GroupWindow`).
  - **Colors were wrong**: the whole window used `SYSDispWind` (Blue); `COLORS.INC`'s own
    `ColorScrColor` gives `AttackWind=15` (White/Black), `GroupWind=23` (LightGray/Blue),
    `EnemyWind=4` (Red/Black) — three genuinely different windows, not one.
  - **Sizing was wrong**: full-terminal borderless `Dim.Fill()` instead of a fixed 80x28 bordered
    window centered over the map, `CloseUpWindow`'s own established convention.
  - **`WarpIn`'s reveal animation** (every group sliding in from off-screen to DeepSpace when the
    screen opens, ATTCOMM.PAS:91-124) is now reproduced as a short timer-driven slide.
  - **`AddModal` never disabled `galaxyView`**, only the menu bar — a popup whose own `KeyDown`
    doesn't mark Tab as handled (Fleet Group Configuration) let Terminal.Gui's default focus-advance
    binding hand focus to `galaxyView`, a sibling not gated by z-order, and arrow keys then moved the
    map cursor instead of the popup's own grid. Fixed with the same `Enabled=false/true` treatment
    the menu bar already had.
  - **Deploy Fleet's prompt order was wrong**: source before name, composition before destination.
    `PLAYTURN.PAS`'s own `ParameterData` table (:209-218) gives `FLaunchCom`'s real order as name,
    then source, then destination, with `InputNewDistribution` called last inside
    `LaunchFleetCommand` itself — fixed to match exactly.
  - **Post-battle dialogs ran before `galaxyView.Refresh()`**, so the map behind them showed stale
    (or blank-looking) state until every dialog closed. `Refresh` now runs immediately after
    dismissing the battle display.
  - **Checked, not changed**: Ministry of War's own hotkey. `PLAYTURN.PAS:1188,1219` give
    `Worlds='W'`, `Ministry of War='M'` in real Pascal — already what this port has. Switching
    Ministry of War to `W` would collide with `Worlds`' own real hotkey and be less faithful.
  - **Flagged, fixed next**: `GameShell`'s own post-battle dialogs (`OldShipsFound`, `AskToCapture`,
    the final result report) were still stock `MessageBox.Query`, which has no color/scheme override
    at all — see 8p below.
- **8p, post-battle dialogs recolored.** `MessageBox.Query` genuinely has no scheme parameter
  (confirmed via `dotnet-inspect`'s full member list), so the three post-battle call sites 8o flagged
  (`OldShipsFound`, `AskToCapture`, the final result report) are now a new `DosMessageWindow` popup
  instead: `DISPLAY.PAS:161-162`'s own `DisplayWindow` (`ThinBRD`, `SYSDispWind` content /
  `SYSWBorder` border, the exact window every one of these procedures — and `CloseUpWindow` — already
  draws into) with the `SYSDispHigh` "Attack:" header line every one of them writes at the top
  (ATTCOMM.PAS:1347,1520 etc.). `CloseUpWindow.DisplayWindowTitle` made `internal` and reused verbatim
  rather than duplicated, now that there are two callers.
  - Restructured `ApplyAttackOutcome` into a continuation chain (`ShowOldShipsFound` →
    the capture confirm → `FinishConquest`/`FinishAttackOutcome`) instead of the straight-line sequence
    it used to be: `AddModal`'s popups are event-driven, not blocking, unlike the `MessageBox.Query`
    calls they replace, and this repo has no precedent for a reentrant blocking `Application.Run`
    from inside an already-running one — same callback shape Deploy Fleet and Fleet Group
    Configuration already use.
  - `AskToCapture`'s own content (`'You have captured:'` + the ship list, ATTCOMM.PAS:1349-1356) is
    now shown in the confirm dialog's body instead of a blank box with the question pinned to the
    bottom — its three random "commander begs for his life" flavor lines (:1358-1372) are still
    skipped, since porting them means placing an extra `Rnd(1,3)` call correctly relative to
    `EnemyConquered`'s own, and they're flavor text, not content.
  - Caught in review before playtesting, not by the playtest itself (advisor flagged both): `AddModal`'s
    `Dismiss` has no idempotence guard, and `DosMessageWindow` is the first popup where *any* key
    dismisses it — a repeated key landing between `Answered` firing and the popup's removal could fire
    it twice, driving `openModalCount` negative and permanently disabling the menu bar and map for the
    rest of the turn. Fixed with a one-shot `answered` latch. Also: chaining straight into the next
    popup from inside the key handler that just dismissed the previous one meant a fast-repeated key
    (mashing Enter through a chain of dialogs, or two sends arriving close together) could land on the
    new popup before the player ever saw it, silently picking `Capture`'s default. Fixed by deferring
    each next-popup open one tick via `AddTimeout(TimeSpan.Zero, ...)`.
  - Verified: `dotnet test` (564 passed, all Core — this repo still has no Tui test harness) plus a
    psmux session confirming Deploy Fleet, the Resource Distribution Editor, turn advancement, and
    `CloseUpWindow`'s own `DisplayWindowTitle`-formatted title (the exact title `DosMessageWindow` now
    reuses) all still render and dismiss correctly. The live playtest fleet ran out of trillum four
    sectors short of the target before actually reaching combat, so the three new dialogs themselves
    were not exercised end-to-end in a real battle this round — noting that gap rather than claiming
    more than was actually seen. Closed in 8q below.
- **8q, a combat-ready fixture, and the three post-battle dialogs actually seen live.** 8p's playtest
  never reached a real battle — reaching one from "Border Skirmish" means several turns of transit
  first, and the user asked for a fixture that skips that. New
  `assets/saves/Garrisoned Outpost.json`: the human attack fleet starts already at the target sector
  (`Ready`, no `Destination`), which shares its coordinate with both a Kingdom garrison fleet and a
  Kingdom outpost. `GameShell.Attack`'s own target search picks the sole enemy fleet first (matching
  real `GetTarget`'s own priority), so attacking once reaches the garrison fleet; attacking again with
  no Kingdom fleet left there reaches the outpost instead — one fixture, no travel, both target kinds.
  Built through the same Core APIs `GameJsonFixtureTests`'s "Border Skirmish" already uses
  (`GalaxySetup.CreateWorld`, plain `Fleet` construction), not hand-authored JSON — confirmed by
  re-reading that file's own doc comment, which names this as the user's explicit prior direction, not
  a style guess. Both fixture test classes also gained a `[Explicit]` `RegenerateFixture` test:
  excluded from the default `dotnet test` run (it overwrites a committed file) but runnable by name
  (`dotnet test -- --treenode-filter "/*/*/<Class>/RegenerateFixture"`) after editing a fixture's
  `BuildGame`, so a save no longer means hand-editing JSON or a throwaway generator script each time.
  - Live psmux run against the new fixture actually reached combat this time, no turns needed: `T`arget
    both groups, `M`ove/Advance into the garrison's shell, `E`ngage. All three `DosMessageWindow` paths
    fired for real and rendered/dismissed cleanly, menu bar and map still responsive after each —
    `AskToCapture`'s confirm (captured 15 surviving `HunterKiller`s answering "N"), the conquest report
    against the outpost on the second attack (`ConquestMessage`'s `Message2` text, since it isn't the
    Kingdom capital), and the plain `Result: AttackerRetreats` fallback after retreating out of the
    second fight rather than chasing the outpost's orbit-shell defense satellites (a pre-existing shell/
    targeting mechanic, unrelated to this pass).
- **8r, a headless driver replacing psmux.** User asked whether Terminal.Gui had any first-party
  functional-UI-testing support, given how much of 8o/8p/8q's own verification leaned on psmux
  (external tmux-alike process, real pty, sleep-after-every-send-keys timing, ANSI-art screenshots that
  had to be eyeballed). It does: `Terminal.Gui.Testing`, confirmed live in a throwaway probe run with no
  pty attached at all (`Application.Create().Init()` picks the `ansi` driver headless; `InjectKey` +
  `Driver.Contents` work with zero terminal). New `src/Reconstructed4021.TuiDriver` (added to
  `Reconstructed4021.slnx`): a separate project, not a `--headless` flag on `Reconstructed4021.Tui`
  itself, so a future Tui test project can reuse it without Program.cs's own interactive bootstrap;
  `GameShell` made `public` so it can be constructed directly from a loaded save. Takes `--load
  <save.json> --script <path>`, runs a plain-text instruction script (`DUMP`, `SLEEP <ms>`, or
  space-separated `Key.TryParse` tokens) against a live `GameShell`, and prints the rendered screen
  (`Driver.Contents`' `Grapheme`s, trimmed) on request.
  - **Two real deadlocks, found by decompiling Terminal.Gui itself (`dotnet-inspect`'s decompiler
    skill) rather than guessing after two more naive designs hung.** First design: single-threaded
    `InjectKey`+manual iteration pump — hung the instant a script opened a `MessageBox.Query` (GameShell
    has several, e.g. "Standard battle configuration?"), because `IApplication.InjectKey` defaults to
    `InputInjectionMode.Direct` (`ResolveMode(Auto) == Direct`), which calls
    `IInputProcessor.RaiseKeyDownEvent` *synchronously* — runs the key's whole handler chain, including
    entering `MessageBox.Query`'s own nested `Run()`, before `InjectKey` itself returns. That nested
    `Run()` can't return until its own answer is injected by a later script line, and that line can
    never execute because the call that would let script execution continue hasn't returned. Second
    design: moved `app.Run(gameShell, null)` to a dedicated thread and drove the script from a second
    thread via `IApplication.Invoke` — still hung, because `Invoke`'s callback runs the exact same
    `InjectKey` Direct-mode dispatch on the UI thread, so the UI thread was what got stuck this time,
    regardless of which thread called it. Fix, confirmed by decompiling
    `InputProcessorImpl<T>.InjectKeyDownEvent`/`ProcessQueue`: use
    `app.GetInputInjector().InjectKey(key, new InputInjectionOptions { Mode = Pipeline, AutoProcess =
    false })` instead of the `InjectKey` extension method — Pipeline mode just enqueues onto the input
    processor's own `InputQueue`, a real thread-safe queue, and `ProcessQueue()` (called once per
    iteration by *whichever* `Run()` loop is currently live, nested or not — the same mechanism a real
    keyboard-reading thread uses) drains it in order, so a key queued while a dialog is open reaches
    that dialog's own next iteration exactly like a real keypress would. `app.Run` stays on the main
    thread (it has to keep iterating for Pipeline-mode queuing to ever drain); a second thread walks the
    script, queuing input with a short real sleep between actions for pacing. `RequestStop()` needed the
    same `Invoke` marshaling — calling it directly from the script thread didn't reliably unblock
    `app.Run`.
  - Verified against the exact battle 8q drove by hand over psmux: `assets/tui-driver-scripts/
    garrisoned-outpost-battle.txt` replays cursor navigation, Ministry of War > Attack, standard
    config, targeting, advancing, engaging, `AskToCapture`, and the conquest report end to end,
    headless, deterministic, exit code 0 — no pty, no external process.

- **8s, the rest of the Fleet menu (Change Destination/SRM Sweep/Probe), plus contextual fleet
  actions on the Sector Selected Popup and Close Up.** Picked this cluster out of `OPEN_GAPS.md`'s
  "most of the menu bar is still stubs" bullet: all three live together in `FLTCOMM.PAS` right next
  to Transfer/Abort-Join/Refuel (already done), and all three already had full Core support sitting
  unused before this — `FleetLifecycle.SetFleetDestination`, `Galaxy.GetMineOwner`/`ClearMine`/
  `ClearMineScouted` (built for scenario loading's `CreateSRMs`), and `Empire.TryLaunchProbe` (already
  wired into `VisibilityHandler`'s turn resolution). No new Core code needed, only `GameShell` wiring:
  `ChangeDestination`/`SrmSweep` reuse `PickOwnFleetAtCursor` (Change Destination just calls
  `BeginPick` for the new coordinate; SRM Sweep reads the mine at the fleet's own location, matching
  `MineSweeperCommand`'s `GetCoord(FltID,XY)`); `LaunchProbe` skips the fleet-pick step entirely since
  real Pascal's `LaunchProbeCommand` never takes one either.

  Then decided to add a second piece in the same branch: from the Sector Selected Popup and Close Up,
  when the selected item is one of the player's own fleets, reach Change Destination/Transfer/
  Abort-Join/Attack directly (C/T/J/A) instead of going back through `PickOwnFleetAtCursor`'s own
  map-cursor pick. `TransferFleet`/`AbortJoinFleet`/`Attack` each split into a parameterless
  cursor-picking entry point (still used by the menu bar) plus a `Fleet`-accepting overload the new
  hotkeys call directly — same split `ChangeDestination` already needed. `ShowObjectPicker` gained an
  `allowFleetActions` flag (true only for `ShowSectorPicker`, since its other two callers, `PickGround`
  and Attack's own enemy-fleet picker, already give Enter a specific meaning); `ShowCloseUp` checks the
  same four letters directly since Close Up has no shared picker method to gate.

  **One real bug, found by decompiling `Terminal.Gui.Views.ListView` (`dotnet-inspect`) rather than
  guessing after two live-tested "it just doesn't fire" runs.** C/T/J/A worked immediately in Close Up
  (no `ListView`, so its own `KeyDown` fires unconditionally) but silently did nothing in the Sector
  Selected Popup, even though Enter/Esc worked fine there. Confirmed via instrumenting every level
  (`GameShell.KeyDown`, `picker.KeyDown`, `listView.KeyDown`) that a bare letter key produced *zero*
  `KeyDown` events anywhere in the chain, while arrow keys and Enter reached `listView.KeyDown` every
  time. Root cause, from decompiling `ListView.OnKeyDown`: its own type-ahead search
  (`KeystrokeNavigator`) runs as the base `View`'s protected `OnKeyDown` hook, which the public
  `KeyDown` *event* wrapper only raises if that hook itself returns `false` — and `GetNextMatchingItem`
  counts "no better match, selection unchanged" as a handled result, not a miss, so it swallows every
  plain letter unconditionally, match or not. Fix: `listView.KeystrokeNavigator = null` when
  `allowFleetActions` (none of "Outpost"/"Fleet"/"Warfleet" benefit from letter-search anyway), then
  attach the C/T/J/A handling to `listView.KeyDown` directly rather than the picker Window's own
  `KeyDown` (confirmed necessary: even with the navigator disabled, a letter key with no other
  `ListView` binding still reaches the *focused* view's own `KeyDown` first, not the container's).
  Verified via the headless driver (8r): `assets/saves/Garrisoned Outpost.json`, Warfleet in the
  Sector Selected Popup answering C (destination round-trips through `BeginPick`/`Enter` cleanly) and
  J (opens the real Abort/Join ground picker), plus Close Up answering A (opens the real "Standard
  battle configuration?" dialog) — same code path both surfaces share.

- **8t, Fleet Orders (`ORDERS.PAS`'s own mini scripting language, `FLTCOMM.PAS: FleetOrdersCommand`/
  `FleetCancelOrdersCommand`).** `Fleet.Orders` had existed since the save-format work purely for
  round-trip fidelity — nothing ever compiled a player's text into it or executed it during a turn.
  Three pieces: `Fleet.NextOrder` (the execution cursor) plus `Core.Turns.FleetMovementHandler.
  ExecuteFleetOrders`/`ExecuteDestCOM`/`ExecuteTransCOM`, ported directly from `FLEET.PAS:562-617`;
  `Core.Entities.FleetOrderCompiler` (`ParseLine`/`CompileOrders`/`DeCompileOrders`, destination
  resolution via the same per-empire `Names` dictionary already populated elsewhere, or a bounded
  `"x,y"`); `Tui.FleetOrdersWindow`, hosting a `Terminal.Gui.Editor.Editor` child since this version's
  own `TextView` is deprecated with no in-house replacement.

  A real pre-existing save/load gap surfaced and got fixed alongside this: `SavGameLoader`/
  `SavGameWriter` treated the on-disk `NextOrder` byte as dead legacy state (only `OrderData`, the
  adjacent field, really is — a serialized heap pointer). Confirmed against `FLEET_ORDERS.SAV`
  (`nextOrder=2`, genuinely mid-queue, not a fresh compile) that it round-trips as real, live state in
  real Pascal; harmless only because nothing executed the queue yet.

  One genuinely surprising, verified-against-source behavior worth flagging for anyone reading the
  execution loop later: reaching the *last* command in a fleet's order list disposes the whole queue
  in that same call, even when that last command (a `WaitCOM`, say) also independently satisfies the
  loop's own stop condition — `ExecuteFleetOrders`' "what's next" bookkeeping runs unconditionally
  every iteration, *before* the `UNTIL` check, matching `FLEET.PAS:582-611` exactly. Practical upshot:
  a script only actually repeats if it ends with `REPEAT`; anything else is a one-shot no matter what
  the last command is.

  Verified end-to-end via the headless driver: compile success, a compile error's Yes/No
  keep-editing/discard-and-close confirm, and the resulting fleet mutation, all live against
  `assets/saves/Garrisoned Outpost.json`. Getting there took two real, load-bearing discoveries about
  this Terminal.Gui version, both found by decompiling rather than guessing: (1) a bare Esc reaches
  neither a Window's own `KeyDown` nor a plain C# `KeyDown` subscriber added on the Editor child from
  outside — it's consumed through Terminal.Gui's own Command-dispatch path, so overriding it needs a
  thin `Editor` subclass (`AddCommand`/`KeyBindings` are `protected`) rather than an external handler;
  (2) the driver's own Pipeline-mode input queue needs real wall-clock time after a key before a
  `DUMP` reflects its effect — several apparent "the keystroke did nothing" failures during this work
  were actually just a `DUMP` reading the screen one iteration too early, not a real bug, resolved by
  adding a `SLEEP` before the check.

  Two unrelated bugs found and fixed along the way (their own commits): `GalaxyView`'s drag-pan mouse
  grab could get stuck forever if a click happened to follow so much as a pixel of drift after the
  press — any ordinary click with a little drag, not a rare edge case — because the grab-release
  fallback sat behind early returns for `Clicked`/`DoubleClicked`; and `ReportResourceShortfall`/
  `ConstructionLacksRawMaterial` were passing this port's own bare `CargoType` ordinal into a table
  indexed by Pascal's combined `ResourceTypes` numbering, so a Metals shortfall rendered as "ion
  cannons" instead of "megatons of metals" (missing the same `+N` offset already applied correctly for
  ships elsewhere). That fix is explicitly interim — see `OPEN_GAPS.md`; the real fix replaces the
  ordinal arithmetic with nullable typed fields on `NewsItem`, matching `FleetOrder`'s own
  `TransferShip`/`TransferCargo` split, deferred as a follow-up since it touches both save formats.

- **8u, the Build menu (`CONSTR.PAS: ConstructCommand`/`AbortConstructionCommand`/
  `ConstrStatusCommand`).** `ConstructionSite`/the tick's own `UpdateConstruction` (countdown +
  completion) were already fully ported, but nothing ever created one outside of `SavGameLoader`
  reading it from a file. `Core.Entities.ConstructionCatalog` (new `YearsToBuild`/`ConsName` tables
  plus `AnnualTickHandler.Production.cs`'s own private raw-material table, relocated here so the
  tick and the Build menu's cost preview read the same source) and `ConstructionLifecycle.
  StartConstruction` (no upfront resource cost — matching real `Construction`, which never touches
  `Cargo`) are the two new Core pieces. No explicit visibility mark needed at creation — confirmed
  `Game.Visible`/`ScoutedOrOwned` already fall back to plain ownership, the same reason a freshly
  created Starbase/Stargate doesn't mark it either.

  `GameShell.NewConstruction` (tech-filtered `ListView<ConstructionType>` popup → `BeginPick` →
  `ConstructionLifecycle.StartConstruction` → the real cost/time summary, including a small ported
  `Noun` a/an helper, `STRG.PAS:86-92`), `AbortConstruction` (`PickOwnConstructionSiteAtCursor`, a
  plain lookup rather than a picker — a sector holds at most one ground object), and
  `ConstructionSiteStatusWindow` (`NewsWindow`'s own single-pane scrolling shape, not a `TableView`)
  replace the three menu stubs.

  A real bug found and fixed while building this: the "sector already occupied" reprompt used to call
  itself synchronously, inside the very callback that opened its own error dialog — left the map
  cursor silently stuck (`AddModal`'s own `galaxyView.Enabled` didn't clear until that dialog was
  dismissed by some later, unrelated keypress). Deferred one tick instead, the same pattern
  `ShowOldShipsFound` already uses for the identical reason. Verified end-to-end via the headless
  driver against a small dedicated fixture (`BuildMenuFixtureTests`, an empire with
  `Outpost`/`CommandBase` pre-unlocked so the smoke test doesn't need a multi-turn tech grind first):
  the occupied-reprompt, a successful build, Site Status showing it, and Abort removing it again.

  Two unrelated bugs found and fixed along the way (their own commits): `BeginPick`'s own
  `galaxyView.SetFocus()` silently did nothing whenever a still-open panel (Fleet/Status/News/etc,
  stacked underneath a Close Up that had just dismissed *itself*) left `AddModal`'s own modal count
  above zero — found live going from F5's Fleet Window into Close Up into Change Destination, which
  left the map cursor completely unmovable with no visible sign anything was wrong; `BeginPick` now
  dismisses any open panel itself first. And `PickGround` (Transfer/Abort-Join's own target picker)
  now sorts the player's own fleets/world before an enemy's, rather than interleaving them in
  whatever order `Galaxy.Fleets` happened to hold them.

- **8v, Resupply (`GameShell.ResupplyMission`, `Core.Entities.FleetOrderTemplates`).** No Pascal
  precedent — a "templated fleet orders" feature: pick a fleet, a source world, a cargo type/amount,
  and a destination world, and get a one-shot pickup/deliver/return/refuel order sequence installed
  directly, no hand-typing required. The existing DEST/TRAN/REPE/WAIT order language
  (`Core.Entities.FleetOrderCompiler`) already covers the whole "travel, transfer, travel back" shape;
  the one real gap was Refuel, previously only a player-interactive command
  (`GameShell.RefuelFleet`/`FLTCOMM.PAS: RefuelFleetCommand`), not an order-queue token. A new `REFU`
  `CommandType` closes that gap by calling `FleetLifecycle.RefuelFleet` from the order queue — the
  same primitive the NPE AI's own `RefuelBMS` mission already uses, just not previously reachable by a
  player's own orders. `FleetOrderTemplates.Resupply` is the one template so far; more can follow the
  same shape later.

  Three small, unrelated Tui fixes landed alongside this: Close Up was offering "Deploy from here" for
  any world/enemy fleet regardless of ownership (always failing `PickDeploySource`'s own check) — now
  gated the same way the Sector Selected Popup's `FleetActionHint` already was; the bottom-right
  coordinate readout was missing its right margin (`Pos.AnchorEnd()` alone sits flush against the
  edge, `Pos.AnchorEnd() - 1` reserves one column); and Refuel joins Change Destination/Transfer/
  Abort-Join/Attack as a direct quick-option on a selected fleet (Close Up and the Sector Selected
  Popup), not just Fleet menu → Refuel.

## 9. Async/hotseat turn mode

Deferred multiplayer option — sequential mode (already built) is the only mode a solo player sees.

---

Not scheduled, pull in only if/when needed: v2 gameplay changes and new features from
`PASCAL_V1_VS_V2_DIFF.md` (all opt-in, none are baseline).

---

# Long-term future possibilities

- options-based enable/disable v2 features, as well as other future enhancements
- fleet orders to build a minefield across an entire area, either a list of coordinates, or a bounded area
- improved scenario format without so many "magic" numbers
- Tactical Battle Display: named attack-pattern profiles (per-ship-type targeting rules, saved and
  reapplied across engagements/turns) as a further layer on top of the auto-target toggle (§8m/8n) --
  no Pascal precedent, a genuinely port-invented convenience, bigger than auto-target alone (needs a
  profile editor + persistence) so scoped as a later addition, not part of the initial auto-target work
- address shortcomings mentioned (or implied) in Jerry Pournelle's review: https://archive.org/details/byte-magazine-1989-01/page/n137/mode/2up
- counter-based prng for reproduceability: https://chatgpt.com/share/6a945a34-8f90-83e8-83c3-35ef26d6abc0
