using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Fleet creation, composition changes, refueling, and destination-setting (FLEET.PAS/INTRFACE.PAS),
/// the fleet-lifecycle primitives <see cref="Npe.NpeToolkit"/>'s NPEINTR.PAS toolkit needs but that
/// FLEET.PAS/INTRFACE.PAS itself, not NPEINTR.PAS, actually owns (see <see cref="Npe.NpeToolkit"/>'s
/// own doc comment for that split). Kept separate from <see cref="FleetLogistics"/> (pure fuel/cargo
/// formulas, no state mutation) and <see cref="Turns.FleetMovementHandler"/> (the per-turn
/// advancement stepper) since these are one-shot mutators an NPE's Deploy*/Implement*MSN procedures
/// call mid-turn, not part of either of those.
/// </summary>
public static class FleetLifecycle
{
    /// <summary>
    /// DeployFleet (FLEET.PAS:391-435) — creates a new fleet at <paramref name="launchSource"/>'s
    /// location heading for <paramref name="destination"/>, funded by <paramref name="ships"/>/
    /// <paramref name="cargo"/> drawn off <paramref name="launchSource"/>. Real Pascal's GetNextFleet
    /// (a fixed <c>MaxNoOfFleets</c>-slot array scan returning <c>EmptyQuadrant</c> when full)
    /// collapses to a plain <c>List.Add</c> — this port's <see cref="Galaxy.Galaxy.Fleets"/> has no
    /// slot cap, so DeployFleet always succeeds and every real caller's
    /// <c>IF NOT SameID(FltID,EmptyQuadrant)</c> guard is always true, dropped there rather than here.
    ///
    /// <paramref name="launchSource"/> must be a <see cref="Fleet"/> or <see cref="IEconomicWorld"/> —
    /// every real call site (NpeToolkit's Deploy*Fleet procedures) sources it that way. The reversed
    /// argument order Pascal uses for <see cref="ChangeCompositionOfFleet"/> when the launch source is
    /// itself a fleet (so the fuel-proration math treats the right side as "the fleet") is preserved
    /// verbatim — see that method's own doc comment for which side can end up destroyed.
    /// </summary>
    public static Fleet DeployFleet(Empire emp, IShipCargoHolder launchSource, ShipCounts ships, CargoHold cargo, Coordinate destination, Game game)
    {
        var fleet = new Fleet { Location = ((ISectorObject)launchSource).Location, Owner = emp };
        emp.Fleets.MarkScouted(fleet);
        game.Galaxy.Fleets.Add(fleet);

        SetFleetDestination(fleet, destination);

        var remainingShips = Subtract(launchSource.Ships, ships);
        var remainingCargo = Subtract(launchSource.Cargo, cargo);

        if (launchSource is Fleet launchFleet) {
            ChangeCompositionOfFleet(launchFleet, fleet, remainingShips, remainingCargo, ships, cargo, game);
        } else {
            ChangeCompositionOfFleet(fleet, launchSource, ships, cargo, remainingShips, remainingCargo, game);
        }

        // Floor, not a top-up (FLEET.PAS:432-433): fires only when ChangeCompositionOfFleet left the
        // new fleet at exactly 0.0. An exact double comparison is correct here, not a float-equality
        // bug: fleet.Fuel starts at 0.0 (a fresh Fleet's default) and the only way it can still read
        // 0.0 here is if nothing was ever added to it — the not-a-fleet branch's FuelChange clamps to
        // TonsOnGround*FuelPerTon, which is exactly 0.0 (not some tiny epsilon) when the launch world
        // has no trillum at all.
        if (fleet.Fuel == 0) {
            fleet.Fuel = 10;
        }

        return fleet;
    }

    /// <summary>
    /// ChangeCompositionOfFleet (FLEET.PAS:282-389) — resizes <paramref name="fleet"/> and
    /// <paramref name="ground"/> to the given ship/cargo compositions, prorating fuel between them.
    /// <paramref name="fleet"/> being typed as a real <see cref="Fleet"/> (not <c>object</c>) is a
    /// genuine contract, not an incidental typing choice: every real Pascal call site (including
    /// <see cref="DeployFleet"/>'s reversed-argument case) passes a fleet in this slot.
    /// <paramref name="ground"/> is a <see cref="Fleet"/> or <see cref="IEconomicWorld"/>.
    ///
    /// <b>Either side can end up destroyed, never both:</b> if <paramref name="newFleetShips"/> is
    /// entirely empty, <paramref name="fleet"/> aborts its remaining ships/cargo onto
    /// <paramref name="ground"/> and is destroyed — the method returns having removed the fleet you
    /// passed in. Else, if <paramref name="ground"/> is itself a fleet and
    /// <paramref name="newGroundShips"/> is entirely empty, <paramref name="ground"/> aborts onto
    /// <paramref name="fleet"/> and is destroyed instead. Callers that need to keep using one side
    /// after this call must check which one (if either) is still in <see cref="Galaxy.Galaxy.Fleets"/>.
    ///
    /// <b>When <paramref name="ground"/> isn't a fleet, the trillum-for-fuel math reads
    /// <paramref name="ground"/>'s trillum <i>after</i> writing <paramref name="newGroundCargo"/> onto
    /// it</b> (FLEET.PAS:356-365: <c>PutCargo(GroundID,NewGCr)</c> runs before <c>GetTrillum(GroundID)</c>)
    /// — so "how much trillum is available to convert" is whatever <paramref name="newGroundCargo"/>
    /// says the ground ends up with, not whatever it held before this call. Ported verbatim: a caller
    /// that wants some of the ground's pre-existing trillum considered must carry it forward into
    /// <paramref name="newGroundCargo"/> itself.
    ///
    /// A caller may pass <paramref name="fleet"/>'s (or <paramref name="ground"/>'s) own live ships/cargo
    /// straight through as the "new composition" arguments — the internal copies degenerate to
    /// self-copies in that case, which is fine and intended, not a bug to fix.
    /// </summary>
    public static void ChangeCompositionOfFleet(
        Fleet fleet, IShipCargoHolder ground,
        ShipCounts newFleetShips, CargoHold newFleetCargo,
        ShipCounts newGroundShips, CargoHold newGroundCargo,
        Game game)
    {
        if (NoShips(newFleetShips)) {
            CombatOutcome.AbortFleet(fleet, ground, report: true);
            CombatOutcome.DestroyFleet(fleet, game);
            return;
        }

        if (ground is Fleet groundToAbort && NoShips(newGroundShips)) {
            CombatOutcome.AbortFleet(groundToAbort, fleet, report: true);
            CombatOutcome.DestroyFleet(groundToAbort, game);
            return;
        }

        var newFleetCapacity = FleetLogistics.FuelCapacity(newFleetShips);
        var fleetFuel = fleet.Fuel;

        if (ground is Fleet groundFleet) {
            var oldFleetCapacity = FleetLogistics.FuelCapacity(fleet.Ships);
            var groundFuel = groundFleet.Fuel;

            var fuelChange = fleetFuel * newFleetCapacity / oldFleetCapacity - fleetFuel;
            fuelChange = Math.Min(fuelChange, groundFuel);

            fleet.Fuel = fleetFuel + fuelChange;
            groundFleet.Fuel = groundFuel - fuelChange;

            Copy(fleet.Ships, newFleetShips);
            Copy(fleet.Cargo, newFleetCargo);
            Copy(groundFleet.Ships, newGroundShips);
            Copy(groundFleet.Cargo, newGroundCargo);
        } else {
            var world = (IEconomicWorld)ground;

            Copy(world.Ships, newGroundShips);
            Copy(world.Cargo, newGroundCargo);

            var fuelChange = newFleetCapacity - fleetFuel;
            var tonsNeeded = PascalRound(fuelChange / FleetLogistics.FuelPerTon);
            var tonsOnGround = world.Cargo.Trillum;

            if (tonsOnGround >= tonsNeeded) {
                world.Cargo.Trillum = ClampResource(tonsOnGround - tonsNeeded);
            } else {
                world.Cargo.Trillum = 0;
                fuelChange = tonsOnGround * FleetLogistics.FuelPerTon;
            }

            fleet.Fuel = fleetFuel + fuelChange;
            Copy(fleet.Ships, newFleetShips);
            Copy(fleet.Cargo, newFleetCargo);
        }
    }

    /// <summary>
    /// AbortFleet (FLEET.PAS:150-209), exposed publicly for Fleet menu &gt; Abort/Join
    /// (FLTCOMM.PAS: AbortFleetCommand's own tail, lines 597-609): dumps every ship/cargo in
    /// <paramref name="fleet"/> onto <paramref name="ground"/>, then removes <paramref name="fleet"/>.
    /// No distribution grid -- AbortFleetCommand never calls InputNewDistribution, unlike
    /// TransferFleetCommand -- and no capacity clamp, matching <see cref="Combat.CombatOutcome"/>'s
    /// own AbortFleet doc comment (Pascal has none here either). CombatOutcome's AbortFleet/DestroyFleet
    /// stay internal (every other caller -- DestroyEmpire, ChangeCompositionOfFleet above -- is
    /// Core-internal); this is the one real Tui entry point for the raw operation, confirmation
    /// prompts (ownership warning, MaxResources overflow warning) staying in Tui to ask the player.
    /// </summary>
    public static void AbortFleet(Fleet fleet, IShipCargoHolder ground, Game game)
    {
        CombatOutcome.AbortFleet(fleet, ground, report: true);
        CombatOutcome.DestroyFleet(fleet, game);
    }

    /// <summary>
    /// RefuelFleetCommand's own "how much could the player ask for" math (FLTCOMM.PAS:733-738), split
    /// from that procedure's own numeric-input loop (GetTrillumToUse), which stays in Tui matching
    /// this port's Core/Tui split. The lesser of what's needed to top <paramref name="target"/>'s
    /// tank off and however much trillum <paramref name="ground"/> actually has.
    /// </summary>
    public static int MaxTrillumToRefuel(IShipCargoHolder target, IShipCargoHolder ground)
    {
        var fuel = target is Fleet fleet ? fleet.Fuel : 0; // GetFleetFuel no-op precedent -- see RefuelFleet's own doc comment.
        var maxFuel = FleetLogistics.FuelCapacity(target.Ships);
        var tonsNeeded = (int)((maxFuel - fuel) / FleetLogistics.FuelPerTon) + 1;
        return Math.Min(tonsNeeded, ground.Cargo.Trillum);
    }

    /// <summary>
    /// RefuelFleet (FLEET.PAS:452-490): converts up to <paramref name="trillum"/> tons of
    /// <paramref name="ground"/>'s trillum into fuel for <paramref name="target"/>, capped at
    /// capacity, then re-derives Ready/InTransit status if the result clears consumption. Real
    /// Pascal's GetFleetFuel/SetFleetFuel silently no-op for anything that isn't a <see cref="Fleet"/>
    /// (PRIMINTR.PAS:853-880); <paramref name="target"/> can be a <see cref="Starbase"/> at this
    /// method's one real call site (<see cref="Npe.NpeToolkit.ImplementRefuelMSN"/>, whose TargetID
    /// resolves dynamically from the mission), in which case the whole fuel side of this method is a
    /// no-op, and the ground's trillum is still spent for nothing. Ported verbatim, not "fixed,"
    /// matching this port's existing GetFleetFuel/SetFleetFuel no-op precedent.
    /// </summary>
    public static void RefuelFleet(IShipCargoHolder target, IShipCargoHolder ground, int trillum)
    {
        var targetFuel = target is Fleet targetAsFleet ? targetAsFleet.Fuel : 0;
        var maxFuel = FleetLogistics.FuelCapacity(target.Ships);

        ground.Cargo.Trillum -= trillum;
        targetFuel = Math.Min(targetFuel + trillum * FleetLogistics.FuelPerTon, maxFuel);

        if (target is Fleet fleetTarget) {
            fleetTarget.Fuel = targetFuel;
        }

        if (targetFuel > FleetLogistics.FuelConsumption(target.Ships, target.Cargo) && target is IMovable movable) {
            var location = ((ISectorObject)target).Location;
            // Real Pascal's GetFleetDestination always returns a real coordinate (defaulting to the
            // fleet's own location once arrived) -- this port instead nulls Destination out on arrival
            // (FleetMovementHandler's own doc comment), so a null Destination here means the same thing
            // SameXY(FPos,FDes) would: already there, no real ternary needed to reach FReady.
            movable.Status = movable.Destination is null || movable.Destination == location
                ? FleetStatus.Ready
                : FleetStatus.InTransit;
        }
    }

    /// <summary>SetFleetDestination (FLEET.PAS:90-106) — sets a mover's destination and derives its Ready/InTransit status from whether it's already there.</summary>
    public static void SetFleetDestination<T>(T mover, Coordinate newDestination) where T : IMovable, ISectorObject
    {
        mover.Destination = newDestination;
        mover.Status = mover.Location == newDestination ? FleetStatus.Ready : FleetStatus.InTransit;
    }

    /// <summary>
    /// EstimatedDateOfArrival (INTRFACE.PAS:863-893) — years until arrival: 1 if stargate/warp-linking
    /// through, else Chebyshev distance (minus 4, floored at 1, if departing a fortress) divided by
    /// the fleet type's movement rate, rounded up. Ported for both arms (<see cref="Fleet"/> and
    /// <see cref="Starbase"/>) even though every real 6c-2 call site only ever passes a freshly
    /// <see cref="DeployFleet"/>-created <see cref="Fleet"/> — a half-ported two-armed function would
    /// be worse than a fully-ported one with an untested arm.
    /// </summary>
    public static int EstimatedDateOfArrival(object id, Game game)
    {
        switch (id) {
            case Fleet fleet: {
                var destination = fleet.Destination ?? throw new InvalidOperationException("EstimatedDateOfArrival: fleet has no destination.");
                if (FleetMovementHandler.IsPassingThroughGate(fleet, fleet.Location, destination, game)) {
                    return 1;
                }

                var dist = fleet.Location.DistanceTo(destination);
                if (FleetMovementHandler.IsAtFortress(fleet.Location, game)) {
                    dist = Math.Max(dist - 4, 1);
                }

                var quadsPerYear = FleetLogistics.MovementRate(fleet.Type);
                var eda = dist / quadsPerYear;
                if (dist % quadsPerYear > 0) {
                    eda++;
                }
                return eda;
            }
            case Starbase starbase: {
                var destination = starbase.Destination ?? throw new InvalidOperationException("EstimatedDateOfArrival: starbase has no destination.");
                return starbase.Location.DistanceTo(destination);
            }
            default:
                throw new ArgumentException($"EstimatedDateOfArrival: expected a Fleet or Starbase, got {id.GetType()}.", nameof(id));
        }
    }

    /// <summary>
    /// EstimatedRange (INTRFACE.PAS:895-914) — years of range left: fuel/consumption for a fleet,
    /// trillum/100 for a starbase (starbases have no fuel concept — see <see cref="RefuelFleet"/>'s
    /// own doc comment for the same GetFleetFuel no-op).
    /// </summary>
    public static int EstimatedRange(object id) => id switch {
        Fleet fleet => (int)(fleet.Fuel / FleetLogistics.FuelConsumption(fleet.Ships, fleet.Cargo)),
        Starbase starbase => starbase.Cargo.Trillum / 100,
        _ => throw new ArgumentException($"EstimatedRange: expected a Fleet or Starbase, got {id.GetType()}.", nameof(id)),
    };

    /// <summary>NoShips (MISC.PAS): whether every ship-type count is zero. Internal: <see cref="Npe.NpeToolkit.AttackEnemyFleets"/> needs the same check DeployFleet/ChangeCompositionOfFleet already make.</summary>
    internal static bool NoShips(ShipCounts ships)
    {
        foreach (var t in Enum.GetValues<ShipType>()) {
            if (ships[t] != 0) {
                return false;
            }
        }
        return true;
    }

    private static void Copy(ShipCounts dest, ShipCounts source)
    {
        foreach (var t in Enum.GetValues<ShipType>()) {
            dest[t] = source[t];
        }
    }

    private static void Copy(CargoHold dest, CargoHold source)
    {
        foreach (var t in Enum.GetValues<CargoType>()) {
            dest[t] = source[t];
        }
    }

    /// <summary>SubThings (MISC.PAS:287-303) — element-wise subtraction, clamped to [0,MaxResources] like every other thing-quantity mutation.</summary>
    private static ShipCounts Subtract(ShipCounts a, ShipCounts b)
    {
        var result = new ShipCounts();
        foreach (var t in Enum.GetValues<ShipType>()) {
            result[t] = ClampResource(a[t] - b[t]);
        }
        return result;
    }

    private static CargoHold Subtract(CargoHold a, CargoHold b)
    {
        var result = new CargoHold();
        foreach (var t in Enum.GetValues<CargoType>()) {
            result[t] = ClampResource(a[t] - b[t]);
        }
        return result;
    }
}
