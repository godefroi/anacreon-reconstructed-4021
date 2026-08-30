# Port gaps found during the comment/xmldoc sweep (working draft)

Not a real doc yet. Every real port limitation (as opposed to a Pascal-source finding) turned up
while sweeping "Phase N"/"commit N" references and completeness-status language out of code
comments gets logged here, so none of them get lost before the docs pass decides where they
actually belong (a `ROADMAP.md` section, or a separate to-do doc) and folds them in for real. Delete
this file once that happens.

## Entities/Combat/Galaxy pass

- **`CombatOutcome.AbortFleet` doesn't convert leftover fuel to trillum.** A real Fuel/FuelCapacity
  model exists (`Entities/FleetLogistics.cs`) that this method doesn't use; a fleet that dissolves
  via `AbortFleet` silently loses whatever fuel it was carrying instead of it landing as trillum on
  the ground. Already had a matching, more detailed entry in `docs/PASCAL_ARCHITECTURE_NOTES.md`
  ("`CombatOutcome.AbortFleet` still doesn't convert leftover fuel to trillum") that's misfiled
  there for the same reason: it's a port gap, not a Pascal-source finding.
- **`CombatOutcome.RestoreCombatant` doesn't port the Fleet branch's FleetCargoSpace/BalanceFleet/
  FuelCapacity clamp.** This port has no fleet cargo-space/fuel-capacity system. Noted in the code
  as low-severity: structurally unreachable from this method regardless, since it only ever
  subtracts casualties, which can only free up space, never exceed it. Already loosely
  cross-referenced to `docs/ROADMAP.md`'s Phase 6a movement-fidelity gaps, which is a real tracker
  location, unlike the architecture notes.
- **`CombatOutcome.DestroyEmpire` doesn't port `CleanUpNPE`.** Doesn't clean up NPE-AI-decision
  state when an empire dies. A Kingdom's own diplomacy dictionary can still reference a destroyed
  empire, which is the actual mechanism behind the SaveFormat layer's "orphan empire" handling
  (`EntityIndex` in `GameJson.cs`/`SavGameWriter.cs`). Worth a real tracked entry since it has a
  concrete, already-observed consequence elsewhere in the port.
- **No naming system exists anywhere in this port** (Pascal's `Location2Index`/`GetDefinedName`/
  `DeleteName`/`AddName`/`FleetNameDestruction`). Real Pascal lets a player assign a custom name to
  a location; this port has no equivalent concept at all, so every real call site that would
  touch it just skips that step and says so in its own comment: `CombatOutcome.AbortFleet`,
  `CombatOutcome.RestoreCombatant`, `CombatOutcome.DestroyEmpire` (`DeleteAllNames`),
  `CombatStandalone.LAMAttack`/`DestroyConstructionOrGate`, `FleetMovementHandler`'s starbase
  movement, and `AnnualTickHandler.UpdateConstruction`. One real, cross-cutting gap noted
  independently at (at least) seven call sites, not seven separate gaps.

## Turns/NPE pass

(none found beyond the naming system above, which also surfaced here)

## NewGame/Types pass

- **`ScenarioLoader` can't reproduce a .SCN file's own explicit-seed determinism.** Real Pascal's
  `LoadScenario` reads the file's `Seed` field and branches: `Seed=0` calls `Randomize` (system
  clock, non-deterministic — real play, different every time); `Seed<>0` sets `RandSeed:=Seed`
  directly, making that exact file deterministically reproducible (NEWGAME.PAS:1735-1738). Checked
  all 12 real shipped `.SCN` files: every one uses `Seed=0` ("random scenario" in its own header
  comment), so the deterministic branch is never exercised by real content, but it's a real,
  intentional feature of the file format. This port's `ScenarioLoader` tokenizes and discards
  `Seed` unconditionally (`ScenarioLoader.cs`'s own `Load`: `NextToken(tokenizer); // Seed --
  consumed, discarded`) and always takes its `Random` from the caller instead. Mechanically, a
  caller *can* get full reproducibility today by constructing its own `Random` from the same seed
  value twice (.NET's `Random` is itself fully deterministic given a fixed seed) — but only if it
  independently parses `Seed` out of the raw `.SCN` text itself first, since `ScenarioLoader`
  exposes no way to read the file's own recorded value. No real (non-test) caller exists anywhere
  in this port yet to have hit this gap in practice — confirmed by checking: every
  `new ScenarioLoader(...)` in the whole codebase is in a test file.

## SaveFormat pass

(none found beyond the order-queue/message/naming-system gaps already logged above, all
cross-referenced from here too — `SavGameLoader`/`SavGameWriter` are exactly where the order-queue
and message gaps are read-and-discarded/written-empty)

## Tests pass

- **A stale test-doc claim uncovered a real, unresolved numeric mismatch.**
  `AnnualTickHandlerProductionTests.MatchesGoldenFile` excludes `Cargo.Chemicals`/`Cargo.Metals` from
  its golden-file comparison, with the doc comment blaming it on `UpdateDefenses` "not yet ported."
  That's false today — `UpdateDefenses` is fully ported and runs via `RunAnnualTick` in this exact
  test. Checked empirically (temporarily asserting both fields, reverted after): they're still
  genuinely divergent from real Pascal, off by a handful of units (4857 vs. 4863 on
  `FullPipelineCapitalWorld`, 4996 vs. 4998 on `NinjaProductionThrottledByScarceAmbrosia`) — a small,
  real, currently unexplained mismatch, not the missing-feature gap the old comment described. Not
  root-caused: could be an RNG-stream-position difference in how `UpdateDefenses`' own jitter draw
  lands relative to `FixedRandom(0)`'s "always return min" semantics vs. the harness's
  `RngFixedValue`/`ForcedRandomValue` substitution, or a genuine small formula divergence — worth a
  real investigation, not a doc fix.
