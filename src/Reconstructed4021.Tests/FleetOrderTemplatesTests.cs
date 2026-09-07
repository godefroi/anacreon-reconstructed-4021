using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

public class FleetOrderTemplatesTests
{
    [Test]
    public async Task Resupply_BuildsTheSixCommandShuttleSequence()
    {
        var owner = new Empire { Name = "Owner" };
        var source = new Planet { Location = new Coordinate(1, 1), Owner = owner, Class = WorldClass.EarthLike, Type = WorldType.Base };
        var destination = new Planet { Location = new Coordinate(9, 9), Owner = owner, Class = WorldClass.EarthLike, Type = WorldType.Base };

        var orders = FleetOrderTemplates.Resupply(source, destination, CargoType.Supplies, 200);

        await Assert.That(orders).Count().IsEqualTo(6);

        await Assert.That(orders[0].Type).IsEqualTo(CommandType.Destination);
        await Assert.That(orders[0].DestinationObject).IsEqualTo(source);

        await Assert.That(orders[1].Type).IsEqualTo(CommandType.Transfer);
        await Assert.That(orders[1].TransferCargo).IsEqualTo(CargoType.Supplies);
        await Assert.That(orders[1].TransferAmount).IsEqualTo(200); // pickup at source

        await Assert.That(orders[2].Type).IsEqualTo(CommandType.Destination);
        await Assert.That(orders[2].DestinationObject).IsEqualTo(destination);

        await Assert.That(orders[3].Type).IsEqualTo(CommandType.Transfer);
        await Assert.That(orders[3].TransferCargo).IsEqualTo(CargoType.Supplies);
        await Assert.That(orders[3].TransferAmount).IsEqualTo(-200); // drop-off at destination

        await Assert.That(orders[4].Type).IsEqualTo(CommandType.Destination);
        await Assert.That(orders[4].DestinationObject).IsEqualTo(source);

        await Assert.That(orders[5].Type).IsEqualTo(CommandType.Refuel);
    }
}
