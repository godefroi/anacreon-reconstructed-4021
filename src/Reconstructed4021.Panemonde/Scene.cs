using System.Text;

namespace Reconstructed4021.Panemonde;

// ponytail: one screen cell per sector (the real GalaxyView.cs is 3 screen columns per sector --
// player-fleet | world glyph | enemy-fleet). Visual fidelity to the real map isn't the point here,
// cell-diff and write-batching throughput is, so this collapses that back to one cell and skips the
// per-empire ownership color/fog-of-war logic entirely. If the prototype graduates to a real
// replacement, that domain logic moves over from GalaxyView.cs -- it isn't reinvented here.
internal sealed class Scene
{
    // Sized so a viewport-sized window sees a couple dozen objects at once, roughly matching the
    // density GalaxyView.cs's own comment cites (GAUNTLET.SCN, ~290 objects in a ~1800-cell
    // viewport) -- not a vast mostly-empty field you have to hunt across to find anything.
    private const int GalaxyWidth = 400;
    private const int GalaxyHeight = 150;
    private const int ObjectCount = 290;

    private static readonly Rune[] WorldGlyphs = "aAbBCcijJmNorRsStTUXz".Select(c => new Rune(c)).ToArray();
    private static readonly Rune NebulaGlyph = new('▒');
    private static readonly Rune PlayerFleetGlyph = new('►');
    private static readonly Rune EnemyFleetGlyph = new('▼');

    private readonly (int X, int Y, Rune Glyph, ConsoleColor Color)[] _objects;

    public int CursorX;
    public int CursorY;

    public Scene(int seed)
    {
        var rng = new Random(seed);
        _objects = new (int, int, Rune, ConsoleColor)[ObjectCount];
        for (var i = 0; i < ObjectCount; i++)
        {
            var kind = rng.Next(4);
            var (glyph, color) = kind switch
            {
                0 => (WorldGlyphs[rng.Next(WorldGlyphs.Length)], ConsoleColor.White),
                1 => (NebulaGlyph, ConsoleColor.Magenta),
                2 => (PlayerFleetGlyph, ConsoleColor.Cyan),
                _ => (EnemyFleetGlyph, ConsoleColor.Red),
            };
            _objects[i] = (rng.Next(GalaxyWidth), rng.Next(GalaxyHeight), glyph, color);
        }

        CursorX = GalaxyWidth / 2;
        CursorY = GalaxyHeight / 2;
    }

    public void Move(int dx, int dy)
    {
        CursorX = Math.Clamp(CursorX + dx, 0, GalaxyWidth - 1);
        CursorY = Math.Clamp(CursorY + dy, 0, GalaxyHeight - 1);
    }

    // Paints the viewport centered on the cursor into fb's back buffer. Called every frame -- the
    // FrameBuffer's own diff against _front is what decides how much of this actually gets written.
    public void Draw(FrameBuffer fb)
    {
        var originX = CursorX - (fb.Width / 2);
        var originY = CursorY - (fb.Height / 2);

        fb.Clear(new Cell(new Rune('·'), ConsoleColor.DarkGray, ConsoleColor.Black));

        foreach (var obj in _objects)
        {
            var sx = obj.X - originX;
            var sy = obj.Y - originY;
            if ((uint)sx < (uint)fb.Width && (uint)sy < (uint)fb.Height)
            {
                fb.Set(sx, sy, new Cell(obj.Glyph, obj.Color, ConsoleColor.Black));
            }
        }

        var cx = CursorX - originX;
        var cy = CursorY - originY;
        if ((uint)cx < (uint)fb.Width && (uint)cy < (uint)fb.Height)
        {
            fb.Set(cx, cy, new Cell(new Rune('┼'), ConsoleColor.Yellow, ConsoleColor.Black));
        }
    }
}
