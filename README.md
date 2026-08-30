# AnacreonReconstruction4021

A from-scratch C# port of the DOS 4X game *Anacreon* (targeting the 1.31 release), reconstructed
directly from its original Turbo Pascal source (`reference/DOSAnacreonSource131/`).

The defining constraint of this project: every game-logic decision is checked against the real
Pascal, not just against what seems plausible. `reference/verify/` builds and runs the actual
original source under FreePascal and compares its output to the C# port's, so formulas, RNG
sequencing, and edge-case behavior trace back to what the original game actually did rather than a
plausible-sounding guess.

## Status

Core simulation (economy, galaxy/scenario setup, probes, news, combat), Kingdom NPE AI, and save/load
(DOS `.SAV` import plus a native JSON format) are done. A playable UI hasn't started — the current
`dotnet run` entry point is a placeholder, not a playable game yet. See
[`docs/ROADMAP.md`](docs/ROADMAP.md) for how this was built, and [`docs/OPEN_GAPS.md`](docs/OPEN_GAPS.md)
for known limitations in what exists so far.

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). There's no solution file — build
or test a specific project directly from the repo root:

```
dotnet build src/ThreeLn.Reconstruction4021
dotnet test src/ThreeLn.Reconstruction4021.Tests
```

Most tests run against the C# port alone. A subset compares against real compiled Pascal output
and needs [FreePascal](https://www.freepascal.org/) (`fpc`) and `git` on `PATH` — those tests skip
themselves automatically (with a clear reason) when either tool isn't available, so `dotnet test`
still runs cleanly without them. See [`reference/verify/README.md`](reference/verify/README.md)
for how that comparison works and what it takes to add to it.

## Repository layout

- **`src/`** — the C# port. `ThreeLn.Reconstruction4021.Core` is the simulation itself;
  `ThreeLn.Reconstruction4021` is the (currently placeholder) entry point; `*.Tests` is everything
  else, including the Pascal ground-truth harness.
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
- **[`docs/TUI_LIBRARY_RECOMMENDATION.md`](docs/TUI_LIBRARY_RECOMMENDATION.md)** — the Phase 8 UI
  library choice.
- **[`docs/AnacreonManual.md`](docs/AnacreonManual.md)** — the original player-facing manual.
