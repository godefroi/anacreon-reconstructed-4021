namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Regenerates every committed golden file (reference/verify/golden/*.golden) from a real
/// FreePascal run of the matching reference/verify/*.pas harness — the source of every expected
/// value the always-on AnnualTickHandler*Tests.MatchesGoldenFile tests assert against. Each method
/// just supplies its domain's harness name, case list, and per-case CLI arg format to
/// GoldenFile.Regenerate, which does the actual compile/run/write/verify work — see there.
/// [Explicit] + [Category("PascalGroundTruth")]: requires fpc on PATH, so these are excluded from
/// the default `dotnet test` run (see PascalHarness). Run one, review the diff, and commit the
/// result whenever its Cases list or its harness's transcription changes.
/// </summary>
public class GoldenFileTests
{
    [Test, Explicit, Category("PascalGroundTruth")]
    public async Task FpcIsAvailable()
    {
        // Fails loudly rather than silently no-op'ing if this category is explicitly requested
        // without FreePascal installed.
        await Assert.That(PascalHarness.IsFpcAvailable)
            .IsTrue()
            .Because("PascalGroundTruth tests require fpc on PATH to compute ground truth");
    }

    [Test, Explicit, Category("PascalGroundTruth")]
    public void RegenerateAmbrosiaGoldenFile() =>
        // RevIndexStart is fixed at 0: UseUpAmbrosia only ever writes to RevIndex, never reads it
        // back within the procedure, so its starting value can't affect the fields under test.
        GoldenFile.Regenerate("ambrosia", AmbrosiaCases.All,
            c => $"{c.HarnessPop},{c.Efficiency},{(int)c.Tech},{(c.StartAddicted ? 1 : 0)},{c.StartAmbrosia},0,{c.RngFixedValue}");

    [Test, Explicit, Category("PascalGroundTruth")]
    public void RegenerateRevolutionGoldenFile() =>
        GoldenFile.Regenerate("revolution", RevolutionCases.All,
            c => $"{c.HarnessPop},{c.Efficiency},{c.RevIndex},0,0,{c.Legions},{c.Ninja},{(int)c.Type}");

    [Test, Explicit, Category("PascalGroundTruth")]
    public void RegenerateProductionGoldenFile() =>
        GoldenFile.Regenerate("production", ProductionCases.All,
            c => $"{(int)c.Class},{(int)c.Type},{c.Population},{c.Efficiency},{(int)c.Tech},{(c.AmbAddict ? 1 : 0)}," +
                 $"{c.IndusBio},{c.IndusChe},{c.IndusMin},{c.IndusSYG},{c.IndusSYJ},{c.IndusSYS},{c.IndusSYT},{c.IndusSup},{c.IndusTri}," +
                 $"{c.CargoMen},{c.CargoNnj},{c.CargoAmb},{c.CargoChe},{c.CargoMet},{c.CargoSup},{c.CargoTri},{c.TrillumReserve}");
}
