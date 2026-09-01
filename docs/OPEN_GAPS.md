# Open gaps

Known limitations in this port: places where the C# doesn't yet do everything the original Pascal
did, distinct from `docs/PASCAL_ARCHITECTURE_NOTES.md`, which is about the 1988 game itself. An
entry here means "this port is missing something," not "the original had a quirk."

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

## Turn engine

- **No simultaneous-turns mode.** Real Pascal's `AsyncTurns` (`ANACREON.PAS:217-417`) lets every
  empire act without waiting in strict rotation; `TurnEngine.AdvanceOneTurn` only implements the
  synchronous mode (one `CurrentEmpire` at a time, `NextEmpire`'s fixed cyclic order). `Game.AsyncTurns`
  round-trips through `.SAV` but nothing reads it — out of scope for now, but real multiplayer/hotseat
  parity needs it eventually.

## Human interactive turn handler / TUI

A real per-empire turn loop now exists (`Program.cs`'s own loop, human sessions via `GameShell`,
NPE turns auto-played with no UI), wired against a hand-built `assets/saves/Border Skirmish.json`
fixture (loaded via `--load`) rather than a `.SCN` scenario. Real gaps this deliberately doesn't
close:

- **Fog-of-war is real for the map and Close Up's own gating, but not yet for Close Up's fields.**
  `GalaxyView` and `GameShell.ObjectsAt` now gate on `Game.Visible` (Known, or owned outright) — an
  unscouted world or enemy fleet just doesn't draw, and can't be opened via Close Up/the sector picker
  at all (`MAPWIND.PAS`'s `UMSector`/`UMFleets`/`EnemyFleetInSector`). Two real gaps remain: (1)
  `CloseUpWindow`'s own field dump still shows everything about an object once it's open, rather than
  reproducing `CloseUpCom`'s finer-grained `Known`-vs-`Scouted` field redaction (Known-but-not-Scouted
  should show less detail than fully Scouted); (2) `MAPWIND.PAS`'s `UnkPlanetChar` case — an unscouted
  planet in a nebula still gets a distinct "something's there" glyph — isn't reproduced, since this
  port has no nebula-vision modeling at all yet (`VisibilityHandler.ScoutAdjacent`'s own TODO); an
  unscouted planet reads as plain nebula (or nothing, outside one) instead.
  Also worth knowing, not a gap: first discovery of an object this empire has never seen only happens
  via adjacency (a fleet/world within 1 cell) or a 50% roll in starbase scan range — there's no
  starbase-free "detect at a distance" path (`INTRFACE.PAS: DetermineIfScouted`'s capital-range check
  only re-detects something already Known). A scenario fixture with no starbases (`assets/saves/Border
  Skirmish.json`) is genuinely flying blind on turn 1 until a fleet closes to adjacency — faithful
  behavior, confirmed by inspecting the fixture's actual post-refresh visibility state before this was
  wired up, not a bug.
- **Fleet menu's real commands are done; the rest of the menu bar is still stubs.** Deploy, Transfer,
  Abort/Join, and Refuel are all real now (`FLTCOMM.PAS`'s `LaunchFleetCommand`/`TransferFleetCommand`/
  `AbortFleetCommand`/`RefuelFleetCommand`, `TUI_SURFACES_MAPPING.md`'s "Fleet management" table) —
  Deploy/Transfer share the real Resource Distribution Editor (`ResourceDistributionEditor.cs`,
  `InputNewDistribution`), Abort/Join and Refuel share a Ground/Fleet Target Picker
  (`GameShell.PickGround`, `GetGround`) with no grid. Everything else on the menu bar (`Fleet >
  SRM Sweep`/`Orders`/`Cancel Orders`/`Probe`, all of `Build`/`Empire`/`Worlds`, `Ministry of War >
  Auto Attack`/`Launch LAMs`/`Defenses`, every status-bar F-key panel) is still a `MessageBox` stub.
  `Defenses` needs its own custom grid (ship type × orbital shell percentages) — a different widget
  from the Resource Distribution Editor, not yet built.
- **No Fleet Group Configuration or Tactical Battle Display.** `Ministry of War > Attack` resolves
  entirely through `CombatResolution.NPEAttack`'s own `CombatEngine.DefaultDistribution` (the same
  grouping Kingdom's AI uses) with `AttackIntentionType.Conquer` always assumed and no player choice
  of intent, groups, or a turn-by-turn tactical view.
- **No hotseat protection.** `Program.cs`'s turn loop is already `Status`/`ITurnHandler.IsHuman`-driven
  per empire (not hardcoded to one `Empire` reference), so a second human empire would already get
  its own greeting+`GameShell` cycle when its slot comes up — and the Empire Status Report step of
  `PROLOG.PAS: SetUpPlayer`'s chained per-turn sequence is now real (`EmpireStatusWindow.cs`), but
  the password prompt and Capital Fallen Report steps are still missing, and nothing exercises any
  of this yet (this branch's own fixture has exactly one human).

## Production tick: unresolved Cargo.Chemicals/Metals mismatch

`AnnualTickHandlerProductionTests.MatchesGoldenFile` excludes `Cargo.Chemicals`/`Cargo.Metals` from
its golden-file comparison. They're genuinely divergent from real Pascal, off by a handful of units
(4857 vs. 4863 on `FullPipelineCapitalWorld`, 4996 vs. 4998 on
`NinjaProductionThrottledByScarceAmbrosia`) — confirmed by temporarily asserting both fields against
the golden file directly. Not root-caused: could be an RNG-stream-position difference in how
`UpdateDefenses`' own jitter draw lands relative to `FixedRandom(0)`'s "always return min" semantics
versus the harness's `RngFixedValue`/`ForcedRandomValue` substitution, or a genuine small formula
divergence. Needs real investigation.
