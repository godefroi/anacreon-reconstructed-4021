# Port roadmap

Bottom-up order: simulation core first, UI last. Completed phases aren't listed here — the git log
is the record of what's done. Each phase gets its own plan/design pass when it's picked up; this
file only tracks the sequence and the design decisions/scope calls that need to be made up front.

## 1. Economy / annual tick

Implement `IAnnualTickHandler`. `UpdateUniverse` (UPDATE.PAS:1440-1488) runs, in order: `Year++`,
then `UpdateWorld` over every planet and starbase, then `UpdateConstruction`, then `UpdateEmpire`.
This phase lands `UpdateWorld` for planets first; starbases, construction, and empire-level updates
follow once that's working.

**Design decisions to lock in before writing code:**

- **RNG strategy.** `UpdatePopulation`, `UpdateEfficiency`, and `UpdateTechLevel` all roll dice.
  Pick one approach and use it everywhere in this phase: inject a `Random` (same pattern as
  `VisibilityHandler`), or assert bounds/invariants in tests instead of exact values.
- **Planet/starbase overlap.** Both run through `UpdateWorld` (one procedure, branches on
  `World.ObjTyp`). Decide: a narrow interface covering the shared fields (`Location`, `Owner`,
  `Population`, `Efficiency`, `RevolutionIndex`, `Industry`, `Ships`, `Cargo`), or duplicated logic.
  `IMovable` is the existing precedent for a Fleet/Starbase-shared narrow interface.
- **`Empire.TotalRevolutionIndex`.** `UpdateUniverse` zeroes `NewTotalRevIndex` at line 1445, then
  each world's `UpdateRevolution` presumably accumulates into it, and `UpdateEmpire` applies it
  empire-wide. Verify whether this needs to be genuine per-turn accumulated state or can just be a
  derived sum over owned worlds' `RevolutionIndex` (matches this project's derive-don't-store bias).

**Commits, in order:**

1. **Population, efficiency, revolution** (planets only) — `UpdateEfficiency` (UPDATE.PAS:1381,
   capped at 100) → `UpdatePopulation` (UPDATE.PAS:1074-1116, `MaxPop` table per world class,
   exponential/linear growth) → `UseUpFood` (UPDATE.PAS:1118-1161 — starvation reduces population
   *and* raises revolution index; these two are coupled, don't split them) → `UpdateRevolution`.
2. **Industry and production** (planets only) — `ProduceRawMaterial`, `GetIndustrialDistribution`,
   `UpdateIndustry`, `Production` (UPDATE.PAS:1375-1379; production formula at 844-925). The
   heaviest numerical model in this phase. Defer `UseUpAmbrosia` (couples to this commit's output
   but is a large addition on its own — addiction death/riot/efficiency effects, UPDATE.PAS:1163-1276).
3. **Tech advancement** (planets only) — `UpdateTechLevel` (UPDATE.PAS:1032-1072), random
   advancement/regression toward the empire's capital tech level.
4. **Starbase economy** — same `UpdateWorld` sequence, but with `SupplyLink`/`SurplusLink`
   (UPDATE.PAS:1408,1413 — raw-material redistribution across the empire) and industrial-complex-only
   production (`STyp=cmp` branch, lines 1403-1415).
5. **Construction and empire-level updates** — `UpdateConstruction` (UPDATE.PAS:103-220, countdown
   and completion) and `UpdateEmpire` (UPDATE.PAS:222+, applies accumulated revolution index).

**Deferred past this phase, pull in only when something else needs them:**
- `UseUpAmbrosia` full addiction logic (commit 2's natural follow-on)
- `UpdateDefenses`/`UpdateMilitary` (UPDATE.PAS:1278-1351,1386 — defense/troop buildup; pull in only
  if the combat phase needs baseline defensive state)

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
