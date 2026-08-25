# AnacreonReconstruction4021

A from-scratch C# port of the DOS 4X game *Anacreon* (targeting the 1.31 release), reconstructed
directly from its original Turbo Pascal source (`reference/DOSAnacreonSource131/`). See
`docs/ROADMAP.md` for current phase status and design decisions, and `reference/verify/README.md`
for how ground-truth (real Pascal output) is used to verify the port.

## Ideas noticed but not chased down

A living list of things spotted incidentally while working on something else — not an audit, just a
place to write down a lead before it's forgotten. Light on detail where detail hasn't been derived.

### Probes may once have persisted at their destination instead of resolving instantly

`ProbeStatus` (`TYPES.PAS:125`) declares four states — `PReady`, `PInTrans`, `PAtDest`, `PLost` — but
the shipped 1.31 (and 2.0) behavior only ever reaches the first two. `UpdateProbes`
(`INTRFACE.PAS:1346-1359`) resolves an in-transit probe in a single call (scout, then straight back to
`PReady`); `PAtDest`/`PLost` are never assigned anywhere in either source tree, and `ProbesReturn`
(`INTRFACE.PAS:1361-1370`, the one procedure that reads `PAtDest`) is never called in either tree
either — checked both directly, not assumed from one.

The shape is suggestive: a design where a probe might take multiple turns to arrive, then sit at its
destination (continuing to scout, or awaiting recall) before returning, would need exactly this
four-state enum. Whether that was ever implemented, planned, or removed before either shipped source
tree isn't something source alone can answer — this is inference from dead code's shape, not a
confirmed history. Not ported (see `docs/ROADMAP.md`'s Phase 3), and not planned unless it resurfaces
as something worth reviving deliberately.

### An earlier, simpler combat-resolution design predates the group/shell system

While scoping Phase 5 (Combat), `BATTLE.PAS` (`CalcAttackRound`/`CalcMilitaryPower`) and `BOMBER.PAS`
(`Bombing1stPhase`/`Bombing2ndPhase`) turned up as dead code — grepped for callers across the entire
1.31 source tree, and nothing outside those two files calls into either one. `Bombing2ndPhase`'s own
body is an empty stub (`BEGIN END`), and `Bombing1stPhase` (which does have a real body, calling into
`BATTLE.PAS`) is itself never called by anything.

The shape here is a much simpler, non-group-based "quick resolve" combat formula — a single
attacker-vs-defender military-power ratio, no orbital shells, no per-ship-type targeting — next to
the real, live combat engine (`ATTACK.PAS`'s group/shell system: `GroupRecord`s advancing through
`ShellPosition`s, `CombatTable`-driven per-type targeting, round-by-round resolution). Reads like an
earlier or alternate design for resolving an attack that was superseded once the group/shell system
was built, left in the tree rather than deleted. As with the probe-persistence lead above, this is
inference from dead code's shape, not confirmed history. Not ported (see `docs/ROADMAP.md`'s
Phase 5) — the live `ATTACK.PAS` engine is the one this port implements.

## Known limitation: scenario golden-file testing can't be bit-exact, and why

`ScenarioLoaderGoldenTests` (loading a real `.SCN` file end to end through the C# `ScenarioLoader`)
only asserts fields that never involve a random draw. Every field derived from `Rnd()`/`RndVar()` —
planet coordinates, population, trillum, ships/cargo/defenses, world class/tech, nebula cell count,
even starbase population/efficiency — is deliberately excluded from exact-match comparison. This
was not the original design; it's the outcome of a real investigation, recorded here so nobody has
to redo it.

### What looked like memory corruption, and wasn't

While chasing a small `sumpop`/`sumtri` mismatch on 10 of 11 real `dos_131/*.SCN` files (Phase 2
commit 2e), a "fix" to `CreateRndPlanet`'s `MI:=Round(MI*RndMilTechAdj[T])` (storing the
intermediate `Real` in a variable before rounding, to match C#'s `double` result) caused a much
larger divergence in an unrelated file (`AFTERMAT.SCN`): wrong coordinates, wrong nebula cell
counts — fields with no floating-point involvement at all. This looked exactly like memory
corruption or an FPU register-stack bug, and was bisected as such for some time: ruled out "any new
local variable" (an unused one changed nothing), ruled out the optimizer (`-O-` reproduced it
identically), narrowed the divergence to exactly planet index 18 in that file (the first
`CreateRandomWorlds`-generated planet, right after the file's explicit `CreateWorld` commands run
out).

The real mechanism, once found, is mundane: `INT.PAS`'s `Rnd(Min,Max)` returns `Min` **without
drawing** whenever `Max<=Min`, and `RndVar`'s `temp1:=Trunc(Value*(Variation/100))` truncates to
`0` for many small `Value`s at 20% variation — so a ±1 change in `MI` can flip several of
`SetUpWorld`'s 18 `RndVar` calls between "skip the draw" and "take it." Every world/scenario in this
codebase shares **one** PRNG stream, so changing how many draws one planet consumes reshuffles every
subsequent draw for every remaining planet and the nebula generator. That's not corruption; it's the
ordinary, expected behavior of a shared-stream PRNG when anything upstream changes the draw count.

### Why this can't be fixed by matching floating-point precision

The immediate cause of the `MI`/`Pop` mismatch is that fpc's default i386 codegen keeps chained
`Real` expressions in the x87 FPU's 80-bit extended-precision register stack until a value is
explicitly stored, while C#'s `double` is always a strict 64-bit IEEE754 value — so the same
borderline expression (one whose exact mathematical result sits very close to an integer) can
`Trunc`/`Round` to a different integer in each language.

Forcing fpc to compile the ground-truth harness with `-CfSSE2` (strict 64-bit double, matching C#)
was tried as a fix (`reference/verify/build.ps1`, `PatchHarness.cs` — this flag is now permanent).
Verified via `git diff --stat` on the regenerated golden files that it changed none of the 13 golden
domains that existed at the time of the switch — but that's a snapshot, not a guarantee: the flag
changes float semantics harness-wide, so a future domain with its own borderline `Real` expression
will get different ground truth under it than it would have under fpc's default x87 mode. It's kept
anyway as a deliberate baseline choice, not a no-op: aligning the harness with the only precision C#
has removes a whole class of future "which precision does this quirky expression round under"
questions, even though it doesn't and can't make the scenario domain bit-exact: it eliminates the original `MI`/`Pop` boundary
cases, but introduces *different* ones (coordinate sums, e.g., started mismatching under `-CfSSE2`
where they hadn't before), because the C# `ScenarioLoader`/`GalaxySetup` and the Pascal source are
two independently-written implementations of the same formulas — even under identical IEEE754
double precision, a different operand evaluation order can differ by one ULP, which is enough to
flip a `Trunc`/`Round` at an exact-integer boundary. There is no floating-point precision setting
that makes two independently-written implementations of an RNG-cascading algorithm agree forever;
the fragility is structural, not a compiler-flag bug.

This was confirmed concretely, not just reasoned about: `AWAKEN.SCN`'s starbase population (created
by two explicit `CreateStarbase` commands) mismatched even though it's computed from just the 10
explicit `CreateWorld` population draws that precede it in the file — nowhere near the randomized
`CreateRandomWorlds` section. "Explicit command, not randomized generation" does not make a field
safe from this — anything downstream of *any* earlier `Rnd()`/`RndVar()` call in the same file is at
risk.

## Questions for George

George Moromisato, the original author, has offered to answer a limited number of questions about
the original design and implementation. This is a working list of what's actually worth asking —
real ambiguities the source alone can't resolve, not things a closer reading would answer. Grouped
by topic; each has a concrete hook back to the source or this port so the question is checkable
against the actual behavior, not just a vague memory prompt.

### Dead code — was it abandoned, or superseded, and by what?

- **`BATTLE.PAS`/`BOMBER.PAS`.** A whole second combat-resolution system — `CalcAttackRound`/
  `CalcMilitaryPower`, single attacker-vs-defender power ratio, no orbital shells, no per-ship-type
  targeting — sits next to the live `ATTACK.PAS` group/shell engine, and nothing in the 1.31 source
  tree calls into it (confirmed by grepping every caller). `Bombing2ndPhase` is an empty stub
  (`BEGIN END`); `Bombing1stPhase` has a real body but is itself never called. Was this an earlier
  design that `ATTACK.PAS` replaced, or a parallel "quick resolve" mode that was planned (e.g. for
  auto-resolving low-stakes fights) but never finished?
- **`Consolidate` (`ATTNPE.PAS`).** Declared, empty body, never called anywhere including from
  within its own unit. Its name suggests merging groups (maybe collapsing multiple surviving groups
  of the same `AttackType`/target after a round?) — was that the intent, and if so why was it never
  wired into `GroupEngage`'s round loop?
- **`SelfDestructObject` (`SBASE.PAS`).** No caller anywhere in the source tree, including
  `ATTCOMM.PAS`/`FLTCOMM.PAS`'s human-order compilers. Was there ever a player-facing "scuttle this
  starbase" command, or was it infrastructure for something else that got cut?
- **`ProbeStatus`'s unused `PAtDest`/`PLost` states** (`TYPES.PAS:125`, `INTRFACE.PAS:1346-1370`) —
  see "An earlier, simpler combat-resolution design" above for the combat analog; this is the same
  shape of question for probes. Was multi-turn probe travel (arrive, sit at destination, then
  return) ever implemented or shipped in an earlier build, or did it stay a plan the four-state enum
  outlived?
- **`AttackResultTypes`' `DefCapturedART`.** Declared alongside `AttDestroyedART`/`AttRetreatsART`/
  `DefConqueredART`, but never assigned anywhere in `ATTACK.PAS`/`ATTNPE.PAS` — and `ResolveAttack`'s
  own `CASE Result OF` has no branch for it at all. Was "captured" meant to be a distinct outcome
  from "conquered" (e.g. a fleet taken intact as a trophy rather than destroyed), and if so what
  stopped it from being finished?
- **`ATTNPE.PAS`'s debug combat window.** `{DEFINE Debug}` (line 7) is missing its leading `` $ ``,
  so it's an inert comment, not a real compiler directive — the `{$IFDEF Debug}`-gated `CRT`
  debug-window code (`ClrScr` calls scattered through the round loop) can never have compiled into
  any shipped build. Was that window a real debugging tool during development that got disabled on
  purpose before release, or is the missing `$` an accident that quietly turned it off?

### Combat resolution — deliberate rule, or incidental?

- **`AdvanceGroups`' troop-type hardcode** (`ATTACK.PAS:183-215`, this port's
  `Combat/CombatResolution.cs`). When a transport/jumptransport group reaches the ground with troops
  aboard, its `Trg` is unconditionally set to `Legion` — even when the cargo actually carried was
  `NinjaLegion`. Was landing ninja legions always meant to draw the same defensive-targeting
  response as regular legions, or is this a copy-paste of the more common case that never got a
  matching `NinjaLegion` branch?
- **`GetBestTarget`'s tie-break** (`ATTNPE.PAS:92-107`). On equal priority, the comparison is `>=`,
  so the *last* candidate scanned in `AttackTypes`' `LAM..nnj` order wins ties, not the first. Was
  that a deliberate bias (e.g. troop types sort last and should be preferred on a tie), or just
  whichever comparison operator happened to get typed?
- **`FleetRetreats`' unused `RetrIndex` parameter** (`ATTNPE.PAS:160-192`, threaded all the way
  through `NPEAttack`/`FleetEngage`/`WorldEngage`). The retreat rule itself is a hardcoded 30-round
  timeout — `RetrIndex` is never read anywhere in its own body. Was a configurable retreat
  threshold (e.g. per-empire aggressiveness, or a player-settable "fight to the last N rounds")
  planned but never connected, or is the parameter a leftover from an earlier version of the rule?

### Empire elimination and conquest

- **`ConquerEmpire`'s disabled starbase-recapture loop** (`ATTACK.PAS:1099-1118`, wrapped in
  `(* ... *)`). The commented-out block would have let a `CommandBase`/`Fortress` starbase become
  the conquered empire's new capital candidate, the same way the active planet loop above it does.
  Why was this disabled — a bug in the starbase branch specifically, a balance decision that
  starbases shouldn't ever become capitals, or just unfinished when the planet-only version shipped?
- **Human-empire defeat via the capital-sentinel trick** (`ATTACK.PAS:1120-1131`). Rather than
  calling `DestroyEmpire`, a human's `Capital` gets overwritten with `ObjTyp:=Void; Index:=Ord(Player)`
  to record the winner, and the empire otherwise stays in play with no capital. Was there ever a
  concrete plan for what a defeated human sees or can still do after this (game over screen,
  spectator mode, elimination from the turn order), or did that UI never get built before this
  point was reached?
- **`ConquerEmpire`'s magic distance/population thresholds** (`ATTACK.PAS:1044,1050,1056`) — worlds
  more than 10 sectors from the old capital and within 10 of the conqueror's automatically join if
  under ~1000 population; a high-revolution world over ~1000 population has a 75% chance to go
  independent instead; a nearby high-revolution world has a 60% chance to join anyway. Were these
  tuned against playtesting, or is there a simpler rule of thumb behind the specific numbers (e.g.
  "10 sectors" as a stand-in for realistic administrative/supply reach)?

### Formulas with a visible asymmetry

- **`GetOptimumIndus`'s missing 999 clamp** (`INTRFACE.PAS:1264-1287`, this port's construction
  commit). It reuses `GetIndustrialDistribution`'s own math to seed a freshly-built industrial
  complex's `Industry`, but — unlike every other caller of that formula
  (`GetIndustrialDistribution`/`UpdateIndustry` both clamp their result to 999) — never applies the
  same clamp. Is a new complex deliberately allowed to start above the normal per-tick production
  ceiling and decay down toward it, or is the missing clamp just an oversight that never mattered in
  practice (e.g. because the seeded value rarely exceeds 999 anyway)?
- **`NewCapital`'s asymmetric tech reset** (`ATTACK.PAS:1003-1019`, within `ConquerEmpire`). When an
  empire's new capital is *lower*-tech than its old one, the empire's technology set is reset to
  exactly `TechDev[NewTech]` (the new level's full set) immediately; when the new capital is
  *higher*-tech, it's reset to `TechDev[Pred(NewTech)]` — one level short of what the new capital
  itself has. Is the higher-tech case deliberately withholding the top level's techs until the
  normal `NewTechLevel` research roll grants them (consistent with how `NewTechLevel` never
  auto-grants a level's techs on advancing), or should both branches have matched?

### The actual target: fpc-compiled behavior, not the historical DOS binary

Worth naming explicitly, since it's easy to assume otherwise: the goal is for the C# port to match
what *this project's own patched Pascal source, compiled by the fpc we actually have*, produces —
not necessarily what the original 1990s Borland Turbo Pascal-compiled DOS binary produced. Turbo
Pascal's native `Real` is a 6-byte software-emulated format, a third rounding regime distinct from
both fpc's x87-extended and C#'s double — so there was never a realistic path to historical
bit-exactness here anyway, and it isn't a project goal. The C# port also doesn't need to be
RNG-stream-compatible with the Pascal harness for gameplay purposes; bit-exact bug-for-bug behavior
is a testing-fidelity nice-to-have, not a correctness requirement, given the end goal is a playable
game, not a historical reproduction.

### What's actually verified, and where

- **Formula-level correctness** for the randomized generation paths (`CreateRndPlanet`,
  `RandomTrillumReserves`, `NebulaeBand`/`NebulaePatches`) is covered by the dedicated Phase 2d
  domain tests (`randomplanet.golden`, `nebula.golden`, `trillumreserves.golden`), which use the
  `ForcedRandomValue` single-draw convention and don't chain into a real collision-retry loop — no
  RNG-cascade risk there.
- **Scenario-loader dispatch/structure correctness** (the actual thing `ScenarioLoaderGoldenTests`
  exists to check) is covered by exact-match assertions on fields that never involve a random draw:
  `year`, `planetcount`, `starbasecount`, `stargatecount`, `empirecount`, per-empire tech/revolution/
  modifier/empress summaries, and mined-cell count (`CreateSRMs` is a deterministic explicit
  rectangle, no RNG at all).
- **Everything else** (coordinates, population, trillum, ships/cargo/defenses, class/tech, nebula
  cell count, starbase population/efficiency) gets a cheap smoke test instead: bounds derived from
  type/domain invariants (a coordinate can't exceed the galaxy's size, an enum ordinal can't exceed
  its cardinality, a resource count can't be negative) rather than from expected game-balance values,
  so they can catch a genuinely broken formula without ever producing a false failure on legitimate
  scenario content or drifting with RNG-stream position.

The only remaining lever anyone's identified for tighter parity — not attempted, and not recommended
as a promising path — is reordering/rewriting the C# formulas to match the Pascal source's exact
operand evaluation order term-for-term, on the theory that identical order plus identical precision
might eliminate the ULP-level differences that flip boundary cases. This is a task of unbounded cost,
not a bounded fix: it would need doing per-formula, verifying term order against two different
`Trunc` implementations, in a system where a single remaining mismatch anywhere still cascades
through the entire shared RNG stream. Given the stated goals above, it isn't worth attempting.
