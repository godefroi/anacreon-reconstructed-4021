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

- **Most of the menu bar is still stubs.** `Fleet > Orders`/`Cancel Orders` (`Change Destination`/
  `SRM Sweep`/`Probe` are done), all of `Build`/`Empire`/`Worlds`, `Ministry of War > Auto Attack`/
  `Launch LAMs`/`Defenses`, and every status-bar F-key panel are a `MessageBox` stub. `Orders` is
  `FLTCOMM.PAS: FleetOrdersCommand` — a full mini scripting language (`ORDERS.PAS`'s own
  compile/decompile pair over a captive text editor), not a quick add; `Cancel Orders` is trivial but
  pointless before `Orders` exists. `Defenses` needs its own custom grid (ship type × orbital shell
  percentages) — a different widget from the Resource Distribution Editor, not yet built.
- **No post-conquest world-list report.** `ATTACK.PAS: ConquerEmpire`'s own `Booty` (the set of
  world indices that joined the conqueror mid-`ConquerEmpire`) is declared and threaded through
  real Pascal's call chain but never read anywhere in it — dropped entirely by this port, confirmed
  by reading (`CombatOutcome.cs`'s own doc comment). `ATTCOMM.PAS: EmpireConquestReport` (the screen
  that would list those worlds after a capital-conquering attack) is the one real consumer of that
  data; building it means un-dropping `Booty` tracking first, not just adding a screen.
- **No hotseat protection.** `PROLOG.PAS: SetUpPlayer`'s own password prompt (`GetPassword`) isn't
  built. `Program.cs`'s turn loop is already `Status`/`ITurnHandler.IsHuman`-driven per empire (not
  hardcoded to one `Empire` reference), so a second human empire would already get its own
  greeting+`GameShell` cycle when its slot comes up — the password step is the only thing actually
  missing for that — but nothing exercises any of this yet (this branch's own fixture has exactly
  one human).

## Production tick: unresolved Cargo.Chemicals/Metals mismatch

`AnnualTickHandlerProductionTests.MatchesGoldenFile` excludes `Cargo.Chemicals`/`Cargo.Metals` from
its golden-file comparison. They're genuinely divergent from real Pascal, off by a handful of units
(4857 vs. 4863 on `FullPipelineCapitalWorld`, 4996 vs. 4998 on
`NinjaProductionThrottledByScarceAmbrosia`) — confirmed by temporarily asserting both fields against
the golden file directly. Not root-caused: could be an RNG-stream-position difference in how
`UpdateDefenses`' own jitter draw lands relative to `FixedRandom(0)`'s "always return min" semantics
versus the harness's `RngFixedValue`/`ForcedRandomValue` substitution, or a genuine small formula
divergence. Needs real investigation.
