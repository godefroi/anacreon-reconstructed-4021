namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and the always-on
/// LamAttackTests.MatchesGoldenFile. Expected outputs live exclusively in
/// reference/verify/golden/lamattack.golden, computed patch-based: a real run of ATTACK.PAS's own
/// LAMAttack (see ATTACK.PAS.patch) against a hand-assembled Universe^
/// (reference/verify/runworld.pas's lamattack domain). Unlike every other combat domain, LAMAttack
/// has no Rnd calls at all — no RngFixedValue field here — so this is pure deterministic arithmetic
/// verification of its proportional-distribution formula (Round/Trunc against ProtecNeeded/
/// CombatTable), exactly the class of formula PascalRound's own banker's-rounding behavior matters for.
///
/// The attacker is always Empire1 (Player); the target (Fleet or Planet, whichever TargetIsFleet
/// selects) is always Empire2's.
/// </summary>
public sealed record LamAttackCase(
    string Name, bool TargetIsFleet, int LamToUse,
    int Fgt = 0, int Hkr = 0, int Pen = 0, int Trn = 0,
    int Lam = 0, int Def = 0, int Gdm = 0, int Ion = 0) : INamedCase;

internal static class LamAttackCases
{
    public static readonly IReadOnlyList<LamAttackCase> All = [
        // A modest strike against a real fleet -- Pen/Trn's high ProtecNeeded ([Ship]=200/150) fully
        // absorbs their small share and gets wiped out, while Fgt/Hkr (ProtecNeeded 5/30) only take
        // partial losses -- exercises both the partial and the fully-saturated LesserInt clamp in one
        // case.
        new(Name: "FleetPartialDamage", TargetIsFleet: true, LamToUse: 50, Fgt: 100, Hkr: 50, Pen: 10, Trn: 5),

        // A small fleet against an overwhelming strike -- every ship type destroyed outright, so
        // NoShips(Ships) fires and the fleet-destroyed branch (not the damaged/PutShips branch) runs.
        new(Name: "FleetTotalDestruction", TargetIsFleet: true, LamToUse: 5000, Fgt: 5, Hkr: 2),

        // World defenses, moderate strike -- Lam/Gdm/Ion's small starting counts get fully wiped out
        // while Def's larger count only takes partial losses, same "mixed partial/saturated" shape as
        // FleetPartialDamage but through the world/Defns branch instead of the fleet/Ships one.
        new(Name: "WorldPartialDamage", TargetIsFleet: false, LamToUse: 30, Lam: 20, Def: 10, Gdm: 5, Ion: 2),

        // TotalDefns=0 edge case (ATTACK.PAS:1688-1689's fallback to 1, avoiding a divide-by-zero) --
        // a world with no defenses at all takes a strike and nothing happens, confirming the fallback
        // doesn't crash or destroy phantom defenses.
        new(Name: "WorldNoDefenses", TargetIsFleet: false, LamToUse: 100),
    ];

    /// <summary>MethodDataSource shape for LamAttackTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<LamAttackCase>> AsDataSource() => All.Select(c => (Func<LamAttackCase>)(() => c));
}
