using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>FLTWIND.PAS: InitializeFleetDataArray, transcribed as <see cref="FleetStatusReport.BuildRows"/>.</summary>
public class FleetStatusReportTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    private static Fleet NewFleet(Empire owner, Coordinate location) => new() { Location = location, Owner = owner };

    [Test]
    public async Task BuildRows_OwnFleets_SortedByLocationYThenXDescending()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 100);
        var lowY = NewFleet(viewer, new Coordinate(5, 1));
        var highY = NewFleet(viewer, new Coordinate(2, 9));
        var sameYHigherX = NewFleet(viewer, new Coordinate(8, 9));
        galaxy.Fleets.AddRange([lowY, highY, sameYHigherX]);

        var rows = FleetStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEquivalentTo((ISectorObject[])[sameYHigherX, highY, lowY]);
    }

    [Test]
    public async Task BuildRows_OwnFleets_ComeBeforeOwnCommandBaseStarbases()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 100);
        var fleet = NewFleet(viewer, new Coordinate(0, 0));
        var starbase = new Starbase { Location = new Coordinate(0, 0), Owner = viewer, Kind = StarbaseKind.CommandBase };
        galaxy.Fleets.Add(fleet);
        galaxy.Starbases.Add(starbase);

        var rows = FleetStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEquivalentTo((ISectorObject[])[fleet, starbase]);
    }

    [Test]
    public async Task BuildRows_OwnStarbase_ExcludedUnlessCommandBaseOrFortress()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 100);
        var outpost = new Starbase { Location = new Coordinate(0, 0), Owner = viewer, Kind = StarbaseKind.Outpost };
        galaxy.Starbases.Add(outpost);

        var rows = FleetStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEmpty();
    }

    [Test]
    public async Task BuildRows_ScoutedEnemyFleets_ComeAfterOwnRowsButBeforeKnownOnlyFleets()
    {
        var viewer = NewEmpire("Viewer");
        var enemy = NewEmpire("Enemy");
        var galaxy = new Galaxy(size: 100);
        var ownFleet = NewFleet(viewer, new Coordinate(0, 0));
        var scoutedEnemyFleet = NewFleet(enemy, new Coordinate(1, 1));
        var knownOnlyEnemyFleet = NewFleet(enemy, new Coordinate(2, 2));
        viewer.Fleets.MarkScouted(scoutedEnemyFleet);
        viewer.Fleets.MarkKnown(knownOnlyEnemyFleet);
        galaxy.Fleets.AddRange([ownFleet, scoutedEnemyFleet, knownOnlyEnemyFleet]);

        var rows = FleetStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEquivalentTo((ISectorObject[])[ownFleet, scoutedEnemyFleet, knownOnlyEnemyFleet]);
    }

    [Test]
    public async Task BuildRows_EnemyFleet_ExcludedUnlessAtLeastKnown()
    {
        var viewer = NewEmpire("Viewer");
        var enemy = NewEmpire("Enemy");
        var galaxy = new Galaxy(size: 100);
        var unseenFleet = NewFleet(enemy, new Coordinate(0, 0));
        galaxy.Fleets.Add(unseenFleet);

        var rows = FleetStatusReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEmpty();
    }
}
