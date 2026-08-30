namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Loads a real committed <c>reference/scenarios/dos_131/*.SCN</c>
/// file through both the C# <c>ScenarioLoader</c> and the real, patched Pascal <c>RunScenarioCase</c>
/// (NEWGAME.PAS's own header-parse + command-dispatch loop, reimplemented fresh since it's saturated
/// with dead DOS UI — see UPDATE.PAS's own PATCH comment at the relocation), seeded with the same
/// <see cref="PascalRandom"/>/<c>RandSeed</c> value on both sides so <c>CREATERANDOMWORLDS</c>'
/// collision-retry loop — unreachable under every other domain's <c>ForcedRandomValue</c> convention —
/// finally gets exercised against a real, non-degenerate RNG sequence. <c>RunScenarioCase</c> emits an
/// aggregate checksum over the whole loaded Universe^, but <c>ScenarioLoaderGoldenTests.MatchesGoldenFile</c>
/// only asserts a subset of it — see that class's own doc comment for why every RNG-derived field, not
/// just the obviously-randomized ones, had to be dropped from exact-match comparison.
///
/// <c>dos_131</c> has 12 files; 11 are covered here. <c>PRINCES.SCN</c> is deliberately excluded: its
/// first <c>CREATESTARBASE</c> command has one extra integer field that matches neither this 1.31
/// source tree's own <c>CreateBase</c> (NEWGAME.PAS:1027-1098) nor the 2.0 source tree's — confirmed
/// directly against both, not assumed — so the file itself carries a real authoring bug. Confirmed
/// against the real thing, not just source: the user independently ran this exact file through the
/// genuine pristine DOS 1.31 binary and hit the identical "Unknown command 3500" failure this port's
/// own relocated Pascal harness produces, after empire creation, in the same place — this isn't a
/// desync introduced by this port at all, real 1.31 chokes on this file too. (This lines up with a
/// suspicion <c>reference/verify/README.md</c> already raised, now confirmed.) Not a coverage gap: a
/// file the real game itself can't load
/// isn't a meaningful ground-truth fixture for a port that mirrors that same loader's behavior.
/// <c>AWAKEN.SCN</c>, still included below, carries a milder version of the same category of defect
/// — it creates more planets than <c>TYPES.PAS</c>'s <c>MaxNoOfPlanets</c> allows, silently
/// corrupting two starbases' stats via an out-of-bounds array write real DOS Turbo Pascal would
/// reproduce too (range checking is off by default) — see <c>ScenarioLoaderGoldenTests</c>' own doc
/// comment on <c>sumstarbaseeff</c> for the full trace.
///
/// Player identity is a fixed "PlayerN"/"pwN"/not-an-empress convention on both sides — RunScenarioCase
/// hard-codes the identical scheme (a name string can't round-trip through this domain's otherwise
/// all-integer CLI argument), so both sides agree without threading extra fields through.
/// </summary>
public sealed record ScenarioCase(string Name, string FileName, uint Seed, int NumPlayers) : INamedCase;

internal static class ScenarioCases
{
    public static readonly IReadOnlyList<ScenarioCase> All = [
        new(Name: "Aftermat", FileName: "AFTERMAT.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Arronax", FileName: "ARRONAX.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Awaken", FileName: "AWAKEN.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Eastwest", FileName: "EASTWEST.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Fences", FileName: "FENCES.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Gauntlet", FileName: "GAUNTLET.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Imperium", FileName: "IMPERIUM.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Intro", FileName: "INTRO.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Jakarta", FileName: "JAKARTA.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Peripher", FileName: "PERIPHER.SCN", Seed: 12345, NumPlayers: 4),
        new(Name: "Trinity", FileName: "TRINITY.SCN", Seed: 12345, NumPlayers: 4),
    ];

    public static IEnumerable<Func<ScenarioCase>> AsDataSource() => All.Select(c => (Func<ScenarioCase>)(() => c));
}
