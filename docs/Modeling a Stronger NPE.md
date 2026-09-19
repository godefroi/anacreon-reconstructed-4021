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

**Attack size and frequency (`AttackSizeGene`) was built and tested, and landed inconclusive.**
`WarCabinet`'s JumpAttack/SlowAttack split (75/25 or 50/50 by policy tier, fixed in real Pascal) was
made tunable; the search found a mild lean toward more-frequent-smaller attacks (+0.08 correlation
with fitness) that isn't distinguishable from this scale's own noise floor. Not a confirmed effect
in either direction.

**Target selection is currently omniscient** (`GetBestTarget` reads a candidate's `Ships`/`Defenses`
directly off the live world object, no stale/scouted-intel model distinct from ground truth), a real
gap, deliberately not pursued: fixing it would only make the AI worse by construction, forcing it to
act on less information than it has access to today. Not on the active list.

## Production efficiency: is the economy leaking, and is that shaping our results?

A real, separate question turned out to matter as much as any individual gene: is the game's
production pipeline itself wasteful, and if so, are any of this session's own changes making that
worse.

**The baseline economy leaks a lot, independent of anything built this session.** Measured directly
against real, unmodified `KingdomTurnHandler` playing Intro: 20.6% of all metal produced (and 14% of
chemical) is lost outright to storage-cap overflow, while metal shortages simultaneously throttle
industry growth more than any other resource, more than the next three industries combined. That's
not a contradiction, it's the actual bug: some worlds accumulate metal they can't store while others
are starved for the same resource, and nothing moves it between them. `SupplyLink`/`SurplusLink`
only redistribute between a starbase and its immediately adjacent worlds, never empire-wide. This is
a real, substantial inefficiency in the base game, not something this session's changes introduced,
and it's now tracked in [issue #50](https://github.com/godefroi/anacreon-reconstructed-4021/issues/50)
as a candidate player-facing report alongside other useful numbers (ships produced, ships per
population, ship-transit-years).

**One of this session's own genes measurably worsens this, and it's not the one that seemed most
likely to.** A combined-genome comparison against a same-seat baseline found the evolved empire
wasting 2.3x as much metal and 1.7x as much chemical to overflow, with ship output and
shortage-driven losses unchanged, meaning it wasn't starving, it was accumulating more surplus than
it could store. A factorial breakdown ruled out `CompositionGene` and `AttackSizeGene` (both
contribute close to nothing) and isolated the actual cause with no ambiguity: `FocusGene` alone
fully explains it, with zero contribution from `TrendWeightGene` or `CenterOfGravityGene` and no
interaction effect between any of them. Mechanistically coherent with what Focus actually does,
concentrating attacks onto one priority enemy changes production/dispatch rhythm enough that
stockpiles build past storage caps in the gaps between commitments. This is the same gene that
showed this session's only confirmed real *positive* correlation with win rate (+0.17 to +0.24) once
seed count was scaled up enough to see past noise — a real side effect of doing the thing that
helps, not evidence the underlying idea is bad.

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
- `src/Reconstructed4021.Core/Turns/ProductionDiagnostics.cs`: a static, opt-in accumulator tracking
  production lost to storage-cap overflow and raw-material shortage, filterable to a single empire.
  Used to find and isolate the `FocusGene` overflow effect above; also the natural data source for
  [issue #50](https://github.com/godefroi/anacreon-reconstructed-4021/issues/50)'s proposed report.

## Open questions

- Whether reclaiming `FocusGene`'s own overflow (spending or redistributing the surplus it creates)
  makes its already-positive effect on win rate stronger, now that the cause is confirmed and
  isolated rather than hypothetical. The natural next build, and a better-understood one than the
  trillum-logistics idea below.
- Whether the general resource-redistribution gap behind both findings above (no empire-wide
  mechanism moves surplus to shortage, only `SupplyLink`/`SurplusLink`'s adjacent-worlds-only
  version) is worth fixing on its own, independent of any specific gene.
- Trillum designation and logistics: since fuel/trillum turned out to be the dominant real cost of
  heavy compositions, an NPE that recognizes its own preferred composition implies a trillum need,
  designates a suitable world as `TrillumMine` to meet it, and moves the output to where a campaign
  is actually launching from is a well-motivated next capability. `Orion's Belt` can't test this
  fairly as it stands, though: at 3 worlds per empire, there's no room to dedicate one purely to
  trillum mining without crippling something else. A prerequisite is a modest, still-controlled
  expansion (a few more worlds per empire, symmetric across all three, still zero independent
  worlds) that makes specialization a real choice rather than a forced sacrifice, without
  reintroducing the "expand into empty space" economic-growth confound the current scenario was
  built to remove.
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
