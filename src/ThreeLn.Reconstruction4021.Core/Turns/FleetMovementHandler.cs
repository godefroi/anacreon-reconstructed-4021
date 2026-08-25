using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;
using static ThreeLn.Reconstruction4021.Core.PascalMath;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>
/// Advances fleets and mobile starbases according to the sequential turn-order rules in the
/// original game: current-empire jump fleets move first, then the next empire's warp fleets,
/// each consuming fuel and stepping toward their destination until arrival.
///
/// Phase 5 commit 5h added minefield damage and disrupter blocking (FLEET.PAS:705-746, 619-644) as a
/// per-step check inside JumpFleet/HunterKillerFleet movement — the only two fleet types Pascal's own
/// <c>FltTyp IN [JumpFleet,HKFleet]</c> gate checks. Other fleet types keep the original single bulk
/// step, unaffected. Stargate teleportation and fortress pass-through jumps are still not ported (a
/// pre-existing movement-fidelity gap, tracked in docs/ROADMAP.md's Phase 6 commit 6a, not this one) —
/// same for dense-nebula movement blocking (GetNewPos's own Pos:=Limbo branch), which this commit found
/// but didn't fix, since it's a terrain effect rather than a combat mechanic; also filed under 6a.
/// </summary>
public sealed class FleetMovementHandler(Random random) : IFleetMovementHandler
{
    private static readonly ShipType[] _mineableShipTypes = [ShipType.HunterKiller, ShipType.Jumpship, ShipType.Jumptransport];

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
                AdvanceFleet(fleet, game);
                continue;
            }

            if (fleet.Owner == nextEmpire && ShouldAdvanceNextEmpireFleet(fleet, game)) {
                AdvanceFleet(fleet, game);
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

    private void AdvanceFleet(Fleet fleet, Game game)
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

        Coordinate nextLocation;
        if (fleet.Type is FleetType.JumpFleet or FleetType.HunterKillerFleet) {
            if (!StepThroughMinesAndDisrupters(fleet, destination, rate, game, out nextLocation))
                return; // fleet destroyed by a minefield — caller's snapshot list still holds the reference, but nothing left to update
        } else {
            MoveTowardDestination(fleet.Location, destination, rate, out nextLocation);
        }

        fleet.Location = nextLocation;
        fleet.Status = fleet.Location == destination ? FleetStatus.Ready : FleetStatus.InTransit;

        if (fleet.Location == destination)
            fleet.Destination = null;
    }

    /// <summary>
    /// The [JumpFleet,HKFleet] branch of UpdateFleet's move loop (FLEET.PAS:801-838) — steps one cell
    /// at a time, checking for a minefield or disrupter at each intermediate cell, exactly as real
    /// Pascal's own per-step <c>GOTO ExitMoveLoop</c> does; other fleet types never run this check, so
    /// they keep the original single bulk-step move. A mine or disrupter hit stops movement for the
    /// rest of this turn's allowance, same as Pascal's GOTO — but the fleet still lands on the cell
    /// where it was hit (verified directly against source: Pascal's <c>MoveFleet(FltID,NewPos)</c>
    /// after <c>ExitMoveLoop</c> uses whatever NewPos the loop last computed, mine-survived or
    /// disrupter-blocked alike; only outright destruction skips it). Returns false if the fleet was
    /// destroyed by the minefield — the caller must not touch the fleet again.
    /// </summary>
    private bool StepThroughMinesAndDisrupters(Fleet fleet, Coordinate destination, int rate, Game game, out Coordinate nextLocation)
    {
        var current = fleet.Location;

        for (var step = 0; step < rate; step++) {
            if (current == destination)
                break;

            var dx = Math.Sign(destination.X - current.X);
            var dy = Math.Sign(destination.Y - current.Y);
            var candidate = current with { X = current.X + dx, Y = current.Y + dy };

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
    /// post-damage cargo rebalance isn't ported on the survives branch — same "no fleet cargo-capacity
    /// system exists yet" gap as RestoreCombatant/LAMAttack's own doc comments; this method only ever
    /// removes ships, never cargo, so nothing here could exceed a capacity anyway. FleetNameDestruction
    /// is dropped too (AbortFleet's established gap).
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
                // Pascal's raw ResourceTypes ordinal for fgt..trn is ShipType's own C# ordinal + 5
                // (see CombatStandalone.LAMAttack's identical comment).
                owner.AddNews(NewsType.DestructionDetail, fleet, p1: destroyed[t], p2: (int)t + 5);
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
