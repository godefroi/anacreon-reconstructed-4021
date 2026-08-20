using System.Diagnostics;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Shells out to FreePascal (fpc) to compile and run the harnesses in reference/verify/, computing
/// ground truth live instead of via hand-transcribed/pasted expected values. Backs the [Explicit],
/// [Category("PascalGroundTruth")]-gated tests in this namespace — never invoked by the default
/// `dotnet test` run, since it requires fpc on PATH. Run that category with:
///   dotnet run --treenode-filter "/*/*/*/*[Category=PascalGroundTruth]"
/// </summary>
internal static class PascalHarness
{
    private static readonly Lazy<string?> RepoRootLazy = new(FindRepoRoot);
    private static readonly Lazy<string?> FpcPathLazy = new(TryLocateFpc);

    public static bool IsFpcAvailable => FpcPathLazy.Value is not null;

    /// <summary>Compiles reference/verify/{harnessName}.pas (if not already compiled this run) and
    /// executes it with the given arguments, returning captured stdout.</summary>
    public static string CompileAndRun(string harnessName, params string[] args)
    {
        var fpc = FpcPathLazy.Value
            ?? throw new InvalidOperationException("fpc not found on PATH — install FreePascal to run PascalGroundTruth tests.");
        var repoRoot = RepoRootLazy.Value
            ?? throw new InvalidOperationException("Could not locate the repo root (no reference/verify directory found above the test assembly).");

        var verifyDir = Path.Combine(repoRoot, "reference", "verify");
        var sourcePath = Path.Combine(verifyDir, harnessName + ".pas");
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Harness source not found: {sourcePath}");

        RunProcess(fpc, [sourcePath], verifyDir);

        var exePath = Path.Combine(verifyDir, harnessName + ".exe");
        return RunProcess(exePath, args, verifyDir);
    }

    private static string RunProcess(string fileName, IReadOnlyList<string> args, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName) {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} {string.Join(' ', args)} exited {process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}");

        return stdout;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (Directory.Exists(Path.Combine(dir.FullName, "reference", "verify")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? TryLocateFpc()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe").Split(';');

        foreach (var dir in pathEnv.Split(Path.PathSeparator)) {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var plain = Path.Combine(dir, "fpc");
            if (File.Exists(plain))
                return plain;

            foreach (var ext in pathExt) {
                var candidate = Path.Combine(dir, "fpc" + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
