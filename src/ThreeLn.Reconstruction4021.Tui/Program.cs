using Terminal.Gui.App;
using ThreeLn.Reconstruction4021.Core;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Tui;

// GAUNTLET.SCN: real committed scenario data, 50x50 galaxy with 8 empires and ~290 sector objects --
// comfortably larger than any terminal, so the viewport actually has to scroll.
var scenarioPath = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "reference", "scenarios", "dos_131", "GAUNTLET.SCN");
var scenarioText = File.ReadAllText(scenarioPath);

var random = new Random(4021);
var setup = new GalaxySetup(random);
var loader = new ScenarioLoader(setup, random);
var players = new[] { new ScenarioLoader.PlayerInfo("Player", Password: null, IsEmpress: false) };
var game = loader.Load(scenarioText, players);

// GAUNTLET.SCN has no CreateFleet commands, so a freshly loaded galaxy has none -- these exist purely
// to exercise GalaxyView's fleet-indicator columns. Must run before GalaxyWindow/GalaxyView is
// constructed: GalaxyView indexes Galaxy.Fleets once in its constructor and never re-scans it.
SpawnDemoFleets(game);

// Terminal.Gui's main loop is fixed-cadence: each iteration drains the input queue, draws, then
// sleeps out whatever's left of 1000/MaximumIterationsPerSecond via Task.Delay -- so a keypress can
// wait up to a full iteration period before it's even processed, on top of our ~3.5ms redraw. The
// default of 25 (40ms) is the real source of perceptible lag, not our draw cost. 60 (16.7ms) still
// lands close to Task.Delay's ~15.6ms Windows timer granularity, so the sleep often overshoots; going
// higher shrinks the requested sleep enough that it's skipped more often, avoiding that overshoot.
Application.MaximumIterationsPerSecond = 240;

// Tried forcing a single write per frame (custom IOutput override) to fix Home/End tearing --
// reverted, no measured difference. Windows Terminal's renderer runs on its own dedicated thread,
// decoupled from how the writing process chunks its output (it locks and repaints the terminal's
// text buffer on its own schedule, regardless of write-call count) -- so the tearing is downstream
// of our process, in ConPTY/Windows Terminal's rendering pipeline, not something app-side code can
// fix. See https://github.com/tui-cs/Terminal.Gui/issues/5323 for the upstream tracking issue.

IApplication app = Application.Create().Init();
app.Run(new GalaxyWindow(game), null);
app.Dispose();

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThreeLn.Reconstruction4021.slnx"))) {
        dir = dir.Parent;
    }

    return dir?.FullName
        ?? throw new InvalidOperationException($"Could not locate repo root (ThreeLn.Reconstruction4021.slnx) above {start}.");
}

static void SpawnDemoFleets(Game game)
{
    var galaxy = game.Galaxy;
    var player = game.Empires[0];
    var enemy = game.Empires[1];

    Coordinate Near(Coordinate baseCoord, int dx) => baseCoord with { X = Math.Clamp(baseCoord.X + dx, 0, galaxy.Size - 1) };

    var playerBase = player.Capital?.Location ?? new Coordinate(galaxy.Size / 2, galaxy.Size / 2);
    var enemyBase = enemy.Capital?.Location ?? Near(playerBase, 5);

    galaxy.Fleets.Add(new Fleet { Location = Near(playerBase, 1), Owner = player });
    galaxy.Fleets.Add(new Fleet { Location = Near(playerBase, 2), Owner = player });
    galaxy.Fleets.Add(new Fleet { Location = Near(enemyBase, -1), Owner = enemy });
    galaxy.Fleets.Add(new Fleet { Location = Near(enemyBase, -2), Owner = enemy });

    // A contested sector -- both a player and an enemy fleet in the same place -- to check the
    // two-column split (left = ours, right = theirs) renders correctly when both are present.
    var contested = Near(playerBase, 5);
    galaxy.Fleets.Add(new Fleet { Location = contested, Owner = player });
    galaxy.Fleets.Add(new Fleet { Location = contested, Owner = enemy });
}
