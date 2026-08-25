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

## 5. Combat — in progress, 5a-5h landed

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
- **5i, `HostileLife` news fix + roadmap wrap-up** — a small Phase 1/4 gap (`HostileLife`, shipped
  Phase 1, calls no `AddNews` despite the relevant `NewsType` members already existing).

## 6. NPE AI

Implement an `ITurnHandler` for computer empires. Start with one "classic" implementation — the
handler-per-empire design (`Game.TurnHandlers`) already supports adding an "advanced" variant later.

Moved after Probes/News/Combat (was Phase 3 originally): NPE decision-making is written against
combat primitives from the start (`NPE01.PAS`'s `USES` clause pulls in `Attack`/`AttNPE`) and reads
the News feed as a real sensory input, not just a display concern.

- **6a, fleet/starbase movement fidelity (prerequisite).** `FleetMovementHandler`'s `AdvanceFleet`/
  `AdvanceStarbases` (shipped pre-Phase-1, in the initial skeleton) are deliberately simplified
  straight-line steppers next to what real Pascal does — found while scoping Combat (Phase 5), not
  fixed there since it's movement fidelity, not a combat mechanic (see `PORT_DESIGN.md`). Belongs
  here rather than Phase 5 or Phase 8: `NPE01.PAS`'s decision logic (fuel-aware retreat, stargate
  routing) can't be reasoned about correctly until these are real, and it's simulation core, not UI,
  so it lands before Phase 8 per this roadmap's own bottom-up ordering. Land it as prep before the
  actual AI commits:
  - Fleet stargate teleportation / fortress pass-through jumps (`FLEET.PAS:646-860`'s `UpdateFleet`).
  - Starbase obstacle-avoidance and fuel cost (`SBASE.PAS:88-263`'s `MovePlayerStarbases`/
    `GetNewBasePos`).
  - Fleet fuel/cargo-capacity system (`FuelCapacity`/`FleetCargoSpace`/`GetFleetFuel`/`SetFleetFuel`/
    `BalanceFleet` — surfaced by 5f's `RestoreCombatant`, which stubs out the `BalanceFleet` call
    since real Pascal's own version lives in `Intrface`, dropped from this port's scope so far).
  - Dense-nebula movement blocking (`FLEET.PAS:437-450`'s `GetNewPos`) — surfaced by 5h while tracing
    the exact per-step move loop for mine/disrupter checks; applies to every fleet type, not just
    `JumpFleet`/`HunterKillerFleet`, so it's a terrain effect rather than a combat mechanic.

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
- address shortcomings mentioned (or implied) in Jerry Pournelle's review: https://archive.org/details/byte-magazine-1989-01/page/n137/mode/2up
