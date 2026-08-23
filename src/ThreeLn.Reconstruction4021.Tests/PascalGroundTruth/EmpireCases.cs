using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests, requires fpc and git —
/// dynamically skipped otherwise) and the always-on AnnualTickHandlerEmpireTests.MatchesGoldenFile.
/// Only inputs live here — expected outputs live exclusively in reference/verify/golden/empire.golden,
/// computed patch-based: runworld.pas's empire domain calls UpdateEmpire directly (not UpdateWorld —
/// NewTechLevel is empire-level, not per-world, so there's no need to run a full per-planet tick to
/// exercise it), against a hand-assembled Universe^, not a per-procedure transcription.
///
/// TechnologyBitmask encodes Empire.Technology (all 4 categories: Defenses, Ships, Resources,
/// Constructions) as a single 26-bit mask, bit i = TechnologyTypes(i+1) in Pascal's own
/// enum-declaration order (LAM..dis, skipping NoRes) — Defenses(4 bits) then Ships(7) then
/// Resources/CargoType(7) then Constructions(8) = 26. Every category enum here was declared in the
/// same order as its Pascal counterpart (confirmed directly against TYPES.PAS), so bit position and
/// enum-declaration position always agree in both directions.
///
/// Up to two planet labs and one starbase lab cover every branch of GetChanceForNewTech
/// (Capital; University at exactly EmpTech, with/without Ruins class; Ruins-only fallback; the same
/// two branches again for a starbase) without needing Pascal's full 20-lab array. A null lab leaves
/// that slot present in neither SetOfPlanetsOf nor SetOfStarbasesOf, matching a real empire that
/// simply owns fewer worlds — see runworld.pas's empire domain.
/// </summary>
public sealed record EmpireLab(WorldType Type, WorldClass Class, TechLevel Tech, int Efficiency);

public sealed record EmpireCase(
    string Name, TechLevel TechLevel, int TechnologyBitmask, int RngFixedValue,
    EmpireLab? Planet1, EmpireLab? Planet2, EmpireLab? Starbase) : INamedCase;

internal static class EmpireCases
{
    private const int SuppliesBit = 1 << 16;             // TechDev[PreTech] = [sup] exactly
    private const int FullyResearchedMask = (1 << 26) - 1;

    public static readonly IReadOnlyList<EmpireCase> All = [
        // Empty Technology at PreTech: only Supplies (TechDev[PreTech]=[sup]) is missing/possible.
        // Roll=Rnd(1,12)=1<=12(TechIncCap) succeeds -> GetNewTech's only candidate (Supplies) is added.
        new(Name: "NewTechnologyRollSucceeds", TechLevel: TechLevel.PreTech, TechnologyBitmask: 0, RngFixedValue: 0,
            Planet1: new EmpireLab(WorldType.Capital, WorldClass.ClassM, TechLevel.PreTech, Efficiency: 100),
            Planet2: null, Starbase: null),

        // Technology already equals TechDev[PreTech] ([sup]) -> "new tech level" branch. Two labs:
        // Capital(chance 12) + University at exactly PreTech(chance 15) = 27 total; Roll=Rnd(1,27)=1
        // picks the Capital (processed first) as LabID, but TechnologyLevel advances regardless of
        // which lab won the walk. TechLevel advances to Primitive; Technology set itself is unchanged
        // (the level-up branch never grants the new level's techs).
        new(Name: "TechLevelAdvanceRollSucceeds", TechLevel: TechLevel.PreTech, TechnologyBitmask: SuppliesBit, RngFixedValue: 0,
            Planet1: new EmpireLab(WorldType.Capital, WorldClass.ClassM, TechLevel.PreTech, Efficiency: 100),
            Planet2: new EmpireLab(WorldType.University, WorldClass.ClassM, TechLevel.PreTech, Efficiency: 100),
            Starbase: null),

        // Every category already complete (TechSet=TechDev[GteTchLvl]) -> the outer guard is a no-op
        // before GetChanceForNewTech is even called; TechLevel/Technology come back unchanged despite
        // a live Capital lab that would otherwise definitely succeed.
        new(Name: "AlreadyFullyResearchedNoOp", TechLevel: TechLevel.Gate, TechnologyBitmask: FullyResearchedMask, RngFixedValue: 0,
            Planet1: new EmpireLab(WorldType.Capital, WorldClass.ClassM, TechLevel.Gate, Efficiency: 100),
            Planet2: null, Starbase: null),

        // No owned planets or starbases at all -> GetChanceForNewTech's Rnd(1,0) fallback (returns 1,
        // per Rnd's Max<=Min contract) with zero labs to walk; TotalChance=0 makes the outer
        // Rnd(1,100)<=0 gate impossible to pass regardless of RngFixedValue.
        new(Name: "ZeroLabsNoOp", TechLevel: TechLevel.PreTech, TechnologyBitmask: 0, RngFixedValue: 0,
            Planet1: null, Planet2: null, Starbase: null),

        // The empire's only lab is a starbase (University at exactly EmpTech) -- proves the starbase
        // cascade (a strict subset of the planet cascade's branches) actually participates, not just
        // the planet loop. Same "new technology" shape as NewTechnologyRollSucceeds otherwise.
        new(Name: "StarbaseAsLabContributes", TechLevel: TechLevel.PreTech, TechnologyBitmask: 0, RngFixedValue: 0,
            Planet1: null, Planet2: null,
            Starbase: new EmpireLab(WorldType.University, WorldClass.ClassM, TechLevel.PreTech, Efficiency: 100)),

        // Every case above uses Efficiency=100, where GetChanceForNewTech's Trunc(percent*eff/100) is
        // always an exact whole-number pass-through of the percent constant — none of them can tell a
        // real Trunc from an accidental Round. A fractional-efficiency case belongs in
        // AnnualTickHandlerEmpireTests as a hardcoded test instead of here: this harness's
        // runworld.pas empire domain calls UpdateEmpire directly and never runs UpdateEfficiency, but
        // the C# side can only reach NewTechLevel (private) via the full RunAnnualTick, which *does*
        // run UpdateEfficiency on every lab planet first — growing a non-100 starting Efficiency by a
        // RngFixedValue-dependent amount before NewTechLevel ever reads it. That mismatch makes a
        // shared Pascal-CLI-arg/C#-fixture Efficiency field impossible for this specific scenario; see
        // FractionalLabChanceTruncatesNotRounds's own comment for the case and its independent
        // real-Pascal verification.
    ];

    /// <summary>MethodDataSource shape for AnnualTickHandlerEmpireTests.MatchesGoldenFile — one
    /// Func per case, per TUnit's guidance for reference types.</summary>
    public static IEnumerable<Func<EmpireCase>> AsDataSource() => All.Select(c => (Func<EmpireCase>)(() => c));
}
