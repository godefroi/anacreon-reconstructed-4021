using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// End-to-end check that <see cref="AnnualTickHandler.UpdateWorld"/> actually wires production
/// redirection (GitHub issue #8) into a real tick, not just that <see cref="ProductionRedirection.Apply"/>
/// works in isolation (see <see cref="ProductionRedirectionTests"/> for that). Planet parameters are
/// borrowed from <c>PascalGroundTruth.ProductionCases</c>' "FullPipelineCapitalWorld" case -- known to
/// produce all seven ship types from one developed ShipyardGeneral level -- but hardcoded here rather
/// than shared, since this test only needs "definitely produces some fighters this tick," not the exact
/// golden-file amount.
/// </summary>
public class ProductionRedirectionIntegrationTests
{
    [Test]
    public async Task RunAnnualTick_PlanetWithRedirectionConfigured_DispatchesThisTicksNewFighters()
    {
        var galaxy = new Galaxy(size: 20);
        var game = new Game(galaxy);
        var owner = new Empire { Name = "Test" };
        owner.Technology.Ships.UnionWith(Enum.GetValues<ShipType>());
        game.Empires.Add(owner);

        var planet = new Planet {
            Location = new Coordinate(0, 0), Owner = owner,
            Class = WorldClass.EarthLike, Type = WorldType.Capital,
            Population = 1000, Efficiency = 100, TechLevel = TechLevel.Gate,
            TrillumReserve = 5000,
        };
        planet.Industry.ShipyardGeneral = 100;
        planet.Cargo.Chemicals = 5000;
        planet.Cargo.Metals = 5000;
        planet.Cargo.Supplies = 5000;
        planet.Cargo.Trillum = 5000;
        planet.Redirection.Destination = new Coordinate(15, 15);
        planet.Redirection.Ships[ShipType.Fighter] = RedirectionMode.Yes;
        galaxy.Planets.Add(planet);

        new AnnualTickHandler(new FixedRandom(0)).RunAnnualTick(game);

        var fleet = galaxy.Fleets.Single();
        await Assert.That(fleet.Ships.Fighters).IsGreaterThan(0);
        await Assert.That(fleet.Destination).IsEqualTo(new Coordinate(15, 15));
        await Assert.That(fleet.Names[owner]).IsEqualTo("Redirect-1");
        await Assert.That(planet.Ships.Fighters).IsEqualTo(0); // this tick's whole delta was swept -- nothing pre-existed
    }
}
