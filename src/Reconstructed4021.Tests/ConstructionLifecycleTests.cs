using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Entities.ConstructionLifecycle"/>/<see cref="Core.Entities.ConstructionCatalog"/> --
/// no golden-file domain exists for construction-site creation, same rationale as
/// <see cref="FleetLifecycleTests"/>.
/// </summary>
public class ConstructionLifecycleTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    [Test]
    public async Task StartConstruction_AddsSiteToGalaxyWithCorrectYearsToCompletion()
    {
        var owner = NewEmpire("Owner");
        var game = new Game(new Galaxy(size: 10));

        var site = ConstructionLifecycle.StartConstruction(game, owner, ConstructionType.Outpost, new Coordinate(3, 4));

        await Assert.That(game.Galaxy.ConstructionSites).Contains(site);
        await Assert.That(site.Location).IsEqualTo(new Coordinate(3, 4));
        await Assert.That(site.Owner).IsEqualTo(owner);
        await Assert.That(site.Building).IsEqualTo(ConstructionType.Outpost);
        await Assert.That(site.YearsToCompletion).IsEqualTo(3); // DATACNST.PAS:528-536
    }

    [Test]
    public async Task StartConstruction_OwnerSeesTheNewSiteWithNoExplicitVisibilityMark()
    {
        // Game.Visible/ScoutedOrOwned already fall back to plain ownership (Game.cs:211-260) -- the
        // same reason a freshly created Starbase/Stargate needs no MarkKnown/MarkScouted call either.
        var owner = NewEmpire("Owner");
        var game = new Game(new Galaxy(size: 10));

        var site = ConstructionLifecycle.StartConstruction(game, owner, ConstructionType.Outpost, new Coordinate(3, 4));

        await Assert.That(Game.Visible(owner, site)).IsTrue();
        await Assert.That(Game.ScoutedOrOwned(owner, site)).IsTrue();
    }

    [Test]
    [Arguments(ConstructionType.Minefield, 2)]
    [Arguments(ConstructionType.CommandBase, 6)]
    [Arguments(ConstructionType.Fortress, 12)]
    [Arguments(ConstructionType.IndustrialComplex, 10)]
    [Arguments(ConstructionType.Outpost, 3)]
    [Arguments(ConstructionType.Gate, 15)]
    [Arguments(ConstructionType.WarpLink, 5)]
    [Arguments(ConstructionType.Disrupter, 8)]
    public async Task YearsToBuild_MatchesDatacnstDirectly(ConstructionType type, int years)
    {
        // Checked against DATACNST.PAS:528-536 directly, not against this port's own prior copy --
        // this is exactly the table a transcription slip would hide from a self-referential test.
        await Assert.That(ConstructionCatalog.YearsToBuild[type]).IsEqualTo(years);
    }

    [Test]
    public async Task RawMaterialPerYear_Outpost_MatchesDatacnstDirectly()
    {
        // DATACNST.PAS:539-548's own "out" row: che=350, met=1120, sup=0 (omitted here, sup is never
        // drawn on by construction), tri=150.
        var row = ConstructionCatalog.RawMaterialPerYear[ConstructionType.Outpost];

        await Assert.That(row[CargoType.Chemicals]).IsEqualTo(350);
        await Assert.That(row[CargoType.Metals]).IsEqualTo(1120);
        await Assert.That(row[CargoType.Trillum]).IsEqualTo(150);
    }
}
