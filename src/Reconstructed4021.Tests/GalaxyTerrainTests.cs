using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>Nebulae/minefields are sparse (dictionary-backed) — these confirm the default/round-trip behavior that sparsity depends on.</summary>
public class GalaxyTerrainTests
{
    [Test]
    public async Task UntouchedCoordinate_HasNoNebula()
    {
        var galaxy = new Galaxy(size: 100);

        await Assert.That(galaxy.GetNebula(new Coordinate(50, 50))).IsEqualTo(NebulaType.None);
    }

    [Test]
    public async Task UntouchedCoordinate_HasNoMine()
    {
        var galaxy = new Galaxy(size: 100);

        await Assert.That(galaxy.GetMineOwner(new Coordinate(50, 50))).IsNull();
    }

    [Test]
    public async Task SetNebula_RoundTrips()
    {
        var galaxy = new Galaxy(size: 100);
        var coordinate = new Coordinate(10, 10);

        galaxy.SetNebula(coordinate, NebulaType.DenseNebula);

        await Assert.That(galaxy.GetNebula(coordinate)).IsEqualTo(NebulaType.DenseNebula);
    }

    [Test]
    public async Task SettingNebulaBackToNone_ClearsIt()
    {
        var galaxy = new Galaxy(size: 100);
        var coordinate = new Coordinate(10, 10);
        galaxy.SetNebula(coordinate, NebulaType.Nebula);

        galaxy.SetNebula(coordinate, NebulaType.None);

        await Assert.That(galaxy.GetNebula(coordinate)).IsEqualTo(NebulaType.None);
    }

    [Test]
    public async Task SetAndClearMine_RoundTrips()
    {
        var galaxy = new Galaxy(size: 100);
        var coordinate = new Coordinate(10, 10);
        var empire = new Empire { Name = "Minelayer" };

        galaxy.SetMine(coordinate, empire);
        await Assert.That(galaxy.GetMineOwner(coordinate)).IsSameReferenceAs(empire);

        galaxy.ClearMine(coordinate);
        await Assert.That(galaxy.GetMineOwner(coordinate)).IsNull();
    }

    [Test]
    public async Task NebulaAndMine_AreIndependentAtTheSameCoordinate()
    {
        var galaxy = new Galaxy(size: 100);
        var coordinate = new Coordinate(10, 10);
        var empire = new Empire { Name = "Minelayer" };

        galaxy.SetNebula(coordinate, NebulaType.DarkNebula);
        galaxy.SetMine(coordinate, empire);

        await Assert.That(galaxy.GetNebula(coordinate)).IsEqualTo(NebulaType.DarkNebula);
        await Assert.That(galaxy.GetMineOwner(coordinate)).IsSameReferenceAs(empire);
    }
}
