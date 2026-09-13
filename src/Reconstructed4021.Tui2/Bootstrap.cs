using Reconstructed4021.Core.NewGame;
using Reconstructed4021.LegacyNpe;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

// Shared by Program.cs (the real console entry point) and Reconstructed4021.Tui2Driver (the headless
// scripted entry point), so the scenario-directory scan and New Game dependencies can't drift between
// the two -- a driver that reconstructs its own copy of this setup silently stops matching the real
// app the moment one of them changes (same precedent as Reconstructed4021.TuiDriver referencing
// Reconstructed4021.Tui's own GameShell rather than rebuilding one).
public static class Bootstrap
{
    public static IScreen CreateInitialScreen(string repoRoot, bool skipIntro = false)
    {
        var random = new Random();
        var npeProvider = new LegacyNpeProvider();

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

        IScreen MakeTitleScreen() => new TitleScreen(new NewGameContext(random, npeProvider, MakeTitleScreen, LoadScenarios));

        var titleScreen = MakeTitleScreen();
        return skipIntro ? titleScreen : new TmaLogoScreen(titleScreen);
    }
}
