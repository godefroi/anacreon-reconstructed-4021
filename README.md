# AnacreonReconstruction4021

A from-scratch C# port of the DOS 4X game *Anacreon* (targeting the 1.31 release), reconstructed
directly from its original Turbo Pascal source (`reference/DOSAnacreonSource131/`).

## Documentation map

- **[`docs/ROADMAP.md`](docs/ROADMAP.md)** — phase/commit status: what's done, what's in progress,
  what's next.
- **[`docs/PORT_DESIGN.md`](docs/PORT_DESIGN.md)** — this port's own cross-cutting design decisions:
  how Pascal's data/behavior gets modeled in C# and why (RNG strategy, entity/interface shapes, the
  empire-elimination model, ground-truth harness methodology).
- **[`docs/PASCAL_ARCHITECTURE_NOTES.md`](docs/PASCAL_ARCHITECTURE_NOTES.md)** — a map of the
  *original* Pascal source (file-by-file, subsystem-by-subsystem), plus findings turned up while
  porting: dead code, quirks, real bugs, and the scenario golden-file investigation.
- **[`docs/QUESTIONS_FOR_GEORGE.md`](docs/QUESTIONS_FOR_GEORGE.md)** — a working list of real
  ambiguities in the source for the original author to weigh in on.
- **[`docs/PASCAL_V1_VS_V2_DIFF.md`](docs/PASCAL_V1_VS_V2_DIFF.md)** — what changed between the 1.31
  and 2.0 Pascal source trees (bugfixes vs. opt-in gameplay/feature changes).
- **[`docs/TUI_LIBRARY_RECOMMENDATION.md`](docs/TUI_LIBRARY_RECOMMENDATION.md)** — the Phase 8 UI
  library choice.
- **[`docs/AnacreonManual.md`](docs/AnacreonManual.md)** — the original player-facing manual.
- **[`reference/verify/README.md`](reference/verify/README.md)** — how ground-truth (real, compiled
  Pascal output) is used to verify the port; read this before touching the harness itself.
