using System.Diagnostics;

namespace ThreeLn.Reconstruction4021.Tests.PascalGroundTruth;

/// <summary>
/// Shells out to FreePascal (fpc) to compile and run the harnesses in reference/verify/, computing
/// ground truth from the real Pascal source instead of via hand-transcribed/pasted expected values.
/// Some tests in this namespace assert directly against a live harness run; others (see GoldenFile)
/// use it to regenerate a committed golden file that the always-on suite reads from. GoldenFileTests'
/// regenerator tests run in the default `dotnet test` suite and dynamically skip themselves (see
/// RequiresFpcAttribute/RequiresGitAttribute) when fpc — or, for the patch-based lane, git — isn't on
/// PATH, rather than needing [Explicit] + manual category selection to opt into fresh ground truth.
/// </summary>
internal static class PascalHarness
{
    private static readonly Lazy<string?> RepoRootLazy = new(FindRepoRoot);
    private static readonly Lazy<string?> FpcPathLazy = new(() => TryLocateOnPath("fpc"));
    private static readonly Lazy<string?> GitPathLazy = new(() => TryLocateOnPath("git"));

    public static bool IsFpcAvailable => FpcPathLazy.Value is not null;
    public static bool IsGitAvailable => GitPathLazy.Value is not null;

    /// <summary>Resolved fpc path, shared with PatchHarness (which compiles a different driver in a
    /// different directory, but needs the same fpc binary).</summary>
    internal static string FpcPath => FpcPathLazy.Value
        ?? throw new InvalidOperationException("fpc not found on PATH — install FreePascal to run PascalGroundTruth tests.");

    /// <summary>Resolved git path, needed only by PatchHarness (to apply patches/*.patch) — exposed
    /// here since it shares TryLocateOnPath with FpcPath rather than duplicating the PATH search.</summary>
    internal static string GitPath => GitPathLazy.Value
        ?? throw new InvalidOperationException("git not found on PATH — required for the patch-based ground-truth lane.");

    /// <summary>Repo root (the directory containing reference/verify), for locating harness sources
    /// and the golden files under reference/verify/golden/. Throws if it can't be found — every
    /// caller needs it to do anything useful, so there's no value in returning null here too.</summary>
    public static string RepoRoot => RepoRootLazy.Value
        ?? throw new InvalidOperationException("Could not locate the repo root (no reference/verify directory found above the test assembly).");

    /// <summary>Compiles reference/verify/{harnessName}.pas (if not already compiled this run) and
    /// executes it with the given arguments, returning captured stdout.</summary>
    public static string CompileAndRun(string harnessName, params string[] args)
    {
        var verifyDir = Path.Combine(RepoRoot, "reference", "verify");
        var sourcePath = Path.Combine(verifyDir, harnessName + ".pas");
        if (!File.Exists(sourcePath)) {
            throw new FileNotFoundException($"Harness source not found: {sourcePath}");
        }

        RunProcess(FpcPath, [sourcePath], verifyDir);

        var exePath = Path.Combine(verifyDir, harnessName + ".exe");
        return RunProcess(exePath, args, verifyDir);
    }

    /// <summary>Parses one "key=value;key=value;..." line — the wire format every harness in this
    /// directory prints, and the format golden files use verbatim (see GoldenFile).</summary>
    public static IReadOnlyDictionary<string, string> ParseFields(string line)
    {
        var fields = new Dictionary<string, string>();
        foreach (var part in line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var eq = part.IndexOf('=');
            if (eq < 0) {
                continue;
            }
            fields[part[..eq]] = part[(eq + 1)..];
        }
        return fields;
    }

    /// <summary>
    /// Shared with PatchHarness, which shells out to git and fpc from a different working directory
    /// but needs the same process-running/error-reporting behavior.
    ///
    /// Reads stdout and stderr concurrently, not sequentially — reading one stream fully with
    /// ReadToEnd() before even starting the other is the classic .NET child-process deadlock: it
    /// blocks until the child closes that pipe (normally, by exiting), but a child that fills its
    /// *other* stream's OS pipe buffer first (nobody is draining it yet) blocks on that write and
    /// never reaches exit — so the never-touched stream's buffer filling is what wedges the
    /// exits-eventually stream too. Real, not hypothetical: a since-removed debug WriteLn(StdErr,...)
    /// added while investigating Phase 2 commit 2e's own golden-file test — one line per planet across
    /// 11 real scenario files in one batched invocation — reproduced multi-minute hangs here before
    /// being traced back to this exact pattern.
    /// </summary>
    internal static string RunProcess(string fileName, IReadOnlyList<string> args, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName) {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WaitAll(stdoutTask, stderrTask);
        process.WaitForExit();

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;
        if (process.ExitCode != 0) {
            throw new InvalidOperationException($"""
                {fileName} {string.Join(' ', args)} exited {process.ExitCode}
                stdout:
                {stdout}
                stderr:
                {stderr}
                """);
        }

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

    private static string? TryLocateOnPath(string toolName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathExt = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".exe").Split(';');

        foreach (var dir in pathEnv.Split(Path.PathSeparator)) {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            var plain = Path.Combine(dir, toolName);
            if (File.Exists(plain))
                return plain;

            foreach (var ext in pathExt) {
                var candidate = Path.Combine(dir, toolName + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
