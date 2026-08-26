# Port design notes

Cross-cutting decisions about how *this C# port* models Pascal's data and behavior — the "why does
the port's own code look like this" reference. Distinct from
[`docs/ROADMAP.md`](ROADMAP.md), which tracks phase/commit status, and
[`docs/PASCAL_ARCHITECTURE_NOTES.md`](PASCAL_ARCHITECTURE_NOTES.md), which maps and records findings
about the *original* Pascal source. Each entry below cites the `ROADMAP.md` phase/commit it landed in
for the full "what shipped" story; this doc only covers the *why this shape*.

## Randomness

Every simulation method that needs randomness takes a `Random` parameter explicitly (constructor
injection for stateful handlers like `VisibilityHandler`, a plain parameter for static methods) —
never a held/default instance. Tests use a `Random` subclass (`FixedRandom`) that fixes `Next`'s
return value for deterministic branches, and assert bounds/invariants (e.g. "efficiency never
exceeds 100") for genuinely-random magnitudes.

Translation trap, worth restating since it recurs in every phase: Pascal's `Rnd(lo,hi)` is inclusive
on both ends — `Rnd(2,5)` is `random.Next(2, 6)` in C#, not `random.Next(2, 5)`. `PascalMath.Rnd`
wraps this once (`Core/PascalMath.cs`, Phase 1/2a) so no call site re-derives it. `PascalMath.Rnd`
also reproduces `Rnd`'s own degenerate-range clamp (`max<=min` returns `min` **without drawing**) —
this matters for RNG-stream parity: a call that doesn't draw doesn't consume a slot in the shared
PRNG stream either, and getting this wrong desyncs every later draw (see
`PASCAL_ARCHITECTURE_NOTES.md`'s memory-corruption-that-wasn't investigation for a concrete case of
this exact class of bug).

`PascalMath.PascalRound` is FreePascal's actual `Round`: banker's rounding (half-to-even at exact
`.5` boundaries), not Turbo Pascal-style half-away-from-zero — confirmed directly against the ground-
truth compiler (`Round(2.5)=2`, `Round(3.5)=4`, `Round(-2.5)=-2`), fixed in Phase 5d after going
undetected through every earlier phase's golden-file coverage (no earlier formula happened to land
exactly on a `.5` boundary).

## Economic worlds: `IEconomicWorld`

`Planet` and `Starbase` share most of the annual-tick economy pipeline (an industrial-complex
starbase runs almost the same `UpdateWorld` sequence a planet does) but not all of it — introduced in
Phase 1 commit 4 once the real overlap (smaller than "one shared procedure" suggested from a single
call site) was visible from actually building starbase support, not guessed at up front. Four members
are explicit-interface-only, each encoding one `PRIMINTR.PAS` accessor's Base-case behavior a shared
property can't express: `EffectiveClass` (`ArtCls`, a starbase has no world-class field),
`SelfSufficiencyIndex` (always index 0, no `ImpExp` field), `TrillumReserve` (`MaxResources`, writes
discarded, no `TriReserve` field), `InitializeSelfSufficiency` (`InitializeISSP`'s `CASE` has no Base
branch).

`Empire.Capital` widened from `Planet?` to `IEconomicWorld?` in Phase 2c once `CreateBase`
(`NEWGAME.PAS:1095-1096`) turned out to be able to make a *starbase* an empire's capital — a real
type-modeling gap the narrower type would have silently dropped rather than surfaced.

## Technology tracking

`Empire.Technology` (`UnlockedTechnology`) has four buckets — `Ships`/`Defenses`/`Constructions`/
`Resources` — not three. `Resources` (`HashSet<CargoType>`) was added in Phase 1 commit 5a once
`NewTechLevel`'s outer guard turned out to be a real equality check against Pascal's `TechSet`, which
spans resource types too; without it the guard could never detect "still missing a resource-type
unlock." `TechCatalog.FullSetAt(TechLevel)` (Phase 5f) computes `TechDev[level]` on demand from the
same per-category min-tech tables rather than storing all 11 raw Pascal sets — valid because `TechDev`
is genuinely monotonic in `TechLevel` (verified by expanding all 11 rows to explicit enum-position
membership, not assumed).

"Extra techs" at empire creation (`EmpireFactory`, Phase 2b) are typed against this port's own enums
(`TechCatalog.Grant(ShipType.HunterKiller)`, etc.), not any file format's raw ordinal — a scenario-
file parser decodes into these, not the other way around, so the domain model never couples to the
legacy `.SCN` numbering.

## Resource-shortfall reporting: a per-tick dedup set

Pascal's `ReportPlanetLack` (`UPDATE.PAS:39-56`) bumps `RevolutionIndex` by 1 the first time a given
resource type is reported short in a tick — a real state mutation, not just a skipped `AddNews` call,
and easy to under-model as "just fire the news" (which is what this port originally did, until
migrating `revolution.golden` to the patch-based lane surfaced the gap, Phase 1). `Report
ResourceShortfall` (`AnnualTickHandler.Production.cs`) fixes this with a per-tick `HashSet<CargoType>`
mirroring Pascal's own `OtherReports` scratch set, threaded through `UpdateIndustry` (fires for both
planets and starbases) and `ApplyRawMaterialConstraint`/`Production` (planets only, per
`IEconomicWorld.IsPlanet` — `UPDATE.PAS:904`'s guard on that specific call site) — so the same
resource type only bumps `RevolutionIndex` once per world per tick, matching Pascal exactly.

## Revolution index: two different mutation patterns, on purpose

`Empire.TotalRevolutionIndex` is genuine stored state (Phase 1), updated two different ways that
never run simultaneously:

- **Annual-tick commit** (`AnnualTickHandler.RunAnnualTick`): a per-tick scratch accumulator
  (`newTotalRevIndex: Dictionary<Empire,int>`) that only `Rebellion` writes to, committed (replacing,
  not adding to, the prior value) once at the end of the tick — a snapshot/accumulate/commit pattern,
  not something a live sum could reproduce (reading a "current total" partway through would be
  order-dependent on which worlds had already been processed).
- **Real-time mutator** (`CombatOutcome`'s private `ChangeTotalRevIndex`, Phase 5f): combat resolution
  mutates the field directly and immediately, mid-turn, matching Pascal's own `ChangeTotalRevIndex`
  (`PRIMINTR.PAS:1085-1096` — no clamp, unlike the per-world `[0,100]` `ChangeRevIndex`).

A per-world `RevolutionIndex` clamp (`AnnualTickHandler.ChangeRevIndex`, `[0,100]`) is shared between
the annual tick and `CombatOutcome`'s `ConquerWorld`/`NewCapital` — promoted from `private` to
`internal` in Phase 5f rather than duplicated, the same way `VisibilityHandler.ScoutAdjacent` (which
already *is* Pascal's `Scout(Emp,XY)` primitive, gaps and all) was promoted for `ConquerWorld`'s own
`Scout` call. Real Pascal keeps both in `PRIMINTR.PAS`, a shared primitives unit multiple other units
`USE` — this mirrors that shape instead of introducing a new file for two one-line callers.

## News: from a Pascal union to real typed fields

`NewsItem` (Phase 4) replaces Pascal's `NewsRecord.Loc: Location` union (an entity reference almost
always, but a bare `Coordinate` for `ConstructionCompleted` specifically) with two real nullable
fields — `Subject: ISectorObject?` / `Position: Coordinate?` — typed via a new `ISectorObject`
interface (`Coordinate Location`, `Empire Owner`) rather than `object`. Earned by a genuine
third/fourth occurrence, not invented for this occasion: `VisibilityHandler.FindProbeTarget` (Phase 3)
already dispatched across the same concrete types by hand. `Fleet` joined `ISectorObject` in Phase 5f,
once `ResolveAttack`'s fleet-target branch needed to fire a Global news item with a fleet as its
subject.

The recurring Pascal `(Loc,Emp)` shape (most combat/interaction headlines carry a second empire via
`Ord(Emp)`, since `Parm1..3` were its only generic slots) became a real `Empire? OtherEmpire` field.
Four headlines (`GLBDest`/`GLBConq`/`GLBCapConq`/`GLBLAMStrk`, Phase 5f) turned out to need *two*
empire references — `(Loc,Attacker,Defender)`, not `(Loc,Emp)` — so `NewsItem`/`Empire.AddNews`/
`Game.AddGlobalNews` gained a second real `Defender: Empire?` field rather than falling back to
Pascal's own raw-`Parm1`/`Parm2` packing, the exact pattern `OtherEmpire` was added to avoid in the
first place.

`NCapTech`'s `Ord(NewTech)` needed the same treatment for a different reason: this port has no flat
`TechnologyTypes` ordinal to port (already deliberately split into four typed enums), so
`TechCatalog.MissingTechAt` pairs each grant with a `TechGrantIdentity` (category + that category's
own ordinal) alongside its unlock delegate.

`Game.AddGlobalNews` broadcasts to every empire that's scouted the source, excluding a caller-supplied
set — built from scratch in Phase 4 (`Rebellion`'s "world goes independent" branch was the first real
caller) since nothing needed a broadcast-style news call before then.

## Combat: a unified `AttackType` axis

`AttackType` (`Types/AttackType.cs`, Phase 5c) mirrors Pascal's `AttackTypes = NoRes..nnj` — spanning
`DefenseType ∪ ShipType ∪ {Legion, NinjaLegion}`, a genuine single cross-product axis for
`CombatTable[Attacker,Defender]`. `DefenseType`/`ShipType` stay exactly as they are (real per-type
stored state lives on `DefenseCounts`/`ShipCounts`); `AttackType` is purely a combat-engine indexing
concern, with `AttackTypeExtensions` mapping at the boundary. The leading `NoRes` sentinel is dropped
rather than ported — its row/column in every combat table is all zero and never legitimately read, so
"no target" is `AttackType?` at the call site instead of an enum member.

## Empire elimination: a plain list, not a ported flag

Real Pascal marks a fixed array slot `InUse:=False` (`INTRFACE.PAS:1657`) because it has no real list
to remove an eliminated empire from. This port does have one (`Game.Empires: List<Empire>`), and
`Game.NextEmpire`/`Game.IsFirstEmpire` already re-derive everything live (`Empires.IndexOf(current)`,
`Empires[0]`) rather than caching a position — hand-traced before committing to this (Phase 5a) that
plain `Empires.Remove(eliminated)` self-heals the "one annual tick per lap" invariant regardless of
*when* in a lap the removal happens: if the removed empire wasn't at position 0, nothing observable
changes; if it was, whoever was at position 1 slides into position 0 and becomes the new lap anchor
from then on, because both `Count` and `Empires[0]` are re-read live on every call, never stored. No
new `IsEliminated` field, no guards needed anywhere `Game.Empires` is already iterated
(`Game.AddGlobalNews`'s Scouted-check loop, `RunAnnualTick`'s per-empire loop) — an eliminated empire
isn't in the list any more, so there's nothing to filter.

Only a human empire is exempt from removal, matching Pascal's own `EmpirePlayer` branch in
`ConquerEmpire`: `Empire.DefeatedBy: Empire?` (Phase 5f) replaces Pascal's capital-sentinel trick
(`ConquerEmpire`, `ATTACK.PAS:1120-1131`, overloads `Capital: IDNumber` with `ObjTyp:=Void;
Index:=Ord(Player)` to record the winner) — the same shape of problem `NewsItem.Loc` had before Phase
4's split, fixed the same way: `Capital = null` (already means "no capital" unambiguously) plus a
real second field for the second fact. The write is real Pascal behavior `ConquerEmpire`'s port needs
regardless of a reader; nothing reads `DefeatedBy` yet (no human `ITurnHandler` exists — Phase 8's
job), matching the "port the real branch, leave it unread until its phase exists" precedent already
set by `Empire.News.Clear()` (Phase 4).

## Standalone attack mechanics: port what's live, skip what's dead

`Combat/CombatStandalone.cs` (Phase 5g) covers four ATTACK.PAS/SBASE.PAS procedures that sit outside
`CombatResolution.NPEAttack`'s own group/shell round loop — each its own separate Pascal entry point,
not a branch that loop reaches on its own:

- **`LAMAttack` and `DestroyConstructionOrGate` are ported.** Both are live in the shipped 1.31 game
  (confirmed by tracing every caller, not assumed) — `LAMAttack` from `DESIGN.PAS`'s `LaunchLAM` (human
  command, Phase 8) and `NPE00`/`NPE03`/`NPEINTR`'s guardian/NPE strikes (Phase 6); `ATTNPE.PAS`'s own
  `NPEAttack` calls `DestroyConstructionOrGate` directly for a construction-site/stargate target — a
  real branch of *already-ported* code (`CombatResolution.NPEAttack`), not a future consumer, so it's
  wired in now rather than left for later. `LAMAttack`'s `Random` parameter is dropped entirely (not
  threaded through unused) — confirmed by reading, it has no `Rnd` call anywhere in its body, the only
  Phase 5 procedure with that property.
- **`HolocaustWorld`/`HolocaustEffectiveness` are NOT ported — confirmed dead code, not deferred.**
  Their only caller (`MSCCOMM.PAS`'s `HolocaustCommand`) is wrapped in a Pascal comment, along with its
  own forward interface declaration and its `PLAYTURN.PAS` dispatch entry — the real game could not
  have called it, in either the 1.31 or 2.0 source. Same treatment as `BATTLE.PAS`/`BOMBER.PAS`/
  `Consolidate` (see `PASCAL_ARCHITECTURE_NOTES.md`'s "Findings from porting" section) — a different
  category from "live Pascal, no port-side caller yet" (`SelfDestructObject`, below), and important not
  to conflate: the first is never worth porting, the second is a real primitive waiting on a phase.
- **`SelfDestructObject` is ported with no port-side caller yet** (Phase 8's `SelfDestructCommand`
  doesn't exist), same `Empire.News.Clear()` precedent as `DefeatedBy` above — but *is* real, reachable
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
`MaxProbesInTransit` in Phase 3 — `ProbeRecord` (`DATASTRC.PAS:170-175`) has no position field, only a
destination and status, so a probe doesn't move incrementally at all; `UpdateProbes` resolves an
in-transit probe in one call, so "in transit" reduces to just a list of destinations. `ProbeStatus`'s
`AtDestination`/`Lost` states were dropped as confirmed dead code (see
`PASCAL_ARCHITECTURE_NOTES.md` for the dead-code investigation) — `Status` was fully derivable from
`Destination`'s nullability, and slot identity had no observable meaning.

## Ground-truth harness generation

Two ways to get real-Pascal ground truth for a C# behavior, so a check can't silently agree with the
same hand-derivation mistake on both sides.

**Patch-based (`reference/verify/`) — the default**, used by every domain landed since Phase 2's
`techlevel`. Small maintained patches (`reference/verify/patches/*.PAS.patch`) apply to a disposable
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
web is its own investigation, not something the domains above generalize to automatically — Phase 6
is the first to actually pull in `Intrface`'s `Fleet`/`Orders`/`NPE` web (patching `FLEET.PAS` as
part of its 6a), decided when that phase was scoped, not assumed in advance.

## NPE AI: personality dispatch, not one generic AI

Phase 6's own scoping pass (six research forks reading every NPE-adjacent Pascal file in full)
found real Pascal implements **four structurally distinct NPE personalities** — Pirate, Kingdom,
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
port's `Game.TurnHandlers: Dictionary<Empire, ITurnHandler>` (pre-existing, Phase 1) already gets
that for free through ordinary polymorphism — a parallel tagged-union type would port Pascal's
workaround into a codebase that doesn't have the problem it works around, the same call already
made for empire elimination above. `KingdomTurnHandler : ITurnHandler` is the one implementation
Phase 6 builds; `PirateTurnHandler`/`BerserkerTurnHandler`/`GuardianTurnHandler` slot in later as
their own classes when picked up — no shared AI base class invented ahead of a second real
implementation to generalize from.

**What genuinely is shared, and gets built now despite only one personality existing:**
`NPEINTR.PAS` (1,732 lines) is a real toolkit multiple personalities call in Pascal — fleet
deployment, targeting, mission-dispatch, bookkeeping — not Kingdom-private code, so it's ported as
its own service `KingdomTurnHandler` depends on rather than folded into Kingdom's own class; later
personalities call the same methods. `Empire.NpeType: NpeEmpireType?` (recording which personality
an empire is) is real, needed-now state regardless of how many personalities are implemented —
Phase 7's save/load needs it, and (landed in 6b) `ScenarioLoader.RunCreateNPEmpire` now records it
on every NPE empire and constructs a `KingdomTurnHandler` for `Kingdom1`/`Kingdom2` specifically;
other types are recorded but left with no `Game.TurnHandlers` entry until their own personality
lands. Per-fleet AI mission state (Pascal's `FleetDataRecord`) gets a field-by-field audit
before anything is ported, not a verbatim mirror — some fields (`Waiting`, `Midway`) look
derivable from state the port already tracks (`FleetStatus`, `Location`/`Destination`); only what
survives the audit is new state, homed on the owning `ITurnHandler` instance (AI bookkeeping only
some fleets have), not bolted onto `Fleet` itself.

Full commit breakdown and reachability data: `docs/ROADMAP.md`'s Phase 6 section.

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
  picking case parameters, the same way Phase 5f's `ConquerEmpire` cases were designed against the
  exact `AND`-short-circuit/`Rnd` operand order in `ATTACK.PAS`, not just the intended outcome.
