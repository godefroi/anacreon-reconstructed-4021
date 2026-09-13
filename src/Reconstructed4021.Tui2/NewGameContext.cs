using Reconstructed4021.LegacyNpe;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

// Threaded through every New Game screen: the shared random/NPE-provider the eventual
// ScenarioLoader.Load call needs, a factory for a fresh title screen, and a lazy scenario-directory
// scan. LoadScenarios is a delegate, not an already-computed list, so the directory scan and per-file
// ReadHeader parse only happen when a player actually picks New Game -- every other title-screen visit
// (Load Game, Options, Quit, or just sitting there, which is most of them) never pays for it.
//
// Every Esc in this flow goes all the way back to the main menu -- matching Reconstructed4021.Tui's
// own Program.cs, whose New Game loop unconditionally `continue`s to a brand new AnacreonTitleWindow
// on any cancellation, never back one step -- and a screen instance must never be revisited a second
// time: ScreenRunner never clears an outgoing screen's own NextScreen, so reusing one here would make
// the reused screen transition again on its very next frame, straight back to wherever it pointed last
// time.
public sealed record NewGameContext(
    Random Random,
    LegacyNpeProvider NpeProvider,
    Func<IScreen> MakeTitleScreen,
    Func<IReadOnlyList<ScenarioPickerScreen.ScenarioChoice>> LoadScenarios);
