using System.Collections.ObjectModel;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Reconstructed4021.Tui;

/// <summary>
/// Main menu > Load Game: lists the *.json saves under saves/ (written by GameShell's own Save Game,
/// see its FindSaveDirectory). Real Pascal has no equivalent to browse here either (LOADSAVE.PAS just
/// prompts for a hardcoded filename) -- same rationale as ScenarioPickerWindow's own doc comment.
/// </summary>
public sealed class SaveGamePickerWindow : NewGameWindow
{
    public sealed record SaveChoice(string Path, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public string? SelectedPath { get; private set; }

    public SaveGamePickerWindow(IReadOnlyList<SaveChoice> saves) : base("Load Game")
    {
        if (saves.Count == 0) {
            Content.Add(new Label { X = 0, Y = 0, Text = "No saved games found." });
            Content.Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Esc: back to main menu" });

            KeyDown += (_, key) => {
                if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                    Dismiss();
                    key.Handled = true;
                }
            };
            return;
        }

        var listView = new ListView<SaveChoice> {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        listView.SetSource(new ObservableCollection<SaveChoice>(saves));
        Content.Add(listView);

        Content.Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Enter: choose   Esc: back to main menu" });

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.Enter:
                    SelectedPath = listView.Value?.Path;
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
