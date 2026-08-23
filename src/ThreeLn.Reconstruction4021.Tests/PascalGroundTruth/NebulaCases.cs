namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// NEWGAME.PAS:1261-1313 (NebulaeBand/NebulaePatches), relocated verbatim into the patched UPDATE.PAS
/// (Phase 2 commit 2d) and called directly. <see cref="Mode"/> 1 selects NebulaeBand (<see
/// cref="PatchCount"/> unused), 2 selects NebulaePatches. Sizes stay at or under 15 so the harness's
/// SizeOfGalaxy*SizeOfGalaxy grid dump fits Pascal's 255-char String cap under -Mtp (see runworld.pas's
/// own RunNebulaCase doc comment).
///
/// The three band cases exist specifically to exercise NebulaeBand's three XDisp branches (initX at
/// or below Size/4, at or above Size*3/4, and the middle range) — the highest-risk part of this
/// domain to hand-derive wrong, since it involves this port's 0-based Coordinate shifted against
/// Pascal's own 1-based coordinate space used throughout NebulaeBand's/NebulaePatches' arithmetic
/// (GalaxySetup.NebulaeBand/NebulaePatches keep the arithmetic in 1-based space and shift only at the
/// point of painting a cell — see that class's own doc comments).
/// </summary>
public sealed record NebulaCase(string Name, int SizeOfGalaxy, int Mode, int PatchCount, int RngFixedValue) : INamedCase;

internal static class NebulaCases
{
    public static readonly IReadOnlyList<NebulaCase> All = [
        // Size=15: Size/4=3, Size*3/4=11. Rng=0 -> InitX=1 (<=3, left branch).
        new(Name: "BandLeftBranch", SizeOfGalaxy: 15, Mode: 1, PatchCount: 0, RngFixedValue: 0),
        // Rng=5 -> InitX=6 (between 4 and 10, middle branch).
        new(Name: "BandMiddleBranch", SizeOfGalaxy: 15, Mode: 1, PatchCount: 0, RngFixedValue: 5),
        // Rng=14 -> InitX=15 (>=11, right branch).
        new(Name: "BandRightBranch", SizeOfGalaxy: 15, Mode: 1, PatchCount: 0, RngFixedValue: 14),

        // Same inputs as GalaxySetupTests.NebulaePatches_PaintsADiamondPerPatch's own hand-derivation.
        new(Name: "PatchesSingleRng0", SizeOfGalaxy: 10, Mode: 2, PatchCount: 1, RngFixedValue: 0),
        new(Name: "PatchesMultipleRng2", SizeOfGalaxy: 10, Mode: 2, PatchCount: 3, RngFixedValue: 2),
    ];

    public static IEnumerable<Func<NebulaCase>> AsDataSource() => All.Select(c => (Func<NebulaCase>)(() => c));
}
