using TUnit.Core;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Dynamically skips a GoldenFileTests regenerator when fpc isn't on PATH, so these tests can run in
/// the default `dotnet test` suite (refreshing reference/verify/golden/*.golden from a live Pascal run
/// every time, per [[feedback_prefer_patch_based_ground_truth]]'s "cache" framing) instead of needing
/// [Explicit] + manual category selection on machines that happen to have FreePascal installed.
/// </summary>
internal sealed class RequiresFpcAttribute()
    : SkipAttribute("fpc not found on PATH — install FreePascal to run this ground-truth test")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!PascalHarness.IsFpcAvailable);
}

/// <summary>Stack alongside [RequiresFpc] on a patch-based regenerator (reference/verify/)
/// — it also needs git to apply patches/*.patch.</summary>
internal sealed class RequiresGitAttribute()
    : SkipAttribute("git not found on PATH — required to apply reference/verify/patches/*.patch")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!PascalHarness.IsGitAvailable);
}
