using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace ThreeLn.Reconstruction4021.Tui;

/// <summary>
/// PROLOG.PAS's MainTitle/ZoomOutSFX plus the ambient orbiting-stars decoration
/// (InitStarArray/UpdateStarArray) that runs continuously behind the DOS pre-game main menu, plus a menu
/// of our own: New Game/Load Game/Options/Quit. PROLOG.PAS's <c>Prologue</c> drives the real thing with a
/// Terminal.Gui-unrelated MenuBar type (InitializeMenuBar/AddBarItem/AddBarMenuItem) with four top-level
/// items (⌂/Game/Options/Configure) covering ~20 commands total -- config toggles, save-game slots,
/// multi-empire setup -- most of which this project has no backing feature for yet, so reproducing that
/// exact menu shape isn't worth it before those features exist; see docs/TUI_SURFACES_MAPPING.md's
/// "Pre-game setup" entry. Load Game/Options are still stubs; New Game and Quit are wired up (see
/// <see cref="Choice"/>).
/// </summary>
/// <remarks>
/// A deliberate departure from PROLOG.PAS here, not a literal port: the original flies the word in via
/// BITPIC.INC's 4 hand-drawn bitmap frames (a real zoom, but only 4 discrete images -- always going to
/// read as a jump-cut on modern hardware, not a smooth animation, however long each frame is held), and
/// only starts the orbiting stars afterward, in the outer menu loop. Replaced with one continuous,
/// formula-driven system: the same 12 stars (cycling '∙ o ☼ o' -- CP437 15, the sunburst, is the 3rd
/// frame) orbit the title from the very first frame, easing their radius out from 0 to its resting size
/// -- the "fly-in" -- with the title text itself static at its final position throughout. Once the ring
/// reaches full size, PROLOG.PAS's white highlight band sweeps across the text exactly as before, then
/// the ring keeps spinning indefinitely -- ambient decoration behind the menu, same as the original,
/// rather than stopping after a fixed hold and auto-continuing (which was fine when there was no menu
/// here to interact with, but isn't once there is one). The ring is drawn in two passes so it passes
/// behind the far side of the text and in front of the near side, rather than always drawing flat on top
/// of it.
/// </remarks>
internal sealed class AnacreonTitleWindow : Window
{
    public enum MenuChoice { None, NewGame, Quit }

    /// <summary>Which menu item ended the window's run -- <see cref="MenuChoice.None"/> if it's still showing (Load Game/Options don't dismiss it).</summary>
    public MenuChoice Choice { get; private set; }

    // PROLOG.PAS's TitleAnacreon (CP437-decoded -- see the project's CP437 caveat).
    private static readonly string[] TitleText = [
        "        █                                                                ",
        "       ███                                                               ",
        "      █ ▀██                                                              ",
        "     █   ▀██      ▀██▄▀▀█▄  ▄▀▀▀█▄  ▄█▀▀▀▄ ██▄▀▀ ▄█▀▀▀▄  ▄█▀▀█▄ ▀██▄▀▀█▄ ",
        "    █▄▄▄▄▄███      ██   ██    ▄▄██ ██      ██   ██▄▄▄▄▀ ██    ██ ██   ██ ",
        "   █       ▀██     ██   ██  ▄█▀ ██ ██      ██   ██      ██    ██ ██   ██ ",
        "▄▄█▄      ▄▄███▄▄ ▄██▄  ██▄ ▀█▄█▀█▄ ▀█▄▄▄▀ ██    ▀█▄▄▄▀  ▀█▄▄█▀ ▄██▄  ██▄",
    ];

    // COLORS.INC: Title1 = 4 -> Red on Black (same red as the TMA logo, and as PROLOG.PAS's own
    // WriteString(...,C.Title1) calls for the version/copyright lines below). The orbiting stars use the
    // inline assembly's hardcoded "bright" attribute 0x0F -> White on Black.
    private static readonly TgAttribute TitleAttribute = new(StandardColor.Red, StandardColor.Black);
    private static readonly TgAttribute BandAttribute = new(StandardColor.White, StandardColor.Black);
    private static readonly TgAttribute StarAttribute = new(StandardColor.White, StandardColor.Black);

    // Red on black, same as the title text and copyright lines below (TitleAttribute) -- no Pascal
    // equivalent for a widget like this (see the class doc comment), so matching this screen's own
    // existing color reads better than introducing a new one. Focus brightens it (BrightRed) rather than
    // inverting to a light background -- black stays the background in both states, so the bright-yellow
    // hotkey letter stays legible either way (a white focus background is what made it unreadable before).
    private static readonly Scheme MenuButtonScheme = new() {
        Normal = new TgAttribute(StandardColor.Red, StandardColor.Black),
        Focus = new TgAttribute(StandardColor.BrightRed, StandardColor.Black),
        HotNormal = new TgAttribute(StandardColor.BrightYellow, StandardColor.Black),
        HotFocus = new TgAttribute(StandardColor.BrightYellow, StandardColor.Black),
    };

    private static readonly char[] OrbitGlyphs = ['∙', 'o', '☼', 'o'];

    private const int OrbitStars = 12;

    // A vertical ring (not the previous horizontal one), viewed obliquely rather than face-on or
    // edge-on: 0 degrees would be a flat vertical line (pure edge-on), 90 would be a full face-on circle
    // with no depth cue at all. RadiusRows is the ring's true, unsquashed vertical extent, scaled off the
    // text's own height (rather than a bare magic number) so it stays proportioned if the text ever
    // changes -- sized as a multiple of the full height, not just half of it, so the ring genuinely
    // encircles the word with room to spare instead of just touching its top/bottom edges. RadiusCols is
    // *derived* from that (via the cell-aspect correction and the view angle's squash) rather than a
    // separately tuned number, so the shape stays principled if either constant below changes.
    private const double OrbitRadiusRowsMultiplier = 1.3;
    private static readonly double OrbitRadiusRows = TitleText.Length * OrbitRadiusRowsMultiplier;
    private const double OrbitCellAspectRatio = 2.0; // terminal character cells are roughly twice as tall as wide.
    private const double OrbitViewAngleDegrees = 65;
    private static readonly double OrbitRadiusCols = OrbitRadiusRows * OrbitCellAspectRatio * Math.Cos(OrbitViewAngleDegrees * Math.PI / 180.0);
    private const double OrbitLapMs = 3000; // time for one full revolution once at full radius.
    private const int OrbitGlyphTicksPerFrame = 4; // how many ticks each of the 4 glyphs holds for -- cycling every tick would flicker too fast to read as a twinkle.
    private const double FlyInDurationMs = 1800; // time for the ring to ease from radius 0 out to full size -- the "fly-in".
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(30);
    private static readonly double OrbitAngleStep = 2 * Math.PI / (OrbitLapMs / TickInterval.TotalMilliseconds);

    private enum Phase { FlyIn, Band, Orbit }

    private sealed class Star
    {
        public double Angle;
        public int GlyphOffset; // staggers which glyph each star shows at a given tick, so they twinkle independently rather than in lockstep.
    }

    private Phase _phase = Phase.FlyIn;
    private double _flyInElapsedMs;
    private int _bandColumn;
    private int _tickCount;
    private readonly Star[] _stars = new Star[OrbitStars];

    private int _titleX;
    private int _titleY;

    // Every timer currently scheduled, so choosing New Game/Quit can cancel all of them -- otherwise an
    // abandoned one fires later against whatever window happens to be running by then. See
    // TmaLogoWindow's Dismiss for the full explanation of why this matters. This window can have two
    // concurrently pending (the ongoing orbit ticker, plus the band sweep's own chain while it runs).
    private readonly List<object> _pendingTimeouts = [];

    public AnacreonTitleWindow()
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        BorderStyle = LineStyle.None;
        Border.Thickness = new Thickness(0);
        SetScheme(new Scheme(new TgAttribute(StandardColor.Black, StandardColor.Black)));

        DrawingContent += OnDrawingContent;

        for (var i = 0; i < OrbitStars; i++) {
            _stars[i] = new Star { Angle = i * 2 * Math.PI / OrbitStars, GlyphOffset = i };
        }

        var newGameButton = CreateMenuButton(0, "_New Game", Key.N, () => Choose(MenuChoice.NewGame));
        Add(newGameButton);
        Add(CreateMenuButton(1, "_Load Game", Key.L, () => Stub("Load Game")));
        Add(CreateMenuButton(2, "_Options", Key.O, () => Stub("Options")));
        Add(CreateMenuButton(3, "_Quit", Key.Q, () => Choose(MenuChoice.Quit)));

        var versionLabel = new Label { X = Pos.Center(), Y = Pos.AnchorEnd(3), Text = "Reconstruction 4021" };
        var copyrightLabel = new Label { X = Pos.Center(), Y = Pos.AnchorEnd(2), Text = "(c) Copyright 1990 by T M A   All Rights Reserved" };
        versionLabel.SetScheme(new Scheme(TitleAttribute));
        copyrightLabel.SetScheme(new Scheme(TitleAttribute));
        Add(versionLabel);
        Add(copyrightLabel);

        // Left/Right cycle focus among the four menu buttons -- they're the only focusable subviews here,
        // so this is unambiguous. Attached to the Window (not each button) for the same reason GameShell's
        // Esc handler is: unhandled key events bubble up from whichever button currently has focus.
        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorLeft:
                    AdvanceFocus(NavigationDirection.Backward, null);
                    key.Handled = true;
                    break;
                case KeyCode.CursorRight:
                    AdvanceFocus(NavigationDirection.Forward, null);
                    key.Handled = true;
                    break;
            }
        };

        // App isn't assigned yet during construction (only once Application.Run begins this window's
        // session), so the first tick has to wait for Initialized rather than starting here.
        Initialized += (_, _) => {
            ScheduleTimeout(TickInterval, OrbitTick);
            newGameButton.SetFocus();
        };
    }

    /// <summary>Four equal-width framed cells tiling one row -- Pos/Dim percentages do the layout, not manual column arithmetic. A 1-column inset on each side keeps adjacent buttons' borders from touching.</summary>
    private View CreateMenuButton(int index, string text, Key hotKey, Action action)
    {
        var button = new View {
            X = Pos.Percent(index * 25) + 1,
            Y = 1,
            Width = Dim.Percent(25) - 2,
            Height = 5,
            Text = text,
            TextAlignment = Alignment.Center,
            VerticalTextAlignment = Alignment.Center,
            BorderStyle = LineStyle.Double, // a classic DOS-dialog look, and reads as heavier/bolder than a plain single line without needing a custom glyph set.
            CanFocus = true,
            HotKey = hotKey,
        };
        button.SetScheme(MenuButtonScheme);

        button.KeyDown += (_, key) => {
            if (key.KeyCode is KeyCode.Enter or KeyCode.Space) {
                action();
                key.Handled = true;
            }
        };
        button.MouseEvent += (_, mouse) => {
            if (mouse.IsSingleClicked) {
                action();
                mouse.Handled = true;
            }
        };
        // The base View's default hot-key handling only moves focus; this is what actually fires the
        // button's action on a bare N/L/O/Q press, regardless of which button currently has focus.
        button.HotKeyCommand += (_, _) => action();

        return button;
    }

    private void Choose(MenuChoice choice)
    {
        Choice = choice;
        Dismiss();
    }

    private void Stub(string label) => MessageBox.Query(App!, label, "Not yet implemented.", "OK");

    private object ScheduleTimeout(TimeSpan delay, Func<bool> callback)
    {
        var token = App!.AddTimeout(delay, callback)!;
        _pendingTimeouts.Add(token);
        return token;
    }

    private void Dismiss()
    {
        foreach (var token in _pendingTimeouts) {
            App?.RemoveTimeout(token);
        }

        _pendingTimeouts.Clear();
        App?.RequestStop();
    }

    // Runs continuously from the first frame until the window closes -- spins the ring every tick
    // regardless of phase, and separately drives whichever phase-specific progress is current. Never
    // stops itself: unlike a one-shot splash, this screen's ambient animation is meant to keep running
    // behind the menu for as long as the menu is up (see the class doc comment).
    private bool OrbitTick()
    {
        _tickCount++;

        foreach (var star in _stars) {
            star.Angle += OrbitAngleStep;
        }

        if (_phase == Phase.FlyIn) {
            _flyInElapsedMs += TickInterval.TotalMilliseconds;
            if (_flyInElapsedMs >= FlyInDurationMs) {
                _phase = Phase.Band;
                AdvanceBand();
            }
        }

        SetNeedsDraw();
        return true;
    }

    private void AdvanceBand()
    {
        SetNeedsDraw();
        _bandColumn++;

        if (_bandColumn <= TitleText[0].Length) {
            ScheduleTimeout(TimeSpan.FromMilliseconds(15), () => {
                AdvanceBand();
                return false;
            });
        } else {
            _phase = Phase.Orbit;
        }
    }

    // Cubic ease-out: fast start, gentle settle -- reads as the ring arriving and easing into place
    // rather than mechanically ramping at a constant rate.
    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    private void OnDrawingContent(object? sender, DrawEventArgs e)
    {
        e.Cancel = true;

        // Recomputed every frame rather than once: Frame.Width/Height aren't guaranteed populated the
        // very first time this runs (this view's own first layout pass isn't guaranteed to have happened
        // yet at that exact point in the startup sequence) -- recomputing here self-corrects on whichever
        // frame first sees a real size, instead of gambling on one read timed exactly right.
        _titleX = Math.Max(0, (Frame.Width - TitleText[0].Length) / 2);
        _titleY = Math.Max(0, (Frame.Height - TitleText.Length) / 2);

        // The text occupies rows _titleY.._titleY+6 (7 rows) and columns _titleX.._titleX+len-1 -- the
        // midpoint of that span is at index (count-1)/2, not count/2. Using count/2 here previously put
        // the ring's center half a row too low, which Math.Round below then rounds a good many stars a
        // full row off from where they belong -- reading as the whole ring sitting oddly high relative
        // to the text, with its "front" half cutting across the text's actual bottom edge.
        var centerX = _titleX + (TitleText[0].Length - 1) / 2.0;
        var centerY = _titleY + (TitleText.Length - 1) / 2.0;
        var radiusScale = _phase == Phase.FlyIn ? EaseOutCubic(Math.Min(1.0, _flyInElapsedMs / FlyInDurationMs)) : 1.0;

        DrawStars(centerX, centerY, radiusScale, drawBackHalf: true);
        DrawTitle();
        DrawStars(centerX, centerY, radiusScale, drawBackHalf: false);
    }

    private void DrawTitle()
    {
        for (var row = 0; row < TitleText.Length; row++) {
            var line = TitleText[row];

            for (var col = 0; col < line.Length; col++) {
                // Skip blank cells rather than drawing a space over them: this is what lets a back-half
                // star drawn just before this method show through everywhere except where a real glyph
                // stroke sits. Drawing every cell unconditionally (including blanks) would repaint the
                // text's entire rectangular bounding box, erasing any back star underneath it regardless
                // of whether that particular cell was ever an actual letter -- which is what made the
                // whole back half vanish once its row range came to match the text's own.
                if (line[col] == ' ') {
                    continue;
                }

                var inBand = _phase == Phase.Band && col >= _bandColumn - 3 && col < _bandColumn;
                SetAttribute(inBand ? BandAttribute : TitleAttribute);
                Move(_titleX + col, _titleY + row);
                AddRune(new Rune(line[col]));
            }
        }
    }

    // The ring passes behind the far half of the title and in front of the near half, rather than
    // flatly on top of it -- drawn in two passes, with DrawTitle in between. For this vertical, obliquely
    // viewed ring, Cos(Angle) is the squashed (depth) axis, so its sign is what now doubles as depth:
    // stars on the far side (the ring's back, going away from the viewer) render first, so the title
    // covers them; stars on the near side render last, on top of the title.
    private void DrawStars(double centerX, double centerY, double radiusScale, bool drawBackHalf)
    {
        SetAttribute(StarAttribute);

        foreach (var star in _stars) {
            var isBack = Math.Cos(star.Angle) < 0;
            if (isBack != drawBackHalf) {
                continue;
            }

            var x = (int)Math.Round(centerX + Math.Cos(star.Angle) * OrbitRadiusCols * radiusScale);
            var y = (int)Math.Round(centerY + Math.Sin(star.Angle) * OrbitRadiusRows * radiusScale);
            if (x < 0 || y < 0 || x >= Frame.Width || y >= Frame.Height) {
                continue;
            }

            Move(x, y);
            AddRune(new Rune(OrbitGlyphs[((_tickCount / OrbitGlyphTicksPerFrame) + star.GlyphOffset) % OrbitGlyphs.Length]));
        }
    }
}
