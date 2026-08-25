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

## 5. Combat — in progress, 5a-5f landed

Attack resolution, fleet/starbase destruction, capital loss and empire elimination. Design decisions
(empire-elimination model, `Empire.DefeatedBy`, `AttackType`) are in `PORT_DESIGN.md`; dead-code and
quirk findings (`BATTLE.PAS`/`BOMBER.PAS`, `ATTNPE.PAS` naming, etc.) are in
`PASCAL_ARCHITECTURE_NOTES.md`.

- ✅ **5a, scoping.** Research pass over `ATTACK.PAS`/`ATTCOMM.PAS`/`ATTNPE.PAS`/`BATTLE.PAS`/
  `BOMBER.PAS`/`FLEET.PAS`/`FLTCOMM.PAS`/`SBASE.PAS`/`ORDERS.PAS` plus relevant `UPDATE.PAS`/
  `DATACNST.PAS`/`DATASTRC.PAS`/`ANACREON.PAS`/`INTRFACE.PAS` sections. Findings recorded in
  `PASCAL_ARCHITECTURE_NOTES.md`/`PORT_DESIGN.md`.
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
- **5g, standalone mechanics** — `HolocaustWorld`, `LAMAttack`, `DestroyConstructionOrGate`,
  `SelfDestructObject` (ported with no caller wired up yet, same precedent as `Empire.News.Clear()`).
- **5h, minefield damage + disrupter blocking + minefield visibility** — folded additively into
  `FleetMovementHandler`'s existing step loop; new per-cell `Galaxy` mine-visibility structure
  (can't reuse `EntityVisibility<T>`, which is keyed by entity reference, not coordinate).
- **5i, `HostileLife` news fix + roadmap wrap-up** — a small Phase 1/4 gap (`HostileLife`, shipped
  Phase 1, calls no `AddNews` despite the relevant `NewsType` members already existing).

### Tracked gaps in already-shipped movement (surfaced by Phase 5, not fixed by it)

Deliberately simplified straight-line steppers in `FleetMovementHandler` (Phase 2/3) next to what real
Pascal does — found while scoping Combat, but movement fidelity, not a combat mechanic, so not fixed
in Phase 5 (see `PORT_DESIGN.md` for why each is out of scope for now):

- **Stargate teleportation / fortress pass-through jumps** (`FLEET.PAS:646-860`'s `UpdateFleet`).
- **Starbase obstacle-avoidance and fuel cost** (`SBASE.PAS:88-263`'s `MovePlayerStarbases`/
  `GetNewBasePos`).
- **Fleet fuel/cargo-capacity system** (surfaced by 5f) — no `FuelCapacity`/`FleetCargoSpace`/
  `BalanceFleet` equivalent anywhere in this port yet. Currently only visible as a gap in
  `RestoreCombatant`'s Fleet branch (harmless there — see `PORT_DESIGN.md`).

## 6. NPE AI

Implement an `ITurnHandler` for computer empires. Start with one "classic" implementation — the
handler-per-empire design (`Game.TurnHandlers`) already supports adding an "advanced" variant later.

Moved after Probes/News/Combat (was Phase 3 originally): NPE decision-making is written against
combat primitives from the start (`NPE01.PAS`'s `USES` clause pulls in `Attack`/`AttNPE`) and reads
the News feed as a real sensory input, not just a display concern.

## 7. Save/load

Translation layer to read/write original `.SAV` files. Does not shape the in-memory model — the
on-disk format is a serialization concern, not a design input.

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
