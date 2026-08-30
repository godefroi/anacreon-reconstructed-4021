using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// TMA.PAS's TMALogo: the one-time "presents" splash shown before the main menu. Reproduces the real
/// ASCII-art logo (decoded from TMA.PAS's CP437 source bytes -- reading it through normal tooling
/// corrupts those bytes, see the project's CP437 caveat) and its left-to-right reveal animation; skips
/// the original's subsequent 13-column slide-right flourish (drawn once already at its final resting,
/// horizontally-centered position instead) as low-value to reproduce exactly, and because the original's
/// fixed column-13 placement assumed an 80-column screen this port doesn't have.
/// </summary>
/// <remarks>
/// Dismissed by any keypress, or automatically after the reveal plus a 5-second hold (TMA.PAS's
/// <c>Wait(5, Ch)</c>). Unlike the original, a keypress can skip the reveal animation itself too --
/// forcing the player to sit through an unskippable animation is a DOS-era artifact, not a deliberate
/// design choice worth preserving.
/// </remarks>
internal sealed class TmaLogoWindow : Window
{
    private static readonly string[] Logo = [
        " █▀▀▀▀▀██▀▀▀▀▀█ ▀▀██▄         ██▀▀         █        ",
        " ▀     ██     ▀   ███▄       ███          ███       ",
        "       ██         █ ██▄     █ ██         █ ▀██      ",
        "       ██         █  ██▄   █  ██        █   ▀██     ",
        "       ██         █   ██▄ █   ██       █▄▄▄▄▄███    ",
        "       ██         █    ███    ██      █       ▀██   ",
        "     ▄▄██▄▄     ▄▄█▄    █   ▄▄██▄▄ ▄▄█▄      ▄▄███▄▄",
    ];

    private readonly Label _logoLabel = new() { Y = Pos.Center() - 4 };
    private readonly Label _presentsLabel = new() { X = Pos.Center(), Y = Pos.Center() + 4, Text = "Presents", Visible = false };
    private int _revealedColumns;

    // Whatever's currently scheduled, so a keypress dismissal can cancel it -- otherwise it fires later
    // against whatever window happens to be running by then (this is what made the app quit a few
    // seconds after reaching the map when a key skipped an earlier screen: the abandoned 5-second hold
    // timer below fired anyway, calling RequestStop on the map instead of this dismissed window).
    private object? _pendingTimeout;

    public TmaLogoWindow()
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        BorderStyle = LineStyle.None;
        Border.Thickness = new Thickness(0);

        // Without an explicit Scheme, the Window's own background falls back to the terminal's actual
        // default (Color.None), which reads as a different shade than the Labels' explicit pure black --
        // showing up as a visibly darker box behind the logo text. Setting it here keeps the whole
        // screen one uniform black, matching TMALogo's ClrScr.
        SetScheme(new Scheme(new TgAttribute(StandardColor.Black, StandardColor.Black)));
        _logoLabel.SetScheme(new Scheme(new TgAttribute(StandardColor.Red, StandardColor.Black)));
        _presentsLabel.SetScheme(new Scheme(new TgAttribute(StandardColor.LightCyan, StandardColor.Black)));

        Add(_logoLabel);
        Add(_presentsLabel);

        KeyDown += (_, _) => Dismiss();

        // App isn't assigned yet during construction (only once Application.Run begins this window's
        // session), so the timer has to wait for Initialized rather than starting here.
        Initialized += (_, _) => { _pendingTimeout = App!.AddTimeout(TimeSpan.FromMilliseconds(30), RevealTick); };
    }

    private void Dismiss()
    {
        if (_pendingTimeout is { } token) {
            App?.RemoveTimeout(token);
            _pendingTimeout = null;
        }

        App?.RequestStop();
    }

    private bool RevealTick()
    {
        // TMALogo always draws at a fixed column (1 in the original); each frame's growing string is
        // redrawn from there, which is what makes the reveal read as characters marching in from the
        // left rather than a static wipe -- see UpdateLogoText. Pos.Center() would recompute every frame
        // as the string grows, defeating that: it has to be a value fixed once the FINAL full-width logo
        // is known, so it still ends up centered rather than pinned to column 1.
        //
        // Recomputed every tick rather than once at Initialized: Frame.Width isn't reliable that early
        // -- this view's own first layout pass hasn't necessarily run yet, so it can read as 0 and never
        // get revisited, pinning the whole reveal to the left edge. By the time a timer callback fires
        // (even the very first one), at least one real layout pass is far more likely to have happened;
        // recomputing here rather than gambling on exactly when is what actually guarantees correctness.
        _logoLabel.X = Math.Max(0, (Frame.Width - Logo[0].Length) / 2);

        _revealedColumns += 2;
        UpdateLogoText();

        if (_revealedColumns < Logo[0].Length) {
            return true;
        }

        _presentsLabel.Visible = true;
        _pendingTimeout = App!.AddTimeout(TimeSpan.FromSeconds(5), () => {
            _pendingTimeout = null;
            App?.RequestStop();
            return false;
        });

        return false;
    }

    // TMALogo's loop (TMA.PAS:62-69) builds each frame by *prepending* the next character to the left
    // of the previous frame's string -- i.e. each frame shows a growing suffix of the final line, always
    // redrawn from the same starting column. A character enters at that column the frame it's first
    // revealed, then appears to march rightward on every later frame as more get prepended before it --
    // not a simple left-to-right wipe of the final image (which is what a growing prefix, instead of a
    // growing suffix, would give you).
    private void UpdateLogoText() =>
        _logoLabel.Text = string.Join('\n', Logo.Select(line => line[Math.Max(0, line.Length - _revealedColumns)..]));
}
