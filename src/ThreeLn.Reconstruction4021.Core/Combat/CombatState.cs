using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Combat;

/// <summary>Pascal's GroupStatus (ATTACK.PAS:55): Ready to act, Advancing/Retreating between shells, or Destroyed.</summary>
public enum GroupStatus
{
    Ready,
    Advancing,
    Retreating,
    Destroyed,
}

/// <summary>Pascal's AttackIntentionTypes (ATTACK.PAS:27-30): what an attack is trying to accomplish — biases targeting toward or away from transports.</summary>
public enum AttackIntentionType
{
    None,
    Conquer,
    DestroyTransports,
    CaptureTransports,
}

/// <summary>Pascal's AttackResultTypes (ATTACK.PAS:32-33): how an engagement ended.</summary>
public enum AttackResultType
{
    None,
    AttackerDestroyed,
    AttackerRetreats,
    DefenderConquered,
    DefenderCaptured,
}

/// <summary>
/// One group of like ships (or, after AdvanceGroups' own transport-to-troop swap on reaching the
/// ground, ground troops) moving and fighting together (ATTACK.PAS:57-67's GroupRecord). A mutable
/// class, not a record or
/// struct — every field is reassigned in place by CombatEngine as combat proceeds (matching Pascal's
/// own VAR-array-element mutation), and reference identity is what a <see cref="HashSet{T}"/> of
/// groups (Pascal's <c>GroupSet</c>) keys on.
/// </summary>
public sealed class GroupRecord
{
    public required AttackType Typ { get; set; }
    public int Num { get; set; }

    /// <summary>Null for Pascal's NoRes sentinel ("no target chosen yet") — see AttackType's own doc comment.</summary>
    public AttackType? Trg { get; set; }

    public ShellPosition Pos { get; set; } = ShellPosition.DeepSpace;
    public GroupStatus Sta { get; set; } = GroupStatus.Ready;

    /// <summary>Ground Assault Troops carried by a transport/jumptransport group.</summary>
    public int Gat { get; set; }

    /// <summary>Null unless <see cref="Gat"/> is a real troop cargo (Legion or NinjaLegion).</summary>
    public AttackType? GatTyp { get; set; }

    /// <summary>Temporary storage for a transport group's own ship type, written by AdvanceGroups when
    /// it swaps Typ to the carried troop type on reaching the ground (<see cref="CombatResolution"/>).</summary>
    public ShipType? TrnTyp { get; set; }

    /// <summary>Whether a hunter-killer group's cloak is off (it uncloaks the instant it attacks).</summary>
    public bool Flg { get; set; }
}

/// <summary>
/// Enemy forces present at each orbital shell, by AttackType (ATTACK.PAS's EnemyArray). Backed by a
/// plain 2D array rather than nested dictionaries — this grid is read/written in the innermost loops
/// of Battle's per-round resolution, one lookup per (shell, type) pair per group per round.
/// </summary>
public sealed class EnemyForces
{
    private static readonly int _shellCount = Enum.GetValues<ShellPosition>().Length;
    private static readonly int _typeCount = Enum.GetValues<AttackType>().Length;

    private readonly int[,] _counts = new int[_shellCount, _typeCount];

    public int this[ShellPosition position, AttackType type]
    {
        get => _counts[(int)position, (int)type];
        set => _counts[(int)position, (int)type] = value;
    }
}

/// <summary>Per-AttackType running total (ATTACK.PAS's AttackArray) — used for Casualties, Killed, and a round's EShDest.</summary>
public sealed class AttackTally
{
    private readonly int[] _values = new int[Enum.GetValues<AttackType>().Length];

    public int this[AttackType type]
    {
        get => _values[(int)type];
        set => _values[(int)type] = value;
    }
}

/// <summary>
/// Per-(AttackType, group) cumulative damage this combat, plus which AttackTypes have hit anything at
/// all (ATTACK.PAS's DetailArray) — the latter derived from whether any recorded hit was nonzero
/// rather than stored as Pascal's own literal <c>Details[ShpI,0]:=1</c> sentinel-index write, the same
/// "derive, don't duplicate" fix already applied to <see cref="Empire.DefeatedBy"/> for the capital
/// sentinel. Nothing reads this yet — it exists for whichever later commit ports ResolveAttack's
/// detailed-loss news reporting.
/// </summary>
public sealed class CombatDetails
{
    private readonly Dictionary<(AttackType Type, GroupRecord Group), int> _perGroup = [];
    private readonly HashSet<AttackType> _anyDamage = [];

    public int Get(AttackType type, GroupRecord group) => _perGroup.GetValueOrDefault((type, group));

    /// <summary>Records this round's <paramref name="damage"/> against <paramref name="group"/>, clamped to the group's own current <see cref="GroupRecord.Num"/> (ATTACK.PAS:803).</summary>
    public void Record(AttackType type, GroupRecord group, int damage, int groupCurrentNum)
    {
        _perGroup[(type, group)] = Math.Min(PascalMath.ClampResource(Get(type, group) + damage), groupCurrentNum);
        if (damage > 0) {
            _anyDamage.Add(type);
        }
    }

    public bool AnyDamage(AttackType type) => _anyDamage.Contains(type);
}

/// <summary>
/// Pre-computed adjustments for one attack (ATTACK.PAS:45-53's CombatDataRecord), from
/// CalculateCombatData. <see cref="Target"/> mirrors Pascal's own IDNumber tagged union (a Planet,
/// Starbase, or Fleet) rather than a separate stored "kind" enum — CalculateCombatData/EnemySurrenders
/// both branch on it with a type pattern instead (<c>Target is Fleet</c>, etc.), deriving the kind
/// from the reference instead of duplicating it as data that could drift from what Target actually is.
/// </summary>
public sealed record CombatDataRecord(
    object Target, TechLevel DTech, int AShipAdj, int DShipAdj, int DGrndAdj, int MaxGdm, int RevIndex);
