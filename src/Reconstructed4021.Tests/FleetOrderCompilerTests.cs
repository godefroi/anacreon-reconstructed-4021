using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

public class FleetOrderCompilerTests
{
    private static (Game Game, Empire Owner) NewGame()
    {
        var owner = new Empire { Name = "Owner" };
        var game = new Game(new Galaxy(size: 20));
        game.Empires.Add(owner);
        return (game, owner);
    }

    [Test]
    public async Task Compile_Destination_ByCoordinate()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["DESTINATION 5,7"]);

        await Assert.That(result.ErrorMessage).IsNull();
        await Assert.That(result.Orders).Count().IsEqualTo(1);
        await Assert.That(result.Orders[0].Type).IsEqualTo(CommandType.Destination);
        await Assert.That(result.Orders[0].DestinationObject).IsNull();
        await Assert.That(result.Orders[0].DestinationPosition).IsEqualTo(new Coordinate(5, 7));
    }

    [Test]
    public async Task Compile_Destination_ByCoordinate_OccupiedByAnObject_ResolvesTheObjectNotTheBarePosition()
    {
        var (game, owner) = NewGame();
        var planet = new Planet { Location = new Coordinate(5, 7), Owner = owner, Class = WorldClass.EarthLike, Type = WorldType.Base };
        game.Galaxy.Planets.Add(planet);

        var result = FleetOrderCompiler.Compile(game, owner, ["DEST 5,7"]);

        await Assert.That(result.Orders[0].DestinationObject).IsEqualTo(planet);
        await Assert.That(result.Orders[0].DestinationPosition).IsNull();
    }

    [Test]
    public async Task Compile_Destination_ByName_CaseInsensitive()
    {
        // Single-word only -- real Pascal's own GetDestination reads just one token too
        // (SplitLine/ParseLine's Parm[2]), so a multi-word player-given name was never enterable here.
        var (game, owner) = NewGame();
        var starbase = new Starbase { Location = new Coordinate(3, 3), Owner = owner, Kind = StarbaseKind.Outpost };
        starbase.Names[owner] = "FortApache";
        game.Galaxy.Starbases.Add(starbase);

        var result = FleetOrderCompiler.Compile(game, owner, ["DEST fortapache"]);

        await Assert.That(result.Orders[0].DestinationObject).IsEqualTo(starbase);
    }

    [Test]
    public async Task Compile_Destination_NeitherANameNorAValidCoordinate_Errors()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["DEST nowhere"]);

        await Assert.That(result.ErrorMessage).IsEqualTo("Unknown destination in line");
        await Assert.That(result.ErrorLine).IsEqualTo(1);
    }

    [Test]
    public async Task Compile_Destination_CoordinateOutsideTheGalaxy_Errors()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["DEST 99,99"]);

        await Assert.That(result.ErrorMessage).IsEqualTo("Unknown destination in line");
    }

    [Test]
    public async Task Compile_Transfer_ShipType()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["TRANSFER 100 FGT"]);

        await Assert.That(result.Orders[0].Type).IsEqualTo(CommandType.Transfer);
        await Assert.That(result.Orders[0].TransferShip).IsEqualTo(ShipType.Fighter);
        await Assert.That(result.Orders[0].TransferCargo).IsNull();
        await Assert.That(result.Orders[0].TransferAmount).IsEqualTo(100);
    }

    [Test]
    public async Task Compile_Transfer_CargoType_NegativeAmount()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["TRAN -50 met"]);

        await Assert.That(result.Orders[0].TransferShip).IsNull();
        await Assert.That(result.Orders[0].TransferCargo).IsEqualTo(CargoType.Metals);
        await Assert.That(result.Orders[0].TransferAmount).IsEqualTo(-50);
    }

    [Test]
    public async Task Compile_Transfer_UnknownResource_Errors()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["TRAN 100 xyz"]);

        await Assert.That(result.ErrorMessage).IsEqualTo("Unknown resource in line");
    }

    [Test]
    public async Task Compile_Transfer_BadAmount_Errors()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["TRAN abc met"]);

        await Assert.That(result.ErrorMessage).IsEqualTo("Bad transfer value in line");
    }

    [Test]
    public async Task Compile_Transfer_BothResourceAndAmountBad_ReportsBadTransferValue()
    {
        // ORDERS.PAS:206-208 -- GetTransfer runs after GetResourceType and unconditionally overwrites
        // Error on its own failure, so this ordering is real Pascal behavior, not arbitrary.
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["TRAN abc xyz"]);

        await Assert.That(result.ErrorMessage).IsEqualTo("Bad transfer value in line");
    }

    [Test]
    public async Task Compile_Repeat_And_Wait_NeedNoOperand()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["REPEAT", "WAIT"]);

        await Assert.That(result.Orders[0].Type).IsEqualTo(CommandType.Repeat);
        await Assert.That(result.Orders[1].Type).IsEqualTo(CommandType.Wait);
    }

    [Test]
    public async Task Compile_BlankLine_SilentlySkipped()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["WAIT", "", "   ", "REPEAT"]);

        await Assert.That(result.ErrorMessage).IsNull();
        await Assert.That(result.Orders).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Compile_UnknownCommand_Errors()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["FOOBAR"]);

        await Assert.That(result.ErrorMessage).IsEqualTo("Unknown command in line");
    }

    [Test]
    public async Task Compile_ErrorLine_IsOneBasedAndPointsAtTheFailingLine()
    {
        var (game, owner) = NewGame();

        var result = FleetOrderCompiler.Compile(game, owner, ["WAIT", "REPEAT", "BOGUS"]);

        await Assert.That(result.ErrorLine).IsEqualTo(3);
        await Assert.That(result.Orders).IsEmpty();
    }

    [Test]
    public async Task Decompile_Then_Compile_RoundTrips_EveryCommandType()
    {
        var (game, owner) = NewGame();
        var target = new Planet { Location = new Coordinate(6, 6), Owner = owner, Class = WorldClass.EarthLike, Type = WorldType.Base };
        game.Galaxy.Planets.Add(target);

        IReadOnlyList<FleetOrder> original = [
            new FleetOrder(CommandType.Destination, DestinationObject: target),
            new FleetOrder(CommandType.Destination, DestinationPosition: new Coordinate(2, 2)),
            new FleetOrder(CommandType.Transfer, TransferShip: ShipType.Jumpship, TransferAmount: 40),
            new FleetOrder(CommandType.Transfer, TransferCargo: CargoType.Trillum, TransferAmount: -15),
            new FleetOrder(CommandType.Wait),
            new FleetOrder(CommandType.Repeat),
        ];

        var lines = FleetOrderCompiler.Decompile(owner, original);
        var recompiled = FleetOrderCompiler.Compile(game, owner, lines);

        await Assert.That(recompiled.ErrorMessage).IsNull();
        await Assert.That(recompiled.Orders).Count().IsEqualTo(original.Count);
        await Assert.That(recompiled.Orders[0].DestinationObject).IsEqualTo(target);
        await Assert.That(recompiled.Orders[1].DestinationPosition).IsEqualTo(new Coordinate(2, 2));
        await Assert.That(recompiled.Orders[2].TransferShip).IsEqualTo(ShipType.Jumpship);
        await Assert.That(recompiled.Orders[2].TransferAmount).IsEqualTo(40);
        await Assert.That(recompiled.Orders[3].TransferCargo).IsEqualTo(CargoType.Trillum);
        await Assert.That(recompiled.Orders[3].TransferAmount).IsEqualTo(-15);
        await Assert.That(recompiled.Orders[4].Type).IsEqualTo(CommandType.Wait);
        await Assert.That(recompiled.Orders[5].Type).IsEqualTo(CommandType.Repeat);
    }

    [Test]
    public async Task Decompile_NamedDestination_UsesTheViewersOwnName()
    {
        var owner = new Empire { Name = "Owner" };
        var target = new Starbase { Location = new Coordinate(6, 6), Owner = owner, Kind = StarbaseKind.Outpost };
        target.Names[owner] = "Rally Point";

        var lines = FleetOrderCompiler.Decompile(owner, [new FleetOrder(CommandType.Destination, DestinationObject: target)]);

        await Assert.That(lines[0]).IsEqualTo("DESTination Rally Point");
    }

    [Test]
    public async Task Decompile_UnnamedDestination_FallsBackToItsPlainCoordinate()
    {
        var owner = new Empire { Name = "Owner" };
        var target = new Starbase { Location = new Coordinate(9, 4), Owner = owner, Kind = StarbaseKind.Outpost };

        var lines = FleetOrderCompiler.Decompile(owner, [new FleetOrder(CommandType.Destination, DestinationObject: target)]);

        await Assert.That(lines[0]).IsEqualTo("DESTination 9,4");
    }
}
