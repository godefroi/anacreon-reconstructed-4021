# New Emperor's Guide

Practical answers to the decisions a new player actually faces early on -- what to do with a
freshly conquered world, whether a young colony needs help, when rebellion is a real risk -- read
directly out of this port's own formulas (`src/Reconstructed4021.Core`), not general 4X instinct.
Every claim below cites the C# source (and, where it's ported from, the original Pascal) it came
from. Two things flagged as genuinely unresolved live in their own section at the end; everything
else was read straight from the code.

## A newly conquered world

### What conquest does automatically

The moment a world changes hands, two things happen with no input from the player:

- **Efficiency drops 10-20 points**, floored at zero.
- **Revolution Index shifts** -- and which way depends on how stable the world already was under
  its old owner, not on anything the new owner does:

| Old Revolution Index was... | What happens |
|---|---|
| 0-10 (calm) | Jumps up 10-20. A quiet world gets *less* stable under new management. |
| 11-30 | Mixed roll -- most often drops 3-15, occasionally spikes 10-30. |
| 31-55 | Mixed roll, leaning toward a drop (50% chance of -10 to -20). |
| 56-75 | Drops 40-55. |
| 76-90 | Drops 50-65 -- the biggest calming effect in the table. |
| 91-100 | Drops 30-40. |

Source: `CombatOutcome.ChangeRevolutionIndexOnConquest` (ATTACK.PAS:946-974).

Practical read: a world that was already miserable under its old owner calms down the most when
conquered. A world that was placid gets *more* restless -- conquest itself is destabilizing for a
happy population, and reassuring for an unhappy one.

### Should you redesignate it right away?

The manual's own advice (*Conquest and Empire: What Do I Do with a Pre-Atomic World?*) is blunt:
for a low-tech, low-efficiency conquest, often **the best move is no move**. Leave it alone and let
it ride the empire's own tech curve -- a world's tech level chases its owning empire's capital tech
every tick, with roughly a 16% chance per tick to advance one level when it's behind
(`AnnualTickHandler.UpdateTechLevel`, `TechLevelIncreaseChance`). The manual estimates a pre-atomic
world can reach warp level in about ten years under a stable empire.

If you do redesignate, know the real cost first:

```
newEfficiency = efficiency - efficiency / (1.5 + random[0,1))
```

That's a loss of roughly **40% to 67% of current efficiency**, every single time -- even
redesignating a world to the type it already is. There's no discount for a small change and no way
to avoid it. Source: `WorldDesignation.Redesignate` (INTRFACE.PAS:189-223).

> **Manual vs. code.** The manual describes this cost as a flat "20 to 30 points." The actual
> formula is *proportional*, not flat: a 90-efficiency world loses 36-60 points; a 30-efficiency
> world loses only 12-20. The manual's number is a rough middle case, not the rule. Since conquest
> already knocks 10-20 points off on its own, redesignating in the same turn you conquer compounds
> both hits -- a freshly-conquered world can drop to single-digit efficiency in one move.

### If the world is earthlike

The manual has a specific answer: designate it as a raw-material world -- chemical, metal, or
trillum, or the generic type that does all three at reduced efficiency. The three specialized
single-resource types are always more efficient than the generalist; the generalist just saves
shipping. The manual singles out **trillum** as worth designating somewhere regardless: it's
needed in kilotons, not megatons, so a modest jumptransport run covers a large base for years, and
jumptransports reach anywhere in jumpspace within one or two years.

### Making it your capital costs much more

Redesignating *to Capital* stacks on top of everything above:

- The empire's whole tech catalog resets toward the new capital's tech level -- if the new capital
  is behind the old one, empire tech *drops* to match; if it's ahead, the empire still loses one
  level off what the new capital has (`WorldDesignation.Redesignate`'s `Capital` branch).
- The old capital is demoted to a plain Base and takes the same proportional efficiency hit
  described above.
- Empire-wide Revolution Index spikes 35-45, on every world at once
  (`CombatOutcome.ChangeTotalRevIndex`).

Swap capitals for a real strategic reason, not on a whim.

## Feeding young colonies

### Every world's food bill, every tick

```
suppliesNeeded = (population / 100) * 25
```

If stored Supplies cover it, they're simply spent. If they don't, the shortfall triggers immediate,
hard consequences -- there's no partial-ration middle ground. Source: `AnnualTickHandler.UseUpFood`
(UPDATE.PAS:1130-1160).

### What actually happens when supplies run short

```
peopleStarved = min(shortfall / 6, population / 10)
```

Population loss from starvation is capped at **10% per tick**, no matter how large the shortfall
is -- a world can't be wiped out by one bad turn of supply, but it can bleed 10% a turn, repeatedly,
if the underlying shortfall never gets fixed. Every starving turn also raises Revolution Index,
scaled by the world's tech level and capped at +45 in one tick (see the Open Questions section
below -- the tech-level scaling table itself has an unresolved inconsistency).

### The real lever is Supply industry, not rationing

There's no "emergency ration" control in the simulation -- Supplies are produced like any other
resource, and the only dial that changes how much a world *tries* to produce is its **ISSP**
setting for Supply industry (World/ISSP). A young or newly conquered world sitting below 100%
self-sufficiency on Supply is manufacturing its own future starvation event; raising that dial is
the actual fix, not stockpiling from elsewhere as a first resort -- ISSP only ever describes the
shortfall/surplus, it never moves material for you; shipping Supplies in by transport still works,
it's just not automatic.

### Why a young world's population plateaus -- and it isn't food

Separately from starvation, ordinary population growth is capped by **the world's own tech level**,
not by how well-fed it is:

- Below 75 population: flat fast growth, +2 to +5 per tick.
- Between 75 and the tech level's own average population: growth accelerates roughly with
  `128 * population / classMaxPopulation`.
- **Once population passes the tech level's own average** (a fixed table -- 3 at Pre-Tech, 250 at
  Atomic, 1700 at Bio, 3000 at Gate), growth collapses to a tiny fixed trickle:
  `averagePopForTech / 100`.

Source: `AnnualTickHandler.UpdatePopulation`, `BasePopulationByTech` (DATACNST.PAS:221-223).

In practice: a freshly conquered Pre-Atomic world's population growth nearly stalls once it passes
~100, long before it comes anywhere near its environmental ceiling (which can be 2,000-5,000
depending on world class, `_maxPopulationByClass`). The environmental cap almost never binds early
on. **Advancing tech level is what raises the growth ceiling**; feeding the world well only
prevents the population you already have from shrinking.

## When rebellion is a real risk

The manual's five-step Revolution Index ladder maps to real, different mechanics at each band, not
just cosmetic labels (thresholds from `AnnualTickHandler.UpdateRevolution`, UPDATE.PAS:681-755):

| Band | Range | Meaning |
|---|---|---|
| `no` | 0-30 | No chance of rebellion. |
| `Lo-` | 31-43 | Dissatisfaction. |
| `Lo+` | 44-66 | Riots and demonstrations. |
| `Hi-` | 67-75 | Rebellion likely. |
| `Hi+` | 76-100 | Rebellion imminent. |

### Rebellion literally cannot happen below 76

Nothing in the code even rolls for an actual rebellion until Revolution Index exceeds 75. Above
that line, the chance *is* the index itself -- a world sitting at 80 has an 80% chance, per tick, of
actually revolting rather than just issuing another warning. The Capital is coded as immune to
rebellion outright, no matter how high its index climbs.

### The index mostly heals itself -- if the empire is stable

Every world's Revolution Index drifts down every tick by an amount tied to the **empire's own
total** revolution index, not just the one world's. A well-run empire drags every world toward calm
on its own; the Capital gets an extra fixed -20 to -30 on top, which is most of why capitals rarely
destabilize.

### Garrison size matters, but there's a counter-intuitive trap

Each world type has an "optimum" garrison (Legions, with Ninja Legions counted at 5x weight) scaled
to its population. Over-garrisoning an already-index-30+ world actively suppresses unrest. But
**under**-garrisoning a non-Capital, non-Base world that's *already calm* (index <= 30) has a 1-in-5
chance per tick of triggering a "troops want out" event that *raises* the index 5-15 -- agitation
for withdrawing troops that aren't even there in force. A too-light garrison on a calm world isn't
neutral; it's a small, recurring destabilizer.

**Practical takeaway:** freshly conquered worlds arrive with Revolution Index already elevated in
most cases (see the conquest table above). Get a real garrison in place before it compounds -- the
difference between garrisoning early and garrisoning after the first "Lo+" warning is the
difference between suppressing unrest and racing it to 76.

### If a rebellion actually fires

Whether the empire suppresses it depends on garrison strength versus a rebel force sized to the
world's own population (roughly `sqrt(population) * 65` rebels). Lose, and the world goes fully
Independent, its ISSP dials reset to the default midpoint, and Revolution Index crashes down as an
independent world. Win, and the index drops modestly but the news header still reads as a black
mark. Source: `AnnualTickHandler.Rebellion`.

## What tech level actually gates

This port (and the original) tracks tech level in two genuinely separate places. Mixing them up is
the single most common source of "why can't I build/designate that?"

### A world's own tech level

Gates which *designations* are legal on that specific world, independent of what the empire has
researched anywhere else (`WorldDesignation.MinTechForType`, DATACNST.PAS:279-300):

- **Pre-Tech:** Agricultural, Independent
- **Primitive:** Mine, raw-material types
- **Pre-Atomic:** Chemical
- **Atomic:** Trillum mine
- **Pre-Warp:** Base, Transport base
- **Jump:** Capital, Jumpship base, University
- **Bio:** Ambrosia, Starship base
- **Starship:** Ninja world
- **Gate:** Outpost, every starbase variant

A world can never be designated above its own current tech level -- this is why a newly conquered
Pre-Atomic world simply *cannot* become a Capital or a University yet, no matter what the empire
otherwise knows how to build.

### The empire's tech catalog

Separately, the empire accumulates unlocked ship types, defense types, cargo types, and
construction types as a whole (`TechCatalog`) -- this is what actually determines what can be
*built* anywhere, once a world's own designation and industry allow it. A world being tech-eligible
for a designation doesn't mean the empire has unlocked what that designation produces yet, and vice
versa.

## Open questions -- verify before relying on these

Everything above was read directly out of the simulation's own code. Two things didn't fully
resolve on this pass and are flagged rather than guessed at.

**Starvation's tech-level sensitivity table reads backward from its own code comment.** The table
that scales how much a starvation event raises Revolution Index
(`_starvationRevoltAdjustmentByTech`) is highest for Pre-Tech and Primitive worlds (100) and lowest
around Jump/Bio (12-13), rising slightly again toward Gate (15). The comment attached to that table
in the source claims "low-tech worlds...revolt less readily than high-tech ones" -- the opposite of
what the numbers show. The numbers themselves are unambiguous and are what the simulation actually
runs on; the English explanation next to them looks like it doesn't match. Worth a second look
before treating the "why" as settled.

**Some less-common news headline wording wasn't independently re-verified this pass.** The exact
text of rarer events (riot deaths, industry sabotage from ambrosia withdrawal, hostile-life
encounters) came from this session's own prior work on the News Window rather than being re-read
from source directly during this research pass. The mechanics they're attached to (the population/
rebellion/starvation formulas above) were read fresh and directly for this guide.
