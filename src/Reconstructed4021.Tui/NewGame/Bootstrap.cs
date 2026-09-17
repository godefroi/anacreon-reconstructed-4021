using Reconstructed4021.Core;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.LegacyNpe;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui.Screens;


namespace Reconstructed4021.Tui.NewGame;


// Shared by Program.cs (the real console entry point) and Reconstructed4021.TuiDriver (the headless
// scripted entry point), so the scenario-directory scan and New Game dependencies can't drift between
// the two -- a driver that reconstructs its own copy of this setup silently stops matching the real
// app the moment one of them changes (same precedent as Reconstructed4021.TuiDriver referencing
// Reconstructed4021.Tui's own GameShell rather than rebuilding one).
public static class Bootstrap
{
    public static IScreen CreateInitialScreen(string repoRoot, bool skipIntro = false)
    {
        var titleScreen = CreateContext(repoRoot).MakeTitleScreen();
        return skipIntro ? titleScreen : new TmaLogoScreen(titleScreen);
    }

    // Skips the whole pre-game flow (splash/title/picker/player setup) straight into a running game --
    // for testing the map without re-running New Game's ~10 keypresses every time, matching
    // Reconstructed4021.Tui's own Program.cs --load flag ("skip the whole pre-game flow ... for
    // testing a hand-built fixture without routing it through scenario authoring").
    public static IScreen CreateGalaxyMapScreen(string repoRoot, Game game)
    {
        var player = game.CurrentEmpire ?? throw new InvalidOperationException("Save has no CurrentEmpire set -- nothing to drive.");
        return new GalaxyMapScreen(game, player, CreateContext(repoRoot));
    }

    private static NewGameContext CreateContext(string repoRoot)
    {
        var random = new Random();
        var npeProvider = new LegacyNpeProvider();
        var settings = TuiSettings.Load(repoRoot);

        // NEWGAME.PAS's own ScenarioIntroduction just prompts for a hardcoded filename -- no directory
        // scan or title list. ReadHeader reads only the same header tokens Load() itself would, so
        // this list can't drift out of sync with what a real load consumes. Deferred behind a
        // delegate (see NewGameContext) rather than run here, so it only costs anything once a player
        // actually picks New Game.
        IReadOnlyList<ScenarioPickerScreen.ScenarioChoice> LoadScenarios()
        {
            var scenarioDir = Path.Combine(repoRoot, "reference", "scenarios", "dos_131");
            if (!Directory.Exists(scenarioDir))
            {
                return [];
            }

            return Directory.GetFiles(scenarioDir, "*.SCN")
                .Select(path => new ScenarioPickerScreen.ScenarioChoice(path, ScenarioLoader.ReadHeader(ScenarioLoader.ReadScenarioFile(path))))
                .OrderBy(choice => choice.Header.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // saves/ is gitignored (a player's own save files, never committed); auto/ is GameShell's own
        // autosave subdirectory, listed alongside manual saves rather than as a separate screen --
        // same layout Reconstructed4021.Tui's own Program.cs Load Game branch uses.
        IReadOnlyList<SaveGamePickerScreen.SaveChoice> LoadSaves()
        {
            var saveDir = Path.Combine(repoRoot, "saves");
            var autoSaveDir = Path.Combine(saveDir, "auto");
            Directory.CreateDirectory(saveDir);
            Directory.CreateDirectory(autoSaveDir);

            return Directory.GetFiles(saveDir, "*.json")
                .Concat(Directory.GetFiles(autoSaveDir, "*.json"))
                .OrderByDescending(File.GetLastWriteTime)
                .Select(path => new SaveGamePickerScreen.SaveChoice(path,
                    $"{(Path.GetDirectoryName(path) == autoSaveDir ? "[auto]" : "[save]"),-7}{Path.GetFileNameWithoutExtension(path),-30} {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}"))
                .ToList();
        }

        // One TurnEngine for the whole process -- stateless itself, just wraps three handlers that all
        // share this same random, matching Reconstructed4021.Tui's own Program.cs. No tech-debug log
        // file (AnnualTickHandler's techDebugLog param is optional): that's a debug tool tui1 built for
        // its own tech-balance investigation, not something this port's turn loop needs to duplicate.
        var turnEngine = new TurnEngine(new VisibilityHandler(random), new FleetMovementHandler(random, settings.UseLegacyOrderResolution), new AnnualTickHandler(random));

        NewGameContext? context = null;
        IScreen MakeTitleScreen() => new TitleScreen(context!);
        context = new NewGameContext(random, npeProvider, repoRoot, MakeTitleScreen, LoadScenarios, LoadSaves, turnEngine, settings);
        return context;
    }
}
