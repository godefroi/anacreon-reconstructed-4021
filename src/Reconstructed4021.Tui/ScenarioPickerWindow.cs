using System.Collections.ObjectModel;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.NewGame;

namespace Reconstructed4021.Tui;

/// <summary>
/// New Game's scenario picker. Real Pascal has no equivalent -- NEWGAME.PAS's own ScenarioIntroduction
/// just prompts for a hardcoded filename, no directory scan or title list -- this is a TUI-only
/// convenience over the same reference/scenarios/dos_131/*.SCN files.
/// </summary>
internal sealed class ScenarioPickerWindow : NewGameWindow
{
    public sealed record ScenarioChoice(string Path, ScenarioLoader.ScenarioHeader Header)
    {
        public override string ToString() =>
            $"{Header.Title,-28} {Header.GalaxySize,3}x{Header.GalaxySize,-3} {Header.MinPlayers}-{Header.MaxPlayers} players";
    }

    public ScenarioChoice? Selected { get; private set; }

    public ScenarioPickerWindow(IReadOnlyList<ScenarioChoice> scenarios) : base("New Game -- Choose a Scenario")
    {
        var listView = new ListView<ScenarioChoice> {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        listView.SetSource(new ObservableCollection<ScenarioChoice>(scenarios));
        Content.Add(listView);

        Content.Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Enter: choose   Esc: back to main menu" });

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.Enter:
                    Selected = listView.Value;
                    Dismiss();
                    key.Handled = true;
                    break;
                case KeyCode.Esc:
                    Dismiss();
                    key.Handled = true;
                    break;
            }
        };

        Initialized += (_, _) => listView.SetFocus();
    }
}
