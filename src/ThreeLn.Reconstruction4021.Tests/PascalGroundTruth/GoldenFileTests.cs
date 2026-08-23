namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Regenerates every committed golden file (reference/verify/golden/*.golden) from a real
/// FreePascal run, one domain after another in a single test — not one test per domain — so there's
/// exactly one thing for [DependsOn] to key off of and no risk of concurrent fpc invocations racing on
/// Common's shared build output (.ppu/.o) the way several parallel-scheduled tests would.
///
/// Runs in the default `dotnet test` suite, not gated behind [Explicit]: each committed .golden file
/// is a cache, refreshed here once per test run when fpc and git are both on PATH
/// (RequiresFpcAttribute/RequiresGitAttribute dynamically skip this test otherwise, with a clear
/// reason — see PascalHarness/PatchHarness). Every AnnualTickHandler*Tests.MatchesGoldenFile test
/// depends on this one via [DependsOn] alone — TUnit skips a test whose dependency was itself skipped
/// ("Skipped due to failed dependencies", confirmed empirically), so a run either verifies fully
/// against live Pascal or visibly skips that coverage, never silently trusting a possibly-stale
/// committed snapshot. No need to repeat RequiresFpc/RequiresGit on every dependent — that would just
/// be two independent claims about the same missing tool that could drift apart.
///
/// Always regenerates unconditionally, every run — no change-detection (hashing patches, checking git
/// log, etc.) to skip regeneration when nothing changed. Not worth the complexity yet.
///
/// Adding a new domain: give `runworld.pas` a new `case &lt;domain&gt;` branch (see its own header
/// comment for the CLI convention), add a `&lt;Domain&gt;Cases.cs` with the case record and a doc
/// comment explaining that domain's own rationale, then one `GoldenFile.Regenerate(...)` call below.
/// The format-string lambda is the only place the C#-side field order has to match `runworld.pas`'s
/// parser field-for-field — keep the two in view together when changing either. See
/// `docs/ROADMAP.md`'s "Ground-truth harness generation" section and `reference/verify/README.md`
/// for the full mechanics and investigation history; domain-specific rationale belongs in each
/// `&lt;Domain&gt;Cases.cs`, not repeated here.
/// </summary>
public class GoldenFileTests
{
    [Test, RequiresFpc, RequiresGit]
    public void RegenerateAllGoldenFiles()
    {
        GoldenFile.Regenerate("ambrosia", AmbrosiaCases.All,
            c => $"{(c.StartAddicted ? 1 : 0)},{c.StartAmbrosia},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "ambrosia", .. args.Skip(1)]));

        // RngFixedValue is always 0 here, matching AnnualTickHandlerRevolutionTests.MatchesGoldenFile's
        // own hardcoded FixedRandom(0) — RevolutionCase has no per-case field for it.
        GoldenFile.Regenerate("revolution", RevolutionCases.All,
            c => $"{c.PlanetPop},{(int)c.Class},{(int)c.Tech},{c.Efficiency},{c.RevIndex},{c.Legions},0",
            args => PatchHarness.CompileAndRun("runworld", ["case", "revolution", .. args.Skip(1)]));

        GoldenFile.Regenerate("military", MilitaryCases.All,
            c => $"{c.PlanetPop},{(int)c.Tech},{c.Legions},{(int)c.Type},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "military", .. args.Skip(1)]));

        GoldenFile.Regenerate("techlevel", TechLevelCases.All,
            c => $"{(int)c.Tech},{(c.IsIndependent ? 1 : 0)},{(int)c.CapitalTech},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "techlevel", .. args.Skip(1)]));

        GoldenFile.Regenerate("starbase", StarbaseCases.All,
            c => $"{c.StarbaseChemicals},{c.NeighborChemicals},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "starbase", .. args.Skip(1)]));

        GoldenFile.Regenerate("production", ProductionCases.All,
            c => $"{(int)c.Class},{(int)c.Type},{c.Population},{c.Efficiency},{(int)c.Tech},{(c.AmbAddict ? 1 : 0)}," +
                 $"{c.IndusBio},{c.IndusChe},{c.IndusMin},{c.IndusSYG},{c.IndusSYJ},{c.IndusSYS},{c.IndusSYT},{c.IndusSup},{c.IndusTri}," +
                 $"{c.CargoMen},{c.CargoNnj},{c.CargoAmb},{c.CargoChe},{c.CargoMet},{c.CargoSup},{c.CargoTri},{c.TrillumReserve}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "production", .. args.Skip(1)]));

        GoldenFile.Regenerate("empire", EmpireCases.All,
            c => $"{(int)c.TechLevel},{c.TechnologyBitmask},{c.RngFixedValue}," +
                 $"{(c.Planet1 is not null ? 1 : 0)},{(int)(c.Planet1?.Type ?? 0)},{(int)(c.Planet1?.Class ?? 0)},{(int)(c.Planet1?.Tech ?? 0)},{c.Planet1?.Efficiency ?? 0}," +
                 $"{(c.Planet2 is not null ? 1 : 0)},{(int)(c.Planet2?.Type ?? 0)},{(int)(c.Planet2?.Class ?? 0)},{(int)(c.Planet2?.Tech ?? 0)},{c.Planet2?.Efficiency ?? 0}," +
                 $"{(c.Starbase is not null ? 1 : 0)},{(int)(c.Starbase?.Type ?? 0)},{(int)(c.Starbase?.Tech ?? 0)},{c.Starbase?.Efficiency ?? 0}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "empire", .. args.Skip(1)]));

        // ConstrTypesOrd is pre-offset (SRM=19..dis=26, TechnologyTypes' own ordinals) since
        // ConstructionType's C# ordinals start at 0.
        GoldenFile.Regenerate("construction", ConstructionCases.All,
            c => $"{19 + (int)c.Building},{c.YearsToCompletion},{(int)c.OwnerTechLevel},{c.RngFixedValue}," +
                 $"{(c.Fleet1 is not null ? 1 : 0)},{c.Fleet1?.Chemicals ?? 0},{c.Fleet1?.Metals ?? 0},{c.Fleet1?.Trillum ?? 0}," +
                 $"{(c.Fleet2 is not null ? 1 : 0)},{c.Fleet2?.Chemicals ?? 0},{c.Fleet2?.Metals ?? 0},{c.Fleet2?.Trillum ?? 0}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "construction", .. args.Skip(1)]));

        GoldenFile.Regenerate("empirecreate", EmpireFactoryCases.All,
            c => $"{(int)c.TechLevel},{c.ExtraTechsMask}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "empirecreate", .. args.Skip(1)]));

        GoldenFile.Regenerate("trillumreserves", TrillumReservesCases.All,
            c => $"{(int)c.Class},{c.RegionReserves},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "trillumreserves", .. args.Skip(1)]));

        GoldenFile.Regenerate("randomplanet", RandomPlanetCases.All,
            c => $"{(int)c.Class},{(int)c.Tech},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "randomplanet", .. args.Skip(1)]));

        GoldenFile.Regenerate("nebula", NebulaCases.All,
            c => $"{c.SizeOfGalaxy},{c.Mode},{c.PatchCount},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "nebula", .. args.Skip(1)]));
    }
}
