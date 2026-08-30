# Open gaps

Known limitations in this port: places where the C# doesn't yet do everything the original Pascal
did, distinct from `docs/PASCAL_ARCHITECTURE_NOTES.md`, which is about the 1988 game itself. An
entry here means "this port is missing something," not "the original had a quirk."

## Naming system

No naming system exists anywhere in this port (Pascal's `Location2Index`/`GetDefinedName`/
`DeleteName`/`AddName`/`FleetNameDestruction`). Real Pascal lets a player assign a custom name to a
location; this port has no equivalent concept at all, so every call site that would touch it just
skips that step and says so in its own comment: `CombatOutcome.AbortFleet`,
`CombatOutcome.RestoreCombatant`, `CombatOutcome.DestroyEmpire` (`DeleteAllNames`),
`CombatStandalone.LAMAttack`/`DestroyConstructionOrGate`, `FleetMovementHandler`'s starbase
movement, and `AnnualTickHandler.UpdateConstruction`. One real, cross-cutting gap, not seven
separate ones.

## Combat

- **`CombatOutcome.AbortFleet` doesn't convert leftover fuel to trillum.** A real Fuel/FuelCapacity
  model exists (`Entities/FleetLogistics.cs`) that this method doesn't use; a fleet that dissolves
  via `AbortFleet` silently loses whatever fuel it was carrying instead of it landing as trillum on
  the ground.
- **`CombatOutcome.DestroyEmpire` doesn't port `CleanUpNPE`.** Doesn't clean up NPE-AI-decision
  state when an empire dies. A Kingdom's own diplomacy dictionary can still reference a destroyed
  empire, which is the actual mechanism behind the SaveFormat layer's "orphan empire" handling
  (`EntityIndex` in `GameJson.cs`/`SavGameWriter.cs`).

## Scenario loading

**`ScenarioLoader` can't reproduce a `.SCN` file's own explicit-seed determinism.** Real Pascal's
`LoadScenario` reads the file's `Seed` field and branches: `Seed=0` calls `Randomize` (system
clock, non-deterministic — real play, different every time); `Seed<>0` sets `RandSeed:=Seed`
directly, making that exact file deterministically reproducible (NEWGAME.PAS:1735-1738). All 12
real shipped `.SCN` files use `Seed=0` ("random scenario" in their own header comment), so the
deterministic branch is never exercised by real content, but it's a real, intentional feature of
the file format. This port's `ScenarioLoader` tokenizes and discards `Seed` unconditionally
(`ScenarioLoader.cs`'s own `Load`: `NextToken(tokenizer); // Seed -- consumed, discarded`) and
always takes its `Random` from the caller instead. A caller can still get full reproducibility
today by constructing its own `Random` from the same seed value twice (.NET's `Random` is itself
deterministic given a fixed seed) — but only if it independently parses `Seed` out of the raw
`.SCN` text itself first, since `ScenarioLoader` exposes no way to read the file's own recorded
value. No non-test caller exists anywhere in this port yet — every `new ScenarioLoader(...)` in the
codebase is in a test file.

## Save/load

- **Fleet order queues aren't modeled.** `.SAV`'s `CommandRecord`/`DestCOM` order queues are
  read-and-discarded on import (`SavGameLoader`) and always written empty on export
  (`SavGameWriter`) — no in-memory representation of a fleet's pending orders exists anywhere in
  this port.
- **Messages aren't modeled.** Same treatment as order queues: no in-memory concept of a player
  message exists, so `.SAV` messages are read-and-discarded on import and never written on export.
- **UI/session Environment fields are discarded on import.** `EmpiresToMove`/`TimePerTurn`/
  `AutoSave`/`AsyncTurns`/`PauseActive`/`ReEnterGame` have no effect anywhere in this port yet —
  `EmpiresToMove` is redundant with `Game.CurrentEmpire`/`NextEmpire()` so there's nothing to store
  regardless; the rest are read-and-discarded pending a UI session that has settings to hold them.
- **`SavGameLoader`'s `DefeatedBy` decode branch (a conquered human empire) has no exercising
  reference save.** None of the 13 captured `.SAV` files include a defeated human empire, so this
  branch was implemented and reasoned through directly from `ConquerEmpire`'s on-disk sentinel
  convention, not verified against a real file. Worth a note for whoever next captures or
  hand-builds one.

## Production tick: unresolved Cargo.Chemicals/Metals mismatch

`AnnualTickHandlerProductionTests.MatchesGoldenFile` excludes `Cargo.Chemicals`/`Cargo.Metals` from
its golden-file comparison. They're genuinely divergent from real Pascal, off by a handful of units
(4857 vs. 4863 on `FullPipelineCapitalWorld`, 4996 vs. 4998 on
`NinjaProductionThrottledByScarceAmbrosia`) — confirmed by temporarily asserting both fields against
the golden file directly. Not root-caused: could be an RNG-stream-position difference in how
`UpdateDefenses`' own jitter draw lands relative to `FixedRandom(0)`'s "always return min" semantics
versus the harness's `RngFixedValue`/`ForcedRandomValue` substitution, or a genuine small formula
divergence. Needs real investigation.
