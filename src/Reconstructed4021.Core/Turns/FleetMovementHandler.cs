using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// Advances fleets and mobile starbases according to the sequential turn-order rules in the
/// original game: current-empire jump fleets move first, then the next empire's warp fleets,
/// each consuming fuel and stepping toward their destination until arrival.
///
/// Minefield damage and disrupter blocking (FLEET.PAS:705-746, 619-644) are a per-step check inside
/// JumpFleet/HunterKillerFleet movement. Dense-nebula blocking is a real per-step check for every
/// fleet type (not just Jump/HK), not only combat's minefield/disrupter pair. Stargate teleportation
/// and fortress pass-through jumps are real (FLEET.PAS:646-861's <c>UpdateFleet</c>);
/// <c>UpdateAllFleets</c>'s dispatch (FLEET.PAS:861-905) checks stargate occupancy the way real
/// Pascal does (owner-blind: any gate, not just one owned by the fleet), via
/// <see cref="Galaxy.Galaxy.GetObjectAt"/>. The fuel model (<see cref="FleetLogistics"/>) uses the
/// real DATACNST.PAS constants. Starbase movement (<see cref="AdvanceStarbases"/>) is the real
/// obstacle-avoiding, fuel-costed <c>MovePlayerStarbases</c>/<c>GetNewBasePos</c> (SBASE.PAS), not a
/// straight-line stepper.
///
/// <paramref name="useLegacyOrderResolution"/> (Tui's own <c>appsettings.json</c> <c>UseLegacyOrderResolution</c>,
/// default false) reverts <see cref="ResolveOrders"/>'s own two-phase, cross-fleet, compile-time-has-no-side-effects
/// scheme entirely: <see cref="ResolveOrders"/> becomes a no-op, and a same-location
/// <see cref="CommandType.Destination"/> once again halts <see cref="ExecuteFleetOrdersStep"/>'s batch
/// exactly like Pascal's own DEST always does, rather than continuing into the next order the same
/// call -- leaving only the arrival-triggered <see cref="ExecuteFleetOrders"/> calls below as the sole
/// order-resolution path, at Pascal's own once-per-round cadence. The one fix this does <em>not</em>
/// revert either way is <see cref="ShouldAdvanceActingEmpireFleet"/>/<see cref="ShouldAdvanceNextEmpireFleet"/>
/// no longer excluding a null-<see cref="Fleet.Destination"/> fleet -- that's a plain correctness fix
/// (a stationary fleet handed a fresh order queue would otherwise never be visited at all, matching
/// neither Pascal nor any reasonable "legacy" reading of it), not a QoL deviation worth being able to
/// turn back off.
/// </summary>
public sealed class FleetMovementHandler(Random random, bool useLegacyOrderResolution = false) : IFleetMovementHandler
{
    private static readonly ShipType[] _mineableShipTypes = [ShipType.HunterKiller, ShipType.Jumpship, ShipType.Jumptransport];

    /// <summary>Compass steps No,Ne,Ea,Se,So,Sw,We,Nw in clockwise order (DATACNST.PAS:561-564's DirX/DirY) — index arithmetic mod 8 replaces Pascal's Succ/Pred-with-wraparound on the Directions enum.</summary>
    private static readonly Coordinate[] _compassSteps = [
        new(0, -1), new(1, -1), new(1, 0), new(1, 1),
        new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
    ];

    public void AdvanceFleets(Game game, Empire actingEmpire, Empire nextEmpire)
    {
        foreach (var fleet in game.Galaxy.Fleets.ToList()) {
            if (fleet.Owner == actingEmpire && ShouldAdvanceActingEmpireFleet(fleet, game)) {
                AdvanceFleet(fleet, game);
                continue;
            }

            if (fleet.Owner == nextEmpire && ShouldAdvanceNextEmpireFleet(fleet, game)) {
                AdvanceFleet(fleet, game);
            }
        }
    }

    /// <summary>
    /// MovePlayerStarbases (SBASE.PAS:203-263) — only command bases and fortresses ever move; a flat
    /// 100-trillum cost per turn (charged even when the step ends up blocked), obstacle-avoiding via
    /// <see cref="GetNewBasePos"/>. No per-turn movement-rate throttle exists in source (confirmed by
    /// checking <c>MovePlayerStarbases</c>'s only callers, ANACREON.PAS's main loop — always once per
    /// empire per turn, the same cadence as <see cref="AdvanceFleets"/>) — the previous
    /// <c>YearsUntilNextMove</c> field was an invented throttle, not ported behavior, and is removed.
    /// </summary>
    public void AdvanceStarbases(Game game, Empire empire)
    {
        const int moveCost = 100;

        foreach (var starbase in game.Galaxy.Starbases.ToList()) {
            if (starbase.Owner != empire || starbase.Status == FleetStatus.Lost) {
                continue;
            }

            if (starbase.Kind is not (StarbaseKind.CommandBase or StarbaseKind.Fortress)) {
                continue;
            }

            if (starbase.Destination is null) {
                continue;
            }

            var destination = starbase.Destination.Value;
            if (starbase.Location == destination) {
                starbase.Destination = null;
                starbase.Status = FleetStatus.Ready;
                continue;
            }

            if (starbase.Cargo.Trillum < moveCost) {
                starbase.Owner.AddNews(NewsType.StarbaseOutOfFuel, starbase);
                continue;
            }

            var newPos = GetNewBasePos(starbase.Location, destination, game);
            if (newPos is null) {
                starbase.Owner.AddNews(NewsType.StarbaseBlocked, starbase);
            } else {
                starbase.Location = newPos.Value;
            }

            starbase.Cargo.Trillum -= moveCost;

            if (starbase.Location == destination) {
                starbase.Destination = null;
                starbase.Status = FleetStatus.Ready;
            }
        }
    }

    private static bool ShouldAdvanceActingEmpireFleet(Fleet fleet, Game game)
    {
        if (fleet.Status == FleetStatus.Lost || fleet.Status == FleetStatus.Inactive) {
            return false;
        }

        return fleet.Type == FleetType.JumpFleet
            && game.Galaxy.GetObjectAt(fleet.Location) is not Stargate;
    }

    private static bool ShouldAdvanceNextEmpireFleet(Fleet fleet, Game game)
    {
        if (fleet.Status == FleetStatus.Lost || fleet.Status == FleetStatus.Inactive) {
            return false;
        }

        return fleet.Type is FleetType.Standard
            or FleetType.Penetrator
            or FleetType.AdvancedWarpFleet
            or FleetType.HunterKillerFleet
            || game.Galaxy.GetObjectAt(fleet.Location) is Stargate;
    }

    /// <summary>
    /// UpdateFleet (FLEET.PAS:646-859). Gate/fortress pass-through is checked before fuel consumption
    /// (matching source: a fortress hop's own <c>MoveFleet</c> happens before <c>UseUpFuel</c>), and a
    /// fortress hop that lands short of teleport range still runs this turn's ordinary
    /// <see cref="StepFleet"/> allotment afterward from its new position — both apply in the same turn,
    /// not one or the other (verified directly: <c>Teleport</c> stays <c>False</c> on that path, so
    /// control falls through to the normal step loop below it).
    ///
    /// A null <see cref="Fleet.Destination"/> (this port's own stand-in for Pascal's <c>Dest=FltXY</c> --
    /// no null coordinate there) hits the same branch as already-arrived, for the same reason: real
    /// Pascal's <c>UpdateFleet</c> tail-calls <c>ExecuteFleetOrders</c> unconditionally whenever the fleet
    /// is Ready (<c>IF (NOT FltDestroyed) AND (GetFleetStatus(FltID)=FReady) THEN ExecuteFleetOrders</c>,
    /// FLEET.PAS:855-858), regardless of whether it currently has anywhere to go --
    /// <see cref="ShouldAdvanceActingEmpireFleet"/>/<see cref="ShouldAdvanceNextEmpireFleet"/> used to
    /// exclude a null-Destination fleet from this loop entirely, which real Pascal's own
    /// <c>UpdateAllFleets</c> (FLEET.PAS:861-905) never does (it gates only on owner/fleet type). This fix
    /// itself is unconditional -- not part of <paramref name="useLegacyOrderResolution"/> -- since without
    /// it a fleet freshly handed an order queue while already stationary is never visited by anything.
    ///
    /// The tail call itself, though, only fires here when <paramref name="useLegacyOrderResolution"/> is
    /// true. When it's false (the default), this arrival moment doesn't execute anything at all beyond
    /// setting Ready/null-Destination -- <see cref="ResolveOrders"/>'s own next pass (the same
    /// <see cref="TurnEngine.EndTurn"/> call this arrival happened in, or the very next
    /// <see cref="TurnEngine.BeginTurn"/>) picks this fleet up instead, with its full cross-fleet Phase
    /// 1/2 treatment. Keeping this call active in both modes would defeat that treatment for exactly the
    /// case it matters most: a Transfer sitting right after the DEST that just resolved would execute
    /// immediately here, single-fleet, no deferral -- clamped needlessly if some other fleet's own
    /// same-turn order would have freed the room first, precisely the "visited in an unlucky order"
    /// problem Phase 1 exists to avoid. One accepted gap from skipping it: a
    /// <see cref="EmpireStatus.PendingElimination"/> empire's fleet never gets a <see cref="ResolveOrders"/>
    /// pass (gated on <see cref="EmpireStatus.Active"/>), so its queue simply stops progressing in
    /// non-legacy mode -- deliberate, since that empire is torn down at its own next
    /// <see cref="TurnEngine.BeginTurn"/> regardless.
    /// </summary>
    private void AdvanceFleet(Fleet fleet, Game game)
    {
        if (fleet.Status is FleetStatus.Lost or FleetStatus.Inactive)
            return;

        if (fleet.Destination is not { } destination || fleet.Location == destination) {
            fleet.Destination = null;
            fleet.Status = FleetStatus.Ready;
            if (useLegacyOrderResolution) {
                ExecuteFleetOrdersStep(fleet, game, allowWait: true, requireFullTransfer: false, legacyDestHalt: true);
            }

            return;
        }

        var teleporting = false;
        if (IsPassingThroughGate(fleet, fleet.Location, destination, game)) {
            teleporting = true;
        } else if (IsAtFortress(fleet.Location, game)) {
            if (fleet.Location.DistanceTo(destination) <= 5) {
                teleporting = true;
            } else {
                fleet.Location = HopThroughFortress(fleet.Location, destination, game);
            }
        }

        if (!ConsumeFuel(fleet))
            return;

        if (teleporting) {
            if (game.Galaxy.GetNebula(destination) == NebulaType.DenseNebula) {
                fleet.Owner.AddNews(NewsType.CannotGateToDenseNebula, fleet);
            } else {
                fleet.Location = destination;
                fleet.Destination = null;
                fleet.Status = FleetStatus.Ready;
                if (useLegacyOrderResolution) {
                    ExecuteFleetOrdersStep(fleet, game, allowWait: true, requireFullTransfer: false, legacyDestHalt: true);
                }
            }

            return;
        }

        var rate = FleetLogistics.MovementRate(fleet.Type);
        if (!StepFleet(fleet, destination, rate, game, out var nextLocation))
            return; // fleet destroyed by a minefield — caller's snapshot list still holds the reference, but nothing left to update

        fleet.Location = nextLocation;
        fleet.Status = fleet.Location == destination ? FleetStatus.Ready : FleetStatus.InTransit;

        if (fleet.Location == destination) {
            fleet.Destination = null;
            if (useLegacyOrderResolution) {
                ExecuteFleetOrdersStep(fleet, game, allowWait: true, requireFullTransfer: false, legacyDestHalt: true);
            }
        }
    }

    /// <summary>
    /// ExecuteFleetOrders (FLEET.PAS:562-617) -- called right after the three spots above that can
    /// leave a fleet <see cref="FleetStatus.Ready"/> this turn, matching Pascal's own single tail call
    /// in <c>UpdateFleet</c> exactly ("should be called after a fleet has reached its destination but
    /// before a player takes his/her turn"). This overload is that exact single-fleet, single-shot
    /// behavior, unchanged: a <see cref="CommandType.Transfer"/> that can't be fully satisfied still
    /// executes with today's own clamping rather than waiting, matching every existing caller (this is
    /// the one call site elsewhere in this file). See <see cref="ResolveOrders"/> for the cross-fleet,
    /// two-phase version the turn boundary itself uses instead.
    ///
    /// Runs commands starting at <see cref="Fleet.NextOrder"/> (1-based, matching Pascal) until one of
    /// three things happens: a <see cref="CommandType.Wait"/> command is reached (an explicit "skip a
    /// turn," always a real stop here since this overload always allows consuming it), a
    /// <see cref="CommandType.Destination"/> command leaves the fleet genuinely
    /// <see cref="FleetStatus.InTransit"/> (still real travel pending -- unlike Pascal, one that resolves
    /// to nowhere new, e.g. a DEST back to the fleet's own current location, is a same-call no-op and
    /// does not halt the batch), the list runs out (<see cref="Fleet.NextOrder"/> resets to 0 and
    /// <see cref="Fleet.Orders"/> is cleared -- Pascal's own <c>DisposeOrders</c>/<c>SetFleetCode</c>
    /// pairing), or the fleet is destroyed mid-execution (a <see cref="CommandType.Transfer"/> emptying
    /// the fleet via <see cref="FleetLifecycle.ChangeCompositionOfFleet"/>'s own abort-if-empty branch) --
    /// in which case <see cref="Fleet.NextOrder"/> is left untouched, matching Pascal's own
    /// <c>IF NOT FleetDestroyed THEN SetFleetNextStatement</c> guard. <see cref="CommandType.Repeat"/>
    /// jumps back to command 1 exactly once per call (<c>IgnoreRepeat</c>) so two adjacent Repeats
    /// can't spin forever.
    /// </summary>
    public static void ExecuteFleetOrders(Fleet fleet, Game game) =>
        ExecuteFleetOrdersStep(fleet, game, allowWait: true, requireFullTransfer: false, legacyDestHalt: false);

    /// <summary>
    /// The shared stepping engine behind both <see cref="ExecuteFleetOrders"/> (single-fleet, always
    /// allows WAIT, never defers a Transfer -- clamps immediately like Pascal always has) and
    /// <see cref="ResolveOrders"/>'s own two-phase cross-fleet cycling. <paramref name="allowWait"/>
    /// false leaves a WAIT command entirely untouched (not even "consumed" -- <see cref="Fleet.NextOrder"/>
    /// stays pointing at the WAIT itself, not past it), so only calls that pass true ever advance past
    /// one; see <see cref="ResolveOrders"/>'s own doc comment for why that's what makes WAIT cost exactly
    /// one full turn regardless of which pass first reaches it, with no extra per-fleet state needed.
    /// <paramref name="requireFullTransfer"/> true defers (also leaves <see cref="Fleet.NextOrder"/>
    /// untouched) any <see cref="CommandType.Transfer"/> whose full requested amount isn't currently
    /// available rather than clamping it -- <see cref="ResolveOrders"/>'s Phase 1 uses this to let other
    /// fleets' orders run first and potentially unblock it, instead of committing to a needlessly-partial
    /// transfer just because of visitation order. <paramref name="legacyDestHalt"/> true makes every
    /// <see cref="CommandType.Destination"/> halt the batch unconditionally, matching Pascal's own DEST
    /// exactly -- only <see cref="FleetMovementHandler"/>'s own legacy-mode arrival calls pass true; every
    /// other caller passes false, letting a same-location DEST continue into the next order the same call.
    /// </summary>
    /// <returns>Whether this call executed at least one command (advanced <see cref="Fleet.NextOrder"/> or mutated game state) -- <see cref="ResolveOrders"/>'s Phase 1 cycles until no active fleet makes any further progress.</returns>
    private static bool ExecuteFleetOrdersStep(Fleet fleet, Game game, bool allowWait, bool requireFullTransfer, bool legacyDestHalt)
    {
        if (fleet.NextOrder == 0)
            return false;

        var com = fleet.NextOrder;
        var lastCommand = fleet.Orders.Count;
        var ignoreRepeat = false;
        var stopsHere = false;
        var madeProgress = false;

        do {
            var command = fleet.Orders[com - 1];
            var type = command.Type;

            if (type == CommandType.Wait && !allowWait) {
                break; // NextOrder stays exactly here -- not consumed by this call at all
            }

            if (type == CommandType.Transfer && requireFullTransfer && !CanFullyTransfer(fleet, command, game)) {
                break; // ditto -- left for a later Phase 1 sweep, or Phase 2's clamped fallback
            }

            if (requireFullTransfer && type is CommandType.Refuel or CommandType.Join) {
                break; // Phase 1 never touches these at all -- see ResolveOrders' own doc comment: they
                       // always "succeed" (no target to compare against), so Phase 2 handles them, once,
                       // strictly after every fully-satisfiable Transfer has already settled
            }

            switch (type) {
                case CommandType.Destination:
                    ExecuteDestCOM(fleet, command);
                    break;
                case CommandType.Transfer:
                    ExecuteTransCOM(fleet, command, game);
                    break;
                case CommandType.Refuel:
                    ExecuteRefuCOM(fleet, game);
                    break;
                case CommandType.Join:
                    ExecuteJoinCOM(fleet, command, game);
                    break;
            }

            madeProgress = true;

            if (!game.Galaxy.Fleets.Contains(fleet)) {
                return true; // fleet destroyed by ExecuteTransCOM's ChangeCompositionOfFleet or ExecuteJoinCOM -- NextOrder stays whatever it was, matching Pascal
            }

            stopsHere = type == CommandType.Wait
                || (type == CommandType.Destination && (legacyDestHalt || fleet.Status == FleetStatus.InTransit));

            if (type == CommandType.Repeat && !ignoreRepeat) {
                com = 1;
                ignoreRepeat = true;
            } else if (com < lastCommand) {
                com++;
            } else {
                com = 0;
                fleet.Orders.Clear();
            }
        } while (!stopsHere && com != 0);

        fleet.NextOrder = com;
        return madeProgress;
    }

    /// <summary>Would <see cref="ExecuteTransCOM"/>'s own <see cref="ClampTransfer"/> move the full requested amount right now, with nothing clamped away? Read-only (no clone/mutate) -- <see cref="ResolveOrders"/>'s Phase 1 uses this to decide whether to defer a Transfer for a later sweep instead of executing it partially.</summary>
    private static bool CanFullyTransfer(Fleet fleet, FleetOrder command, Game game)
    {
        if (game.Galaxy.GetObjectAt(fleet.Location) is not IEconomicWorld ground || ground.Owner != fleet.Owner) {
            return true; // ExecuteTransCOM itself no-ops here too -- nothing waiting could ever change that
        }

        if (command.TransferShip is { } shipType) {
            return ClampTransfer(command.TransferAmount, fleet.Ships[shipType], ground.Ships[shipType], cargoSpacePerUnit: null, fleet.Ships, fleet.Cargo) == command.TransferAmount;
        }

        if (command.TransferCargo is { } cargoType) {
            return ClampTransfer(command.TransferAmount, fleet.Cargo[cargoType], ground.Cargo[cargoType], FleetLogistics.CargoSpacePerUnit[cargoType], fleet.Ships, fleet.Cargo) == command.TransferAmount;
        }

        return true; // a compiled Transfer order always sets exactly one of the two -- defensive only, matching ExecuteTransCOM's own fallback
    }

    /// <summary>
    /// Replaces a fleet's order queue -- no side effects, matching Pascal's own <c>FleetOrdersCommand</c>
    /// (<c>FLTCOMM.PAS:843-915</c>, confirmed: it only ever calls <c>SetFleetNextStatement</c>/
    /// <c>SetFleetCode</c>, never anything that executes an order). Resolution happens only at the two
    /// dedicated turn-boundary points (see <see cref="ResolveOrders"/>), never at compile time -- so
    /// recompiling a fleet's orders can never itself run anything, remove anything from the queue's
    /// displayed text, or move a resource, regardless of how many times it's done before the turn ends.
    /// An empty <paramref name="orders"/> list is a plain cancel (<see cref="Fleet.NextOrder"/> resets to
    /// 0 regardless of <paramref name="startAt"/>), matching <c>GameShell.CancelFleetOrders</c>/Pascal's
    /// own <c>FleetCancelOrdersCommand</c>.
    /// </summary>
    public static void CommitOrders(Fleet fleet, IReadOnlyList<FleetOrder> orders, int startAt, Game game)
    {
        fleet.Orders.Clear();
        fleet.Orders.AddRange(orders);
        fleet.NextOrder = orders.Count == 0 ? 0 : startAt;
    }

    /// <summary>
    /// Resolves as much of every stationary (Ready, no <see cref="Fleet.Destination"/>) order-bearing
    /// fleet <paramref name="empire"/> owns as it can, right now -- called once immediately before that
    /// empire's own turn and once immediately after it ends (<see cref="TurnEngine.BeginTurn"/>/
    /// <see cref="TurnEngine.EndTurn"/>), deliberately not at order-compile time (see
    /// <see cref="CommitOrders"/>'s own doc comment): orders are for cutting tedium, not a difficulty
    /// gate, but a queue a player just finished editing shouldn't visibly jump ahead of them while
    /// they're still looking at it either. Physical fleet stepping (how many sectors a fleet advances
    /// this round) is untouched -- still <see cref="AdvanceFleet"/>'s own Pascal-derived type/owner
    /// schedule; this only ever touches fleets that aren't already <see cref="FleetStatus.InTransit"/>.
    ///
    /// <para>
    /// <b>WAIT always costs exactly one full turn.</b> <paramref name="allowWait"/> is false for the
    /// post-turn call, so a WAIT reached there is left completely untouched (see
    /// <see cref="ExecuteFleetOrdersStep"/>) -- only the pre-turn call (and the pre-existing
    /// arrival-triggered <see cref="ExecuteFleetOrders"/> calls inside <see cref="AdvanceFleet"/>, which
    /// already only ever run once per round) can advance past one. That holds regardless of which pass
    /// first reaches a given WAIT, with no per-fleet flag needed: whichever pass is running is either
    /// allowed to touch WAIT or it isn't, full stop.
    /// </para>
    ///
    /// <para>
    /// <b>Two-phase, so cross-fleet resource ordering finds the least-surprising outcome</b> -- the one
    /// a player interleaving these same transfers by hand would have reached, not whatever a fleet
    /// happens to be clamped by if it's visited in an unlucky order. Two concrete cases motivate this:
    /// a same-location Transfer dropoff that would clamp against a full 9999 stack succeeding instead
    /// once another fleet's same-turn pickup frees room, and a Refuel seeing another fleet's same-turn
    /// trillum dropoff land before it tops off rather than missing it by a hair of visitation order.
    /// Clamping never destroys anything either way (confirmed against <see cref="ClampTransfer"/>: a
    /// clamped amount simply stays wherever it started), so there's no "lost" resource to recover --
    /// this is purely about finding a good order, not preventing loss.
    /// </para>
    ///
    /// <para>
    /// Phase 1 repeatedly sweeps every active fleet, advancing through <see cref="CommandType.Destination"/>
    /// (a same-location one resolves and the fleet keeps going the same sweep) and any
    /// <see cref="CommandType.Transfer"/> whose full request is satisfiable <em>right now</em>, until a
    /// full sweep makes no further progress across every fleet -- a Transfer that would still need
    /// clamping is left untouched rather than run partially, so a later sweep (after some other fleet's
    /// order changes what's available) gets a real chance to satisfy it in full.
    /// </para>
    ///
    /// <para>
    /// Phase 2 is one final best-effort sweep over whatever's still pending once Phase 1 can make no more
    /// progress: <see cref="CommandType.Refuel"/> and <see cref="CommandType.Join"/> always "succeed" by
    /// design (they request whatever's available, never a fixed target to compare against, so they can
    /// never usefully "wait" the way Transfer can) and any Transfer that still can't fully satisfy its
    /// request finally takes today's own clamp -- identical to <see cref="ExecuteFleetOrders"/>'s
    /// single-shot behavior, just deferred to the best moment this turn boundary can offer instead of an
    /// arbitrary one.
    /// </para>
    /// </summary>
    public void ResolveOrders(Game game, Empire empire, bool allowWait)
    {
        if (useLegacyOrderResolution) {
            return; // this whole mechanism is off -- see the class doc comment; the arrival-triggered
                     // calls inside AdvanceFleet are the only order-resolution path in legacy mode
        }

        // A fleet whose *current* command is WAIT gets exactly one look this whole call (consumed if
        // allowWait, left untouched otherwise) and is then never revisited again this call, no matter
        // how many more sweeps happen -- without this, Phase 1's own cycle-to-fixed-point would revisit
        // it on the very next sweep (nothing about consuming a WAIT changes Destination/Status, so
        // ActiveFleets' own filter doesn't naturally exclude it the way a genuine-travel DEST does) and
        // let whatever comes right after the WAIT execute in this same call -- exactly what WAIT costing
        // a full turn is supposed to prevent.
        var settledOnWaitThisCall = new HashSet<Fleet>();
        bool progressed;

        do {
            progressed = false;

            foreach (var fleet in ActiveFleets(game, empire)) {
                if (settledOnWaitThisCall.Contains(fleet)) {
                    continue;
                }

                var isWait = fleet.Orders[fleet.NextOrder - 1].Type == CommandType.Wait;

                if (ExecuteFleetOrdersStep(fleet, game, allowWait, requireFullTransfer: true, legacyDestHalt: false)) {
                    progressed = true;
                }

                if (isWait) {
                    settledOnWaitThisCall.Add(fleet);
                }
            }
        } while (progressed);

        foreach (var fleet in ActiveFleets(game, empire)) {
            if (settledOnWaitThisCall.Contains(fleet)) {
                continue;
            }

            ExecuteFleetOrdersStep(fleet, game, allowWait, requireFullTransfer: false, legacyDestHalt: false);
        }
    }

    /// <summary>
    /// Snapshotted (not a live view) since <see cref="ResolveOrders"/>'s own execution can destroy fleets
    /// (a Transfer emptying one via <see cref="FleetLifecycle.ChangeCompositionOfFleet"/>) or otherwise
    /// mutate <see cref="Game.Galaxy"/>'s own fleet collection while iterating.
    ///
    /// <see cref="Fleet.Status"/> alone is the right test here, not an additional
    /// <c>Destination is null</c> check -- <see cref="FleetLifecycle.SetFleetDestination"/> always keeps
    /// them in sync (<c>Ready</c> exactly when <c>Destination is null || Destination == Location</c>), so
    /// a redundant Destination check isn't just extra, it's wrong: a same-location DEST (Resupply's own
    /// leading no-op back to wherever the fleet already sits) sets Destination to that same coordinate,
    /// not null, while correctly leaving Status Ready. A prior version of this filter checked
    /// <c>Destination is null</c> too and silently dropped exactly this fleet from every later Phase 1
    /// sweep and Phase 2 the moment its next order (a Transfer needing more cargo space than its ship
    /// composition allows) got deferred rather than executing immediately -- found live: a Resupply
    /// request larger than the fleet's own cargo capacity left it stuck at NextOrder 2 forever, since
    /// Phase 2 (which would have clamped it and moved on to the real destination) never got to see it.
    /// </summary>
    private static List<Fleet> ActiveFleets(Game game, Empire empire) =>
        game.Galaxy.Fleets.Where(f => f.Owner == empire && f.Status == FleetStatus.Ready && f.NextOrder > 0).ToList();

    /// <summary>ExecuteDestCOM (FLEET.PAS:492-497) -- a target object's own current location wins over the raw compiled coordinate (matching Pascal's own <c>IF SameXY(Loc.XY,Limbo) THEN GetCoord(Loc.ID,Loc.XY)</c>), so a DEST order aimed at a starbase still tracks it if that starbase has since moved.</summary>
    private static void ExecuteDestCOM(Fleet fleet, FleetOrder command)
    {
        var destination = command.DestinationObject?.Location ?? command.DestinationPosition!.Value;
        FleetLifecycle.SetFleetDestination(fleet, destination);
    }

    /// <summary>
    /// ExecuteTransCOM (FLEET.PAS:515-560) -- transfers between the fleet and whatever's at its own
    /// current location, silently clamped to whatever's actually available/fits rather than failing
    /// outright (there's no player present to retry on error, unlike <see cref="ResourceDistribution.TryTransfer"/>'s
    /// interactive validate-then-move). A ground that isn't the player's own (or isn't a
    /// <see cref="IEconomicWorld"/> at all -- <see cref="Galaxy.Galaxy.GetObjectAt"/> never returns a
    /// <see cref="Fleet"/>, matching real Pascal's own ground/fleet split) is a silent no-op, matching
    /// Pascal's own <c>GetStatus(GroundID)=GetStatus(FltID)</c> guard.
    /// </summary>
    private static void ExecuteTransCOM(Fleet fleet, FleetOrder command, Game game)
    {
        if (game.Galaxy.GetObjectAt(fleet.Location) is not IEconomicWorld ground || ground.Owner != fleet.Owner) {
            return;
        }

        var newFleetShips = CombatEngine.CloneShips(fleet.Ships);
        var newFleetCargo = CombatEngine.CloneCargo(fleet.Cargo);
        var newGroundShips = CombatEngine.CloneShips(ground.Ships);
        var newGroundCargo = CombatEngine.CloneCargo(ground.Cargo);

        if (command.TransferShip is { } shipType) {
            var trans = ClampTransfer(command.TransferAmount, newFleetShips[shipType], newGroundShips[shipType], cargoSpacePerUnit: null, newFleetShips, newFleetCargo);
            newFleetShips[shipType] += trans;
            newGroundShips[shipType] -= trans;
        } else if (command.TransferCargo is { } cargoType) {
            var trans = ClampTransfer(command.TransferAmount, newFleetCargo[cargoType], newGroundCargo[cargoType], FleetLogistics.CargoSpacePerUnit[cargoType], newFleetShips, newFleetCargo);
            newFleetCargo[cargoType] += trans;
            newGroundCargo[cargoType] -= trans;
        } else {
            return; // a compiled Transfer order always sets exactly one of the two -- defensive only
        }

        FleetLogistics.BalanceFleet(newFleetShips, newFleetCargo);
        FleetLifecycle.ChangeCompositionOfFleet(fleet, ground, newFleetShips, newFleetCargo, newGroundShips, newGroundCargo, game);
    }

    /// <summary>
    /// No ORDERS.PAS token -- this port's own addition (see <see cref="FleetOrderCompiler"/>'s doc
    /// comment on <c>REFU</c>). Same ground guard as <see cref="ExecuteTransCOM"/> (only a same-owner
    /// <see cref="IEconomicWorld"/>, i.e. a <see cref="Planet"/>, at the fleet's own location), then
    /// tops the fleet's tank off with as much of the ground's trillum as it needs -- exactly the
    /// Tui's own interactive Refuel command, just automated for an order queue via the same
    /// <see cref="FleetLifecycle.RefuelFleet"/>/<see cref="FleetLifecycle.MaxTrillumToRefuel"/> pair
    /// the NPE AI's own <c>RefuelBMS</c> mission already calls.
    /// </summary>
    private static void ExecuteRefuCOM(Fleet fleet, Game game)
    {
        if (game.Galaxy.GetObjectAt(fleet.Location) is not IEconomicWorld ground || ground.Owner != fleet.Owner) {
            return;
        }

        FleetLifecycle.RefuelFleet(fleet, ground, FleetLifecycle.MaxTrillumToRefuel(fleet, ground));
    }

    private const string HoldingNamePrefix = "Holding-";

    /// <summary>
    /// No ORDERS.PAS token -- port-only, same precedent as <see cref="ExecuteRefuCOM"/>. Production
    /// redirection's own "join on arrival" option compiles this as the one order a freshly-dispatched
    /// fleet is born with (<see cref="Entities.ProductionRedirection.Apply"/>), so it runs the moment
    /// that fleet arrives -- but it's a real order like any other, so a player can queue it by hand too
    /// (JOIN / JOIN OVER, <see cref="FleetOrderCompiler"/>). Not to be confused with the existing
    /// player-driven "Abort/Join" command (<c>Tui.GameShell.AbortJoinFleet</c>) -- that's an on-demand
    /// merge to whatever's at the fleet's current location, always via the unclamped
    /// <see cref="FleetLifecycle.AbortFleet"/>; this is the automatic, arrival-triggered version with
    /// its own clamp/no-clamp choice (<see cref="FleetOrder.PreserveOverflow"/>).
    /// </summary>
    private static void ExecuteJoinCOM(Fleet fleet, FleetOrder command, Game game)
    {
        if (game.Galaxy.GetObjectAt(fleet.Location) is IEconomicWorld world && world.Owner == fleet.Owner) {
            JoinWorld(fleet, world, command.PreserveOverflow, game);
            return;
        }

        JoinHoldingFleets(fleet, game);
    }

    /// <summary>
    /// The owned-world half of <see cref="ExecuteJoinCOM"/>. <paramref name="preserveOverflow"/> false
    /// clamps at <see cref="PascalMath.MaxResources"/> per type -- a plain "top it off" merge, losing
    /// whatever doesn't fit; true never loses anything, spilling whatever doesn't fit into this
    /// empire's own Holding-N fleets in the same sector instead (<see cref="JoinHoldingFleets"/>) --
    /// deliberately not <see cref="FleetLifecycle.AbortFleet"/>, which neither clamps nor spills
    /// overflow anywhere, just lets the world read over the cap forever.
    /// </summary>
    private static void JoinWorld(Fleet fleet, IEconomicWorld world, bool preserveOverflow, Game game)
    {
        // Fuel has no meaning on a world -- convert to trillum first, same conversion
        // CombatOutcome.AbortFleet's own world branch already does.
        fleet.Cargo.Trillum = ClampResource(fleet.Cargo.Trillum + ClampResource(fleet.Fuel / FleetLogistics.FuelPerTon));
        fleet.Fuel = 0;

        if (!preserveOverflow) {
            foreach (var t in Enum.GetValues<ShipType>())
                world.Ships[t] = ClampResource(world.Ships[t] + fleet.Ships[t]);
            foreach (var t in Enum.GetValues<CargoType>())
                world.Cargo[t] = ClampResource(world.Cargo[t] + fleet.Cargo[t]);

            CombatOutcome.DestroyFleet(fleet, game);
            return;
        }

        MergeUpToCapacity(fleet, world);
        if (IsDrained(fleet)) {
            CombatOutcome.DestroyFleet(fleet, game);
            return;
        }

        JoinHoldingFleets(fleet, game); // whatever didn't fit spills into Holding-N, never destroyed
    }

    /// <summary>
    /// Moves as much of <paramref name="source"/>'s ships/cargo onto <paramref name="target"/> as fits
    /// under <see cref="PascalMath.MaxResources"/> per type, leaving any remainder on
    /// <paramref name="source"/>. The same bound <see cref="ClampTransfer"/>'s own drop-off clamp
    /// already applies for a manual Transfer order -- not a new cap, the established "no stack of a
    /// resource type exceeds 9999 anywhere" convention. Generic over <see cref="IShipCargoHolder"/> so
    /// it works for both "merge into a world" (<see cref="JoinWorld"/>) and "merge into a holding
    /// fleet" (<see cref="MergeIntoHoldingFleet"/>) unchanged.
    /// </summary>
    private static void MergeUpToCapacity(IShipCargoHolder source, IShipCargoHolder target)
    {
        foreach (var t in Enum.GetValues<ShipType>()) {
            var move = Math.Min(source.Ships[t], PascalMath.MaxResources - target.Ships[t]);
            target.Ships[t] += move;
            source.Ships[t] -= move;
        }
        foreach (var t in Enum.GetValues<CargoType>()) {
            var move = Math.Min(source.Cargo[t], PascalMath.MaxResources - target.Cargo[t]);
            target.Cargo[t] += move;
            source.Cargo[t] -= move;
        }
    }

    /// <summary>
    /// The not-owned half of <see cref="ExecuteJoinCOM"/>, and <see cref="JoinWorld"/>'s own overflow
    /// spillover target: consolidates <paramref name="fleet"/> into this empire's own "Holding-N"
    /// fleets already in this sector, lowest N first, spilling into newly created ones as needed, until
    /// <paramref name="fleet"/> is fully drained -- always terminates in practice after at most one
    /// freshly-created (empty, so up to 9999-per-type headroom) Holding fleet, since a single tick's
    /// redirected production can't realistically exceed that, but is correct for any magnitude.
    /// </summary>
    private static void JoinHoldingFleets(Fleet fleet, Game game)
    {
        var owner = fleet.Owner;
        var location = fleet.Location;

        var holdingFleets = game.Galaxy.Fleets
            .Where(f => f != fleet && f.Owner == owner && f.Location == location && HoldingNumber(f, owner) is not null)
            .OrderBy(f => HoldingNumber(f, owner)!.Value)
            .ToList();

        var nextNumber = holdingFleets.Count == 0 ? 1 : holdingFleets.Max(f => HoldingNumber(f, owner)!.Value) + 1;

        foreach (var holding in holdingFleets) {
            MergeIntoHoldingFleet(fleet, holding);
            if (IsDrained(fleet)) {
                CombatOutcome.DestroyFleet(fleet, game);
                return;
            }
        }

        while (true) {
            var holding = new Fleet { Location = location, Owner = owner };
            holding.Names[owner] = $"{HoldingNamePrefix}{nextNumber}";
            nextNumber++;
            game.Galaxy.Fleets.Add(holding);

            MergeIntoHoldingFleet(fleet, holding);
            if (IsDrained(fleet)) {
                CombatOutcome.DestroyFleet(fleet, game);
                return;
            }
        }
    }

    /// <summary>This owner's own name for <paramref name="f"/> parsed as "Holding-&lt;n&gt;", or null if it doesn't match -- per-viewer names (<see cref="ISectorObject.Names"/>), so only this owner's own naming counts.</summary>
    private static int? HoldingNumber(Fleet f, Empire owner) =>
        f.Names.TryGetValue(owner, out var name) && name.StartsWith(HoldingNamePrefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(HoldingNamePrefix.Length), out var n) ? n : null;

    /// <summary>Fuel is added to whichever holding fleet is reached first -- zeroing <paramref name="source"/>'s own Fuel immediately makes every later call's fuel line a harmless no-op instead of needing a separate "already placed" flag.</summary>
    private static void MergeIntoHoldingFleet(Fleet source, Fleet target)
    {
        MergeUpToCapacity(source, target);
        target.Fuel += source.Fuel;
        source.Fuel = 0;
    }

    private static bool IsDrained(Fleet f) =>
        FleetLifecycle.NoShips(f.Ships) && Enum.GetValues<CargoType>().All(t => f.Cargo[t] == 0);

    /// <summary>
    /// The pickup (<paramref name="trans"/>&gt;0)/drop-off clamp cascade from <c>ExecuteTransCOM</c>
    /// itself (<c>LesserInt</c> chains, FLEET.PAS:537-552): a pickup is bounded by what's on the ground,
    /// by the fleet's own <see cref="PascalMath.MaxResources"/> headroom, and -- cargo only, ships take
    /// up their own hull rather than cargo space -- by <see cref="FleetLogistics.FleetCargoSpace"/>; a
    /// drop-off is bounded by what the fleet has and the ground's own headroom.
    /// </summary>
    private static int ClampTransfer(int trans, int fleetAmount, int groundAmount, int? cargoSpacePerUnit, ShipCounts fleetShips, CargoHold fleetCargo)
    {
        if (trans > 0) {
            trans = Math.Min(trans, groundAmount);
            trans = Math.Min(trans, PascalMath.MaxResources - fleetAmount);
            if (cargoSpacePerUnit is { } perUnit) {
                trans = Math.Min(FleetLogistics.FleetCargoSpace(fleetShips, fleetCargo) * perUnit, trans);
            }
        } else {
            trans = -trans;
            trans = Math.Min(trans, fleetAmount);
            trans = Math.Min(trans, PascalMath.MaxResources - groundAmount);
            trans = -trans;
        }
        return trans;
    }

    /// <summary>
    /// PassingThroughGate (FLEET.PAS:832-861) — true if the fleet's current cell holds a public Gate
    /// (any destination teleports), or a private WarpLink whose exact destination cell is itself a
    /// same-owner Gate/WarpLink the fleet's own owner already knows about. This is real Pascal's whole
    /// check — an owner/known-ness match against whatever sits at <paramref name="destination"/>, not
    /// a stored link pointer, so <see cref="Stargate.LinkedTo"/> (a separate, not-yet-ported linking
    /// mechanic) plays no part here.
    /// </summary>
    public static bool IsPassingThroughGate(Fleet fleet, Coordinate origin, Coordinate destination, Game game)
    {
        if (game.Galaxy.GetObjectAt(origin) is not Stargate gate)
            return false;

        if (gate.Kind == StargateKind.Gate)
            return true;

        if (gate.Kind != StargateKind.WarpLink)
            return false;

        return game.Galaxy.GetObjectAt(destination) is Stargate destinationGate
            && destinationGate.Kind is StargateKind.Gate or StargateKind.WarpLink
            && destinationGate.Owner == gate.Owner
            && fleet.Owner.Stargates.Known.Contains(destinationGate);
    }

    public static bool IsAtFortress(Coordinate location, Game game) =>
        game.Galaxy.GetObjectAt(location) is Starbase { Kind: StarbaseKind.Fortress };

    /// <summary>
    /// GetNewPos (FLEET.PAS:437-450) — a single Sgn-toward-destination step, or null if the candidate
    /// cell is dense nebula. Pascal's own version writes a sentinel coordinate (<c>Limbo=(0,0)</c>)
    /// into its VAR parameter instead of returning null; ported as a nullable return here since this
    /// port has no equivalent sentinel need — see <see cref="StepFleet"/>'s own doc comment for why
    /// that's observationally identical, not just a convenient rewrite.
    /// </summary>
    public static Coordinate? GetNewPos(Coordinate current, Coordinate destination, Game game)
    {
        var dx = Math.Sign(destination.X - current.X);
        var dy = Math.Sign(destination.Y - current.Y);
        var candidate = current with { X = current.X + dx, Y = current.Y + dy };

        return game.Galaxy.GetNebula(candidate) == NebulaType.DenseNebula ? null : candidate;
    }

    /// <summary>
    /// The far-from-destination branch of fortress pass-through (FLEET.PAS:768-779) — up to 5
    /// <see cref="GetNewPos"/> steps toward the destination, silently stopping at the first
    /// dense-nebula block. No <c>FltBlocked</c> news fires here, unlike <see cref="StepFleet"/>'s own
    /// nebula check — a real asymmetry in source (this branch never calls the news-firing code path
    /// at all), not something to "fix" for consistency.
    /// </summary>
    private static Coordinate HopThroughFortress(Coordinate start, Coordinate destination, Game game)
    {
        var current = start;

        for (var step = 0; step < 5; step++) {
            if (current == destination)
                break;

            if (GetNewPos(current, destination, game) is not { } next)
                break;

            current = next;
        }

        return current;
    }

    /// <summary>
    /// The FOR j:=1 TO FltMovementRate[FltTyp] step loop (FLEET.PAS:801-838) — steps one cell at a time
    /// toward the destination, checking dense-nebula blocking every step (every fleet type: Pascal's
    /// own <c>GetNewPos</c>) and minefields/disrupters every step for JumpFleet/HunterKillerFleet only
    /// (Pascal's <c>FltTyp IN [JumpFleet,HKFleet]</c> gate). Checks nebula *before* mines/disrupters —
    /// the reverse of source's literal order, but observationally identical: Pascal's own order checks
    /// hazards against a possibly-already-nebula-blocked <c>NewPos</c> (the sentinel coordinate
    /// <c>Limbo=(0,0)</c>), which is never a real placed coordinate in a 1-indexed galaxy (confirmed:
    /// this port's own placement code only ever draws <c>Rnd(1,Size)</c>), so no real mine or disrupter
    /// gate is ever found there — checking nebula first and skipping the hazard check on a block is the
    /// same outcome without porting the sentinel-coordinate trick. A mine or disrupter hit stops
    /// movement for the rest of this turn's allowance, but the fleet still lands on the cell where it
    /// was hit (Pascal's <c>MoveFleet(FltID,NewPos)</c> after <c>ExitMoveLoop</c> uses whatever NewPos
    /// the loop last computed); a nebula block instead reverts to the last cell before it and fires
    /// <c>FltBlocked</c>. Returns false only if the fleet was destroyed by a minefield.
    /// </summary>
    private bool StepFleet(Fleet fleet, Coordinate destination, int rate, Game game, out Coordinate nextLocation)
    {
        var checkHazards = fleet.Type is FleetType.JumpFleet or FleetType.HunterKillerFleet;
        var current = fleet.Location;

        for (var step = 0; step < rate; step++) {
            if (current == destination)
                break;

            if (GetNewPos(current, destination, game) is not { } candidate) {
                fleet.Owner.AddNews(NewsType.FleetBlocked, fleet);
                break;
            }

            if (checkHazards) {
                var minedBy = game.Galaxy.GetMineOwner(candidate);
                if (minedBy is not null && !minedBy.IsIndependent && minedBy != fleet.Owner) {
                    var survived = ApplyMineFieldDamage(fleet, minedBy, game);
                    minedBy.AddNews(NewsType.EnemyFleetDamagedInMinefield, position: candidate, otherEmpire: fleet.Owner);
                    game.Galaxy.MarkMineScouted(fleet.Owner, candidate);

                    nextLocation = candidate;
                    return survived;
                }

                if (InRangeOfDisrupter(fleet.Owner, candidate, game, out var disruptedBy)) {
                    fleet.Owner.AddNews(NewsType.FleetStoppedByDisrupter, fleet, otherEmpire: disruptedBy);
                    nextLocation = candidate;
                    return true;
                }
            }

            current = candidate;
        }

        nextLocation = current;
        return true;
    }

    /// <summary>
    /// MineFieldDamage (FLEET.PAS:705-746). Only HunterKiller/Jumpship/Jumptransport ships take damage
    /// (Pascal's <c>hkr TO jtn</c> range) — the only ship types a JumpFleet/HunterKillerFleet can ever
    /// carry in the first place (<see cref="Fleet.Type"/>'s own derivation), so checking just those
    /// three for "anything left" is equivalent to Pascal's full <c>NoShips</c> scan here. BalanceFleet's
    /// post-damage cargo rebalance isn't ported on the survives branch — this method only ever removes
    /// ships, never cargo, so nothing here could exceed a capacity anyway. FleetNameDestruction needs
    /// no equivalent here either, same reasoning as <see cref="Combat.CombatOutcome.AbortFleet"/>'s
    /// own remarks.
    /// </summary>
    private bool ApplyMineFieldDamage(Fleet fleet, Empire minedBy, Game game)
    {
        var owner = fleet.Owner;
        var ships = fleet.Ships;
        var destroyed = new ShipCounts();

        foreach (var t in _mineableShipTypes) {
            var count = ships[t];
            destroyed[t] = Math.Min(count, Rnd(random, 1, 100) + PascalRound(count * ((CombatConstants.ProtecNeeded[t] + 20) / 100.0)));
        }

        var survives = _mineableShipTypes.Any(t => ships[t] - destroyed[t] > 0);
        if (survives) {
            owner.AddNews(NewsType.FleetDamagedByMines, fleet, otherEmpire: minedBy);
            foreach (var t in _mineableShipTypes) {
                ships[t] -= destroyed[t];
            }
        } else {
            owner.AddNews(NewsType.FleetDestroyedByMines, fleet, otherEmpire: minedBy);
            CombatOutcome.DestroyFleet(fleet, game);
        }

        foreach (var t in _mineableShipTypes) {
            if (destroyed[t] > 0) {
                owner.AddNews(NewsType.DestructionDetail, fleet, p1: destroyed[t], resource: new ResourceKind.Ship(t));
            }
        }

        return survives;
    }

    /// <summary>InRangeOfDisrupter (FLEET.PAS:619-644) — any enemy-owned disrupter gate within Chebyshev distance 3.</summary>
    private static bool InRangeOfDisrupter(Empire owner, Coordinate position, Game game, out Empire? disruptedBy)
    {
        foreach (var gate in game.Galaxy.Stargates) {
            if (gate.Kind == StargateKind.Disrupter && gate.Owner != owner && gate.Location.DistanceTo(position) <= 3) {
                disruptedBy = gate.Owner;
                return true;
            }
        }

        disruptedBy = null;
        return false;
    }

    /// <summary>
    /// UseUpFuel (FLEET.PAS:660-703) — burns a year's <see cref="FleetLogistics.FuelConsumption"/>,
    /// converting the fleet's own cargo trillum into fuel (<see cref="FleetLogistics.FuelPerTon"/>,
    /// capped at <see cref="FleetLogistics.FuelCapacity"/>) and retrying when short, exactly as
    /// Pascal's own recursive retry does — ported as a loop instead of recursion, same guaranteed
    /// termination (each retry converts at least 1 ton, so <c>Cargo.Trillum</c> strictly decreases).
    /// Only fires <see cref="NewsType.FleetOutOfFuel"/> and deactivates when cargo trillum is fully
    /// exhausted too; does not clear <see cref="Fleet.Destination"/> — Pascal's own UseUpFuel never
    /// touches it, so a refueled-later fleet still remembers where it was headed.
    /// </summary>
    private static bool ConsumeFuel(Fleet fleet)
    {
        while (true) {
            var consumption = FleetLogistics.FuelConsumption(fleet.Ships, fleet.Cargo);
            if (fleet.Fuel >= consumption) {
                fleet.Fuel -= consumption;
                return true;
            }

            if (fleet.Cargo.Trillum <= 0) {
                fleet.Owner.AddNews(NewsType.FleetOutOfFuel, fleet);
                fleet.Status = FleetStatus.Inactive;
                return false;
            }

            var trillumToConvert = Math.Max(1, Math.Min(fleet.Cargo.Trillum, PascalRound(consumption / (double)FleetLogistics.FuelPerTon)));
            fleet.Cargo.Trillum -= trillumToConvert;
            fleet.Fuel = Math.Min(fleet.Fuel + trillumToConvert * FleetLogistics.FuelPerTon, FleetLogistics.FuelCapacity(fleet.Ships));
        }
    }

    /// <summary>
    /// GetNewBasePos (SBASE.PAS:103-191) — obstacle-avoiding single-cell step toward the destination.
    /// Unlike fleets' plain nebula-only block, a starbase also blocks on any occupied cell (a planet,
    /// another starbase, a stargate, or a construction site — <see cref="Galaxy.Galaxy.GetObjectAt"/>),
    /// and tries the two compass directions adjacent to the direct heading before giving up for the
    /// turn — each alternate is only accepted if both the hop itself *and* its own next step toward
    /// the destination are clear, matching source's <c>PosTest</c>/<c>PosTest2</c> double check.
    /// Returns null if every direction tried is blocked (the base stays put this turn).
    /// </summary>
    private static Coordinate? GetNewBasePos(Coordinate position, Coordinate destination, Game game)
    {
        var direct = TentativeStep(position, destination, game);
        if (direct is { } directPosition)
            return directPosition;

        var heading = CompassIndex(position, destination);
        int[] alternates = [(heading + 1) % 8, (heading + 7) % 8];

        foreach (var direction in alternates) {
            var step = _compassSteps[direction];
            var adjacent = position with { X = position.X + step.X, Y = position.Y + step.Y };

            if (game.Galaxy.GetNebula(adjacent) == NebulaType.DenseNebula || game.Galaxy.GetObjectAt(adjacent) is not null)
                continue;

            if (TentativeStep(adjacent, destination, game) is not null)
                return adjacent;
        }

        return null;
    }

    /// <summary>A single Sgn-toward-destination step, or null if the target cell is dense nebula or already occupied (SBASE.PAS's own nested TentativeMove).</summary>
    private static Coordinate? TentativeStep(Coordinate origin, Coordinate destination, Game game)
    {
        var dx = Math.Sign(destination.X - origin.X);
        var dy = Math.Sign(destination.Y - origin.Y);
        var candidate = origin with { X = origin.X + dx, Y = origin.Y + dy };

        if (game.Galaxy.GetNebula(candidate) == NebulaType.DenseNebula)
            return null;

        return game.Galaxy.GetObjectAt(candidate) is null ? candidate : null;
    }

    /// <summary>Index into <see cref="_compassSteps"/> matching the Sgn-based heading from one coordinate to another (SBASE.PAS's XY2Dir, minus its Directions-enum indirection).</summary>
    private static int CompassIndex(Coordinate origin, Coordinate to)
    {
        var dx = Math.Sign(to.X - origin.X);
        var dy = Math.Sign(to.Y - origin.Y);
        return Array.FindIndex(_compassSteps, s => s.X == dx && s.Y == dy);
    }
}
