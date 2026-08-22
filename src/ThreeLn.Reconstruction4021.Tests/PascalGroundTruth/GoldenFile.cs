namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>Implemented by every domain's case record (AmbrosiaCase, RevolutionCase, ...) so
/// GoldenFile.Regenerate can key rows without each domain repeating that plumbing.</summary>
internal interface INamedCase
{
    string Name { get; }
}

/// <summary>
/// Reads/writes the golden files committed under reference/verify/golden/ — one "case=Name;key=
/// value;..." line per named scenario. Each committed file is a cache: a GoldenFileTests regenerator
/// refreshes it from a real FreePascal run once per test run when the required tools are available
/// (dynamically skipping itself otherwise, see RequiresFpcAttribute/RequiresGitAttribute), and a
/// separate always-on test reads whatever is on disk — freshly regenerated this run, or the
/// last-committed snapshot — to drive the same scenario through the C# port. This is what makes those
/// assertions trace to the real Pascal source instead of a hand-typed literal that could silently
/// agree with a shared mistake on both sides.
/// </summary>
internal static class GoldenFile
{
    /// <summary>Runs <paramref name="harnessName"/> in "case" mode with one arg per case (via <paramref name="argFormatter"/>), writes the
    /// result to reference/verify/golden/{harnessName}.golden keyed by each case's Name, and
    /// verifies the file round-trips. Shared by every domain's regenerator test in GoldenFileTests —
    /// only the harness name, case list, and per-case arg format differ between domains. runHarness
    /// defaults to PascalHarness.CompileAndRun(harnessName, args) (the per-procedure transcription
    /// pattern); pass PatchHarness.CompileAndRun for a domain whose ground truth instead comes from
    /// the real, patched UpdateWorld (see reference/verify/patch-based/README.md).</summary>
    public static void Regenerate<TCase>(string harnessName, IReadOnlyList<TCase> cases, Func<TCase, string> argFormatter,
        Func<string[], string>? runHarness = null)
        where TCase : INamedCase
    {
        runHarness ??= args => PascalHarness.CompileAndRun(harnessName, args);
        var args = cases.Select(argFormatter).Prepend("case").ToArray();
        var output = runHarness(args);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != cases.Count)
            throw new InvalidOperationException(
                $"{harnessName}: expected one output line per case ({cases.Count}), got {lines.Length}.\n{output}");

        var fileName = harnessName + ".golden";
        Write(fileName, cases.Zip(lines, (c, line) => (c.Name, HarnessOutput: line)));

        var reloaded = Load(fileName);
        if (reloaded.Count != cases.Count)
            throw new InvalidOperationException(
                $"{fileName}: wrote {cases.Count} rows but read back {reloaded.Count} — case names must be unique.");
    }

    private static string DirectoryPath => Path.Combine(PascalHarness.RepoRoot, "reference", "verify", "golden");

    /// <summary>Reads a committed golden file, keyed by its "case" field.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Golden file not found: {path} — commit one from a machine with fpc (and, for a patch-based " +
                "domain, git) on PATH by running the matching GoldenFileTests regenerator.", path);

        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var line in File.ReadAllLines(path)) {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var fields = PascalHarness.ParseFields(line);
            result[fields["case"]] = fields;
        }
        return result;
    }

    /// <summary>Overwrites a golden file with one line per row, each prefixed with its case name.
    /// Writes to a temp file and moves it into place so anything reading the same file concurrently
    /// sees either the old or the new content, never a torn write — belt-and-suspenders alongside
    /// [DependsOn] (see GoldenFileTests), which already orders every MatchesGoldenFile test after
    /// RegenerateAllGoldenFiles.</summary>
    public static void Write(string fileName, IEnumerable<(string Name, string HarnessOutput)> rows)
    {
        Directory.CreateDirectory(DirectoryPath);
        var lines = rows.Select(r => $"case={r.Name};{r.HarnessOutput.Trim()}");
        var path = Path.Combine(DirectoryPath, fileName);
        var tempPath = path + ".tmp";
        File.WriteAllLines(tempPath, lines);
        File.Move(tempPath, path, overwrite: true);
    }
}
