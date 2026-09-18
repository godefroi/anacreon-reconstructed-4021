# Modeling a stronger NPE

A record of what we know about the ported NPE AI (Kingdom1/Kingdom2) and what it would take to
make it a genuinely competent opponent, following from the question "how fast can this game
actually be won."

## What we know about NPE behavior

**There's no enforced win condition.** Nothing in either the Pascal original or the C# port checks
"only one empire remains" and declares a winner. A scenario's suggested length (`.SCN`'s
`MinLen`/`MaxLen`, e.g. Intro's "50+ years," East-vs-West's "5-10 years") is read and discarded —
flavor text, not an enforced rule.

**Symmetric Kingdom-vs-Kingdom NPE combat essentially never finishes.** A harness
(`ScenarioPlayoutTests.cs`) plays a real scenario to elimination or a 500-year cap with every empire
driven by `KingdomTurnHandler`. Across 40 runs (Intro and East-vs-West, Kingdom1 and Kingdom2
personas, 10 seeds each), zero eliminations. The galaxy is not frozen, though: population grows
4x-40x, ship counts 100x-3,000x, industry 4x-30x per empire, almost entirely from conquering
independent worlds, never from finishing off a rival.

**This is real Pascal behavior, not a porting defect.** A matching harness was built on the Pascal
side: a new `npeplayout` domain in `reference/verify/runworld.pas`, driving the real
`ImplementKingdom1NPE`/`ImplementKingdom2NPE` (`NPE02.PAS`) through the same non-interactive
per-turn loop real Pascal uses for an all-NPE game, loaded via the existing `LoadScenario` call.
Result: 0/40 eliminations, same shape and magnitude of growth as the C# port. Getting this running
surfaced one genuine bug: `DestroyFleet` (`FLEET.PAS`) disposes a fleet's pointer without
re-nilling or reallocating it. Real Turbo Pascal's heap leaves that block harmlessly readable
afterward; `fpc`'s heap doesn't, and `EnforceNPEDataLinks` unconditionally dereferences all 240
fleet slots every NPE turn, so a long enough run reliably crashed around year 40-45. Fixed with a
minimal patch (`reference/verify/patches/FLEET.PAS.patch`) that reproduces real Pascal's guarantee
rather than changing any observable behavior.

**Asymmetric matchups are a different story.** Once one side's persona is tuned even a bit better
than a fixed opponent (see the genetic algorithm section below), real eliminations happen, some
within 120 years. So the game isn't structurally incapable of producing a winner — two AIs of
matched skill just stall each other out. What that means for whether a human can lose is still
open: we never tested a human-shaped strategy (or mistake) against this AI, never tested
Pirate/Berserker (narrower hostile-roamer personas, untested in either harness), and never tested
non-combat loss vectors like rebellion/`RevolutionIndex`-driven collapse. It's also not confirmed
whether human player slots in these scenarios have `LosesIfCapitalConquered` set — if they do, a
human's practical "loss" condition could be easier to trigger than anything measured here.

## The ML path, evaluated

An investigation into training an ML.NET model — tree-based, not a neural net — to imitate or
evaluate NPE decisions had already been undertaken. That general shape still holds up: this game's
state space is small and tabular (ratios, counts), exactly where gradient-boosted trees match or
beat neural nets while staying fast, deterministic, and inspectable. A bigger model doesn't fix
anything here, because the problem we actually found isn't about model capacity.

**Pure imitation is capped at its teacher's skill, and none of the available teachers are good
enough.** Behavioral cloning of Kingdom1, Kingdom2, Pirate, or Berserker would, at best, reproduce
that persona's own decision function — and we now know none of them reliably wins. In practice
cloning tends to land slightly below the teacher (compounding errors on states the teacher never
demonstrated), not above it. The one real exception: cloning across many noisy teacher rollouts can
average out that teacher's own bad luck, a modest smoothing gain, not a capability gain.

**Exceeding a fixed teacher requires scoring outcomes, not imitating choices.** Once the training
signal is "did this action correlate with actually winning" instead of "what would the teacher have
picked," the result is no longer bounded by any single teacher's policy. This is the actual
justification for the genetic algorithm work below: it optimizes directly against simulated
outcomes, sidestepping the "no good teacher" problem entirely.

**A staged path (proposed, not yet attempted) for going further:** GA-tune the existing persona's
parameters (cheap, bounded search space) to get a better-than-baseline teacher; distill that into a
structurally richer model — more decision points than the existing hand-coded genes can express;
run a second round of evolution or outcome-based training on the richer model's own parameters,
now searching a space that strictly contains the first solution. The first distillation step can
only recover what the GA-tuned teacher already does, per the imitation ceiling above — the actual
gain comes from the second round searching a larger space than the first could ever express. This
is a real, recognized pattern (roughly "iterated distillation and amplification" / policy iteration
with growing function approximation), not a hopeful guess, but it hasn't been built.

## The genetic algorithm: what was tried, and what it found

`GeneticAlgorithmTests.cs` evolves `NpeCharacter`'s gene fields — `ImperialistGene`,
`DefensiveGene`, `OffensiveGene`, `FactorGene`, `Provoke`, `SphereX` — against a *fixed* baseline
Kingdom1 opponent in East-vs-West (2 empires, cleaner 1-v-1 signal than Intro's 5). Gene bounds are
the union of Kingdom1's and Kingdom2's own real roll ranges, not invented values.

Four rounds, each addressing a problem the previous one surfaced:

1. **First pass** (population 16, generations 6, 3 seeds/candidate, population-margin fitness,
   120-year horizon): cheap (7.7s total), and the numbers implied real eliminations were happening,
   though not logged explicitly.
2. **Scaled up with explicit elimination logging** (population 24, generations 12, 8
   seeds/candidate): confirmed real eliminations (84 evolved-wins / 0 baseline-wins / 2,220
   neither, of 2,304 playouts), but held-out validation on 20 fresh seeds exposed the problem: the
   best training genome (38% training win rate) scored **0%** held out. The fitness function
   (evolved side's population minus baseline's population, plus a large elimination bonus) was
   rewarding economic growth, which this game's mechanics make easy to get from conquering
   independent worlds, not war-winning capability — a real design flaw, not sampling noise.
3. **Redesigned fitness function**: instead of comparing final population sizes, score the fixed
   baseline's own decline in population/ships/industry/planet-count from its own starting values,
   sampled every 10 years and summed across the horizon (an area-under-curve measure), plus the
   same large elimination bonus as a terminal term. Re-run at the same scale: eliminations rose to
   101/0/2,203, the generation-over-generation win-rate trend went from noisy and flat to a real
   climb (2%→7%), and held-out win rate moved from 0% to **5%** (1/20). Real improvement, not a
   full fix — the best training genome still showed the same shape of gap (38% training vs. this
   round's 5% held-out).
4. **More seeds per candidate** (8→30, held-out set 20→50), same fitness function, same population
   and generation counts, to test whether the remaining gap was measurement noise (a ~5% true win
   rate is hard to estimate reliably from only 8 samples) rather than a fitness design problem.
   Result: held-out win rate rose to **8%** (4/50, a sturdier result on 4 real wins instead of 1),
   the training/held-out optimism gap narrowed from ~7.6x to ~2.9x, and the win-rate trend across
   generations climbed further (peaking at 9%). Improved, not resolved — some residual gap is
   expected at this sample size and would need a substantially larger sample to close further.

**The best genome found is a real, qualitatively new strategy, not just "become Kingdom2."** Final
best: Imp=14, Def=5, Off=100, Fac=24, Prv=62, Sph=31 — near-maximum offense and near-minimum
defense (Kingdom2-like), paired with a low Imperialist gene, in Kingdom1's passive range rather than
Kingdom2's. In plain terms: skip the independent-world land grab, focus on attacking. Neither
hand-tuned preset represents that combination.

## Open threads

- Whether pushing seeds-per-candidate (and/or population/generation count) further continues to
  close the training/held-out gap, or whether the fitness function needs further refinement beyond
  the damage-based redesign.
- The staged distillation/re-evolution bootstrap described above, not yet attempted.
- Whether `LosesIfCapitalConquered` is set for human player slots in the tested scenarios, which
  bears directly on what "losing" actually means for a human versus what this session's harnesses
  measured (full empire elimination).
- Rebellion/`RevolutionIndex`-driven internal collapse as a loss vector, entirely untested here —
  everything measured this session was combat-driven.
- Pirate and Berserker personas, untested in either playout harness.
- Cheap behavioral-cloning of the existing personas as a way to validate the ML.NET pipeline
  end-to-end, independent of whether it produces a strong opponent.

## Where things live

- `src/Reconstructed4021.Tests/ScenarioPlayoutTests.cs` — C# all-Kingdom-AI playout harness.
- `reference/verify/runworld.pas` (`npeplayout` domain) and
  `src/Reconstructed4021.Tests/PascalGroundTruth/NpePlayoutCases.cs` — the Pascal-side equivalent.
- `reference/verify/patches/FLEET.PAS.patch` — the heap-safety fix the Pascal harness needed.
- `src/Reconstructed4021.Tests/GeneticAlgorithmTests.cs` — the genetic algorithm harness.
