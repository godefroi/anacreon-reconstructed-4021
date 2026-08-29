using Terminal.Gui.App;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Tui;

var random = new Random(4021);

// NEWGAME.PAS's own ScenarioIntroduction just prompts for a hardcoded filename -- no directory scan or
// title list. ScenarioLoader.ReadHeader reads only the same header tokens Load() itself would, so this
// list can't drift out of sync with what a real load consumes.
var scenarioDir = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "reference", "scenarios", "dos_131");
var scenarios = Directory.GetFiles(scenarioDir, "*.SCN")
    .Select(path => new ScenarioPickerWindow.ScenarioChoice(path, ScenarioLoader.ReadHeader(ScenarioLoader.ReadScenarioFile(path))))
    .OrderBy(choice => choice.Header.Title, StringComparer.OrdinalIgnoreCase)
    .ToList();

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

// --intro-only: play the TMA logo and the Anacreon title/orbit main menu, then exit whenever it's
// dismissed, regardless of which button -- for reviewing those without waiting through the New Game flow
// too. --no-intro: skip the TMA logo splash, straight to the main menu (the map itself is then one New
// Game choice away from there). None given: play the full original sequence (ANACREON.PAS's Introduction,
// then PROLOG.PAS's MainTitle/SetUpPlayer).
var introOnly = args.Contains("--intro-only");
var noIntro = args.Contains("--no-intro");

IApplication app = Application.Create().Init();

// A window constructor or event handler throwing mid-Run (like the OptionSelector<TEnum> crash this
// try/finally was added for) would otherwise skip every app.Dispose() call below it, leaving the
// console in whatever raw mode/alt-screen-buffer state Init() put it in -- garbled colors and all,
// even after the process exits. finally guarantees Dispose() (which restores the console) always runs.
try {
    if (introOnly) {
        app.Run(new TmaLogoWindow(), null);
        app.Run(new AnacreonTitleWindow(), null);
        return;
    }

    if (!noIntro) {
        app.Run(new TmaLogoWindow(), null);
    }

    while (true) {
        var titleWindow = new AnacreonTitleWindow();
        app.Run(titleWindow, null);

        // Explicit about which choice actually opens the picker, rather than "anything but Quit" -- a
        // real bug here (now fixed in AnacreonTitleWindow itself) once let Esc silently stop this Run
        // without setting Choice at all, and the old "!= Quit" check mistook that leftover None for
        // NewGame and opened the picker anyway.
        if (titleWindow.Choice == AnacreonTitleWindow.MenuChoice.Quit) {
            return;
        }

        if (titleWindow.Choice != AnacreonTitleWindow.MenuChoice.NewGame) {
            continue;
        }

        var pickerWindow = new ScenarioPickerWindow(scenarios);
        app.Run(pickerWindow, null);
        if (pickerWindow.Selected is null) {
            continue; // Esc -- back to the main menu, not a quit
        }

        var header = pickerWindow.Selected.Header;
        var scenarioText = ScenarioLoader.ReadScenarioFile(pickerWindow.Selected.Path);

        var introPages = ScenarioLoader.ReadIntroPages(scenarioText);
        if (introPages.Count > 0) {
            var introWindow = new IntroTextWindow(header.Title, introPages);
            app.Run(introWindow, null);
            if (introWindow.Cancelled) {
                continue; // Esc -- back to the main menu
            }
        }

        var playerCount = header.MinPlayers;
        if (header.MinPlayers < header.MaxPlayers) {
            var countWindow = new PlayerCountWindow(header.Title, header.MinPlayers, header.MaxPlayers);
            app.Run(countWindow, null);
            if (countWindow.Count is null) {
                continue; // Esc -- back to the main menu
            }

            playerCount = countWindow.Count.Value;
        }

        var players = new List<ScenarioLoader.PlayerInfo>();
        var cancelled = false;
        for (var playerNumber = 1; playerNumber <= playerCount; playerNumber++) {
            var suggestedName = ScenarioLoader.SuggestEmpireName(random, players.Select(p => p.Name).ToList());
            var setupWindow = new PlayerSetupWindow(header.Title, playerNumber, suggestedName);
            app.Run(setupWindow, null);
            if (setupWindow.PlayerInfo is null) {
                cancelled = true;
                break; // Esc -- back to the main menu
            }

            players.Add(setupWindow.PlayerInfo);
        }

        if (cancelled) {
            continue;
        }

        var setup = new GalaxySetup(random);
        var loader = new ScenarioLoader(setup, random);
        var game = loader.Load(scenarioText, players);

        app.Run(new TurnStartGreetingWindow(game.Empires[0], game.Year, Random.Shared.Next(1, 4)), null);

        var gameShell = new GameShell(game);
        app.Run(gameShell, null);
        if (gameShell.Choice == GameShell.ExitChoice.MainMenu) {
            continue;
        }

        break; // ExitToOs, or any other/unexpected way this Run ended
    }
} finally {
    app.Dispose();
}

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThreeLn.Reconstruction4021.slnx"))) {
        dir = dir.Parent;
    }

    return dir?.FullName
        ?? throw new InvalidOperationException($"Could not locate repo root (ThreeLn.Reconstruction4021.slnx) above {start}.");
}
