namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs for FleetLogisticsTests.MatchesGoldenFile — FuelCapacity/FuelConsumption/
/// FleetCargoSpace/BalanceFleet (MISC.PAS:168-222, INTRFACE.PAS:431-465 as relocated into
/// reference/verify/patched/INTRFACE.PAS's own trimmed copy). No RNG anywhere in
/// these four — pure arithmetic over a ship/cargo distribution, the same class of formula
/// PascalRound's own banker's-rounding behavior matters for (FleetCargoSpace's own
/// Round call is exactly that risk).
/// </summary>
public sealed record FleetLogisticsCase(
    string Name,
    int Fgt = 0, int Hkr = 0, int Jmp = 0, int Jtn = 0, int Pen = 0, int Ssp = 0, int Trn = 0,
    int Men = 0, int Nnj = 0, int Amb = 0, int Che = 0, int Met = 0, int Sup = 0, int Tri = 0) : INamedCase;

internal static class FleetLogisticsCases
{
    public static readonly IReadOnlyList<FleetLogisticsCase> All = [
        // Baseline: one cheap ship, no cargo -- confirms the "+1" base term in both FuelCapacity and
        // FuelConsumption (MISC.PAS's own temp1:=1 starting point) and that an empty CargoArray never
        // triggers BalanceFleet's trim loop.
        new(Name: "SingleFighterNoCargo", Fgt: 1),

        // Every ship type contributes to FuelCapacity/FuelConsumption at once, exercising the full
        // FuelCap/FuelCons table (DATACNST.PAS:394-401), not just one entry.
        new(Name: "OneOfEachShipType", Fgt: 1, Hkr: 1, Jmp: 1, Jtn: 1, Pen: 1, Ssp: 1, Trn: 1),

        // Cargo well within capacity (10 transports easily carry 20 legions) -- FleetCargoSpace stays
        // positive, so BalanceFleet's WHILE loop never executes and every cargo field is unchanged.
        new(Name: "BalancedFleetNoTrimNeeded", Trn: 10, Men: 20),

        // Single-tier overload: chemicals alone (BalanceFleet's own top priority) push cargo space
        // negative; zeroing just Che brings it back non-negative, so the trim stops at the first
        // priority tier with a partial restore (Cr[che] set back to exactly fill the remainder).
        new(Name: "OverloadedSingleTierChemicals", Hkr: 2, Jtn: 1, Che: 500),

        // Multi-tier overload: both Che and Met are overloaded together, forcing BalanceFleet past
        // priority 1 (che, fully zeroed, still negative) to priority 3 (met, partially restored) --
        // the cascading Inc(CargoToRemove) path, not just the first-tier case above.
        new(Name: "OverloadedAcrossChemicalsAndMetalsTiers", Trn: 1, Che: 300, Met: 300),
    ];

    /// <summary>MethodDataSource shape for FleetLogisticsTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<FleetLogisticsCase>> AsDataSource() => All.Select(c => (Func<FleetLogisticsCase>)(() => c));
}
