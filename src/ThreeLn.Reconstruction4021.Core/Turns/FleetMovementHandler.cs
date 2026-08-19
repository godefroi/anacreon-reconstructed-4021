using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Advances fleets and mobile starbases according to the sequential turn-order rules in the
/// original game: current-empire jump fleets move first, then the next empire's warp fleets,
/// each consuming fuel and stepping toward their destination until arrival.
/// </summary>
public sealed class FleetMovementHandler : IFleetMovementHandler
{
    private static readonly FrozenDictionary<FleetType, int> _movementRateByType = new Dictionary<FleetType, int>() {
        [FleetType.Standard] = 1,
        [FleetType.JumpFleet] = 10,
        [FleetType.HunterKillerFleet] = 10,
        [FleetType.Penetrator] = 2,
        [FleetType.AdvancedWarpFleet] = 2,
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<ShipType, int> _fuelBurnPerShip = new Dictionary<ShipType, int>() {
        [ShipType.Fighter] = 4,
        [ShipType.HunterKiller] = 8,
        [ShipType.Jumpship] = 12,
        [ShipType.Jumptransport] = 5,
        [ShipType.Penetrator] = 10,
        [ShipType.Starship] = 14,
        [ShipType.Transport] = 7,
    }.ToFrozenDictionary();

    public void AdvanceFleets(Game game, Empire actingEmpire, Empire nextEmpire)
    {
        foreach (var fleet in game.Galaxy.Fleets.ToList()) {
            if (fleet.Owner == actingEmpire && ShouldAdvanceActingEmpireFleet(fleet, game)) {
                AdvanceFleet(fleet);
                continue;
            }

            if (fleet.Owner == nextEmpire && ShouldAdvanceNextEmpireFleet(fleet, game)) {
                AdvanceFleet(fleet);
            }
        }
    }

    public void AdvanceStarbases(Game game, Empire empire)
    {
        foreach (var starbase in game.Galaxy.Starbases.ToList()) {
            if (starbase.Owner != empire || starbase.Destination is null || starbase.Status == FleetStatus.Lost) {
                continue;
            }

            if (starbase.Location == starbase.Destination.Value) {
                starbase.Destination = null;
                starbase.Status = FleetStatus.Ready;
                continue;
            }

            if (starbase.YearsUntilNextMove > 0) {
                starbase.YearsUntilNextMove--;
                continue;
            }

            MoveTowardDestination(starbase.Location, starbase.Destination.Value, 1, out var nextLocation);
            starbase.Location = nextLocation;

            if (starbase.Location == starbase.Destination.Value) {
                starbase.Destination = null;
                starbase.Status = FleetStatus.Ready;
            } else {
                starbase.Status = FleetStatus.InTransit;
            }

            starbase.YearsUntilNextMove = 1;
        }
    }

    private static bool ShouldAdvanceActingEmpireFleet(Fleet fleet, Game game)
    {
        if (fleet.Destination is null
            || fleet.Status == FleetStatus.Lost
            || fleet.Status == FleetStatus.Inactive) {
            return false;
        }

        return fleet.Type == FleetType.JumpFleet
            && !IsAtStargate(fleet.Location, fleet.Owner, game);
    }

    private static bool ShouldAdvanceNextEmpireFleet(Fleet fleet, Game game)
    {
        if (fleet.Destination is null
            || fleet.Status == FleetStatus.Lost
            || fleet.Status == FleetStatus.Inactive) {
            return false;
        }

        return fleet.Type is FleetType.Standard
            or FleetType.Penetrator
            or FleetType.AdvancedWarpFleet
            or FleetType.HunterKillerFleet
            || IsAtStargate(fleet.Location, fleet.Owner, game);
    }

    private static bool IsAtStargate(Coordinate location, Empire owner, Game game) =>
        game.Galaxy.Stargates.Any(gate => gate.Owner == owner && gate.Location == location);

    private static void AdvanceFleet(Fleet fleet)
    {
        if (fleet.Destination is null)
            return;

        if (fleet.Status == FleetStatus.Lost)
            return;

        var destination = fleet.Destination.Value;
        if (fleet.Location == destination) {
            fleet.Destination = null;
            fleet.Status = FleetStatus.Ready;
            return;
        }

        if (!ConsumeFuel(fleet))
            return;

        var rate = GetMovementRate(fleet.Type);
        MoveTowardDestination(fleet.Location, destination, rate, out var nextLocation);
        fleet.Location = nextLocation;
        fleet.Status = fleet.Location == destination ? FleetStatus.Ready : FleetStatus.InTransit;

        if (fleet.Location == destination)
            fleet.Destination = null;
    }

    private static bool ConsumeFuel(Fleet fleet)
    {
        var requiredFuel = GetFleetFuelCost(fleet);
        if (requiredFuel <= 0)
            return true;

        if (fleet.Fuel >= requiredFuel) {
            fleet.Fuel -= requiredFuel;
            return true;
        }

        var deficit = requiredFuel - fleet.Fuel;
        while (deficit > 0 && fleet.Cargo.Trillum > 0) {
            var converted = Math.Min(fleet.Cargo.Trillum, deficit);
            fleet.Cargo.Trillum -= converted;
            fleet.Fuel += converted;
            deficit -= converted;
        }

        if (deficit > 0) {
            fleet.Status = FleetStatus.Inactive;
            fleet.Destination = null;
            return false;
        }

        fleet.Fuel -= requiredFuel;
        return true;
    }

    private static int GetFleetFuelCost(Fleet fleet)
    {
        var shipCost = GetShipFuelCost(fleet.Ships);
        var cargoCost = GetCargoFuelCost(fleet.Cargo);
        return 1 + shipCost + cargoCost;
    }

    private static int GetShipFuelCost(ShipCounts ships)
    {
        return ships.Fighters * _fuelBurnPerShip[ShipType.Fighter]
            + ships.HunterKillers * _fuelBurnPerShip[ShipType.HunterKiller]
            + ships.Jumpships * _fuelBurnPerShip[ShipType.Jumpship]
            + ships.Jumptransports * _fuelBurnPerShip[ShipType.Jumptransport]
            + ships.Penetrators * _fuelBurnPerShip[ShipType.Penetrator]
            + ships.Starships * _fuelBurnPerShip[ShipType.Starship]
            + ships.Transports * _fuelBurnPerShip[ShipType.Transport];
    }

    private static int GetCargoFuelCost(CargoHold cargo)
    {
        return cargo.Legions
            + cargo.NinjaLegions
            + cargo.Ambrosia
            + cargo.Chemicals
            + cargo.Metals
            + cargo.Supplies
            + cargo.Trillum;
    }

    private static int GetMovementRate(FleetType fleetType) =>
        _movementRateByType.TryGetValue(fleetType, out var orbitRate)
            ? orbitRate
            : 1;

    private static void MoveTowardDestination(
        Coordinate start,
        Coordinate destination,
        int steps,
        out Coordinate nextLocation)
    {
        var current = start;
        for (var step = 0; step < steps; step++) {
            if (current == destination)
                break;

            var dx = Math.Sign(destination.X - current.X);
            var dy = Math.Sign(destination.Y - current.Y);

            if (dx != 0)
                current = current with { X = current.X + dx };

            if (dy != 0)
                current = current with { Y = current.Y + dy };
        }

        nextLocation = current;
    }
}
