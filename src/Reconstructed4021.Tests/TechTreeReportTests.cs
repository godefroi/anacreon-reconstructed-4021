using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>Empire menu's Tech Tree screen -- <see cref="TechTreeReport.BuildRows"/>.</summary>
public class TechTreeReportTests
{
    private static Empire NewEmpire(TechLevel tech) =>
        EmpireFactory.CreateEmpire("Viewer", null, isEmpress: false, tech, restlessness: 0, centralModifier: false, foundingYear: 0);

    [Test]
    public async Task BuildRows_OrderedByTechLevelAscending()
    {
        var viewer = NewEmpire(TechLevel.Bio);

        var rows = TechTreeReport.BuildRows(viewer);

        await Assert.That(rows.Select(r => r.Level)).IsInOrder();
    }

    [Test]
    public async Task BuildRows_EverythingBelowOwnTechLevel_IsOwned()
    {
        // A freshly-created empire's own seeding (EmpireFactory.SeedTechnology) only ever grants
        // TechDev[Pred(Tech)] -- the *previous* level's full set -- so items at the empire's own
        // current level aren't automatically owned; only strictly-below ones are guaranteed.
        var viewer = NewEmpire(TechLevel.Bio);

        var rows = TechTreeReport.BuildRows(viewer);

        await Assert.That(rows.Where(r => r.Level < TechLevel.Bio).All(r => r.Owned)).IsTrue();
    }

    [Test]
    public async Task BuildRows_AboveOwnTechLevel_NotOwnedByDefault()
    {
        var viewer = NewEmpire(TechLevel.PreTech);

        var rows = TechTreeReport.BuildRows(viewer);

        await Assert.That(rows.Where(r => r.Level > TechLevel.PreTech).Any(r => r.Owned)).IsFalse();
    }

    [Test]
    public async Task BuildRows_IncludesEveryCatalogEntryExactlyOnce()
    {
        var viewer = NewEmpire(TechLevel.Gate);

        var rows = TechTreeReport.BuildRows(viewer);

        await Assert.That(rows.Select(r => r.Identity).Distinct().Count()).IsEqualTo(rows.Count);
    }
}
