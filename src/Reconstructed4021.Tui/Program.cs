using Terminal.Gui.App;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;
using Reconstructed4021.Tui;
using Game = Reconstructed4021.Core.Game;

// No fixed seed: real Pascal's own Seed=0 branch (NEWGAME.PAS:1735-1738, "IF Seed=0 THEN Randomize")
// is what every one of the 12 real shipped .SCN files actually uses (see ScenarioLoader's own doc
// comment) -- real play is never reproducible run to run. A hardcoded seed here before would have
// made every fresh New Game -- galaxy layout, empire placement, suggested names, all of it --
// identical on every single launch, which is what TuiDriver's own fixed Random(4021) is *for*
// (deterministic test scripts), not what the real game should do.
var random = new Random();
var npeProvider = new LegacyNpeProvider();

var tuiSettings = TuiSettings.Load(FindRepoRoot(AppContext.BaseDirectory));

// Diagnostic: one line per empire per year for NewTechLevel's own roll (chance/roll/outcome/lab
// breakdown) -- logs/ is gitignored, same as WriteCrashLog's own file below, so this never needs
// cleaning up or committing. Unconditional (not env-var-gated): the file is tiny (a few KB per full
// game) and this is exactly the evidence a slow-tech-advancement report needs to actually diagnose,
// rather than reconstructing history from sparse autosaves after the fact.
var techDebugLogPath = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "logs", $"tech-debug-{DateTime.Now:yyyyMMdd-HHmmss}.log");
Directory.CreateDirectory(Path.GetDirectoryName(techDebugLogPath)!);
void TechDebugLog(string line) => File.AppendAllText(techDebugLogPath, line + Environment.NewLine);

// One TurnEngine for the whole process -- stateless itself, just wraps three handlers that all
// share this same random, matching every other scenario-load/setup component below.
var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random, tuiSettings.UseLegacyOrderResolution), new AnnualTickHandler(random, TechDebugLog));

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
// Game choice away from there). --load <path>: skip the whole pre-game flow (logo/title/picker/intro/
// player setup) and drop straight into a game deserialized from a native JSON save (Core/SaveFormat/
// GameJson.cs) -- for testing a hand-built fixture without routing it through scenario authoring. None
// of the above: play the full original sequence (ANACREON.PAS's Introduction, then PROLOG.PAS's
// MainTitle/SetUpPlayer).
var introOnly = args.Contains("--intro-only");
var noIntro = args.Contains("--no-intro");
var loadIndex = Array.IndexOf(args, "--load");
var loadPath = loadIndex >= 0 && loadIndex + 1 < args.Length
    ? Path.Combine(FindRepoRoot(AppContext.BaseDirectory), args[loadIndex + 1])
    : null;

IApplication app = Application.Create().Init();

// A window constructor or event handler throwing mid-Run (like the OptionSelector<TEnum> crash this
// try/finally was added for) would otherwise skip every app.Dispose() call below it, leaving the
// console in whatever raw mode/alt-screen-buffer state Init() put it in -- garbled colors and all,
// even after the process exits. finally guarantees Dispose() (which restores the console) always runs.
//
// The catch below is a separate, later fix: an uncaught exception here used to just propagate past
// this whole block to the runtime's own default handler, which does print to stderr -- but by then
// Dispose() (in finally) has already run, and depending on the terminal, an unhandled-exception exit
// can still close the window before that text is ever read (confirmed as a real complaint: "the game
// crashes with no output" after a genuine crash -- ScenarioLoader.Load never setting
// Game.CurrentEmpire, found and fixed separately). Catching here instead prints a clear banner *and*
// writes the same detail to a timestamped file under logs/ (gitignored), so there's always a durable
// record even if the console output scrolls away or the window closes before it's read.
Exception? crashException = null;
try {
    if (introOnly) {
        app.Run(new TmaLogoWindow(), null);
        app.Run(new AnacreonTitleWindow(), null);
        return 0;
    }

    if (!noIntro) {
        app.Run(new TmaLogoWindow(), null);
    }

    if (loadPath is not null) {
        var loadedGame = GameJson.Deserialize(File.ReadAllText(loadPath), random, npeProvider);
        if (RunGame(loadedGame) == GameShell.ExitChoice.ExitToOs) {
            return 0;
        }
        // MainMenu: fall through into the normal main-menu loop below for whatever comes next.
    }

    while (true) {
        var titleWindow = new AnacreonTitleWindow();
        app.Run(titleWindow, null);

        // Explicit about which choice actually opens the picker, rather than "anything but Quit" -- a
        // real bug here (now fixed in AnacreonTitleWindow itself) once let Esc silently stop this Run
        // without setting Choice at all, and the old "!= Quit" check mistook that leftover None for
        // NewGame and opened the picker anyway.
        if (titleWindow.Choice == AnacreonTitleWindow.MenuChoice.Quit) {
            return 0;
        }

        if (titleWindow.Choice == AnacreonTitleWindow.MenuChoice.LoadGame) {
            var saveDir = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "saves");
            var autoSaveDir = Path.Combine(saveDir, "auto"); // GameShell.AutoSave's own subdirectory -- listed alongside manual saves, not a separate screen.
            Directory.CreateDirectory(saveDir);
            Directory.CreateDirectory(autoSaveDir);
            var saves = Directory.GetFiles(saveDir, "*.json")
                .Concat(Directory.GetFiles(autoSaveDir, "*.json"))
                .OrderByDescending(File.GetLastWriteTime)
                .Select(path => new SaveGamePickerWindow.SaveChoice(path,
                    $"{(Path.GetDirectoryName(path) == autoSaveDir ? "[auto]" : "[save]"),-7}{Path.GetFileNameWithoutExtension(path),-30} {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}"))
                .ToList();

            var saveGamePicker = new SaveGamePickerWindow(saves);
            app.Run(saveGamePicker, null);
            if (saveGamePicker.SelectedPath is null) {
                continue; // Esc, or no saves found -- back to the main menu
            }

            var loadedGame = GameJson.Deserialize(File.ReadAllText(saveGamePicker.SelectedPath), random, npeProvider);
            if (RunGame(loadedGame) == GameShell.ExitChoice.MainMenu) {
                continue;
            }

            break; // ExitToOs
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
        var loader = new ScenarioLoader(setup, random, npeProvider);
        var game = loader.Load(scenarioText, players);

        if (RunGame(game) == GameShell.ExitChoice.MainMenu) {
            continue;
        }

        break; // ExitToOs, or any other/unexpected way this Run ended
    }
} catch (Exception ex) {
    crashException = ex;
} finally {
    app.Dispose();
}

if (crashException is not null) {
    var logPath = WriteCrashLog(crashException);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Reconstructed4021 crashed with an unhandled exception:");
    Console.Error.WriteLine(crashException);
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Details saved to {logPath}");
    return 1;
}

return 0;

// Real per-empire turn cycle for one whole game session: ANACREON.PAS's own main loop plays every
// empire's turn, human or NPE, one after another, wrapping back to the first when it runs out --
// this is that loop. A human empire gets its own Turn Start Greeting + Empire Status Report
// (PROLOG.PAS's SetUpPlayer, "once per player, per turn") plus an interactive GameShell session;
// anything else (an NPE, or a human TurnEngine itself is quietly finishing off via
// PendingElimination/Eliminated) just advances with no UI at all. Returns how the session ended,
// for the caller to decide what runs next.
//
// Deliberately still out of scope here, same as the rest of PROLOG.PAS's chained per-turn sequence:
// the password prompt. Nothing yet exercises more than one human empire in the same game, so real
// hotseat protection would be speculative -- this loop is already Status/IsHuman-driven per empire
// rather than hardcoded to one Empire reference, so a second human slotting in later needs no
// changes here, just that still-missing step. The Capital Fallen Report step (below) is real now.
GameShell.ExitChoice RunGame(Game game)
{
    while (true) {
        var current = game.CurrentEmpire ?? throw new InvalidOperationException("Game.CurrentEmpire must be set before the first turn.");
        var handler = game.TurnHandlers[current];

        if (!handler.IsHuman || current.Status != EmpireStatus.Active) {
            // A human parked at PendingElimination reaches this branch too (Status<>Active) --
            // AdvanceOneTurn is where TurnEngine.BeginTurn actually finishes that empire's own
            // teardown (CombatOutcome.DestroyEmpire), so the transition to Eliminated has to be
            // detected around this exact call, not before it.
            var wasPendingElimination = handler.IsHuman && current.Status == EmpireStatus.PendingElimination;
            turnEngine.AdvanceOneTurn(game);

            if (wasPendingElimination) {
                ShowCapitalFallenReport(current);
            }
        } else {
            // BeginTurn (fog-of-war refresh, matching ANACREON.PAS's SetUpTurn) runs here, before the
            // human sees anything -- not inside GameShell's own End Turn, which would show them a map
            // still reflecting the end of their *previous* turn. GameShell's End Turn only runs the
            // other half, TurnEngine.EndTurn (see its own doc comment).
            turnEngine.BeginTurn(game);

            app.Run(new TurnStartGreetingWindow(current, game.Year, Random.Shared.Next(1, 4)), null);
            app.Run(new EmpireStatusWindow(current, game), null);

            var gameShell = new GameShell(game, turnEngine, current, random, tuiSettings);
            app.Run(gameShell, null);
            if (gameShell.Choice is GameShell.ExitChoice.MainMenu or GameShell.ExitChoice.ExitToOs) {
                return gameShell.Choice;
            }
            // EndTurn: TurnEngine.EndTurn already ran inside GameShell -- loop straight to whatever
            // CurrentEmpire is now.
        }

        if (!game.AnyHumanPlayersRemain) {
            DosDialogWindow.ShowInfo(app, "Defeat", "Your empire has fallen.");
            return GameShell.ExitChoice.MainMenu;
        }

        var npeEmpires = game.Empires.Where(e => e.NpeType is not null).ToList();
        if (npeEmpires.Count > 0 && npeEmpires.All(e => e.Status == EmpireStatus.Eliminated)) {
            DosDialogWindow.ShowInfo(app, "Victory", "Every enemy empire has been destroyed.");
            return GameShell.ExitChoice.MainMenu;
        }
    }
}

// EmpireNews (PROLOG.PAS:456-488), the "Capital Fallen Report": shown once, in place of that turn's
// own GameShell session, at the start of the defeated player's own next turn-prologue -- there's
// nothing left to do once Empire.Capital is null and Status is Eliminated, matching real Pascal's own
// SetUpPlayer ordering (this replaces the whole per-turn sequence for that one cycle, same as
// PROLOG.PAS's own EmpireNews sets LastTurn:=True to skip EmpireStatus). The mechanical teardown
// (Eliminated, DefeatedBy) already ran inside TurnEngine.BeginTurn/AdvanceOneTurn -- this is only the
// narrative screen real Pascal shows alongside it, transcribed verbatim from EmpireNews' own
// WriteString calls.
void ShowCapitalFallenReport(Empire defeated)
{
    var lord = Honorifics.MyLord(defeated.IsEmpress);
    var text =
        $"{lord},\n\n" +
        "I regret that I must communicate the dreadful news in this impersonal way,\n" +
        "but by the time you read this I will most likely be either dead or\n" +
        "imprisoned.  While you slept peacefully, our capital was attacked by the\n" +
        $"{defeated.DefeatedBy!.Name} Empire.  Though our men and women fought bravely,\n" +
        "the strength of our adversary overwhelmed us and we were forced to surrender.\n\n" +
        "I have arranged an honorable course of action for Your Majesty; you will find\n" +
        $"necessary materials by your bedside.  Good luck, {lord}.\n\n" +
        "- Your Loyal Servant";
    DosDialogWindow.ShowInfo(app, "Capital Fallen", text);
}

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reconstructed4021.slnx"))) {
        dir = dir.Parent;
    }

    return dir?.FullName
        ?? throw new InvalidOperationException($"Could not locate repo root (Reconstructed4021.slnx) above {start}.");
}

// logs/ is gitignored -- these are the player's own local crash history, never committed.
static string WriteCrashLog(Exception ex)
{
    var logsDir = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "logs");
    Directory.CreateDirectory(logsDir);
    var path = Path.Combine(logsDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    File.WriteAllText(path, $"{DateTime.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}");
    return path;
}
