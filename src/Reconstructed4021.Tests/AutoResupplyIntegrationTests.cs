using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// End-to-end check that <see cref="AnnualTickHandler.RunAnnualTick"/> actually wires auto-resupply
/// (GitHub issue #85) into a real tick -- not just that <see cref="AutoResupply.Apply"/> works in
/// isolation (see <see cref="AutoResupplyTests"/> for that). A starving Agricultural world gets fed
/// by a Capital source with Resupply enabled, driven by the real starvation signal (UseUpFood), not a
/// hand-set ShortfallsLastTick.
/// </summary>
public class AutoResupplyIntegrationTests
{
    [Test]
    public async Task RunAnnualTick_StarvingDestination_GetsAutoFedByCapitalSource()
    {
        var galaxy = new Galaxy(size: 20);
        var game = new Game(galaxy);
        var owner = new Empire { Name = "Test" };
        game.Empires.Add(owner);

        var capital = new Planet {
            Location = new Coordinate(0, 0), Owner = owner,
            Class = WorldClass.ClassM, Type = WorldType.Capital,
            TechLevel = TechLevel.Warp, Efficiency = 100,
        };
        capital.Cargo.Supplies = 10_000;
        capital.Resupply.Enabled = true;
        galaxy.Planets.Add(capital);

        // Mirrors AnnualTickHandlerTests.StarvationReducesPopulationAndRaisesRevolutionIndex's own
        // known-good setup (Efficiency left at its 0 default) -- that test's own hand-derivation
        // confirms starvation fires reliably for exactly this shape.
        var starving = new Planet {
            Location = new Coordinate(1, 0), Owner = owner,
            Class = WorldClass.ClassM, Type = WorldType.Agricultural,
            Population = 1000, TechLevel = TechLevel.Warp,
        };
        starving.Cargo.Supplies = 0; // guarantees UseUpFood starves this tick
        galaxy.Planets.Add(starving);

        var idleFleet = new Fleet {
            Location = capital.Location, Owner = owner, Status = FleetStatus.Ready, Fuel = 10_000,
            Ships = { Transports = 50 },
        };
        galaxy.Fleets.Add(idleFleet);

        new AnnualTickHandler(new FixedRandom(0)).RunAnnualTick(game);

        await Assert.That(starving.ShortfallsLastTick).Contains(CargoType.Supplies);
        await Assert.That(idleFleet.NextOrder).IsEqualTo(1);
        await Assert.That(idleFleet.Orders).Count().IsEqualTo(5); // FleetOrderTemplates.Resupply's own DEST/TRAN/DEST/TRAN/DEST shape -- no trailing Refuel
        await Assert.That(idleFleet.Orders[0].Type).IsEqualTo(CommandType.Destination);
        await Assert.That(idleFleet.Orders[0].DestinationObject).IsEqualTo(capital);
        await Assert.That(idleFleet.Orders[1].TransferCargo).IsEqualTo(CargoType.Supplies);
        await Assert.That(idleFleet.Orders[1].TransferAmount).IsGreaterThan(0);
        await Assert.That(idleFleet.Orders[2].DestinationObject).IsEqualTo(starving);
        await Assert.That(idleFleet.Orders[4].Type).IsEqualTo(CommandType.Destination);
        await Assert.That(idleFleet.Orders[4].DestinationObject).IsEqualTo(capital);
    }
}
