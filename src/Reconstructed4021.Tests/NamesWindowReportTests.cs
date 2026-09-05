using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>NMSWIND.PAS: InitNamesDataArray, transcribed as <see cref="NamesWindowReport.BuildRows"/>.</summary>
public class NamesWindowReportTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    [Test]
    public async Task BuildRows_NamedFleets_ComeBeforeOtherNamedKinds()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 100);
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = viewer };
        fleet.Names[viewer] = "Raider";
        var planet = new Planet { Location = new Coordinate(1, 1), Owner = viewer };
        planet.Names[viewer] = "Homeworld";
        galaxy.Fleets.Add(fleet);
        galaxy.Planets.Add(planet);

        var rows = NamesWindowReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEquivalentTo((ISectorObject[])[fleet, planet]);
    }

    [Test]
    public async Task BuildRows_ExcludesObjectsNotNamedByViewer()
    {
        var viewer = NewEmpire("Viewer");
        var other = NewEmpire("Other");
        var galaxy = new Galaxy(size: 100);
        var namedByOther = new Planet { Location = new Coordinate(0, 0), Owner = viewer };
        namedByOther.Names[other] = "SomeoneElsesName";
        galaxy.Planets.Add(namedByOther);

        var rows = NamesWindowReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEmpty();
    }

    [Test]
    public async Task BuildRows_NoNamedObjects_ReturnsEmpty()
    {
        var viewer = NewEmpire("Viewer");
        var galaxy = new Galaxy(size: 100);
        galaxy.Planets.Add(new Planet { Location = new Coordinate(0, 0), Owner = viewer });

        var rows = NamesWindowReport.BuildRows(galaxy, viewer);

        await Assert.That(rows).IsEmpty();
    }
}
