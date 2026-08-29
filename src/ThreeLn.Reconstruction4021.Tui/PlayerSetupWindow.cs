using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using ThreeLn.Reconstruction4021.Core.NewGame;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace ThreeLn.Reconstruction4021.Tui;

/// <summary>
/// NEWGAME.PAS:1517-1632 (InputEmpireName), inside the same full-screen backdrop PlayerCountWindow opens
/// (see its own doc comment) -- name is plain WriteString/InputString straight onto that blue
/// background, matching source. Gender is real Pascal's own separate popup
/// (<c>OpenWindow(20,12,50,7,ThinBRD,'',C.CommWind,C.SYSWBorder,...)</c> -- CommWind=15 decodes to
/// white-on-black), reproduced here as a small bordered box revealed over the blue background rather
/// than a second nested Terminal.Gui Window/Application.Run (this codebase always runs one screen at a
/// time; see Program.cs's own sequential app.Run calls). Real Pascal's next step in this same procedure
/// is a password, entered twice to confirm -- dropped here: its only purpose there is protecting each
/// human's turn in hot-seat multiplayer, and nothing in this port cycles through more than one human's
/// turn yet (GameShell always plays Empires[0]), so collecting one now would have no consumer. Add it
/// back alongside real hot-seat turn-taking.
/// </summary>
internal sealed class PlayerSetupWindow : Window
{
    private enum Gender { Male, Female }

    // COLORS.INC's ColorScrColor: SYSDispWind=23 -> LightGray on Blue; CommWind=15 -> White on Black.
    private static readonly TgAttribute SysDispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute CommWindAttribute = new(StandardColor.White, StandardColor.Black);

    public ScenarioLoader.PlayerInfo? PlayerInfo { get; private set; }

    public PlayerSetupWindow(string scenarioTitle, int playerNumber, string suggestedName)
    {
        Title = scenarioTitle;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(new Scheme(SysDispWindAttribute));

        var nameField = new TextField { X = 1, Y = 2, Width = 32, Text = suggestedName };
        Add(new Label { X = 1, Y = 1, Text = $"Name of player empire #{playerNumber} : " });
        Add(nameField);

        // CanFocus = true, not false: this container not being part of the focus chain is what blocked
        // genderSelector.SetFocus() below from actually landing there, confirmed from real testing --
        // focus silently stayed on nameField underneath, so typing "f"/"m" typed into the name instead of
        // picking a gender.
        var genderBox = new View {
            X = Pos.Center(),
            Y = 6,
            Width = 34,
            Height = 5,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
            Visible = false,
        };
        genderBox.SetScheme(new Scheme(CommWindAttribute));

        // OptionSelector<TEnum> populates its own Values/Labels from TEnum internally -- calling
        // SetValuesAndLabels<TEnum>() on it (that's for the non-generic OptionSelector) throws
        // "Setting Values directly is not allowed", confirmed from a real crash here.
        var genderSelector = new OptionSelector<Gender> { X = 1, Y = 1, Value = Gender.Male };

        genderBox.Add(new Label { X = 1, Y = 0, Text = "Are you male or female?" });
        genderBox.Add(genderSelector);
        Add(genderBox);

        Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: confirm name   Click/arrows+Enter: pick gender   Esc: back" });

        void ConfirmName()
        {
            genderBox.Visible = true;
            genderSelector.SetFocus();
        }

        // ValueChanged instead of a KeyDown-based "confirm" step: OptionSelector's own child CheckBoxes
        // (not the OptionSelector itself) are what actually take focus and handle Enter/click, so a
        // handler attached to genderSelector.KeyDown never saw those key presses -- confirmed from real
        // testing (mouse-picking an option worked, but nothing then dismissed the screen). ValueChanged
        // fires regardless of which internal child raised it, from either a click or an arrow+Enter
        // pick, so picking a gender is itself "confirm" -- no separate step needed.
        genderSelector.ValueChanged += (_, args) => {
            if (args.Value is not { } gender)
                return;

            var name = string.IsNullOrWhiteSpace(nameField.Text) ? suggestedName : nameField.Text.Trim();
            PlayerInfo = new ScenarioLoader.PlayerInfo(name, Password: null, gender == Gender.Female);
            App?.RequestStop();
        };

        // Attached directly to the field, not a bubbled Window-level Accepting/Command.Accept handler
        // (that crashed here in testing) -- same proven pattern as AnacreonTitleWindow's menu buttons: a
        // view's own KeyDown fires before any internal command handling gets a chance to swallow it.
        nameField.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Enter && !genderBox.Visible) {
                ConfirmName();
                key.Handled = true;
            }
        };

        KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Esc) {
                return;
            }

            App?.RequestStop();
            key.Handled = true;
        };

        Initialized += (_, _) => nameField.SetFocus();
    }
}
