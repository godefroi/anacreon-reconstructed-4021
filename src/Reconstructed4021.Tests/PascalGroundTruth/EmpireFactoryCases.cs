using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and the always-on
/// EmpireFactoryTests.MatchesGoldenFile. Only inputs live here — expected outputs live exclusively in
/// reference/verify/golden/empirecreate.golden, computed patch-based: runworld.pas's empirecreate
/// domain reproduces NEWGAME.PAS's exact tech-set formula (KnownTechs:=TechDev[Pred(Tech)];
/// KnownTechs:=KnownTechs+extras; KnownTechs:=KnownTechs*TechDev[Tech]) inline — rather than pulling
/// in all of NEWGAME.PAS's much larger USES clause (Crt/Dos/DOS2/EIO/WND/Menu/DFA/LoadSave/NPE/
/// NPETypes) for 3 lines of pure set math — then calls the real, already-exported CreateEmpire
/// (PRIMINTR.PAS) with it. No RNG anywhere in this domain; both the formula and CreateEmpire are
/// fully deterministic.
///
/// ExtraTechsMask uses the same 26-bit encoding as the empire domain (see EmpireCases's doc comment)
/// — bit i = TechnologyTypes(i+1).
///
/// The "no extra techs" cases at every level from Primitive through Gate exhaustively cross-check all
/// 11 rows of Pascal's real TechDev constant (DATACNST.PAS:359-370) against TechCatalog's own
/// min-tech tables (with no extras, KnownTechs always collapses to exactly TechDev[Pred(Tech)] — the
/// final *TechDev[Tech] intersect is a no-op by TechDev's own monotonicity). TechLevel.PreTech itself
/// is deliberately not exercised here — Pred(PreTchLvl) is an out-of-range TechDev index in real
/// Pascal (a range-check error), not a well-defined empty set, and no real scenario file ever creates
/// a player/NPE empire at that level; see EmpireFactoryTests' own hardcoded coverage of that
/// defensive case instead.
/// </summary>
public sealed record EmpireFactoryCase(string Name, TechLevel TechLevel, int ExtraTechsMask) : INamedCase;

internal static class EmpireFactoryCases
{
    // Bit positions: Defenses(4, bits 0-3) then Ships(7, bits 4-10) then Resources(7, bits 11-17)
    // then Constructions(8, bits 18-25) — see EmpireCases's doc comment for the full convention.
    private const int HunterKillerBit = 1 << 5;         // 2nd ship; MinTech=Bio, matches the level itself
    private const int MinefieldBit = 1 << 18;           // 1st construction; MinTech=Starship, one past Bio
    private const int GateConstructionBit = 1 << 23;    // the only entry whose MinTech is Gate itself

    public static readonly IReadOnlyList<EmpireFactoryCase> All = [
        new(Name: "Primitive", TechLevel: TechLevel.Primitive, ExtraTechsMask: 0),
        new(Name: "PreAtomic", TechLevel: TechLevel.PreAtomic, ExtraTechsMask: 0),
        new(Name: "Atomic", TechLevel: TechLevel.Atomic, ExtraTechsMask: 0),
        new(Name: "PreWarp", TechLevel: TechLevel.PreWarp, ExtraTechsMask: 0),
        new(Name: "Warp", TechLevel: TechLevel.Warp, ExtraTechsMask: 0),
        new(Name: "Jump", TechLevel: TechLevel.Jump, ExtraTechsMask: 0),
        new(Name: "Bio", TechLevel: TechLevel.Bio, ExtraTechsMask: 0),
        new(Name: "Starship", TechLevel: TechLevel.Starship, ExtraTechsMask: 0),
        new(Name: "PreGate", TechLevel: TechLevel.PreGate, ExtraTechsMask: 0),
        new(Name: "Gate", TechLevel: TechLevel.Gate, ExtraTechsMask: 0),

        // HunterKiller's own MinTech is Bio — already legal at Tech=Bio, survives the final
        // TechDev[Tech] clamp untouched.
        new(Name: "ExtraTechAtCurrentLevel", TechLevel: TechLevel.Bio, ExtraTechsMask: HunterKillerBit),

        // Minefield's MinTech is Starship, one level past Bio — the real Pascal clamp
        // (KnownTechs:=KnownTechs*TechDev[Tech]) silently drops it.
        new(Name: "ExtraTechAboveCurrentLevel", TechLevel: TechLevel.Bio, ExtraTechsMask: MinefieldBit),

        // Gate-construction is the one entry whose MinTech is Gate itself — granting it as an extra
        // at Tech=Gate completes TechDev[Gate] to the full 26-bit set (every category, matching
        // DATACNST.PAS:370's final TechDev row, [LAM..dis]).
        new(Name: "ExtraTechCompletesTopRow", TechLevel: TechLevel.Gate, ExtraTechsMask: GateConstructionBit),
    ];

    public static IEnumerable<Func<EmpireFactoryCase>> AsDataSource() => All.Select(c => (Func<EmpireFactoryCase>)(() => c));
}
