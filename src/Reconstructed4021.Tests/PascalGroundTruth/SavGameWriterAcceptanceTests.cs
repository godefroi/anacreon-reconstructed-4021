using Reconstructed4021.Core;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;

namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// The real-Pascal acceptance check for <see cref="SavGameWriter"/>: does the unmodified
/// <c>LOADSAVE.PAS</c> <c>LoadGame</c> (via <c>reference/verify/runload.pas</c>, a driver dedicated to
/// this — see its own header comment for why it isn't folded into <c>runworld.pas</c>) actually accept
/// a file this writer produced.
///
/// Differential, not golden-file: this runs the *same* real Pascal checksum function against both the
/// original committed `.SAV` and <see cref="SavGameWriter"/>'s rewrite of the <see cref="Game"/>
/// <see cref="SavGameLoader"/> loads from it, then diffs the two checksum lines directly — no
/// independently-computed C# side to drift out of sync with real Pascal the way
/// <c>ScenarioLoaderGoldenTests</c>' own RNG-position problem forced most fields out of exact-match
/// there (see that class's own doc comment). There's no `Rnd()` call anywhere in `LoadGame`, so an
/// exact match is safe here in a way it wasn't for a `.SCN` load. See `runload.pas`'s own header
/// comment for exactly which fields the checksum covers and why (sorted by empire name, not on-disk
/// slot ordinal — <see cref="SavGameWriter"/> compacts slots, so a real save's ordinal gaps don't
/// survive a round trip even though every empire's own data does).
/// </summary>
public class SavGameWriterAcceptanceTests
{
    private static readonly string[] ReferenceSaveFiles = [
        "INTRO_1.SAV", "INTRO_2.SAV", "GAUNTLET_1.SAV", "IMPERIUM_1.SAV", "AFTERMAT_1.SAV",
        "PRINCES_1.SAV", "FLEET_ORDERS.SAV", "Confront_1.SAV", "Confront_2.SAV",
        "STARGATE_PREP.SAV", "STARGATE_STARTED.SAV", "STARGATE_NEARDONE.SAV", "STARGATE_DONE.SAV",
    ];

    private static string PatchedDir => Path.Combine(PascalHarness.RepoRoot, "reference", "verify", "patched");

    // DependsOn GoldenFileTests, not just RequiresFpc/RequiresGit: PatchHarness.CompileAndRun wipes
    // and rebuilds the shared reference/verify/patched/ directory the first time any driver name is
    // compiled in this process -- running concurrently with GoldenFileTests.RegenerateAllGoldenFiles
    // (a different driver name, "runworld") raced on that same directory and threw a real
    // "being used by another process" IOException the first time this test existed. Ordering after it
    // (TUnit's dependency mechanism, not just a hope about scheduling) guarantees the shared directory
    // is done being built by the time this test's own PatchHarness calls run.
    [Test, RequiresFpc, RequiresGit]
    [DependsOn<GoldenFileTests>(nameof(GoldenFileTests.RegenerateAllGoldenFiles))]
    public async Task RealPascalAcceptsWriterOutput_ForEveryReferenceSave()
    {
        // Build-only invocation first (no args -> runload.pas's own Halt(0) branch): PatchHarness
        // wipes and rebuilds patched/ on the first driver it compiles in this process, so writing our
        // .SAV files into patched/ before this call would just delete them again.
        PatchHarness.CompileAndRun("runload");

        var mismatches = new List<string>();

        foreach (var fileName in ReferenceSaveFiles) {
            var originalBytes = File.ReadAllBytes(Path.Combine(PascalHarness.RepoRoot, "reference", "saves", fileName));
            var checksumA = RunLoadChecksum(originalBytes, "orig.sav");

            var game = new SavGameLoader().LoadGame(originalBytes);
            var rewritten = SavGameWriter.WriteGame(game);
            var checksumB = RunLoadChecksum(rewritten, "rt.sav");

            mismatches.AddRange(Diff(fileName, checksumA, checksumB));
        }

        await Assert.That(mismatches).IsEmpty();
    }

    /// <summary>
    /// Smoke check only (per `docs/ROADMAP.md`'s own save/load scope note): a freshly built
    /// <see cref="ScenarioLoader"/> game has no original `.SAV` file to diff against, so this just
    /// confirms real Pascal accepts the write-back at all (`error=0`) rather than differentially
    /// comparing a checksum.
    /// </summary>
    // Depends on the other test in this class too, not just GoldenFileTests -- both call
    // PatchHarness.CompileAndRun("runload", ...) themselves, and the first fpc invocation for a given
    // driver name isn't safe to race either (see the other test's own doc comment).
    [Test, RequiresFpc, RequiresGit]
    [DependsOn<GoldenFileTests>(nameof(GoldenFileTests.RegenerateAllGoldenFiles))]
    [DependsOn(nameof(RealPascalAcceptsWriterOutput_ForEveryReferenceSave))]
    public async Task RealPascalAcceptsWriterOutput_ForFreshlyCreatedScenario()
    {
        PatchHarness.CompileAndRun("runload");

        var random = new PascalRandom(12345);
        var loader = new ScenarioLoader(new GalaxySetup(random), random);
        var text = File.ReadAllText(Path.Combine(PascalHarness.RepoRoot, "reference", "scenarios", "dos_131", "INTRO.SCN"));
        var players = new[] { new ScenarioLoader.PlayerInfo("test_player_1", "test_pass_1", IsEmpress: false) };
        var game = loader.Load(text, players);

        var bytes = SavGameWriter.WriteGame(game);
        var checksum = RunLoadChecksum(bytes, "fresh.sav");
        var fields = PascalHarness.ParseFields(checksum);

        await Assert.That(fields["error"]).IsEqualTo("0");
    }

    private static string RunLoadChecksum(byte[] savBytes, string shortFileName)
    {
        File.WriteAllBytes(Path.Combine(PatchedDir, shortFileName), savBytes);
        return PatchHarness.CompileAndRun("runload", [shortFileName]).Trim();
    }

    private static List<string> Diff(string caseName, string checksumA, string checksumB)
    {
        var a = PascalHarness.ParseFields(checksumA);
        var b = PascalHarness.ParseFields(checksumB);
        var diffs = new List<string>();

        foreach (var key in a.Keys) {
            if (!b.TryGetValue(key, out var bValue)) {
                diffs.Add($"{caseName}: {key} missing on round-tripped side (was {a[key]})");
            } else if (a[key] != bValue) {
                diffs.Add($"{caseName}: {key} original={a[key]} round-tripped={bValue}");
            }
        }

        foreach (var key in b.Keys.Except(a.Keys)) {
            diffs.Add($"{caseName}: {key} present on round-tripped side only (={b[key]})");
        }

        return diffs;
    }
}
