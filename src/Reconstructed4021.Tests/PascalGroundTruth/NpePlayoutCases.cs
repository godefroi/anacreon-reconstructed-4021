namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Real-Pascal twin of <see cref="Reconstructed4021.Tests.ScenarioPlayoutTests"/>: plays the same two
/// scenarios out to the same 500-year cap with the real Kingdom1/Kingdom2 AI (reference/verify's
/// npeplayout domain, itself a direct, non-interactive replica of ANACREON.PAS:260-290's own all-NPE
/// turn loop calling NPE02.PAS's ImplementKingdom1NPE directly — see runworld.pas's own domain-header
/// comment), to answer whether the C# port's <see cref="LegacyNpe.KingdomTurnHandler"/> behaves worse
/// (less able to eliminate a rival) than the real original. It doesn't — both sides independently
/// produced zero eliminations across every scenario/persona/seed combination tried.
///
/// Not a golden-file exact-match test: this domain seeds Pascal's own GroundTruthSeed, a generator
/// deliberately unrelated to the C# side's System.Random (see reference/verify/README.md's own "The
/// ground-truth RNG is a generator this project owns" section) — there is no shared seed space to
/// assert byte-identical results against, only comparable aggregate behavior (elimination counts,
/// growth magnitude) between two independently-driven runs. [Explicit] for the same reason
/// ScenarioPlayoutTests.cs is: each case is a real, possibly-500-year playout, not a unit test.
/// </summary>
public class NpePlayoutCases
{
    private const int SeedCount = 10;
    private const int Years = 500;

    // NotInParallel: PatchHarness.CompileAndRun wipes and rebuilds the shared reference/verify/patched/
    // directory the first time any driver is compiled in a process -- TUnit runs this method's four
    // [Arguments] cases concurrently by default, which raced on that directory (a real
    // UnauthorizedAccessException/"being used by another process" failure, not theoretical) the first
    // time this test existed. Same root cause SavGameWriterAcceptanceTests' own [DependsOn] comment
    // documents; NotInParallel is the simpler fix here since these four cases only need to serialize
    // against each other, not against a specific other test class.
    [Test, Explicit, RequiresFpc, RequiresGit, NotInParallel]
    [Arguments("INTRO.SCN", 3, 1)]
    [Arguments("INTRO.SCN", 3, 2)]
    [Arguments("EASTWEST.SCN", 2, 1)]
    [Arguments("EASTWEST.SCN", 2, 2)]
    public async Task AllKingdomPlayout(string fileName, int numPlayers, int persona)
    {
        var path = System.IO.Path.Combine("..", "..", "scenarios", "dos_131", fileName);

        Console.WriteLine($"{fileName} (numPlayers={numPlayers}, persona={persona}), {SeedCount} seeds, year cap {Years}:");
        var anyEliminated = false;
        for (var seed = 0; seed < SeedCount; seed++) {
            var line = PatchHarness.CompileAndRun("runworld",
                "case", "npeplayout", $"{path},{seed},{numPlayers},{persona},{Years}");
            var fields = PascalHarness.ParseFields(line);
            var eliminated = int.Parse(fields["eliminated"]);
            anyEliminated |= eliminated > 0;
            Console.WriteLine($"  seed {seed}: {line.Trim()}");
        }

        // Not an assertion the C# port must match forever — a real change to Kingdom AI balance could
        // legitimately flip this. It's the comparison point: as of this test's own writing, the real
        // Pascal Kingdom AI also never eliminates a rival within 500 years, matching what
        // ScenarioPlayoutTests.cs already found for the C# port across the same scenario/persona grid.
        await Assert.That(anyEliminated).IsFalse();
    }
}
