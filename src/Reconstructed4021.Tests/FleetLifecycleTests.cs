using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Entities.FleetLifecycle"/> — the fleet-lifecycle primitives NPEINTR.PAS's
/// Deploy*/Implement*MSN layer needs (FLEET.PAS/INTRFACE.PAS). Hardcoded, same rationale as
/// <c>NpeToolkitTests</c>: no golden-file domain exists for fleet-lifecycle mutation, though this
/// port's SaveFormat layer could make building one cheap.
/// </summary>
public class FleetLifecycleTests
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
    public async Task SetFleetDestination_AtDestination_SetsReady()
    {
        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = NewEmpire("Owner") };

        FleetLifecycle.SetFleetDestination(fleet, new Coordinate(5, 5));

        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(5, 5));
    }

    [Test]
    public async Task SetFleetDestination_NotAtDestination_SetsInTransit()
    {
        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = NewEmpire("Owner") };

        FleetLifecycle.SetFleetDestination(fleet, new Coordinate(10, 10));

        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    [Test]
    public async Task ChangeCompositionOfFleet_GroundIsFleet_ProratesFuelByCapacityRatio()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 1.0 };
        fleet.Ships.Fighters = 5;
        var ground = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 5.0 };
        galaxy.Fleets.Add(fleet);
        galaxy.Fleets.Add(ground);

        var oldCapacity = FleetLogistics.FuelCapacity(fleet.Ships);
        var newFleetShips = new ShipCounts { Fighters = 10 };
        var newCapacity = FleetLogistics.FuelCapacity(newFleetShips);
        var expectedFuelChange = 1.0 * newCapacity / oldCapacity - 1.0;

        FleetLifecycle.ChangeCompositionOfFleet(
            fleet, ground,
            newFleetShips, new CargoHold(),
            new ShipCounts { Fighters = 1 }, new CargoHold(),
            game);

        await Assert.That(fleet.Fuel).IsEqualTo(1.0 + expectedFuelChange).Within(0.000001);
        await Assert.That(ground.Fuel).IsEqualTo(5.0 - expectedFuelChange).Within(0.000001);
        await Assert.That(fleet.Ships.Fighters).IsEqualTo(10);
        await Assert.That(ground.Ships.Fighters).IsEqualTo(1);
    }

    [Test]
    public async Task ChangeCompositionOfFleet_GroundIsWorld_ConvertsTrillumToFuelWhenEnoughOnGround()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 0.0 };
        var world = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(world);

        var newFleetShips = new ShipCounts { Starships = 10 }; // big capacity delta so TonsNeeded>0
        var newCapacity = FleetLogistics.FuelCapacity(newFleetShips);

        // PutCargo(GroundID,NewGCr) happens before GetTrillum(GroundID) in real Pascal (FLEET.PAS:356-365)
        // — TonsOnGround reads the *new* composition being assigned to the ground, not whatever trillum
        // was there before this call. So the "5 tons available" this test is about comes from
        // newGroundCargo, not from pre-seeding world.Cargo.Trillum (which would just get overwritten).
        var newGroundCargo = new CargoHold { Trillum = 5 };

        FleetLifecycle.ChangeCompositionOfFleet(
            fleet, world,
            newFleetShips, new CargoHold(),
            new ShipCounts(), newGroundCargo,
            game);

        await Assert.That(fleet.Fuel).IsEqualTo(newCapacity).Within(0.000001);
        await Assert.That(world.Cargo.Trillum).IsEqualTo(4); // 5 - 1 ton needed
    }

    [Test]
    public async Task ChangeCompositionOfFleet_GroundIsWorld_NotEnoughTrillum_TakesWhatsThereAndZeroesGround()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 0.0 };
        var world = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        world.Cargo.Trillum = 0;
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(world);

        FleetLifecycle.ChangeCompositionOfFleet(
            fleet, world,
            new ShipCounts { Starships = 10 }, new CargoHold(),
            new ShipCounts(), new CargoHold(),
            game);

        await Assert.That(fleet.Fuel).IsEqualTo(0.0); // 0 tons available -> 0 fuel change
        await Assert.That(world.Cargo.Trillum).IsEqualTo(0);
    }

    [Test]
    public async Task ChangeCompositionOfFleet_EmptyNewFleetShips_AbortsAndDestroysFleet()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.Fighters = 3;
        var world = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(world);

        FleetLifecycle.ChangeCompositionOfFleet(
            fleet, world,
            new ShipCounts(), new CargoHold(), // NewFSh empty -> fleet dies
            new ShipCounts(), new CargoHold(),
            game);

        await Assert.That(galaxy.Fleets).DoesNotContain(fleet);
        await Assert.That(world.Ships.Fighters).IsEqualTo(3); // AbortFleet dumped the fleet's ships onto the world
    }

    [Test]
    public async Task ChangeCompositionOfFleet_EmptyNewGroundShipsAndGroundIsFleet_AbortsAndDestroysGround()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        var ground = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        ground.Ships.Fighters = 7;
        galaxy.Fleets.Add(fleet);
        galaxy.Fleets.Add(ground);

        FleetLifecycle.ChangeCompositionOfFleet(
            fleet, ground,
            new ShipCounts { Fighters = 1 }, new CargoHold(),
            new ShipCounts(), new CargoHold(), // NewGSh empty, ground is a Fleet -> ground dies
            game);

        await Assert.That(galaxy.Fleets).DoesNotContain(ground);
        await Assert.That(galaxy.Fleets).Contains(fleet);
        await Assert.That(fleet.Ships.Fighters).IsEqualTo(7); // ground's ships dumped onto fleet
    }

    [Test]
    public async Task DeployFleet_FromWorld_CreatesFleetAndDebitsSource()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var source = new Planet { Location = new Coordinate(1, 1), Owner = owner, Type = WorldType.Base };
        source.Ships.Starships = 20; // big enough capacity delta that TonsNeeded>0, so 0 trillum on
                                      // source triggers the insufficient-trillum branch (fuelChange=0
                                      // exactly), which is what actually exercises DeployFleet's floor
        galaxy.Planets.Add(source);

        var fleet = FleetLifecycle.DeployFleet(owner, source, new ShipCounts { Starships = 5 }, new CargoHold(), new Coordinate(10, 10), game);

        await Assert.That(galaxy.Fleets).Contains(fleet);
        await Assert.That(fleet.Owner).IsEqualTo(owner);
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(1, 1));
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(10, 10));
        await Assert.That(fleet.Ships.Starships).IsEqualTo(5);
        await Assert.That(source.Ships.Starships).IsEqualTo(15);
        await Assert.That(fleet.Fuel).IsEqualTo(10); // floor: nothing on source to convert to fuel
        await Assert.That(owner.Fleets.Scouted).Contains(fleet);
    }

    [Test]
    public async Task DeployFleet_FromFleet_ReversedArgumentOrder_ShrinksLaunchFleetCorrectly()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var launch = new Fleet { Location = new Coordinate(2, 2), Owner = owner, Fuel = 50.0 };
        launch.Ships.Fighters = 20;
        galaxy.Fleets.Add(launch);

        var newFleet = FleetLifecycle.DeployFleet(owner, launch, new ShipCounts { Fighters = 5 }, new CargoHold(), new Coordinate(9, 9), game);

        await Assert.That(galaxy.Fleets).Contains(launch); // launch fleet survives — it kept ships
        await Assert.That(launch.Ships.Fighters).IsEqualTo(15);
        await Assert.That(newFleet.Ships.Fighters).IsEqualTo(5);
    }

    [Test]
    public async Task DeployFleet_FromFleet_DonatingEverything_DestroysLaunchFleet()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");
        var launch = new Fleet { Location = new Coordinate(2, 2), Owner = owner, Fuel = 50.0 };
        launch.Ships.Fighters = 5;
        galaxy.Fleets.Add(launch);

        var newFleet = FleetLifecycle.DeployFleet(owner, launch, new ShipCounts { Fighters = 5 }, new CargoHold(), new Coordinate(9, 9), game);

        await Assert.That(galaxy.Fleets).DoesNotContain(launch); // donated everything -> ChangeCompositionOfFleet destroys it
        await Assert.That(galaxy.Fleets).Contains(newFleet);
        await Assert.That(newFleet.Ships.Fighters).IsEqualTo(5);
    }

    [Test]
    public async Task RefuelFleet_TopsUpFuelAndDebitsGroundTrillum()
    {
        var target = new Fleet { Location = new Coordinate(3, 3), Owner = NewEmpire("Owner"), Fuel = 0.0, Destination = new Coordinate(3, 3) };
        target.Ships.Starships = 10; // enough fuel capacity that a 1-ton top-up doesn't hit the cap
        var ground = new Planet { Location = new Coordinate(3, 3), Owner = target.Owner, Type = WorldType.Base };
        ground.Cargo.Trillum = 100;

        FleetLifecycle.RefuelFleet(target, ground, trillum: 1);

        await Assert.That(target.Fuel).IsEqualTo(FleetLogistics.FuelPerTon);
        await Assert.That(ground.Cargo.Trillum).IsEqualTo(99);
        await Assert.That(target.Status).IsEqualTo(FleetStatus.Ready); // above consumption, already at destination
    }

    [Test]
    public async Task RefuelFleet_ArrivedFleetWithNullDestination_ResolvesToReadyNotInTransit()
    {
        // Regression: a fleet whose Destination FleetMovementHandler already nulled out on arrival
        // was falling through to InTransit here (null never equals a real Coordinate), leaving a
        // Status/Destination combination CloseUpWindow's own EstimatedDateOfArrival call correctly
        // refuses to handle -- crashing Close Up on any fleet refueled after arriving.
        var target = new Fleet { Location = new Coordinate(3, 3), Owner = NewEmpire("Owner"), Fuel = 0.0, Destination = null };
        target.Ships.Starships = 10;
        var ground = new Planet { Location = new Coordinate(3, 3), Owner = target.Owner, Type = WorldType.Base };
        ground.Cargo.Trillum = 100;

        FleetLifecycle.RefuelFleet(target, ground, trillum: 1);

        await Assert.That(target.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task RefuelFleet_TargetIsStarbase_FuelSideIsNoOpButTrillumStillSpent()
    {
        var target = new Starbase { Location = new Coordinate(3, 3), Owner = NewEmpire("Owner") };
        var ground = new Planet { Location = new Coordinate(3, 3), Owner = target.Owner, Type = WorldType.Base };
        ground.Cargo.Trillum = 100;

        FleetLifecycle.RefuelFleet(target, ground, trillum: 10);

        await Assert.That(ground.Cargo.Trillum).IsEqualTo(90); // still spent, per real Pascal's GetFleetFuel/SetFleetFuel no-op
    }

    [Test]
    public async Task AbortFleet_DumpsShipsAndCargoOntoGroundAndDestroysFleet()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 10.0 };
        fleet.Ships.Fighters = 3;
        fleet.Cargo.Metals = 5;
        var world = new Planet { Location = new Coordinate(0, 0), Owner = owner, Type = WorldType.Base };
        world.Ships.Fighters = 1;
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(world);

        FleetLifecycle.AbortFleet(fleet, world, game);

        await Assert.That(galaxy.Fleets).DoesNotContain(fleet);
        await Assert.That(world.Ships.Fighters).IsEqualTo(4); // 1 already there + 3 from the fleet
        await Assert.That(world.Cargo.Metals).IsEqualTo(5);
    }

    [Test]
    public async Task AbortFleet_OntoAnotherFleet_MergesFuelToo()
    {
        var (game, galaxy) = NewGame();
        var owner = NewEmpire("Owner");

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 10.0 };
        var ground = new Fleet { Location = new Coordinate(0, 0), Owner = owner, Fuel = 5.0 };
        galaxy.Fleets.Add(fleet);
        galaxy.Fleets.Add(ground);

        FleetLifecycle.AbortFleet(fleet, ground, game);

        await Assert.That(galaxy.Fleets).DoesNotContain(fleet);
        await Assert.That(galaxy.Fleets).Contains(ground);
        await Assert.That(ground.Fuel).IsEqualTo(15.0);
    }

    [Test]
    public async Task MaxTrillumToRefuel_CapsAtWhicheverIsSmaller_TonsNeededOrTonsOnGround()
    {
        var target = new Fleet { Location = new Coordinate(0, 0), Owner = NewEmpire("Owner"), Fuel = 0.0 };
        target.Ships.Starships = 10; // big capacity so TonsNeeded is well above 1
        var maxFuel = FleetLogistics.FuelCapacity(target.Ships);
        var tonsNeeded = (int)(maxFuel / FleetLogistics.FuelPerTon) + 1;
        var ground = new Planet { Location = new Coordinate(0, 0), Owner = target.Owner, Type = WorldType.Base };
        ground.Cargo.Trillum = tonsNeeded + 50; // plenty on ground -> capped by TonsNeeded

        await Assert.That(FleetLifecycle.MaxTrillumToRefuel(target, ground)).IsEqualTo(tonsNeeded);

        ground.Cargo.Trillum = 2; // scarce on ground -> capped by what's there instead
        await Assert.That(FleetLifecycle.MaxTrillumToRefuel(target, ground)).IsEqualTo(2);
    }

    [Test]
    public async Task MaxTrillumToRefuel_TargetIsStarbase_TreatsItsFuelAsZero()
    {
        // GetFleetFuel no-op precedent (RefuelFleet's own doc comment) -- a Starbase target always
        // reads as needing a full tank's worth, regardless of anything a real Fleet's own Fuel field
        // might otherwise hold.
        var starbase = new Starbase { Location = new Coordinate(0, 0), Owner = NewEmpire("Owner") };
        starbase.Ships.Starships = 10;
        var maxFuel = FleetLogistics.FuelCapacity(starbase.Ships);
        var expectedTonsNeeded = (int)(maxFuel / FleetLogistics.FuelPerTon) + 1;
        var ground = new Planet { Location = new Coordinate(0, 0), Owner = starbase.Owner, Type = WorldType.Base };
        ground.Cargo.Trillum = 9999;

        await Assert.That(FleetLifecycle.MaxTrillumToRefuel(starbase, ground)).IsEqualTo(expectedTonsNeeded);
    }

    [Test]
    public async Task EstimatedDateOfArrival_PlainDistance_DividesByMovementRateRoundedUp()
    {
        var (game, _) = NewGame();
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = NewEmpire("Owner"), Destination = new Coordinate(5, 0) };
        fleet.Ships.Starships = 1; // Standard fleet type, movement rate 1/year

        var eda = FleetLifecycle.EstimatedDateOfArrival(fleet, game);

        await Assert.That(eda).IsEqualTo(5);
    }

    [Test]
    public async Task EstimatedRange_Fleet_IsFuelDividedByConsumption()
    {
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = NewEmpire("Owner"), Fuel = 20.0 };
        fleet.Ships.Fighters = 1;

        var expected = (int)(20.0 / FleetLogistics.FuelConsumption(fleet.Ships, fleet.Cargo));

        await Assert.That(FleetLifecycle.EstimatedRange(fleet)).IsEqualTo(expected);
    }

    [Test]
    public async Task EstimatedRange_Starbase_IsTrillumDividedBy100()
    {
        var starbase = new Starbase { Location = new Coordinate(0, 0), Owner = NewEmpire("Owner") };
        starbase.Cargo.Trillum = 350;

        await Assert.That(FleetLifecycle.EstimatedRange(starbase)).IsEqualTo(3);
    }
}
