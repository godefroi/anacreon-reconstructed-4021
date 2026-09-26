# Notes for agents

This repo is a C# port of Anacreon 4021. The original Turbo Pascal source is the spec. Most
questions about whether the port is correct come down to comparing it against that source, so this
file is mostly about how to do that comparison well.

## Where the truth lives

- `reference/DOSAnacreonSource131/` is the pristine v1.31 Pascal source and the behavior the port
  targets. Never edit it. `reference/DOSAnacreonSource20/` is v2, which has bug fixes but also
  balance changes and new features; don't pull behavior from it without an explicit decision
  (`development/PASCAL_V1_VS_V2_DIFF.md` lists the differences).
- C# code cites its Pascal origin as `FILE.PAS:start-end` in comments. Treat those as pointers,
  not proof: open the cited lines and read them yourself, because a port can copy the shape of the
  wrong routine. When two Pascal routines look alike, check each one separately rather than
  assuming the port handled them the same way.
- The Pascal files are CP437. The Read/Edit tools can corrupt extended-ASCII bytes, so read them
  freely but don't edit near such bytes with text tools.
- `reference/verify/query-callgraph.ps1` answers "who calls what" in the Pascal source. Several
  units define routines with the same name, so check which unit a hit belongs to.
- `development/PASCAL_ARCHITECTURE_NOTES.md` and `development/PORT_DESIGN.md` explain design
  decisions and deliberate deviations. Check them before calling something a bug.

## Golden-file ground truth

Expected values in the Pascal ground-truth tests come from running the real Pascal, never from
hand-typed literals.

- `reference/verify/build.ps1` copies the pristine source into `reference/verify/patched/`
  (disposable, gitignored), applies `reference/verify/patches/*.patch`, and compiles
  `runworld.pas` with FreePascal. The patches are the source of truth; never edit `patched/`
  directly. Read `reference/verify/README.md` before changing patches or the harness.
- `runworld.pas` is one CLI with a *domain* per area: `runworld case <domain> <tuple> <tuple>...`,
  printing one `key=value;...` line per tuple. Each domain builds a minimal `Universe^` by hand and
  calls a real Pascal routine, usually the real `UpdateWorld`.
- On the C# side, each domain has a `*Cases.cs` file in `src/Reconstructed4021.Tests/PascalGroundTruth/`
  holding the inputs only. `GoldenFileTests` formats each case into a tuple, runs the harness, and
  writes `reference/verify/golden/<domain>.golden`. A `MatchesGoldenFile` test (look for
  `[DependsOn<GoldenFileTests>(..., ProceedOnFailure = true)]`) then runs the same case through
  the port and compares. New golden-comparing tests need `ProceedOnFailure = true` too; without it
  they're skipped whenever regeneration is.
- Regeneration only happens when `fpc` and `git` are on PATH. Without them, `RegenerateAllGoldenFiles`
  shows as skipped (naming the missing tool) and the comparisons run against the committed golden
  files. A skipped regenerator therefore means the run didn't check any harness change you made.
  Commit regenerated golden files along with the change that produced them.
- A golden file checks only the fields its domain prints *and* its C# test asserts. Before trusting
  a green test for some quantity, check that the quantity is actually emitted and asserted. Gaps
  here are how real bugs survive.
- Each harness line is one case; `GoldenFile.Regenerate` expects exactly one output line per case.

### Making the two sides agree on setup

Most harness disagreements turn out to be setup differences, not formula bugs. Check these first:

- The Pascal side starts from a `FillChar`-zeroed universe, so any field a domain doesn't set is 0.
  Zero doesn't always match the C# default. For example, a zeroed ISSP word reads as the lowest
  self-sufficiency dial, while C# planets default to the normal setting.
- Technology: a Pascal domain may grant the empire the full Technology set while the C# test's
  `Empire` starts with nothing researched. Ships and defenses are gated per-empire in C#, so the
  C# test has to unlock them explicitly to match.
- Enums: the C# enums follow Pascal declaration order, which is why domains pass `(int)` casts.
  Confirm this for any new enum before relying on it.
- RNG: harnesses force a fixed random value, and C# tests use `FixedRandom` to match. Existing
  cases show the pairing.
- Scope: `UpdateWorld` runs the whole per-world tick, while C# `RunAnnualTick` also runs
  empire-level steps (research, tech advancement) that a single-world Pascal domain never calls.
  Multi-year comparisons can drift for that reason alone, so keep them short or rule it out.
- Resource arithmetic: Pascal cargo is clamped to 0..9999 and uses 16-bit types in places; inputs
  above that range behave differently on each side.

## Diagnosing a gameplay complaint ("the numbers feel wrong")

1. Look at real state. Player saves are JSON under `saves/` (manual) and `saves/auto/` (autosaves,
   one per year, pruned to the most recent few). Neither is committed. Tabulate one empire's worlds
   across years with `scripts/saves.ps1` (see Tools below) before theorizing. News isn't kept in
   saves after the turn, so you can't count headline types from them.
2. `shortfallsLastTick` merges shortages from several consumers (industry growth, ship production,
   defenses). A shortage there doesn't tell you which one ran short.
3. Separate "design" from "bug". Work out the intended production/consumption balance from the
   Pascal constants before assuming the port is wrong; the original economy is deliberately tight.
4. For a real check, run the exact world from a save through both Pascal and `RunAnnualTick` for
   several years with `scripts/WorldDiff.cs` (see Tools below). The first field and year that
   disagree usually point straight at the routine. To keep a world as a regression case, add it to
   `MaturationCases`.
5. Whatever fix you land, add a case that fails without it, and confirm that it does fail.

## Tools

- **`scripts/saves.ps1 -Empire <name>`** prints one CSV row per owned planet per save year (the
  newest file wins when a year has several). Nested save objects become `<object>.<field>` columns,
  named from the save itself. `-Location x,y`, `-Year`, and `-Columns` (wildcards, such as
  `industry.*`) narrow it down; `-AsObject` emits objects for `Format-Table`/`Where-Object`.
  Run `Get-Help ./scripts/saves.ps1 -Examples` for usage.
- **`dotnet run scripts/WorldDiff.cs -- <save.json> <x,y> [years] [--all]`** runs one planet from
  a JSON save through the Pascal harness's `maturation` domain and through the port, year by year,
  and prints the first year with differing fields (`--all` prints every differing year). Exit code
  0 means identical. It rebuilds the Pascal harness itself when `runworld.pas`, a patch, or a shim is
  newer than the exe. Both sides use the harness's model rather than the saved game's: the world
  alone, as its own capital, everything researched, ships and revolution index at 0. So it checks
  the per-world tick, not how the game would have played out. It compares only the fields the
  `maturation` domain prints; if you need another field, add it to both that domain's output and
  the script's snapshot.
- **Test filtering:** TUnit runs on Microsoft.Testing.Platform, which filters by class or test
  name. Pass `--treenode-filter '/*/*/<ClassName>/*'` (or `.../<ClassName>/<TestName>`) to
  `dotnet test`, or to `dotnet run` on the test project; the Roslyn MCP test tools can run tests
  too. On a failure, TUnit prints "Expected ..." and "but found ..." on separate lines, so read
  enough lines to see both.

## Practical gotchas

- If you revert a file to test a fix and then restore it with `Copy-Item`, the old timestamp is
  kept and MSBuild may skip recompiling. Touch the file before rebuilding, or you'll be testing
  stale binaries.
- `dotnet build` of the test project doesn't rebuild the Tui or TuiDriver projects.
- Starbases and planets share the economy code through `IEconomicWorld` but differ in several
  Pascal-level details (ISSP, trillum reserves, which steps run). Check both when changing shared
  economy code.
