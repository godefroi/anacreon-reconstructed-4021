# Questions for George

George Moromisato, the original author, has graciously offered to answer a (presumably limited)
number of questions about the original design and implementation. This is a working list of
what's actually worth asking — real ambiguities the source alone can't resolve, not things a
closer reading would answer. Grouped by topic; each has a concrete hook back to the source or this
port so the question is checkable against the actual behavior, not just a vague memory prompt.

Related: [`PASCAL_ARCHITECTURE_NOTES.md`](PASCAL_ARCHITECTURE_NOTES.md) records the dead-code and
quirk findings these questions are drawn from, with more detail on how each was confirmed.

## Dead code — was it abandoned, or superseded, and by what?

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
  see "An earlier, simpler combat-resolution design" in `PASCAL_ARCHITECTURE_NOTES.md` for the combat
  analog; this is the same shape of question for probes. Was multi-turn probe travel (arrive, sit at
  destination, then return) ever implemented or shipped in an earlier build, or did it stay a plan the
  four-state enum outlived?
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

## Combat resolution — deliberate rule, or incidental?

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

## Empire elimination and conquest

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

## Formulas with a visible asymmetry

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
