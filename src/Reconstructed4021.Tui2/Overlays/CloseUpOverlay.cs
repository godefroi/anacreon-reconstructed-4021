using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;
using Reconstructed4021.Tui2.Screens;


namespace Reconstructed4021.Tui2.Overlays;


// CLSCOMM.PAS's CloseUpCom, ported from Reconstructed4021.Tui's CloseUpWindow/CloseUpContentView --
// see those two files' own doc comments for the field-layout (row/column) and per-field redaction
// citations transcribed verbatim below. One real deviation, per the user's own explicit direction
// this time around: real Pascal's shared display window title is dynamic per-turn text ("Anacreon:
// {Year} ({Age}th year of your reign.)", DISPLAY.PAS:156-162) -- never the object's own name. Tui1
// already dropped that text from its own CloseUpWindow.Title to make room for name+tabs (that class's
// own doc comment). This port goes one step further: name/coordinates sit in the frame's own title
// position and the tab strip gets a separate region on the right of the same border row (TabFrame),
// rather than concatenating both into one string.
//
// A second tab, Orders (FLTCOMM.PAS's own FleetOrdersCommand mini scripting language), now exists for
// one of the player's own fleets -- see FleetOrdersTabView's own doc comment (Reconstructed4021.Tui)
// for the compile/commit flow this ports. F2 (rename), F10 (go to map), and the fleet-action shortcuts
// (C/T/J/A/R, resolved externally via _resolveFleetAction) are all wired on the Close Up tab; none of
// them apply while editing Orders.
internal sealed class CloseUpOverlay : IOverlay
{
    private const int FrameWidth = 80;
    private const int FrameHeight = 21;

    private const ConsoleColor BorderFg = ConsoleColor.Gray; // SYSWBorder = 7 (LightGray on Black).
    private const ConsoleColor BorderBg = ConsoleColor.Black;
    private const ConsoleColor ContentFg = ConsoleColor.Gray; // SYSDispWind = 23 (LightGray on Blue).
    private const ConsoleColor ContentBg = ConsoleColor.DarkBlue; // DOS background attributes have no bright variant -- see FrameBuffer.AnsiCode's own note on this.
    private const ConsoleColor GutterFg = ConsoleColor.Yellow; // no Pascal equivalent -- see TextEditor's own doc comment on why a gutter mark replaces tui1's full-line highlight.
    private const ConsoleColor CursorFg = ConsoleColor.Black;
    private const ConsoleColor CursorBg = ConsoleColor.Gray; // SYSDispSelect's own pair, reused here for the editing caret.

    // DisplayFleetInfo's FltTypeName (CLSCOMM.PAS:670-675), ordinal-aligned with FleetType.
    private static readonly string[] FleetTypeNames =
        ["Warpfleet", "Jumpfleet", "Hunter-Killer Fleet", "Stealth Fleet", "Fast-Warp Fleet"];

    private enum TabKind { CloseUp, Orders }

    private readonly ISectorObject _obj;
    private readonly Empire _viewer;
    private readonly Game _game;
    private readonly Action<string, string> _showInfo;
    private readonly Action<IOverlay> _push;
    private readonly Func<char, Fleet, Action<Fleet>?> _resolveFleetAction;
    private readonly Action<ISectorObject> _onGoToMap;
    private readonly TabKind[] _tabKinds;
    private readonly TabFrame _frame;
    private readonly TextEditor? _ordersEditor;

    public bool IsDismissed { get; private set; }

    public CloseUpOverlay(ISectorObject obj, Empire viewer, Game game, Action<string, string> showInfo, Action<IOverlay> push, Func<char, Fleet, Action<Fleet>?> resolveFleetAction, Action<ISectorObject> onGoToMap, string initialTab = "Close Up")
    {
        _obj = obj;
        _viewer = viewer;
        _game = game;
        _showInfo = showInfo;
        _push = push;
        _resolveFleetAction = resolveFleetAction;
        _onGoToMap = onGoToMap;

        if (obj is Fleet ownFleet && ReferenceEquals(ownFleet.Owner, viewer))
        {
            _tabKinds = [TabKind.CloseUp, TabKind.Orders];
            var lines = FleetOrderCompiler.Decompile(game, viewer, ownFleet.Orders);
            _ordersEditor = new TextEditor(string.Join('\n', lines))
            {
                MarkedLine = ownFleet.NextOrder > 0 ? ownFleet.NextOrder : lines.Count > 0 ? 1 : 0,
            };
        }
        else
        {
            _tabKinds = [TabKind.CloseUp];
        }

        _frame = new TabFrame(_tabKinds.Select(k => k == TabKind.CloseUp ? "Close Up" : "Orders").ToArray());

        // Fleet menu > Orders' own entry point (GalaxyMapScreen.FleetOrders): opens straight onto the
        // Orders tab rather than always defaulting to Close Up, matching WorldInfoOverlay's own
        // initialTab convention.
        var initialIndex = Array.IndexOf(_tabKinds, initialTab == "Orders" ? TabKind.Orders : TabKind.CloseUp);
        if (initialIndex >= 0)
        {
            _frame.SelectIndex(initialIndex);
        }
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_frame.HandleKey(key))
        {
            return;
        }

        if (_tabKinds[_frame.ActiveIndex] == TabKind.Orders)
        {
            HandleOrdersKey(key);
            return;
        }

        // _frame.HandleKey already owns Ctrl+PageUp/PageDown (the tab switch); a Ctrl chord it doesn't
        // recognize must still never fall through to the C/T/J/R letter match below (Ctrl+T etc. isn't
        // a fleet-action shortcut) or the any-key-closes fallback (Ctrl+<anything unhandled> should just
        // be ignored, not close the overlay).
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return;
        }

        // F10: dismiss straight back to the map with the cursor moved onto whatever this Close Up was
        // showing -- no Pascal equivalent (CloseUpCom is non-modal, so real Pascal's own cursor never
        // left the object in the first place), added per the user's own explicit request. Checked ahead
        // of the fleet-action shortcuts below so it can never be shadowed by one.
        if (key.Key == ConsoleKey.F10)
        {
            IsDismissed = true;
            _onGoToMap(_obj);
            return;
        }

        // F2: rename this object for the viewer -- NamesOverlay's own Rename, now reachable directly
        // from whatever's already up on screen instead of only through the Names list. Names is
        // per-viewer (obj.Names[_viewer]), so this works the same whether or not _obj is actually owned.
        if (key.Key == ConsoleKey.F2)
        {
            _push(new TextPromptOverlay("Name", "New name (blank to clear):", _obj.Names.GetValueOrDefault(_viewer, string.Empty), newName =>
            {
                if (newName.Length == 0)
                {
                    _obj.Names.Remove(_viewer);
                }
                else
                {
                    _obj.Names[_viewer] = newName;
                }
            }));
            return;
        }

        if (_obj is Fleet ownFleet && ReferenceEquals(ownFleet.Owner, _viewer) &&
            _resolveFleetAction(char.ToUpperInvariant(key.KeyChar), ownFleet) is { } action)
        {
            IsDismissed = true;
            action(ownFleet);
            return;
        }

        // Any other key closes -- CloseUpWindow's own doc comment on this already being a deviation
        // from real Pascal's non-modal CloseUpCom.
        IsDismissed = true;
    }

    private void HandleOrdersKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            TryCommitOrders();
            return;
        }

        // Ctrl+N: this port's own "next order" marker (no ORDERS.PAS equivalent) -- FleetOrdersTabView's
        // own doc comment on why blind index-preservation isn't imitated here.
        if (key.Key == ConsoleKey.N && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            _ordersEditor!.MarkedLine = _ordersEditor.CursorRow + 1;
            return;
        }

        _ordersEditor!.HandleKey(key);
    }

    // FleetOrdersTabView.TryCommit: Esc attempts to commit (FleetOrdersCommand's own
    // REPEAT...UNTIL Ok OR Abort, FLTCOMM.PAS:877-888) -- a successful compile applies the result and
    // closes this whole overlay; a failed one shows the real compile error via a Yes/No confirm (Yes
    // discards every edit and closes, No/Esc returns to the editor).
    private void TryCommitOrders()
    {
        var fleet = (Fleet)_obj;
        var result = FleetOrderCompiler.Compile(_game, fleet.Owner, _ordersEditor!.Lines, _ordersEditor.MarkedLine);

        if (result.ErrorMessage is not null)
        {
            _push(new ConfirmOverlay("Orders", $"{result.ErrorMessage} {result.ErrorLine}.\n\nDiscard changes and close?", yes =>
            {
                if (yes)
                {
                    IsDismissed = true;
                }
            }));
            return;
        }

        var fleetName = DescribeLocation(fleet, _viewer);
        var myLord = Honorifics.MyLord(fleet.Owner.IsEmpress);
        FleetMovementHandler.CommitOrders(fleet, result.Orders, result.MarkedOrderIndex, _game);
        var message = result.Orders.Count == 0
            ? $"All orders to {fleetName} cancelled, {myLord}."
            : $"Orders to {fleetName} completed, {myLord}.";

        IsDismissed = true;
        _showInfo("Orders", message);
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(FrameWidth, fb.Width);
        var height = Math.Min(FrameHeight, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        var name = DisplayName(_obj, _viewer);
        _frame.Draw(fb, x, y, width, height, name, BorderFg, BorderBg, ConsoleColor.White, BorderBg);

        var contentX = x + 1;
        var contentY = y + 1;
        var contentWidth = width - 2;
        var contentHeight = height - 2;
        for (var row = 0; row < contentHeight; row++)
        {
            fb.DrawText(contentX, contentY + row, new string(' ', contentWidth), ContentFg, ContentBg);
        }

        if (_tabKinds[_frame.ActiveIndex] == TabKind.Orders)
        {
            DrawOrders(fb, contentX, contentY, contentWidth, contentHeight);
        }
        else
        {
            DrawContent(fb, contentX, contentY, contentWidth, contentHeight);
        }
    }

    private void DrawOrders(FrameBuffer fb, int cx, int cy, int cw, int ch)
    {
        var fleet = (Fleet)_obj;
        var name = DisplayName(fleet, _viewer);
        fb.DrawText(cx, cy, $"Orders: {name}", ContentFg, ContentBg);

        const int hintLines = 2;
        var editorHeight = Math.Max(1, ch - 1 - hintLines);
        _ordersEditor!.Draw(fb, cx, cy + 1, cw, editorHeight, ContentFg, ContentBg, GutterFg, CursorFg, CursorBg);

        fb.DrawText(cx, cy + ch - 2, "DESTination <name/x,y>  TRANsfer <amt> <code>  REPEat  WAIT  REFUel  JOIN [OVER]", ContentFg, ContentBg, maxWidth: cw);
        fb.DrawText(cx, cy + ch - 1, "Ctrl+N: mark as next order   Esc: compile and close", ContentFg, ContentBg, maxWidth: cw);
    }

    private void DrawContent(FrameBuffer fb, int cx, int cy, int cw, int ch)
    {
        // Every field below sits at a fixed row/column transcribed from CLSCOMM.PAS's own layout,
        // which assumed a fixed 80x24 DOS screen -- on a terminal smaller than this frame's own natural
        // size (clamped in Draw above), a row past the bottom border would otherwise draw straight over
        // it instead of just being omitted. Real Pascal never had to handle this at all (its own screen
        // could never be smaller than the layout it was drawn for); clipping is this port's own
        // equivalent, not a restoration of anything.
        void At(int x, int y, string text)
        {
            if (x < cw && y < ch)
            {
                fb.DrawText(cx + x, cy + y, text, ContentFg, ContentBg, maxWidth: cw - x);
            }
        }

        var fleet = _obj as Fleet;
        string headerKind, headerOwner;
        if (fleet is not null)
        {
            var fleetScouted = Game.ScoutedOrOwned(_viewer, fleet);
            headerKind = fleetScouted ? FleetTypeNames[(int)fleet.Type] : "";
            headerOwner = fleetScouted ? _obj.Owner.Name : "";
        }
        else
        {
            headerKind = CloseUpWindowText.DescribeKind(_obj);
            headerOwner = _obj.Owner.Name;
        }

        var name = DisplayName(_obj, _viewer);
        At(0, 0, $"Close Up: {name}");
        At(35, 0, headerKind);
        At(59, 0, headerOwner);

        if (fleet is not null)
        {
            LayoutFleet(fleet, At);
        }
        else if (_obj is IEconomicWorld world)
        {
            LayoutWorld(world, At);
        }

        // No worldOwned branch: OpenExamine already routes any world the viewer owns to WorldInfoOverlay
        // instead of this class, so CloseUpOverlay only ever sees a fleet or someone else's world.
        var fleetOwned = fleet is not null && ReferenceEquals(fleet.Owner, _viewer);
        At(1, 17, fleetOwned
            // D (Deploy) is deliberately absent: Deploy has no path here from an existing fleet (it
            // only launches from a world picked via the map cursor) -- its own follow-on slice.
            ? $"F2:rename  F10:map   Ctrl+PgUp/PgDn: Orders tab   {GalaxyMapScreen.FleetActionHint(fleet!, _resolveFleetAction)}"
            : "F2:rename  F10:map   (any other key closes)");
    }

    private void LayoutWorld(IEconomicWorld world, Action<int, int, string> at)
    {
        var owned = ReferenceEquals(world.Owner, _viewer);
        var scouted = Game.ScoutedOrOwned(_viewer, world);

        at(1, 2, " Cls:"); at(1, 3, "Tech:"); at(1, 4, " Pop:");
        at(23, 2, "Eff:"); at(23, 3, "Amb:"); at(23, 4, "Rev:");
        if (scouted)
        {
            at(7, 2, CloseUpWindowText.WorldClassNames[world.EffectiveClass]);
            at(7, 3, CloseUpWindowText.TechLevelNames[world.TechLevel]);
            at(7, 4, world.Population.ToString());
            at(28, 2, $"{world.Efficiency}%");
            at(28, 3, world.IsAddictedToAmbrosia ? "yes" : "no");
            at(28, 4, world.RevolutionIndex.ToString());
        }

        at(51, 2, "amb  che  met  sup  tri");
        at(49, 3, owned
            ? $"{world.Cargo.Ambrosia,5}{world.Cargo.Chemicals,5}{world.Cargo.Metals,5}{world.Cargo.Supplies,5}{world.Cargo.Trillum,5}"
            : "???? ???? ???? ???? ????");

        at(2, 6, "fgt  hkr  jmp  jtn  pen  str  trn    men  nnj    LAM  def  GDM  ion");
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;
        string Level(int value) => owned ? value.ToString() : scouted ? CloseUpWindowText.YesNo(value) : "????";
        at(0, 7,
            $"{Level(s.Fighters),5}{Level(s.HunterKillers),5}{Level(s.Jumpships),5}{Level(s.Jumptransports),5}{Level(s.Penetrators),5}{Level(s.Starships),5}{Level(s.Transports),5}" +
            $"  {Level(c.Legions),5}{Level(c.NinjaLegions),5}" +
            $"  {Level(d.Lams),5}{Level(d.DefenseSatellites),5}{Level(d.Gdms),5}{Level(d.IonCannons),5}");

        if (scouted && Game.FindWorldBackgroundText(_game, world, _viewer, conquer: false) is { } background)
        {
            for (var i = 0; i < background.Count; i++)
            {
                at(1, 9 + i, background[i]);
            }
        }
    }

    private void LayoutFleet(Fleet fleet, Action<int, int, string> at)
    {
        var owned = ReferenceEquals(fleet.Owner, _viewer);
        var scouted = Game.ScoutedOrOwned(_viewer, fleet);
        var origin = _viewer.Capital?.Location ?? new Coordinate(_game.Galaxy.Size / 2, _game.Galaxy.Size / 2);

        at(1, 2, "   Position:");
        at(14, 2, RelativeCoordinate.Format(fleet.Location, origin));

        at(1, 3, "     Status:");
        at(14, 3, CloseUpWindowText.DescribeFleetStatus(fleet, _viewer, _game));

        at(1, 4, "Destination:");
        at(14, 4, CloseUpWindowText.DescribeFleetDestination(fleet, _viewer, origin));

        at(1, 5, "      Range:");
        at(14, 5, owned ? FleetLifecycle.EstimatedRange(fleet).ToString() : "(unknown)");

        at(2, 7, "fgt  hkr  jmp  jtn  pen  str  trn  men  nnj  amb  che  met  sup  tri");
        var s = fleet.Ships;
        var c = fleet.Cargo;
        string ShipLevel(int value) => owned ? value.ToString() : scouted ? CloseUpWindowText.YesNo(value) : "????";
        string CargoLevel(int value) => owned ? value.ToString() : "????";
        at(0, 8,
            $"{ShipLevel(s.Fighters),5}{ShipLevel(s.HunterKillers),5}{ShipLevel(s.Jumpships),5}{ShipLevel(s.Jumptransports),5}{ShipLevel(s.Penetrators),5}{ShipLevel(s.Starships),5}{ShipLevel(s.Transports),5}" +
            $"{CargoLevel(c.Legions),5}{CargoLevel(c.NinjaLegions),5}{CargoLevel(c.Ambrosia),5}{CargoLevel(c.Chemicals),5}{CargoLevel(c.Metals),5}{CargoLevel(c.Supplies),5}{CargoLevel(c.Trillum),5}");
    }

    internal static string DescribeLocation(ISectorObject obj, Empire viewer) => obj switch
    {
        Fleet => "Fleet",
        _ => RelativeCoordinate.Format(obj.Location, viewer.Capital?.Location ?? new Coordinate(0, 0)),
    };

    /// <summary>A viewer's own name for <paramref name="obj"/> if they've named it, else <see cref="DescribeLocation"/> -- the same fallback every screen in this project independently needed at least three times over (GalaxyMapScreen, TacticalBattleScreen, and this class's own several inline uses) before it was worth sharing.</summary>
    internal static string DisplayName(ISectorObject obj, Empire viewer) => obj.Names.GetValueOrDefault(viewer) ?? DescribeLocation(obj, viewer);
}
