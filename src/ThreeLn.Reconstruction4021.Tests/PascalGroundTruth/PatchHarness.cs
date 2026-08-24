namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Builds and runs the patch-based ground-truth driver in reference/verify/ — the same steps as that
/// directory's own build.ps1, done from C# so a GoldenFileTests regenerator can call it like any other
/// harness: rebuild a disposable copy of the real reference/DOSAnacreonSource131/*.PAS sources with
/// patches/*.patch applied, then compile and run the named driver against them. See
/// reference/verify/README.md for why this exists: the real, only-minimally-touched UpdateWorld
/// against a hand-assembled Universe^, instead of a per-procedure transcription. Mirrors
/// PascalHarness.CompileAndRun's signature so GoldenFile.Regenerate can use either interchangeably.
/// </summary>
internal static class PatchHarness
{
    /// <summary>
    /// Driver names already compiled in this process — GoldenFileTests.RegenerateAllGoldenFiles calls
    /// CompileAndRun("runworld", ...) once per domain (14 domains as of Phase 2 commit 2e), and every
    /// one of those calls used to rebuild the identical patched/ tree and driver from scratch: the
    /// pristine source and patches/*.patch don't change between them, only the CLI args do. Rebuilding
    /// from scratch each time (delete/copy/git apply/fpc compile) cost ~5s per call measured directly
    /// — 14 redundant rebuilds of one unchanged binary, ~70s wasted in one test method. A set (not just
    /// the last driver built) so alternating between two different driver names — nothing does today,
    /// but nothing rules it out — doesn't thrash: each name is rebuilt at most once per process, not
    /// re-evicted the moment a different name is requested. Caching is safe because nothing else calls
    /// PatchHarness.CompileAndRun (confirmed: RegenerateAllGoldenFiles is its only caller) and
    /// RunCaseMode's own binary is stateless between separate process invocations either way — only
    /// the compiled artifact is being reused, not any process state. The shared patched/ tree (pristine
    /// source + applied patches) is likewise only rebuilt once, the first time any driver is compiled,
    /// since every driver compiles against the same patched source.
    /// </summary>
    private static readonly HashSet<string> _builtDrivers = [];

    public static string CompileAndRun(string driverName, params string[] args)
    {
        var verifyDir = Path.Combine(PascalHarness.RepoRoot, "reference", "verify");
        var outDir = Path.Combine(verifyDir, "patched");
        var exePath = Path.Combine(outDir, driverName + ".exe");

        if (!_builtDrivers.Contains(driverName) || !File.Exists(exePath)) {
            if (_builtDrivers.Count == 0 || !Directory.Exists(outDir)) {
                var pristineDir = Path.Combine(PascalHarness.RepoRoot, "reference", "DOSAnacreonSource131");

                if (Directory.Exists(outDir))
                    Directory.Delete(outDir, recursive: true);
                Directory.CreateDirectory(outDir);

                foreach (var source in Directory.EnumerateFiles(pristineDir, "*.PAS"))
                    File.Copy(source, Path.Combine(outDir, Path.GetFileName(source)));

                var patchesDir = Path.Combine(verifyDir, "patches");
                foreach (var patch in Directory.EnumerateFiles(patchesDir, "*.patch").OrderBy(p => p, StringComparer.Ordinal))
                    PascalHarness.RunProcess(PascalHarness.GitPath, ["apply", "-p1", patch], outDir);

                _builtDrivers.Clear();
            }

            var driverPath = Path.Combine(verifyDir, driverName + ".pas");
            if (!File.Exists(driverPath))
                throw new FileNotFoundException($"Patch-based driver source not found: {driverPath}");
            File.Copy(driverPath, Path.Combine(outDir, driverName + ".pas"), overwrite: true);

            PascalHarness.RunProcess(PascalHarness.FpcPath, ["-Mtp", "-CfSSE2", driverName + ".pas"], outDir);
            _builtDrivers.Add(driverName);
        }

        return PascalHarness.RunProcess(exePath, args, outDir);
    }
}
