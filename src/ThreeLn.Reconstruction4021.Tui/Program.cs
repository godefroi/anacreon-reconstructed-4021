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

// --greetings: cycle every Turn Start Greeting variant once each, then exit -- for reviewing the text
// without relying on random luck to see all 3. --intro-only: play the TMA logo and the Anacreon
// title/orbit animation, then exit -- for reviewing those without waiting through the greeting/map too.
// --skip-intro: skip the TMA logo, the Anacreon title/orbit animation, and the greeting entirely,
// straight to the map (the fast dev-iteration path this project used before any of them existed; will
// likely instead land on the DOS pre-game main menu once that screen exists, but it's currently a bit
// awkward -- a whole menu bar for only a handful of actionable items -- and due for a rethink before
// it's worth wiring in here). None given: play the full original sequence once (ANACREON.PAS's
// Introduction, then PROLOG.PAS's MainTitle/SetUpPlayer).
var showAllGreetings = args.Contains("--greetings");
var introOnly = args.Contains("--intro-only");
var skipIntro = args.Contains("--skip-intro");

IApplication app = Application.Create().Init();

if (showAllGreetings) {
    for (var variant = 1; variant <= 3; variant++) {
        app.Run(new TurnStartGreetingWindow(game.Empires[0], game.Year, variant), null);
    }

    app.Dispose();
    return;
}

if (introOnly) {
    app.Run(new TmaLogoWindow(), null);
    app.Run(new AnacreonTitleWindow(), null);
    app.Dispose();
    return;
}

if (!skipIntro) {
    app.Run(new TmaLogoWindow(), null);
    app.Run(new AnacreonTitleWindow(), null);
    app.Run(new TurnStartGreetingWindow(game.Empires[0], game.Year, Random.Shared.Next(1, 4)), null);
}

app.Run(new GameShell(game), null);
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
