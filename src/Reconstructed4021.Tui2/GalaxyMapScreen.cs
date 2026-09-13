using System.Text;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tui2;

// The permanent shell: unlike the original DOS game, where the galaxy map was just one of several
// swappable panels, here the map is always the base view -- the menu bar (row 0) and status line
// (last row) sit on top of it, never replace it (docs/TUI_SURFACES_MAPPING.md's own "Deliberate
// deviation" section already made this call for Reconstructed4021.Tui; carried forward here).
//
// First slice only: map rendering, cursor movement, and a real menu bar/status line -- every overlay
// panel (Close Up, Fleet, Status, News, Names, Help, Tech Tree), mouse, and End Turn (which needs the
// whole per-empire turn loop, not just this screen) come later. Every menu leaf below is a stub except
// Quit, matching TitleScreen's own Load Game/Options precedent.
internal sealed class GalaxyMapScreen : IScreen
{
    private const int CellWidth = 3;
    private const int GridSpacing = 5;
    private const int CursorJump = 5;

    // DATACNST.PAS's TypeStr/BaseTypeData/GateTypeData/NebulaChar, indexed by the matching enum's
    // ordinal -- same tables GalaxyView already verified against source.
    private const string WorldTypeGlyphs = "aAbBCcijJmNorRsStTUXz";
    private const string StarbaseGlyphs = "■≡πo";
    private const string StargateGlyphs = "↕↑@";
    private const string NebulaGlyphs = " ▒░░";

    private const char PlayerFleetGlyph = '►';
    private const char EnemyFleetGlyph = '▼';
    private const char MineGlyph = '+';
    private const char GridCrossGlyph = '┼';
    private const char GridLineGlyph = '·';
    private const char UnkPlanetGlyph = 'p';
    private const char CursorTopLeft = '┌';
    private const char CursorTopRight = '┐';
    private const char CursorBottomLeft = '└';
    private const char CursorBottomRight = '┘';

    private const ConsoleColor Bg = ConsoleColor.Black;
    private const ConsoleColor PlayerColor = ConsoleColor.White;
    private const ConsoleColor OtherColor = ConsoleColor.Gray;
    private const ConsoleColor UnownedColor = ConsoleColor.DarkGray; // dims Independent-owned worlds so owned ones stand out.
    private const ConsoleColor NebulaColor = ConsoleColor.DarkMagenta;
    private const ConsoleColor UnscoutedColor = ConsoleColor.DarkRed; // COLORS.INC's UnscoutedColor.

    private const ConsoleColor MenuBarFg = ConsoleColor.White;
    private const ConsoleColor MenuBarBg = ConsoleColor.DarkRed; // SYSMenuBar.
    private const ConsoleColor DropdownFg = ConsoleColor.Gray;
    private const ConsoleColor DropdownBg = ConsoleColor.Black; // SYSMenu.
    private const ConsoleColor HelpLineFg = ConsoleColor.DarkRed;
    private const ConsoleColor HelpLineBg = ConsoleColor.Black; // SYSHelpLine.

    private sealed record MenuLeaf(string Label, Action Activate);
    private sealed record TopMenu(string Label, IReadOnlyList<MenuLeaf> Leaves);

    private readonly Game _game;
    private readonly Empire _player;
    private readonly NewGameContext _context;
    private readonly Coordinate _origin;
    private readonly Dictionary<Coordinate, ISectorObject> _objectsByLocation = [];
    private readonly Dictionary<Empire, ConsoleColor> _empireColors;
    private readonly IReadOnlyList<TopMenu> _menus;
    private ILookup<Coordinate, Fleet> _fleetsByLocation = null!;

    private Coordinate _cursor;
    private int _viewportX;
    private int _viewportY;
    private bool _viewportInitialized;

    private bool _menuOpen;
    private int _activeMenuIndex;
    private int _selectedLeafIndex;

    public IScreen? NextScreen { get; private set; }

    public GalaxyMapScreen(Game game, Empire player, NewGameContext context)
    {
        _game = game;
        _player = player;
        _context = context;
        _origin = player.Capital?.Location ?? new Coordinate(game.Galaxy.Size / 2, game.Galaxy.Size / 2);
        _cursor = _origin;
        _empireColors = BuildEmpireColors(game.Empires, player);
        _menus = BuildMenus();

        RebuildIndex();
    }

    // Callers that mutate the galaxy (orders, End Turn -- neither wired up yet) must call this
    // afterward; the index is only ever as fresh as the last call. Kept public now, not speculative:
    // GameShell's own Refresh exists for exactly this and this screen will need the same thing the
    // moment a command actually changes anything.
    public void Refresh()
    {
        _objectsByLocation.Clear();
        RebuildIndex();
    }

    private void RebuildIndex()
    {
        foreach (var planet in _game.Galaxy.Planets)
        {
            _objectsByLocation[planet.Location] = planet;
        }

        foreach (var starbase in _game.Galaxy.Starbases)
        {
            _objectsByLocation[starbase.Location] = starbase;
        }

        foreach (var stargate in _game.Galaxy.Stargates)
        {
            _objectsByLocation[stargate.Location] = stargate;
        }

        foreach (var site in _game.Galaxy.ConstructionSites)
        {
            _objectsByLocation[site.Location] = site;
        }

        _fleetsByLocation = _game.Galaxy.Fleets.ToLookup(f => f.Location);
    }

    // Reconstructed4021.Tui's own DosColors.EmpirePalette (issue #3): one slot per non-player,
    // non-independent empire below Empire's hard cap of 8, avoiding every color already meaningful
    // elsewhere on this map (White=player, Gray=other-empire fallback/grid, DarkGray=independent,
    // DarkMagenta=nebula, DarkRed=unscouted -- Red/DarkRed and DarkMagenta/Magenta are too close to
    // tell apart at a glance). Reconstructed4021.Tui2 has no dependency on that project, so the
    // palette is restated directly in this project's own ConsoleColor terms rather than shared.
    private static readonly ConsoleColor[] EmpirePalette =
    [
        ConsoleColor.DarkBlue,
        ConsoleColor.DarkGreen,
        ConsoleColor.DarkCyan,
        ConsoleColor.DarkYellow,
        ConsoleColor.Blue,
        ConsoleColor.Cyan,
        ConsoleColor.Yellow,
    ];

    private static Dictionary<Empire, ConsoleColor> BuildEmpireColors(IReadOnlyList<Empire> empires, Empire player)
    {
        var colors = new Dictionary<Empire, ConsoleColor>();
        var paletteIndex = 0;
        foreach (var empire in empires)
        {
            if (ReferenceEquals(empire, player) || empire.IsIndependent)
            {
                continue;
            }

            colors[empire] = EmpirePalette[paletteIndex];
            paletteIndex++;
        }

        return colors;
    }

    private IReadOnlyList<TopMenu> BuildMenus() =>
    [
        new TopMenu("Game", [
            new MenuLeaf("Pause", () => { }), // ponytail: purely informational in real Pascal too; nothing to pause yet.
            new MenuLeaf("Next Turn", () => { }), // ponytail: needs the per-empire turn loop -- its own slice.
            new MenuLeaf("Quit", () => NextScreen = _context.MakeTitleScreen()), // no confirm dialog yet -- add one alongside a real dialog widget.
        ]),
        new TopMenu("Empire", [
            new MenuLeaf("Send Message", () => { }),
            new MenuLeaf("Tech Tree", () => { }),
        ]),
        new TopMenu("Worlds", [
            new MenuLeaf("Close Up", () => { }),
            new MenuLeaf("Designate", () => { }),
            new MenuLeaf("Production", () => { }),
            new MenuLeaf("ISSP", () => { }),
        ]),
        new TopMenu("Fleet", [
            new MenuLeaf("Deploy", () => { }),
            new MenuLeaf("Change Destination", () => { }),
            new MenuLeaf("Transfer", () => { }),
            new MenuLeaf("Attack", () => { }),
        ]),
        new TopMenu("Build", [
            new MenuLeaf("Site Status", () => { }),
            new MenuLeaf("New", () => { }),
        ]),
    ];

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_menuOpen)
        {
            HandleMenuKey(key);
            return;
        }

        if (key.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            var typed = char.ToUpperInvariant(key.KeyChar);
            for (var i = 0; i < _menus.Count; i++)
            {
                if (char.ToUpperInvariant(_menus[i].Label[0]) == typed)
                {
                    OpenMenu(i);
                    return;
                }
            }

            return;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            OpenMenu(activeMenuIndex: 0); // matches GameShell's own Esc-opens-Game-menu precedent.
            return;
        }

        HandleMapKey(key);
    }

    private void OpenMenu(int activeMenuIndex)
    {
        _menuOpen = true;
        _activeMenuIndex = activeMenuIndex;
        _selectedLeafIndex = 0;
    }

    private void HandleMenuKey(ConsoleKeyInfo key)
    {
        var leaves = _menus[_activeMenuIndex].Leaves;
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _activeMenuIndex = (_activeMenuIndex + _menus.Count - 1) % _menus.Count;
                _selectedLeafIndex = 0;
                return;
            case ConsoleKey.RightArrow:
                _activeMenuIndex = (_activeMenuIndex + 1) % _menus.Count;
                _selectedLeafIndex = 0;
                return;
            case ConsoleKey.UpArrow:
                _selectedLeafIndex = (_selectedLeafIndex + leaves.Count - 1) % leaves.Count;
                return;
            case ConsoleKey.DownArrow:
                _selectedLeafIndex = (_selectedLeafIndex + 1) % leaves.Count;
                return;
            case ConsoleKey.Enter:
                var activate = leaves[_selectedLeafIndex].Activate;
                _menuOpen = false;
                activate();
                return;
            case ConsoleKey.Escape:
                _menuOpen = false;
                return;
        }
    }

    /// <summary>
    /// Arrows move the cursor one sector, Shift+arrow jumps it CursorJump sectors on that axis,
    /// PageUp/PageDown jump it CursorJump rows, Home/End jump to the X axis's edges, Ctrl+arrow jumps
    /// on a single axis to the next visible sector object/fleet beyond the cursor -- ported directly
    /// from GalaxyView.OnKeyDown/TryMoveCursorToNextObject.
    /// </summary>
    private void HandleMapKey(ConsoleKeyInfo key)
    {
        var isArrow = key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.UpArrow or ConsoleKey.DownArrow;

        if (isArrow && key.Modifiers.HasFlag(ConsoleModifiers.Control) && !key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            TryMoveCursorToNextObject(key.Key);
        }
        else
        {
            var newX = _cursor.X;
            var newY = _cursor.Y;
            var step = isArrow && key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? CursorJump : 1;

            switch (key.Key)
            {
                case ConsoleKey.LeftArrow: newX -= step; break;
                case ConsoleKey.RightArrow: newX += step; break;
                case ConsoleKey.Home: newX = 0; break;
                case ConsoleKey.End: newX = _game.Galaxy.Size - 1; break;
                case ConsoleKey.UpArrow: newY -= step; break;
                case ConsoleKey.DownArrow: newY += step; break;
                case ConsoleKey.PageUp: newY -= CursorJump; break;
                case ConsoleKey.PageDown: newY += CursorJump; break;
                default: return;
            }

            newX = Math.Clamp(newX, 0, _game.Galaxy.Size - 1);
            newY = Math.Clamp(newY, 0, _game.Galaxy.Size - 1);
            _cursor = new Coordinate(newX, newY);
        }
    }

    private void TryMoveCursorToNextObject(ConsoleKey direction)
    {
        var axisIsX = direction is ConsoleKey.LeftArrow or ConsoleKey.RightArrow;
        var positive = direction is ConsoleKey.RightArrow or ConsoleKey.DownArrow;
        var target = FindNextObjectCoordinate(axisIsX, positive) ?? (positive ? _game.Galaxy.Size - 1 : 0);
        _cursor = axisIsX ? new Coordinate(target, _cursor.Y) : new Coordinate(_cursor.X, target);
    }

    private int? FindNextObjectCoordinate(bool axisIsX, bool positive)
    {
        var cursorValue = axisIsX ? _cursor.X : _cursor.Y;
        int? best = null;

        void Consider(Coordinate location)
        {
            var value = axisIsX ? location.X : location.Y;
            if (positive ? value <= cursorValue : value >= cursorValue)
            {
                return;
            }

            if (best is null || (positive ? value < best.Value : value > best.Value))
            {
                best = value;
            }
        }

        foreach (var (location, sectorObject) in _objectsByLocation)
        {
            if (Game.Visible(_player, sectorObject) || (sectorObject is Planet && _game.Galaxy.GetNebula(location) != NebulaType.None))
            {
                Consider(location);
            }
        }

        foreach (var fleet in _game.Galaxy.Fleets)
        {
            if (ReferenceEquals(fleet.Owner, _player) || Game.Visible(_player, fleet))
            {
                Consider(fleet.Location);
            }
        }

        return best;
    }

    public void Update(TimeSpan elapsed)
    {
        // Nothing time-driven on this screen yet -- no ambient animation, unlike the pre-game screens.
    }

    public void Draw(FrameBuffer fb)
    {
        fb.Clear(new Cell(new Rune(' '), ConsoleColor.Gray, Bg));

        const int menuRow = 0;
        var statusRow = fb.Height - 1;
        var mapTop = 1;
        var mapHeight = Math.Max(0, statusRow - mapTop);

        if (!_viewportInitialized && mapHeight > 0)
        {
            CenterOnCursor(fb.Width, mapHeight);
            _viewportInitialized = true;
        }

        DrawMap(fb, mapTop, mapHeight);
        DrawMenuBar(fb, menuRow);
        DrawStatusLine(fb, statusRow);

        if (_menuOpen)
        {
            DrawDropdown(fb, menuRow);
        }
    }

    private void DrawMap(FrameBuffer fb, int mapTop, int mapHeight)
    {
        if (mapHeight <= 0)
        {
            return;
        }

        EnsureCursorVisible(fb.Width, mapHeight);

        var firstCellX = Math.Max(0, _viewportX / CellWidth);
        var lastCellX = Math.Min(_game.Galaxy.Size - 1, (_viewportX + fb.Width - 1) / CellWidth);

        for (var row = 0; row < mapHeight; row++)
        {
            var galaxyY = _viewportY + row;
            if (galaxyY < 0 || galaxyY >= _game.Galaxy.Size)
            {
                continue;
            }

            var screenRow = mapTop + row;
            for (var galaxyX = firstCellX; galaxyX <= lastCellX; galaxyX++)
            {
                var coordinate = new Coordinate(galaxyX, galaxyY);
                var cellLeft = galaxyX * CellWidth - _viewportX;
                var nebula = _game.Galaxy.GetNebula(coordinate);

                var (worldGlyph, worldColor) = WorldGlyphAt(coordinate, nebula);
                SetMapCell(fb, cellLeft + 1, screenRow, worldGlyph, worldColor);
                SetMapCell(fb, cellLeft, screenRow, SideGlyphAt(coordinate, nebula, wantPlayerOwned: true));
                SetMapCell(fb, cellLeft + 2, screenRow, SideGlyphAt(coordinate, nebula, wantPlayerOwned: false));
            }
        }

        DrawCursorOverlay(fb, mapTop, mapHeight);
    }

    private void SetMapCell(FrameBuffer fb, int col, int row, (char Glyph, ConsoleColor Color) cell) => SetMapCell(fb, col, row, cell.Glyph, cell.Color);

    private static void SetMapCell(FrameBuffer fb, int col, int row, char glyph, ConsoleColor color)
    {
        if (col < 0 || col >= fb.Width)
        {
            return;
        }

        fb.Set(col, row, new Cell(new Rune(glyph), color, Bg));
    }

    private (char Glyph, ConsoleColor Color) WorldGlyphAt(Coordinate coordinate, NebulaType nebula)
    {
        if (_objectsByLocation.TryGetValue(coordinate, out var sectorObject) && Game.Visible(_player, sectorObject))
        {
            var glyph = sectorObject switch
            {
                Planet p => WorldTypeGlyphs[(int)p.Type],
                Starbase s => StarbaseGlyphs[(int)s.Kind],
                Stargate g => StargateGlyphs[(int)g.Kind],
                ConstructionSite => '#',
                _ => '?',
            };
            return (glyph, OwnerColor(sectorObject.Owner));
        }

        if (_game.Galaxy.GetMineOwner(coordinate) is { } mineOwner)
        {
            return (MineGlyph, OwnerColor(mineOwner));
        }

        if (sectorObject is Planet && nebula != NebulaType.None)
        {
            return (UnkPlanetGlyph, UnscoutedColor);
        }

        if (nebula != NebulaType.None)
        {
            return (NebulaGlyphs[(int)nebula], NebulaColor);
        }

        var onVerticalLine = coordinate.X % GridSpacing == 0;
        var onHorizontalLine = coordinate.Y % GridSpacing == 0;
        if (onVerticalLine && onHorizontalLine)
        {
            return (GridCrossGlyph, OtherColor);
        }

        return onVerticalLine || onHorizontalLine ? (GridLineGlyph, OtherColor) : (' ', ConsoleColor.Black);
    }

    private (char Glyph, ConsoleColor Color) SideGlyphAt(Coordinate coordinate, NebulaType nebula, bool wantPlayerOwned)
    {
        if (FleetPresent(coordinate, wantPlayerOwned))
        {
            return wantPlayerOwned ? (PlayerFleetGlyph, PlayerColor) : (EnemyFleetGlyph, OtherColor);
        }

        return nebula != NebulaType.None ? (NebulaGlyphs[(int)nebula], NebulaColor) : (' ', ConsoleColor.Black);
    }

    private ConsoleColor OwnerColor(Empire owner)
    {
        if (ReferenceEquals(owner, _player))
        {
            return PlayerColor;
        }

        return owner.IsIndependent ? UnownedColor : _empireColors.GetValueOrDefault(owner, OtherColor);
    }

    private bool FleetPresent(Coordinate coordinate, bool wantPlayerOwned)
    {
        foreach (var fleet in _fleetsByLocation[coordinate])
        {
            if (ReferenceEquals(fleet.Owner, _player) == wantPlayerOwned && Game.Visible(_player, fleet))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawCursorOverlay(FrameBuffer fb, int mapTop, int mapHeight)
    {
        var cellLeft = _cursor.X * CellWidth - _viewportX;
        DrawCursorCorner(fb, cellLeft, _cursor.Y - 1, mapTop, mapHeight, CursorTopLeft);
        DrawCursorCorner(fb, cellLeft + 2, _cursor.Y - 1, mapTop, mapHeight, CursorTopRight);
        DrawCursorCorner(fb, cellLeft, _cursor.Y + 1, mapTop, mapHeight, CursorBottomLeft);
        DrawCursorCorner(fb, cellLeft + 2, _cursor.Y + 1, mapTop, mapHeight, CursorBottomRight);
    }

    private void DrawCursorCorner(FrameBuffer fb, int col, int galaxyY, int mapTop, int mapHeight, char glyph)
    {
        if (galaxyY < 0 || galaxyY >= _game.Galaxy.Size)
        {
            return;
        }

        var row = galaxyY - _viewportY;
        if (row < 0 || row >= mapHeight)
        {
            return;
        }

        SetMapCell(fb, col, mapTop + row, glyph, PlayerColor);
    }

    private void CenterOnCursor(int viewportWidth, int viewportHeight)
    {
        var contentWidth = _game.Galaxy.Size * CellWidth;
        var contentHeight = _game.Galaxy.Size;
        var maxX = Math.Max(0, contentWidth - viewportWidth);
        var maxY = Math.Max(0, contentHeight - viewportHeight);
        _viewportX = Math.Clamp((_cursor.X * CellWidth) - (viewportWidth / 2), 0, maxX);
        _viewportY = Math.Clamp(_cursor.Y - (viewportHeight / 2), 0, maxY);
    }

    private void EnsureCursorVisible(int viewportWidth, int viewportHeight)
    {
        var contentWidth = _game.Galaxy.Size * CellWidth;
        var contentHeight = _game.Galaxy.Size;
        var maxX = Math.Max(0, contentWidth - viewportWidth);
        var maxY = Math.Max(0, contentHeight - viewportHeight);

        var cursorLeft = _cursor.X * CellWidth;
        var cursorRight = cursorLeft + CellWidth - 1;
        if (cursorLeft < _viewportX)
        {
            _viewportX = cursorLeft;
        }
        else if (cursorRight >= _viewportX + viewportWidth)
        {
            _viewportX = cursorRight - viewportWidth + 1;
        }

        if (_cursor.Y < _viewportY)
        {
            _viewportY = _cursor.Y;
        }
        else if (_cursor.Y >= _viewportY + viewportHeight)
        {
            _viewportY = _cursor.Y - viewportHeight + 1;
        }

        _viewportX = Math.Clamp(_viewportX, 0, maxX);
        _viewportY = Math.Clamp(_viewportY, 0, maxY);
    }

    private void DrawMenuBar(FrameBuffer fb, int row)
    {
        fb.DrawText(0, row, new string(' ', fb.Width), MenuBarFg, MenuBarBg);

        var col = 1;
        for (var i = 0; i < _menus.Count; i++)
        {
            var label = $" {_menus[i].Label} ";
            var highlighted = _menuOpen && i == _activeMenuIndex;
            fb.DrawText(col, row, label, highlighted ? MenuBarBg : MenuBarFg, highlighted ? MenuBarFg : MenuBarBg);
            col += label.Length + 1;
        }
    }

    private void DrawStatusLine(FrameBuffer fb, int row)
    {
        // Decorative for now -- F1/F3/F5/F7/F8/F9 aren't wired to anything until their overlay panels
        // exist (Help/Status/Fleet/News/Empire/Names), same "exists but stubbed" precedent as the menu
        // leaves above.
        fb.DrawText(1, row, "F1:Help  F3:Status  F5:Fleet  F7:News  F8:Empire  F9:Names", HelpLineFg, HelpLineBg);

        var coordinateText = RelativeCoordinate.Format(_cursor, _origin);
        fb.DrawText(Math.Max(0, fb.Width - coordinateText.Length - 1), row, coordinateText, HelpLineFg, HelpLineBg);
    }

    private void DrawDropdown(FrameBuffer fb, int menuRow)
    {
        var col = 1;
        for (var i = 0; i < _activeMenuIndex; i++)
        {
            col += _menus[i].Label.Length + 3;
        }

        var leaves = _menus[_activeMenuIndex].Leaves;
        var width = leaves.Max(l => l.Label.Length) + 2;
        var height = leaves.Count + 2;
        var x = Math.Min(col, Math.Max(0, fb.Width - width));
        var y = menuRow + 1;

        DrawDoubleBox(fb, x, y, width, height);
        for (var i = 0; i < leaves.Count; i++)
        {
            var selected = i == _selectedLeafIndex;
            fb.DrawText(x + 1, y + 1 + i, leaves[i].Label.PadRight(width - 2),
                selected ? DropdownBg : DropdownFg, selected ? DropdownFg : DropdownBg);
        }
    }

    private static void DrawDoubleBox(FrameBuffer fb, int x, int y, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                fb.Set(x + col, y + row, new Cell(new Rune(' '), DropdownFg, DropdownBg));
            }
        }

        fb.Set(x, y, new Cell(new Rune('┌'), DropdownFg, DropdownBg));
        fb.Set(x + width - 1, y, new Cell(new Rune('┐'), DropdownFg, DropdownBg));
        fb.Set(x, y + height - 1, new Cell(new Rune('└'), DropdownFg, DropdownBg));
        fb.Set(x + width - 1, y + height - 1, new Cell(new Rune('┘'), DropdownFg, DropdownBg));
        for (var i = 1; i < width - 1; i++)
        {
            fb.Set(x + i, y, new Cell(new Rune('─'), DropdownFg, DropdownBg));
            fb.Set(x + i, y + height - 1, new Cell(new Rune('─'), DropdownFg, DropdownBg));
        }

        for (var i = 1; i < height - 1; i++)
        {
            fb.Set(x, y + i, new Cell(new Rune('│'), DropdownFg, DropdownBg));
            fb.Set(x + width - 1, y + i, new Cell(new Rune('│'), DropdownFg, DropdownBg));
        }
    }
}
