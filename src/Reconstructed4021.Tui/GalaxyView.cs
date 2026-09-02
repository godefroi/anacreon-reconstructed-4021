using System.Drawing;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using CoreGalaxy = Reconstructed4021.Core.Galaxy.Galaxy;
using Coordinate = Reconstructed4021.Core.Galaxy.Coordinate;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Renders the galaxy grid directly onto a custom View, scrolled via Viewport. Layout, glyphs, and
/// colors are taken from the real DOS map (reference/DOSAnacreonSource131/MAPWIND.PAS's CellRecord and
/// DATACNST.PAS's TypeStr/BaseTypeData/GateTypeData), not invented: each sector is 3 screen columns
/// (player-fleet indicator | world glyph | enemy-fleet indicator), and color is two-tier -- White for
/// whatever <see cref="_player"/> owns, LightGray for everyone else's (including Independent, matching
/// DrawPlanets' own <c>IF Emp=Player THEN PlayerColor ELSE EnemyColor</c> -- the original never gave
/// individual empires distinct colors). A cursor (arrows move it, PageUp/PageDown jump it, the viewport
/// auto-follows) highlights one sector the same way DrawMapCursor did -- corner brackets straddling the
/// cursor's own column in the rows above/below, not a box drawn on the cursor's own row.
///
/// Fog-of-war (MAPWIND.PAS's UMSector/UMFleets/EnemyFleetInSector): a world glyph only shows once
/// <see cref="Game.Visible"/> says so, otherwise the sector falls through to nebula/grid/blank exactly
/// as if nothing were there -- except an unscouted planet specifically inside a nebula, which still
/// draws <c>UnkPlanetChar</c> ("something's here") rather than reading as empty nebula; the
/// enemy-fleet indicator needs the same <see cref="Game.Visible"/> check per fleet, the player-fleet
/// indicator needs none (owning it makes it trivially visible). Nebula genuinely blocks scouting too
/// (<see cref="Reconstructed4021.Core.Turns.VisibilityHandler"/>'s own dark-nebula ring-stop and
/// planet-detection exclusion), not just this glyph -- this view doesn't need to know that itself, it
/// only ever draws what <see cref="Game.Visible"/> already decided.
/// </summary>
internal sealed class GalaxyView : View
{
    private const int CellWidth = 3;
    private const int GridSpacing = 5; // MAPWIND.PAS's GridSep
    private const int CursorJump = 5; // MAPWIND.PAS's CursorJump (FastMoveUp/Down's page-jump size)

    // DATACNST.PAS's TypeStr, indexed by WorldType's ordinal (Core.WorldType's doc comment confirms
    // its order was verified against DATACNST.PAS's TypeName table, i.e. matches TypeStr 1:1).
    private const string WorldTypeGlyphs = "aAbBCcijJmNorRsStTUXz";

    // DATACNST.PAS's BaseTypeData ('\xFE\xF0\xE3o' in CP437), indexed by StarbaseKind's ordinal.
    private const string StarbaseGlyphs = "■≡πo";

    // DATACNST.PAS's GateTypeData ('\x12\x18@' in CP437), indexed by StargateKind's ordinal.
    private const string StargateGlyphs = "↕↑@";

    // MAPWIND.PAS's NebulaChar (' \xB1\xB0\xB0' in CP437), indexed by NebulaType's ordinal -- DarkNebula
    // and DenseNebula share a glyph in the original; only color would distinguish them, and the
    // original doesn't bother (DrawNebulaAndMines uses one NebulaColor for all three kinds).
    private const string NebulaGlyphs = " ▒░░";

    private static readonly Rune PlayerFleetRune = new('►'); // MAPWIND.PAS's PlayerFleetChar (CP437 #16)
    private static readonly Rune EnemyFleetRune = new('▼'); // MAPWIND.PAS's EnemyFleetChar (CP437 #31)
    private static readonly Rune MineRune = new('+'); // MAPWIND.PAS's MineChar
    private static readonly Rune GridCrossRune = new('┼'); // MAPWIND.PAS's CrossChar2
    private static readonly Rune GridLineRune = new('·'); // MAPWIND.PAS's Horz/VertChar
    private static readonly Rune UnkPlanetRune = new('p'); // MAPWIND.PAS's UnkPlanetChar (CP437 #112)

    // MAPWIND.PAS's TLCursor/TRCursor/BLCursor/BRCursor (CP437 #218/#191/#192/#217).
    private static readonly Rune CursorTopLeftRune = new('┌');
    private static readonly Rune CursorTopRightRune = new('┐');
    private static readonly Rune CursorBottomLeftRune = new('└');
    private static readonly Rune CursorBottomRightRune = new('┘');

    private static readonly TgAttribute PlayerAttribute = new(StandardColor.White, StandardColor.Black);
    private static readonly TgAttribute OtherAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute NebulaAttribute = new(StandardColor.Magenta, StandardColor.Black);
    private static readonly TgAttribute EmptyAttribute = new(StandardColor.Black, StandardColor.Black);
    private static readonly TgAttribute UnscoutedAttribute = new(DosColors.Red, StandardColor.Black); // COLORS.INC's UnscoutedColor = 4

    private readonly CoreGalaxy _galaxy;
    private readonly Empire _player;

    // A render-local cache, not a duplicated/stale copy of Core state: without it, every redraw
    // called Galaxy.GetObjectAt (up to four linear scans across Planets/Starbases/Stargates/
    // ConstructionSites) once per visible cell -- on GAUNTLET.SCN (~290 objects) at a ~1800-cell
    // viewport, that's on the order of half a million comparisons on every single arrow-key press.
    // Now that a real turn engine can move fleets/change ownership mid-session, callers that mutate
    // Galaxy must call Refresh() afterward -- this index is only ever as fresh as the last Refresh().
    private readonly Dictionary<Coordinate, ISectorObject> _objectsByLocation = [];

    // Not readonly: Refresh() reassigns this (a lookup has no in-place "clear and repopulate").
    private ILookup<Coordinate, Fleet> _fleetsByLocation = null!;

    // PRIMINTR.PAS's RelativeX/RelativeY report cursor position relative to the player's capital (X same
    // sign, Y flipped since screen-down is universe-"south") -- captured once here rather than re-looked-up
    // per move, since it's the same fallback-to-galaxy-center expression _cursor's initial value already uses.
    private readonly Coordinate _origin;

    private Coordinate _cursor;
    private bool _viewportInitialized;

    // Screen-space position of the last drag event, so a left-button-held move can scroll by the pixel
    // delta rather than jumping the viewport to the raw cursor position. Null when no drag is active.
    // MAPWIND.PAS has no mouse handling at all (DOS-era, keyboard-only) -- this is a TUI-only addition,
    // not a port of anything.
    private Point? _dragOrigin;

    // Terminal.Gui's SetAttribute is persistent (like a terminal SGR code) until changed again, so
    // re-setting an unchanged color is a pure no-op that still costs a full framework call. Reset once
    // per frame (not per row): the attribute genuinely persists across Move() calls and row boundaries,
    // so a run of same-colored cells spanning a row wrap still gets to skip the redundant call.
    private TgAttribute? _lastAttribute;

    public GalaxyView(CoreGalaxy galaxy, Empire player)
    {
        _galaxy = galaxy;
        _player = player;
        _origin = player.Capital?.Location ?? new Coordinate(galaxy.Size / 2, galaxy.Size / 2);
        _cursor = _origin;
        SetContentSize(new Size(galaxy.Size * CellWidth, galaxy.Size));
        CanFocus = true;

        RebuildIndex();

        DrawingContent += OnDrawingContent;
        KeyDown += OnKeyDown;
        MouseEvent += OnMouseEvent;
    }

    /// <summary>
    /// The cursor's current sector -- the one real selection mechanism this view has (no Enter/menu
    /// action of its own yet). Named distinctly from <c>View.Cursor</c> (Terminal.Gui's own unrelated
    /// text-cursor-position concept), which this would otherwise silently hide.
    /// </summary>
    public Coordinate CursorLocation => _cursor;

    /// <summary>
    /// Rebuilds the location caches from the galaxy's current state and redraws. Needed because a
    /// turn actually running now (End Turn, Deploy, Attack -- unlike when this view was first built,
    /// with no turn engine driving it at all) can move fleets, add new ones, or change ownership.
    /// </summary>
    public void Refresh()
    {
        _objectsByLocation.Clear();
        RebuildIndex();
        SetNeedsDraw();
    }

    private void RebuildIndex()
    {
        foreach (var planet in _galaxy.Planets) {
            _objectsByLocation[planet.Location] = planet;
        }

        foreach (var starbase in _galaxy.Starbases) {
            _objectsByLocation[starbase.Location] = starbase;
        }

        foreach (var stargate in _galaxy.Stargates) {
            _objectsByLocation[stargate.Location] = stargate;
        }

        foreach (var site in _galaxy.ConstructionSites) {
            _objectsByLocation[site.Location] = site;
        }

        _fleetsByLocation = _galaxy.Fleets.ToLookup(f => f.Location);
    }

    private void OnDrawingContent(object? sender, DrawEventArgs e)
    {
        e.Cancel = true;
        _lastAttribute = null;

        if (!_viewportInitialized) {
            _viewportInitialized = true;
            CenterOnCursor();
        }

        var viewport = Viewport;
        var viewportWidth = viewport.Width;
        var firstCellX = Math.Max(0, viewport.X / CellWidth);
        var lastCellX = Math.Min(_galaxy.Size - 1, (viewport.X + viewport.Width - 1) / CellWidth);

        for (var row = 0; row < viewport.Height; row++) {
            var y = viewport.Y + row;
            if (y < 0 || y >= _galaxy.Size) {
                continue;
            }

            for (var galaxyX = firstCellX; galaxyX <= lastCellX; galaxyX++) {
                var coordinate = new Coordinate(galaxyX, y);
                var cellLeft = galaxyX * CellWidth - viewport.X;
                var nebula = _galaxy.GetNebula(coordinate);

                var (worldRune, worldAttribute) = WorldGlyphAt(coordinate, nebula);
                DrawColumn(cellLeft + 1, row, worldRune, worldAttribute, viewportWidth);

                // Both side columns are always drawn -- MAPWIND.PAS's DrawNebulaAndMines sets
                // PlayerFltChar/EnemyFltChar to the nebula glyph too (not just WorldChar), which is why
                // adjacent nebula sectors read as one solid cloud instead of gapped dots; leaving a
                // fleet-absent column undrawn also left it showing the view's default background
                // instead of black, which is what read as "striped" across the whole map.
                DrawColumn(cellLeft, row, SideGlyphAt(coordinate, nebula, wantPlayerOwned: true), viewportWidth);
                DrawColumn(cellLeft + 2, row, SideGlyphAt(coordinate, nebula, wantPlayerOwned: false), viewportWidth);
            }
        }

        DrawCursorOverlay(viewport, viewportWidth);
    }

    private void DrawColumn(int col, int row, (Rune Rune, TgAttribute Attribute) cell, int viewportWidth) => DrawColumn(col, row, cell.Rune, cell.Attribute, viewportWidth);

    private void DrawColumn(int col, int row, Rune rune, TgAttribute attribute, int viewportWidth)
    {
        if (col < 0 || col >= viewportWidth) {
            return;
        }

        Move(col, row);
        if (_lastAttribute != attribute) {
            SetAttribute(attribute);
            _lastAttribute = attribute;
        }

        AddRune(rune);
    }

    private (Rune Rune, TgAttribute Attribute) WorldGlyphAt(Coordinate coordinate, NebulaType nebula)
    {
        if (_objectsByLocation.TryGetValue(coordinate, out var sectorObject) && Game.Visible(_player, sectorObject)) {
            var glyph = sectorObject switch {
                Planet p => WorldTypeGlyphs[(int)p.Type],
                Starbase s => StarbaseGlyphs[(int)s.Kind],
                Stargate g => StargateGlyphs[(int)g.Kind],
                ConstructionSite => '#',
                _ => '?',
            };
            return (new Rune(glyph), OwnerAttribute(sectorObject.Owner));
        }

        if (_galaxy.GetMineOwner(coordinate) is { } mineOwner) {
            return (MineRune, OwnerAttribute(mineOwner));
        }

        // UMSector's own ELSE IF (Obj.ObjTyp=Pln) AND (Neb<>NoNeb): an unscouted planet still reads
        // as "something's here" specifically inside a nebula -- sectorObject is set here whenever
        // TryGetValue found something above, even though the Visible check just failed for it.
        if (sectorObject is Planet && nebula != NebulaType.None) {
            return (UnkPlanetRune, UnscoutedAttribute);
        }

        if (nebula != NebulaType.None) {
            return (new Rune(NebulaGlyphs[(int)nebula]), NebulaAttribute);
        }

        // Background reference grid every GridSpacing sectors -- MAPWIND.PAS anchors this to the
        // player's capital (CapXY.x MOD GridSep); anchoring to the galaxy origin instead is visually
        // equivalent (a dot grid, just not phase-shifted to the capital) and needs no capital lookup.
        var onVerticalLine = coordinate.X % GridSpacing == 0;
        var onHorizontalLine = coordinate.Y % GridSpacing == 0;
        if (onVerticalLine && onHorizontalLine) {
            return (GridCrossRune, OtherAttribute);
        }

        if (onVerticalLine || onHorizontalLine) {
            return (GridLineRune, OtherAttribute);
        }

        return (new Rune(' '), EmptyAttribute);
    }

    /// <summary>The player-fleet (left) or enemy-fleet (right) column: the matching fleet glyph if one's there, else nebula (widened to fill the whole sector) or blank.</summary>
    private (Rune Rune, TgAttribute Attribute) SideGlyphAt(Coordinate coordinate, NebulaType nebula, bool wantPlayerOwned)
    {
        if (FleetPresent(coordinate, wantPlayerOwned)) {
            return wantPlayerOwned ? (PlayerFleetRune, PlayerAttribute) : (EnemyFleetRune, OtherAttribute);
        }

        if (nebula != NebulaType.None) {
            return (new Rune(NebulaGlyphs[(int)nebula]), NebulaAttribute);
        }

        return (new Rune(' '), EmptyAttribute);
    }

    private TgAttribute OwnerAttribute(Empire owner) => ReferenceEquals(owner, _player) ? PlayerAttribute : OtherAttribute;

    private bool FleetPresent(Coordinate coordinate, bool wantPlayerOwned)
    {
        foreach (var fleet in _fleetsByLocation[coordinate]) {
            // Game.Visible is trivially true for the player's own fleets (Owner == _player); an enemy
            // fleet still needs Known (EnemyFleetInSector, MAPWIND.PAS:982-1005).
            if (ReferenceEquals(fleet.Owner, _player) == wantPlayerOwned && Game.Visible(_player, fleet)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// DrawMapCursor (MAPWIND.PAS:410-440): corner brackets in the cursor cell's own left/right columns,
    /// on the galaxy rows immediately above and below the cursor -- an overlay drawn after the main map
    /// content, not a box around the cursor's own row.
    /// </summary>
    private void DrawCursorOverlay(Rectangle viewport, int viewportWidth)
    {
        var cellLeft = _cursor.X * CellWidth - viewport.X;

        DrawCursorCorner(cellLeft, _cursor.Y - 1, viewport, viewportWidth, CursorTopLeftRune);
        DrawCursorCorner(cellLeft + 2, _cursor.Y - 1, viewport, viewportWidth, CursorTopRightRune);
        DrawCursorCorner(cellLeft, _cursor.Y + 1, viewport, viewportWidth, CursorBottomLeftRune);
        DrawCursorCorner(cellLeft + 2, _cursor.Y + 1, viewport, viewportWidth, CursorBottomRightRune);
    }

    private void DrawCursorCorner(int col, int galaxyY, Rectangle viewport, int viewportWidth, Rune rune)
    {
        if (galaxyY < 0 || galaxyY >= _galaxy.Size) {
            return;
        }

        var row = galaxyY - viewport.Y;
        if (row < 0 || row >= viewport.Height) {
            return;
        }

        DrawColumn(col, row, rune, PlayerAttribute, viewportWidth);
    }

    /// <summary>Arrows move the cursor one sector, PageUp/PageDown jump it CursorJump rows, Home/End jump it to the X axis's edges; the viewport auto-follows to keep the cursor visible.</summary>
    private void OnKeyDown(object? sender, Key key)
    {
        var newX = _cursor.X;
        var newY = _cursor.Y;

        // Strip modifiers defensively before matching -- not a confirmed fix for anything (the actual
        // "arrow keys don't redraw" bug turned out to be the missing SetNeedsDraw() below), just cheap
        // insurance against a terminal reporting stray modifier bits on an otherwise-unmodified arrow key.
        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.CursorLeft: newX--; break;
            case KeyCode.CursorRight: newX++; break;
            case KeyCode.Home: newX = 0; break;
            case KeyCode.End: newX = _galaxy.Size - 1; break;
            case KeyCode.CursorUp: newY--; break;
            case KeyCode.CursorDown: newY++; break;
            case KeyCode.PageUp: newY -= CursorJump; break;
            case KeyCode.PageDown: newY += CursorJump; break;
            default: return;
        }

        newX = Math.Clamp(newX, 0, _galaxy.Size - 1);
        newY = Math.Clamp(newY, 0, _galaxy.Size - 1);
        if (newX == _cursor.X && newY == _cursor.Y) {
            return;
        }

        _cursor = new Coordinate(newX, newY);
        EnsureCursorVisible();

        // EnsureCursorVisible only reassigns Viewport, which is a no-op redraw trigger when the new
        // cursor position doesn't actually require scrolling (i.e. most moves) -- the cursor overlay is
        // drawn from the _cursor field, not from Viewport, so it needs its own explicit invalidation on
        // every move regardless of whether the viewport itself changed.
        SetNeedsDraw();
        key.Handled = true;
        CursorCoordinateChanged?.Invoke(this, CursorCoordinateText);
    }

    /// <summary>
    /// A plain click moves the cursor to that sector and activates it, same as pressing Enter there
    /// -- no Pascal equivalent (MAPWIND.PAS is keyboard-only, DOS-era), a TUI-only convenience like
    /// this view's own drag-panning below. Left-button drag (no full click, held and moved) instead
    /// pans the viewport by the pixel delta since the last event; released or moved-without-the-button
    /// ends the drag.
    /// </summary>
    private void OnMouseEvent(object? sender, Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } clickPosition) {
            // A click landing inside this View's own Frame but outside the galaxy's actual drawn
            // grid (e.g. a small galaxy with room to spare in a bigger terminal) used to silently
            // clamp to the nearest edge cell -- which, if that happened to already be the cursor's
            // own position, activated whatever was already selected instead of doing nothing.
            if (TryGetSectorAt(clickPosition, out var sector)) {
                MoveCursorTo(sector);
                SectorActivated?.Invoke(this, _cursor);
            }

            mouse.Handled = true;
            return;
        }

        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) || mouse.Position is not { } position) {
            if (_dragOrigin is not null) {
                _dragOrigin = null;
                App?.Mouse.UngrabMouse();
            }

            return;
        }

        if (_dragOrigin is { } last) {
            var viewport = Viewport;
            var content = GetContentSize();
            var maxX = Math.Max(0, content.Width - viewport.Width);
            var maxY = Math.Max(0, content.Height - viewport.Height);
            var newX = Math.Clamp(viewport.X - (position.X - last.X), 0, maxX);
            var newY = Math.Clamp(viewport.Y - (position.Y - last.Y), 0, maxY);
            if (newX != viewport.X || newY != viewport.Y) {
                Viewport = new Rectangle(newX, newY, viewport.Width, viewport.Height);
            }
        } else {
            // First event of the drag: grabbing keeps events routed here even once the pointer moves
            // outside the view's own bounds mid-drag.
            App?.Mouse.GrabMouse(this);
        }

        _dragOrigin = position;
        mouse.Handled = true;
    }

    /// <summary>Raised whenever the cursor moves, with the same text <see cref="CursorCoordinateText"/> would return.</summary>
    public event EventHandler<string>? CursorCoordinateChanged;

    /// <summary>
    /// Raised when a sector is clicked (see <see cref="OnMouseEvent"/>) -- click behaves like Enter,
    /// per the user's own explicit request. <see cref="GameShell"/> subscribes to this and its own
    /// Enter-key handler calls the exact same method, so the two input paths can never drift apart.
    /// </summary>
    public event EventHandler<Coordinate>? SectorActivated;

    /// <summary>PRIMINTR.PAS's GetCoordName format: cursor position relative to <see cref="_origin"/>, e.g. "0,0" at the origin.</summary>
    public string CursorCoordinateText => $"{_cursor.X - _origin.X},{_origin.Y - _cursor.Y}";

    /// <summary>Maps a click's screen position (relative to this View's own Viewport) to a galaxy sector -- false if it lands outside the actual grid, not clamped to the nearest edge.</summary>
    private bool TryGetSectorAt(Point screenPosition, out Coordinate sector)
    {
        var viewport = Viewport;
        var galaxyX = (screenPosition.X + viewport.X) / CellWidth;
        var galaxyY = screenPosition.Y + viewport.Y;

        if (galaxyX < 0 || galaxyX >= _galaxy.Size || galaxyY < 0 || galaxyY >= _galaxy.Size) {
            sector = default;
            return false;
        }

        sector = new Coordinate(galaxyX, galaxyY);
        return true;
    }

    private void MoveCursorTo(Coordinate target)
    {
        if (target.X == _cursor.X && target.Y == _cursor.Y) {
            return;
        }

        _cursor = target;
        EnsureCursorVisible();
        SetNeedsDraw();
        CursorCoordinateChanged?.Invoke(this, CursorCoordinateText);
    }

    private void CenterOnCursor()
    {
        var viewport = Viewport;
        var content = GetContentSize();
        var maxX = Math.Max(0, content.Width - viewport.Width);
        var maxY = Math.Max(0, content.Height - viewport.Height);
        var x = _cursor.X * CellWidth - viewport.Width / 2;
        var y = _cursor.Y - viewport.Height / 2;
        Viewport = new Rectangle(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY), viewport.Width, viewport.Height);
    }

    private void EnsureCursorVisible()
    {
        var viewport = Viewport;
        var cursorLeft = _cursor.X * CellWidth;
        var cursorRight = cursorLeft + CellWidth - 1;

        var x = viewport.X;
        if (cursorLeft < x) {
            x = cursorLeft;
        } else if (cursorRight >= x + viewport.Width) {
            x = cursorRight - viewport.Width + 1;
        }

        var y = viewport.Y;
        if (_cursor.Y < y) {
            y = _cursor.Y;
        } else if (_cursor.Y >= y + viewport.Height) {
            y = _cursor.Y - viewport.Height + 1;
        }

        var content = GetContentSize();
        var maxX = Math.Max(0, content.Width - viewport.Width);
        var maxY = Math.Max(0, content.Height - viewport.Height);
        Viewport = new Rectangle(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY), viewport.Width, viewport.Height);
    }
}
