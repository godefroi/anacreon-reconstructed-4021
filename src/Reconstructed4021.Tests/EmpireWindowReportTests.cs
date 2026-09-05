using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>EMPWIND.PAS: DrawEmpireWindow, transcribed as <see cref="EmpireWindowReport.BuildRows"/>.</summary>
public class EmpireWindowReportTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    private static Planet NewCapital(Empire owner, Coordinate location)
    {
        var planet = new Planet { Location = location, Owner = owner, Type = WorldType.Capital };
        owner.Capital = planet;
        return planet;
    }

    [Test]
    public async Task BuildRows_ViewerRow_AlwaysComesFirstAndFull()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var viewer = NewEmpire("Viewer");
        var capital = NewCapital(viewer, new Coordinate(0, 0));
        galaxy.Planets.Add(capital);
        game.Empires.Add(viewer);

        var rows = EmpireWindowReport.BuildRows(game, viewer);

        await Assert.That(rows).IsEquivalentTo([new EmpireWindowReport.Row(viewer, true)]);
    }

    [Test]
    public async Task BuildRows_OtherEmpire_ExcludedUnlessCapitalKnown()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var viewer = NewEmpire("Viewer");
        var other = NewEmpire("Other");
        galaxy.Planets.Add(NewCapital(other, new Coordinate(1, 1)));
        game.Empires.AddRange([viewer, other]);

        var rows = EmpireWindowReport.BuildRows(game, viewer);

        await Assert.That(rows).IsEquivalentTo([new EmpireWindowReport.Row(viewer, true)]);
    }

    [Test]
    public async Task BuildRows_OtherEmpire_KnownButNotScouted_IncludedButNotFull()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var viewer = NewEmpire("Viewer");
        var other = NewEmpire("Other");
        var otherCapital = NewCapital(other, new Coordinate(1, 1));
        galaxy.Planets.Add(otherCapital);
        viewer.Planets.MarkKnown(otherCapital);
        game.Empires.AddRange([viewer, other]);

        var rows = EmpireWindowReport.BuildRows(game, viewer);

        await Assert.That(rows).IsEquivalentTo([
            new EmpireWindowReport.Row(viewer, true),
            new EmpireWindowReport.Row(other, false),
        ]);
    }

    [Test]
    public async Task BuildRows_OtherEmpire_Scouted_IncludedAndFull()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var viewer = NewEmpire("Viewer");
        var other = NewEmpire("Other");
        var otherCapital = NewCapital(other, new Coordinate(1, 1));
        galaxy.Planets.Add(otherCapital);
        viewer.Planets.MarkScouted(otherCapital);
        game.Empires.AddRange([viewer, other]);

        var rows = EmpireWindowReport.BuildRows(game, viewer);

        await Assert.That(rows).IsEquivalentTo([
            new EmpireWindowReport.Row(viewer, true),
            new EmpireWindowReport.Row(other, true),
        ]);
    }

    [Test]
    public async Task BuildRows_EliminatedEmpire_Excluded()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var viewer = NewEmpire("Viewer");
        var eliminated = NewEmpire("Eliminated");
        var eliminatedCapital = NewCapital(eliminated, new Coordinate(1, 1));
        galaxy.Planets.Add(eliminatedCapital);
        viewer.Planets.MarkScouted(eliminatedCapital);
        eliminated.Status = EmpireStatus.Eliminated;
        game.Empires.AddRange([viewer, eliminated]);

        var rows = EmpireWindowReport.BuildRows(game, viewer);

        await Assert.That(rows).IsEquivalentTo([new EmpireWindowReport.Row(viewer, true)]);
    }

    [Test]
    public async Task GetEmpireStatus_SumsPlanetsPopulationAndShips()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Game(galaxy);
        var owner = NewEmpire("Owner");
        var capital = NewCapital(owner, new Coordinate(0, 0));
        capital.Population = 100;
        capital.Ships.Fighters = 5;
        var otherWorld = new Planet { Location = new Coordinate(1, 1), Owner = owner, Population = 50 };
        otherWorld.Ships.Fighters = 3;
        galaxy.Planets.AddRange([capital, otherWorld]);
        game.Empires.Add(owner);

        var (planets, totalPop, _, totalShips) = EmpireWindowReport.GetEmpireStatus(owner, game);

        await Assert.That(planets).IsEqualTo(2);
        await Assert.That(totalPop).IsEqualTo(150);
        await Assert.That(totalShips.Fighters).IsEqualTo(8);
    }
}
