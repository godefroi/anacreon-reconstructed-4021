# Port roadmap

Bottom-up order: simulation core first, UI last. Each phase gets its own plan/design pass when it's picked up — this is just the sequence.

1. **Fleet movement** — ✅ DONE
2. **Fog-of-war / visibility** — ✅ DONE
3. **Economy / annual tick** — implement `IAnnualTickHandler`: (3 commits, UPDATE.PAS verified)
   
   **Scope:** `UpdateUniverse` (UPDATE.PAS:1440-1488) runs Year++, then UpdateWorld over all planets/starbases, UpdateConstruction, UpdateEmpire. This phase lands UpdateWorld only; UpdateConstruction/UpdateEmpire deferred.
   
   **Design decisions to lock in first:**
   - **RNG strategy:** UpdatePopulation, UpdateEfficiency, UpdateTechLevel all use `Rnd()` calls. Pick one: (a) seeded `Random` passed into handler, (b) narrow abstraction (e.g., `IRandomSource`), or (c) assert bounds/invariants in tests instead of exact values. Commit to this before writing any code.
   - **Planet/Starbase overlap:** Both run UpdateWorld (same procedure, branches on `World.ObjTyp`). Add a narrow `IEconomic { Location, Owner, Population, Efficiency, RevolutionIndex, Industry, Ships, Cargo, SpecialConditions }` interface on Planet/Starbase, or duplicate logic? Precedent: `IMovable` exists for Fleet/Starbase.
   - **TotalRevolutionIndex:** `Empire.TotalRevolutionIndex` is a stored field. Verify if it's genuinely incremental state (accumulated across worlds per tick) or should be derived as a sum of all owned worlds' RevolutionIndex. Current code initializes `NewTotalRevIndex` at line 1445 then updates per-world; decide whether to mirror that or compute on demand.
   
   **Commit 1: Population, efficiency, revolution**
   - Implement UpdateWorld call sequence (for planets only, starbase variant deferred):
     1. UpdateEfficiency(Emp, Eff) — UPDATE.PAS:1381, random range-based increment capped at 100
     2. UpdatePopulation(Cls, Tech, Pop) — UPDATE.PAS:1074-1116, MaxPop table per class, exponential/linear growth
     3. UseUpFood(WorldID, Pop, Food) — UPDATE.PAS:1118-1161, starvation reduces Pop and increases RevIndex
     4. UpdateRevolution(WorldID) — finalize accumulated RevIndex
   - Test: seeded Population growth, food starvation reduces pop/increases revolution, efficiency caps at 100
   
   **Commit 2: Industry and production**
   - ProduceRawMaterial, GetIndustrialDistribution, UpdateIndustry, Production (UPDATE.PAS:1375-1379). Heavy numerical model; production formula at lines 844-925.
   - Defer UseUpAmbrosia (coupled to Commit 1, adds complexity)
   
   **Commit 2b: Ambrosia consumption** (optional, if scope allows)
   - UseUpAmbrosia — UPDATE.PAS:1163-1276, coupled to Commit 2 production (ambrosia addiction logic, affects pop/efficiency/revolution)
   
   **Commit 3: Tech advancement**
   - UpdateTechLevel(WorldID, Emp, Tech) — UPDATE.PAS:1032-1072, random advancement/decay toward capital
   
   **Commit 4: Starbase economy**
   - Same UpdateWorld sequence as planets, but with SupplyLink/SurplusLink (raw-material distribution across empire). Simpler when planets are working.
   
   **Commit 5: Construction and empire-wide updates**
   - UpdateConstruction(i) — UPDATE.PAS:103-220, countdown and completion logic
   - UpdateEmpire(Emp) — UPDATE.PAS:222+, apply NewTotalRevIndex accumulation to empire state
   
   **Deferred to later phases:**
   - UpdateDefenses/UpdateMilitary (UPDATE.PAS:1278-1351, 1386) — defense/troop buildup; scope creep for initial economy, pull in if combat phase needs baseline defenses
   
   **Constants to extract:** MaxPop array (UPDATE.PAS:1077-1098), BasePop lookup, TechAdj[], TechAdj2[], K6, SuppliesPerBillion, DrugsPerBillion, ThgLmt() clamp function.
4. **Galaxy / new-game setup** — galaxy generation or scenario loading, empire creation, initial fleet/planet placement. Needed before any of the above can run against a real game rather than hand-built test fixtures.
5. **NPE AI** — implement an `ITurnHandler` for computer empires (start with one "classic" implementation; the handler-per-empire design already supports adding an "advanced" variant later).
6. **Probe movement and visibility** — `UpdateProbes` (deferred from phase 2); probes move each turn and grant scouting around their location. Wire into visibility refresh after probes are moving.
7. **Combat** — attack resolution, fleet/starbase destruction, capital loss and empire elimination (the turn loop currently assumes empires never leave `Game.Empires` mid-game — this phase removes that assumption). Includes minefield scouting (`SectorRecord.MineScout` per-cell) and minefield hit resolution.
8. **Save/load** — translation layer to read/write original `.SAV` files; explicitly does not shape the in-memory model (per earlier decision).
9. **Human interactive turn handler + Terminal.Gui UI** — the last `ITurnHandler` implementation, plus the actual windowed interface (map view, fleet orders, construction, etc.) per `TUI_LIBRARY_RECOMMENDATION.md`.
10. **Async/hotseat turn mode** — deferred multiplayer option; sequential mode (already built) is the only mode a solo player sees.

## Deferred items (ordered by where they become possible)

Scope decisions: not on the critical path to a playable game, deferred until other phases need them.

**Economy phase (Commits 4-5):**
- **Starbase economy variants** — SupplyLink/SurplusLink raw-material redistribution (UPDATE.PAS:1408,1413) and industrial-complex-only production (lines 1403-1415 branch on STyp=cmp)
- **UpdateDefenses/UpdateMilitary** — defense and troop buildup per world (UPDATE.PAS:1278-1351, 1386). Scope creep; pull in only if combat phase needs baseline defensive state.
- **UseUpAmbrosia full logic** — Commit 2b optional; ambrosia addiction effects (death, riots, efficiency loss) couple to production.

**After phase 6 (Probe movement):**
- **Probe visibility in scouting** — probes grant scouting radius around their location, fed into RefreshVisibility. Deferred: probe movement (`UpdateProbes`) not yet implemented.

**Phase 7 (Combat):**
- **Minefield visibility and hit resolution** — `SectorRecord.MineScout` is per-cell (not per-entity; no `EntityVisibility<Minefield>`). Requires combat mechanics for minefield damage and per-cell tracking. Deferred to combat phase.

**Not scheduled, pull in only if/when needed:**
- v2 gameplay changes and new features from `PASCAL_V1_VS_V2_DIFF.md` (all opt-in, none are baseline)
