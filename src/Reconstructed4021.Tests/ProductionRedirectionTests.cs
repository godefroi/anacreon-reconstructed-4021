using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="ProductionRedirection"/> -- GitHub issue #8's "production redirection", confirmed to have
/// no Pascal source (see <see cref="RedirectionSettings"/>'s own doc comment), so these are hardcoded
/// against the feature's own spec rather than a golden file.
/// </summary>
public class ProductionRedirectionTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    private static (Game Game, Galaxy Galaxy) NewGame()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        return (game, galaxy);
    }

    [Test]
    public async Task ComputeDispatch_AllNo_SendsNothing()
    {
        var settings = new RedirectionSettings();
        var producedShips = new ShipCounts { Fighters = 10, Transports = 10 };
        var producedCargo = new CargoHold { Legions = 25 };

        var (ships, cargo) = ProductionRedirection.ComputeDispatch(settings, producedShips, producedCargo);

        await Assert.That(Enum.GetValues<ShipType>().Sum(t => ships[t])).IsEqualTo(0);
        await Assert.That(cargo.Legions).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeDispatch_YesOnOneShipType_SendsOnlyThatTypesFullDelta()
    {
        var settings = new RedirectionSettings { Ships = { [ShipType.Fighter] = RedirectionMode.Yes } };
        var producedShips = new ShipCounts { Fighters = 7, Starships = 3 };

        var (ships, _) = ProductionRedirection.ComputeDispatch(settings, producedShips, new CargoHold());

        await Assert.That(ships.Fighters).IsEqualTo(7);
        await Assert.That(ships.Starships).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeDispatch_TransportAsNeeded_SendsExactlyEnoughToCarryIncludedLegions()
    {
        // Legions=25 -> cargoSpaceNeeded = 25/5 = 5.0 transport-equivalents (FleetLogistics.CargoSpacePerUnit[Legion]=5).
        var settings = new RedirectionSettings {
            IncludeLegions = true,
            Ships = { [ShipType.Transport] = RedirectionMode.AsNeeded },
        };
        var producedCargo = new CargoHold { Legions = 25 };
        var producedShips = new ShipCounts { Transports = 10 };

        var (ships, cargo) = ProductionRedirection.ComputeDispatch(settings, producedShips, producedCargo);

        await Assert.That(cargo.Legions).IsEqualTo(25);
        await Assert.That(ships.Transports).IsEqualTo(5);
    }

    [Test]
    public async Task ComputeDispatch_TransportAsNeeded_NothingToCarry_SendsZero()
    {
        var settings = new RedirectionSettings { Ships = { [ShipType.Transport] = RedirectionMode.AsNeeded } };
        var producedShips = new ShipCounts { Transports = 10 };

        var (ships, _) = ProductionRedirection.ComputeDispatch(settings, producedShips, new CargoHold());

        await Assert.That(ships.Transports).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeDispatch_JumptransportAsNeeded_FilledBeforeTransport()
    {
        // Legions=25 -> 5.0 space needed. Plenty of jtn available -> jtn alone covers it, trn gets nothing.
        var settings = new RedirectionSettings {
            IncludeLegions = true,
            Ships = {
                [ShipType.Jumptransport] = RedirectionMode.AsNeeded,
                [ShipType.Transport] = RedirectionMode.AsNeeded,
            },
        };
        var producedCargo = new CargoHold { Legions = 25 };
        var producedShips = new ShipCounts { Jumptransports = 100, Transports = 10 };

        var (ships, _) = ProductionRedirection.ComputeDispatch(settings, producedShips, producedCargo);

        // 5.0 / JumptransportCargoAdjustment(0.2) = 25 jumptransports needed to cover it alone.
        await Assert.That(ships.Jumptransports).IsEqualTo(25);
        await Assert.That(ships.Transports).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeDispatch_JumptransportAsNeeded_CappedByWhatWasProduced_TransportCoversTheRest()
    {
        // 5.0 space needed; only 3 jtn were produced (0.6 space) -> transport covers the remaining 4.4 -> rounds to 4.
        var settings = new RedirectionSettings {
            IncludeLegions = true,
            Ships = {
                [ShipType.Jumptransport] = RedirectionMode.AsNeeded,
                [ShipType.Transport] = RedirectionMode.AsNeeded,
            },
        };
        var producedCargo = new CargoHold { Legions = 25 };
        var producedShips = new ShipCounts { Jumptransports = 3, Transports = 10 };

        var (ships, _) = ProductionRedirection.ComputeDispatch(settings, producedShips, producedCargo);

        await Assert.That(ships.Jumptransports).IsEqualTo(3);
        await Assert.That(ships.Transports).IsEqualTo(4);
    }

    [Test]
    public async Task Apply_DestinationSet_DispatchesFleetAndNamesItSequentially()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var planet = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        planet.Redirection.Destination = new Coordinate(10, 10);
        planet.Redirection.Ships[ShipType.Fighter] = RedirectionMode.Yes;
        galaxy.Planets.Add(planet);

        var shipsBefore = new ShipCounts(); // nothing produced yet
        planet.Ships.Fighters = 5; // this tick's production

        ProductionRedirection.Apply(planet, game, shipsBefore, legionsBefore: 0, ninjaLegionsBefore: 0);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.Ships.Fighters).IsEqualTo(5);
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(10, 10));
        await Assert.That(fleet.Names[owner]).IsEqualTo("Redirect-1");
        await Assert.That(planet.Ships.Fighters).IsEqualTo(0);
        await Assert.That(planet.Redirection.NextDispatchNumber).IsEqualTo(2);
        await Assert.That(fleet.Orders).IsEmpty(); // JoinOnArrival not set -- no Join order compiled
        await Assert.That(fleet.NextOrder).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_JoinOnArrival_CompilesAJoinOrder()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var planet = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        planet.Redirection.Destination = new Coordinate(10, 10);
        planet.Redirection.Ships[ShipType.Fighter] = RedirectionMode.Yes;
        planet.Redirection.JoinOnArrival = true;
        planet.Redirection.PreserveOverflowOnJoin = true;
        galaxy.Planets.Add(planet);

        var shipsBefore = new ShipCounts();
        planet.Ships.Fighters = 5;

        ProductionRedirection.Apply(planet, game, shipsBefore, legionsBefore: 0, ninjaLegionsBefore: 0);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.Orders).Count().IsEqualTo(1);
        await Assert.That(fleet.Orders[0].Type).IsEqualTo(CommandType.Join);
        await Assert.That(fleet.Orders[0].PreserveOverflow).IsTrue();
        await Assert.That(fleet.NextOrder).IsEqualTo(1);
    }

    [Test]
    public async Task Apply_NoDestination_DoesNothing()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var planet = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        planet.Redirection.Ships[ShipType.Fighter] = RedirectionMode.Yes;
        galaxy.Planets.Add(planet);

        var shipsBefore = new ShipCounts();
        planet.Ships.Fighters = 5;

        ProductionRedirection.Apply(planet, game, shipsBefore, legionsBefore: 0, ninjaLegionsBefore: 0);

        await Assert.That(galaxy.Fleets).IsEmpty();
        await Assert.That(planet.Ships.Fighters).IsEqualTo(5);
    }

    [Test]
    public async Task Apply_NothingNewlyProduced_DoesNothing()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var planet = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        planet.Redirection.Destination = new Coordinate(10, 10);
        planet.Redirection.Ships[ShipType.Fighter] = RedirectionMode.Yes;
        planet.Ships.Fighters = 5; // pre-existing stockpile, not newly produced this tick
        galaxy.Planets.Add(planet);

        var shipsBefore = new ShipCounts { Fighters = 5 }; // same as after -- nothing new

        ProductionRedirection.Apply(planet, game, shipsBefore, legionsBefore: 0, ninjaLegionsBefore: 0);

        await Assert.That(galaxy.Fleets).IsEmpty();
        await Assert.That(planet.Ships.Fighters).IsEqualTo(5); // untouched stockpile
    }
}
