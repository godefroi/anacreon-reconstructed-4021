# Modeling a stronger NPE

Current understanding of the ported NPE AI (Kingdom1/Kingdom2), what's wrong with it, and what
we've tried to fix. This tracks where we are, not a chronological log of every round; superseded
findings are removed rather than marked retired.

## What we know about NPE behavior

There's no enforced win condition anywhere in this port or the original Pascal. Nothing checks
"only one empire remains" and declares a winner; a scenario's suggested length (`.SCN`'s
`MinLen`/`MaxLen`) is read by `ScenarioLoader` and discarded.

Symmetric Kingdom-vs-Kingdom combat, where every empire runs the same persona, essentially never
resolves. Confirmed identically in both the C# port and real Pascal (a matching harness drives the
actual `ImplementKingdom1NPE`/`ImplementKingdom2NPE` from `NPE02.PAS`): zero eliminations across 40
runs at up to a 500-year cap, in both engines, despite large real growth (4x-40x population,
100x-3,000x ships) from conquering independent worlds. This isn't a porting defect; it's genuine
1988 AI behavior.

Once one side is tuned even a little better than a fixed opponent, real eliminations happen, and
the rate collapses steeply with how many opponents there are at once:

| Matchup | Scenario | Best confirmed win rate |
|---|---|---|
| 1 vs 1 | East-vs-West | ~8% held-out |
| 1 vs 2 | A purpose-built 3-empire scenario (below) | ~0.5% |
| 1 vs 4 | Intro | 0%, even at a 500-year horizon |

Going from one simultaneous opponent to two alone costs roughly a 16x drop in win rate. This
"force-division tax" is the single largest factor found in any of this work, bigger than anything
any tunable behavior has moved so far.

**The bottleneck is not targeting sophistication.** Trend-awareness (does an enemy's strength show
a real decline over time), empire-level proximity ("center of gravity" distance between empires,
distinct from picking a nearby individual world), and a focus-bias (concentrate attacks on one
priority enemy rather than spreading) were all built as tunable genes and tested across three
different scenario setups, including one purpose-built to give empire-proximity an unambiguous
signal to find (a 3-empire scenario with the evolved side at one end, a near opponent, and a far
opponent 2.7x farther away). None showed a positive effect; two showed a small negative one. Only
after re-running with far more samples per candidate did one of them (focus) reveal a small real
positive signal that a smaller sample had hidden, worth remembering when a result looks flat.

**A real mechanical bottleneck was found and worked through to a settled conclusion: powerful,
slow ships are a genuine net negative in this game, not an idea sabotaged by implementation bugs.**
`GetFleetComposition`'s dominant dispatch mode (`JumpAttack`, ~95% of real attacks) draws from a
hardcoded ship-priority table that never includes Starships and only reaches Penetrator as a last
resort, so a `CompositionGene` was built to let the AI deliberately choose to include them. Three
separate, real, confirmed problems were found and fixed along the way, and the correlation with
fitness never once turned positive through any of them:

- Any fleet containing a Starship or a cargo-carrying Transport collapses to the slowest speed
  tier, confirmed directly against the real Pascal source (`PRIMINTR.PAS`'s `TypeOfFleet`), the
  same all-or-nothing purity rule that also governs stealth (below). This is faithful 1988 design,
  not a bug, so the fix was dispatching a Starship-heavy attack as its own separate fleet instead
  of merging it into a fast escort.
- A dedicated heavy wave is still slow in absolute terms and target defenses grow continuously
  regardless of distance, so a wide-ranging heavy wave often arrived too late to matter. Gating
  heavy-wave dispatch by target distance helped some.
- The dominant cost turned out to be neither of those: launching a heavy wave was draining a base's
  entire trillum stockpile, leaving nothing to fuel that same base's next ordinary attack, which
  then aborted before it even launched. This was starving the empire's normal offense, not just the
  heavy wave itself; measured directly, 60% of a heavy-composition genome's attack dispatches
  aborted this way, versus 0% for a cheap-ship genome. Fixed by checking a base's fuel reserve
  before committing a heavy wave, skipping it rather than starving the escort.

Correlation with fitness across these three fixes: -0.44, then -0.29, then -0.07 (closest to
neutral), then, once the trillum-starvation confound was actually removed, **-0.22**, further
negative than the step before it. That's the real, final signal, not an artifact: once the escort
was no longer being collaterally starved, the data more cleanly showed the underlying cost that was
there from the start. Settled: this isn't a bug hunt anymore, it's a genuine property of the game.
Slow, powerful ships cost more in lost time than they gain in strength, at least in every scenario
tested here.

Every ship also burns trillum-derived fuel per year of activity, at a wildly uneven rate (a
Starship costs more than double a Jumpship's), and each world has its own finite, non-renewable
trillum reserve funding ongoing production. Neither of those turned out to be the actual mechanism
above (world reserves never got close to zero in a 120-year run, and mid-journey fleet fuel
exhaustion happened once in an entire sampled run); the real cost was the one-time drain on a
base's stockpile at launch, not ongoing consumption. An empire could in principle send trillum
ahead as cargo to resupply a long campaign, but that would be a real three-way trade against speed
and stealth (`VisibilityHandler.cs`: a fleet that's *purely* HunterKillers is exempt from ordinary
detection, a pure-Penetrator fleet gets a partial version of the same exemption, and adding any
other ship type, including a cargo carrier, loses that exemption immediately, the same purity
mechanic as the speed cliff) rather than a free fix, and isn't needed now that the actual bug is
fixed.

**Two more real, confirmed gaps, not yet built, next up:**

- Attack size and frequency (small fleets often vs. large fleets rarely) is currently a fixed
  probability split ported straight from Pascal (`WarCabinet`'s 75/25 or 50/50 JumpAttack/SlowAttack
  choice by policy tier), not tunable by any persona today.
- Target selection is currently omniscient. `GetBestTarget` reads a candidate's `Ships`/`Defenses`
  directly off the live world object, gated only by a one-time "have I ever discovered this world"
  flag, no notion of stale or last-scouted intelligence distinct from ground truth. A human player
  would only know what they last scouted. Worth fixing if the goal is a defensible opponent rather
  than a cheating one.

## Training methodology, separate from persona capability

Two things matter for training a genetic search on this AI, independent of which behaviors are
gened: the fitness function, and how many seeds per candidate.

The fitness function needs to reward actually damaging an opponent, not growing the economy.
Comparing final population sizes rewarded conquering independent worlds, which this game makes easy
regardless of combat skill, and produced genomes that scored 38% on their training seeds but 0% on
fresh ones. Scoring the opponent's own decline in population, ships, industry, and world count
against its own starting values, summed across the whole horizon, fixed this: held-out win rate
went from 0% to a real, if modest, 5-8%. With more than one fixed opponent, even that broke down,
since opponents' independent-world growth can outpace real combat damage regardless of what's done
to them; the fix there was crediting directly attributable combat outcomes (worlds actually
conquered from a specific opponent) rather than net stock decline.

Seed count per candidate matters more than it looks like it should. At a win rate below 1%, 8-16
seeds per candidate isn't enough to reliably tell a genuinely better genome from a lucky one; this
is why the Composition and Focus genes above needed 50 seeds per candidate before their real signal
(negative and positive, respectively) became visible.

## Architecture

Experimental genes and behaviors live on a new, separate `ITurnHandler` implementation,
`Kingdom2ModernTurnHandler` (`src/Reconstructed4021.LegacyNpe/`), not on the real `KingdomTurnHandler`
that actual games use. It reuses `NpeToolkit`'s shared static helpers (`StateDepartment`,
`WarCabinet`, `GetBestTarget`, and so on) so there's no duplicated engine logic, but it's wired into
the GA harness through `INpeHandlerProvider`, the same extension point real scenario loading already
uses to pick a persona's implementation. Real Kingdom1/Kingdom2 behavior is never touched by any of
this work; it doesn't need per-round reverification against the regression suite anymore, since the
experimental path is a genuinely different class rather than a no-op-gated flag on the production
one.

## Where things live

- `src/Reconstructed4021.Tests/ScenarioPlayoutTests.cs`: C# all-Kingdom-AI scenario playout harness.
- `reference/verify/runworld.pas` (`npeplayout` domain) and
  `src/Reconstructed4021.Tests/PascalGroundTruth/NpePlayoutCases.cs`: the Pascal-side equivalent, for
  ground-truth comparison.
- `reference/verify/patches/FLEET.PAS.patch`: a real heap-safety fix the Pascal harness needed
  (`fpc`'s heap doesn't tolerate a dangling disposed pointer the way real Turbo Pascal's does).
- `src/Reconstructed4021.LegacyNpe/Kingdom2ModernTurnHandler.cs`: the experimental persona.
- `src/Reconstructed4021.Tests/GeneticAlgorithmTests.cs`: the genetic algorithm harness, including
  the fitness functions and gene definitions described above.
- `src/Reconstructed4021.Tests/Fixtures/OrionsBelt.scn`: a purpose-built 3-empire scenario with a
  guaranteed near/far opponent geometry and zero independent worlds, for isolating force-division
  and proximity questions from economic growth.

## Open questions

- Attack size/frequency and masked-intel target selection, both confirmed real gaps, neither built,
  next up.
- Whether the force-division tax itself is addressable at all by tuning the existing decision
  structure, or whether it needs a genuinely different mechanism, such as an empire deliberately
  building up overwhelming economic strength before engaging more than one opponent.
- Whether `LosesIfCapitalConquered` is set for human player slots in these scenarios, which bears on
  what "losing" means for a human versus what these harnesses measure (full empire elimination).
- Rebellion and internal collapse as a loss vector, entirely untested; everything measured so far is
  combat-driven.
- Pirate and Berserker personas, untested in any playout harness.
- Cheap behavioral cloning of the existing personas as a way to validate an ML.NET pipeline
  end-to-end, independent of whether it produces a strong opponent; deprioritized relative to the
  genetic-algorithm work above, since pure imitation can't exceed any of the four existing personas'
  own skill, and none of them are good.
