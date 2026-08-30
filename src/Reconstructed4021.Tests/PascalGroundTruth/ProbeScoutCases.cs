namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// ProbeScout (INTRFACE.PAS:1289-1344), relocated verbatim into the patched UPDATE.PAS (Intrface's
/// own IMPLEMENTATION USES pulls in Fleet/Orders/NPE — confirmed directly, not the whole unit's real
/// blast radius this codebase stays out of elsewhere — see UPDATE.PAS.patch's own comment). Called
/// directly (reference/verify/runworld.pas's probescout domain) against a hand-assembled Universe^:
/// one planet at the probe's destination with a configurable owner/legions/already-scouted state, and
/// one at the very next ring cell in Pascal's fixed offset order, used purely as a "did the scan
/// continue past the destination" signal.
///
/// Covers the arithmetic this domain exists to cross-check: ISqrt(Cargo[men]) and the
/// Rnd(1,100)&lt;ChanceToDestroy threshold, plus its Exit-before-ScoutObject sequencing (a destroyed
/// probe never gets to mark its own destroyer as scouted). Ring ordering/early-exit control flow
/// itself is covered by VisibilityHandlerProbeTests' own hardcoded (fixed-Random) cases — there's no
/// separate Pascal formula to cross-check there, just C#-side dispatch order.
/// </summary>
public sealed record ProbeScoutCase(string Name, bool DestOwnedByIndependent, int DestLegions, bool DestAlreadyScouted, int RngFixedValue) : INamedCase;

internal static class ProbeScoutCases
{
    public static readonly IReadOnlyList<ProbeScoutCase> All = [
        // ISqrt(0)=0 -> Rnd(1,100) is always >=1, never <0 -- never destroyed regardless of roll.
        new(Name: "NoLegionsNeverDestroys", DestOwnedByIndependent: false, DestLegions: 0, DestAlreadyScouted: false, RngFixedValue: 0),

        // ISqrt(100)=10, RngFixedValue=0 -> Rnd(1,100)=1, 1<10 -> destroyed.
        new(Name: "HighLegionsDestroysOnLowRoll", DestOwnedByIndependent: false, DestLegions: 100, DestAlreadyScouted: false, RngFixedValue: 0),

        // Same legions/chance, but RngFixedValue=99 -> Rnd(1,100)=100, not <10 -> survives.
        new(Name: "HighLegionsSurvivesOnHighRoll", DestOwnedByIndependent: false, DestLegions: 100, DestAlreadyScouted: false, RngFixedValue: 99),

        // OtherEmp<>Indep is false, so the destroy branch never runs regardless of legions/roll.
        new(Name: "IndependentNeverDestroysDespiteLegions", DestOwnedByIndependent: true, DestLegions: 100, DestAlreadyScouted: false, RngFixedValue: 0),

        // NOT Scouted(Emp,Obj) is false, so the destroy branch never runs even though the roll would
        // otherwise succeed.
        new(Name: "AlreadyScoutedNeverRolls", DestOwnedByIndependent: false, DestLegions: 100, DestAlreadyScouted: true, RngFixedValue: 0),
    ];

    public static IEnumerable<Func<ProbeScoutCase>> AsDataSource() => All.Select(c => (Func<ProbeScoutCase>)(() => c));
}
