using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

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

    [Test]
    public async Task TryLaunchProbe_UnderCapacity_SucceedsAndRecordsDestination()
    {
        var empire = new Empire { Name = "Human" };
        var destination = new Coordinate(5, 5);

        var launched = empire.TryLaunchProbe(destination);

        await Assert.That(launched).IsTrue();
        await Assert.That(empire.ProbesInTransit).Contains(destination);
    }

    [Test]
    public async Task TryLaunchProbe_AtCapacity_Fails()
    {
        var empire = new Empire { Name = "Human" };
        for (var i = 0; i < Empire.MaxProbesInTransit; i++) {
            await Assert.That(empire.TryLaunchProbe(new Coordinate(i, i))).IsTrue();
        }

        var launched = empire.TryLaunchProbe(new Coordinate(0, 1));

        await Assert.That(launched).IsFalse();
        await Assert.That(empire.ProbesInTransit.Count).IsEqualTo(Empire.MaxProbesInTransit);
    }

    [Test]
    public async Task AddNews_OwnedEmpire_RecordsTheItem()
    {
        var empire = new Empire { Name = "Human" };

        empire.AddNews(NewsType.PeopleStarving, p1: 5);

        await Assert.That(empire.News).Count().IsEqualTo(1);
        await Assert.That(empire.News[0].Headline).IsEqualTo(NewsType.PeopleStarving);
        await Assert.That(empire.News[0].Parm1).IsEqualTo(5);
    }

    [Test]
    public async Task AddNews_Independent_IsANoOp()
    {
        Empire.Independent.AddNews(NewsType.PeopleStarving);

        await Assert.That(Empire.Independent.News).IsEmpty();
    }
}
