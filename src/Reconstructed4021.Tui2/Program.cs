using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui2;

// Run this in a real Windows Terminal window -- not through a tool-captured shell, which has
// redirected stdio and no real VT-processing terminal on the other end.
ScreenHost.Run(Bootstrap.CreateInitialScreen(FindRepoRoot(AppContext.BaseDirectory)));

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reconstructed4021.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException($"Could not locate repo root (Reconstructed4021.slnx) above {start}.");
}
