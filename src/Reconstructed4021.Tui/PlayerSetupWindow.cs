using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.NewGame;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// NEWGAME.PAS:1517-1632 (InputEmpireName), inside the same 80x24 NewGameWindow box the whole New Game
/// flow runs in (see its own doc comment) -- name is plain WriteString/InputString straight onto that
/// blue background, matching source. Gender is real Pascal's own separate popup
/// (<c>OpenWindow(20,12,50,7,ThinBRD,'',C.CommWind,C.SYSWBorder,...)</c> -- CommWind=15 decodes to
/// white-on-black), reproduced here as a small bordered box revealed over the blue background rather
/// than a second nested Terminal.Gui Window/Application.Run (this codebase always runs one screen at a
/// time; see Program.cs's own sequential app.Run calls). Real Pascal's next step in this same procedure
/// is a password, entered twice to confirm -- dropped here: its only purpose there is protecting each
/// human's turn in hot-seat multiplayer, and nothing in this port cycles through more than one human's
/// turn yet (GameShell always plays Empires[0]), so collecting one now would have no consumer. Add it
/// back alongside real hot-seat turn-taking.
/// </summary>
public sealed class PlayerSetupWindow : NewGameWindow
{
    private enum Gender { Male, Female }

    // COLORS.INC's ColorScrColor: CommWind=15 -> White on Black.
    private static readonly TgAttribute CommWindAttribute = new(StandardColor.White, StandardColor.Black);

    public ScenarioLoader.PlayerInfo? PlayerInfo { get; private set; }

    public PlayerSetupWindow(string scenarioTitle, int playerNumber, string suggestedName) : base(scenarioTitle)
    {
        var nameField = new TextField { X = 1, Y = 2, Width = 32, Text = suggestedName };
        Content.Add(new Label { X = 1, Y = 1, Text = $"Name of player empire #{playerNumber} : " });
        Content.Add(nameField);

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

        genderBox.Add(new Label { X = 1, Y = 0, Text = "Are you male or female? (M/F)" });
        genderBox.Add(genderSelector);
        Content.Add(genderBox);

        Content.Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: confirm name   M/F, click, or arrows+Enter: pick gender   Esc: back" });

        void ConfirmName()
        {
            genderBox.Visible = true;
            genderSelector.SetFocus();
        }

        void ConfirmGender(Gender gender)
        {
            var name = string.IsNullOrWhiteSpace(nameField.Text) ? suggestedName : nameField.Text.Trim();
            PlayerInfo = new ScenarioLoader.PlayerInfo(name, Password: null, gender == Gender.Female);
            Dismiss();
        }

        // ValueChanged instead of a KeyDown-based "confirm" step: OptionSelector's own child CheckBoxes
        // (not the OptionSelector itself) are what actually take focus and handle Space/click, so a
        // handler attached to genderSelector.KeyDown never saw those key presses -- confirmed from real
        // testing (mouse-picking an option worked, but nothing then dismissed the screen). ValueChanged
        // fires regardless of which internal child raised it, from either a click or Space, so picking
        // a gender that way is itself "confirm" -- no separate step needed. Enter is a separate case,
        // handled explicitly below (View's own Enter/Space split means it never reaches this event at
        // all -- see that handler's own doc comment).
        genderSelector.ValueChanged += (_, args) => {
            if (args.Value is { } gender) {
                ConfirmGender(gender);
            }
        };

        // Enter never reaches OptionSelector's own selection logic (Space/click does, via
        // ValueChanged above): View's base KeyBindings map Space to Command.Activate (what
        // OptionSelector.OnActivated handles) and Enter to the separate Command.Accept, which
        // OptionSelector never overrides -- confirmed by decompiling View.SetupKeyboard and
        // OptionSelector.OnActivated (dotnet-inspect). Command.Accept isn't a plain KeyDown either
        // (a KeyDown handler on genderSelector itself, or this Window, never saw it -- confirmed live:
        // whichever child CheckBox is focused consumes the key at that level) -- it's Terminal.Gui's
        // own separate Accept-dispatch system, and genderSelector's own Accepted event does fire
        // regardless of which child actually held focus (confirmed live too), so reading its current
        // Value there is the real "arrows+Enter: pick gender" confirm path.
        genderSelector.Accepted += (_, _) => ConfirmGender(genderSelector.Value ?? Gender.Male);

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
            // M/F hotkeys: OptionSelector<TEnum>'s whole API surface is Value/Values/ValueChanged -- no
            // per-item HotKey concept like the plain Views elsewhere in this project (e.g.
            // AnacreonTitleWindow's menu buttons), confirmed from its type definition. Its internal child
            // CheckBoxes are what actually hold focus, so a plain letter key only reaches here once
            // nothing under genderBox handles it itself -- the same bubbling this Window already relies
            // on for Esc below. Matched by character (key.AsRune), not KeyCode: KeyCode.M/F name the
            // physical key regardless of case (there's no separate lowercase enum member, and a
            // ShiftMask bit tracks case separately) -- comparing the actual typed character is what
            // guarantees both "m" and "M" work, which is what was actually asked for.
            if (genderBox.Visible && !key.IsCtrl && !key.IsAlt) {
                var ch = char.ToUpperInvariant((char)key.AsRune.Value);
                if (ch == 'M') {
                    ConfirmGender(Gender.Male);
                    key.Handled = true;
                    return;
                }

                if (ch == 'F') {
                    ConfirmGender(Gender.Female);
                    key.Handled = true;
                    return;
                }
            }

            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Esc) {
                return;
            }

            Dismiss();
            key.Handled = true;
        };

        Initialized += (_, _) => nameField.SetFocus();
    }
}
