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
(DOS `.SAV` import plus a native JSON format) are done. The interactive UI
(`src/Reconstructed4021.Tui`, built on `src/Reconstructed4021.Panemonde`, a from-scratch renderer —
see `docs/PORT_DESIGN.md`) covers every menu command, F-key report window, save/load path, combat
flow, and the pregame sequence; a handful of menu items remain stubs, tracked as GitHub issues. See
[`docs/ROADMAP.md`](docs/ROADMAP.md) for how this was built.

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). There's no solution file — build
or test a specific project directly from the repo root:

```
dotnet build src/Reconstructed4021.Tui
dotnet test src/Reconstructed4021.Tests
```

Most tests run against the C# port alone. A subset compares against real compiled Pascal output
through committed golden files under `reference/verify/golden/`. With
[FreePascal](https://www.freepascal.org/) (`fpc`) and `git` on `PATH`, a test run regenerates those
files from the real Pascal first. Without them, the regeneration test skips (naming the missing
tool), the comparisons run against the committed files, and the few tests that call Pascal directly
skip too. See [`reference/verify/README.md`](reference/verify/README.md) for how that comparison
works and what it takes to add to it.

### Investigation scripts

Two scripts help check the simulation against real play (both read the JSON saves the Tui writes
under `saves/` and `saves/auto/`):

- `scripts/saves.ps1 -Empire <name>` prints one CSV row per planet the empire owns per save year:
  population, industry, cargo, defenses, shortfalls, and so on. Narrow it with `-Location x,y`,
  `-Year`, and `-Columns` (wildcards, such as `industry.*`). See `Get-Help ./scripts/saves.ps1 -Examples`.
- `dotnet run scripts/WorldDiff.cs -- <save.json> <x,y> [years] [--all]` runs one planet through
  both the real Pascal `UpdateWorld` and the port for several years, and reports the first year and
  fields that differ. It needs `fpc` and `git`, and builds the Pascal harness when it's out of date.
  It models the world alone and fully researched, so it checks the per-world economy tick, not the
  whole game.

## Repository layout

- **`src/`** — the C# port. `Reconstructed4021.Core` is the simulation itself;
  `Reconstructed4021.Panemonde` is a from-scratch console rendering engine (see its own
  [README](src/Reconstructed4021.Panemonde/README.md)); `Reconstructed4021.Tui` is the interactive
  UI built on it; `Reconstructed4021.TuiDriver` is a headless driver for exercising Tui screens
  without a real terminal; `*.Tests` is everything else, including the Pascal ground-truth harness.
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
- **[`reference/verify/README.md`](reference/verify/README.md)** — how ground-truth (real, compiled
  Pascal output) is used to verify the port: what gets patched and why, the ground-truth domain
  catalog, known landmines. Read this before touching the harness itself.
- **[`docs/QUESTIONS_FOR_GEORGE.md`](docs/QUESTIONS_FOR_GEORGE.md)** — a working list of real
  ambiguities in the source for the original author to weigh in on.
- **[`docs/PASCAL_V1_VS_V2_DIFF.md`](docs/PASCAL_V1_VS_V2_DIFF.md)** — what changed between the 1.31
  and 2.0 Pascal source trees (bugfixes vs. opt-in gameplay/feature changes).
- **[`docs/TUI_LIBRARY_RECOMMENDATION.md`](docs/TUI_LIBRARY_RECOMMENDATION.md)** — why Terminal.Gui
  v2 was the initial choice for the interactive UI, and why it was later replaced (see
  `docs/PORT_DESIGN.md`'s "Presentation layer" section for the short version).
- **[`docs/AnacreonManual.md`](docs/AnacreonManual.md)** — the original player-facing manual.
- **[`src/Reconstructed4021.Panemonde/README.md`](src/Reconstructed4021.Panemonde/README.md)** — how
  the rendering engine underneath Tui works.
