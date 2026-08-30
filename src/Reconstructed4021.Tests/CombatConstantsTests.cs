using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// Shape/lookup sanity for <see cref="CombatConstants"/>'s own tables — these confirm every table is
/// fully populated over its declared key space (a missing entry would silently look like 0 at a real
/// call site) and that a few known values transcribed from source read back correctly.
/// </summary>
public class CombatConstantsTests
{
    [Test]
    public async Task CombatTable_HasEveryAttackerDefenderPair()
    {
        var attackTypes = Enum.GetValues<AttackType>();
        foreach (var attacker in attackTypes)
            foreach (var defender in attackTypes)
                await Assert.That(CombatConstants.CombatTable).ContainsKey((attacker, defender));
    }

    [Test]
    public async Task CombatTable_KnownValuesMatchSource()
    {
        // LAM vs. fgt (DATACNST.PAS:181): 250. nnj vs. men (DATACNST.PAS:193): 20.
        await Assert.That(CombatConstants.CombatTable[(AttackType.Lam, AttackType.Fighter)]).IsEqualTo(250);
        await Assert.That(CombatConstants.CombatTable[(AttackType.NinjaLegion, AttackType.Legion)]).IsEqualTo(20);
        await Assert.That(CombatConstants.CombatTable[(AttackType.Transport, AttackType.Lam)]).IsEqualTo(0);
    }

    [Test]
    public async Task WeapEff_ShipValue_CombatPower_HaveEveryAttackType()
    {
        foreach (var type in Enum.GetValues<AttackType>()) {
            await Assert.That(CombatConstants.WeapEff).ContainsKey(type);
            await Assert.That(CombatConstants.ShipValue).ContainsKey(type);
            await Assert.That(CombatConstants.CombatPower).ContainsKey(type);
        }
    }

    [Test]
    public async Task ProtecOffered_ProtecNeeded_TrnAdj_GdmKill_HaveEveryShipType()
    {
        foreach (var type in Enum.GetValues<ShipType>()) {
            await Assert.That(CombatConstants.ProtecOffered).ContainsKey(type);
            await Assert.That(CombatConstants.ProtecNeeded).ContainsKey(type);
            await Assert.That(CombatConstants.TrnAdj).ContainsKey(type);
            await Assert.That(CombatConstants.GdmKill).ContainsKey(type);
        }
    }

    [Test]
    public async Task CargoSpace_HasEveryCargoType()
    {
        foreach (var type in Enum.GetValues<CargoType>())
            await Assert.That(CombatConstants.CargoSpace).ContainsKey(type);
    }

    [Test]
    public async Task CombatTechAdj_HasEveryAttackerDefenderPair()
    {
        var techLevels = Enum.GetValues<TechLevel>();
        foreach (var attacker in techLevels)
            foreach (var defender in techLevels)
                await Assert.That(CombatConstants.CombatTechAdj).ContainsKey((attacker, defender));
    }

    [Test]
    public async Task CombatTechAdj_IsAsymmetricInFavorOfHigherAttackerTech()
    {
        // Gate attacker vs. pre-tech defender (ATTACK.PAS:145): 530 — heavily favors the attacker's
        // tech gap. Pre-tech attacker vs. gate defender (ATTACK.PAS:135's last column): 1.
        await Assert.That(CombatConstants.CombatTechAdj[(TechLevel.Gate, TechLevel.PreTech)]).IsEqualTo(530);
        await Assert.That(CombatConstants.CombatTechAdj[(TechLevel.PreTech, TechLevel.Gate)]).IsEqualTo(1);
    }

    [Test]
    public async Task CombatClassAdj_HasEveryWorldClass()
    {
        foreach (var cls in Enum.GetValues<WorldClass>())
            await Assert.That(CombatConstants.CombatClassAdj).ContainsKey(cls);
    }

    [Test]
    public async Task CombatBaseAdj_HasEveryStarbaseKind()
    {
        foreach (var kind in Enum.GetValues<StarbaseKind>())
            await Assert.That(CombatConstants.CombatBaseAdj).ContainsKey(kind);
    }

    [Test]
    public async Task GdmLaunch_HasEveryTechLevel()
    {
        foreach (var tech in Enum.GetValues<TechLevel>())
            await Assert.That(CombatConstants.GdmLaunch).ContainsKey(tech);
    }

    [Test]
    public async Task AttackTypeMapping_RoundTripsThroughDefenseAndShipType()
    {
        foreach (var defense in Enum.GetValues<DefenseType>())
            await Assert.That(defense.ToAttackType().AsDefenseType()).IsEqualTo(defense);

        foreach (var ship in Enum.GetValues<ShipType>())
            await Assert.That(ship.ToAttackType().AsShipType()).IsEqualTo(ship);
    }

    [Test]
    public async Task AttackTypeMapping_TroopTypesAreNeitherDefenseNorShip()
    {
        await Assert.That(AttackType.Legion.AsDefenseType()).IsNull();
        await Assert.That(AttackType.Legion.AsShipType()).IsNull();
        await Assert.That(AttackType.NinjaLegion.AsDefenseType()).IsNull();
        await Assert.That(AttackType.NinjaLegion.AsShipType()).IsNull();
    }
}
