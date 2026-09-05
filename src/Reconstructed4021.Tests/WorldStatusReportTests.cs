using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>STAWIND.PAS: InitializeStatusDataArray, transcribed as <see cref="WorldStatusReport.BuildRows"/>.</summary>
public class WorldStatusReportTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    private static Planet NewPlanet(Empire owner, TechLevel tech, int population) => new() {
        Location = new Coordinate(0, 0),
        Owner = owner,
        Type = WorldType.Agricultural,
        TechLevel = tech,
        Population = population,
    };

    [Test]
    public async Task BuildRows_CapitalAlwaysFirst_EvenIfNotHighestTechOrPopulation()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 10);
        var capital = NewPlanet(viewer, TechLevel.PreTech, population: 1);
        var higherWorld = NewPlanet(viewer, TechLevel.Gate, population: 1000);
        viewer.Capital = capital;
        galaxy.Planets.Add(capital);
        galaxy.Planets.Add(higherWorld);

        var rows = WorldStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows[0]).IsSameReferenceAs(capital);
        await Assert.That(rows[1]).IsSameReferenceAs(higherWorld);
    }

    [Test]
    public async Task BuildRows_OwnWorlds_SortedByTechLevelThenPopulationDescending()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 10);
        var lowTechHighPop = NewPlanet(viewer, TechLevel.Atomic, population: 10000);
        var highTechLowPop = NewPlanet(viewer, TechLevel.Gate, population: 1);
        var midTech = NewPlanet(viewer, TechLevel.Warp, population: 500);
        galaxy.Planets.AddRange([lowTechHighPop, highTechLowPop, midTech]);

        var rows = WorldStatusReport.BuildRows(galaxy, viewer);

        // No capital set -- tech level dominates the sort regardless of the much larger population gap.
        await Assert.That(rows).IsEquivalentTo((IEconomicWorld[])[highTechLowPop, midTech, lowTechHighPop]);
    }

    [Test]
    public async Task BuildRows_OwnWorlds_ComeBeforeForeignScoutedWorlds()
    {
        var viewer = NewEmpire("Viewer");
        var enemy = NewEmpire("Enemy");
        var galaxy = new Galaxy(size: 10);
        var ownWorld = NewPlanet(viewer, TechLevel.PreTech, population: 1);
        var foreignWorld = NewPlanet(enemy, TechLevel.Gate, population: 100000);
        viewer.Planets.MarkScouted(foreignWorld);
        galaxy.Planets.AddRange([ownWorld, foreignWorld]);

        var rows = WorldStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEquivalentTo((IEconomicWorld[])[ownWorld, foreignWorld]);
    }

    [Test]
    public async Task BuildRows_ForeignWorld_ExcludedUnlessScouted()
    {
        var viewer = NewEmpire("Viewer");
        var enemy = NewEmpire("Enemy");
        var galaxy = new Galaxy(size: 10);
        var unscoutedForeignWorld = NewPlanet(enemy, TechLevel.Gate, population: 100000);
        galaxy.Planets.Add(unscoutedForeignWorld);

        var rows = WorldStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEmpty();
    }

    [Test]
    public async Task BuildRows_IncludesStarbasesAlongsidePlanets()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 10);
        var starbase = new Starbase {
            Location = new Coordinate(0, 0),
            Owner = viewer,
            Type = WorldType.BaseStarbase,
            TechLevel = TechLevel.Gate,
        };
        galaxy.Starbases.Add(starbase);

        var rows = WorldStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEquivalentTo((IEconomicWorld[])[starbase]);
    }
}
