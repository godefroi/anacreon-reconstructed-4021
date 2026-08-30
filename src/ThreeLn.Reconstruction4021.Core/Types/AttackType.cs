namespace ThreeLn.Reconstruction4021.Core.Types;

/// <summary>
/// The combat engine's single indexing axis for "a thing that can attack or be attacked": Pascal's
/// <c>AttackTypes = NoRes..nnj</c> (ATTACK.PAS/TYPES.PAS), minus the leading <c>NoRes</c> sentinel —
/// that value only exists in Pascal because <c>AttackTypes</c> is a subrange starting at the first
/// member of the wider <c>TechnologyTypes</c> enum, not because "no attack type" is ever a real
/// lookup key; <see cref="Combat.CombatConstants.CombatTable"/>'s own NUL row/column are
/// correspondingly all zero and never read. A real "no target yet" case (e.g. an unset
/// <c>GroupRecord.Trg</c>) is represented as <c>AttackType?</c> at the call site instead of porting
/// that sentinel.
///
/// Spans <see cref="DefenseType"/> ∪ <see cref="ShipType"/> ∪ { Legion, NinjaLegion } — a genuine
/// single cross-product axis for <c>CombatTable[Attacker,Defender]</c>, unlike
/// <see cref="Entities.TechCatalog.TechGrantIdentity"/>'s four independent identity spaces.
/// <see cref="DefenseType"/>/<see cref="ShipType"/> stay exactly as they are — real per-type stored
/// state lives on <see cref="Entities.DefenseCounts"/>/<see cref="Entities.ShipCounts"/> — this enum
/// is purely a combat-engine indexing concern, with <see cref="AttackTypeExtensions"/> mapping at
/// the boundary.
/// Declared in the same order Pascal's <c>AttackTypes</c> does (LAM..nnj) so every combat constant
/// table below reads directly off the source comments without a reordering step.
/// </summary>
public enum AttackType
{
    Lam,
    DefenseSatellite,
    Gdm,
    IonCannon,
    Fighter,
    HunterKiller,
    Jumpship,
    Jumptransport,
    Penetrator,
    Starship,
    Transport,
    Legion,
    NinjaLegion,
}

public static class AttackTypeExtensions
{
    public static AttackType ToAttackType(this DefenseType type) => type switch {
        DefenseType.Lam => AttackType.Lam,
        DefenseType.DefenseSatellite => AttackType.DefenseSatellite,
        DefenseType.Gdm => AttackType.Gdm,
        DefenseType.IonCannon => AttackType.IonCannon,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static AttackType ToAttackType(this ShipType type) => type switch {
        ShipType.Fighter => AttackType.Fighter,
        ShipType.HunterKiller => AttackType.HunterKiller,
        ShipType.Jumpship => AttackType.Jumpship,
        ShipType.Jumptransport => AttackType.Jumptransport,
        ShipType.Penetrator => AttackType.Penetrator,
        ShipType.Starship => AttackType.Starship,
        ShipType.Transport => AttackType.Transport,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Null for the two troop AttackTypes (Legion/NinjaLegion) — they aren't DefenseTypes.</summary>
    public static DefenseType? AsDefenseType(this AttackType type) => type switch {
        AttackType.Lam => DefenseType.Lam,
        AttackType.DefenseSatellite => DefenseType.DefenseSatellite,
        AttackType.Gdm => DefenseType.Gdm,
        AttackType.IonCannon => DefenseType.IonCannon,
        _ => null,
    };

    /// <summary>Null for the troop AttackTypes and every DefenseType — they aren't ShipTypes.</summary>
    public static ShipType? AsShipType(this AttackType type) => type switch {
        AttackType.Fighter => ShipType.Fighter,
        AttackType.HunterKiller => ShipType.HunterKiller,
        AttackType.Jumpship => ShipType.Jumpship,
        AttackType.Jumptransport => ShipType.Jumptransport,
        AttackType.Penetrator => ShipType.Penetrator,
        AttackType.Starship => ShipType.Starship,
        AttackType.Transport => ShipType.Transport,
        _ => null,
    };
}
