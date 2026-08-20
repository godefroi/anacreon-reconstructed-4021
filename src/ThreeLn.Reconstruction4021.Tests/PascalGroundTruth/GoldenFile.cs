namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Reads/writes the golden files committed under reference/verify/golden/ — one "case=Name;key=
/// value;..." line per named scenario, each value computed once by a real FreePascal run (an
/// [Explicit]-gated generator test in this namespace) and read back by an always-on test that
/// drives the same scenario through the C# port. This is what makes the always-on assertions trace
/// to the real Pascal source instead of a hand-typed literal that could silently agree with a
/// shared mistake on both sides.
/// </summary>
internal static class GoldenFile
{
    private static string DirectoryPath => Path.Combine(PascalHarness.RepoRoot, "reference", "verify", "golden");

    /// <summary>Reads a committed golden file, keyed by its "case" field.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load(string fileName)
    {
        var path = Path.Combine(DirectoryPath, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Golden file not found: {path} — run the matching [Explicit] PascalGroundTruth generator test to create it.", path);

        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var line in File.ReadAllLines(path)) {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var fields = PascalHarness.ParseFields(line);
            result[fields["case"]] = fields;
        }
        return result;
    }

    /// <summary>Overwrites a golden file with one line per row, each prefixed with its case name.</summary>
    public static void Write(string fileName, IEnumerable<(string Name, string HarnessOutput)> rows)
    {
        Directory.CreateDirectory(DirectoryPath);
        var lines = rows.Select(r => $"case={r.Name};{r.HarnessOutput.Trim()}");
        File.WriteAllLines(Path.Combine(DirectoryPath, fileName), lines);
    }
}
