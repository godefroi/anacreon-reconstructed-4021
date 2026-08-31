# Empire lifecycle: a design proposal

**Status: proposed, not implemented.** This is a design note, not a plan — it records the reasoning
and the target shape so a future phase can turn it into one. It supersedes the reasoning (not
necessarily the current behavior) of `docs/PORT_DESIGN.md`'s "Empire elimination: a plain list, not
a ported flag" section, which describes what this port does *today* and is still accurate as a
description of current code. Nothing here is implemented yet.

## What actually happens in real Pascal

Traced directly from source, not inferred from the port, while investigating why `GameJson`/
`SavGameWriter` need "orphan empire" handling at all.

**Every empire ever created keeps a permanent slot for the life of the game.** `Universe^.EmpireData`
is a fixed `ARRAY[Empire1..Empire8]` — it never grows or shrinks, for humans or NPEs alike. A
scenario creates at most 8 empires, once, at load; nothing ever creates one later. "Deleting" an
empire never means removing its record — Pascal has no mechanism to do that — it means flipping one
field: `Universe^.EmpireData[Emp].InUse:=False` (`INTRFACE.PAS:1657`).

**`EmpireActive` is the single canonical liveness check, and it's just that one field**
(`PRIMINTR.PAS:962-965`: `EmpireActive := Universe^.EmpireData[Emp].InUse`). No other gameplay code
has a separate, more specific notion of "still real." `EmpirePlayer` (`:967-970`, `IsAPlayer`) is a
completely orthogonal flag — human vs. NPE — not a liveness check.

**There is one lifecycle, not two, and both empire kinds end the same way — they just get there on
different schedules:**

- **NPE loser → instant elimination.** `ConquerEmpire` (`ATTACK.PAS:985-1139`) branches solely on
  `EmpirePlayer(EnemyEmp)` (line 1123) — the *loser's* type, never the conqueror's. A non-human
  loser goes straight to `DestroyEmpire` (`INTRFACE.PAS:1612-1658`), synchronously, in the same call:
  every planet still owned by them → Independent (unconditional, no dice), every starbase →
  Independent, every fleet aborted then destroyed outright, news erased, names deleted, `CleanUpNPE`
  called (disposes their own AI decision data — `NPE.PAS:34-45`, just `Dispose(Data)` per NPE
  sub-type), then `InUse:=False`. Full teardown, no delay, no distinction between Kingdom/Pirate/
  Guardian/Berserker — `NPE.PAS`'s own dispatch treats them identically.
- **Human loser → deferred elimination.** `ConquerEmpire`'s human branch (`:1124-1128`) never calls
  `DestroyEmpire`. It only overwrites `Capital` with a sentinel recording the conqueror
  (`ObjTyp:=Void; Index:=Ord(Player)`). `InUse` stays true. Fleets and starbases are untouched.
  Planets are handled by the *earlier*, separate per-planet loop (`:1032-1097`), which is
  probabilistic per world (join conqueror / declare independence / considered-but-not-chosen as a
  new capital) — a planet that lands in the third bucket and simply loses the new-capital comparison
  is never reassigned at all. **A defeated human can genuinely keep planets, all fleets, and all
  starbases after "defeat."** This state is a waiting room, not an end state: the *next* time that
  player's own turn comes up, `EmpireNews` (`PROLOG.PAS:456-488`) checks `GetCapital(Player,CapID);
  IF CapID.ObjTyp=Void`, shows a personalized defeat narrative naming the conqueror
  (`EmpireName(OtherEmp)`), sets `LastTurn:=True`, and calls the *exact same* `DestroyEmpire` an NPE
  gets — same teardown, same `InUse:=False`, only difference being `CleanUpNPE` is skipped
  (`EmpirePlayer` is still true, and there was never NPE data to begin with).

So the real model is: `Active → PendingElimination(conqueror) → Eliminated`, where an NPE transitions
through the middle state in zero time (no one to notify), and a human parks there for up to one lap
of the turn order until their own turn-prologue delivers the news and finishes the transition.
`EmpireActive`/`InUse` stays true for the *entire* `PendingElimination` window, for both kinds — an
empire mid-defeat is fully real to every other system until the final transition, not before.

**Nothing treats a `PendingElimination` empire specially.** Checked this directly rather than
assuming:
- `StateDeptReport`/`StateDepartment` (`NPEINTR.PAS:1600`, `:1477`) gate purely on `EmpireActive AND
  EnemyEmp<>Emp` — an NPE tracks threat/military and can escalate to open war (`ConflictPLT`) against
  a defeated-in-place human exactly as it would against anyone else, with zero exemption for "already
  lost."
- `WarCabinet` (`NPE00.PAS:511-600`) picks attack targets from `SetOfPlanetsOf[EnemyEmp]` — the
  target's *currently owned* planets, not their capital. If that set is empty (the common case),
  `GetBestTarget` returns `EmptyQuadrant` and nothing happens; if the defeated human still holds a
  straggler planet, it's a perfectly ordinary, unprotected attack target. The one place `WarCabinet`
  *does* read the capital (`:585-586`, probing near it) degrades gracefully — `GetCoord` on a `Void`
  `ObjTyp` falls through to `Limbo` (`PRIMINTR.PAS:310-311`, a defined sentinel coordinate, not a
  crash), so it just wastes a probe aimed at nowhere.
- Combat itself is never triggered by mere fleet arrival. Every call site of `NPEAttack`/
  `ResolveAttack` (searched exhaustively across the source) is an explicit dispatch: either an NPE's
  own mission logic firing when *its* fleet completes *its* own attack mission, or a human-issued
  attack command (`ATTCOMM.PAS:1582`). A defeated human's surviving fleet sitting at an NPE's planet
  doesn't spontaneously fight anyone. (Not fully verified: I traced call sites, not `FLEET.PAS`'s
  full arrival/movement code, so a separate automatic-defense mechanic — e.g. a starbase firing on an
  unauthorized fleet in range — hasn't been ruled out.)

## What this means for the "orphan empire" problem

`GameJson`'s `EntityIndex` and `SavGameWriter`'s `EmpireSlotIndex` both exist because this port's
`CombatOutcome.DestroyEmpire` does `Game.Empires.Remove(empire)` — a real, deliberate divergence from
Pascal (`docs/PORT_DESIGN.md`'s existing "Empire elimination" section, chosen so `NextEmpire`/
`IsFirstEmpire` self-heal with no new guard) — while `KingdomTurnHandler.State` keeps live `Empire`
references to anyone it's ever dealt with, dead or alive. Once an empire is gone from the one list
that gives it identity, anything still holding a reference needs an id from somewhere else, and both
save formats currently reconstruct that lazily by walking every possible reference site (Kingdom
`State` keys, `DefeatedBy`, `News.OtherEmpire`/`Defender`, minefield owner/scouted-by).

Given the above, that divergence is the thing worth removing, not working around further: **Pascal's
own model already has a permanent identity for every empire, for the life of the game.** Matching
that directly — instead of hard-deleting and then reconstructing orphan identity after the fact —
removes the orphan concept at its root rather than teaching more code to tolerate it.

One category is already provably safe to stop worrying about regardless of this bigger change:
Kingdom diplomatic memory of a dead empire is inert today. `NpeToolkit.StateDeptReport`/
`StateDepartment` are the *only* two places `_state` gets read during play, and both iterate
`game.Empires` (the live roster) then look up `state[enemyEmp]` — neither ever iterates
`state.Keys`. A `StateDeptRecord` for an empire no longer in `game.Empires` is read by nothing,
confirmed by tracing every call site.

## Proposed shape

- **`Empire` gains an explicit status** — `Active` / `PendingElimination(Empire conqueror)` /
  `Eliminated` — replacing today's `DefeatedBy: Empire?` plus physical removal from `Game.Empires`.
  `DefeatedBy` becomes the conqueror payload carried by the last two states, unified across human and
  NPE instead of being human-only.
- **`Game.Empires` keeps every empire for the life of the game.** No more `.Remove()`. This directly
  mirrors `Universe^.EmpireData`'s own fixed-for-the-session shape, and means an `Empire`'s identity
  never needs reconstructing after the fact — id assignment for the save layer becomes "walk the list
  once," full stop.
- **Liveness for AI/diplomacy purposes is `Status != Eliminated`** — matching `EmpireActive`/`InUse`
  exactly, which (per the trace above) stays true through the *entire* `PendingElimination` window
  for both kinds. `PendingElimination` is not "inactive," it's "about to be."
- **Turn dispatch is the one place that needs an explicit filter now** (`Status == Active`) — this is
  the single call site that used to get "not eliminated" for free from list membership. Everywhere
  else (`NpeToolkit`'s targeting/diplomacy loops, `AnnualTickHandler`'s per-empire tick) already
  iterates `game.Empires` and would keep working unchanged, since a `PendingElimination`/`Eliminated`
  empire staying in the list is *correct* now, not a bug to filter around.
- **NPE elimination stays synchronous** (`Active → Eliminated` in one step, matching `DestroyEmpire`
  exactly, just setting `Status` instead of removing from the list). **Human elimination stays
  deferred** (`Active → PendingElimination` at `ConquerEmpire` time, matching today's port behavior),
  but the `PendingElimination → Eliminated` transition — real Pascal's `EmpireNews` — has no trigger
  in this port yet, because no human `ITurnHandler` exists. That's fine to leave as an explicit,
  documented hook for whoever builds one: the same teardown `DestroyEmpire` already does for NPEs,
  fired at the top of that human's next turn instead of instantly.
- **A small, engine-owned, pull-based facility for per-empire scoped data**, for any `ITurnHandler` —
  Kingdom today, whatever comes after it — to use instead of hand-rolling its own dictionary and its
  own lifecycle tracking:

  ```csharp
  public sealed class EmpireScopedStore<T>(Func<Empire, bool> isActive)
  {
      private readonly Dictionary<Empire, T> _data = new();
      public T? Get(Empire empire) => isActive(empire) ? _data.GetValueOrDefault(empire) : default;
      public void Set(Empire empire, T value) => _data[empire] = value;
      public IEnumerable<KeyValuePair<Empire, T>> All => _data.Where(kv => isActive(kv.Key));
  }
  ```

  Pull-based, not push-based: no event to subscribe to, no per-handler sweep to remember to call, no
  risk of a future handler getting this wrong. `isActive` is `Status != Eliminated`, sourced once
  from `Game`. This replaces `NpeToolkit.EnforceNpeDataLinks`-style per-turn sweeps as the *general*
  pattern — that function stays as-is for `_fleetStates` (a genuinely different case: Pascal's own
  30-slot fleet table is actively recycled, a real resource-scarcity concern, not an identity one),
  but a new handler's per-*empire* data shouldn't need its own version of it.

## What this removes

Once every empire keeps a permanent, stable identity in `Game.Empires`, `SavGameWriter`'s
`EmpireSlotIndex` and `GameJson`'s `EntityIndex` no longer need to *discover* orphans by walking
Kingdom `State` keys, `DefeatedBy`, News, and minefield data — every empire that could ever need an
id or a slot is already sitting in `Game.Empires`, in stable creation order, capped at exactly 8
(a real bound in practice already, since a scenario can only ever declare 8 player+NPE slots to
begin with — this was never actually a squeeze).

This also resolves `News.OtherEmpire`/`Defender` without any special handling. Denormalizing a
captured name onto `NewsItem` looked necessary earlier only because a referenced empire could
disappear out from under the reference under hard-delete; once nothing is ever removed from
`Game.Empires`, `OtherEmpire`/`Defender` are exactly as unremarkable as `Empire.DefeatedBy` or a
Kingdom `State` key — a plain, stable, permanently-valid `Empire` reference like any other, with a
real name still attached because the object itself never goes away.

## Open, unresolved by this note

- **Minefield owner/scouted-by** (`Galaxy.MinefieldData`/`MineScoutedByData`) hasn't been checked
  against real read sites the way Kingdom `State` was. Before assuming the same treatment applies,
  confirm whether combat/detection logic reads a live minefield's owner during play.
- **This note doesn't attempt an implementation plan.** Turning it into one touches `CombatOutcome`,
  `ScenarioLoader`, `SavGameLoader`, `SavGameWriter`, `GameJson`, `NpeToolkit`, `KingdomTurnHandler`,
  and every existing test that currently asserts hard removal from `Game.Empires` (including tests
  added on the `cleanup-npe-on-empire-death` branch). That's real, multi-file surgery, and belongs in
  its own planned phase once this shape is agreed on, not folded into whatever branch happens to be
  open when it's written.
