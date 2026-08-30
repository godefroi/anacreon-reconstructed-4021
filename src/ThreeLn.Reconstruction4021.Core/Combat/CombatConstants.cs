using System.Collections.Frozen;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Combat;

/// <summary>
/// The pure balance data ATTACK.PAS's group/shell combat engine reads. Combat logic itself
/// (<c>Battle</c>/<c>GroupAttack</c>/<c>EnemyAttack</c> and friends) lives in <see cref="CombatEngine"/>;
/// these are just the constant tables, transcribed directly from source since they're isolated,
/// parameter-only data with no real-state dependency.
/// Every table is keyed by a real C# enum whose declaration order already matches the Pascal source
/// order 1:1 (verified against TYPES.PAS/DATACNST.PAS/ATTACK.PAS directly), so each row below reads
/// off the cited source comment without any reordering step.
/// </summary>
public static class CombatConstants
{
    /// <summary>
    /// Destructive power of AttackType vs. AttackType — 100 points destroys one unit of the
    /// defending type, 200 destroys 2, etc. (DATACNST.PAS:172-194). Pascal's table also carries a
    /// leading <c>NoRes</c> row/column, all zero and never legitimately read (see AttackType's own
    /// doc comment) — omitted here rather than ported as dead data.
    /// </summary>
    public static readonly FrozenDictionary<(AttackType Attacker, AttackType Defender), int> CombatTable = BuildCombatTable();

    private static FrozenDictionary<(AttackType, AttackType), int> BuildCombatTable()
    {
        var defenders = Enum.GetValues<AttackType>();
        var table = new Dictionary<(AttackType, AttackType), int>();

        void Row(AttackType attacker, params int[] values)
        {
            for (var i = 0; i < defenders.Length; i++)
                table[(attacker, defenders[i])] = values[i];
        }

        //                            LAM  def  GDM  ion  fgt  hkr  jmp  jtn  pen  ssp  trn  men  nnj
        Row(AttackType.Lam, /*         */ 200, 100, 200, 150, 250, 60, 75, 100, 50, 15, 200, 0, 0);
        Row(AttackType.DefenseSatellite, 0, 0, 0, 0, 25, 90, 100, 100, 50, 6, 250, 0, 0);
        Row(AttackType.Gdm, /*         */ 0, 0, 0, 0, 250, 100, 100, 100, 50, 25, 50, 0, 0);
        Row(AttackType.IonCannon, /*   */ 0, 0, 0, 0, 50, 65, 75, 75, 15, 2, 100, 0, 0);
        Row(AttackType.Fighter, /*     */ 5, 5, 1, 25, 25, 15, 8, 20, 12, 1, 25, 5, 4);
        Row(AttackType.HunterKiller, /**/ 12, 40, 38, 20, 50, 50, 75, 50, 50, 4, 75, 0, 0);
        Row(AttackType.Jumpship, /*    */ 8, 15, 25, 10, 150, 25, 38, 50, 25, 1, 150, 0, 0);
        Row(AttackType.Jumptransport, /**/0, 1, 0, 0, 25, 1, 1, 5, 0, 0, 10, 0, 0);
        Row(AttackType.Penetrator, /*  */ 15, 35, 50, 35, 50, 65, 105, 125, 50, 5, 250, 0, 0);
        Row(AttackType.Starship, /*    */ 45, 90, 200, 50, 1000, 250, 300, 500, 100, 15, 500, 0, 0);
        Row(AttackType.Transport, /*   */ 0, 0, 0, 0, 12, 0, 0, 1, 0, 0, 5, 0, 0);
        Row(AttackType.Legion, /*      */ 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 7, 2);
        Row(AttackType.NinjaLegion, /* */ 0, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 20, 10);

        return table.ToFrozenDictionary();
    }

    /// <summary>Average effectiveness of weapons per AttackType — never 0 (DATACNST.PAS:196-201).</summary>
    public static readonly FrozenDictionary<AttackType, int> WeapEff = new Dictionary<AttackType, int> {
        [AttackType.Lam] = 100, [AttackType.DefenseSatellite] = 150, [AttackType.Gdm] = 100, [AttackType.IonCannon] = 30,
        [AttackType.Fighter] = 30, [AttackType.HunterKiller] = 100, [AttackType.Jumpship] = 100, [AttackType.Jumptransport] = 10,
        [AttackType.Penetrator] = 150, [AttackType.Starship] = 100, [AttackType.Transport] = 10,
        [AttackType.Legion] = 100, [AttackType.NinjaLegion] = 100,
    }.ToFrozenDictionary();

    /// <summary>Average value of a given AttackType, used by the surrender algorithm (DATACNST.PAS:203-206).</summary>
    public static readonly FrozenDictionary<AttackType, int> ShipValue = new Dictionary<AttackType, int> {
        [AttackType.Lam] = 0, [AttackType.DefenseSatellite] = 0, [AttackType.Gdm] = 0, [AttackType.IonCannon] = 0,
        [AttackType.Fighter] = 5, [AttackType.HunterKiller] = 50, [AttackType.Jumpship] = 50, [AttackType.Jumptransport] = 30,
        [AttackType.Penetrator] = 100, [AttackType.Starship] = 200, [AttackType.Transport] = 30,
        [AttackType.Legion] = 50, [AttackType.NinjaLegion] = 100,
    }.ToFrozenDictionary();

    /// <summary>Military power of an AttackType, for the surrender algorithm (ATTACK.PAS:76-81, values 1-100).</summary>
    public static readonly FrozenDictionary<AttackType, int> CombatPower = new Dictionary<AttackType, int> {
        [AttackType.Lam] = 80, [AttackType.DefenseSatellite] = 75, [AttackType.Gdm] = 20, [AttackType.IonCannon] = 65,
        [AttackType.Fighter] = 2, [AttackType.HunterKiller] = 15, [AttackType.Jumpship] = 10, [AttackType.Jumptransport] = 1,
        [AttackType.Penetrator] = 20, [AttackType.Starship] = 100, [AttackType.Transport] = 1,
        [AttackType.Legion] = 20, [AttackType.NinjaLegion] = 100,
    }.ToFrozenDictionary();

    /// <summary>
    /// MPower (DATACNST.PAS:167-170) — a genuinely separate, differently-valued table from
    /// <see cref="CombatPower"/> above despite the similar name and role: Pascal declares
    /// <c>CombatPower</c> locally in ATTACK.PAS for the surrender algorithm, and this one, also
    /// locally named <c>MPower</c>, in DATACNST.PAS for the general-purpose <c>MISC.PAS</c>
    /// <c>MilitaryPower</c> function real Pascal's NPE code (NPE00/01/03/04.PAS) and ATTACK.PAS's own
    /// group-power totals all call. Confirmed distinct by reading both declarations directly — e.g.
    /// Lam is 100 here vs. 80 in <see cref="CombatPower"/>, def is 100 vs. 75, trn is 0 vs. 1.
    /// Pascal's own array is only <c>ARRAY[LAM..trn]</c> (no Legion/NinjaLegion entries at all) since
    /// <c>MilitaryPower</c> never indexes past <c>trn</c> — but NPE00.PAS's <c>AttackSeverity</c>
    /// (ReviewNews's own nested function) indexes this same table by a DestructionDetail news item's
    /// raw AttackType, which — via this port's <c>ReportLosses</c> — can legitimately be Legion/
    /// NinjaLegion when ground troops die. Real Pascal reads whatever garbage byte happens to sit past
    /// the array's declared end there (no range checking); reproducing that isn't possible or
    /// meaningful, so Legion/NinjaLegion get a defined entry here instead, borrowed from
    /// <see cref="CombatPower"/>'s own values for those two types rather than left absent.
    /// </summary>
    public static readonly FrozenDictionary<AttackType, int> MPower = new Dictionary<AttackType, int> {
        [AttackType.Lam] = 100, [AttackType.DefenseSatellite] = 100, [AttackType.Gdm] = 10, [AttackType.IonCannon] = 50,
        [AttackType.Fighter] = 1, [AttackType.HunterKiller] = 20, [AttackType.Jumpship] = 12, [AttackType.Jumptransport] = 1,
        [AttackType.Penetrator] = 25, [AttackType.Starship] = 100, [AttackType.Transport] = 0,
        [AttackType.Legion] = 20, [AttackType.NinjaLegion] = 100,
    }.ToFrozenDictionary();

    /// <summary>How well a given ship type can protect others in the same shell (DATACNST.PAS:208-211).</summary>
    public static readonly FrozenDictionary<ShipType, int> ProtecOffered = new Dictionary<ShipType, int> {
        [ShipType.Fighter] = 5, [ShipType.HunterKiller] = 10, [ShipType.Jumpship] = 10, [ShipType.Jumptransport] = 1,
        [ShipType.Penetrator] = 50, [ShipType.Starship] = 100, [ShipType.Transport] = 0,
    }.ToFrozenDictionary();

    /// <summary>Relative strength needed to protect each ship type — never 0 (DATACNST.PAS:213-218).</summary>
    public static readonly FrozenDictionary<ShipType, int> ProtecNeeded = new Dictionary<ShipType, int> {
        [ShipType.Fighter] = 5, [ShipType.HunterKiller] = 30, [ShipType.Jumpship] = 20, [ShipType.Jumptransport] = 50,
        [ShipType.Penetrator] = 200, [ShipType.Starship] = 500, [ShipType.Transport] = 150,
    }.ToFrozenDictionary();

    /// <summary>Units of a cargo type that fit in one transport-equivalent of hold space (DATACNST.PAS:381-384).</summary>
    public static readonly FrozenDictionary<CargoType, int> CargoSpace = new Dictionary<CargoType, int> {
        [CargoType.Legion] = 5, [CargoType.NinjaLegion] = 5, [CargoType.Ambrosia] = 100,
        [CargoType.Chemicals] = 3, [CargoType.Metals] = 3, [CargoType.Supplies] = 2, [CargoType.Trillum] = 100,
    }.ToFrozenDictionary();

    /// <summary>Cargo capacity of each ship type relative to a transport (=1) (DATACNST.PAS:386-389).</summary>
    public static readonly FrozenDictionary<ShipType, double> TrnAdj = new Dictionary<ShipType, double> {
        [ShipType.Fighter] = 0, [ShipType.HunterKiller] = 0, [ShipType.Jumpship] = 0, [ShipType.Jumptransport] = 0.2,
        [ShipType.Penetrator] = 0, [ShipType.Starship] = 0, [ShipType.Transport] = 1,
    }.ToFrozenDictionary();

    /// <summary>
    /// Adjustment of destructive power by (attacker tech, defender tech) — 100 is neutral, above 100
    /// favors the defender, below 100 favors the attacker (ATTACK.PAS:127-145). Indexed
    /// <c>[AttackerTech, DefenderTech]</c> — confirmed from the two real call sites,
    /// <c>CombatTechAdj[ATech,DTech]</c> and <c>CombatTechAdj[DTech,ATech]</c> (ATTACK.PAS:364-365).
    /// </summary>
    public static readonly FrozenDictionary<(TechLevel Attacker, TechLevel Defender), int> CombatTechAdj = BuildCombatTechAdj();

    private static FrozenDictionary<(TechLevel, TechLevel), int> BuildCombatTechAdj()
    {
        var defenders = Enum.GetValues<TechLevel>();
        var table = new Dictionary<(TechLevel, TechLevel), int>();

        void Row(TechLevel attacker, params int[] values)
        {
            for (var i = 0; i < defenders.Length; i++)
                table[(attacker, defenders[i])] = values[i];
        }

        //                             pt    p   pa    a   pw    w    j    b    s   pg    g
        Row(TechLevel.PreTech, /* */ 100, 90, 50, 25, 10, 5, 3, 1, 1, 1, 1);
        Row(TechLevel.Primitive, /**/ 120, 100, 80, 40, 25, 10, 5, 3, 1, 1, 1);
        Row(TechLevel.PreAtomic, /**/ 150, 110, 100, 60, 40, 25, 10, 5, 3, 1, 1);
        Row(TechLevel.Atomic, /*  */ 160, 140, 130, 100, 60, 40, 30, 20, 10, 5, 3);
        Row(TechLevel.PreWarp, /* */ 200, 170, 145, 120, 100, 90, 85, 75, 70, 55, 45);
        Row(TechLevel.Warp, /*    */ 250, 200, 175, 140, 110, 100, 95, 90, 85, 70, 55);
        Row(TechLevel.Jump, /*    */ 280, 210, 190, 175, 115, 110, 100, 97, 93, 80, 70);
        Row(TechLevel.Bio, /*     */ 320, 250, 210, 195, 120, 115, 105, 100, 95, 90, 80);
        Row(TechLevel.Starship, /**/ 360, 320, 280, 220, 125, 120, 115, 110, 100, 97, 90);
        Row(TechLevel.PreGate, /* */ 500, 400, 300, 250, 130, 125, 120, 115, 110, 100, 95);
        Row(TechLevel.Gate, /*    */ 530, 415, 310, 260, 135, 130, 125, 120, 115, 110, 100);

        return table.ToFrozenDictionary();
    }

    /// <summary>Combat adjustment by world class, e.g. a barren world's terrain favors the attacker (ATTACK.PAS:147-168).</summary>
    public static readonly FrozenDictionary<WorldClass, int> CombatClassAdj = new Dictionary<WorldClass, int> {
        [WorldClass.Ambrosia] = 100, [WorldClass.Arid] = 100, [WorldClass.Artificial] = 100, [WorldClass.Barren] = 50,
        [WorldClass.ClassJ] = 100, [WorldClass.ClassK] = 100, [WorldClass.ClassL] = 100, [WorldClass.ClassM] = 100,
        [WorldClass.Desert] = 140, [WorldClass.EarthLike] = 100, [WorldClass.Forest] = 140, [WorldClass.GasGiant] = 50,
        [WorldClass.Hostile] = 100, [WorldClass.Ice] = 160, [WorldClass.Jungle] = 150, [WorldClass.Ocean] = 80,
        [WorldClass.Paradise] = 100, [WorldClass.Poisonous] = 60, [WorldClass.Ruins] = 100, [WorldClass.Underground] = 130,
        [WorldClass.Volcanic] = 120,
    }.ToFrozenDictionary();

    /// <summary>Combat adjustment by starbase kind, e.g. a Fortress's fortification favors the defender (ATTACK.PAS:170-171).</summary>
    public static readonly FrozenDictionary<StarbaseKind, int> CombatBaseAdj = new Dictionary<StarbaseKind, int> {
        [StarbaseKind.CommandBase] = 150, [StarbaseKind.Fortress] = 250, [StarbaseKind.IndustrialComplex] = 100, [StarbaseKind.Outpost] = 125,
    }.ToFrozenDictionary();

    /// <summary>Number of GDMs (ground defense missiles) launched at one time, by the defending world's tech level (ATTACK.PAS:173-176).</summary>
    public static readonly FrozenDictionary<TechLevel, int> GdmLaunch = new Dictionary<TechLevel, int> {
        [TechLevel.PreTech] = 0, [TechLevel.Primitive] = 0, [TechLevel.PreAtomic] = 0, [TechLevel.Atomic] = 20,
        [TechLevel.PreWarp] = 217, [TechLevel.Warp] = 662, [TechLevel.Jump] = 1013, [TechLevel.Bio] = 1261,
        [TechLevel.Starship] = 2163, [TechLevel.PreGate] = 2644, [TechLevel.Gate] = 2759,
    }.ToFrozenDictionary();

    /// <summary>Number of incoming GDMs killed on approach, per 10 ships of the given type (ATTACK.PAS:178-181).</summary>
    public static readonly FrozenDictionary<ShipType, int> GdmKill = new Dictionary<ShipType, int> {
        [ShipType.Fighter] = 0, [ShipType.HunterKiller] = 3, [ShipType.Jumpship] = 2, [ShipType.Jumptransport] = 1,
        [ShipType.Penetrator] = 15, [ShipType.Starship] = 20, [ShipType.Transport] = 0,
    }.ToFrozenDictionary();
}
