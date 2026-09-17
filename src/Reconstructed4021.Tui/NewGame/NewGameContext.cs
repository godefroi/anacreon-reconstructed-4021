using Reconstructed4021.Core.Turns;
using Reconstructed4021.LegacyNpe;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Tui.Screens;


namespace Reconstructed4021.Tui.NewGame;


// Shared app-level state threaded through every screen from TitleScreen onward: the random/NPE-
// provider ScenarioLoader.Load and GameJson need, the repo root (for the saves/ directory and
// anything else path-based), a factory for a fresh title screen, and lazy scenario/save directory
// scans. LoadScenarios/LoadSaves are delegates, not already-computed lists, so the directory scan
// (and, for scenarios, a ReadHeader parse per file) only happens when a player actually picks New
// Game or Load Game -- every other title-screen visit (Options, Quit, or just sitting there, which is
// most of them) never pays for it.
//
// Every Esc in the New Game flow goes all the way back to the main menu -- matching
// Reconstructed4021.Tui's own Program.cs, whose New Game loop unconditionally `continue`s to a brand
// new AnacreonTitleWindow on any cancellation, never back one step -- and a screen instance must never
// be revisited a second time: ScreenRunner never clears an outgoing screen's own NextScreen, so
// reusing one here would make the reused screen transition again on its very next frame, straight back
// to wherever it pointed last time.
public sealed record NewGameContext(
    Random Random,
    LegacyNpeProvider NpeProvider,
    string RepoRoot,
    Func<IScreen> MakeTitleScreen,
    Func<IReadOnlyList<ScenarioPickerScreen.ScenarioChoice>> LoadScenarios,
    Func<IReadOnlyList<SaveGamePickerScreen.SaveChoice>> LoadSaves,
    TurnEngine TurnEngine,
    TuiSettings Settings);
