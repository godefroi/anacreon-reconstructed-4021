using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>PROLOG.PAS's EmpireStatus (:490-615) aggregate totals -- see EmpireStatusReport's own doc comment.</summary>
public class EmpireStatusReportTests
{
    [Test]
    public async Task For_SumsWorldsAndFleetsButOnlyForTheGivenEmpire()
    {
        var galaxy = new Galaxy(10);
        var empire = EmpireFactory.CreateEmpire("Human", null, isEmpress: false, TechLevel.Atomic, restlessness: 0, centralModifier: false, foundingYear: 4000);
        var other = EmpireFactory.CreateEmpire("Other", null, isEmpress: false, TechLevel.Atomic, restlessness: 0, centralModifier: false, foundingYear: 4000);

        var planet = new Planet { Location = new Coordinate(0, 0), Owner = empire, Population = 100, Efficiency = 80, TechLevel = TechLevel.Atomic };
        planet.Ships.Fighters = 10;
        galaxy.Planets.Add(planet);

        var starbase = new Starbase { Location = new Coordinate(1, 1), Owner = empire, Population = 50, Efficiency = 40, TechLevel = TechLevel.Atomic };
        starbase.Ships.Fighters = 5;
        galaxy.Starbases.Add(starbase);

        var ownFleet = new Fleet { Location = new Coordinate(0, 0), Owner = empire };
        ownFleet.Ships.HunterKillers = 3;
        galaxy.Fleets.Add(ownFleet);

        var otherFleet = new Fleet { Location = new Coordinate(0, 0), Owner = other };
        otherFleet.Ships.Fighters = 999; // must not be counted -- not this empire's.
        galaxy.Fleets.Add(otherFleet);

        var game = new Game(galaxy);
        game.Empires.Add(empire);
        game.Empires.Add(other);

        var report = EmpireStatusReport.For(empire, game);

        await Assert.That(report.TotalWorlds).IsEqualTo(2);
        await Assert.That(report.TotalPopulation).IsEqualTo(150);
        await Assert.That(report.AverageEfficiency).IsEqualTo(60); // (80+40)/2
        await Assert.That(report.TotalShips.Fighters).IsEqualTo(15); // planet 10 + starbase 5, otherFleet excluded
        await Assert.That(report.TotalShips.HunterKillers).IsEqualTo(3); // ownFleet only
    }

    [Test]
    public async Task For_NoOwnedWorldsGivesZeroAverages()
    {
        var galaxy = new Galaxy(10);
        var empire = EmpireFactory.CreateEmpire("Human", null, isEmpress: false, TechLevel.Atomic, restlessness: 0, centralModifier: false, foundingYear: 4000);
        var game = new Game(galaxy);
        game.Empires.Add(empire);

        var report = EmpireStatusReport.For(empire, game);

        await Assert.That(report.TotalWorlds).IsEqualTo(0);
        await Assert.That(report.AverageEfficiency).IsEqualTo(0);
        await Assert.That(report.AverageIndustry).IsEqualTo(0);
    }

    [Test]
    public async Task For_ExtraTechnologiesAreOnlyThoseBeyondTheEmpiresOwnLevel()
    {
        var galaxy = new Galaxy(10);
        // EmpireFactory.SeedTechnology's own real baseline (NEWGAME.PAS:1203,1207) is TechDev[Pred(Tech)]
        // -- one level *behind* the empire's own declared TechLevel -- with "extra tech" grants the
        // only way to reach anything genuinely at that declared level itself (and clamped to it: a
        // grant whose own MinTech sits beyond Tech never survives EmpireFactory's final intersect).
        // HunterKiller's MinTech is exactly Bio, so granting it to a Bio-level empire is exactly the
        // "mastered beyond the automatic Pred(Tech) baseline" case EmpireStatus reports on.
        var empire = EmpireFactory.CreateEmpire("Human", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 4000, TechCatalog.Grant(ShipType.HunterKiller));
        var game = new Game(galaxy);
        game.Empires.Add(empire);

        var report = EmpireStatusReport.For(empire, game);

        await Assert.That(report.ExtraTechnologies).Contains(new TechCatalog.TechGrantIdentity(TechCategory.Ship, (int)ShipType.HunterKiller));
        await Assert.That(TechCatalog.DisplayName(report.ExtraTechnologies[0])).IsEqualTo("hunter-killer");
    }
}
