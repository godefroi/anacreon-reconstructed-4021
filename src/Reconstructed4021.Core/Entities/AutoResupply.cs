using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Automatic cargo resupply (GitHub issue #85): auto-dispatches an idle cargo fleet from a source
/// world to a same-empire destination that came up short this tick, reusing the exact order
/// sequence the manual Resupply mission already builds (<see cref="FleetOrderTemplates.Resupply"/>).
/// New functionality with no Pascal source -- see <see cref="ResupplySettings"/>'s own doc comment.
/// Structured like <see cref="ProductionRedirection"/>: <see cref="Apply"/> is the one stateful entry
/// point, called once per tick from <see cref="AnnualTickHandler.RunAnnualTick"/>, after every
/// planet/starbase has finished its own economy pipeline for the tick -- a source's own candidate
/// destinations need this tick's final <see cref="Planet.ShortfallsLastTick"/>, not whatever was left
/// over from before this tick ran.
///
/// Deliberately greedy, not a solver: one fleet per shortfall, cheapest-movement-rate-first, no
/// global optimization across the whole shortfall list (see the issue's own "deliberately deferred"
/// section). Reactive only, like every shortfall signal it reads -- never a forecast of an
/// impending shortage.
/// </summary>
public static class AutoResupply
{
    private readonly record struct Shortfall(Planet Destination, CargoType Cargo);

    public static void Apply(Game game)
    {
        foreach (var source in game.Galaxy.Planets) {
            if (!source.Resupply.Enabled) {
                continue;
            }

            var eligibleCargo = ResupplySettings.EligibleCargo(source.Type);
            if (eligibleCargo.Count == 0) {
                continue;
            }

            // Reserves against this source's own stockpile for the rest of this pass: the pickup
            // Transfer doesn't actually execute until FleetMovementHandler.ResolveOrders runs at the
            // next turn boundary, so a second dispatch from the same source in the same tick would
            // otherwise plan against the same pre-pickup Cargo twice.
            var reserved = new Dictionary<CargoType, int>();

            foreach (var shortfall in BuildShortfallList(source, eligibleCargo, game)) {
                DispatchOne(source, shortfall, reserved, game);
            }
        }
    }

    /// <summary>
    /// One source's own ranked shortfall list for this tick: starvation (any eligible destination
    /// whose <see cref="Planet.ShortfallsLastTick"/> contains <see cref="CargoType.Supplies"/>, ranked
    /// by population) first, then tier-1 destinations (<see cref="ResupplySettings.Destinations"/>) in
    /// list order, then every other eligible same-owner destination (tier 2), ranked by population.
    /// </summary>
    private static List<Shortfall> BuildShortfallList(Planet source, IReadOnlyList<CargoType> eligibleCargo, Game game)
    {
        var candidates = game.Galaxy.Planets
            .Where(p => !ReferenceEquals(p, source) && ReferenceEquals(p.Owner, source.Owner))
            .ToList();

        var tier1Set = source.Resupply.Destinations.ToHashSet();

        var starving = eligibleCargo.Contains(CargoType.Supplies)
            ? candidates
                .Where(p => p.ShortfallsLastTick.Contains(CargoType.Supplies))
                .OrderByDescending(p => p.Population)
                .Select(p => new Shortfall(p, CargoType.Supplies))
            : [];

        var tier1 = source.Resupply.Destinations
            .Select(loc => candidates.FirstOrDefault(p => p.Location == loc))
            .Where(p => p is not null)
            .SelectMany(p => NonSupplyShortfalls(p!, eligibleCargo));

        var tier2 = candidates
            .Where(p => !tier1Set.Contains(p.Location))
            .OrderByDescending(p => p.Population)
            .SelectMany(p => NonSupplyShortfalls(p, eligibleCargo));

        return [.. starving, .. tier1, .. tier2];
    }

    private static IEnumerable<Shortfall> NonSupplyShortfalls(Planet destination, IReadOnlyList<CargoType> eligibleCargo) =>
        eligibleCargo
            .Where(c => c != CargoType.Supplies && destination.ShortfallsLastTick.Contains(c))
            .Select(c => new Shortfall(destination, c));

    private static void DispatchOne(Planet source, Shortfall shortfall, Dictionary<CargoType, int> reserved, Game game)
    {
        if (HasPendingDelivery(game, source.Owner, shortfall.Destination.Location, shortfall.Cargo)) {
            return;
        }

        var available = Math.Max(0, source.Cargo[shortfall.Cargo] - reserved.GetValueOrDefault(shortfall.Cargo));
        if (available <= 0) {
            return;
        }

        if (PickFleet(source, shortfall, game) is not { } fleet) {
            return;
        }

        var amount = FleetLogistics.MaxPickupAmount(shortfall.Cargo, fleet.Ships, fleet.Cargo, available);
        if (source.Resupply.MaxAmount > 0) {
            amount = Math.Min(amount, source.Resupply.MaxAmount);
        }

        if (amount <= 0) {
            return;
        }

        var orders = FleetOrderTemplates.Resupply(source, shortfall.Destination, shortfall.Cargo, amount);
        FleetMovementHandler.CommitOrders(fleet, orders, startAt: 1, game);

        reserved[shortfall.Cargo] = reserved.GetValueOrDefault(shortfall.Cargo) + amount;
    }

    /// <summary>
    /// The cheapest-movement-rate idle fleet at <paramref name="source"/> that can actually carry
    /// <paramref name="shortfall"/>'s cargo and make the round trip -- conserves fast fleets for
    /// shortfalls only they can reach, without a full assignment solve across the whole shortfall
    /// list. "Idle" is <see cref="FleetStatus.Ready"/> with an empty order queue (no existing helper
    /// combines these two checks). The fuel-range gate (<see cref="FleetLifecycle.EstimatedRange"/>
    /// against twice the one-way ETA) has no Pascal or issue precedent -- added because the order
    /// template only refuels at the very end of the round trip, so an ungated dispatch could strand a
    /// fleet with no fuel to get home.
    /// </summary>
    private static Fleet? PickFleet(Planet source, Shortfall shortfall, Game game)
    {
        Fleet? best = null;
        var bestRate = int.MaxValue;

        foreach (var fleet in game.Galaxy.Fleets) {
            if (!ReferenceEquals(fleet.Owner, source.Owner) || fleet.Location != source.Location) {
                continue;
            }

            if (fleet.Status != FleetStatus.Ready || fleet.NextOrder != 0) {
                continue;
            }

            if (FleetLogistics.FleetCargoSpaceFor(shortfall.Cargo, fleet.Ships, fleet.Cargo) <= 0) {
                continue;
            }

            var eta = FleetLifecycle.EstimatedDateOfArrival(fleet, shortfall.Destination.Location, game);
            if (FleetLifecycle.EstimatedRange(fleet) < eta * 2) {
                continue;
            }

            var rate = FleetLogistics.MovementRate(fleet.Type);
            if (rate < bestRate) {
                bestRate = rate;
                best = fleet;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether some same-owner fleet already has an order pair queued to deliver <paramref name="cargo"/>
    /// to <paramref name="destination"/> -- a <see cref="CommandType.Destination"/> order targeting it
    /// immediately followed by a <see cref="CommandType.Transfer"/> dropping that cargo off (negative
    /// amount, matching <see cref="FleetOrderTemplates.Resupply"/>'s own sign convention), anywhere
    /// from <see cref="Fleet.NextOrder"/> on. No precedent in the issue, but without this a multi-turn
    /// round trip means every idle fleet at a source gets sent at the same still-short destination
    /// again every tick until the first shipment actually lands. Reads the existing
    /// <see cref="Fleet.Orders"/> list -- no new state to keep in sync.
    /// </summary>
    public static bool HasPendingDelivery(Game game, Empire owner, Coordinate destination, CargoType cargo)
    {
        foreach (var fleet in game.Galaxy.Fleets) {
            if (!ReferenceEquals(fleet.Owner, owner) || fleet.NextOrder == 0) {
                continue;
            }

            for (var i = fleet.NextOrder - 1; i < fleet.Orders.Count - 1; i++) {
                var order = fleet.Orders[i];
                if (order.Type != CommandType.Destination) {
                    continue;
                }

                var orderDestination = order.DestinationObject is { } obj ? obj.Location : order.DestinationPosition;
                if (orderDestination is not { } od || od != destination) {
                    continue;
                }

                var next = fleet.Orders[i + 1];
                if (next.Type == CommandType.Transfer && next.TransferCargo == cargo && next.TransferAmount < 0) {
                    return true;
                }
            }
        }

        return false;
    }
}
