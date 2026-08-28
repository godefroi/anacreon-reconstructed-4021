using System.Diagnostics;
using System.Drawing;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;
using CoreGalaxy = ThreeLn.Reconstruction4021.Core.Galaxy.Galaxy;
using Coordinate = ThreeLn.Reconstruction4021.Core.Galaxy.Coordinate;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace ThreeLn.Reconstruction4021.Tui;

/// <summary>
/// Phase 8 prototype (docs/ROADMAP.md item 8, docs/TUI_LIBRARY_RECOMMENDATION.md's "map viewport" gap):
/// renders the galaxy grid directly onto a custom View, scrolled via Viewport. Layout, glyphs, and
/// colors are taken from the real DOS map (reference/DOSAnacreonSource131/MAPWIND.PAS's CellRecord and
/// DATACNST.PAS's TypeStr/BaseTypeData/GateTypeData), not invented: each sector is 3 screen columns
/// (player-fleet indicator | world glyph | enemy-fleet indicator), and color is two-tier -- White for
/// whatever <see cref="_player"/> owns, LightGray for everyone else's (including Independent, matching
/// DrawPlanets' own <c>IF Emp=Player THEN PlayerColor ELSE EnemyColor</c> -- the original never gave
/// individual empires distinct colors). A cursor (arrows move it, PageUp/PageDown jump it, the viewport
/// auto-follows) highlights one sector the same way DrawMapCursor did -- corner brackets straddling the
/// cursor's own column in the rows above/below, not a box drawn on the cursor's own row. Still no
/// selection action (Enter/menu) and no fog-of-war -- just the map, scrolling, and the cursor indicator.
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

    // MAPWIND.PAS's TLCursor/TRCursor/BLCursor/BRCursor (CP437 #218/#191/#192/#217).
    private static readonly Rune CursorTopLeftRune = new('┌');
    private static readonly Rune CursorTopRightRune = new('┐');
    private static readonly Rune CursorBottomLeftRune = new('└');
    private static readonly Rune CursorBottomRightRune = new('┘');

    private static readonly TgAttribute PlayerAttribute = new(StandardColor.White, StandardColor.Black);
    private static readonly TgAttribute OtherAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute NebulaAttribute = new(StandardColor.Magenta, StandardColor.Black);
    private static readonly TgAttribute EmptyAttribute = new(StandardColor.Black, StandardColor.Black);

    private readonly CoreGalaxy _galaxy;
    private readonly Empire _player;

    // The galaxy is static for the lifetime of this prototype (no turn engine driving it), so a
    // one-time coordinate index is safe and correct -- not a duplicated/stale copy of Core state, just
    // a render-local cache. Without it, every redraw called Galaxy.GetObjectAt (up to four linear scans
    // across Planets/Starbases/Stargates/ConstructionSites) once per visible cell: on GAUNTLET.SCN
    // (~290 objects) at a ~1800-cell viewport, that's on the order of half a million comparisons on
    // every single arrow-key press. Both indexes are built once here, so any fleet the caller wants
    // rendered must already be in Galaxy.Fleets before this constructor runs.
    private readonly Dictionary<Coordinate, ISectorObject> _objectsByLocation = [];
    private readonly ILookup<Coordinate, Fleet> _fleetsByLocation;

    private Coordinate _cursor;
    private bool _viewportInitialized;

    // Terminal.Gui's SetAttribute is persistent (like a terminal SGR code) until changed again, so
    // re-setting an unchanged color is a pure no-op that still costs a full framework call. Reset once
    // per frame (not per row): the attribute genuinely persists across Move() calls and row boundaries,
    // so a run of same-colored cells spanning a row wrap still gets to skip the redundant call.
    private TgAttribute? _lastAttribute;

    /// <summary>Diagnostic only: how long the last OnDrawingContent call took, for the caller to surface (e.g. in a window title) while chasing render performance.</summary>
    public event EventHandler<TimeSpan>? FrameRendered;

    public GalaxyView(CoreGalaxy galaxy, Empire player)
    {
        _galaxy = galaxy;
        _player = player;
        _cursor = player.Capital?.Location ?? new Coordinate(galaxy.Size / 2, galaxy.Size / 2);
        SetContentSize(new Size(galaxy.Size * CellWidth, galaxy.Size));
        CanFocus = true;

        foreach (var planet in galaxy.Planets) {
            _objectsByLocation[planet.Location] = planet;
        }

        foreach (var starbase in galaxy.Starbases) {
            _objectsByLocation[starbase.Location] = starbase;
        }

        foreach (var stargate in galaxy.Stargates) {
            _objectsByLocation[stargate.Location] = stargate;
        }

        foreach (var site in galaxy.ConstructionSites) {
            _objectsByLocation[site.Location] = site;
        }

        _fleetsByLocation = galaxy.Fleets.ToLookup(f => f.Location);

        DrawingContent += OnDrawingContent;
        KeyDown += OnKeyDown;
    }

    private void OnDrawingContent(object? sender, DrawEventArgs e)
    {
        e.Cancel = true;
        var stopwatch = Stopwatch.StartNew();
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

        FrameRendered?.Invoke(this, stopwatch.Elapsed);
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
        if (_objectsByLocation.TryGetValue(coordinate, out var sectorObject)) {
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
            if (ReferenceEquals(fleet.Owner, _player) == wantPlayerOwned) {
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

    /// <summary>Diagnostic only: every key GalaxyView's KeyDown sees, for the caller to surface while chasing the arrow-key report.</summary>
    public event EventHandler<Key>? KeyReceived;

    /// <summary>Arrows move the cursor one sector, PageUp/PageDown jump it CursorJump rows, Home/End jump it to the X axis's edges; the viewport auto-follows to keep the cursor visible.</summary>
    private void OnKeyDown(object? sender, Key key)
    {
        KeyReceived?.Invoke(this, key);

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
