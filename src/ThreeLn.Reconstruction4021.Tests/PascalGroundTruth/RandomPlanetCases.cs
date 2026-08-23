using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// NEWGAME.PAS:924-951 (CreateRndPlanet), relocated verbatim into the patched UPDATE.PAS (Phase 2
/// commit 2d) alongside its own SetUpWorld/RndShips/RndCargo/RndDefns dependencies (also relocated
/// verbatim, from NEWGAME.PAS:822-922) and called directly at a fixed (5,5) — location never varies,
/// CreateRndPlanet's own formula doesn't read it. TechLevel spans PreTech (heaviest checkTech
/// zeroing, smallest RndMilTechAdj) through Gate (no zeroing, full RndMilTechAdj) to exercise both
/// ends of the tech-gate table GalaxySetup.CreateRndPlanet shares with SetUpWorld's checkTech path.
/// </summary>
public sealed record RandomPlanetCase(string Name, WorldClass Class, TechLevel Tech, int RngFixedValue) : INamedCase;

internal static class RandomPlanetCases
{
    public static readonly IReadOnlyList<RandomPlanetCase> All = [
        // Same inputs as GalaxySetupTests.CreateRndPlanet_ComputesPopulationMilitaryIndexAndAppliesTechGate's
        // own hand-derivation — cross-checks that hand-derivation against real Pascal.
        new(Name: "EarthLikeWarpRng0", Class: WorldClass.EarthLike, Tech: TechLevel.Warp, RngFixedValue: 0),

        // Almost nothing is unlocked at PreTech (TechDev[PreTech] is small) -- heaviest checkTech
        // zeroing this domain can exercise, plus RndMilTechAdj[PreTech]=0.01, the smallest entry.
        new(Name: "EarthLikePreTechRng0", Class: WorldClass.EarthLike, Tech: TechLevel.PreTech, RngFixedValue: 0),

        // Everything is unlocked by Gate -- no checkTech zeroing at all, RndMilTechAdj[Gate]=1.00.
        new(Name: "ArtificialGateRng0", Class: WorldClass.Artificial, Tech: TechLevel.Gate, RngFixedValue: 0),

        new(Name: "ClassMJumpRng5", Class: WorldClass.ClassM, Tech: TechLevel.Jump, RngFixedValue: 5),
        new(Name: "BarrenBioRng2", Class: WorldClass.Barren, Tech: TechLevel.Bio, RngFixedValue: 2),
    ];

    public static IEnumerable<Func<RandomPlanetCase>> AsDataSource() => All.Select(c => (Func<RandomPlanetCase>)(() => c));
}
