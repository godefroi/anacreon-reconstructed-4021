# Port design notes

Cross-cutting decisions about how *this C# port* models Pascal's data and behavior — the "why does
the port's own code look like this" reference. Distinct from
[`docs/ROADMAP.md`](ROADMAP.md), which narrates how the port came together, and
[`docs/PASCAL_ARCHITECTURE_NOTES.md`](PASCAL_ARCHITECTURE_NOTES.md), which maps and records findings
about the *original* Pascal source. This doc covers the *why this shape*, not the history of how it
got there.

## Randomness

Every simulation method that needs randomness takes a `Random` parameter explicitly (constructor
injection for stateful handlers like `VisibilityHandler`, a plain parameter for static methods) —
never a held/default instance. Tests use a `Random` subclass (`FixedRandom`) that fixes `Next`'s
return value for deterministic branches, and assert bounds/invariants (e.g. "efficiency never
exceeds 100") for genuinely-random magnitudes.

Translation trap, worth restating since it recurs constantly: Pascal's `Rnd(lo,hi)` is inclusive
on both ends — `Rnd(2,5)` is `random.Next(2, 6)` in C#, not `random.Next(2, 5)`. `PascalMath.Rnd`
wraps this once (`Core/PascalMath.cs`) so no call site re-derives it. `PascalMath.Rnd`
also reproduces `Rnd`'s own degenerate-range clamp (`max<=min` returns `min` **without drawing**) —
this matters for RNG-stream parity: a call that doesn't draw doesn't consume a slot in the shared
PRNG stream either, and getting this wrong desyncs every later draw (see
`PASCAL_ARCHITECTURE_NOTES.md`'s memory-corruption-that-wasn't investigation for a concrete case of
this exact class of bug).

`PascalMath.PascalRound` is FreePascal's actual `Round`: banker's rounding (half-to-even at exact
`.5` boundaries), not Turbo Pascal-style half-away-from-zero — confirmed directly against the ground-
truth compiler (`Round(2.5)=2`, `Round(3.5)=4`, `Round(-2.5)=-2`), fixed after going undetected
through earlier golden-file coverage (no earlier formula happened to land exactly on a `.5`
boundary).

## Economic worlds: `IEconomicWorld`

`Planet` and `Starbase` share most of the annual-tick economy pipeline (an industrial-complex
starbase runs almost the same `UpdateWorld` sequence a planet does) but not all of it — introduced
once the real overlap (smaller than "one shared procedure" suggested from a single call site) was
visible from actually building starbase support, not guessed at up front. Four members
are explicit-interface-only, each encoding one `PRIMINTR.PAS` accessor's Base-case behavior a shared
property can't express: `EffectiveClass` (`ArtCls`, a starbase has no world-class field),
`SelfSufficiencyIndex` (always index 0, no `ImpExp` field), `TrillumReserve` (`MaxResources`, writes
discarded, no `TriReserve` field), `InitializeSelfSufficiency` (`InitializeISSP`'s `CASE` has no Base
branch).

`Empire.Capital` widened from `Planet?` to `IEconomicWorld?` once `CreateBase`
(`NEWGAME.PAS:1095-1096`) turned out to be able to make a *starbase* an empire's capital — a real
type-modeling gap the narrower type would have silently dropped rather than surfaced.

## Technology tracking

`Empire.Technology` (`UnlockedTechnology`) has four buckets — `Ships`/`Defenses`/`Constructions`/
`Resources` — not three. `Resources` (`HashSet<CargoType>`) was added once
`NewTechLevel`'s outer guard turned out to be a real equality check against Pascal's `TechSet`, which
spans resource types too; without it the guard could never detect "still missing a resource-type
unlock." `TechCatalog.FullSetAt(TechLevel)` computes `TechDev[level]` on demand from the
same per-category min-tech tables rather than storing all 11 raw Pascal sets — valid because `TechDev`
is genuinely monotonic in `TechLevel` (verified by expanding all 11 rows to explicit enum-position
membership, not assumed).

"Extra techs" at empire creation (`EmpireFactory`) are typed against this port's own enums
(`TechCatalog.Grant(ShipType.HunterKiller)`, etc.), not any file format's raw ordinal — a scenario-
file parser decodes into these, not the other way around, so the domain model never couples to the
legacy `.SCN` numbering.

## Resource-shortfall reporting: a per-tick dedup set

Pascal's `ReportPlanetLack` (`UPDATE.PAS:39-56`) bumps `RevolutionIndex` by 1 the first time a given
resource type is reported short in a tick — a real state mutation, not just a skipped `AddNews` call,
and easy to under-model as "just fire the news" (which is what this port originally did, until
migrating `revolution.golden` to the patch-based lane surfaced the gap). `Report
ResourceShortfall` (`AnnualTickHandler.Production.cs`) fixes this with a per-tick `HashSet<CargoType>`
mirroring Pascal's own `OtherReports` scratch set, threaded through `UpdateIndustry` (fires for both
planets and starbases) and `ApplyRawMaterialConstraint`/`Production` (planets only, per
`IEconomicWorld.IsPlanet` — `UPDATE.PAS:904`'s guard on that specific call site) — so the same
resource type only bumps `RevolutionIndex` once per world per tick, matching Pascal exactly.

## Revolution index: two different mutation patterns, on purpose

`Empire.TotalRevolutionIndex` is genuine stored state, updated two different ways that never run
simultaneously:

- **Annual-tick commit** (`AnnualTickHandler.RunAnnualTick`): a per-tick scratch accumulator
  (`newTotalRevIndex: Dictionary<Empire,int>`) that only `Rebellion` writes to, committed (replacing,
  not adding to, the prior value) once at the end of the tick — a snapshot/accumulate/commit pattern,
  not something a live sum could reproduce (reading a "current total" partway through would be
  order-dependent on which worlds had already been processed).
- **Real-time mutator** (`CombatOutcome`'s private `ChangeTotalRevIndex`): combat resolution
  mutates the field directly and immediately, mid-turn, matching Pascal's own `ChangeTotalRevIndex`
  (`PRIMINTR.PAS:1085-1096` — no clamp, unlike the per-world `[0,100]` `ChangeRevIndex`).

A per-world `RevolutionIndex` clamp (`AnnualTickHandler.ChangeRevIndex`, `[0,100]`) is shared between
the annual tick and `CombatOutcome`'s `ConquerWorld`/`NewCapital` — promoted from `private` to
`internal` rather than duplicated, the same way `VisibilityHandler.ScoutAdjacent` (which
already *is* Pascal's `Scout(Emp,XY)` primitive, gaps and all) was promoted for `ConquerWorld`'s own
`Scout` call. Real Pascal keeps both in `PRIMINTR.PAS`, a shared primitives unit multiple other units
`USE` — this mirrors that shape instead of introducing a new file for two one-line callers.

## News: from a Pascal union to real typed fields

`NewsItem` replaces Pascal's `NewsRecord.Loc: Location` union (an entity reference almost
always, but a bare `Coordinate` for `ConstructionCompleted` specifically) with two real nullable
fields — `Subject: ISectorObject?` / `Position: Coordinate?` — typed via a new `ISectorObject`
interface (`Coordinate Location`, `Empire Owner`) rather than `object`. Earned by a genuine
third/fourth occurrence, not invented for this occasion: `VisibilityHandler.FindProbeTarget`
already dispatched across the same concrete types by hand. `Fleet` joined `ISectorObject` once
`ResolveAttack`'s fleet-target branch needed to fire a Global news item with a fleet as its subject.

The recurring Pascal `(Loc,Emp)` shape (most combat/interaction headlines carry a second empire via
`Ord(Emp)`, since `Parm1..3` were its only generic slots) became a real `Empire? OtherEmpire` field.
Four headlines (`GLBDest`/`GLBConq`/`GLBCapConq`/`GLBLAMStrk`) turned out to need *two*
empire references — `(Loc,Attacker,Defender)`, not `(Loc,Emp)` — so `NewsItem`/`Empire.AddNews`/
`Game.AddGlobalNews` gained a second real `Defender: Empire?` field rather than falling back to
Pascal's own raw-`Parm1`/`Parm2` packing, the exact pattern `OtherEmpire` was added to avoid in the
first place.

`NCapTech`'s `Ord(NewTech)` needed the same treatment for a different reason: this port has no flat
`TechnologyTypes` ordinal to port (already deliberately split into four typed enums), so
`TechCatalog.MissingTechAt` pairs each grant with a `TechGrantIdentity` (category + that category's
own ordinal) alongside its unlock delegate.

`Game.AddGlobalNews` broadcasts to every empire that's scouted the source, excluding a caller-supplied
set — built from scratch (`Rebellion`'s "world goes independent" branch was the first real caller)
since nothing needed a broadcast-style news call before then.

## Combat: a unified `AttackType` axis

`AttackType` (`Types/AttackType.cs`) mirrors Pascal's `AttackTypes = NoRes..nnj` — spanning
`DefenseType ∪ ShipType ∪ {Legion, NinjaLegion}`, a genuine single cross-product axis for
`CombatTable[Attacker,Defender]`. `DefenseType`/`ShipType` stay exactly as they are (real per-type
stored state lives on `DefenseCounts`/`ShipCounts`); `AttackType` is purely a combat-engine indexing
concern, with `AttackTypeExtensions` mapping at the boundary. The leading `NoRes` sentinel is dropped
rather than ported — its row/column in every combat table is all zero and never legitimately read, so
"no target" is `AttackType?` at the call site instead of an enum member.

## Empire elimination: a permanent roster, matching Pascal's own fixed array

`Game.Empires: List<Empire>` never shrinks — nothing is ever removed from it. This directly mirrors
`Universe^.EmpireData`'s own fixed `ARRAY[Empire1..Empire8]`: Pascal never grows or shrinks it either,
only flips one field, `InUse:=False` (`INTRFACE.PAS:1657`). `Empire.Status` (`Active` /
`PendingElimination` / `Eliminated`) is that field's port-side equivalent; liveness for AI/diplomacy
purposes is `Status != Eliminated`, matching `EmpireActive` (`PRIMINTR.PAS:962-965`) exactly.

There is one lifecycle, not two, for both empire kinds — they just reach `Eliminated` on different
schedules, matching `ConquerEmpire`'s (`ATTACK.PAS:985-1139`) own branch on the *loser's*
`EmpirePlayer` flag: an NPE loser goes `Active → Eliminated` synchronously, inside
`CombatOutcome.DestroyEmpire`, in the same call that resolved the fight. A human loser instead parks
at `PendingElimination` — `Capital = null`, `Status = PendingElimination`, `DefeatedBy` set to the
conqueror, nothing else touched — until their own next turn's dispatch (`TurnEngine.AdvanceOneTurn`)
calls the same `DestroyEmpire` to finish the transition, matching Pascal's own deferred
`PROLOG.PAS:456-488` `EmpireNews`. `DestroyEmpire` reassigns every owned planet/starbase to
Independent, destroys every fleet, clears news, and drops the `Game.TurnHandlers` entry (the AI
decision data half of Pascal's `CleanUpNPE`) — but never touches `Game.Empires` itself.

Turn dispatch (`TurnEngine.AdvanceOneTurn`) is the one place that needs an explicit `Status` switch;
everywhere else that already iterated `game.Empires` (`NpeToolkit`'s `StateDeptReport`/
`StateDepartment`, `AnnualTickHandler`'s per-empire tick) just needed an `Eliminated`-skip guard added,
since an eliminated empire staying in the list is now correct, not a bug to filter around.
`Game.NextEmpire`/`Game.IsFirstEmpire` stay deliberately `Status`-blind — see their own doc comments
for why a skip-aware version would break annual-tick wrap detection.

Full reasoning, including the direct Pascal source trace this was designed from, is in
`docs/EMPIRE_LIFECYCLE_DESIGN.md`.

## Standalone attack mechanics: port what's live, skip what's dead

`Combat/CombatStandalone.cs` covers four ATTACK.PAS/SBASE.PAS procedures that sit outside
`CombatResolution.NPEAttack`'s own group/shell round loop — each its own separate Pascal entry point,
not a branch that loop reaches on its own:

- **`LAMAttack` and `DestroyConstructionOrGate` are ported.** Both are live in the shipped 1.31 game
  (confirmed by tracing every caller, not assumed) — `LAMAttack` from `DESIGN.PAS`'s `LaunchLAM` (a
  human command) and `NPE00`/`NPE03`/`NPEINTR`'s guardian/NPE strikes; `ATTNPE.PAS`'s own
  `NPEAttack` calls `DestroyConstructionOrGate` directly for a construction-site/stargate target — a
  real branch of *already-ported* code (`CombatResolution.NPEAttack`), not a future consumer, so it's
  wired in now rather than left for later. `LAMAttack`'s `Random` parameter is dropped entirely (not
  threaded through unused) — confirmed by reading, it has no `Rnd` call anywhere in its body, the only
  one of these four procedures with that property.
- **`HolocaustWorld`/`HolocaustEffectiveness` are NOT ported — confirmed dead code, not deferred.**
  Their only caller (`MSCCOMM.PAS`'s `HolocaustCommand`) is wrapped in a Pascal comment, along with its
  own forward interface declaration and its `PLAYTURN.PAS` dispatch entry — the real game could not
  have called it, in either the 1.31 or 2.0 source. Same treatment as `BATTLE.PAS`/`BOMBER.PAS`/
  `Consolidate` (see `PASCAL_ARCHITECTURE_NOTES.md`'s "Findings from porting" section) — a different
  category from "live Pascal, no port-side caller yet" (`SelfDestructObject`, below), and important not
  to conflate: the first is never worth porting, the second is a real primitive waiting on a phase.
- **`SelfDestructObject` is ported with no port-side caller** — no `SelfDestructCommand` exists in
  this port — same `Empire.News.Clear()` precedent as `DefeatedBy` above — but *is* real, reachable
  Pascal (`MSCCOMM.PAS:519`, dispatched live from `PLAYTURN.PAS`, outside the `(*ARTIFACTS*)` block that
  disables Holocaust). An earlier research pass claimed otherwise; that claim was wrong and has been
  corrected (`PASCAL_ARCHITECTURE_NOTES.md` again) rather than left standing.
- **Ground truth is split three ways, not uniformly golden-file-backed**: `LAMAttack` has real formula
  risk (`Round`/`Trunc` proportional distribution, the same arithmetic shape that produced the
  `PascalRound` bug in `combat.golden`) and no `Rnd` calls to complicate a harness case, so it gets a
  new patch-based domain (`lamattack.golden`). `DestroyConstructionOrGate`/`SelfDestructObject` have
  much less formula risk (plain removal, a linear `Rnd(min,max)` RevIndex change) and would need real
  new harness infrastructure to reach (`DestroyConstruction`/`DestroyStargate` need `Intrface`, not
  linked into the patched build; `SBASE.PAS` was never patched in at all) — covered by hardcoded C#
  tests instead (`CombatStandaloneTests.cs`), the same "harness can't reach it, cover it directly"
  precedent as `CombatOutcomeTests`' own human-defeat branch.

## Probes: no position field, because Pascal has none either

`Empire.Probes`/`Probe`/`ProbeStatus` (a pre-existing stub literally mirroring Pascal's 4-state
`ProbeRecord`) simplified to `Empire.ProbesInTransit: List<Coordinate>` plus `TryLaunchProbe`/
`MaxProbesInTransit` — `ProbeRecord` (`DATASTRC.PAS:170-175`) has no position field, only a
destination and status, so a probe doesn't move incrementally at all; `UpdateProbes` resolves an
in-transit probe in one call, so "in transit" reduces to just a list of destinations. `ProbeStatus`'s
`AtDestination`/`Lost` states were dropped as confirmed dead code (see
`PASCAL_ARCHITECTURE_NOTES.md` for the dead-code investigation) — `Status` was fully derivable from
`Destination`'s nullability, and slot identity had no observable meaning.

## Ground-truth harness generation

Two ways to get real-Pascal ground truth for a C# behavior, so a check can't silently agree with the
same hand-derivation mistake on both sides.

**Patch-based (`reference/verify/`) — the default**, used by every domain since the harness's first
one, `techlevel`. Small maintained patches (`reference/verify/patches/*.PAS.patch`) apply to a disposable
copy of the real `reference/DOSAnacreonSource131/*.PAS` source (`reference/verify/patched/`,
gitignored, rebuilt every run); `runworld.pas` then calls the real, only-minimally-touched Pascal
procedure(s) directly against a hand-assembled `Universe^`. Full mechanics, layout, and the
domain-by-domain investigation history live in
[`reference/verify/README.md`](../reference/verify/README.md) — read that (not this section) before
touching the harness itself.

**Transcription (`reference/verify/*.pas`)** — the older pattern: hand-copy a procedure into an
isolated file, called with plain parameters instead of a real `Universe^`. Unused today (the last
domain on it, `production`, migrated 2026-08-22) but not retired — still the right call for a
genuinely isolated, parameter-only procedure with no real-state dependency. Re-read the Pascal source
fresh every time; never transcribe from the existing C# port.

**Which one to reach for.** Patch-based whenever a procedure touches real state transcription would
otherwise have to fake (`GetCapital`/`GetTech`-style lookups, other empires/planets/worlds) or where
call *ordering* across a real pipeline is what's being checked — transcription missed a real bug this
way once (`UpdateMilitary` mutating `Cargo.Legions` before `UpdateRevolution` reads it). Transcription
only for a procedure with no such dependency. Don't gate the choice on authoring cost — lean into
patch-based broadly. Dependency-blast-radius judgment still applies: each new subsystem's dependency
web is its own investigation, not something the domains above generalize to automatically — fleet
movement fidelity was the first to actually pull in `Intrface`'s `Fleet`/`Orders`/`NPE` web (patching
`FLEET.PAS`), decided when that work was scoped, not assumed in advance.

## NPE AI: personality dispatch, not one generic AI

A scoping pass (six research forks reading every NPE-adjacent Pascal file in full) found real
Pascal implements **four structurally distinct NPE personalities** — Pirate, Kingdom,
Berserker, Guardian, each its own file with its own private data record in `NPETYPES.PAS`
(`PirateDataRecord`/`Kingdom1DataRecord`/`BerserkerDataRecord`/`GuardianDataRecord`) — dispatched
from `NPE.PAS`'s `NPEData[Emp].Typ: NPEmpireTypes` via a classic Pascal variant-pointer union
(`NPEDataRecord{Typ; Data: Pointer}`). `Kingdom1NPE`/`Kingdom2NPE` are the one place "one AI, two
tiers" actually holds: both share `NPE02.PAS`'s single `Implement`/`CleanUp`/`Save` procedure,
differing only in their `NPECharacterRecord` persona seed (`Kingdom1`: passive, `DefGene:=
Rnd(50,75)`; `Kingdom2`: aggressive, `OffGene:=Rnd(50,100)`) — Pirate/Berserker/Guardian are
separate code, not presets of one algorithm. A sixth type, `TraderNPE`, is declared but has zero
case arms in any of `NPE.PAS`'s 5 dispatch procedures, no data record, and zero usage across all
12 `dos_131/*.SCN` scenario files — confirmed dead, same treatment as `BATTLE.PAS`/`BOMBER.PAS`.

**No `NPEDataRecord`-style tagged union in C#.** Pascal's variant pointer exists only because its
dispatch array (`NPEData: ARRAY[Empire]`) needs one element type regardless of which AI runs. This
port's `Game.TurnHandlers: Dictionary<Empire, ITurnHandler>` (pre-existing) already gets
that for free through ordinary polymorphism — a parallel tagged-union type would port Pascal's
workaround into a codebase that doesn't have the problem it works around, the same call already
made for empire elimination above. `KingdomTurnHandler : ITurnHandler` is the one implementation
built so far; `PirateTurnHandler`/`BerserkerTurnHandler`/`GuardianTurnHandler` slot in later as
their own classes when picked up — no shared AI base class invented ahead of a second real
implementation to generalize from.

**What genuinely is shared, and gets built now despite only one personality existing:**
`NPEINTR.PAS` (1,732 lines) is a real toolkit multiple personalities call in Pascal — fleet
deployment, targeting, mission-dispatch, bookkeeping — not Kingdom-private code, so it's ported as
its own service `KingdomTurnHandler` depends on rather than folded into Kingdom's own class; later
personalities call the same methods. `Empire.NpeType: NpeEmpireType?` records which personality an
empire is — read by save/load, and by `ScenarioLoader.RunCreateNPEmpire`, which records it on every
NPE empire and constructs a `KingdomTurnHandler` for `Kingdom1`/`Kingdom2` specifically; other types
are recorded but have no `Game.TurnHandlers` entry until their own personality is implemented.
Per-fleet AI mission state (Pascal's `FleetDataRecord`) got a field-by-field audit before anything
was ported, not a verbatim mirror — every real read/write site checked directly rather
than guessed. Result: `Mission`/`TargetID`/`HomeBaseID`/`Waiting` are real and kept (as
`Core/Npe/NpeTypes.cs`'s `KingdomFleetState.Mission`/`Target`/`HomeBase`/`Waiting`); `Index`
dissolves into the owning `Dictionary<Fleet, KingdomFleetState>`'s key. `Midway` is confirmed
dead (declared at NPETYPES.PAS:86, never read or written anywhere in the 1.31 tree — see
`PASCAL_ARCHITECTURE_NOTES.md`), and `BlockX`/`BlockY` are Pirate-only (NPE01.PAS's hunting-ground
grid, not part of the shared toolkit) — neither ported for Kingdom. `Waiting` in particular turned
out not to be derivable from `FleetStatus` as an earlier pass here guessed — it's a real 0-5
raid-dwell counter `ImplementRaidTrnMSN` increments per turn, not a movement state.

**`NPEINTR.PAS` itself splits into two pieces along a real dependency line, not an arbitrary
cut.** Every one of its procedures either reads state and computes a value, or creates/moves/
refuels a fleet. The read-and-compute half (`MilitaryPower`, targeting, regional bookkeeping,
world (re)designation — `Core/Npe/NpeToolkit.cs`) needs nothing beyond what already exists in
the port. The other half (the five `Deploy*Fleet` procedures, all eight `Implement*MSN` mission
executors) needs real fleet-lifecycle primitives — `DeployFleet`, `ChangeCompositionOfFleet`,
`EstimatedDateOfArrival`/`EstimatedRange`, `RefuelFleet`, `SetFleetDestination` (FLEET.PAS/
PRIMINTR.PAS) — that didn't exist anywhere in this port before; every earlier subsystem only ever
moved or destroyed fleets that scenario loading or human setup already created, never made a new
one from a world's own stock. That half is its own separate unit, with `KingdomTurnHandler` as its
first real caller, kept apart from the read-and-compute half so the whole file wouldn't be
unverifiable until the very last fleet-lifecycle primitive was in place.

**Ground truth for the read-and-compute half stayed hardcoded, not golden-file, on purpose.** By
the time this was scoped the fuller Pascal tree (see "Ground-truth harness generation" above) was
already the default build, so the "grows one grudging function at a time" cost that used to justify
reaching for transcription over patching no longer applies — but these methods don't need a new
patched domain either way: every one either has no `Rnd`/`Random` call, or draws inside a
scan-every-planet loop whose count depends on live galaxy shape, and a golden case for the second
group would need a full hand-assembled universe to mean anything close to real play. Save/load,
now that it exists, would make building one of those cheap — worth revisiting if this ever needs
tighter verification, not worth scaffolding for preemptively.

Full breakdown and reachability data: see `docs/ROADMAP.md`'s Kingdom NPE AI entry.

**Adding a new patch-based domain:**
1. If the target procedure isn't already exported from its unit's `INTERFACE`, add/extend a patch —
   see `reference/verify/README.md`'s "Layout" for the patch-editing workflow (never hand-edit a
   `.patch` file directly; edit the applied copy under `patched/`, verify it compiles/runs, then
   regenerate the diff).
2. Give `runworld.pas` a new `case <domain>` branch (see its own header comment for the CLI
   convention: comma-separated fields in, one `key=value;...` line out per case).
3. Add a `<Domain>Cases.cs` — a case record plus `All`/`AsDataSource`, with a doc comment explaining
   that domain's own rationale (why patch-based, what the case shape encodes). Domain-specific detail
   belongs here, not in `GoldenFileTests.cs`.
4. One `GoldenFile.Regenerate(...)` call in `GoldenFileTests.RegenerateAllGoldenFiles` — the
   format-string lambda's field order must match `runworld.pas`'s parser field-for-field.
5. `dotnet test`, then review the new/changed `.golden` file's diff field-by-field before committing.

**Landmines worth checking for on every new domain** (each has bitten more than once already):
- **Sector-grid access** — anything touching `Sector[x]^[y]` (`PutMine`/`CreateStarbase`/
  `CreateStargate`, ...) needs `Galaxy.InitializeSector` called first, or it's a runtime error 216
  (access violation). Ask whether the domain's call graph touches the sector grid before wiring it up.
- **A hand-assembled `Universe^` is only as faithful as the fields it remembers to set.** "Defaults to
  zero" is not "reachable in real gameplay" — check the real initialization/seeding code
  (`CreateEmpire`, `PRIMINTR.PAS`'s `Set*` procedures, `DefaultISSP`) for what a genuinely new object
  actually gets set to.
- **`ABSOLUTE` overlays compile but lie.** `DATASTRC.PAS:235`'s `GlobalSets ABSOLUTE
  SetOfActiveFleets` relies on Turbo Pascal's declaration-order memory layout, which fpc doesn't
  guarantee — writing through it silently corrupts unrelated memory. Never write through a
  `GlobalSets`-style overlay; use the real standalone variable.
- **A `git diff --no-index`-generated patch needs its extended-format header stripped** (`diff --git`
  / `index` lines, and CRLF fixed) before `git apply` will actually apply it inside a real repo —
  otherwise it silently no-ops (0 files changed, no error). On Windows, generate the diff through a
  tool that reads raw bytes (e.g. PowerShell driving `git diff --no-index` directly) rather than a
  Bash pipeline — some Git-Bash/MSYS mounts silently translate CRLF↔LF on read/write through `sed`/
  `diff`, which produces a patch that looks clean but won't `git apply` against the real (CRLF)
  Pascal source.
- **Relocate the small dependency, not the whole unit**, when a pure procedure/formula lives in a unit
  with a much larger `USES` clause than the domain needs (`GetIndustrialDistribution` out of
  `INTRFACE.PAS`; `empirecreate`'s inline copy of `NEWGAME.PAS`'s 3-line tech-set formula, avoiding
  that unit's `Crt`/`Dos`/`NPE`/... chain entirely).
- **A discrepancy that looks architectural is usually the harness, not the port.** Verify against the
  real initialization/seeding code before concluding the C# port (or a doc comment) is wrong — the
  harness's own hand-assembled `Universe^` is the newest, least-trusted part of the whole chain.
- **A golden-file `Rnd(min,max)` scenario needs `RngFixedValue` chosen against the exact call
  sequence.** `ForcedRandomValue` (`FixedRandom` on the C# side) makes every `Rnd` call in a run
  return the same underlying draw, mapped into that call's own `[min,max]` range — with `RngFixedValue=0`,
  every `Rnd(min,max)` call returns exactly `min`. Hand-trace which branch that drives *before*
  picking case parameters, the same way `ConquerEmpire`'s own combat cases were designed against the
  exact `AND`-short-circuit/`Rnd` operand order in `ATTACK.PAS`, not just the intended outcome.
