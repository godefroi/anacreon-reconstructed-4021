using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Reconstructed4021.Tui;

/// <summary>
/// NEWGAME.PAS:1425-1470 (GetNoOfPlayers), inside the same 80x24 NewGameWindow box that wraps the whole
/// New Game flow (see its own doc comment) -- the prompt itself is plain WriteString/InputString
/// straight onto that box, not its own popup (only the gender prompt in PlayerSetupWindow gets one; see
/// its own doc comment). Program.cs skips this window entirely when MinPlayers equals MaxPlayers,
/// matching ScenarioIntroduction's own "IF MinPlay&lt;MaxPlay" branch (its NoChoice counterpart, which
/// never prompts at all).
/// </summary>
internal sealed class PlayerCountWindow : NewGameWindow
{
    public int? Count { get; private set; }

    public PlayerCountWindow(string scenarioTitle, int minPlayers, int maxPlayers) : base(scenarioTitle)
    {
        var field = new TextField { X = 1, Y = 2, Width = 10, Text = minPlayers.ToString() };
        var error = new Label { X = 1, Y = 4, Text = "" };

        Content.Add(new Label { X = 1, Y = 1, Text = $"How many players ({minPlayers}-{maxPlayers}) ? " });
        Content.Add(field);
        Content.Add(error);
        Content.Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: confirm   Esc: back to main menu" });

        void Confirm()
        {
            if (int.TryParse(field.Text, out var count) && count >= minPlayers && count <= maxPlayers) {
                Count = count;
                Dismiss();
            } else {
                error.Text = $"Please enter a number between {minPlayers} and {maxPlayers}.";
            }
        }

        // Attached directly to the field (not a bubbled Window-level Accepting/Command.Accept handler --
        // that crashed here in testing), same proven pattern as AnacreonTitleWindow's menu buttons: a
        // view's own KeyDown fires before any internal command handling gets a chance to swallow it.
        field.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Enter) {
                Confirm();
                key.Handled = true;
            }
        };

        KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Esc) {
                return;
            }

            Dismiss();
            key.Handled = true;
        };

        Initialized += (_, _) => field.SetFocus();
    }
}
