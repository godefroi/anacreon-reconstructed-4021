# AnacreonReconstructed4021

A from-scratch C# port of the DOS 4X game *Anacreon* (targeting the 1.31 release), reconstructed
directly from its original Turbo Pascal source (`reference/DOSAnacreonSource131/`).

The goal is a fun, playable game that's true to the original 1.31 release, not a byte-for-byte
simulation exercise for its own sake. Once the faithful baseline is solid, the plan is to layer in
optional, opt-in features on top of it, and the game itself should feel modern to actually sit down
and play, not just correct on paper.

Getting the original right still matters a lot, and it's the more interesting part of this project
from a port-design angle: `reference/verify/` builds and runs the actual original source under
FreePascal and compares its output against the C# port's, so game-logic decisions trace back to what
the original game actually did instead of a plausible-sounding guess.

## Status

Core simulation (economy, galaxy/scenario setup, probes, news, combat), Kingdom NPE AI, and save/load
(DOS `.SAV` import plus a native JSON format) are done. A Terminal.Gui interface
(`src/Reconstructed4021.Tui`) is under construction — the galaxy map, navigation shell,
startup/title screens, and New Game flow work; the human `ITurnHandler` and most in-game command
screens (fleet orders, combat, construction, etc.) don't exist yet, and `dotnet run` on the
placeholder `Reconstructed4021` entry point still isn't a playable game. See
[`docs/ROADMAP.md`](docs/ROADMAP.md) for how this was built, and [`docs/OPEN_GAPS.md`](docs/OPEN_GAPS.md)
for known limitations in what exists so far.

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). There's no solution file — build
or test a specific project directly from the repo root:

```
dotnet build src/Reconstructed4021
dotnet test src/Reconstructed4021.Tests
```

Most tests run against the C# port alone. A subset compares against real compiled Pascal output
and needs [FreePascal](https://www.freepascal.org/) (`fpc`) and `git` on `PATH` — those tests skip
themselves automatically (with a clear reason) when either tool isn't available, so `dotnet test`
still runs cleanly without them. See [`reference/verify/README.md`](reference/verify/README.md)
for how that comparison works and what it takes to add to it.

## Known issues

**TUI feels laggy on Windows (keystrokes/redraws take 100ms+ to show up):** this has been traced
to Windows Terminal's own rendering pipeline, not the app -- Terminal.Gui tracks it upstream as
[tui-cs/Terminal.Gui#4588](https://github.com/tui-cs/Terminal.Gui/issues/4588) (open; the one fix
attempt, [#4589](https://github.com/tui-cs/Terminal.Gui/pull/4589), was closed unmerged). Switching
Windows Terminal's text renderer from its default (DirectX 11/AtlasEngine) to Direct2D
(Settings -> Rendering) has resolved it in practice. See also
`src/Reconstructed4021.Tui/Program.cs`'s own notes on a related
ConPTY tearing issue ([#5323](https://github.com/tui-cs/Terminal.Gui/issues/5323)).

**TUI froze for several seconds after Windows Terminal was minimized/backgrounded, or after the PC
woke from sleep (fixed):** reproduced under both the default renderer and Direct2D, and didn't
reproduce under `conhost.exe` or on screens that redraw continuously while idle (e.g. the main
menu's orbit animation) -- only ones that only redraw in response to input (e.g. the galaxy map),
pointing at Windows Terminal/ConPTY deferring or throttling a backgrounded session's servicing
rather than a bug in this app or Terminal.Gui (see issue #1). Giving the galaxy map the same kind
of continuous-idle redraw the main menu already had -- a low-frequency
`Application.AddTimeout` heartbeat in `GameShell` -- resolved it in testing.

## Repository layout

- **`src/`** — the C# port. `Reconstructed4021.Core` is the simulation itself;
  `Reconstructed4021.Tui` is the Terminal.Gui interface (in progress);
  `Reconstructed4021` is the (currently placeholder) entry point; `*.Tests` is everything
  else, including the Pascal ground-truth harness; `Reconstructed4021.TuiDriver` is a headless
  driver for exercising the Tui end-to-end without a real terminal — see its own
  [README](src/Reconstructed4021.TuiDriver/README.md).
- **`reference/DOSAnacreonSource131/`** — the pristine, unmodified 1.31 Turbo Pascal source. Never
  edited directly — see `reference/verify/README.md` for how changes to it are made (as patches,
  not in place).
- **`reference/DOSAnacreonSource20/`** — the 2.0 Pascal source tree, consulted only to distinguish
  real bugfixes from opt-in gameplay/feature changes (see `docs/PASCAL_V1_VS_V2_DIFF.md`) — 1.31 is
  canonical for this port.
- **`reference/verify/`** — the ground-truth harness: a patched, compilable copy of the pristine
  source plus the driver/tooling that runs it and compares its output to the C# port's.
- **`reference/scenarios/`**, **`reference/saves/`** — real `.SCN` scenario files and a real `.SAV`
  save file, both used as ground-truth fixtures.
- **`docs/`** — design and reference documentation, see the map below.

## Documentation map

- **[`docs/ROADMAP.md`](docs/ROADMAP.md)** — a development log: how this port was built, bottom-up,
  simulation core first.
- **[`docs/PORT_DESIGN.md`](docs/PORT_DESIGN.md)** — this port's own cross-cutting design decisions:
  how Pascal's data/behavior gets modeled in C# and why (RNG strategy, entity/interface shapes, the
  empire-elimination model, ground-truth harness methodology).
- **[`docs/PASCAL_ARCHITECTURE_NOTES.md`](docs/PASCAL_ARCHITECTURE_NOTES.md)** — a map of the
  *original* Pascal source (file-by-file, subsystem-by-subsystem), plus findings turned up while
  porting: dead code, quirks, real bugs, and the scenario golden-file investigation.
- **[`docs/OPEN_GAPS.md`](docs/OPEN_GAPS.md)** — known limitations in this port: places where the C#
  doesn't yet do everything the original Pascal did.
- **[`reference/verify/README.md`](reference/verify/README.md)** — how ground-truth (real, compiled
  Pascal output) is used to verify the port: what gets patched and why, the ground-truth domain
  catalog, known landmines. Read this before touching the harness itself.
- **[`docs/QUESTIONS_FOR_GEORGE.md`](docs/QUESTIONS_FOR_GEORGE.md)** — a working list of real
  ambiguities in the source for the original author to weigh in on.
- **[`docs/PASCAL_V1_VS_V2_DIFF.md`](docs/PASCAL_V1_VS_V2_DIFF.md)** — what changed between the 1.31
  and 2.0 Pascal source trees (bugfixes vs. opt-in gameplay/feature changes).
- **[`docs/TUI_LIBRARY_RECOMMENDATION.md`](docs/TUI_LIBRARY_RECOMMENDATION.md)** — why Terminal.Gui
  v2 was chosen for the interactive UI.
- **[`docs/TUI_SURFACES_MAPPING.md`](docs/TUI_SURFACES_MAPPING.md)** — every player-facing window,
  menu, dialog, and editor in the original game, mapped to its Pascal source and the Terminal.Gui
  primitive it's built (or to be built) from.
- **[`docs/AnacreonManual.md`](docs/AnacreonManual.md)** — the original player-facing manual.
- **[`src/Reconstructed4021.TuiDriver/README.md`](src/Reconstructed4021.TuiDriver/README.md)** — how
  to drive the Tui headlessly (no pty) to debug, diagnose, and regression-test screens and fixes.
