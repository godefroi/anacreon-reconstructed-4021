namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Builds and runs the patch-based ground-truth driver in reference/verify/patch-based/ — the same
/// steps as that directory's own build.ps1, done from C# so a GoldenFileTests regenerator can call it
/// like any other harness: rebuild a disposable copy of the real reference/DOSAnacreonSource131/*.PAS
/// sources with patches/*.patch applied, then compile and run the named driver against them. See
/// reference/verify/patch-based/README.md for why this exists: the real, only-minimally-touched
/// UpdateWorld against a hand-assembled Universe^, instead of a per-procedure transcription. Mirrors
/// PascalHarness.CompileAndRun's signature so GoldenFile.Regenerate can use either interchangeably.
/// </summary>
internal static class PatchHarness
{
    public static string CompileAndRun(string driverName, params string[] args)
    {
        var patchBasedDir = Path.Combine(PascalHarness.RepoRoot, "reference", "verify", "patch-based");
        var pristineDir = Path.Combine(PascalHarness.RepoRoot, "reference", "DOSAnacreonSource131");
        var outDir = Path.Combine(patchBasedDir, "pascal");

        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);

        foreach (var source in Directory.EnumerateFiles(pristineDir, "*.PAS"))
            File.Copy(source, Path.Combine(outDir, Path.GetFileName(source)));

        var driverPath = Path.Combine(patchBasedDir, driverName + ".pas");
        if (!File.Exists(driverPath))
            throw new FileNotFoundException($"Patch-based driver source not found: {driverPath}");
        File.Copy(driverPath, Path.Combine(outDir, driverName + ".pas"));

        var patchesDir = Path.Combine(patchBasedDir, "patches");
        foreach (var patch in Directory.EnumerateFiles(patchesDir, "*.patch").OrderBy(p => p, StringComparer.Ordinal))
            PascalHarness.RunProcess("git", ["apply", "-p1", patch], outDir);

        PascalHarness.RunProcess(PascalHarness.FpcPath, ["-Mtp", driverName + ".pas"], outDir);

        var exePath = Path.Combine(outDir, driverName + ".exe");
        return PascalHarness.RunProcess(exePath, args, outDir);
    }
}
