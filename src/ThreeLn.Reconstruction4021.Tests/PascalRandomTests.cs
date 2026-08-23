namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>See <see cref="PascalRandom"/>'s own doc comment for what this proves and why it exists.</summary>
public class PascalRandomTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.RngCases), nameof(PascalGroundTruth.RngCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.RngCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("rng.golden");
        var random = new PascalRandom(c.Seed);

        var values = string.Join(',', Enumerable.Range(0, c.Count).Select(_ => random.Next(c.Range)));

        await Assert.That(values).IsEqualTo(golden[c.Name]["values"]);
    }
}
