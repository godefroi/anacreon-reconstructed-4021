using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;

namespace Reconstructed4021.Tests;

public class GalaxySmokeTests
{
    [Test]
    public async Task CanConstructAGalaxyWithOwnedPlanetsAndFleets()
    {
        var galaxy = new Galaxy(size: 20);
        var empire = new Empire { Name = "Terra" };

        var homeworld = new Planet { Location = new Coordinate(5, 5), Owner = empire };
        var fleet = new Fleet { Location = new Coordinate(5, 5), Owner = empire };
        fleet.Ships.Transports = 3;

        galaxy.Planets.Add(homeworld);
        galaxy.Fleets.Add(fleet);
        empire.Capital = homeworld;

        await Assert.That(galaxy.Planets).Contains(homeworld);
        await Assert.That(galaxy.Fleets).Contains(fleet);
        await Assert.That(homeworld.Owner).IsSameReferenceAs(empire);
        await Assert.That(fleet.Owner).IsSameReferenceAs(empire);
        await Assert.That(empire.Capital).IsSameReferenceAs(homeworld);
    }

    [Test]
    public async Task CoordinateDistance_IsChebyshevNotEuclidean()
    {
        var a = new Coordinate(0, 0);
        var b = new Coordinate(3, 1);

        // Chebyshev: max(|3|,|1|) = 3. Euclidean would be sqrt(10) ~= 3.16.
        await Assert.That(a.DistanceTo(b)).IsEqualTo(3);
    }
}
