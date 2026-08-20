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
   own roadmap line. Untested branches, faithful to source but never exercised: `HostileLife`, and
   the military-suppression path (`Military>OptimumMilitary`, UPDATE.PAS:715-735) — add coverage if
   a future change touches either.
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
