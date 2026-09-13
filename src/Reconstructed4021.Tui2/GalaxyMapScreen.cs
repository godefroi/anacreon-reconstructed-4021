using System.Text;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

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
    private const ConsoleColor MenuHotColor = ConsoleColor.Yellow; // no Pascal equivalent -- see MenuBar's own doc comment.
    private const ConsoleColor MenuSelectedFg = ConsoleColor.Black;
    private const ConsoleColor MenuSelectedBg = ConsoleColor.Gray; // SYSDispSelect.
    private const ConsoleColor DropdownFg = ConsoleColor.Gray;
    private const ConsoleColor DropdownBg = ConsoleColor.Black; // SYSMenu.
    private const ConsoleColor HelpLineFg = ConsoleColor.DarkRed;
    private const ConsoleColor HelpLineBg = ConsoleColor.Black; // SYSHelpLine.

    private readonly Game _game;
    private readonly Empire _player;
    private readonly NewGameContext _context;
    private readonly Coordinate _origin;
    private readonly Dictionary<Coordinate, ISectorObject> _objectsByLocation = [];
    private readonly Dictionary<Empire, ConsoleColor> _empireColors;
    private readonly MenuBar _menuBar;
    private ILookup<Coordinate, Fleet> _fleetsByLocation = null!;

    private Coordinate _cursor;
    private int _viewportX;
    private int _viewportY;
    private bool _viewportInitialized;

    // At most one of these is active at a time: an info popup (dismissed on any key) or the save-name
    // prompt (Enter confirms, Esc cancels) -- both take over HandleKey/Draw ahead of the menu bar/map
    // while set.
    private string? _infoTitle;
    private string? _infoMessage;
    private TextInputField? _savePrompt;

    public IScreen? NextScreen { get; private set; }

    public GalaxyMapScreen(Game game, Empire player, NewGameContext context)
    {
        _game = game;
        _player = player;
        _context = context;
        _origin = player.Capital?.Location ?? new Coordinate(game.Galaxy.Size / 2, game.Galaxy.Size / 2);
        _cursor = _origin;
        _empireColors = BuildEmpireColors(game.Empires, player);
        _menuBar = new MenuBar(BuildMenus());

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

    // Verbatim from Reconstructed4021.Tui's own GameShell.BuildMenus (hotkeys included) -- every leaf
    // is a stub except Quit, matching TitleScreen's own Load Game/Options precedent. End Turn needs
    // the whole per-empire turn loop (its own slice); every other stub needs an overlay panel that
    // doesn't exist yet.
    private IReadOnlyList<MenuBar.TopItem> BuildMenus() =>
    [
        new MenuBar.TopItem("⌂", [
            new MenuBar.Item("_About Anacreon", Stub),
        ]),
        new MenuBar.TopItem("_Game", [
            new MenuBar.Item("_Pause", () => ShowInfo("Paused", "Time has stopped. Press any key to continue.")),
            new MenuBar.Item("_Status Hardcopy", Stub),
            new MenuBar.Item("Sa_ve", () => _savePrompt = new TextInputField($"{_player.Name}-{_game.Year}", maxLength: 60)),
            new MenuBar.Item("_Next Turn", Stub), // needs the whole per-empire turn loop -- its own slice.
            new MenuBar.Item("_Quit", () => NextScreen = _context.MakeTitleScreen()), // no confirm dialog yet -- add one alongside a real dialog widget.
            new MenuBar.Item("E_xit to OS", () => NextScreen = QuitScreen.Instance), // same no-confirm simplification as Quit above.
        ]),
        new MenuBar.TopItem("_Empire", [
            new MenuBar.Item("_Send Message", Stub),
            new MenuBar.Item("_Read Messages", Stub),
            new MenuBar.Item("_Trade Technology", Stub),
            new MenuBar.Item("Te_ch Tree", Stub),
        ]),
        new MenuBar.TopItem("_Worlds", [
            new MenuBar.Item("_Close Up", Stub),
            new MenuBar.Item("_Designate", Stub),
            new MenuBar.Item("P_roduction", Stub),
            new MenuBar.Item("_ISSP", Stub),
            new MenuBar.Item("_Add Name", Stub),
            new MenuBar.Item("Delete _Name", Stub),
            new MenuBar.Item("_Liberate", Stub),
            new MenuBar.Item("_Self-Destruct", Stub),
        ]),
        new MenuBar.TopItem("_Fleet", [
            new MenuBar.Item("_Deploy", Stub),
            new MenuBar.Item("_Change Destination", Stub),
            new MenuBar.Item("_Transfer", Stub),
            new MenuBar.Item("_Abort/Join", Stub),
            new MenuBar.Item("_Refuel", Stub),
            new MenuBar.Item("_SRM Sweep", Stub),
            new MenuBar.Item("_Orders", Stub),
            new MenuBar.Item("Canc_el Orders", Stub),
            new MenuBar.Item("Res_upply", Stub),
            new MenuBar.Item("_Probe", Stub),
        ]),
        new MenuBar.TopItem("_Build", [
            new MenuBar.Item("_Site Status", Stub),
            new MenuBar.Item("_New", Stub),
            new MenuBar.Item("_Abort", Stub),
        ]),
        new MenuBar.TopItem("_Ministry of War", [
            new MenuBar.Item("_Attack", Stub),
            new MenuBar.Item("Auto A_ttack", Stub),
            new MenuBar.Item("Launch _LAMs", Stub),
            new MenuBar.Item("_Defenses", Stub),
        ]),
    ];

    private static void Stub()
    {
        // No overlay-panel/dialog system yet -- every leaf above but Quit is inert until its own
        // surface exists.
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_infoMessage is not null)
        {
            _infoTitle = null;
            _infoMessage = null;
            return;
        }

        if (_savePrompt is not null)
        {
            HandleSavePromptKey(key);
            return;
        }

        if (_menuBar.HandleKey(key))
        {
            return;
        }

        // Not consumed by the menu bar (it only opens on Alt+<letter> while closed) -- Esc still opens
        // the Game menu specifically, matching GameShell's own precedent (index 1: ⌂ is index 0).
        if (key.Key == ConsoleKey.Escape)
        {
            _menuBar.Open(1);
            return;
        }

        HandleMapKey(key);
    }

    private void HandleSavePromptKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                var name = _savePrompt!.Text.Trim();
                _savePrompt = null;
                if (name.Length > 0)
                {
                    SaveGameAs(name);
                }

                return;
            case ConsoleKey.Escape:
                _savePrompt = null;
                return;
        }

        _savePrompt!.HandleKey(key);
    }

    /// <summary>
    /// SaveGame (LOADSAVE.PAS:645-714): writes to a temp file first and only swaps it in on success,
    /// so a failed write can never corrupt an existing save -- kept even though this port's own format
    /// is JSON, not the DOS binary layout, since it's a real correctness property, not a DOS-era
    /// artifact. Ported from GameShell.WriteSaveFile/SaveGameAs/AvoidCollision.
    /// </summary>
    private void SaveGameAs(string name)
    {
        var fileName = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.json";
        var saveDir = Path.Combine(_context.RepoRoot, "saves");
        Directory.CreateDirectory(saveDir);
        fileName = AvoidCollision(saveDir, fileName);

        var path = Path.Combine(saveDir, fileName);
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, GameJson.Serialize(_game, _context.NpeProvider));
            File.Move(tempPath, path, overwrite: true);
            ShowInfo("Save Game", $"Game saved to {fileName}.");
        }
        catch (IOException ex)
        {
            ShowInfo("Save Game", $"Could not save: {ex.Message}");
        }
    }

    // GameShell.AvoidCollision: the suggested save name ("{player}-{year}") makes picking the same
    // name twice easy to do by accident, so a second save under that name would otherwise silently
    // destroy the first with no confirmation. Appends " (1)", " (2)", etc. instead.
    private static string AvoidCollision(string directory, string fileName)
    {
        if (!File.Exists(Path.Combine(directory, fileName)))
        {
            return fileName;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; ; i++)
        {
            var candidate = $"{baseName} ({i}){extension}";
            if (!File.Exists(Path.Combine(directory, candidate)))
            {
                return candidate;
            }
        }
    }

    private void ShowInfo(string title, string message)
    {
        _infoTitle = title;
        _infoMessage = message;
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
        _menuBar.Draw(fb, menuRow, MenuBarFg, MenuBarBg, MenuHotColor, DropdownFg, DropdownBg, MenuSelectedFg, MenuSelectedBg);
        DrawStatusLine(fb, statusRow);

        if (_savePrompt is not null)
        {
            DrawSavePrompt(fb);
        }

        if (_infoMessage is not null)
        {
            DrawInfoPopup(fb);
        }
    }

    private void DrawSavePrompt(FrameBuffer fb)
    {
        const int width = 50;
        const int height = 4;
        var x = (fb.Width - width) / 2;
        var y = (fb.Height - height) / 2;

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + 1, y + 1, "Filename to save to:", ConsoleColor.White, ConsoleColor.Black);
        _savePrompt!.Draw(fb, x + 1, y + 2, width - 2, TextInputField.DefaultFg, TextInputField.DefaultBg);
    }

    private void DrawInfoPopup(FrameBuffer fb)
    {
        var lines = _infoMessage!.Split('\n');
        var width = Math.Max(lines.Max(l => l.Length), _infoTitle!.Length + 2) + 4;
        var height = lines.Length + 5;
        var x = (fb.Width - width) / 2;
        var y = (fb.Height - height) / 2;

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = $" {_infoTitle} ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);
        for (var i = 0; i < lines.Length; i++)
        {
            fb.DrawText(x + 2, y + 2 + i, lines[i], ConsoleColor.White, ConsoleColor.Black);
        }

        fb.DrawText(x + 2, y + height - 2, "Press any key to continue...", ConsoleColor.Gray, ConsoleColor.Black);
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

    private void DrawStatusLine(FrameBuffer fb, int row)
    {
        // Decorative for now -- F1/F3/F5/F7/F8/F9 aren't wired to anything until their overlay panels
        // exist (Help/Status/Fleet/News/Empire/Names), same "exists but stubbed" precedent as the menu
        // leaves above.
        fb.DrawText(1, row, "F1:Help  F3:Status  F5:Fleet  F7:News  F8:Empire  F9:Names", HelpLineFg, HelpLineBg);

        var coordinateText = RelativeCoordinate.Format(_cursor, _origin);
        fb.DrawText(Math.Max(0, fb.Width - coordinateText.Length - 1), row, coordinateText, HelpLineFg, HelpLineBg);
    }

}
