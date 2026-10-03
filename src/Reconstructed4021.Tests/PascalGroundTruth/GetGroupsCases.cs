namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs shared between the golden-file generator (GoldenFileTests) and
/// FleetGroupConfigurationGroundTruthTests.MatchesGoldenFile. Expected outputs live exclusively in
/// reference/verify/golden/getgroups.golden, computed by driving the real ATTCOMM.PAS GetGroups with
/// the scripted keys below (reference/verify/runattcomm.pas). Each case is a fleet plus a keystroke
/// script; the C# side replays the same script through FleetGroupConfiguration.
///
/// Group 1 is selected at the start. Space and a committed amount both load the selected group and
/// then advance the selection to the next group (ATTCOMM.PAS:911-915), so a script that wants to
/// act on the same group again presses <see cref="GetGroupsKeys.Up"/> first.
/// </summary>
public sealed record GetGroupsCase(
    string Name, string Keys,
    int Fgt = 0, int Hkr = 0, int Jmp = 0, int Jtn = 0, int Pen = 0, int Ssp = 0, int Trn = 0,
    int Men = 0, int Nnj = 0) : INamedCase;

/// <summary>Key characters, as the ordinals ATTCOMM.PAS's GetCharacter sees (EIO.PAS:55-77).</summary>
internal static class GetGroupsKeys
{
    public const char Up = 'È';
    public const char Down = 'Ð';
    public const char Left = 'Ë';
    public const char Right = 'Í';
    public const char Return = '\r';
    public const char Esc = '\u001B';

    /// <summary>Right arrow x6: Fighter to Transport along the type cycle.</summary>
    public const string ToTransport = "ÍÍÍÍÍÍ";
}

internal static class GetGroupsCases
{
    private const string R6 = GetGroupsKeys.ToTransport;
    private const char U = GetGroupsKeys.Up;
    private const char L = GetGroupsKeys.Left;
    private const char Ret = GetGroupsKeys.Return;
    private const char Esc = GetGroupsKeys.Esc;

    public static readonly IReadOnlyList<GetGroupsCase> All = [
        // Baseline with no count change: load every transport, go back to the group, load legions.
        new(Name: "LoadAllTransportsThenLegions", Keys: $"{R6} {U}M{Esc}", Trn: 10, Men: 50),

        // Issue #119: shrinking a loaded transport group. Pascal returns all 50 legions to the pool
        // and the final pass reloads the 2 remaining transports at their own capacity (10).
        new(Name: "ShrinkLoadedTransportGroup", Keys: $"{R6} {U}M-8{Ret}{Esc}", Trn: 10, Men: 50),

        // Issue #119, reverse direction: growing a loaded group. The old 10-legion load is returned,
        // so the final pass reloads 10 transports at 50 instead of skipping the group (Gat != 0).
        new(Name: "GrowLoadedTransportGroup", Keys: $"{R6}2{Ret}{U}M+8{Ret}{Esc}", Trn: 10, Men: 50),

        // Switching a loaded group's type (LoadShips' own Typ<>CurTyp branch) returns the ships and
        // the troops under the old type before loading the new one.
        new(Name: "SwitchLoadedGroupType", Keys: $"{R6} {U}M{L} {Esc}", Trn: 10, Ssp: 3, Men: 50),

        // The final pass tries legions before ninja legions (ATTCOMM.PAS:1055-1068), the opposite of
        // DefaultGroup's order.
        new(Name: "FinalPassPrefersLegions", Keys: $"{R6} {Esc}", Trn: 4, Men: 15, Nnj: 15),

        // Compaction: group 1 holds the transports and group 2 the fighters, so the final pass has to
        // move the fighter group ahead of the transport group, then auto-load the unloaded transports.
        new(Name: "FinalPassCompactsAndLoads", Keys: $"{R6}3{Ret}{GetGroupsKeys.Left}{GetGroupsKeys.Left}{GetGroupsKeys.Left}{GetGroupsKeys.Left}{GetGroupsKeys.Left}{GetGroupsKeys.Left} {Esc}", Trn: 3, Fgt: 5, Men: 100),
    ];

    /// <summary>MethodDataSource shape for FleetGroupConfigurationGroundTruthTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<GetGroupsCase>> AsDataSource() => All.Select(c => (Func<GetGroupsCase>)(() => c));
}
