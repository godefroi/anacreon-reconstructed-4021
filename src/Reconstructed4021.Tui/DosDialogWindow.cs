using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Generic styled replacement for a plain <c>MessageBox.Query</c> call -- Terminal.Gui's own
/// <c>MessageBox</c> has no color/scheme parameter at all (confirmed via dotnet-inspect, same finding
/// that led to <see cref="GameShell"/>'s own Attack-specific popup, its private <c>DosMessageWindow</c>
/// class), so every other confirm/notice dialog in this project was still rendering in Terminal.Gui's
/// default scheme, visually clashing with the DOS palette used everywhere else.
///
/// WND.PAS's own <c>AttentionWindow</c> (:558-614) is the real Pascal primitive every one of these
/// corresponds to: <c>ThinBRD</c> border, flat <c>C.SYSAttnWind</c>/<c>C.SYSWBorder</c> color -- both
/// LightGray on Black (confirmed against COLORS.INC's <c>ColorScrColor</c> table), distinct from the
/// Attack window's own blue content pane. Real Pascal's own confirm convention is "Esc cancels,
/// any other key confirms" (no title, no button widgets) -- this port keeps a real Y/N choice
/// instead, matching what every call site already got from <c>MessageBox.Query</c>'s own Yes/No
/// buttons before this, but Esc still cancels distinctly from a plain "No" (<see cref="ButtonIndex"/>
/// stays null), preserving the one real semantic distinction a caller can depend on (e.g.
/// <c>GameShell.BeginAttack</c>'s "Standard battle configuration?", where Esc abandons the attack
/// entirely and No opens Fleet Group Configuration) -- <c>MessageBox.Query</c>'s own null-vs-0-vs-1
/// return shape, unchanged, so callers converting from it don't need to change their own branching.
/// </summary>
public sealed class DosDialogWindow : Window
{
    private static readonly TgAttribute ContentAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSAttnWind = 7
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7

    public event EventHandler? Answered;

    /// <summary>Meaningful only when constructed with <c>isConfirm</c>: 0 = Yes/Enter, 1 = No, null = Esc -- matches <c>MessageBox.Query</c>'s own return shape exactly.</summary>
    public int? ButtonIndex { get; private set; }

    /// <param name="hint">
    /// Overrides the default Yes/No/Esc legend -- for a caller that's layered an extra key onto a
    /// confirm dialog beyond its own Y/N/Esc (see <c>GameShell.BeginAttack</c>'s own A: Auto Attack
    /// shortcut) and wants it shown, not hidden. Doesn't change what keys the dialog itself reacts to
    /// (still only Y/N/Enter/Esc here) -- purely cosmetic, the extra key is still handled by whatever
    /// the caller layers on top via its own <see cref="View.KeyDown"/> subscriber.
    /// </param>
    public DosDialogWindow(string title, string body, bool isConfirm = false, string? hint = null)
    {
        var lines = body.Split('\n');
        var hintText = hint ?? (isConfirm ? "(Y)es / (N)o   Esc: cancel" : "Press any key to continue...");
        var longest = new[] { title.Length, lines.Max(l => l.Length), hintText.Length }.Max();

        Title = title;
        Width = Math.Clamp(longest + 8, 40, 76);
        Height = lines.Length + 6;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single; // ThinBRD
        CanFocus = true;
        SetScheme(new Scheme(ContentAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        Add(new Label { X = 1, Y = 1, Width = Dim.Fill(1), Height = lines.Length, Text = body });
        Add(new Label {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Text = hintText,
        });

        var answered = false;
        KeyDown += (_, key) => {
            if (answered) {
                return;
            }

            if (!isConfirm) {
                answered = true;
                key.Handled = true;
                Answered?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                answered = true;
                key.Handled = true;
                ButtonIndex = null;
                Answered?.Invoke(this, EventArgs.Empty);
                return;
            }

            var ch = char.ToUpperInvariant((char)key.AsRune.Value);
            if (ch != 'Y' && ch != 'N' && key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Enter) {
                return; // unrecognized key -- ignored, matches MessageBox's own behavior of staying open
            }

            answered = true;
            key.Handled = true;
            ButtonIndex = ch == 'N' ? 1 : 0;
            Answered?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// Plain "press any key" notice, driven via a nested <c>Application.Run</c> -- the same mechanism
    /// <c>MessageBox.Query</c> itself already used at every one of these call sites, so a hand-rolled
    /// version carries no new reentrancy risk. For use from a context with no enclosing
    /// <c>GameShell</c>-style <c>AddModal</c> child-popup system (<c>Program.cs</c>'s top-level script,
    /// <c>AnacreonTitleWindow</c>, <c>TacticalBattleDisplayWindow</c>'s own inline result screen).
    /// </summary>
    public static void ShowInfo(IApplication app, string title, string body)
    {
        var dialog = new DosDialogWindow(title, body);
        dialog.Answered += (_, _) => app.RequestStop();
        app.Run(dialog, null);
    }

    /// <summary>Yes/No/Esc confirm, blocking -- same null/0/1 return shape as <c>MessageBox.Query</c>.</summary>
    public static int? Confirm(IApplication app, string title, string body)
    {
        var dialog = new DosDialogWindow(title, body, isConfirm: true);
        dialog.Answered += (_, _) => app.RequestStop();
        app.Run(dialog, null);
        return dialog.ButtonIndex;
    }
}
