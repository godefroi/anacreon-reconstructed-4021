using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="AutoResupply"/> -- GitHub issue #85's automatic cargo resupply, confirmed to have no
/// Pascal source (see <see cref="ResupplySettings"/>'s own doc comment), so these are hardcoded
/// against the feature's own spec rather than a golden file. Most tests drive
/// <see cref="Planet.ShortfallsLastTick"/> directly rather than running a full annual tick -- that
/// signal's own wiring (production/starvation -> ShortfallsLastTick) has its own coverage in
/// AnnualTickHandlerTests.
/// </summary>
public class AutoResupplyTests
{
    private static (Game Game, Galaxy Galaxy, Empire Owner) NewGame()
    {
        var galaxy = new Galaxy(size: 50);
        var game = new Game(galaxy);
        var owner = new Empire { Name = "Test" };
        game.Empires.Add(owner);
        return (game, galaxy, owner);
    }

    private static Planet NewSource(Empire owner, WorldType type, Coordinate location) =>
        new() { Location = location, Owner = owner, Type = type };

    private static Planet NewDestination(Empire owner, Coordinate location, int population = 100) =>
        new() { Location = location, Owner = owner, Type = WorldType.Base, Population = population };

    // Fleet.Type derives from ship composition (Fleet.cs) -- Transports-only is FleetType.Standard
    // (MovementRate 1), Jumptransports-only (no Transports/Starships/Fighters/Penetrators) is
    // FleetType.JumpFleet (MovementRate 10). Fuel=1000 is comfortably enough range for every distance
    // used in these tests; a dedicated test drives it down to 0 to exercise the range gate itself.
    private static Fleet NewIdleTransportFleet(Empire owner, Coordinate location, int transports = 5, double fuel = 1000) =>
        new() { Location = location, Owner = owner, Status = FleetStatus.Ready, Fuel = fuel, Ships = { Transports = transports } };

    private static Fleet NewIdleJumptransportFleet(Empire owner, Coordinate location, int jumptransports = 5, double fuel = 1000) =>
        new() { Location = location, Owner = owner, Status = FleetStatus.Ready, Fuel = fuel, Ships = { Jumptransports = jumptransports } };

    [Test]
    [Arguments(WorldType.Agricultural, new[] { CargoType.Supplies })]
    [Arguments(WorldType.Chemical, new[] { CargoType.Chemicals })]
    [Arguments(WorldType.Mine, new[] { CargoType.Metals })]
    [Arguments(WorldType.TrillumMine, new[] { CargoType.Trillum })]
    [Arguments(WorldType.RawMaterialMine, new[] { CargoType.Chemicals, CargoType.Metals, CargoType.Trillum })]
    [Arguments(WorldType.Ambrosia, new[] { CargoType.Ambrosia })]
    [Arguments(WorldType.Capital, new[] { CargoType.Chemicals, CargoType.Metals, CargoType.Trillum, CargoType.Supplies })]
    [Arguments(WorldType.University, new CargoType[0])]
    public async Task EligibleCargo_MatchesResolvedTable(WorldType type, CargoType[] expected)
    {
        await Assert.That(ResupplySettings.EligibleCargo(type)).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Apply_StarvationOutranksPriorityAndNormal_OnlyOneFleetAvailable()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Capital, new Coordinate(0, 0));
        source.Cargo.Supplies = 1000;
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;

        var priority = NewDestination(owner, new Coordinate(1, 0));
        priority.ShortfallsLastTick = [CargoType.Metals];
        source.Resupply.Priority.Add(priority.Location);

        var starving = NewDestination(owner, new Coordinate(2, 0));
        starving.ShortfallsLastTick = [CargoType.Supplies];

        galaxy.Planets.AddRange([source, priority, starving]);
        galaxy.Fleets.Add(NewIdleTransportFleet(owner, source.Location));

        AutoResupply.Apply(game);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.NextOrder).IsEqualTo(1);
        await Assert.That(fleet.Orders[2].DestinationObject).IsEqualTo(starving);
        await Assert.That(fleet.Orders[3].TransferCargo).IsEqualTo(CargoType.Supplies);
    }

    [Test]
    public async Task Apply_PriorityGroupOutranksNormalGroup_OnlyOneFleetAvailable()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;

        var normal = NewDestination(owner, new Coordinate(1, 0));
        normal.ShortfallsLastTick = [CargoType.Metals];

        var priority = NewDestination(owner, new Coordinate(2, 0));
        priority.ShortfallsLastTick = [CargoType.Metals];
        source.Resupply.Priority.Add(priority.Location);

        galaxy.Planets.AddRange([source, normal, priority]);
        galaxy.Fleets.Add(NewIdleTransportFleet(owner, source.Location));

        AutoResupply.Apply(game);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.NextOrder).IsEqualTo(1);
        await Assert.That(fleet.Orders[2].DestinationObject).IsEqualTo(priority);
    }

    [Test]
    public async Task Apply_PriorityGroupRespectsListOrder()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;

        var second = NewDestination(owner, new Coordinate(1, 0));
        second.ShortfallsLastTick = [CargoType.Metals];
        var first = NewDestination(owner, new Coordinate(2, 0));
        first.ShortfallsLastTick = [CargoType.Metals];

        // List order is the priority, not declaration order: "first" is listed second.
        source.Resupply.Priority.Add(second.Location);
        source.Resupply.Priority.Insert(0, first.Location);

        galaxy.Planets.AddRange([source, second, first]);
        galaxy.Fleets.Add(NewIdleTransportFleet(owner, source.Location));

        AutoResupply.Apply(game);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.Orders[2].DestinationObject).IsEqualTo(first);
    }

    [Test]
    public async Task Apply_NormalGroupRanksByPopulationDescending()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;

        var smaller = NewDestination(owner, new Coordinate(1, 0), population: 50);
        smaller.ShortfallsLastTick = [CargoType.Metals];
        var bigger = NewDestination(owner, new Coordinate(2, 0), population: 5000);
        bigger.ShortfallsLastTick = [CargoType.Metals];

        galaxy.Planets.AddRange([source, smaller, bigger]);
        galaxy.Fleets.Add(NewIdleTransportFleet(owner, source.Location));

        AutoResupply.Apply(game);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.Orders[2].DestinationObject).IsEqualTo(bigger);
    }

    [Test]
    public async Task Apply_NeverGroupIsExcludedEvenFromStarvation()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Capital, new Coordinate(0, 0));
        source.Cargo.Supplies = 1000;
        source.Resupply.Enabled = true;

        var never = NewDestination(owner, new Coordinate(1, 0), population: 5000);
        never.ShortfallsLastTick = [CargoType.Supplies];
        source.Resupply.Never.Add(never.Location);

        var normal = NewDestination(owner, new Coordinate(2, 0), population: 10);
        normal.ShortfallsLastTick = [CargoType.Supplies];

        galaxy.Planets.AddRange([source, never, normal]);
        galaxy.Fleets.Add(NewIdleTransportFleet(owner, source.Location));

        AutoResupply.Apply(game);

        // "never" outranks "normal" by population and even carries a starvation shortfall, but the
        // Never group is an absolute exclusion -- "normal" gets served instead.
        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.NextOrder).IsEqualTo(1);
        await Assert.That(fleet.Orders[2].DestinationObject).IsEqualTo(normal);
    }

    [Test]
    public async Task Apply_PruneLostWorlds_RemovesConqueredCoordinateFromPriorityAndNever()
    {
        var (game, galaxy, owner) = NewGame();
        var otherEmpire = new Empire { Name = "Other" };
        game.Empires.Add(otherEmpire);

        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        var lostLocation = new Coordinate(1, 0);
        source.Resupply.Priority.Add(lostLocation);
        source.Resupply.Never.Add(new Coordinate(2, 0));

        // The world at lostLocation now belongs to a different empire -- conquered since these lists
        // were last edited.
        var conquered = new Planet { Location = lostLocation, Owner = otherEmpire, Type = WorldType.Base };
        var stillOwned = new Planet { Location = new Coordinate(2, 0), Owner = owner, Type = WorldType.Base };
        galaxy.Planets.AddRange([source, conquered, stillOwned]);

        AutoResupply.Apply(game);

        await Assert.That(source.Resupply.Priority).IsEmpty();
        await Assert.That(source.Resupply.Never).IsEquivalentTo([new Coordinate(2, 0)]);
    }

    [Test]
    public async Task Apply_GlobalMaxAmountCapsEveryDispatchRegardlessOfGroup()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;
        source.Resupply.MaxAmount = 50;

        var destination = NewDestination(owner, new Coordinate(1, 0));
        destination.ShortfallsLastTick = [CargoType.Metals];
        source.Resupply.Priority.Add(destination.Location);

        galaxy.Planets.AddRange([source, destination]);
        // Plenty of ships -> plenty of cargo room, so the cap (not fleet capacity) is the binding limit.
        galaxy.Fleets.Add(NewIdleTransportFleet(owner, source.Location, transports: 500));

        AutoResupply.Apply(game);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.Orders[1].TransferAmount).IsEqualTo(50);
        await Assert.That(fleet.Orders[3].TransferAmount).IsEqualTo(-50);
    }

    [Test]
    public async Task Apply_PicksLowestMovementRateFleet()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;

        var destination = NewDestination(owner, new Coordinate(3, 0));
        destination.ShortfallsLastTick = [CargoType.Metals];

        galaxy.Planets.AddRange([source, destination]);
        var fast = NewIdleJumptransportFleet(owner, source.Location); // FleetType.JumpFleet, rate 10
        var slow = NewIdleTransportFleet(owner, source.Location); // FleetType.Standard, rate 1
        galaxy.Fleets.AddRange([fast, slow]);

        AutoResupply.Apply(game);

        await Assert.That(slow.NextOrder).IsEqualTo(1);
        await Assert.That(fast.NextOrder).IsEqualTo(0);
    }

    [Test]
    public async Task Apply_FleetWithoutRangeForRoundTrip_IsSkipped()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;

        var destination = NewDestination(owner, new Coordinate(10, 0));
        destination.ShortfallsLastTick = [CargoType.Metals];

        galaxy.Planets.AddRange([source, destination]);
        var starved = NewIdleTransportFleet(owner, source.Location, fuel: 0);
        galaxy.Fleets.Add(starved);

        AutoResupply.Apply(game);

        await Assert.That(starved.NextOrder).IsEqualTo(0);
        await Assert.That(starved.Orders).IsEmpty();
    }

    [Test]
    public async Task Apply_PendingDeliveryAlreadyQueued_SkipsTheShortfall()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;
        source.Resupply.Enabled = true;

        var destination = NewDestination(owner, new Coordinate(1, 0));
        destination.ShortfallsLastTick = [CargoType.Metals];

        galaxy.Planets.AddRange([source, destination]);

        // A fleet already en route with the same delivery queued -- simulates last tick's own dispatch,
        // still mid-flight (a real multi-turn round trip).
        var enRoute = new Fleet {
            Location = new Coordinate(5, 5), Owner = owner, Status = FleetStatus.InTransit, NextOrder = 3,
            Orders = [
                new FleetOrder(CommandType.Destination, DestinationObject: source),
                new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: 100),
                new FleetOrder(CommandType.Destination, DestinationObject: destination),
                new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: -100),
                new FleetOrder(CommandType.Destination, DestinationObject: source),
                new FleetOrder(CommandType.Refuel),
            ],
        };
        galaxy.Fleets.Add(enRoute);

        var idle = NewIdleTransportFleet(owner, source.Location);
        galaxy.Fleets.Add(idle);

        AutoResupply.Apply(game);

        await Assert.That(idle.NextOrder).IsEqualTo(0); // shortfall suppressed -- idle fleet left untouched
        await Assert.That(idle.Orders).IsEmpty();
    }

    [Test]
    public async Task Apply_TwoDestinationsShareOneSourcesStock_SecondIsReservedAgainstTheFirst()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 100; // exactly enough for one full dispatch, not two
        source.Resupply.Enabled = true;

        var bigger = NewDestination(owner, new Coordinate(1, 0), population: 5000);
        bigger.ShortfallsLastTick = [CargoType.Metals];
        var smaller = NewDestination(owner, new Coordinate(2, 0), population: 50);
        smaller.ShortfallsLastTick = [CargoType.Metals];

        galaxy.Planets.AddRange([source, bigger, smaller]);
        var first = NewIdleTransportFleet(owner, source.Location, transports: 500);
        var second = NewIdleTransportFleet(owner, source.Location, transports: 500);
        galaxy.Fleets.AddRange([first, second]);

        AutoResupply.Apply(game);

        // "bigger" is served first (normal-group population ranking) and takes the whole 100
        // available; "smaller"'s own dispatch then sees 0 left reserved and is skipped, even though a
        // second idle fleet was still sitting right there.
        var dispatched = galaxy.Fleets.Where(f => f.NextOrder != 0).ToList();
        await Assert.That(dispatched).Count().IsEqualTo(1);
        await Assert.That(dispatched.Single().Orders[2].DestinationObject).IsEqualTo(bigger);
        await Assert.That(dispatched.Single().Orders[1].TransferAmount).IsEqualTo(100);
    }

    [Test]
    public async Task Apply_SourceNotEnabled_DoesNothing()
    {
        var (game, galaxy, owner) = NewGame();
        var source = NewSource(owner, WorldType.Mine, new Coordinate(0, 0));
        source.Cargo.Metals = 1000;

        var destination = NewDestination(owner, new Coordinate(1, 0));
        destination.ShortfallsLastTick = [CargoType.Metals];

        galaxy.Planets.AddRange([source, destination]);
        var idle = NewIdleTransportFleet(owner, source.Location);
        galaxy.Fleets.Add(idle);

        AutoResupply.Apply(game);

        await Assert.That(idle.NextOrder).IsEqualTo(0);
    }

    [Test]
    public async Task HasPendingDelivery_TrueForQueuedDropOff_FalseOtherwise()
    {
        var (game, galaxy, owner) = NewGame();
        var destination = NewDestination(owner, new Coordinate(1, 0));
        galaxy.Planets.Add(destination);

        var fleet = new Fleet {
            Location = new Coordinate(5, 5), Owner = owner, NextOrder = 1,
            Orders = [
                new FleetOrder(CommandType.Destination, DestinationObject: destination),
                new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: -100),
            ],
        };
        galaxy.Fleets.Add(fleet);

        await Assert.That(AutoResupply.HasPendingDelivery(game, owner, destination.Location, CargoType.Metals)).IsTrue();
        await Assert.That(AutoResupply.HasPendingDelivery(game, owner, destination.Location, CargoType.Chemicals)).IsFalse();
    }
}
