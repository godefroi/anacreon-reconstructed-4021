using System.Diagnostics;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui2.NewGame;

// Run this in a real Windows Terminal window -- not through a tool-captured shell, which has
// redirected stdio and no real VT-processing terminal on the other end.
// --no-intro: skip the TMA logo splash, straight to the title menu -- same flag Reconstructed4021.Tui's
// own Program.cs supports.
var noIntro = args.Contains("--no-intro");
var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

// ScreenHost logs slow frames via Trace -- writing straight to the console would corrupt the
// alternate screen buffer the TUI is drawing into, so route it to a file instead, same "logs/"
// convention Reconstructed4021.Tui2Driver already uses.
var logDir = Path.Combine(repoRoot, "logs");
Directory.CreateDirectory(logDir);
Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(logDir, "panemonde-frames.log")) { TraceOutputOptions = TraceOptions.DateTime });
Trace.AutoFlush = true;

ScreenHost.Run(Bootstrap.CreateInitialScreen(repoRoot, skipIntro: noIntro));

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reconstructed4021.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException($"Could not locate repo root (Reconstructed4021.slnx) above {start}.");
}
