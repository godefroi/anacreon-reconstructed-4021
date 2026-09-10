namespace Reconstructed4021.Tests;

/// <summary>See <see cref="GroundTruthRandom"/>'s own doc comment for what this proves and why it exists.</summary>
public class GroundTruthRandomTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.GroundTruthRngCases), nameof(PascalGroundTruth.GroundTruthRngCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.GroundTruthRngCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("groundtruthrng.golden");
        var random = new GroundTruthRandom(c.Seed);

        var values = string.Join(',', Enumerable.Range(0, c.Count).Select(_ => random.Next(c.Range)));

        await Assert.That(values).IsEqualTo(golden[c.Name]["values"]);
    }
}
