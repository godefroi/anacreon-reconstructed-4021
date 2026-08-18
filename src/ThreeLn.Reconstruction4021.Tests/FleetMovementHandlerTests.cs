using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

public class FleetMovementHandlerTests
{
    [Test]
    public async Task SequentialMovement_ActingEmpireJumpFleetAndNextEmpireWarpFleetAdvanceTogether()
    {
        var human = new Empire { Name = "Human" };
        var ai = new Empire { Name = "AI" };
        var game = new Game(new Galaxy(size: 20));
        game.Empires.Add(human);
        game.Empires.Add(ai);

        var jumpFleet = new Fleet
        {
            Owner = human,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(10, 0),
            Fuel = 100,
            Status = FleetStatus.Ready,
        };
        jumpFleet.Ships.Jumpships = 2;

        var warpFleet = new Fleet
        {
            Owner = ai,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(2, 0),
            Fuel = 100,
            Status = FleetStatus.Ready,
        };
        warpFleet.Ships.Fighters = 2;

        game.Galaxy.Fleets.Add(jumpFleet);
        game.Galaxy.Fleets.Add(warpFleet);

        var handler = new FleetMovementHandler();
        handler.AdvanceFleets(game, human, ai);

        await Assert.That(jumpFleet.Location).IsEqualTo(new Coordinate(10, 0));
        await Assert.That(warpFleet.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(jumpFleet.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(warpFleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    [Test]
    public async Task FleetWithInsufficientFuel_DeactivatesInsteadOfMoving()
    {
        var human = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet
        {
            Owner = human,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(10, 0),
            Fuel = 0,
            Status = FleetStatus.Ready,
        };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler();
        handler.AdvanceFleets(game, human, new Empire { Name = "AI" });

        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Inactive);
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(fleet.Destination).IsNull();
    }

    [Test]
    public async Task StarbaseMovesOneCellPerTurnTowardDestination()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var starbase = new Starbase
        {
            Owner = empire,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(1, 0),
            Kind = StarbaseKind.CommandBase,
            Status = FleetStatus.Ready,
        };
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler();
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(starbase.Destination).IsNull();
        await Assert.That(starbase.Status).IsEqualTo(FleetStatus.Ready);
    }
}
