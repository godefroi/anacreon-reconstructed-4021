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
/// </summary>
public sealed class FleetMovementHandler(Random random) : IFleetMovementHandler
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
        if (fleet.Destination is null
            || fleet.Status == FleetStatus.Lost
            || fleet.Status == FleetStatus.Inactive) {
            return false;
        }

        return fleet.Type == FleetType.JumpFleet
            && game.Galaxy.GetObjectAt(fleet.Location) is not Stargate;
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
            || game.Galaxy.GetObjectAt(fleet.Location) is Stargate;
    }

    /// <summary>
    /// UpdateFleet (FLEET.PAS:646-859). Gate/fortress pass-through is checked before fuel consumption
    /// (matching source: a fortress hop's own <c>MoveFleet</c> happens before <c>UseUpFuel</c>), and a
    /// fortress hop that lands short of teleport range still runs this turn's ordinary
    /// <see cref="StepFleet"/> allotment afterward from its new position — both apply in the same turn,
    /// not one or the other (verified directly: <c>Teleport</c> stays <c>False</c> on that path, so
    /// control falls through to the normal step loop below it).
    /// </summary>
    private void AdvanceFleet(Fleet fleet, Game game)
    {
        if (fleet.Destination is null)
            return;

        if (fleet.Status is FleetStatus.Lost or FleetStatus.Inactive)
            return;

        var destination = fleet.Destination.Value;
        if (fleet.Location == destination) {
            fleet.Destination = null;
            fleet.Status = FleetStatus.Ready;
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
            }

            return;
        }

        var rate = FleetLogistics.MovementRate(fleet.Type);
        if (!StepFleet(fleet, destination, rate, game, out var nextLocation))
            return; // fleet destroyed by a minefield — caller's snapshot list still holds the reference, but nothing left to update

        fleet.Location = nextLocation;
        fleet.Status = fleet.Location == destination ? FleetStatus.Ready : FleetStatus.InTransit;

        if (fleet.Location == destination)
            fleet.Destination = null;
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
