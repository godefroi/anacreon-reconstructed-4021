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
        // RevIndexStart is fixed at 0: UseUpAmbrosia only ever writes to RevIndex, never reads it
        // back within the procedure, so its starting value can't affect the fields under test.
        GoldenFile.Regenerate("ambrosia", AmbrosiaCases.All,
            c => $"{c.HarnessPop},{c.Efficiency},{(int)c.Tech},{(c.StartAddicted ? 1 : 0)},{c.StartAmbrosia},0,{c.RngFixedValue}");

        GoldenFile.Regenerate("revolution", RevolutionCases.All,
            c => $"{c.HarnessPop},{c.Efficiency},{c.RevIndex},0,0,{c.Legions},{c.Ninja},{(int)c.Type}");

        // Patch-based, not transcribed: runs the real UpdateWorld on the case's raw pre-tick
        // Population (not a hand-derived post-UpdatePopulation value) through runworld.pas's
        // military domain, sharing that driver with techlevel via a domain selector rather than
        // recompiling the whole patched tree a second time. See MilitaryCases's doc comment for
        // why this drops the old HarnessPop field entirely.
        GoldenFile.Regenerate("military", MilitaryCases.All,
            c => $"{c.PlanetPop},{(int)c.Tech},{c.Legions},{(int)c.Type},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "military", .. args.Skip(1)]));

        // Patch-based, not transcribed: runs the real, only-minimally-touched UpdateWorld against a
        // hand-assembled Universe^ (reference/verify/patch-based/runworld.pas) instead of an isolated
        // transcription of UpdateTechLevel alone, so the ground truth includes the real GetCapital/
        // GetTech lookups and Emp=Indep check rather than TechLevelCase's CapitalTech/IsIndependent
        // standing in for them. See reference/verify/patch-based/README.md.
        GoldenFile.Regenerate("techlevel", TechLevelCases.All,
            c => $"{(int)c.Tech},{(c.IsIndependent ? 1 : 0)},{(int)c.CapitalTech},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "techlevel", .. args.Skip(1)]));

        // Patch-based, not transcribed: only SupplyLink/SurplusLink's arithmetic (the one part of
        // Commit 4 with real Pascal-transcription risk) needs this — see StarbaseCases's doc comment
        // for why the rest of Commit 4 stays covered by hardcoded tests alone.
        GoldenFile.Regenerate("starbase", StarbaseCases.All,
            c => $"{c.StarbaseChemicals},{c.NeighborChemicals},{c.RngFixedValue}",
            args => PatchHarness.CompileAndRun("runworld", ["case", "starbase", .. args.Skip(1)]));

        GoldenFile.Regenerate("production", ProductionCases.All,
            c => $"{(int)c.Class},{(int)c.Type},{c.Population},{c.Efficiency},{(int)c.Tech},{(c.AmbAddict ? 1 : 0)}," +
                 $"{c.IndusBio},{c.IndusChe},{c.IndusMin},{c.IndusSYG},{c.IndusSYJ},{c.IndusSYS},{c.IndusSYT},{c.IndusSup},{c.IndusTri}," +
                 $"{c.CargoMen},{c.CargoNnj},{c.CargoAmb},{c.CargoChe},{c.CargoMet},{c.CargoSup},{c.CargoTri},{c.TrillumReserve}");
    }
}
