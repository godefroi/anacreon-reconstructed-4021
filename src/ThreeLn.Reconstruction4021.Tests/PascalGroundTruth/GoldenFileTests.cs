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
/// </summary>
public class GoldenFileTests
{
    [Test, RequiresFpc, RequiresGit]
    public void RegenerateAllGoldenFiles()
    {
        // Patch-based, not transcribed: runs the real UpdateWorld on the case's raw pre-tick
        // Population (not a hand-derived post-UpdatePopulation value) through runworld.pas's
        // ambrosia domain. See AmbrosiaCases's doc comment for why this drops the old HarnessPop
        // field entirely.
        GoldenFile.Regenerate("ambrosia", AmbrosiaCases.All,
            c => $"{(c.StartAddicted ? 1 : 0)},{c.StartAmbrosia},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "ambrosia", .. args.Skip(1)]));

        // Patch-based, not transcribed: runs the real UpdateWorld on the case's raw pre-tick
        // Population through runworld.pas's revolution domain. See RevolutionCases's doc comment for
        // why this drops the old HarnessPop field and what real-pipeline gap it caught along the way.
        // RngFixedValue is always 0 (matching AnnualTickHandlerRevolutionTests.MatchesGoldenFile's own
        // hardcoded FixedRandom(0) — RevolutionCase has no per-case field for it).
        GoldenFile.Regenerate("revolution", RevolutionCases.All,
            c => $"{c.PlanetPop},{(int)c.Class},{(int)c.Tech},{c.Efficiency},{c.RevIndex},{c.Legions},0",
            args => PatchHarness.CompileAndRun("runworld", ["case", "revolution", .. args.Skip(1)]));

        // Patch-based, not transcribed: runs the real UpdateWorld on the case's raw pre-tick
        // Population (not a hand-derived post-UpdatePopulation value) through runworld.pas's
        // military domain, sharing that driver with techlevel via a domain selector rather than
        // recompiling the whole patched tree a second time. See MilitaryCases's doc comment for
        // why this drops the old HarnessPop field entirely.
        GoldenFile.Regenerate("military", MilitaryCases.All,
            c => $"{c.PlanetPop},{(int)c.Tech},{c.Legions},{(int)c.Type},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "military", .. args.Skip(1)]));

        // Patch-based, not transcribed: runs the real, only-minimally-touched UpdateWorld against a
        // hand-assembled Universe^ (reference/verify/runworld.pas) instead of an isolated
        // transcription of UpdateTechLevel alone, so the ground truth includes the real GetCapital/
        // GetTech lookups and Emp=Indep check rather than TechLevelCase's CapitalTech/IsIndependent
        // standing in for them. See reference/verify/README.md.
        GoldenFile.Regenerate("techlevel", TechLevelCases.All,
            c => $"{(int)c.Tech},{(c.IsIndependent ? 1 : 0)},{(int)c.CapitalTech},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "techlevel", .. args.Skip(1)]));

        // Patch-based, not transcribed: only SupplyLink/SurplusLink's arithmetic (the one part of
        // Commit 4 with real Pascal-transcription risk) needs this — see StarbaseCases's doc comment
        // for why the rest of Commit 4 stays covered by hardcoded tests alone.
        GoldenFile.Regenerate("starbase", StarbaseCases.All,
            c => $"{c.StarbaseChemicals},{c.NeighborChemicals},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "starbase", .. args.Skip(1)]));

        // Patch-based, not transcribed: runs the real UpdateWorld through runworld.pas's production
        // domain instead of the old isolated FullPipeline (ProduceRawMaterial/GetIndustrialDistribution/
        // UpdateIndustry/Production only). See ProductionCases's doc comment for the two harness bugs
        // this caught (Technology-set and ISSP-dial defaults) and the real UpdateDefenses gap it
        // surfaced (not yet ported) that widened the golden comparison's exclusion list.
        GoldenFile.Regenerate("production", ProductionCases.All,
            c => $"{(int)c.Class},{(int)c.Type},{c.Population},{c.Efficiency},{(int)c.Tech},{(c.AmbAddict ? 1 : 0)}," +
                 $"{c.IndusBio},{c.IndusChe},{c.IndusMin},{c.IndusSYG},{c.IndusSYJ},{c.IndusSYS},{c.IndusSYT},{c.IndusSup},{c.IndusTri}," +
                 $"{c.CargoMen},{c.CargoNnj},{c.CargoAmb},{c.CargoChe},{c.CargoMet},{c.CargoSup},{c.CargoTri},{c.TrillumReserve}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "production", .. args.Skip(1)]));

        // Patch-based, not transcribed: runworld.pas's empire domain calls UpdateEmpire directly
        // (restored in UPDATE.PAS.patch — Commit 5a) against a hand-assembled Universe^, exercising
        // the real GetChanceForNewTech lab cascade and GetNewTech's TechDev-membership pick instead of
        // parameterizing them away. See EmpireCases's doc comment for the 26-bit Technology encoding.
        GoldenFile.Regenerate("empire", EmpireCases.All,
            c => $"{(int)c.TechLevel},{c.TechnologyBitmask},{c.RngFixedValue}," +
                 $"{(c.Planet1 is not null ? 1 : 0)},{(int)(c.Planet1?.Type ?? 0)},{(int)(c.Planet1?.Class ?? 0)},{(int)(c.Planet1?.Tech ?? 0)},{c.Planet1?.Efficiency ?? 0}," +
                 $"{(c.Planet2 is not null ? 1 : 0)},{(int)(c.Planet2?.Type ?? 0)},{(int)(c.Planet2?.Class ?? 0)},{(int)(c.Planet2?.Tech ?? 0)},{c.Planet2?.Efficiency ?? 0}," +
                 $"{(c.Starbase is not null ? 1 : 0)},{(int)(c.Starbase?.Type ?? 0)},{(int)(c.Starbase?.Tech ?? 0)},{c.Starbase?.Efficiency ?? 0}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "empire", .. args.Skip(1)]));

        // Patch-based, not transcribed: runworld.pas's construction domain calls UpdateConstruction
        // directly (restored in UPDATE.PAS.patch, Commit 5b — along with ConstructStarbase/
        // ConstructStargate and five small Intrface-only helpers relocated verbatim, mirroring
        // GetIndustrialDistribution's own earlier relocation). See ConstructionCases's doc comment.
        // ConstrTypesOrd is pre-offset (SRM=19..dis=26, TechnologyTypes' own ordinals) since
        // ConstructionType's C# ordinals start at 0.
        GoldenFile.Regenerate("construction", ConstructionCases.All,
            c => $"{19 + (int)c.Building},{c.YearsToCompletion},{(int)c.OwnerTechLevel},{c.RngFixedValue}," +
                 $"{(c.Fleet1 is not null ? 1 : 0)},{c.Fleet1?.Chemicals ?? 0},{c.Fleet1?.Metals ?? 0},{c.Fleet1?.Trillum ?? 0}," +
                 $"{(c.Fleet2 is not null ? 1 : 0)},{c.Fleet2?.Chemicals ?? 0},{c.Fleet2?.Metals ?? 0},{c.Fleet2?.Trillum ?? 0}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "construction", .. args.Skip(1)]));
    }
}
