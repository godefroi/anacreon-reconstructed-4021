using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace ThreeLn.Reconstruction4021.Tui;

/// <summary>
/// PROLOG.PAS's MainTitle/ZoomOutSFX plus the ambient orbiting-stars decoration
/// (InitStarArray/UpdateStarArray) that normally runs continuously behind the DOS pre-game main menu.
/// We don't have that menu screen yet (docs/TUI_SURFACES_MAPPING.md's "Pre-game setup" -- a separate,
/// unscoped backlog item), so this plays the reveal once and lets the orbit run for a few seconds
/// before continuing, rather than tying it to a menu-input loop that doesn't exist yet.
/// </summary>
/// <remarks>
/// A deliberate departure from PROLOG.PAS here, not a literal port: the original flies the word in via
/// BITPIC.INC's 4 hand-drawn bitmap frames (a real zoom, but only 4 discrete images -- always going to
/// read as a jump-cut on modern hardware, not a smooth animation, however long each frame is held), and
/// only starts the orbiting stars afterward, in the outer menu loop. Replaced with one continuous,
/// formula-driven system: the same 12 stars (cycling '∙ o ☼ o' -- CP437 15, the sunburst, is the 3rd
/// frame) orbit the title from the very first frame, easing their radius out from 0 to its resting size
/// -- the "fly-in" -- with the title text itself static at its final position throughout. Once the ring
/// reaches full size, PROLOG.PAS's white highlight band sweeps across the text exactly as before, while
/// the ring keeps spinning uninterrupted underneath/around it. The ring is drawn in two passes so it
/// passes behind the far side of the text and in front of the near side, rather than always drawing flat
/// on top of it.
/// </remarks>
internal sealed class AnacreonTitleWindow : Window
{
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

    // COLORS.INC: Title1 = 4 -> Red on Black (same red as the TMA logo). The orbiting stars use the
    // inline assembly's hardcoded "bright" attribute 0x0F -> White on Black.
    private static readonly TgAttribute TitleAttribute = new(StandardColor.Red, StandardColor.Black);
    private static readonly TgAttribute BandAttribute = new(StandardColor.White, StandardColor.Black);
    private static readonly TgAttribute StarAttribute = new(StandardColor.White, StandardColor.Black);

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
    private const int OrbitHoldTicks = 150; // ~4.5s of ambient orbit, after the band sweep, at the tick interval below.
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
    private int _orbitTicksLeft = OrbitHoldTicks;
    private int _tickCount;
    private readonly Star[] _stars = new Star[OrbitStars];

    private int _titleX;
    private int _titleY;

    // Every timer currently scheduled, so a keypress dismissal can cancel all of them -- otherwise an
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
        KeyDown += (_, _) => Dismiss();

        for (var i = 0; i < OrbitStars; i++) {
            _stars[i] = new Star { Angle = i * 2 * Math.PI / OrbitStars, GlyphOffset = i };
        }

        // App isn't assigned yet during construction (only once Application.Run begins this window's
        // session), so the first tick has to wait for Initialized rather than starting here.
        Initialized += (_, _) => ScheduleTimeout(TickInterval, OrbitTick);
    }

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
    // regardless of phase, and separately drives whichever phase-specific progress is current.
    private bool OrbitTick()
    {
        _tickCount++;

        foreach (var star in _stars) {
            star.Angle += OrbitAngleStep;
        }

        switch (_phase) {
            case Phase.FlyIn:
                _flyInElapsedMs += TickInterval.TotalMilliseconds;
                if (_flyInElapsedMs >= FlyInDurationMs) {
                    _phase = Phase.Band;
                    AdvanceBand();
                }

                break;

            case Phase.Orbit:
                if (--_orbitTicksLeft <= 0) {
                    SetNeedsDraw();
                    Dismiss();
                    return false;
                }

                break;
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
