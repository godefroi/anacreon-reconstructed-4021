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
   
   **Commit 3: Tech advancement**
   - UpdateTechLevel(WorldID, Emp, Tech) — UPDATE.PAS:1032-1072, random advancement/decay toward capital
   - Consider deferring UpdateDefenses/UpdateMilitary (scope creep; not on critical path)
   
   **Constants to extract:** MaxPop array (UPDATE.PAS:1077-1098), BasePop lookup, TechAdj[], TechAdj2[], K6, SuppliesPerBillion, DrugsPerBillion, ThgLmt() clamp function.
4. **Galaxy / new-game setup** — galaxy generation or scenario loading, empire creation, initial fleet/planet placement. Needed before any of the above can run against a real game rather than hand-built test fixtures.
5. **NPE AI** — implement an `ITurnHandler` for computer empires (start with one "classic" implementation; the handler-per-empire design already supports adding an "advanced" variant later).
6. **Combat** — attack resolution, fleet/starbase destruction, capital loss and empire elimination (the turn loop currently assumes empires never leave `Game.Empires` mid-game — this phase removes that assumption).
7. **Save/load** — translation layer to read/write original `.SAV` files; explicitly does not shape the in-memory model (per earlier decision).
8. **Human interactive turn handler + Terminal.Gui UI** — the last `ITurnHandler` implementation, plus the actual windowed interface (map view, fleet orders, construction, etc.) per `TUI_LIBRARY_RECOMMENDATION.md`.
9. **Async/hotseat turn mode** — deferred multiplayer option; sequential mode (already built) is the only mode a solo player sees.

## Deferred items

Scope decisions: not on the critical path to a playable game, defer until other phases need them:

- **Probe visibility** — `Empire.Probes` exists; probes grant scouting around their location. Deferred because probe movement (`UpdateProbes`) is not yet implemented. Wire up when the probes phase lands.
- **Minefield visibility** — `SectorRecord.MineScout` in the original is per-cell (not per-entity); no `EntityVisibility<Minefield>` exists on `Empire`. Requires combat-phase decision on minefield hit resolution and per-cell tracking. Defer to combat or economy phase.

Not scheduled, pull in only if/when needed: v2 gameplay changes and new features from `PASCAL_V1_VS_V2_DIFF.md` (all opt-in, none are baseline).
