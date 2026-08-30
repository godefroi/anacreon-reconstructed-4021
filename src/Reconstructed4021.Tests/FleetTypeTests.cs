using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>Mirrors PRIMINTR.PAS:814-832's exact branches so a transcription mistake fails immediately.</summary>
public class FleetTypeTests
{
    private static Fleet MakeFleet() => new() { Location = new Coordinate(0, 0) };

    [Test]
    public async Task OnlyHunterKillers_IsHunterKillerFleet()
    {
        var fleet = MakeFleet();
        fleet.Ships.HunterKillers = 5;

        await Assert.That(fleet.Type).IsEqualTo(FleetType.HunterKillerFleet);
    }

    [Test]
    public async Task NoShipsAtAll_IsHunterKillerFleet()
    {
        var fleet = MakeFleet();

        await Assert.That(fleet.Type).IsEqualTo(FleetType.HunterKillerFleet);
    }

    [Test]
    public async Task OnlyJumpshipsJumptransportsAndHunterKillers_IsJumpFleet()
    {
        var fleet = MakeFleet();
        fleet.Ships.Jumpships = 2;
        fleet.Ships.Jumptransports = 3;
        fleet.Ships.HunterKillers = 1;

        await Assert.That(fleet.Type).IsEqualTo(FleetType.JumpFleet);
    }

    [Test]
    public async Task OnlyPenetratorsAndHunterKillers_IsPenetrator()
    {
        var fleet = MakeFleet();
        fleet.Ships.Penetrators = 4;
        fleet.Ships.HunterKillers = 1;

        await Assert.That(fleet.Type).IsEqualTo(FleetType.Penetrator);
    }

    [Test]
    public async Task JumpshipsAndPenetratorsMixed_IsAdvancedWarpFleet()
    {
        var fleet = MakeFleet();
        fleet.Ships.Jumpships = 2;
        fleet.Ships.Penetrators = 2;

        await Assert.That(fleet.Type).IsEqualTo(FleetType.AdvancedWarpFleet);
    }

    [Test]
    public async Task AnyFightersStarshipsOrTransportsPresent_IsStandard()
    {
        var fleet = MakeFleet();
        fleet.Ships.Fighters = 1;
        fleet.Ships.Jumpships = 3;

        await Assert.That(fleet.Type).IsEqualTo(FleetType.Standard);
    }
}
