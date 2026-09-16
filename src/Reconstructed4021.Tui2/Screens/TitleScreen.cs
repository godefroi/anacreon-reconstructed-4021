using System.Text;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;
using Reconstructed4021.Tui2.NewGame;


namespace Reconstructed4021.Tui2.Screens;


// PROLOG.PAS's MainTitle/ZoomOutSFX plus the ambient orbiting-stars decoration (InitStarArray/
// UpdateStarArray) that runs continuously behind the DOS pre-game main menu, plus a menu of our own:
// New Game/Load Game/Options/Quit. New Game, Load Game, and Options have no screen to hand off to yet
// (only this and the TMA splash exist so far) -- their buttons are wired up and focusable, but only
// Quit actually transitions anywhere.
//
// A deliberate departure from PROLOG.PAS, not a literal port: the original flies the word in via 4
// hand-drawn bitmap frames (always a jump-cut, however long each is held), and only starts the
// orbiting stars afterward. Replaced with one continuous, formula-driven system: the same 12 stars
// (cycling glyphs, the 3rd being a sunburst) orbit the title from the very first frame, easing their
// radius out from 0 to its resting size -- the "fly-in" -- with the title text itself static at its
// final position throughout. Once the ring reaches full size, a white highlight band sweeps across the
// text, then the ring keeps spinning indefinitely. The ring is drawn in two passes so it passes behind
// the far side of the text and in front of the near side, rather than flatly on top of it.
public sealed class TitleScreen : IScreen
{
    private static readonly string[] TitleText = [
        "        █                                                                ",
        "       ███                                                               ",
        "      █ ▀██                                                              ",
        "     █   ▀██      ▀██▄▀▀█▄  ▄▀▀▀█▄  ▄█▀▀▀▄ ██▄▀▀ ▄█▀▀▀▄  ▄█▀▀█▄ ▀██▄▀▀█▄ ",
        "    █▄▄▄▄▄███      ██   ██    ▄▄██ ██      ██   ██▄▄▄▄▀ ██    ██ ██   ██ ",
        "   █       ▀██     ██   ██  ▄█▀ ██ ██      ██   ██      ██    ██ ██   ██ ",
        "▄▄█▄      ▄▄███▄▄ ▄██▄  ██▄ ▀█▄█▀█▄ ▀█▄▄▄▀ ██    ▀█▄▄▄▀  ▀█▄▄█▀ ▄██▄  ██▄",
    ];

    // Calibrated CGA red is a muted dark red, not the vivid one -- ConsoleColor.DarkRed is close
    // enough for this 16-color renderer without adding truecolor support (Cell/FrameBuffer only carry
    // ConsoleColor today); ConsoleColor.Red is the brighter variant used for focus.
    private static readonly ConsoleColor TitleColor = ConsoleColor.DarkRed;
    private static readonly ConsoleColor BandColor = ConsoleColor.White;
    private static readonly ConsoleColor StarColor = ConsoleColor.White;
    private static readonly ConsoleColor ButtonNormalColor = ConsoleColor.DarkRed;
    private static readonly ConsoleColor ButtonFocusColor = ConsoleColor.Red;
    private static readonly ConsoleColor ButtonHotColor = ConsoleColor.Yellow;
    private const ConsoleColor Bg = ConsoleColor.Black;

    private static readonly char[] OrbitGlyphs = ['∙', 'o', '☼', 'o'];
    private const int OrbitStars = 12;

    // A vertical ring, viewed obliquely: 0 degrees would be a flat vertical line (edge-on), 90 a full
    // face-on circle with no depth cue. RadiusRows is the ring's true vertical extent, scaled off the
    // text's own height; RadiusCols is derived from it via the cell-aspect correction and the view
    // angle's squash, rather than a separately tuned number.
    private const double OrbitRadiusRowsMultiplier = 1.3;
    private static readonly double OrbitRadiusRows = TitleText.Length * OrbitRadiusRowsMultiplier;
    private const double OrbitCellAspectRatio = 2.0; // terminal character cells are roughly twice as tall as wide.
    private const double OrbitViewAngleDegrees = 65;
    private static readonly double OrbitRadiusCols = OrbitRadiusRows * OrbitCellAspectRatio * Math.Cos(OrbitViewAngleDegrees * Math.PI / 180.0);
    private const double OrbitLapMs = 3000; // time for one full revolution once at full radius.
    private const int OrbitGlyphTicksPerFrame = 4; // ticks each glyph holds for -- cycling every tick would flicker too fast to read as a twinkle.
    private const double FlyInDurationMs = 1800; // time for the ring to ease from radius 0 out to full size.
    private const double OrbitTickMs = 30;
    private const double BandTickMs = 15;
    private static readonly double OrbitAngleStep = 2 * Math.PI / (OrbitLapMs / OrbitTickMs);

    private enum Phase { FlyIn, Band, Orbit }

    private sealed class Star
    {
        public double Angle;
        public int GlyphOffset; // staggers which glyph each star shows at a given tick, so they twinkle independently rather than in lockstep.
    }

    private sealed record MenuButton(string Label, char HotKey, Action Activate);

    private readonly MenuButton[] _buttons;
    private int _focusedIndex;

    private Phase _phase = Phase.FlyIn;
    private double _orbitAccumulatorMs;
    private double _bandAccumulatorMs;
    private double _flyInElapsedMs;
    private int _bandColumn;
    private int _tickCount;
    private readonly Star[] _stars = new Star[OrbitStars];

    public IScreen? NextScreen { get; private set; }

    public TitleScreen(NewGameContext context)
    {
        for (var i = 0; i < OrbitStars; i++)
        {
            _stars[i] = new Star { Angle = i * 2 * Math.PI / OrbitStars, GlyphOffset = i };
        }

        _buttons = [
            // LoadScenarios() runs the scenario-directory scan lazily, right here, only once the player
            // actually picks New Game -- see NewGameContext's own doc comment for why that's deferred
            // this far rather than done once up front.
            new MenuButton("New Game", 'N', () => NextScreen = new ScenarioPickerScreen(context.LoadScenarios(), context)),
            new MenuButton("Load Game", 'L', () => NextScreen = new SaveGamePickerScreen(context.LoadSaves(), context)),
            new MenuButton("Options", 'O', () => { }), // ponytail: no Options screen yet, wire up when it exists.
            new MenuButton("Quit", 'Q', () => NextScreen = QuitScreen.Instance),
        ];
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _focusedIndex = (_focusedIndex + _buttons.Length - 1) % _buttons.Length;
                return;
            case ConsoleKey.RightArrow:
                _focusedIndex = (_focusedIndex + 1) % _buttons.Length;
                return;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                _buttons[_focusedIndex].Activate();
                return;
        }

        var typed = char.ToUpperInvariant(key.KeyChar);
        foreach (var button in _buttons)
        {
            if (char.ToUpperInvariant(button.HotKey) == typed)
            {
                button.Activate();
                return;
            }
        }
    }

    public void Update(TimeSpan elapsed)
    {
        _orbitAccumulatorMs += elapsed.TotalMilliseconds;
        while (_orbitAccumulatorMs >= OrbitTickMs)
        {
            _orbitAccumulatorMs -= OrbitTickMs;
            OrbitTick();
        }

        if (_phase != Phase.Band)
        {
            _bandAccumulatorMs = 0;
            return;
        }

        _bandAccumulatorMs += elapsed.TotalMilliseconds;
        while (_bandAccumulatorMs >= BandTickMs && _phase == Phase.Band)
        {
            _bandAccumulatorMs -= BandTickMs;
            AdvanceBand();
        }
    }

    // Runs every tick regardless of phase -- the ring keeps spinning behind the menu for as long as
    // this screen is up.
    private void OrbitTick()
    {
        _tickCount++;

        foreach (var star in _stars)
        {
            star.Angle += OrbitAngleStep;
        }

        if (_phase == Phase.FlyIn)
        {
            _flyInElapsedMs += OrbitTickMs;
            if (_flyInElapsedMs >= FlyInDurationMs)
            {
                _phase = Phase.Band;
                AdvanceBand();
            }
        }
    }

    private void AdvanceBand()
    {
        _bandColumn++;

        if (_bandColumn > TitleText[0].Length)
        {
            _phase = Phase.Orbit;
        }
    }

    // Cubic ease-out: fast start, gentle settle -- reads as the ring arriving and easing into place
    // rather than mechanically ramping at a constant rate.
    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    public void Draw(FrameBuffer fb)
    {
        fb.Clear(new Cell(new Rune(' '), ConsoleColor.Gray, Bg));

        var titleX = Math.Max(0, (fb.Width - TitleText[0].Length) / 2);
        var titleY = Math.Max(0, (fb.Height - TitleText.Length) / 2);

        // The text occupies rows titleY..titleY+6 (7 rows) and columns titleX..titleX+len-1 -- the
        // midpoint of that span is at index (count-1)/2, not count/2.
        var centerX = titleX + (TitleText[0].Length - 1) / 2.0;
        var centerY = titleY + (TitleText.Length - 1) / 2.0;
        var radiusScale = _phase == Phase.FlyIn ? EaseOutCubic(Math.Min(1.0, _flyInElapsedMs / FlyInDurationMs)) : 1.0;

        DrawStars(fb, centerX, centerY, radiusScale, drawBackHalf: true);
        DrawTitle(fb, titleX, titleY);
        DrawStars(fb, centerX, centerY, radiusScale, drawBackHalf: false);

        for (var i = 0; i < _buttons.Length; i++)
        {
            DrawButton(fb, i);
        }

        const string version = "Reconstruction 4021";
        const string copyright = "(c) Copyright 1990 by T M A   All Rights Reserved";
        fb.DrawText((fb.Width - version.Length) / 2, fb.Height - 3, version, TitleColor, Bg);
        fb.DrawText((fb.Width - copyright.Length) / 2, fb.Height - 2, copyright, TitleColor, Bg);
    }

    private void DrawTitle(FrameBuffer fb, int titleX, int titleY)
    {
        for (var row = 0; row < TitleText.Length; row++)
        {
            var line = TitleText[row];
            for (var col = 0; col < line.Length; col++)
            {
                // Skip blank cells rather than drawing a space over them -- this is what lets a
                // back-half star drawn just before this show through everywhere except where a real
                // glyph stroke sits.
                if (line[col] == ' ')
                {
                    continue;
                }

                var inBand = _phase == Phase.Band && col >= _bandColumn - 3 && col < _bandColumn;
                fb.Set(titleX + col, titleY + row, new Cell(new Rune(line[col]), inBand ? BandColor : TitleColor, Bg));
            }
        }
    }

    // The ring passes behind the far half of the title and in front of the near half, rather than
    // flatly on top of it. For this vertical, obliquely viewed ring, Cos(Angle) is the squashed (depth)
    // axis, so its sign doubles as depth: far-side stars render first (the title covers them),
    // near-side stars render last (on top of the title).
    private void DrawStars(FrameBuffer fb, double centerX, double centerY, double radiusScale, bool drawBackHalf)
    {
        foreach (var star in _stars)
        {
            var isBack = Math.Cos(star.Angle) < 0;
            if (isBack != drawBackHalf)
            {
                continue;
            }

            var x = (int)Math.Round(centerX + (Math.Cos(star.Angle) * OrbitRadiusCols * radiusScale));
            var y = (int)Math.Round(centerY + (Math.Sin(star.Angle) * OrbitRadiusRows * radiusScale));
            if (x < 0 || y < 0 || x >= fb.Width || y >= fb.Height)
            {
                continue;
            }

            var glyph = OrbitGlyphs[((_tickCount / OrbitGlyphTicksPerFrame) + star.GlyphOffset) % OrbitGlyphs.Length];
            fb.Set(x, y, new Cell(new Rune(glyph), StarColor, Bg));
        }
    }

    // Four equal-width framed cells tiling one row.
    private void DrawButton(FrameBuffer fb, int index)
    {
        var quarterWidth = fb.Width / 4;
        var x = (quarterWidth * index) + 1;
        var y = 1;
        var width = quarterWidth - 2;
        const int height = 5;
        var focused = index == _focusedIndex;
        var color = focused ? ButtonFocusColor : ButtonNormalColor;

        BoxDrawing.DrawDoubleLine(fb, x, y, width, height, color, Bg);

        var button = _buttons[index];
        var textY = y + (height / 2);
        var textX = x + Math.Max(0, (width - button.Label.Length) / 2);
        var hotIndex = button.Label.IndexOf(button.HotKey, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < button.Label.Length; i++)
        {
            fb.Set(textX + i, textY, new Cell(new Rune(button.Label[i]), i == hotIndex ? ButtonHotColor : color, Bg));
        }
    }

}
