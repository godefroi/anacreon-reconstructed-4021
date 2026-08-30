using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;

namespace Reconstructed4021.Tests;

/// <summary>
/// Asserts the Scouted-subset-of-Known invariant confirmed against PRIMINTR.PAS's ScoutObject
/// (sets both together) and INTRFACE.PAS's DetermineIfScouted (Known can precede Scouted).
/// </summary>
public class EntityVisibilityTests
{
    [Test]
    public async Task MarkScouted_AddsToBothKnownAndScouted()
    {
        var visibility = new EntityVisibility<Planet>();
        var planet = new Planet { Location = new Coordinate(1, 1) };

        visibility.MarkScouted(planet);

        await Assert.That(visibility.Known).Contains(planet);
        await Assert.That(visibility.Scouted).Contains(planet);
    }

    [Test]
    public async Task MarkKnown_AddsToKnownOnly()
    {
        var visibility = new EntityVisibility<Planet>();
        var planet = new Planet { Location = new Coordinate(1, 1) };

        visibility.MarkKnown(planet);

        await Assert.That(visibility.Known).Contains(planet);
        await Assert.That(visibility.Scouted).IsEmpty();
    }

    [Test]
    public async Task DifferentEmpires_HaveIndependentVisibility()
    {
        var planet = new Planet { Location = new Coordinate(1, 1) };
        var empireA = new Empire { Name = "A" };
        var empireB = new Empire { Name = "B" };

        empireA.Planets.MarkScouted(planet);

        await Assert.That(empireA.Planets.Scouted).Contains(planet);
        await Assert.That(empireB.Planets.Scouted).IsEmpty();
    }

    [Test]
    public async Task Clear_RemovesBothSets()
    {
        var visibility = new EntityVisibility<Planet>();
        var planet = new Planet { Location = new Coordinate(1, 1) };
        visibility.MarkScouted(planet);

        visibility.Clear();

        await Assert.That(visibility.Known).IsEmpty();
        await Assert.That(visibility.Scouted).IsEmpty();
    }
}
