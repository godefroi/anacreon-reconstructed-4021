using System.Text;
using Reconstructed4021.Panemonde;


namespace Reconstructed4021.Tui2.Shared;


// NewGameWindow's own twinkling backdrop (no Pascal equivalent -- DOS never had empty space around
// its fixed 80x24 box to fill), ported as a small piece of state each screen owns and drives itself
// rather than a shared base class (IScreen has no inheritance chain).
internal sealed class Starfield
{
    private const int StarCount = 90;
    private static readonly char[] Glyphs = ['.', '.', '.', '*'];
    private const double TickMs = 220;

    private readonly record struct Star(int X, int Y, int Phase);

    private readonly Random _random = new();
    private Star[] _stars = [];
    private int _width = -1;
    private int _height = -1;
    private int _tick;
    private double _accumulatorMs;

    public void Update(TimeSpan elapsed)
    {
        _accumulatorMs += elapsed.TotalMilliseconds;
        while (_accumulatorMs >= TickMs)
        {
            _accumulatorMs -= TickMs;
            _tick++;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        EnsureStars(fb.Width, fb.Height);

        foreach (var star in _stars)
        {
            var bright = (_tick + star.Phase) % 23 == 0;
            fb.Set(star.X, star.Y, new Cell(new Rune(Glyphs[star.Phase % Glyphs.Length]),
                bright ? ConsoleColor.White : ConsoleColor.DarkGray, ConsoleColor.Black));
        }
    }

    // Re-seeded only when the frame size actually changes, so this reads as gently twinkling in
    // place rather than a moving field.
    private void EnsureStars(int width, int height)
    {
        if (width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;

        var stars = new Star[StarCount];
        for (var i = 0; i < StarCount; i++)
        {
            stars[i] = new Star(_random.Next(Math.Max(1, width)), _random.Next(Math.Max(1, height)), _random.Next(1000));
        }

        _stars = stars;
    }
}
