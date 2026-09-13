using System.Diagnostics;
using Reconstructed4021.Panemonde;

if (args.Contains("--bench"))
{
    RunBench();
    return;
}

RunInteractive();
return;

// Headless, redirected-stdio-safe: writes go to Stream.Null, so this is the part safe to run under
// a tool-captured shell. Reports bytes/frame and (trivially, by construction) one write syscall per
// frame -- it does NOT report wall-clock time, since a redirected pipe here says nothing about real
// Windows Terminal render latency. Run the interactive mode yourself in a real terminal for that.
static void RunBench()
{
    const int width = 180;
    const int height = 50;
    const int frameCount = 500;

    Console.WriteLine("full-redraw (every cell dirty every frame):");
    RunBenchScenario(width, height, frameCount, fullRedraw: true);

    Console.WriteLine();
    Console.WriteLine("incremental (cursor moves one sector per frame):");
    RunBenchScenario(width, height, frameCount, fullRedraw: false);
}

static void RunBenchScenario(int width, int height, int frameCount, bool fullRedraw)
{
    var fb = new FrameBuffer(width, height, Stream.Null);
    var scene = new Scene(seed: 12345);
    var bytesPerFrame = new int[frameCount];

    for (var i = 0; i < frameCount; i++)
    {
        if (fullRedraw)
        {
            // Alternate fill colors so every single cell differs from the prior frame -- the
            // worst case (a resize, or a window closing over the whole viewport).
            var fill = new Cell(new System.Text.Rune('#'), i % 2 == 0 ? ConsoleColor.White : ConsoleColor.Gray, ConsoleColor.Black);
            fb.Clear(fill);
        }
        else
        {
            scene.Move(1, 0);
            scene.Draw(fb);
        }

        var expected = fb.SnapshotBack();
        var bytes = fb.Present();
        if (!fb.FrontMatches(expected))
        {
            throw new InvalidOperationException($"Present() diff/swap bug at frame {i}: front buffer doesn't match what was requested.");
        }

        bytesPerFrame[i] = bytes;
    }

    Array.Sort(bytesPerFrame);
    Console.WriteLine($"  writes: {frameCount} (1 syscall/frame by construction)");
    Console.WriteLine($"  bytes/frame  min={bytesPerFrame[0]}  median={Percentile(bytesPerFrame, 0.50)}  p95={Percentile(bytesPerFrame, 0.95)}  max={bytesPerFrame[^1]}");
}

static int Percentile(int[] sorted, double p)
{
    var idx = (int)Math.Clamp(p * (sorted.Length - 1), 0, sorted.Length - 1);
    return sorted[idx];
}

// Run this yourself in a real Windows Terminal window -- not through a tool-captured shell, which
// has redirected stdio and no real VT-processing terminal on the other end. Arrow keys pan the
// viewport; 'r' forces a full-viewport redraw (the worst case); 'l' prints input-to-flush latency
// stats so far; 'q' quits and prints them once more.
static void RunInteractive()
{
    if (OperatingSystem.IsWindows())
    {
        ConsoleMode.EnableVirtualTerminalOutput();
    }

    Console.CursorVisible = false;
    var width = Console.IsOutputRedirected ? 120 : Console.WindowWidth;
    var height = Console.IsOutputRedirected ? 30 : Console.WindowHeight;

    var fb = new FrameBuffer(width, height, Console.OpenStandardOutput());
    var scene = new Scene(seed: 12345);
    var latenciesMs = new List<double>();

    // Our own SGR codes (FrameBuffer.AppendSgr) are left set on the real terminal after our last
    // write -- a real terminal doesn't reset attributes on process exit the way Terminal.Gui's
    // driver teardown does, so without this the user's shell prompt inherits our last frame's
    // colors. Cover both the normal 'q' path and Ctrl+C.
    Console.CancelKeyPress += (_, _) => ResetTerminal();

    try
    {
        scene.Draw(fb);
        fb.Present();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            var start = Stopwatch.GetTimestamp();

            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    scene.Move(-1, 0);
                    break;
                case ConsoleKey.RightArrow:
                    scene.Move(1, 0);
                    break;
                case ConsoleKey.UpArrow:
                    scene.Move(0, -1);
                    break;
                case ConsoleKey.DownArrow:
                    scene.Move(0, 1);
                    break;
                case ConsoleKey.R:
                    fb.Clear(new Cell(new System.Text.Rune('#'), ConsoleColor.White, ConsoleColor.Black));
                    break;
                case ConsoleKey.L:
                    PrintLatencyStats(latenciesMs);
                    continue;
                case ConsoleKey.Q:
                    PrintLatencyStats(latenciesMs);
                    return;
                default:
                    continue;
            }

            scene.Draw(fb);
            fb.Present();

            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            latenciesMs.Add(elapsedMs);
        }
    }
    finally
    {
        ResetTerminal();
    }
}

static void ResetTerminal()
{
    Console.Out.Write("\x1b[0m");
    Console.Out.Flush();
    Console.CursorVisible = true;
}

static void PrintLatencyStats(List<double> latenciesMs)
{
    if (latenciesMs.Count == 0)
    {
        Console.Error.WriteLine("no keypresses recorded yet");
        return;
    }

    var sorted = latenciesMs.OrderBy(x => x).ToArray();
    Console.Error.WriteLine(
        $"input-to-flush latency (ms), n={sorted.Length}: " +
        $"min={sorted[0]:F2} median={sorted[sorted.Length / 2]:F2} p95={sorted[(int)(sorted.Length * 0.95)]:F2} max={sorted[^1]:F2}");
}
