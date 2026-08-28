# Port roadmap

Bottom-up order: simulation core first, UI last. Tracks phase/commit status only — the "why does the
C# port look like this" design decisions live in [`PORT_DESIGN.md`](PORT_DESIGN.md), and findings
about the *original* Pascal source (dead code, quirks, bugs) live in
[`PASCAL_ARCHITECTURE_NOTES.md`](PASCAL_ARCHITECTURE_NOTES.md). Each commit below links out to those
where there's a real story; the git log is the record of exactly what changed. Each phase gets its own
plan/design pass when it's picked up.

## 1. Economy / annual tick — ✅ done, all 5 commits landed

`IAnnualTickHandler`. `UpdateUniverse` (UPDATE.PAS:1440-1488): `Year++`, then `UpdateWorld` over every
planet and starbase, then `UpdateConstruction`, then `UpdateEmpire`.

1. ✅ **Population, efficiency, revolution** (planets only) — `UpdateEfficiency` → `UpdatePopulation`
   → `UseUpFood` → `UpdateRevolution` (incl. `Rebellion`). `HostileLife` shipped with this commit too.
   `AnnualTickHandlerTests`/`AnnualTickHandlerRevolutionTests` (golden-file-backed for revolution).
2. ✅ **Industry and production** (planets only) — `ProduceRawMaterial`, `GetIndustrialDistribution`,
   `UpdateIndustry`, `Production`. Golden-file-backed (`AnnualTickHandlerProductionTests`).
   - ✅ **2b, ambrosia addiction** — `UseUpAmbrosia`. `IsAddictedToAmbrosia` becomes real state.
     Golden-file-backed (`AnnualTickHandlerAmbrosiaTests`).
   - ✅ **Golden-file ground truth, all six domains migrated to the patch-based lane** (`techlevel`,
     `military`, `starbase`, `ambrosia`, `revolution`, `production`) — see `PORT_DESIGN.md`'s
     ground-truth harness section for how the two lanes work and when to reach for each.
   - ✅ **2c, military buildup** — `UpdateMilitary`. Golden-file-backed (`AnnualTickHandlerMilitaryTests`).
3. ✅ **Tech advancement** (planets only) — `UpdateTechLevel`. Golden-file-backed
   (`AnnualTickHandlerTechLevelTests`) — the first domain moved to the patch-based lane.
4. ✅ **Starbase economy** — `SupplyLink`/`SurplusLink` plus industrial-complex-only production/economy.
   Introduced `IEconomicWorld` (see `PORT_DESIGN.md`). `AnnualTickHandlerStarbaseTests`, partly
   golden-file-backed (`runworld.pas`'s `starbase` domain).
5. **Construction and empire-level updates** — `UpdateConstruction` and `UpdateEmpire`.
   - ✅ **5a, empire-level tech research** — `NewTechLevel` (nested `GetChanceForNewTech`/`GetNewTech`).
     `Empire.Technology` gained a 4th bucket (`Resources`) — see `PORT_DESIGN.md`. Golden-file-backed
     (`AnnualTickHandlerEmpireTests`).
   - ✅ **5b, construction** — `UpdateConstruction` plus `ConstructStarbase`/`ConstructStargate` entity
     creation. Golden-file-backed (`AnnualTickHandlerConstructionTests`).

**Constants to extract:** `MaxPop` array (UPDATE.PAS:1077-1098), `BasePop` lookup, `TechAdj[]`,
`TechAdj2[]`, `K6`, `SuppliesPerBillion`, `DrugsPerBillion`, `ThgLmt()` clamp function.

## 2. Galaxy / new-game setup — ✅ done, all 5 commits landed

`NEWGAME.PAS`'s `LoadScenario` reads a `.SCN` text scenario file and dispatches world/starbase/
stargate/nebula/mine placement and empire-creation commands one at a time — the scenario script *is*
the generation pipeline. `dos_131` is the canonical scenario set for this phase (matches the DOS 1.31
baseline this port targets); `dos_20`/`pack_1` (the 2.0 port's own sets) stay out of scope until an
opt-in v2-parity pass.

- ✅ **2a, shared helpers** — extracted `Rnd`/`Jitter`/`PascalRound`/`ClampResource`
  (`Core/PascalMath.cs`) and the tech-catalog (`Core/Entities/TechCatalog.cs`). Pure refactor.
- ✅ **2b, empire creation** (`Core/NewGame/EmpireFactory.cs`) — `CreateEmpire`/`CreatePlayerEmpire`/
  `CreateNPEmpire`'s starting tech-set formula, `DefenseSettings.Fleets` seeding. See `PORT_DESIGN.md`
  for the typed-enum "extra techs" design. Golden-file-backed (`empirecreate.golden`, 13 cases).
- ✅ **2c, explicit-coordinate placement** (`Core/NewGame/GalaxySetup.cs`) — `SetUpWorld`/`CreateWorld`/
  `CreateBase`/`CreateGate`/`CreateSRMs`/`CreateNebula`. `Empire.Capital` widened to
  `IEconomicWorld?` — see `PORT_DESIGN.md`.
- ✅ **2d, randomized placement** (`Core/NewGame/GalaxySetup.cs`, continued) — `GetRandomXY`,
  `CreateRndPlanet`, `CreateRandomWorlds`, `NebulaeBand`/`NebulaePatches`. Golden-file-backed
  (`trillumreserves.golden`, `randomplanet.golden`, `nebula.golden`).
- ✅ **2e, `.SCN` scenario file loading** (`Core/NewGame/ScenarioLoader.cs`) — the capstone integration
  test, golden-file-verified against 11 real `dos_131/*.SCN` files (`PRINCES.SCN` excluded — real
  1.31 itself can't load it either). Needed a from-scratch, empirically-verified port of fpc's actual
  `Random`/`RandSeed` algorithm (`PascalRandom.cs`) — see `PASCAL_ARCHITECTURE_NOTES.md`'s scenario
  golden-file section for why exact-match assertions are scoped to non-randomized fields only.

## 3. Probe movement and visibility — ✅ done

`UpdateProbes` runs once per empire at the start of that empire's own turn, resolved as the last step
of `VisibilityHandler.RefreshVisibility`. `Empire.ProbesInTransit: List<Coordinate>` replaces a
pre-existing 4-state `Probe`/`ProbeStatus` stub — see `PORT_DESIGN.md` and
`PASCAL_ARCHITECTURE_NOTES.md`'s dead-code notes for why. Golden-file-backed (`probescout.golden`,
`VisibilityHandlerProbeTests.MatchesGoldenFile`).

## 4. News — ✅ done

Ported `NEWS.PAS`'s per-empire event log — a real data dependency for Phase 6 (NPE AI reads it as its
"what happened to me this turn" signal), not just a UI nicety. `NewsItem`'s design (splitting Pascal's
`Loc` union into typed fields, `ISectorObject`, `OtherEmpire`) is in `PORT_DESIGN.md`.
`EraseNews`'s per-turn reset isn't wired into `TurnEngine` yet — no consumer exists to validate the
timing against.

## 5. Combat — ✅ done, all 9 commits landed

Attack resolution, fleet/starbase destruction, capital loss and empire elimination, plus the
standalone mechanics (LAM strikes, minefield/disrupter movement, self-destruct) that sit outside the
main group-combat round loop. Started with a scoping pass (5a) over `ATTACK.PAS`/`ATTCOMM.PAS`/
`ATTNPE.PAS`/`BATTLE.PAS`/`BOMBER.PAS`/`FLEET.PAS`/`FLTCOMM.PAS`/`SBASE.PAS`/`ORDERS.PAS` plus relevant
`UPDATE.PAS`/`DATACNST.PAS`/`DATASTRC.PAS`/`ANACREON.PAS`/`INTRFACE.PAS` sections. Design decisions
(empire-elimination model, `Empire.DefeatedBy`, `AttackType`) are in `PORT_DESIGN.md`; dead-code and
quirk findings (`BATTLE.PAS`/`BOMBER.PAS`, `ATTNPE.PAS` naming, `HolocaustWorld`, etc.) are in
`PASCAL_ARCHITECTURE_NOTES.md`.

- ✅ **5b, `UpdateDefenses` + `Defns` state** (`AnnualTickHandler.Defenses.cs`) — `DefenseType`-indexed
  growth on `IEconomicWorld`. Golden-file-backed (`defenses.golden`, `AnnualTickHandlerDefensesTests`).
- ✅ **5c, combat constants + `AttackType`** (`Types/AttackType.cs`, `Combat/CombatConstants.cs`) — see
  `PORT_DESIGN.md` for the unified-enum design. Tables: `CombatTable`, `WeapEff`, `ShipValue`,
  `CombatPower`, `ProtecOffered`/`ProtecNeeded`/`TrnAdj`/`GdmKill`, `CargoSpace`, `CombatTechAdj`,
  `CombatClassAdj`, `CombatBaseAdj`, `GdmLaunch`.
- ✅ **5d, group/shell combat engine core** (`Combat/CombatState.cs`, `Combat/CombatEngine.cs`) —
  `CalculateCombatData`, `GetEnemy`, `DefaultDistribution`/`DefaultGroup`, `ForcesUnknown`, `Battle`,
  `EnemySurrenders`. Needed `ATTACK.PAS.patch` (see `PORT_DESIGN.md`'s ground-truth section) to link.
  Golden-file-backed (`combat.golden`, `CombatEngineTests`). Found and fixed the `PascalRound`
  banker's-rounding bug here — see `PASCAL_ARCHITECTURE_NOTES.md`.
- ✅ **5e, resolution loop + `NPEAttack` entry point** (`Combat/CombatResolution.cs`) — `AdvanceGroups`,
  `AllAdvance`/`TrnAdvance`, `FleetRetreats`, `TransportsLeft`, `GroupEngage`, `FleetEngage`/
  `WorldEngage`'s `Targetting`. Needed `ATTNPE.PAS.patch`. Golden-file-backed (`npeattack.golden`,
  `NpeAttackTests`).
- ✅ **5f, outcome application + empire elimination** (`Combat/CombatOutcome.cs`) — `ConquerWorld`,
  `ConquerEmpire`, `RestoreCombatant`, `ResolveAttack`, `DestroyEmpire`/`DestroyFleet`/`AbortFleet`.
  See `PORT_DESIGN.md` for the empire-elimination/`DefeatedBy` model and the `NewsItem.Defender`
  addition. Extended `npeattack.golden` with a second Empire2 world to exercise `ConquerEmpire`'s
  per-planet cascade directly; two hardcoded tests (`CombatOutcomeTests.cs`) cover the human-defeat
  branch the harness can't reach.
- ✅ **5g, standalone mechanics** (`Combat/CombatStandalone.cs`) — `LAMAttack` and
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
- ✅ **5h, minefield damage + disrupter blocking + minefield visibility** (`Turns/FleetMovementHandler.cs`,
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
- ✅ **5i, `HostileLife` news fix** (`Turns/AnnualTickHandler.Revolution.cs`) — `HostileLife`
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

## 6. NPE AI — ✅ done (Kingdom)

Implement an `ITurnHandler` for computer empires. The roadmap's original one-line framing here
("start with one classic implementation") undersold this phase the way "8 known News sites"
undersold Phase 4: six parallel research forks read every NPE-adjacent Pascal file in full
(`NPE.PAS`/`NPETYPES.PAS`/`NPE00`-`NPE04.PAS`/`NPEINTR.PAS`, ~4,400 lines of real decision logic)
before planning. Design rationale and the full commit breakdown are in `PORT_DESIGN.md`; this
section tracks status only.

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

- ✅ **6a, fleet/starbase movement fidelity** (`Turns/FleetMovementHandler.cs`, `Entities/
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
- ✅ **6b, core NPE dispatch + state** (`Types/NpeEmpireType.cs`, `Turns/KingdomTurnHandler.cs`) —
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
- ✅ **6c, `NPEINTR.PAS` toolkit — read-and-compute half** (`Core/Npe/NpeToolkit.cs`,
  `Core/Npe/NpeConstants.cs`, `Core/Npe/NpeTypes.cs`) — `MilitaryPower`, targeting
  (`GetBestTarget`/`GetBestRaiderTarget`/`GetBestBase`/`GetBestPlanetToProtect`/
  `MinimumDefense`/`AverageMilitaryPower`), regional bookkeeping (`CreateRegionArray`/
  `GetRegionalCapital`/`EnforceNpeDataLinks`), world (re)designation (`GetNewDesignation`/
  `ReDesignateEmpire`), `GetFleetComposition`, `SetEmpireDefenses`, `PlunderWorld` — everything in
  NPEINTR.PAS that reads state and computes a value rather than creating/moving a fleet. Includes
  the `FleetDataRecord` field audit (see `PORT_DESIGN.md`'s derive-don't-duplicate note — `Waiting`
  turned out real, not derivable as an earlier pass guessed; `Midway` confirmed dead;
  `BlockX`/`BlockY` Pirate-only). Hardcoded-tested (`NpeToolkitTests.cs`), not golden-file — see
  `PORT_DESIGN.md`'s own note on why, revisit once Phase 7's save/load makes a real test universe
  cheap. Split off from this commit, not bundled in: the five `Deploy*Fleet`/eight `Implement*MSN`
  procedures, which need fleet-lifecycle primitives (`DeployFleet`/`ChangeCompositionOfFleet`/
  `EstimatedDateOfArrival`/`EstimatedRange`/`RefuelFleet`/`SetFleetDestination`) that don't exist
  anywhere in this port yet — see 6c-2.
- ✅ **6c-2, `NPEINTR.PAS` toolkit — fleet-lifecycle half.** Split into two landings: the six
  fleet-lifecycle primitives first (independently verifiable), then the `Deploy*Fleet`/
  `Implement*MSN` layer on top (unverifiable until the primitives are right) — same reasoning that
  split 6c itself.
  - ✅ **Primitives** (`Entities/FleetLifecycle.cs`) — `DeployFleet`/`ChangeCompositionOfFleet`/
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
  - ✅ **`Deploy*Fleet`/`Implement*MSN` layer** (`Core/Npe/NpeToolkit.cs`, alongside 6c's
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
    `SelfDestructObject`. Hardcoded-tested (`NpeToolkitDeployImplementTests.cs`), same rationale as
    6c/6c-2's primitives.
- ✅ **6d, Kingdom core loop** (`Core/Npe/NpeToolkit.cs`, `Core/Npe/NpeTypes.cs`,
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
- ✅ **6e, Kingdom diplomacy** (`Core/Npe/NpeToolkit.cs`, `Core/Turns/KingdomTurnHandler.cs`) —
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
    full hand-assembled universe `NpeToolkit.cs`'s own doc comment already defers to Phase 7.
- ✅ **6f, roadmap wrap-up.** Kingdom (both persona presets) is fully ported: dispatch/state (6b),
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

## 7. Save/load — in progress, 7a landed

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

- ✅ **7a, merge + scope + primitives.** Merged `main` (Phase 6 Kingdom AI, landed after this
  branch split off `sav_file_format_doc` at `28afe88` — confirmed zero file overlap before
  merging, so a clean merge) so the NPE Data section has real `KingdomTurnHandler` state to read.
  Byte-level primitive readers for `SAV_FILE_FORMAT.md`'s documented conventions (fixed-width
  little-endian ints, `STRING[N]`, `SET OF T` bitsets, `IDNumber`, opaque-byte passthrough).
- ✅ **7b, Header + Environment + Sector `LoadGame`** (`Core/SaveFormat/SavGameLoader.cs`). Eight
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
- ✅ **7c, Planets/Starbases/Fleets/Stargates/Constructions `LoadGame`** (`SavGameLoader.cs`).
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
- ✅ **7d, Messages/Empire Data/News `LoadGame`.** Messages read and discarded — no in-memory
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
- **7e, NPE Data `LoadGame` + `KingdomTurnHandler` state seam.** A construct-from-saved-state/
  read-state-back-out seam on `KingdomTurnHandler`, reused by 7f's JSON serializer. Opaque
  per-empire blob storage for Pirate/Berserker/Guardian (no `ITurnHandler` exists for any of them
  yet, but real scenarios mix them with Kingdom empires — their `.SAV` blobs must still
  round-trip). Completes `.SAV` import end to end.
- **7f, native JSON save format.** `Game ↔ JSON`, `System.Text.Json` with `ReferenceHandler.
  Preserve` for the port's real reference cycles and a pragmatic concrete-type switch (not a
  general polymorphic contract) for `IEconomicWorld`/`ISectorObject`/`Game.TurnHandlers` — only
  the types that exist today, not speculative support for handler types not yet built.
- **7g, minimal `.SAV` write-back + real-Pascal acceptance check.** Just enough write-side to
  produce a structurally valid file real Pascal `LoadGame` accepts — no fidelity effort beyond
  that. Verified through `reference/verify/`'s harness (`LOADSAVE.PAS` already compiles cleanly
  there, Tier 11) against every reference save — the strongest available proof 7b-7e's `LoadGame`
  is correct, not just internally self-consistent.
- **7h, roadmap wrap-up.** Flip this phase to done; confirm the tracked gaps (order queues,
  UI/session Environment fields, `.SAV` write not being a maintained feature) are described
  accurately for Phase 8.

## 8. Human interactive turn handler + Terminal.Gui UI

The last `ITurnHandler` implementation, plus the actual windowed interface (map view, fleet orders,
construction, etc.) per `TUI_LIBRARY_RECOMMENDATION.md`.

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
- address shortcomings mentioned (or implied) in Jerry Pournelle's review: https://archive.org/details/byte-magazine-1989-01/page/n137/mode/2up
