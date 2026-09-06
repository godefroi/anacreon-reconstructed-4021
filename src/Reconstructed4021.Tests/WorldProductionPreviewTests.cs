using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// WorldProductionPreview.Compute's whole design rests on "run the real pipeline against a clone,
/// diff the result" instead of reimplementing the math a second time -- these tests verify both
/// halves of that claim: the real world is left untouched, and the clone's own result matches what
/// running AnnualTickHandler.RunProductionPipeline directly, with the same seed, would produce.
/// </summary>
public class WorldProductionPreviewTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 0);

    private static Planet NewPlanet(Empire owner) => new() {
        Location = new Coordinate(0, 0),
        Owner = owner,
        Class = WorldClass.EarthLike,
        Type = WorldType.Base,
        TechLevel = TechLevel.Bio,
        Efficiency = 80,
        Population = 500,
        Industry = { ShipyardGeneral = 100, Supply = 50 },
        Cargo = { Chemicals = 2000, Metals = 2000, Supplies = 500, Trillum = 2000 },
        TrillumReserve = 5000,
    };

    [Test]
    public async Task Compute_DoesNotMutateTheRealWorldOrItsOwner()
    {
        var owner = NewEmpire("Owner");
        var planet = NewPlanet(owner);
        var newsCountBefore = owner.News.Count;

        WorldProductionPreview.Compute(planet, new FixedRandom(0));

        await Assert.That(planet.Industry.ShipyardGeneral).IsEqualTo(100);
        await Assert.That(planet.Industry.Supply).IsEqualTo(50);
        await Assert.That(planet.Cargo.Chemicals).IsEqualTo(2000);
        await Assert.That(planet.Cargo.Metals).IsEqualTo(2000);
        await Assert.That(planet.Cargo.Trillum).IsEqualTo(2000);
        await Assert.That(planet.Ships.Fighters).IsEqualTo(0);
        await Assert.That(planet.TrillumReserve).IsEqualTo(5000);
        await Assert.That(owner.News.Count).IsEqualTo(newsCountBefore);
    }

    [Test]
    public async Task Compute_MatchesRunningTheRealPipelineDirectlyOnAnIdenticalClone()
    {
        var owner = NewEmpire("Owner");
        var planet = NewPlanet(owner);

        var result = WorldProductionPreview.Compute(planet, new FixedRandom(0));

        // A second, independently-built clone with identical starting values, run through the exact
        // same real pipeline with an identically-seeded FixedRandom -- if Compute's own internal
        // clone-and-run is faithful, these must match exactly, field for field.
        var comparisonOwner = NewEmpire("Owner");
        var comparisonPlanet = NewPlanet(comparisonOwner);
        var comparisonShortfalls = new HashSet<CargoType>();
        new AnnualTickHandler(new FixedRandom(0)).RunProductionPipeline(comparisonPlanet, comparisonShortfalls);

        await Assert.That(result.ProjectedIndustry.ShipyardGeneral).IsEqualTo(comparisonPlanet.Industry.ShipyardGeneral);
        await Assert.That(result.ProjectedIndustry.Supply).IsEqualTo(comparisonPlanet.Industry.Supply);
        await Assert.That(result.ProjectedShips.Fighters).IsEqualTo(comparisonPlanet.Ships.Fighters);
        await Assert.That(result.ProjectedCargo.Chemicals).IsEqualTo(comparisonPlanet.Cargo.Chemicals);
        await Assert.That(result.ProjectedCargo.Metals).IsEqualTo(comparisonPlanet.Cargo.Metals);
        await Assert.That(result.ProjectedCargo.Trillum).IsEqualTo(comparisonPlanet.Cargo.Trillum);
        await Assert.That(result.ProjectedTrillumReserve).IsEqualTo(comparisonPlanet.TrillumReserve);

        // And the pipeline actually did something, so this isn't a degenerate all-zero comparison.
        await Assert.That(result.ProjectedIndustry.ShipyardGeneral).IsNotEqualTo(0);

        await Assert.That(result.ShortThisTick).IsEquivalentTo(comparisonShortfalls);
    }

    [Test]
    public async Task Compute_ReportsShortfalls_WhenCargoRunsOut()
    {
        var owner = NewEmpire("Owner");
        var planet = NewPlanet(owner);
        planet.Cargo.Metals = 0; // industry growth needs Metals -- UpdateIndustry reports it short.

        var result = WorldProductionPreview.Compute(planet, new FixedRandom(0));

        await Assert.That(result.ShortThisTick).Contains(CargoType.Metals);
    }
}
