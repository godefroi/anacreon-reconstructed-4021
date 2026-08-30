using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

public class EnumTests
{
    [Test]
    public async Task ShipType_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<ShipType>()).IsEquivalentTo(
        [
            ShipType.Fighter, ShipType.HunterKiller, ShipType.Jumpship, ShipType.Jumptransport,
            ShipType.Penetrator, ShipType.Starship, ShipType.Transport,
        ]);
    }

    [Test]
    public async Task CargoType_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<CargoType>()).IsEquivalentTo(
        [
            CargoType.Legion, CargoType.NinjaLegion, CargoType.Ambrosia, CargoType.Chemicals,
            CargoType.Metals, CargoType.Supplies, CargoType.Trillum,
        ]);
    }

    [Test]
    public async Task DefenseType_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<DefenseType>()).IsEquivalentTo(
        [
            DefenseType.Lam, DefenseType.DefenseSatellite, DefenseType.Gdm, DefenseType.IonCannon,
        ]);
    }

    [Test]
    public async Task IndustryType_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<IndustryType>()).IsEquivalentTo(
        [
            IndustryType.Bioindustry, IndustryType.Chemical, IndustryType.Mining,
            IndustryType.ShipyardGeneral, IndustryType.ShipyardJump, IndustryType.ShipyardStarship,
            IndustryType.ShipyardTransport, IndustryType.Supply, IndustryType.TrillumMining,
        ]);
    }

    [Test]
    public async Task ConstructionType_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<ConstructionType>()).IsEquivalentTo(
        [
            ConstructionType.Minefield, ConstructionType.CommandBase, ConstructionType.Fortress,
            ConstructionType.IndustrialComplex, ConstructionType.Outpost, ConstructionType.Gate,
            ConstructionType.WarpLink, ConstructionType.Disrupter,
        ]);
    }

    [Test]
    public async Task StarbaseKind_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<StarbaseKind>()).IsEquivalentTo(
        [
            StarbaseKind.CommandBase, StarbaseKind.Fortress, StarbaseKind.IndustrialComplex,
            StarbaseKind.Outpost,
        ]);
    }

    [Test]
    public async Task StargateKind_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<StargateKind>()).IsEquivalentTo(
        [
            StargateKind.Gate, StargateKind.WarpLink, StargateKind.Disrupter,
        ]);
    }

    [Test]
    public async Task WorldClass_HasAllTwentyOneMembers()
    {
        await Assert.That(Enum.GetValues<WorldClass>()).Count().IsEqualTo(21);
    }

    [Test]
    public async Task WorldType_HasAllTwentyOneMembers()
    {
        await Assert.That(Enum.GetValues<WorldType>()).Count().IsEqualTo(21);
    }

    [Test]
    public async Task AttackType_MatchesPascalOrder()
    {
        await Assert.That(Enum.GetValues<AttackType>()).IsEquivalentTo(
        [
            AttackType.Lam, AttackType.DefenseSatellite, AttackType.Gdm, AttackType.IonCannon,
            AttackType.Fighter, AttackType.HunterKiller, AttackType.Jumpship, AttackType.Jumptransport,
            AttackType.Penetrator, AttackType.Starship, AttackType.Transport,
            AttackType.Legion, AttackType.NinjaLegion,
        ]);
    }
}
