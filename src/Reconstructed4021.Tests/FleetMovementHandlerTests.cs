using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

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

        var jumpFleet = new Fleet {
            Owner = human,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(10, 0),
            Fuel = 100,
            Status = FleetStatus.Ready,
        };
        jumpFleet.Ships.Jumpships = 2;

        var warpFleet = new Fleet {
            Owner = ai,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(2, 0),
            Fuel = 100,
            Status = FleetStatus.Ready,
        };
        warpFleet.Ships.Fighters = 2;

        game.Galaxy.Fleets.Add(jumpFleet);
        game.Galaxy.Fleets.Add(warpFleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
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
        var fleet = new Fleet {
            Owner = human,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(10, 0),
            Fuel = 0,
            Status = FleetStatus.Ready,
        };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        // Standard-type fleets only advance as the *next* empire's fleets (FLEET.PAS:892-903),
        // so human must be passed as nextEmpire here for this fleet to be evaluated at all.
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, human);

        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Inactive);
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0));
        // UseUpFuel (FLEET.PAS:660-703) never touches Destination -- a fleet that runs dry still
        // remembers where it was headed, so it can resume once refueled.
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(10, 0));
        await Assert.That(human.News).Contains(n => n.Headline == NewsType.FleetOutOfFuel);
    }

    [Test]
    public async Task StarbaseMovesOneCellPerTurnTowardDestination()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var starbase = new Starbase {
            Owner = empire,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(1, 0),
            Kind = StarbaseKind.CommandBase,
            Status = FleetStatus.Ready,
        };
        starbase.Cargo.Trillum = 200;
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(starbase.Destination).IsNull();
        await Assert.That(starbase.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(starbase.Cargo.Trillum).IsEqualTo(100);
    }

    [Test]
    public async Task StarbaseWithoutEnoughTrillum_ReportsOutOfFuelAndStays()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var starbase = new Starbase {
            Owner = empire,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(5, 0),
            Kind = StarbaseKind.Fortress,
            Status = FleetStatus.Ready,
        };
        starbase.Cargo.Trillum = 50;
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(starbase.Cargo.Trillum).IsEqualTo(50);
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.StarbaseOutOfFuel);
    }

    [Test]
    public async Task StarbaseBlockedByOccupiedCell_ReportsBlockedAndStaysAfterPayingFuel()
    {
        var empire = new Empire { Name = "Human" };
        var blocker = new Empire { Name = "Blocker" };
        var game = new Game(new Galaxy(size: 20));
        // Occupies (1,0), (1,-1) and (1,1) so every candidate the direct/alternate-direction search
        // could reach toward (5,0) is blocked -- GetObjectAt is owner-blind, so a foreign starbase
        // blocks just as a planet or gate would.
        game.Galaxy.Starbases.Add(new Starbase { Owner = blocker, Location = new Coordinate(1, 0), Kind = StarbaseKind.IndustrialComplex });
        game.Galaxy.Starbases.Add(new Starbase { Owner = blocker, Location = new Coordinate(1, -1), Kind = StarbaseKind.IndustrialComplex });
        game.Galaxy.Starbases.Add(new Starbase { Owner = blocker, Location = new Coordinate(1, 1), Kind = StarbaseKind.IndustrialComplex });

        var starbase = new Starbase {
            Owner = empire,
            Location = new Coordinate(0, 0),
            Destination = new Coordinate(5, 0),
            Kind = StarbaseKind.CommandBase,
            Status = FleetStatus.Ready,
        };
        starbase.Cargo.Trillum = 200;
        game.Galaxy.Starbases.Add(starbase);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceStarbases(game, empire);

        await Assert.That(starbase.Location).IsEqualTo(new Coordinate(0, 0));
        // Blocked moves still cost fuel (SBASE.PAS:249-250 deducts FuelCon unconditionally).
        await Assert.That(starbase.Cargo.Trillum).IsEqualTo(100);
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.StarbaseBlocked);
    }

    [Test]
    public async Task JumpFleet_HitsEnemyMinefield_SurvivesDamagedAndLandsOnTheMinedCell()
    {
        var victim = new Empire { Name = "Victim" };
        var mineOwner = new Empire { Name = "MineOwner" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.SetMine(new Coordinate(1, 0), mineOwner);

        var fleet = new Fleet { Owner = victim, Location = new Coordinate(0, 0), Destination = new Coordinate(5, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Jumpships = 10;
        game.Galaxy.Fleets.Add(fleet);

        // Rnd(1,100) with FixedRandom(0) = 1; ProtecNeeded[Jumpship]=20, so destroyed = min(10, 1 + PascalRound(10*0.4)) = 5.
        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, victim, new Empire { Name = "Other" });

        await Assert.That(game.Galaxy.Fleets).Contains(fleet);
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(fleet.Ships.Jumpships).IsEqualTo(5);
        await Assert.That(game.Galaxy.IsMineScoutedBy(victim, new Coordinate(1, 0))).IsTrue();
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.FleetDamagedByMines && n.OtherEmpire == mineOwner);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.DestructionDetail && n.Parm1 == 5 && n.Resource == new ResourceKind.Ship(ShipType.Jumpship));
        await Assert.That(mineOwner.News).Contains(n => n.Headline == NewsType.EnemyFleetDamagedInMinefield && n.OtherEmpire == victim && n.Position == new Coordinate(1, 0));
    }

    [Test]
    public async Task JumpFleet_HitsEnemyMinefield_DestroyedWhenNoShipsSurvive()
    {
        var victim = new Empire { Name = "Victim" };
        var mineOwner = new Empire { Name = "MineOwner" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.SetMine(new Coordinate(1, 0), mineOwner);

        var fleet = new Fleet { Owner = victim, Location = new Coordinate(0, 0), Destination = new Coordinate(5, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Jumpships = 1;
        game.Galaxy.Fleets.Add(fleet);

        // Rnd(1,100) with FixedRandom(0) = 1; destroyed = min(1, 1 + PascalRound(1*0.4)) = 1 — total loss.
        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, victim, new Empire { Name = "Other" });

        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.FleetDestroyedByMines && n.OtherEmpire == mineOwner);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.DestructionDetail && n.Parm1 == 1 && n.Resource == new ResourceKind.Ship(ShipType.Jumpship));
    }

    [Test]
    public async Task JumpFleet_PassesNearEnemyDisrupterGate_StopsAndLandsOnTheBlockedCell()
    {
        var victim = new Empire { Name = "Victim" };
        var gateOwner = new Empire { Name = "GateOwner" };
        var game = new Game(new Galaxy(size: 20));

        var gate = new Stargate { Owner = gateOwner, Location = new Coordinate(2, 0), Kind = StargateKind.Disrupter };
        game.Galaxy.Stargates.Add(gate);

        var fleet = new Fleet { Owner = victim, Location = new Coordinate(0, 0), Destination = new Coordinate(10, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Jumpships = 5;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, victim, new Empire { Name = "Other" });

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(1, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(fleet.Ships.Jumpships).IsEqualTo(5);
        await Assert.That(victim.News).Contains(n => n.Headline == NewsType.FleetStoppedByDisrupter && n.OtherEmpire == gateOwner);
    }

    [Test]
    public async Task StandardFleet_HitsDenseNebula_RevertsToLastCellAndReportsBlocked()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.SetNebula(new Coordinate(1, 0), NebulaType.DenseNebula);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(5, 0), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.FleetBlocked);
    }

    [Test]
    public async Task FleetAtPublicGate_TeleportsDirectlyToDestination()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.Gate });

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(15, 15), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        // Standard fleets advance as the next empire's fleets, and a fleet sitting at any gate
        // (owner-blind occupancy check) also advances in the next-empire slot (FLEET.PAS:892-903).
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(15, 15));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(fleet.Destination).IsNull();
    }

    [Test]
    public async Task FleetAtPublicGate_DestinationInDenseNebula_CannotGateThereAndStays()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.Gate });
        game.Galaxy.SetNebula(new Coordinate(15, 15), NebulaType.DenseNebula);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(15, 15), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0));
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(15, 15));
        await Assert.That(empire.News).Contains(n => n.Headline == NewsType.CannotGateToDenseNebula);
    }

    [Test]
    public async Task FleetAtWarpLink_DestinationIsMatchingKnownLink_Teleports()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var originLink = new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.WarpLink };
        var destLink = new Stargate { Owner = empire, Location = new Coordinate(12, 12), Kind = StargateKind.WarpLink };
        game.Galaxy.Stargates.Add(originLink);
        game.Galaxy.Stargates.Add(destLink);
        empire.Stargates.MarkKnown(destLink);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(12, 12), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(12, 12));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task FleetAtWarpLink_DestinationLinkUnknown_DoesNotTeleport()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(0, 0), Kind = StargateKind.WarpLink });
        // Destination gate exists but the fleet's owner has never scouted/been told about it.
        game.Galaxy.Stargates.Add(new Stargate { Owner = empire, Location = new Coordinate(12, 12), Kind = StargateKind.WarpLink });

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(12, 12), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        // Falls through to ordinary movement instead: 1 step toward (12,12) from (0,0).
        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(1, 1));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    [Test]
    public async Task FleetAtFortress_WithinFiveOfDestination_Teleports()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Starbases.Add(new Starbase { Owner = empire, Location = new Coordinate(0, 0), Kind = StarbaseKind.Fortress });

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(3, 3), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(3, 3));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
    }

    [Test]
    public async Task FleetAtFortress_FarFromDestination_HopsFiveThenContinuesWithOrdinaryMovementSameTurn()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        game.Galaxy.Starbases.Add(new Starbase { Owner = empire, Location = new Coordinate(0, 0), Kind = StarbaseKind.Fortress });

        // Standard fleet: FltMovementRate=1, so a plain fleet would only reach (1,1) this turn.
        // A fortress boosts it 5 cells first (to (5,5)), then the ordinary 1-cell allotment applies
        // on top, landing at (6,6) -- both apply in the same turn (FLEET.PAS:768-838).
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Destination = new Coordinate(15, 15), Fuel = 1000, Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(6, 6));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    [Test]
    public async Task ExecuteFleetOrders_DestCOM_SetsDestinationAndStopsTheLoop()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(5, 5)));
        fleet.Orders.Add(new FleetOrder(CommandType.Wait));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(5, 5));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        // Stopped at the DestCOM itself -- the WaitCOM right after it never runs this call.
        await Assert.That(fleet.NextOrder).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteFleetOrders_DestCOM_TargetingAnObject_TracksItsCurrentLocation()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var target = new Starbase { Owner = empire, Location = new Coordinate(7, 7), Kind = StarbaseKind.Outpost };
        game.Galaxy.Starbases.Add(target);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationObject: target));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(7, 7));
    }

    [Test]
    public async Task ExecuteFleetOrders_WaitCOM_StopsTheLoopWithoutTouchingDestination()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        fleet.Orders.Add(new FleetOrder(CommandType.Wait));
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(5, 5)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(fleet.Destination).IsNull();
        await Assert.That(fleet.NextOrder).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteFleetOrders_ReachingTheLastCommand_ClearsOrdersAndZeroesNextOrder()
    {
        // Real Pascal (FLEET.PAS:582-611) advances/disposes Com unconditionally every iteration,
        // *before* the UNTIL check -- so reaching the very last command in the list clears the whole
        // queue and zeroes NextOrder in this same call, even though that same command (Wait here) also
        // independently satisfies the stop condition. Genuinely surprising but faithful: a script's
        // last real action (with no trailing Repeat) is a one-shot, not something that survives to be
        // "resumed" on a later arrival. Starts at NextOrder=2 -- as if a prior call already stopped at
        // order 1's own Wait -- to isolate this from DestCOM-specific or first-order-specific behavior.
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        fleet.Orders.Add(new FleetOrder(CommandType.Wait));
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(5, 5)));
        fleet.NextOrder = 2;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(5, 5));
        await Assert.That(fleet.Orders).IsEmpty();
        await Assert.That(fleet.NextOrder).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteFleetOrders_RepeatCOM_LoopsExactlyOnceWhenTwoRepeatsAreAdjacent()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        // REPEAT, REPEAT, WAIT, DEST -- the first Repeat jumps back to order 1 (itself); IgnoreRepeat
        // then stops the second Repeat from doing the same, so execution falls through to Wait instead
        // of looping forever. A trailing DEST keeps Wait from being the *last* command, so this stays
        // isolated from the separate "last command clears the queue" behavior (see the test above).
        fleet.Orders.Add(new FleetOrder(CommandType.Repeat));
        fleet.Orders.Add(new FleetOrder(CommandType.Repeat));
        fleet.Orders.Add(new FleetOrder(CommandType.Wait));
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(9, 9)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(fleet.NextOrder).IsEqualTo(4);
        await Assert.That(fleet.Orders).Count().IsEqualTo(4);
    }

    [Test]
    public async Task ExecuteFleetOrders_TransCOM_PicksUpCargoFromGroundClampedToWhatsAvailable()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet {
            Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base,
        };
        ground.Cargo.Metals = 100;
        game.Galaxy.Planets.Add(ground);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Transports = 100; // plenty of cargo space
        // A trailing DEST after Wait keeps Wait from being the *last* command -- see the "reaching the
        // last command" test above for why that distinction matters here.
        fleet.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: 9999));
        fleet.Orders.Add(new FleetOrder(CommandType.Wait));
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(9, 9)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        // Clamped to what the ground actually had (100), not the requested 9999.
        await Assert.That(fleet.Cargo.Metals).IsEqualTo(100);
        await Assert.That(ground.Cargo.Metals).IsEqualTo(0);
        // Transfer doesn't stop the loop -- execution continues straight into the WaitCOM after it.
        await Assert.That(fleet.NextOrder).IsEqualTo(3);
        await Assert.That(fleet.Orders).Count().IsEqualTo(3);
    }

    [Test]
    public async Task ExecuteFleetOrders_TransCOM_DropOffOntoGround()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet {
            Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base,
        };
        game.Galaxy.Planets.Add(ground);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Transports = 100;
        fleet.Cargo.Metals = 50;
        fleet.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: -30));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(fleet.Cargo.Metals).IsEqualTo(20);
        await Assert.That(ground.Cargo.Metals).IsEqualTo(30);
    }

    [Test]
    public async Task ExecuteFleetOrders_TransCOM_GroundNotOwnedByFleet_IsANoOp()
    {
        var empire = new Empire { Name = "Human" };
        var other = new Empire { Name = "Other" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet {
            Location = new Coordinate(0, 0), Owner = other, Class = WorldClass.EarthLike, Type = WorldType.Base,
        };
        ground.Cargo.Metals = 100;
        game.Galaxy.Planets.Add(ground);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Transports = 100;
        fleet.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: 50));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(fleet.Cargo.Metals).IsEqualTo(0);
        await Assert.That(ground.Cargo.Metals).IsEqualTo(100);
    }

    [Test]
    public async Task ExecuteFleetOrders_RefuCOM_ConvertsGroundTrillumToFuel()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet {
            Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base,
        };
        ground.Cargo.Trillum = 100;
        game.Galaxy.Planets.Add(ground);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Fuel = 0, Status = FleetStatus.Ready };
        fleet.Ships.Transports = 10;
        fleet.Orders.Add(new FleetOrder(CommandType.Refuel));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        var expectedTrillum = FleetLifecycle.MaxTrillumToRefuel(fleet, ground);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(ground.Cargo.Trillum).IsEqualTo(100 - expectedTrillum);
        // MaxTrillumToRefuel rounds its tons-needed estimate up (FleetLifecycle's own +1), so a
        // ground with plenty of trillum tops the tank exactly to capacity, not to trillum*FuelPerTon.
        await Assert.That(fleet.Fuel).IsEqualTo(FleetLogistics.FuelCapacity(fleet.Ships));
    }

    [Test]
    public async Task ExecuteFleetOrders_RefuCOM_GroundNotOwnedByFleet_IsANoOp()
    {
        var empire = new Empire { Name = "Human" };
        var other = new Empire { Name = "Other" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet {
            Location = new Coordinate(0, 0), Owner = other, Class = WorldClass.EarthLike, Type = WorldType.Base,
        };
        ground.Cargo.Trillum = 100;
        game.Galaxy.Planets.Add(ground);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Fuel = 0, Status = FleetStatus.Ready };
        fleet.Orders.Add(new FleetOrder(CommandType.Refuel));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(ground.Cargo.Trillum).IsEqualTo(100);
        await Assert.That(fleet.Fuel).IsEqualTo(0);
    }

    [Test]
    public async Task AdvanceFleet_ReachingItsDestination_RunsFleetOrdersTheSameTurn()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet {
            Owner = empire, Location = new Coordinate(9, 0), Destination = new Coordinate(10, 0),
            Fuel = 100, Status = FleetStatus.Ready,
        };
        fleet.Ships.Fighters = 1;
        // Arrives at (10,0) this turn (rate 1) -- DestCOM should immediately redirect it onward to
        // (20,0) in the same call, matching real Pascal's "ExecuteFleetOrders runs right after arrival,
        // before the player's own turn" ordering.
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(20, 0)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        // Legacy mode specifically: in the default (non-legacy) mode, arrival no longer executes
        // anything itself -- ResolveOrders' own next pass picks this fleet up instead (see
        // FleetMovementHandler's own class doc comment on why keeping both would defeat Phase 1's
        // cross-fleet deferral for exactly the case that matters most).
        var handler = new FleetMovementHandler(new FixedRandom(0), useLegacyOrderResolution: true);
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(10, 0));
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(20, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    /// <summary>
    /// Default (non-legacy) mode's actual behavior on the exact same scenario as the sibling legacy
    /// test above: arrival sets Ready/null-Destination but executes nothing itself -- the fleet just
    /// sits there, pending order intact, until a real ResolveOrders call (the production path:
    /// TurnEngine.BeginTurn/EndTurn) picks it up with the full cross-fleet treatment.
    /// </summary>
    [Test]
    public async Task AdvanceFleet_ReachingItsDestination_NonLegacyMode_ExecutesNothingUntilResolveOrders()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet {
            Owner = empire, Location = new Coordinate(9, 0), Destination = new Coordinate(10, 0),
            Fuel = 100, Status = FleetStatus.Ready,
        };
        fleet.Ships.Fighters = 1;
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(20, 0)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(10, 0));
        await Assert.That(fleet.Destination).IsNull(); // arrival alone never touched the pending order
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
        await Assert.That(fleet.NextOrder).IsEqualTo(1);

        handler.ResolveOrders(game, empire, allowWait: true);

        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(20, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }

    [Test]
    public async Task ExecuteFleetOrders_JoinCOM_OwnedPlanet_PlainClampsAndLosesOverflow()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var planet = new Planet { Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        planet.Ships.Fighters = 9995;
        game.Galaxy.Planets.Add(planet);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 10;
        fleet.Orders.Add(new FleetOrder(CommandType.Join, PreserveOverflow: false));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(planet.Ships.Fighters).IsEqualTo(PascalMath.MaxResources); // 5 lost to the clamp
        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet);
    }

    [Test]
    public async Task ExecuteFleetOrders_JoinCOM_OwnedStarbase_PlainClampsAndLosesOverflow()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var starbase = new Starbase { Location = new Coordinate(0, 0), Owner = empire, Kind = StarbaseKind.Outpost };
        starbase.Ships.Fighters = 9995;
        game.Galaxy.Starbases.Add(starbase);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 10;
        fleet.Orders.Add(new FleetOrder(CommandType.Join, PreserveOverflow: false));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(starbase.Ships.Fighters).IsEqualTo(PascalMath.MaxResources);
        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet);
    }

    [Test]
    public async Task ExecuteFleetOrders_JoinCOM_FuelConvertsToTrillumOnTheWorld()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var planet = new Planet { Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        game.Galaxy.Planets.Add(planet);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready, Fuel = FleetLogistics.FuelPerTon * 2 };
        fleet.Orders.Add(new FleetOrder(CommandType.Join, PreserveOverflow: false));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(planet.Cargo.Trillum).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteFleetOrders_JoinCOM_OwnedPlanet_PreserveOverflow_SpillsIntoANewHoldingFleet()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var planet = new Planet { Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        planet.Ships.Fighters = 9995;
        game.Galaxy.Planets.Add(planet);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 10;
        fleet.Orders.Add(new FleetOrder(CommandType.Join, PreserveOverflow: true));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(planet.Ships.Fighters).IsEqualTo(PascalMath.MaxResources); // filled to the cap, not over
        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet); // fully drained, so still destroyed

        var holding = game.Galaxy.Fleets.Single(f => f.Names.GetValueOrDefault(empire) == "Holding-1");
        await Assert.That(holding.Ships.Fighters).IsEqualTo(6); // the 6 that didn't fit -- nothing lost
    }

    [Test]
    public async Task ExecuteFleetOrders_JoinCOM_NoOwnedWorldAtDestination_CreatesHoldingOne()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 7;
        fleet.Cargo.Legions = 3;
        fleet.Orders.Add(new FleetOrder(CommandType.Join));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet);
        var holding = game.Galaxy.Fleets.Single(f => f.Names.GetValueOrDefault(empire) == "Holding-1");
        await Assert.That(holding.Ships.Fighters).IsEqualTo(7);
        await Assert.That(holding.Cargo.Legions).IsEqualTo(3);
    }

    [Test]
    public async Task ExecuteFleetOrders_JoinCOM_ExistingHoldingFleetsOutOfOrder_FillsLowestNumberFirstThenCreatesNext()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));

        var holding3 = new Fleet { Owner = empire, Location = new Coordinate(0, 0) };
        holding3.Names[empire] = "Holding-3";
        holding3.Ships.Fighters = 9997; // 2 of headroom left
        var holding5 = new Fleet { Owner = empire, Location = new Coordinate(0, 0) };
        holding5.Names[empire] = "Holding-5";
        holding5.Ships.Fighters = 9998; // 1 of headroom left
        game.Galaxy.Fleets.Add(holding3);
        game.Galaxy.Fleets.Add(holding5);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 20; // 2 into Holding-3, 1 into Holding-5, 17 into a new Holding-6
        fleet.Orders.Add(new FleetOrder(CommandType.Join));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        FleetMovementHandler.ExecuteFleetOrders(fleet, game);

        await Assert.That(holding3.Ships.Fighters).IsEqualTo(PascalMath.MaxResources);
        await Assert.That(holding5.Ships.Fighters).IsEqualTo(PascalMath.MaxResources);
        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet);
        var holding6 = game.Galaxy.Fleets.Single(f => f.Names.GetValueOrDefault(empire) == "Holding-6");
        await Assert.That(holding6.Ships.Fighters).IsEqualTo(17);
    }

    [Test]
    public async Task AdvanceFleet_ArrivingWithAJoinOrder_ExecutesJoinOnArrival()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var planet = new Planet { Location = new Coordinate(10, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        game.Galaxy.Planets.Add(planet);

        var fleet = new Fleet {
            Owner = empire, Location = new Coordinate(9, 0), Destination = new Coordinate(10, 0),
            Fuel = 100, Status = FleetStatus.Ready,
        };
        fleet.Ships.Fighters = 4;
        fleet.Orders.Add(new FleetOrder(CommandType.Join));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        // Legacy mode specifically -- see the sibling DEST-redirect test's own comment on why.
        var handler = new FleetMovementHandler(new FixedRandom(0), useLegacyOrderResolution: true);
        handler.AdvanceFleets(game, new Empire { Name = "AI" }, empire);

        await Assert.That(planet.Ships.Fighters).IsEqualTo(4);
        await Assert.That(game.Galaxy.Fleets).DoesNotContain(fleet);
    }

    /// <summary>
    /// A freshly-compiled Resupply queue on a fleet already sitting at its own source: the leading
    /// same-location DEST no longer halts (issue: it used to cost a whole extra round for nothing), so
    /// the pickup Transfer runs in the same call, and only the genuine-travel DEST to the (distant)
    /// destination actually stops the batch -- matching FleetOrderTemplates.Resupply's own real shape.
    /// </summary>
    [Test]
    public async Task ResolveOrders_SameLocationDestThenTransfer_ChainsIntoOneCallUntilGenuineTravel()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var source = new Planet { Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        source.Cargo.Metals = 200;
        game.Galaxy.Planets.Add(source);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Transports = 100;
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(0, 0))); // already here -- a no-op
        fleet.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: 200));
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(9, 9))); // genuine travel
        fleet.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: -200));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.ResolveOrders(game, empire, allowWait: true);

        await Assert.That(fleet.Cargo.Metals).IsEqualTo(200); // pickup already happened
        await Assert.That(source.Cargo.Metals).IsEqualTo(0);
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(9, 9));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        await Assert.That(fleet.NextOrder).IsEqualTo(4); // stopped at the drop-off, waiting on real travel
    }

    /// <summary>
    /// WAIT costs exactly one full turn regardless of which turn-boundary pass first reaches it: the
    /// post-turn pass (allowWait: false) leaves a WAIT completely untouched -- not even "consumed" --
    /// so only a later allowWait:true call ever advances past one. No per-fleet flag needed; it's purely
    /// which pass is running. And once a call *does* consume a WAIT, whatever comes right after it still
    /// waits for a whole separate call -- Phase 1's own cycle-to-fixed-point must not revisit this same
    /// fleet again within the call that just consumed its WAIT (found live: it did, letting a same-call
    /// DEST run right after, before this test's own fix).
    /// </summary>
    [Test]
    public async Task ResolveOrders_PostTurnPass_NeverAdvancesPastWait()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        fleet.Orders.Add(new FleetOrder(CommandType.Wait));
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(5, 5)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));

        handler.ResolveOrders(game, empire, allowWait: false);
        await Assert.That(fleet.NextOrder).IsEqualTo(1); // untouched -- still sitting at the WAIT itself
        await Assert.That(fleet.Destination).IsNull();

        handler.ResolveOrders(game, empire, allowWait: true);
        await Assert.That(fleet.NextOrder).IsEqualTo(2); // WAIT consumed, but the DEST after it does NOT run this same call
        await Assert.That(fleet.Destination).IsNull();

        handler.ResolveOrders(game, empire, allowWait: true);
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(5, 5)); // a genuinely separate call finally runs it
    }

    /// <summary>
    /// Two fleets at a full (9999-cap) planet: one dropping off metals (would clamp to 0 if it went
    /// first), one picking metals up (frees exactly enough room). Phase 1 defers the drop-off instead of
    /// executing it partially, retries after the pickup succeeds, and reaches the same "least surprising"
    /// outcome regardless of which fleet happens to be visited first -- confirmed by running both
    /// insertion orders.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ResolveOrders_DropOffDefersUntilAPickupFreesRoom_RegardlessOfVisitOrder(bool dropOffFleetAddedFirst)
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet { Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        ground.Cargo.Metals = PascalMath.MaxResources;
        game.Galaxy.Planets.Add(ground);

        var dropOff = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        dropOff.Ships.Transports = 100;
        dropOff.Cargo.Metals = 50;
        dropOff.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: -50));
        dropOff.NextOrder = 1;

        var pickup = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        pickup.Ships.Transports = 100;
        pickup.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: 30));
        pickup.NextOrder = 1;

        game.Galaxy.Fleets.Add(dropOffFleetAddedFirst ? dropOff : pickup);
        game.Galaxy.Fleets.Add(dropOffFleetAddedFirst ? pickup : dropOff);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.ResolveOrders(game, empire, allowWait: true);

        await Assert.That(ground.Cargo.Metals).IsEqualTo(PascalMath.MaxResources); // pickup's 30 backfilled by drop-off's 30
        await Assert.That(pickup.Cargo.Metals).IsEqualTo(30); // picked up in full
        await Assert.That(dropOff.Cargo.Metals).IsEqualTo(20); // delivered 30 of the 50 it wanted, kept the rest -- not destroyed
    }

    /// <summary>
    /// Refuel has no target amount to retry against (it always asks for "whatever's available"), so it
    /// can never itself defer the way Transfer does -- Phase 2 gives it the benefit anyway by running
    /// strictly after every fully-satisfiable Transfer this turn has already settled, regardless of
    /// which fleet is visited first.
    /// </summary>
    [Test]
    public async Task ResolveOrders_RefuelSeesASameTurnTrillumDropOffBeforeToppingOff()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet { Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        game.Galaxy.Planets.Add(ground);

        var tanker = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        tanker.Ships.Transports = 100;
        // Already topped off -- isolates this test from ChangeCompositionOfFleet's own auto-refuel side
        // effect (any transfer that touches a world also tops off the *acting* fleet's own fuel from its
        // trillum, FLEET.PAS-derived; a tanker starting at Fuel=0 would silently siphon some of its own
        // drop-off to fuel itself before it ever reaches the ground, confounding what this test measures).
        tanker.Fuel = FleetLogistics.FuelCapacity(tanker.Ships);
        tanker.Cargo.Trillum = 500;
        tanker.Orders.Add(new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Trillum, TransferAmount: -500));
        tanker.NextOrder = 1;

        var thirsty = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Fuel = 0, Status = FleetStatus.Ready };
        thirsty.Ships.Transports = 10;
        thirsty.Orders.Add(new FleetOrder(CommandType.Refuel));
        thirsty.NextOrder = 1;

        // Refuel's fleet added first: if Refuel ran before the drop-off it would top off against 0
        // trillum instead of 500.
        game.Galaxy.Fleets.Add(thirsty);
        game.Galaxy.Fleets.Add(tanker);

        // MaxTrillumToRefuel reads ground.Cargo.Trillum -- compute the expectation against the amount
        // it will actually hold once the drop-off lands (500), not whatever's left after refueling
        // already happened, then put the ground back to its real starting state (0) for the real run.
        ground.Cargo.Trillum = 500;
        var expectedRefuel = FleetLifecycle.MaxTrillumToRefuel(thirsty, ground);
        ground.Cargo.Trillum = 0;

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.ResolveOrders(game, empire, allowWait: true);

        await Assert.That(ground.Cargo.Trillum).IsEqualTo(500 - expectedRefuel);
        await Assert.That(thirsty.Fuel).IsGreaterThan(0);
    }

    /// <summary>
    /// CommitOrders itself must never execute anything, no matter how many times a fleet's queue is
    /// recompiled mid-turn -- resolution only ever happens at ResolveOrders' own two call sites.
    /// </summary>
    [Test]
    public async Task CommitOrders_NeverExecutesAnything()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var ground = new Planet { Location = new Coordinate(0, 0), Owner = empire, Class = WorldClass.EarthLike, Type = WorldType.Base };
        game.Galaxy.Planets.Add(ground);

        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Transports = 100;
        fleet.Cargo.Metals = 50;
        game.Galaxy.Fleets.Add(fleet);

        var orders = new List<FleetOrder> {
            new(CommandType.Destination, DestinationPosition: new Coordinate(0, 0)),
            new(CommandType.Transfer, TransferCargo: CargoType.Metals, TransferAmount: -50),
        };

        FleetMovementHandler.CommitOrders(fleet, orders, startAt: 1, game);
        FleetMovementHandler.CommitOrders(fleet, orders, startAt: 1, game); // recompiling again changes nothing either

        await Assert.That(fleet.Cargo.Metals).IsEqualTo(50);
        await Assert.That(ground.Cargo.Metals).IsEqualTo(0);
        await Assert.That(fleet.NextOrder).IsEqualTo(1);
        await Assert.That(fleet.Destination).IsNull();
    }

    /// <summary>
    /// The appsettings-driven escape hatch (Tui.TuiSettings.UseLegacyOrderResolution): with it on,
    /// ResolveOrders must be a complete no-op regardless of what a fleet has pending -- the only
    /// resolution path left is the arrival-triggered one inside AdvanceFleet (see FleetMovementHandler's
    /// own class doc comment).
    /// </summary>
    [Test]
    public async Task ResolveOrders_LegacyMode_IsANoOp()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready };
        fleet.Ships.Fighters = 1;
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(5, 5)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0), useLegacyOrderResolution: true);
        handler.ResolveOrders(game, empire, allowWait: true);

        await Assert.That(fleet.NextOrder).IsEqualTo(1);
        await Assert.That(fleet.Destination).IsNull();
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
    }

    /// <summary>
    /// Only a JumpFleet ever gets its physical step during its own owner's AdvanceFleets call (the
    /// acting-empire branch) -- a Standard or Penetrator fleet gets no such chance, ever (it only steps
    /// as some other empire's "next" fleet, grouped identically to Standard -- see
    /// <see cref="FleetMovementHandler"/>'s own ShouldAdvanceNextEmpireFleet, which lists Penetrator
    /// alongside Standard/AdvancedWarpFleet/HunterKillerFleet). TurnEngine.EndTurn must therefore call
    /// ResolveOrders before AdvanceFleets: found live (a real Resupply order on a jump fleet, sitting
    /// InTransit with cargo already loaded but stranded at its starting coordinate for a full round in an
    /// autosave) when EndTurn still called them in the other order -- the freshly-set Destination missed
    /// that round's only step opportunity. This same-call step is exclusive to JumpFleet: Standard and
    /// Penetrator instead spend this call with Destination set and Status InTransit, unmoved, waiting on
    /// the next empire's-turn boundary where they're the "next" fleet (already covered for Standard by
    /// <see cref="SequentialMovement_ActingEmpireJumpFleetAndNextEmpireWarpFleetAdvanceTogether"/>).
    /// </summary>
    [Test]
    [Arguments(FleetType.Standard)]
    [Arguments(FleetType.Penetrator)]
    [Arguments(FleetType.JumpFleet)]
    public async Task ResolveOrders_ThenAdvanceFleets_OnlyJumpFleetStepsAsActingEmpireTheSameCall(FleetType kind)
    {
        var empire = new Empire { Name = "Human" };
        var ai = new Empire { Name = "AI" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet { Owner = empire, Location = new Coordinate(0, 0), Status = FleetStatus.Ready, Fuel = 100 };
        switch (kind) {
            case FleetType.Standard: fleet.Ships.Fighters = 1; break;
            case FleetType.Penetrator: fleet.Ships.Penetrators = 1; break;
            case FleetType.JumpFleet: fleet.Ships.Jumpships = 2; break;
        }
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(10, 0)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0));
        handler.ResolveOrders(game, empire, allowWait: false);
        handler.AdvanceFleets(game, empire, ai);

        await Assert.That(fleet.Destination).IsEqualTo(kind == FleetType.JumpFleet ? null : new Coordinate(10, 0));
        if (kind == FleetType.JumpFleet) {
            await Assert.That(fleet.Location).IsEqualTo(new Coordinate(10, 0));
            await Assert.That(fleet.Status).IsEqualTo(FleetStatus.Ready);
        } else {
            await Assert.That(fleet.Location).IsEqualTo(new Coordinate(0, 0)); // order resolved, but this call's acting-empire step never touches it
            await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
        }
    }

    /// <summary>
    /// Legacy mode's arrival-triggered execution (<see cref="FleetMovementHandler.AdvanceFleet"/>'s own
    /// tail call, legacyDestHalt: true) doesn't care about fleet type -- it's ResolveOrders being a
    /// no-op in legacy mode (see <see cref="ResolveOrders_LegacyMode_IsANoOp"/>) that matters here: a
    /// JumpFleet's freshly-committed order queue only ever gets touched when AdvanceFleet next runs for
    /// it, same as in real Pascal, and TurnEngine.EndTurn's ResolveOrders/AdvanceFleets swap changes
    /// nothing for legacy mode since ResolveOrders contributes nothing to it either way.
    /// </summary>
    [Test]
    public async Task AdvanceFleet_LegacyMode_JumpFleetDestRedirectsOnArrivalSameAsStandardFleet()
    {
        var empire = new Empire { Name = "Human" };
        var game = new Game(new Galaxy(size: 20));
        var fleet = new Fleet {
            Owner = empire, Location = new Coordinate(9, 0), Destination = new Coordinate(10, 0),
            Fuel = 100, Status = FleetStatus.Ready,
        };
        fleet.Ships.Jumpships = 2;
        fleet.Orders.Add(new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(20, 0)));
        fleet.NextOrder = 1;
        game.Galaxy.Fleets.Add(fleet);

        var handler = new FleetMovementHandler(new FixedRandom(0), useLegacyOrderResolution: true);
        // JumpFleet only advances via the acting-empire branch -- empire must be acting here, unlike the
        // sibling Standard-fleet test which passes it as nextEmpire instead.
        handler.AdvanceFleets(game, empire, new Empire { Name = "AI" });

        await Assert.That(fleet.Location).IsEqualTo(new Coordinate(10, 0));
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(20, 0));
        await Assert.That(fleet.Status).IsEqualTo(FleetStatus.InTransit);
    }
}
