namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Regenerates reference/verify/golden/ambrosia.golden from a real FreePascal run of
/// reference/verify/ambrosia.pas — the source of every expected value
/// AnnualTickHandlerAmbrosiaTests.MatchesGoldenFile asserts against. [Explicit] +
/// [Category("PascalGroundTruth")]: requires fpc on PATH, so it's excluded from the default
/// `dotnet test` run (see PascalHarness). Run it, review the diff, and commit the result whenever
/// AmbrosiaCases.All or ambrosia.pas's transcription changes.
/// </summary>
public class AmbrosiaGoldenFileTests
{
    [Test, Explicit, Category("PascalGroundTruth")]
    public async Task RegenerateAmbrosiaGoldenFile()
    {
        var rows = new List<(string Name, string HarnessOutput)>();
        foreach (var c in AmbrosiaCases.All) {
            // RevIndexStart is fixed at 0 for every case: UseUpAmbrosia only ever writes to
            // RevIndex, never reads it back within the procedure, so its starting value can't
            // affect Population/Efficiency/TechLevel/Ambrosia/Addicted — the fields under test.
            var arg = $"{c.HarnessPop},{c.Efficiency},{(int)c.Tech},{(c.StartAddicted ? 1 : 0)},{c.StartAmbrosia},0,{c.RngFixedValue}";
            var output = PascalHarness.CompileAndRun("ambrosia", "case", arg);
            rows.Add((c.Name, output));
        }

        GoldenFile.Write("ambrosia.golden", rows);

        var reloaded = GoldenFile.Load("ambrosia.golden");
        await Assert.That(reloaded.Count).IsEqualTo(AmbrosiaCases.All.Count)
            .Because("one golden row per case, and every row must parse back with a distinct case name");
    }
}
