using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace ThreeLn.Reconstruction4021.Tui;

/// <summary>
/// Shared chrome for every New Game screen (ScenarioPickerWindow, IntroTextWindow, PlayerCountWindow,
/// PlayerSetupWindow). NEWGAME.PAS:1702's <c>OpenWindow(1,1,80,24,ThinBRD,Title,C.SYSDispWind,...)</c> is
/// a fixed 80x24 box, not a fill of whatever size DOS happened to be running at (DOS was always 80x25,
/// so there was never empty space around it to fill in the first place) -- centered here inside however
/// large the real terminal is, over a black backdrop with a static, gently twinkling starfield (no Pascal
/// equivalent; our own addition to fill the space a real DOS screen never had). Subclasses add their own
/// controls to <see cref="Content"/>, never to `this` directly -- the outer Window only ever hosts
/// Content plus the starfield draw, and stays the actual Application.Run top-level so KeyDown/App wiring
/// in subclasses (attached to `this`, same as before this existed) keeps working unchanged.
/// </summary>
internal abstract class NewGameWindow : Window
{
    // COLORS.INC's ColorScrColor: SYSDispWind=23 -> LightGray on Blue.
    private static readonly TgAttribute SysDispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BackdropAttribute = new(StandardColor.Black, StandardColor.Black);
    private static readonly TgAttribute DimStarAttribute = new(StandardColor.DarkGray, StandardColor.Black);
    private static readonly TgAttribute BrightStarAttribute = new(StandardColor.White, StandardColor.Black);

    private static readonly char[] StarGlyphs = ['.', '.', '.', '*'];
    private const int StarCount = 90;
    private static readonly TimeSpan TwinkleTick = TimeSpan.FromMilliseconds(220);

    private readonly record struct Star(int X, int Y, int Phase);

    private Star[] _stars = [];
    private int _tick;
    private int _starsWidth = -1;
    private int _starsHeight = -1;

    // Every timer this window has pending, so Dismiss can cancel them -- an uncancelled twinkle tick
    // would otherwise keep firing against whatever screen runs next, the same class of bug
    // AnacreonTitleWindow's own Dismiss exists to prevent.
    private readonly List<object> _pendingTimeouts = [];

    /// <summary>The centered 80x24 box real content lives in. CanFocus=true is load-bearing: a
    /// container not in the focus chain is what silently blocked focus from ever reaching a child inside
    /// it once (PlayerSetupWindow's genderBox, confirmed from real testing) -- same fix applied here
    /// up front instead of waiting to rediscover it.</summary>
    protected View Content { get; }

    protected NewGameWindow(string title)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        BorderStyle = LineStyle.None;
        Border.Thickness = new Thickness(0);
        SetScheme(new Scheme(BackdropAttribute));
        DrawingContent += OnDrawingStars;

        Content = new View {
            Title = title,
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 80,
            Height = 24,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        Content.SetScheme(new Scheme(SysDispWindAttribute));
        base.Add(Content);

        Initialized += (_, _) => ScheduleTimeout(TwinkleTick, Twinkle);
    }

    protected object ScheduleTimeout(TimeSpan delay, Func<bool> callback)
    {
        var token = App!.AddTimeout(delay, callback)!;
        _pendingTimeouts.Add(token);
        return token;
    }

    /// <summary>Every exit path in every subclass calls this instead of App?.RequestStop() directly, so
    /// the twinkle timer always gets cancelled before the next screen runs.</summary>
    protected void Dismiss()
    {
        foreach (var token in _pendingTimeouts) {
            App?.RemoveTimeout(token);
        }

        _pendingTimeouts.Clear();
        App?.RequestStop();
    }

    // Positions are re-rolled only when the frame size actually changes (fresh terminal size on the
    // first real layout pass, or a live resize) -- otherwise fixed between ticks, so this is a "gently
    // twinkling" field, not a moving one, matching what was actually asked for. Seeding lazily here
    // (rather than once from Initialized) matters because Frame.Width/Height aren't guaranteed populated
    // the first time Initialized fires -- AnacreonTitleWindow's own OnDrawingContent hit this same gap
    // and recomputes its geometry every frame for the same reason; seeding once against a possibly-still-
    // 0x0 Frame would have clustered every star at (0,0), invisible.
    private void EnsureStars()
    {
        if (Frame.Width == _starsWidth && Frame.Height == _starsHeight) {
            return;
        }

        _starsWidth = Frame.Width;
        _starsHeight = Frame.Height;

        var rng = new Random();
        var stars = new Star[StarCount];
        var width = Math.Max(1, Frame.Width);
        var height = Math.Max(1, Frame.Height);

        for (var i = 0; i < StarCount; i++) {
            stars[i] = new Star(rng.Next(width), rng.Next(height), rng.Next(1000));
        }

        _stars = stars;
    }

    private bool Twinkle()
    {
        _tick++;
        SetNeedsDraw();
        return true;
    }

    private void OnDrawingStars(object? sender, DrawEventArgs e)
    {
        e.Cancel = true;
        EnsureStars();

        // Content (an opaque child View) draws over whatever stars fall inside its own rect during the
        // normal subview draw pass right after this, so there's no need to exclude its area here.
        foreach (var star in _stars) {
            if (star.X >= Frame.Width || star.Y >= Frame.Height) {
                continue;
            }

            var bright = (_tick + star.Phase) % 23 == 0;
            SetAttribute(bright ? BrightStarAttribute : DimStarAttribute);
            Move(star.X, star.Y);
            AddRune(new Rune(StarGlyphs[star.Phase % StarGlyphs.Length]));
        }
    }
}
