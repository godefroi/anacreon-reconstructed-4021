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
