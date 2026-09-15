using Reconstructed4021.Panemonde;


namespace Reconstructed4021.Tui2.Screens;


// TMA.PAS's TMALogo: the one-time "presents" splash shown before the main menu. Reproduces the real
// ASCII-art logo (decoded from TMA.PAS's CP437 source bytes) and its left-to-right reveal animation;
// skips the original's subsequent 13-column slide-right flourish (drawn once already at its final
// resting, horizontally-centered position instead) -- low-value to reproduce exactly, and the
// original's fixed column-13 placement assumed an 80-column screen this doesn't have.
//
// Dismissed by any keypress (which also skips straight past an in-progress reveal), or automatically
// after the reveal completes plus a 5-second hold (TMA.PAS's Wait(5, Ch)).
public sealed class TmaLogoScreen : IScreen
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

    private const double TickMs = 30;
    private const double HoldMs = 5000;
    private static readonly ConsoleColor LogoColor = ConsoleColor.DarkRed; // calibrated CGA red, see TitleScreen's own remarks.
    private static readonly ConsoleColor PresentsColor = ConsoleColor.Cyan;

    private readonly IScreen _next;
    private double _accumulatorMs;
    private int _revealedColumns;
    private bool _revealComplete;
    private double _holdElapsedMs;

    public IScreen? NextScreen { get; private set; }

    public TmaLogoScreen(IScreen next)
    {
        _next = next;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        NextScreen = _next;
    }

    public void Update(TimeSpan elapsed)
    {
        _accumulatorMs += elapsed.TotalMilliseconds;
        while (_accumulatorMs >= TickMs)
        {
            _accumulatorMs -= TickMs;
            Tick();
        }
    }

    // Each tick reveals a growing *suffix* of the final line, always redrawn from the same starting
    // column (see Draw) -- that's what makes it read as characters marching in from the left, rather
    // than a static wipe of the whole image (which a growing prefix would give instead).
    private void Tick()
    {
        if (!_revealComplete)
        {
            _revealedColumns += 2;
            if (_revealedColumns >= Logo[0].Length)
            {
                _revealedColumns = Logo[0].Length;
                _revealComplete = true;
            }

            return;
        }

        _holdElapsedMs += TickMs;
        if (_holdElapsedMs >= HoldMs)
        {
            NextScreen = _next;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        fb.Clear(new Cell(new System.Text.Rune(' '), ConsoleColor.Gray, ConsoleColor.Black));

        var topY = Math.Max(0, (fb.Height / 2) - 4);
        var x = Math.Max(0, (fb.Width - Logo[0].Length) / 2);

        for (var row = 0; row < Logo.Length; row++)
        {
            var line = Logo[row];
            var suffix = line[Math.Max(0, line.Length - _revealedColumns)..];
            fb.DrawText(x, topY + row, suffix, LogoColor, ConsoleColor.Black);
        }

        if (_revealComplete)
        {
            const string presents = "Presents";
            fb.DrawText((fb.Width - presents.Length) / 2, topY + Logo.Length + 4, presents, PresentsColor, ConsoleColor.Black);
        }
    }
}
