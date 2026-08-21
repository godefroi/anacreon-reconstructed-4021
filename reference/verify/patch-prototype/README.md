# Patch-based Pascal ground truth: prototype and findings

## What this is

An alternative to the per-procedure transcription pattern used by
`reference/verify/*.pas` (see `docs/ROADMAP.md`'s "Ground-truth harness
generation" section for that baseline). Instead of hand-transcribing a
procedure into a fresh file, this maintains small patches against the real
`reference/DOSAnacreonSource131/*.PAS` source, applies them to a disposable
copy at build time, and calls the real, only-minimally-touched Pascal code
directly against a hand-assembled `Universe^`.

**Status: validated proof of concept, not a replacement for the existing
harnesses.** See "Recommendation" below.

## Layout

- `patches/*.PAS.patch` — unified diffs against the matching pristine file in
  `reference/DOSAnacreonSource131/`. This is the maintained artifact.
  `reference/DOSAnacreonSource131/` itself is never edited.
- `runworld.pas` — a driver program (not a patch target, a genuinely new
  file): assembles a minimal 2-planet `Universe^` and calls the real
  `UpdateWorld`.
- `build.ps1` — deletes and regenerates `pascal/` from pristine source +
  patches, then compiles `runworld.pas`. Run it, then run
  `.\pascal\runworld.exe`.
- `pascal/` — disposable build output, gitignored, never a source of truth.
  If you need to iterate on a patch: run `build.ps1`, edit the file directly
  under `pascal/`, verify it compiles/runs, then regenerate that file's
  `.patch` from the diff against the pristine original and overwrite it in
  `patches/`. Never hand-edit a `.patch` file.

## What it took to get UpdateWorld callable

`UPDATE.PAS`'s own `USES` clause lists 14 units; `Intrface` alone further
pulls in `Fleet`/`Orders`/`NPE`. Every blocker hit while getting
`UpdateWorld` (and everything it actually calls) to compile turned out to be
small and mechanical, not a case of "reconstruct a DOS UI stack":

- **Dead UI/config code, deleted outright.** `Environ`'s `FeatureInActive`
  (a demo-nag dialog) and `LoadConfiguration`/`SaveConfiguration` (directory
  prefs), `UPDATE.PAS`'s `UpdateUniverse` (the whole-galaxy tick loop plus its
  screen progress window — the *only* `Crt`/`WND` usage in the entire unit).
  None of it is reachable from `UpdateWorld`.
- **Trivial I/O helpers, duplicated instead of importing a whole unit for
  them.** `WriteVariable`/`ReadVariable` (used by `Galaxy`'s
  `SaveSector`/`LoadSector`, `Environ`'s `LoadEnvironment`/`SaveEnvironment`,
  and `News`'s `LoadNewsData`/`SaveNewsData`) are just
  `BlockRead`/`BlockWrite`+`IOResult` — no real coupling to the `Dos2` unit
  they live in, which pulls in `Printer`/`CRT`/`DOS`/`EIO`/`WND`/`Menu`. Each
  of those three save/load pairs was kept **verbatim** (this is real, wanted
  logic — see "the save/load angle" below) with its own tiny local copy of
  the two helpers instead.
- **Turbo Pascal-isms with an obvious modern equivalent.** `STRG.PAS`'s
  `AllUpCase` used raw 8086 opcodes via `Inline(...)` — replaced with a
  2-line `UpCase` loop. `PRIMINTR.PAS`'s `GetNewName` gated allocation on
  `MaxAvail` (TP's real-mode heap-free check, meaningless under virtual
  memory) — replaced with `IF True THEN`. Several fixed-length-string
  comparisons needed `{$V-}` (fpc's default `$V+` is stricter than TP about
  exact string-length matching).
- **One relocated (not rewritten) procedure.** `GetIndustrialDistribution` is
  pure economy math (only calls `PrimIntr` getters and `Misc`/`DataCnst`
  tables) but lives in `INTRFACE.PAS`, which would drag in `Fleet`/`Orders`/
  `NPE` for one function. Moved verbatim into `UPDATE.PAS` — a real patch
  spanning two files (delete from one, add to the other), documented as such
  in the patch comments rather than silently dropping the provenance.
- **A test-only RNG override**, added to `INT.PAS`: `ForcedRandomValue`,
  when `>=0`, makes every `Rnd(Min,Max)` call return `Min+ForcedRandomValue`
  — deliberately matching the existing C# harnesses' `FixedRandom`/
  `RngFixedValue` convention exactly, so results are comparable to existing
  golden files. `-1` (default) means "use the real RNG," unchanged.

## A real landmine: `ABSOLUTE` overlays compile but lie

`DATASTRC.PAS:235`: `GlobalSets: GlobalSetsRecord ABSOLUTE SetOfActiveFleets;`
overlays a whole record onto the memory address of `SetOfActiveFleets`,
relying on several separately-declared globals in `TYPES.PAS:180-188`
(`SetOfActiveFleets`, `SetOfFleetsOf`, `SetOfActivePlanets`, `SetOfPlanetsOf`,
etc.) being laid out contiguously in declaration order — Turbo Pascal's
segment-based layout, not something any modern compiler guarantees. `fpc`
accepts the syntax silently. Writing through `GlobalSets.X` in `runworld.pas`
corrupted the `Universe` pointer itself (a genuine access violation, runtime
error 216) with no compile-time warning. Fix: never write through
`GlobalSets`; use the real standalone vars directly. This is exactly the
"compiles fine, wrong at runtime" failure mode that makes this whole approach
riskier than transcription in a way that isn't just about compile effort —
worth remembering if this pattern gets reused elsewhere in the source.

## The save/load angle

The original question this prototype answered: could test fixtures be built
by driving Pascal's *own* save/load machinery instead of hand-writing field
assignments? Two things worth knowing, found but not used yet:

- `LOADSAVE.PAS`'s `InitializeUniverse(StartingYear, Size, Planets)` zero-
  inits the whole `Universe^` and sets planet count — a better starting point
  than a hand-rolled `FillChar`, but `LOADSAVE.PAS`'s own dependency list
  (`Dos2, Intrface->Fleet/Orders/NPE, News, Mess, TMA, Environ, NPETypes,
  NPE, Galaxy, Orders, Fleet`) is much larger than what `UpdateWorld` alone
  needs, so pulling it in wasn't justified for this prototype's scope.
- `LOADSAVE.PAS`'s `LoadGame`/`SaveGame` are the real binary `.SAV` format
  round-trip (`SFSignature = 'Anacreon save file v1.3'`). No `.SAV` file
  ships with the source, so building fixtures this way would mean either
  capturing one from real gameplay (not available) or hand-authoring the
  binary layout — not obviously cheaper than direct field assignment for
  small scenarios.

Neither was pulled into this prototype; `runworld.pas` does its own minimal
`New(Universe); FillChar(Universe^,SizeOf(Universe^),0);` instead. Revisit if
a future scenario needs a much larger/more realistic starting `Universe^`
than a couple of hand-set fields can reasonably cover.

## Validation

`runworld.pas` reproduces `TechLevelCases.OwnedWorldBehindCapitalAdvances`
(Tech=Warp, owned, capital ahead at Jump, `RngFixedValue=0`) by calling the
real `UpdateWorld` against a hand-assembled 2-planet `Universe^`:

```
techlevel=6        # matches techlevel.golden's OwnedWorldBehindCapitalAdvances exactly
population=12      # Pop 10 -> 12: UpdatePopulation's "<75" branch, Rnd(2,5) forced to 2
legions=0
revindex=0
```

Rebuilt from scratch (`build.ps1` deletes `pascal/`, reapplies every patch,
recompiles) and reran with identical results — the patches are complete and
sufficient on their own, not dependent on whatever `pascal/` happened to
contain from prior manual edits.

## Recommendation

Keep this as a **second, narrower lane**, not a replacement for
`reference/verify/*.pas`'s per-procedure transcription:

- Transcription stays the default for new isolated-procedure golden cases —
  it's cheap, bounded, and already proven across four roadmap commits.
- This patch-based, real-`Universe^` approach is worth reaching for when a
  procedure's fidelity risk is high enough to justify it: many state-shaped
  lookups (`GetCapital`/`GetTech`-style calls that transcription would
  otherwise have to simplify into plain parameters), or when the thing worth
  testing is call *ordering* across multiple steps in the same real pipeline
  — exactly the "cross-cutting field" bug class hit twice this session with
  transcription (`UpdateMilitary` mutating `Cargo.Legions` before
  `UpdateRevolution` reads it, discovered only because the isolated harnesses
  didn't model the mutation).
- Don't expand this into combat/fleet movement/NPE AI territory by default.
  `Intrface`'s `Fleet`/`Orders`/`NPE` dependency was dodged here by relocating
  one function; the next subsystem's dependency web is an open question, not
  something this session's results generalize to. Treat each new area as its
  own exploration, not an assumed extension of this one.
