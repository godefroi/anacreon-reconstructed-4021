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
- **Guardian/Trader NPE empires have no turn handler, and the turn loop crashes once one comes up.**
  (Pirate and Berserker had this same gap; both fixed — `PirateTurnHandler`/`BerserkerTurnHandler` now
  handle it via `LegacyNpeProvider`, see `docs/ROADMAP.md`'s phase 6f entry.)
  `ScenarioLoader.RunCreateNPEmpire` only registers a `Game.TurnHandlers` entry for whatever
  `INpeHandlerProvider.Handles` — currently `Kingdom1`/`Kingdom2`/`Pirate`/`Berserker` — the remaining
  two personalities (`Guardian`/`Trader`) get added to `Game.Empires` with their `NpeType` recorded but
  no `ITurnHandler`, matching `TurnEngineTests`' own "ai has no entry in TurnHandlers" comment. That's
  fine as far as it goes, but nothing downstream actually tolerates it: `Program.cs`'s `RunGame` loop
  (`game.TurnHandlers[current]`) and `TurnEngine.BeginTurn` (same unguarded lookup) both assume every
  empire in `Game.Empires` has an entry, so the TUI throws `KeyNotFoundException` and crashes the
  instant `CurrentEmpire` cycles onto one of these empires' slot. `Trader` is confirmed dead code (zero
  scenario usage, zero dispatch-procedure case arms — see `docs/ROADMAP.md`'s own 6f wrap-up), so
  `Guardian` is the one real remaining risk here; `docs/ROADMAP.md` names no scenario that reaches it
  today. `TurnEngineTests.MissingHandler_ThrowsKeyNotFoundException` currently encodes the crash as the
  *expected* behavior, so a real fix needs to change that test's contract too, not just guard one call
  site — and still leaves Guardian with no actual AI behavior, only "doesn't crash."

## Human interactive turn handler / TUI

- **Menu bar items still a `MessageBox` "Not yet implemented." stub.** `docs/TUI_SURFACES_MAPPING.md`
  has the planned widget shape and exact Pascal procedure for each; anything not listed here is done.
  - [ ] `⌂ > About Anacreon`
  - [ ] `Game > Status Hardcopy`
  - [ ] `Empire > Send Message` / `Read Messages` / `Trade Technology`
  - [ ] `Worlds > Add Name` / `Delete Name` / `Liberate` / `Self-Destruct`
- **No post-conquest world-list report.** `ATTACK.PAS: ConquerEmpire`'s own `Booty` (the set of
  world indices that joined the conqueror mid-`ConquerEmpire`) is declared and threaded through
  real Pascal's call chain but never read anywhere in it — dropped entirely by this port, confirmed
  by reading (`CombatOutcome.cs`'s own doc comment). `ATTCOMM.PAS: EmpireConquestReport` (the screen
  that would list those worlds after a capital-conquering attack) is the one real consumer of that
  data; building it means un-dropping `Booty` tracking first, not just adding a screen.
- **Production window preview underestimates an Industrial Complex starbase's own output.**
  `WorldProductionPreview.Compute` (the Production tab's "Next Tick" projection) runs the real
  production pipeline against a throwaway clone, but never runs SupplyLink/SurplusLink (an
  Industrial Complex starbase's own cargo exchange with adjacent same-empire planets) — enabling it
  there would mean also cloning that starbase's real neighbors, since real SupplyLink/SurplusLink
  mutate them directly, and a preview screen mutating other real worlds as a side effect of being
  opened isn't acceptable. See that class's own doc comment.
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
