using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Production redirection (GitHub issue #8): auto-dispatches whatever a planet produced this tick
/// toward its configured <see cref="RedirectionSettings.Destination"/>. New functionality with no
/// Pascal source -- see <see cref="RedirectionSettings"/>'s own doc comment. <see cref="ComputeDispatch"/>
/// is a pure function, a sibling to <see cref="FleetLogistics"/>'s own fuel/cargo formulas;
/// <see cref="Apply"/> is the one stateful entry point, called from <see cref="Turns.AnnualTickHandler.UpdateWorld"/>.
/// </summary>
public static class ProductionRedirection
{
    /// <summary>
    /// Picks how much of this tick's newly-produced ships/cargo to send, given <paramref name="settings"/>.
    /// <see cref="RedirectionMode.AsNeeded"/> on Transport/Jumptransport sends only enough of that tick's
    /// new transports to carry whatever Legion/NinjaLegion cargo was selected -- the same cargo-space math
    /// <see cref="Npe.NpeToolkit.GetAttackForces"/> already uses for the same problem
    /// (<see cref="FleetLogistics.CargoSpacePerUnit"/>/<see cref="FleetLogistics.JumptransportCargoAdjustment"/>),
    /// not a new formula. Jumptransports are filled before Transports, matching that same precedent.
    /// </summary>
    public static (ShipCounts Ships, CargoHold Cargo) ComputeDispatch(RedirectionSettings settings, ShipCounts producedShips, CargoHold producedCargo)
    {
        var cargo = new CargoHold {
            Legions = settings.IncludeLegions ? producedCargo.Legions : 0,
            NinjaLegions = settings.IncludeNinjaLegions ? producedCargo.NinjaLegions : 0,
        };

        var ships = new ShipCounts();
        foreach (var type in Enum.GetValues<ShipType>()) {
            if (type is ShipType.Transport or ShipType.Jumptransport) {
                continue; // resolved below, once cargo space needed is known
            }

            if (settings.Ships.GetValueOrDefault(type, RedirectionMode.No) == RedirectionMode.Yes) {
                ships[type] = producedShips[type];
            }
        }

        var cargoSpaceNeeded = cargo.Legions / (double)FleetLogistics.CargoSpacePerUnit[CargoType.Legion]
                              + cargo.NinjaLegions / (double)FleetLogistics.CargoSpacePerUnit[CargoType.NinjaLegion];

        ships.Jumptransports = ResolveTransportCount(
            settings.Ships.GetValueOrDefault(ShipType.Jumptransport, RedirectionMode.No),
            producedShips.Jumptransports,
            PascalRound(cargoSpaceNeeded / FleetLogistics.JumptransportCargoAdjustment));
        cargoSpaceNeeded -= ships.Jumptransports * FleetLogistics.JumptransportCargoAdjustment;

        ships.Transports = ResolveTransportCount(
            settings.Ships.GetValueOrDefault(ShipType.Transport, RedirectionMode.No),
            producedShips.Transports,
            PascalRound(cargoSpaceNeeded));

        return (ships, cargo);
    }

    private static int ResolveTransportCount(RedirectionMode mode, int produced, int neededForCargo) => mode switch {
        RedirectionMode.No => 0,
        RedirectionMode.Yes => produced,
        RedirectionMode.AsNeeded => Math.Min(produced, Math.Max(0, neededForCargo)),
        _ => 0,
    };

    /// <summary>
    /// Dispatches whatever <paramref name="planet"/> produced this tick, per its own
    /// <see cref="Planet.Redirection"/> settings -- <paramref name="shipsBefore"/>/<paramref name="legionsBefore"/>/
    /// <paramref name="ninjaLegionsBefore"/> are the planet's own counts snapshotted immediately before
    /// this tick's production ran; the delta against <paramref name="planet"/>'s current counts is always
    /// &gt;=0, since production only ever grows them within one tick. No-ops if no destination is set, or
    /// nothing was selected to send.
    /// </summary>
    public static void Apply(Planet planet, Game game, ShipCounts shipsBefore, int legionsBefore, int ninjaLegionsBefore)
    {
        var settings = planet.Redirection;
        if (settings.Destination is not { } destination) {
            return;
        }

        var producedShips = new ShipCounts();
        foreach (var type in Enum.GetValues<ShipType>()) {
            producedShips[type] = planet.Ships[type] - shipsBefore[type];
        }

        var producedCargo = new CargoHold {
            Legions = planet.Cargo.Legions - legionsBefore,
            NinjaLegions = planet.Cargo.NinjaLegions - ninjaLegionsBefore,
        };

        var (ships, cargo) = ComputeDispatch(settings, producedShips, producedCargo);
        if (FleetLifecycle.NoShips(ships) && cargo.Legions == 0 && cargo.NinjaLegions == 0) {
            return;
        }

        var fleet = FleetLifecycle.DeployFleet(planet.Owner, planet, ships, cargo, destination, game);
        fleet.Names[planet.Owner] = $"Redirect-{settings.NextDispatchNumber}";
        settings.NextDispatchNumber++;

        if (settings.JoinOnArrival) {
            fleet.Orders.Add(new FleetOrder(CommandType.Join, PreserveOverflow: settings.PreserveOverflowOnJoin));
            fleet.NextOrder = 1;
        }
    }
}
