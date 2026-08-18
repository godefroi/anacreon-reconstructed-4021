using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;

namespace ThreeLn.Reconstruction4021.Tests;

public class EmpireTests
{
    [Test]
    public async Task Independent_IsFlaggedIndependent()
    {
        await Assert.That(Empire.Independent.IsIndependent).IsTrue();
    }

    [Test]
    public async Task Independent_HasDefenseSettings()
    {
        await Assert.That(Empire.Independent.DefenseSettings).IsNotNull();
    }

    [Test]
    public async Task UnclaimedPlanet_OwnerIsTheSharedIndependentInstance()
    {
        var planet = new Planet { Location = new Coordinate(1, 1) };

        await Assert.That(planet.Owner).IsSameReferenceAs(Empire.Independent);
    }

    [Test]
    public async Task TwoUnclaimedPlanets_ShareTheSameIndependentInstance()
    {
        var a = new Planet { Location = new Coordinate(1, 1) };
        var b = new Planet { Location = new Coordinate(2, 2) };

        await Assert.That(a.Owner).IsSameReferenceAs(b.Owner);
    }
}
