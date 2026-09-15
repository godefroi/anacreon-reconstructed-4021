using System.Text;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;
using Reconstructed4021.Tui2.NewGame;
using Reconstructed4021.Tui2.Overlays;


namespace Reconstructed4021.Tui2.Screens;


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

    // Close Up (and, once 2+ objects share a sector, the picker in front of it) -- only the top
    // overlay ever sees a key; it's popped by reference the frame its own IsDismissed goes true (never
    // by index -- an overlay whose own HandleKey both dismisses itself and pushes a replacement, like
    // Deploy's fleet-name prompt handing off to a map-cursor pick, would otherwise pop whatever's on
    // top *after* that push, not the dismissed one). See IOverlay's own doc comment for why "route to
    // every overlay" is the bug this avoids.
    private readonly List<IOverlay> _overlays = [];

    // "Move the cursor and confirm" mode -- GameShell.BeginPick/EndPick's own map-cursor-reuse pattern
    // for Deploy's source/destination picks (and, later, Change Destination/Transfer/etc.). Menu bar
    // and overlays are irrelevant while this is set; arrows still move the cursor via HandleMapKey,
    // Enter confirms at the current cursor location, Esc cancels with no callback.
    private (string Prompt, Action<Coordinate> OnConfirm)? _pendingPick;

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

        ScreenHost.SetTitle($"Anacreon - {player.Name}");
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
            new MenuBar.Item("_Next Turn", EndTurn),
            new MenuBar.Item("_Quit", ConfirmQuit),
            new MenuBar.Item("E_xit to OS", ConfirmExitToOs),
        ]),
        new MenuBar.TopItem("_Empire", [
            new MenuBar.Item("_Send Message", SendMessageCommand),
            new MenuBar.Item("_Read Messages", ReadMessagesCommand),
            new MenuBar.Item("_Trade Technology", Stub),
            new MenuBar.Item("Te_ch Tree", () => _overlays.Add(new TechTreeOverlay(_player))),
        ]),
        new MenuBar.TopItem("_Worlds", [
            new MenuBar.Item("_Close Up", ExamineCursor),
            new MenuBar.Item("_Designate", () => OpenOwnWorldTab("Designate")),
            new MenuBar.Item("P_roduction", () => OpenOwnWorldTab("Production")),
            new MenuBar.Item("_ISSP", () => OpenOwnWorldTab("ISSP")),
            new MenuBar.Item("_Add Name", AddNameCommand),
            new MenuBar.Item("Delete _Name", DeleteNameCommand),
            new MenuBar.Item("_Liberate", Stub),
            new MenuBar.Item("_Self-Destruct", Stub),
        ]),
        new MenuBar.TopItem("_Fleet", [
            new MenuBar.Item("_Deploy", DeployFleet),
            new MenuBar.Item("_Change Destination", ChangeDestination),
            new MenuBar.Item("_Transfer", TransferFleet),
            new MenuBar.Item("_Abort/Join", AbortJoinFleet),
            new MenuBar.Item("_Refuel", RefuelFleet),
            new MenuBar.Item("_SRM Sweep", SrmSweep),
            new MenuBar.Item("_Orders", FleetOrders),
            // Not "Canc_el" -- 'E' collides with the top-level "_Empire" menu's own hotkey, and
            // MenuBar checks top-level hotkeys first while a dropdown is open (its own doc comment:
            // an item's hotkey must never collide with a *menu's*, only with another item's).
            new MenuBar.Item("Cance_l Orders", CancelFleetOrders),
            new MenuBar.Item("Res_upply", ResupplyMission),
            new MenuBar.Item("_Probe", LaunchProbe),
        ]),
        new MenuBar.TopItem("_Build", [
            new MenuBar.Item("_Site Status", () => _overlays.Add(new ConstructionSiteStatusOverlay(_game, _player, obj => OpenExamine(obj, "Close Up")))),
            new MenuBar.Item("_New", NewConstruction),
            new MenuBar.Item("_Abort", AbortConstruction),
        ]),
        new MenuBar.TopItem("_Ministry of War", [
            new MenuBar.Item("_Attack", Attack),
            new MenuBar.Item("Auto A_ttack", AutoAttack),
            new MenuBar.Item("Launch _LAMs", LaunchLams),
            new MenuBar.Item("_Defenses", () => _overlays.Add(new DefensesOverlay(_player.DefenseSettings.Fleets))),
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

        // F1/F3/F5/F7/F8/F9 switch directly between the six Fn report windows, same as real Pascal's
        // own dispatch loop -- no need to back out of Status before News is reachable. Only live when
        // the overlay stack is empty or already rooted in one of these six (SwitchFKeyOverlay's own
        // doc comment): a fleet-name prompt or a confirm dialog stacked on top of the map shouldn't
        // get silently hijacked by a stray F-key.
        if (key.Key is ConsoleKey.F1 or ConsoleKey.F3 or ConsoleKey.F5 or ConsoleKey.F7 or ConsoleKey.F8 or ConsoleKey.F9 &&
            (_overlays.Count == 0 || _overlays.Exists(IsFKeyOverlay)))
        {
            switch (key.Key)
            {
                case ConsoleKey.F1:
                    SwitchFKeyOverlay(new HelpOverlay(_overlays.Add));
                    return;
                case ConsoleKey.F3:
                    SwitchFKeyOverlay(new StatusOverlay(_game, _player, obj => OpenExamine(obj, "Close Up")));
                    return;
                case ConsoleKey.F5:
                    SwitchFKeyOverlay(new FleetOverlay(_game, _player, _origin, obj => OpenExamine(obj, "Close Up")));
                    return;
                case ConsoleKey.F7:
                    SwitchFKeyOverlay(new NewsOverlay(_player, _origin, obj => OpenExamine(obj, "Close Up")));
                    return;
                case ConsoleKey.F8:
                    SwitchFKeyOverlay(new EmpireOverlay(_game, _player, obj => OpenExamine(obj, "Close Up")));
                    return;
                case ConsoleKey.F9:
                    OpenNames();
                    return;
            }
        }

        if (_overlays.Count > 0)
        {
            _overlays[^1].HandleKey(key);

            // RemoveAll, not just popping from the top while it's dismissed: a handler can both dismiss
            // itself AND push a new overlay in the same call (e.g. CloseUpOverlay's C/T/J/R shortcuts --
            // IsDismissed=true, then the action pushes Transfer's own ObjectPickerOverlay/PickGround
            // picker on top), which leaves the dismissed one buried under the new top rather than
            // sitting there as the top itself. A top-only pop loop never reaches a dismissed entry that
            // isn't on top, and the same underlying scenario (a confirm overlay's callback dismissing the
            // overlay underneath it too, e.g. CloseUpOverlay.TryCommitOrders's discard path) needs a
            // sweep either way, not just a cascade from the top down.
            _overlays.RemoveAll(o => o.IsDismissed);

            return;
        }

        if (_pendingPick is { } pick)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    _pendingPick = null;
                    pick.OnConfirm(_cursor);
                    return;
                case ConsoleKey.Escape:
                    _pendingPick = null;
                    return;
            }

            HandleMapKey(key);
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

        if (key.Key == ConsoleKey.Enter)
        {
            ExamineCursor();
            return;
        }

        HandleMapKey(key);
    }

    // GameShell.BeginPick, minus the openPanelDismiss call: that existed only to work around TG's own
    // AddModal disabling the map underneath a still-open panel, which this engine's overlay stack has
    // no equivalent of -- an overlay that wants a pick already dismissed itself (by reference removal,
    // immediately) before calling this, so the stack is already empty by the time the next key arrives.
    private void BeginPick(string prompt, Action<Coordinate> onConfirm) => _pendingPick = (prompt, onConfirm);

    /// <summary>
    /// Enter on the map, or Worlds menu &gt; Close Up (MAPWIND.PAS: GetMapObject/SelectPoint feeding
    /// PLAYTURN.PAS's InfoCom/CLSCOMM.PAS's CloseUpCom): a single object at the cursor opens
    /// <see cref="CloseUpOverlay"/> directly; 2+ opens <see cref="ObjectPickerOverlay"/> first; none is
    /// a silent no-op in real Pascal either, but this port has nothing useful to say when there's
    /// truly nothing there, so it stays a no-op.
    /// </summary>
    private void ExamineCursor()
    {
        var objects = ObjectsAt(_cursor);
        switch (objects.Count)
        {
            case 0:
                return;
            case 1:
                OpenExamine(objects[0], "Close Up");
                break;
            default:
                _overlays.Add(new ObjectPickerOverlay(objects, _player, obj => OpenExamine(obj, "Close Up"), ResolveFleetContextAction));
                break;
        }
    }

    // GameShell.ShowCloseUp's own routing: one of the player's own worlds gets the full tabbed
    // WorldInfoOverlay (Close Up is just its first tab); anything else -- a fleet, another empire's
    // world -- gets the standalone CloseUpOverlay, which still needs its own general-purpose
    // Known/Scouted redaction since it can't assume ownership.
    private void OpenExamine(ISectorObject obj, string initialTab)
    {
        if (obj is IEconomicWorld ownWorld && ReferenceEquals(ownWorld.Owner, _player))
        {
            _overlays.Add(new WorldInfoOverlay(ownWorld, _player, _game, _context.Random, Refresh, ShowInfo, _overlays.Add, DeployFleet, GoToMapFromExamine, initialTab));
            return;
        }

        _overlays.Add(new CloseUpOverlay(obj, _player, _game, ShowInfo, _overlays.Add, ResolveFleetContextAction, GoToMapFromExamine, initialTab));
    }

    // CloseUpOverlay and WorldInfoOverlay's own F10: drop every overlay (Close Up/World Info itself,
    // and whatever report/picker window it was drilled into from) and land the cursor right on the
    // object that was up, rather than back wherever the cursor happened to be left.
    private void GoToMapFromExamine(ISectorObject obj)
    {
        _cursor = obj.Location;
        _overlays.Clear();
    }

    // GameShell.ShowNamesWindow: F2/F8 mutate in place and just refresh, but F3 (add a bookmark)
    // genuinely needs the map cursor interactive, so NamesOverlay dismisses itself first and calls
    // this back -- BeginPick's own doc comment requires the overlay stack already be empty by the
    // time it's called, which is exactly the state right after that self-dismissal.
    private void OpenNames() =>
        SwitchFKeyOverlay(new NamesOverlay(_game, _player, obj => OpenExamine(obj, "Close Up"), JumpToLocation, _overlays.Add, AddBookmark));

    // Status/Fleet/News/Empire/Help/Names -- the six report windows an F-key can jump straight to
    // from any of the others, no Esc needed first (real Pascal's own dispatch loop). Distinguished by
    // type rather than tracking a separate "current Fn overlay" field: every push site for these six
    // adds to an empty stack (the top-level F-key dispatch above is the only place that constructs
    // one), so whichever one is present is always at index 0 -- anything stacked above it (Close Up
    // drilled into from Status, Help's own Index/Search, a Names rename prompt) is that overlay's own
    // business, not a second Fn overlay.
    private static bool IsFKeyOverlay(IOverlay overlay) =>
        overlay is StatusOverlay or FleetOverlay or NewsOverlay or EmpireOverlay or HelpOverlay or NamesOverlay;

    // Truncates back to (and including) the Fn overlay at the root of the stack, discarding anything
    // stacked on top of it, then opens the next one in its place -- the same "one active window"
    // switch pressing another F-key gives you in real Pascal.
    private void SwitchFKeyOverlay(IOverlay next)
    {
        var root = _overlays.FindIndex(IsFKeyOverlay);
        if (root >= 0)
        {
            _overlays.RemoveRange(root, _overlays.Count - root);
        }

        _overlays.Add(next);
    }

    private void JumpToLocation(Coordinate location) => _cursor = location;

    private void AddBookmark() =>
        BeginPick("Add Bookmark -- move cursor to location, Enter: select, Esc: cancel", location =>
            _overlays.Add(new TextPromptOverlay("Name Location", "Bookmark name:", string.Empty, name =>
            {
                if (name.Length > 0)
                {
                    _player.Bookmarks.Add(new LocationBookmark { Name = name, Location = location });
                }

                OpenNames();
            })));

    // GameShell.Designate/Issp/Production: all three (and Close Up) route through the one
    // WorldInfoOverlay, just opened on a different starting tab -- unlike ExamineCursor, these only
    // ever make sense on one of the player's own worlds, so anything else is a plain error message
    // rather than a picker.
    private void OpenOwnWorldTab(string tab)
    {
        if (_objectsByLocation.TryGetValue(_cursor, out var obj) && obj is IEconomicWorld world && ReferenceEquals(world.Owner, _player))
        {
            _overlays.Add(new WorldInfoOverlay(world, _player, _game, _context.Random, Refresh, ShowInfo, _overlays.Add, DeployFleet, GoToMapFromExamine, tab));
            return;
        }

        ShowInfo(tab, "Move the cursor onto one of your own worlds first.");
    }

    // GameShell.ObjectsAt: every object at location the player can actually see (Game.Visible) --
    // _objectsByLocation only ever holds one non-fleet slot per coordinate (real Pascal's own
    // Sector[x]^[y].Obj is the same single slot), so fleets need their own separate lookup here too.
    private List<ISectorObject> ObjectsAt(Coordinate location)
    {
        var result = new List<ISectorObject>();
        if (_objectsByLocation.TryGetValue(location, out var obj))
        {
            result.Add(obj);
        }

        result.AddRange(_fleetsByLocation[location]);
        return result.Where(o => Game.Visible(_player, o)).ToList();
    }

    // LaunchFleetCommand's own real parameter order (FLTCOMM.PAS): name, then source, then destination,
    // then composition last. Name is always asked first regardless of which overload is used, matching
    // that same real parameter order (name before source is ever validated).
    private void DeployFleet()
    {
        _overlays.Add(new TextPromptOverlay("Name This Fleet", "Fleet name (optional):", string.Empty, name =>
            BeginPick("Deploy Fleet -- move cursor to a world to launch from, Enter: select, Esc: cancel",
                location => PickDeploySource(location, name)),
            maxLength: 40, borderFg: ConsoleColor.Gray, borderBg: ConsoleColor.Black));
    }

    // Contextual Deploy from an owned world's own Close Up (WorldInfoOverlay's D shortcut): that world
    // is already the deploy source, no map-cursor source pick needed -- GameShell.DeployFleet(ISectorObject)'s
    // own equivalent, narrowed to a world since that's the only source WorldInfoOverlay can ever offer
    // (a Fleet source would need FleetLifecycle.DeployFleet to accept a Fleet, which nothing here wires
    // up yet -- CloseUpOverlay's own D-key note already flagged that as its own follow-on slice).
    private void DeployFleet(IEconomicWorld source)
    {
        _overlays.Add(new TextPromptOverlay("Name This Fleet", "Fleet name (optional):", string.Empty, name =>
            ValidateDeploySource(source, name),
            maxLength: 40, borderFg: ConsoleColor.Gray, borderBg: ConsoleColor.Black));
    }

    // IDParm2 (Question 8, "Where shall we deploy the fleet from?").
    private void PickDeploySource(Coordinate location, string fleetName)
    {
        if (!_objectsByLocation.TryGetValue(location, out var obj) || obj is not IEconomicWorld source || !ReferenceEquals(source.Owner, _player))
        {
            ShowInfo("Deploy Fleet", "That isn't one of your own worlds.");
            return;
        }

        ValidateDeploySource(source, fleetName);
    }

    private void ValidateDeploySource(IShipCargoHolder source, string fleetName)
    {
        if (!HasAnyShips(source.Ships))
        {
            ShowInfo("Deploy Fleet", "There are no ships here to deploy.");
            return;
        }

        // XYParm (Question 9, "What shall its destination be?").
        BeginPick("Deploy Fleet -- move cursor to destination, Enter: select, Esc: cancel",
            destination => BeginDeployDistribution(source, fleetName, destination));
    }

    private void BeginDeployDistribution(IShipCargoHolder source, string fleetName, Coordinate destination)
    {
        // A snapshot, not the source's own live Ships/Cargo -- FleetLifecycle.ChangeCompositionOfFleet
        // (which DeployFleet calls internally) overwrites the launch world's own Ships in place, and
        // the editor mutates ground counts live as the player fills/empties columns; passing the real
        // object through would let that happen before the player ever confirms anything.
        var groundShips = CloneShips(source.Ships);
        var groundCargo = CloneCargo(source.Cargo);
        var fleetShips = new ShipCounts();
        var fleetCargo = new CargoHold();
        var sourceName = CloseUpOverlay.DisplayName((ISectorObject)source, _player);

        _overlays.Add(new ResourceDistributionOverlay(
            $"Deploy Fleet from {sourceName}", fleetShips, fleetCargo, groundShips, groundCargo,
            groundIsPlayerOwned: true, groundIsAFleet: source is Fleet,
            onCommitted: () =>
            {
                // NoShips (MISC.PAS) -- LaunchFleetCommand's own "IF NOT NoShips(FltSh)" guard: nothing
                // was actually put aboard, so there's nothing to deploy.
                if (!HasAnyShips(fleetShips))
                {
                    return;
                }

                var fleet = FleetLifecycle.DeployFleet(_player, source, fleetShips, fleetCargo, destination, _game);
                if (!string.IsNullOrWhiteSpace(fleetName))
                {
                    // LaunchFleetCommand's own FleetName[1]:=UpCase(FleetName[1]) (FLTCOMM.PAS:517).
                    fleet.Names[_player] = char.ToUpperInvariant(fleetName[0]) + fleetName[1..];
                }

                Refresh();
            }));
    }

    private static bool HasAnyShips(ShipCounts s) =>
        s.Fighters + s.HunterKillers + s.Jumpships + s.Jumptransports + s.Penetrators + s.Starships + s.Transports > 0;

    private static ShipCounts CloneShips(ShipCounts s) => new()
    {
        Fighters = s.Fighters, HunterKillers = s.HunterKillers, Jumpships = s.Jumpships,
        Jumptransports = s.Jumptransports, Penetrators = s.Penetrators, Starships = s.Starships, Transports = s.Transports,
    };

    private static CargoHold CloneCargo(CargoHold c) => new()
    {
        Legions = c.Legions, NinjaLegions = c.NinjaLegions, Ambrosia = c.Ambrosia,
        Chemicals = c.Chemicals, Metals = c.Metals, Supplies = c.Supplies, Trillum = c.Trillum,
    };

    // GameShell.PickOwnFleetAtCursor: resolves "the player's own fleet under the cursor" for every
    // Fleet-menu command below -- 0 own fleets there is an error, exactly 1 auto-picks, 2+ opens the
    // same object picker ExamineCursor uses (two of the player's own fleets can share a sector, e.g.
    // to Transfer between them).
    private void PickOwnFleetAtCursor(string title, Action<Fleet> onChosen)
    {
        var fleets = _fleetsByLocation[_cursor].Where(f => ReferenceEquals(f.Owner, _player)).ToList();
        switch (fleets.Count)
        {
            case 0:
                ShowInfo(title, "Move the cursor onto one of your own fleets first.");
                break;
            case 1:
                onChosen(fleets[0]);
                break;
            default:
                _overlays.Add(new ObjectPickerOverlay(fleets.Cast<ISectorObject>().ToList(), _player, obj => onChosen((Fleet)obj)));
                break;
        }
    }

    private IEconomicWorld? FindWorldAt(Coordinate location) =>
        _objectsByLocation.TryGetValue(location, out var obj) ? obj as IEconomicWorld : null;

    /// <summary>
    /// GameShell.PickGround (FLTCOMM.PAS's own GetGround): every candidate at <paramref name="source"/>'s
    /// own location that Transfer/Abort-Join can target -- the player's own fleets and world sort
    /// first, then anyone else's, matching real Pascal's own display order. Real GetGround always opens
    /// the picker even for a single candidate; this port auto-picks it instead (same
    /// PickOwnFleetAtCursor exactly-1-auto-picks convenience below), per the user's own explicit
    /// request -- a deliberate deviation, not a restoration.
    /// </summary>
    private void PickGround(Fleet source, bool playerOnly, bool includeFleet, string title, string emptyMessage, Action<ISectorObject> onPicked,
        Func<ISectorObject, bool>? exclude = null, string? excludedEmptyMessage = null)
    {
        var ownFleets = new List<ISectorObject>();
        var enemyFleets = new List<ISectorObject>();
        foreach (var f in _fleetsByLocation[source.Location])
        {
            if (!includeFleet && ReferenceEquals(f, source))
            {
                continue;
            }

            var isOwn = ReferenceEquals(f.Owner, _player);
            if (playerOnly && !isOwn)
            {
                continue;
            }

            (isOwn ? ownFleets : enemyFleets).Add(f);
        }

        ISectorObject? ownWorld = null;
        ISectorObject? enemyWorld = null;
        if (FindWorldAt(source.Location) is { } world)
        {
            if (ReferenceEquals(world.Owner, _player))
            {
                ownWorld = world;
            }
            else if (!playerOnly)
            {
                enemyWorld = world;
            }
        }

        var candidates = new List<ISectorObject>();
        candidates.AddRange(ownFleets);
        if (ownWorld is not null)
        {
            candidates.Add(ownWorld);
        }

        candidates.AddRange(enemyFleets);
        if (enemyWorld is not null)
        {
            candidates.Add(enemyWorld);
        }

        if (candidates.Count == 0)
        {
            ShowInfo(title, emptyMessage);
            return;
        }

        if (exclude is not null)
        {
            candidates = candidates.Where(c => !exclude(c)).ToList();
            if (candidates.Count == 0)
            {
                ShowInfo(title, excludedEmptyMessage ?? emptyMessage);
                return;
            }
        }

        if (candidates.Count == 1)
        {
            onPicked(candidates[0]);
            return;
        }

        _overlays.Add(new ObjectPickerOverlay(candidates, _player, onPicked));
    }

    // Fleet menu > Transfer (FLTCOMM.PAS: TransferFleetCommand) -- the same Resource Distribution
    // Editor Deploy uses, between one of the player's own fleets and whatever PickGround picks as the
    // other side (any owner).
    // GameShell.ResolveFleetContextAction: C/T/J/A/R, the five Fleet/Ministry-of-War commands reachable
    // directly off an already-selected owned fleet in CloseUpOverlay and the sector object picker. D
    // (Deploy) isn't here -- CloseUpOverlay/the sector picker never carry a source object Tui2's own
    // Deploy flow can consume directly (it only launches from a world picked via the map cursor, not an
    // existing fleet), so wiring D would mean building that fleet-source deploy path first, not just
    // pointing at an existing method. A (Attack) is gated on HasAttackTarget -- a port-only refinement
    // over tui1's own FleetActionHint (which shows it unconditionally for any owned fleet) per the
    // user's own explicit request: no point offering Attack from a fleet sitting somewhere with nothing
    // to hit.
    private Action<Fleet>? ResolveFleetContextAction(char letter, Fleet fleet) => letter switch
    {
        'C' => ChangeDestination,
        'T' => TransferFleet,
        'J' => AbortJoinFleet,
        'A' => HasAttackTarget(fleet) ? Attack : null,
        'R' => RefuelFleet,
        _ => null,
    };

    // Shared by ObjectPickerOverlay/CloseUpOverlay's own fleet-action hint lines: built from whichever
    // letters resolve actually resolves for this fleet right now, rather than a fixed string, so the
    // hint can never drift out of sync with what a keypress would actually do (A:attack in particular,
    // now that it's conditionally available).
    internal static string FleetActionHint(Fleet fleet, Func<char, Fleet, Action<Fleet>?> resolve) =>
        string.Join("  ", FleetActionLabels.Where(l => resolve(l.Letter, fleet) is not null).Select(l => $"{l.Letter}:{l.Label}"));

    private static readonly (char Letter, string Label)[] FleetActionLabels = [
        ('C', "dest"), ('T', "transfer"), ('J', "abort/join"), ('A', "attack"), ('R', "refuel"),
    ];

    private void TransferFleet() => PickOwnFleetAtCursor("Transfer Fleet", TransferFleet);

    // Split out from the cursor-driven wrapper above so the Close Up/sector-picker T shortcut can call
    // it directly on an already-known fleet, instead of routing back through PickOwnFleetAtCursor (which
    // re-resolves from the map cursor and would re-prompt a picker even when the fleet is already given).
    private void TransferFleet(Fleet fleet) =>
        PickGround(fleet, playerOnly: false, includeFleet: false, "Transfer Fleet",
            "There is nothing here to transfer with.",
            ground => BeginTransferDistribution(fleet, ground));

    private void BeginTransferDistribution(Fleet fleet, ISectorObject ground)
    {
        var groundHolder = (IShipCargoHolder)ground;
        var fleetShips = CloneShips(fleet.Ships);
        var fleetCargo = CloneCargo(fleet.Cargo);
        var groundShips = CloneShips(groundHolder.Ships);
        var groundCargo = CloneCargo(groundHolder.Cargo);
        var fleetName = CloseUpOverlay.DisplayName(fleet, _player);
        var groundName = CloseUpOverlay.DisplayName(ground, _player);

        _overlays.Add(new ResourceDistributionOverlay(
            $"Transfer -- {fleetName} <-> {groundName}", fleetShips, fleetCargo, groundShips, groundCargo,
            groundIsPlayerOwned: ReferenceEquals(ground.Owner, _player), groundIsAFleet: ground is Fleet,
            onCommitted: () =>
            {
                // ChangeCompositionOfFleet (FLEET.PAS:282-389) -- may destroy either side, see that
                // method's own doc comment; TransferFleetCommand calls it unconditionally too.
                FleetLifecycle.ChangeCompositionOfFleet(fleet, groundHolder, fleetShips, fleetCargo, groundShips, groundCargo, _game);
                Refresh();
            }));
    }

    /// <summary>
    /// Fleet menu > Abort/Join (FLTCOMM.PAS: AbortFleetCommand): dumps the whole fleet onto whatever
    /// PickGround picks -- no distribution grid, matching real Pascal exactly. Two confirmations, both
    /// transcribed from source: target not the player's own, and any single ship type's combined total
    /// exceeding <see cref="ResourceDistribution.MaxResources"/> ("some will be lost"). Declining the
    /// first skips the second -- both just decline the same operation either way.
    /// </summary>
    private void AbortJoinFleet() => PickOwnFleetAtCursor("Abort/Join Fleet", AbortJoinFleet);

    private void AbortJoinFleet(Fleet fleet) =>
        PickGround(fleet, playerOnly: false, includeFleet: false, "Abort/Join Fleet",
            "There is nothing here to abort the fleet to.",
            ground => ConfirmAbortJoin(fleet, ground));

    private void ConfirmAbortJoin(Fleet fleet, ISectorObject ground)
    {
        if (!ReferenceEquals(ground.Owner, _player))
        {
            var groundName = CloseUpOverlay.DisplayName(ground, _player);
            _overlays.Add(new ConfirmOverlay("Abort/Join Fleet",
                $"{groundName} is not part of your empire. Are you sure you want to abort the fleet?",
                yes =>
                {
                    if (yes)
                    {
                        ConfirmAbortJoinOverflow(fleet, ground);
                    }
                }));
            return;
        }

        ConfirmAbortJoinOverflow(fleet, ground);
    }

    private void ConfirmAbortJoinOverflow(Fleet fleet, ISectorObject ground)
    {
        var groundHolder = (IShipCargoHolder)ground;
        var overflow = Enum.GetValues<ShipType>().Any(t => groundHolder.Ships[t] + fleet.Ships[t] > ResourceDistribution.MaxResources);
        if (overflow)
        {
            _overlays.Add(new ConfirmOverlay("Abort/Join Fleet",
                "An object cannot hold so many ships -- some will be lost. Are you sure?",
                yes =>
                {
                    if (yes)
                    {
                        FleetLifecycle.AbortFleet(fleet, groundHolder, _game);
                        Refresh();
                    }
                }));
            return;
        }

        FleetLifecycle.AbortFleet(fleet, groundHolder, _game);
        Refresh();
    }

    // Fleet menu > Change Destination (FLTCOMM.PAS: ChangeDestinationCommand, :614-630): reuses the
    // same map-cursor destination pick Deploy's own XYParm step uses. FleetLifecycle.SetFleetDestination
    // is unconditional -- no legality check beyond "a coordinate" -- works whether the fleet is Ready
    // or already InTransit.
    private void ChangeDestination() => PickOwnFleetAtCursor("Change Destination", ChangeDestination);

    private void ChangeDestination(Fleet fleet) =>
        BeginPick("Change Destination -- move cursor to new destination, Enter: select, Esc: cancel",
            destination =>
            {
                FleetLifecycle.SetFleetDestination(fleet, destination);
                Refresh();
            });

    // Fleet menu > Refuel (FLTCOMM.PAS: RefuelFleetCommand): PickGround restricted to the player's own
    // (IncludeFleet lets the fleet refuel from trillum already in its own cargo), excluding any
    // candidate with nothing to actually refuel from, then a numeric prompt for tons of trillum.
    private void RefuelFleet() => PickOwnFleetAtCursor("Refuel Fleet", RefuelFleet);

    private void RefuelFleet(Fleet fleet) =>
        PickGround(fleet, playerOnly: true, includeFleet: true, "Refuel Fleet",
            "There is no world or fleet of yours here to refuel from.",
            ground => PromptForTrillum(fleet, (IShipCargoHolder)ground),
            exclude: ground => FleetLifecycle.MaxTrillumToRefuel(fleet, (IShipCargoHolder)ground) <= 0,
            excludedEmptyMessage: "There is no trillum available to refuel with.");

    // GetTrillumToUse (FLTCOMM.PAS:692-724): 0 (or a blank field) defaults to the max; out-of-range
    // re-prompts with an error instead of closing -- reopened as a fresh TextPromptOverlay with the
    // error folded into its label, since that widget has no separate error slot of its own.
    private void PromptForTrillum(Fleet fleet, IShipCargoHolder ground, string? errorPrefix = null)
    {
        var maxTri = FleetLifecycle.MaxTrillumToRefuel(fleet, ground);
        var label = (errorPrefix is null ? "" : errorPrefix + " ") + $"Tons of trillum (max {maxTri}, 0 = max):";

        _overlays.Add(new TextPromptOverlay("Refuel Fleet", label, string.Empty, text =>
        {
            if (!int.TryParse(text, out var amount) && text.Length > 0)
            {
                PromptForTrillum(fleet, ground, "Enter a whole number of tons.");
                return;
            }

            if (amount == 0)
            {
                amount = maxTri;
            }
            else if (amount < 0)
            {
                PromptForTrillum(fleet, ground, "That is a most bizarre request.");
                return;
            }
            else if (amount > maxTri)
            {
                PromptForTrillum(fleet, ground, $"The maximum amount allowable is {maxTri} tons.");
                return;
            }

            FleetLifecycle.RefuelFleet(fleet, ground, amount);
            Refresh();
        }));
    }

    // Fleet menu > SRM Sweep (FLTCOMM.PAS: SrmSweepCommand): clears any mine at the fleet's own
    // location -- AddNews only fires when the mine belonged to someone else.
    private void SrmSweep() => PickOwnFleetAtCursor("SRM Sweep", fleet =>
    {
        var location = fleet.Location;
        var mineOwner = _game.Galaxy.GetMineOwner(location);
        if (mineOwner is null)
        {
            ShowInfo("SRM Sweep", "No SRMs found.");
            return;
        }

        if (!ReferenceEquals(mineOwner, _player))
        {
            mineOwner.AddNews(NewsType.MineFieldCleared, position: location, otherEmpire: _player);
        }

        _game.Galaxy.ClearMine(location);
        _game.Galaxy.ClearMineScouted(location);
        Refresh();
        ShowInfo("SRM Sweep", "Mine sweeping completed.");
    });

    // Fleet menu > Orders (FLTCOMM.PAS: FleetOrdersCommand): opens Close Up on the picked fleet,
    // already on its own Orders tab -- see CloseUpOverlay's own Orders-tab doc comment for the
    // compile/commit flow.
    private void FleetOrders() => PickOwnFleetAtCursor("Orders", fleet => OpenExamine(fleet, "Orders"));

    // Fleet menu > Cancel Orders (FLTCOMM.PAS: FleetCancelOrdersCommand, :917-931) -- no window at
    // all, matching real Pascal's own body exactly: clear the queue, zero the resume cursor, report.
    private void CancelFleetOrders() => PickOwnFleetAtCursor("Cancel Orders", fleet =>
    {
        fleet.Orders.Clear();
        fleet.NextOrder = 0;
        var fleetName = DisplayName(fleet);
        ShowInfo("Cancel Orders", $"All orders to {fleetName} cancelled, {MyLord()}.");
    });

    // Fleet menu > Resupply -- no Pascal precedent, a hardcoded fleet-order template
    // (FleetOrderTemplates' own doc comment): pick a fleet, a source world, a destination world, then
    // a cargo type and amount, and commit the generated orders directly (matching how Refuel/Change
    // Destination commit immediately, no editor preview step). Both worlds must be the player's own --
    // FleetOrderTemplates.Resupply's DEST/TRAN/REFU sequence only ever touches ground the fleet
    // actually owns, so an unowned source/destination would just silently no-op every step.
    private void ResupplyMission() => PickOwnFleetAtCursor("Resupply", PickResupplySource);

    private void PickResupplySource(Fleet fleet) =>
        BeginPick("Resupply -- move cursor to source world, Enter: select, Esc: cancel",
            location => PickOwnPlanetOrRetry(location, "Resupply", () => PickResupplySource(fleet),
                source => PickResupplyDestination(fleet, source)));

    private void PickResupplyDestination(Fleet fleet, Planet source) =>
        BeginPick("Resupply -- move cursor to destination world, Enter: select, Esc: cancel",
            location => PickOwnPlanetOrRetry(location, "Resupply", () => PickResupplyDestination(fleet, source),
                destination => _overlays.Add(new ResupplyCargoOverlay(source, fleet, DisplayName(source), (cargo, amount) =>
                {
                    FleetMovementHandler.CommitOrders(fleet, FleetOrderTemplates.Resupply(source, destination, cargo, amount), startAt: 1, _game);
                    var resource = new ResourceKind.Cargo(cargo).DisplayName;
                    ShowInfo("Resupply",
                        $"{DisplayName(fleet)} will shuttle {amount} {resource} from {DisplayName(source)} " +
                        $"to {DisplayName(destination)} and return, {MyLord()}.");
                }))));

    // GameShell.PickOwnWorldOrRetry, narrowed to Planet (Resupply's own FleetOrderTemplates.Resupply
    // signature) -- re-prompts via retry instead of silently failing when the picked coordinate isn't
    // one of the player's own planets.
    private void PickOwnPlanetOrRetry(Coordinate location, string title, Action retry, Action<Planet> onOwnPlanet)
    {
        if (FindWorldAt(location) is Planet planet && ReferenceEquals(planet.Owner, _player))
        {
            onOwnPlanet(planet);
            return;
        }

        ShowInfo(title, "That isn't one of your own worlds.");
        retry();
    }

    // GameShell.FindAttackTarget (ATTCOMM.PAS's own GetTarget, :663-715), shared by Attack and Auto
    // Attack alike -- attacker and target must already be in the same sector (real Pascal's exact range
    // rule wasn't re-derived here, an MVP gate called out rather than silent, same cut tui1 made). Only
    // an enemy fleet is gated on Scouted; the world at that location (Planet/Starbase) is offered only
    // when no enemy fleet is present at all -- a world defended by any scouted enemy fleet can't be
    // attacked directly, the fleet(s) must be dealt with first (confirmed against tui1's own doc
    // comment on this exact ordering mistake and the wildly wrong result it produced once).
    private void FindAttackTarget(Fleet attacker, Action<ISectorObject> onTargetFound)
    {
        var location = attacker.Location;
        var enemyFleets = EnemyFleetsAt(location);

        if (enemyFleets.Count > 1)
        {
            _overlays.Add(new ObjectPickerOverlay(enemyFleets.Cast<ISectorObject>().ToList(), _player, onTargetFound));
            return;
        }

        ISectorObject? target = enemyFleets.Count == 1
            ? enemyFleets[0]
            : _objectsByLocation.TryGetValue(location, out var obj) && !ReferenceEquals(obj.Owner, _player) ? obj : null;

        if (target is null)
        {
            ShowInfo("Attack", "No enemy target in this sector.");
            return;
        }

        onTargetFound(target);
    }

    private List<Fleet> EnemyFleetsAt(Coordinate location) =>
        _fleetsByLocation[location].Where(f => !ReferenceEquals(f.Owner, _player) && Game.Scouted(_player, f)).ToList();

    // Per the user's own explicit request: the sector picker/Close Up's own Attack shortcut should only
    // be offered when this fleet actually has somewhere to point it -- same target-existence check
    // FindAttackTarget itself uses, without resolving (or picking, for 2+ enemy fleets) an actual one.
    private bool HasAttackTarget(Fleet attacker)
    {
        var location = attacker.Location;
        return EnemyFleetsAt(location).Count > 0 || (_objectsByLocation.TryGetValue(location, out var obj) && !ReferenceEquals(obj.Owner, _player));
    }

    private void Attack() => PickOwnFleetAtCursor("Attack", Attack);

    private void Attack(Fleet attacker) => FindAttackTarget(attacker, target => BeginAttack(attacker, target));

    // AttackCommand's own "Standard battle configuration (Y/n)?" fork (ATTCOMM.PAS:1608-1616): Y skips
    // Fleet Group Configuration and uses CombatEngine.DefaultDistribution; N opens the real
    // FleetGroupConfigurationOverlay. Esc abandons the attack entirely. A is a port-only addition (per
    // the user's own explicit request, carried over from tui1) that jumps straight to the same Auto
    // Attack confirmation Ministry of War's own menu item shows.
    private void BeginAttack(Fleet attacker, ISectorObject target)
    {
        _overlays.Add(new AttackConfigPromptOverlay(choice =>
        {
            switch (choice)
            {
                case 'Y':
                    StartEngagement(attacker, target, CombatEngine.DefaultDistribution(attacker));
                    return;
                case 'A':
                    BeginAutoAttack(attacker, target);
                    return;
                case 'N':
                    _overlays.Add(new FleetGroupConfigurationOverlay(attacker.Ships, attacker.Cargo,
                        groups => StartEngagement(attacker, target, groups)));
                    return;
            }
        }));
    }

    // AttackCommand's own IF NoOfGroups>0 (ATTCOMM.PAS:1619) -- zero groups skips the battle entirely,
    // matching real Pascal exactly rather than fighting an empty engagement.
    //
    // Real Pascal (and tui1) take the configured groups straight into InteractiveCombat.BeginEngagement
    // and a round-by-round Tactical Battle Display -- that display doesn't exist in Tui2 yet (its own
    // follow-on slice, the two battle scenarios the user asked for next). Until then this reports what
    // was configured rather than pretending to resolve a battle it can't actually run.
    private void StartEngagement(Fleet attacker, ISectorObject target, IReadOnlyList<GroupRecord> groups)
    {
        // AttackCommand's own IF NoOfGroups>0 (ATTCOMM.PAS:1619) -- zero groups skips the battle
        // entirely, matching real Pascal exactly rather than fighting an empty engagement.
        if (groups.Count == 0)
        {
            return;
        }

        var state = InteractiveCombat.BeginEngagement(_player, (IShipCargoHolder)target, [.. groups]);
        NextScreen = new TacticalBattleScreen(_game, _player, _context, attacker, target, state);
    }

    private void AutoAttack() => PickOwnFleetAtCursor("Auto Attack", AutoAttack);

    private void AutoAttack(Fleet attacker) => FindAttackTarget(attacker, target => BeginAutoAttack(attacker, target));

    // AutoAttackCommand (ATTCOMM.PAS:1640-1746): same target pick as Attack, but skips Fleet Group
    // Configuration/Tactical Battle Display entirely -- one confirm, then the whole engagement resolves
    // in a single call to CombatResolution.NPEAttack, the same headless engine the Kingdom AI itself
    // already uses (DefaultDistribution, hardcoded AttackIntentionType.Conquer -- Pascal's own hardcoded
    // ConquerAIT). No OldShipsFound/AskToCapture/scenario background text here: NPEAttack already runs
    // ResolveAttack internally with Capture hardcoded true, matching AutoAttackCommand's own plain
    // ResultMessage + CasualtyReport pair.
    private void BeginAutoAttack(Fleet attacker, ISectorObject target)
    {
        _overlays.Add(new ConfirmOverlay("Auto Attack", $"{DisplayName(attacker)} ready to attack {DisplayName(target)}.\nGive confirmation order?", yes =>
        {
            if (!yes)
            {
                return;
            }

            var before = new ShipCounts();
            foreach (var shipType in Enum.GetValues<ShipType>())
            {
                before[shipType] = attacker.Ships[shipType];
            }

            var engagement = CombatResolution.NPEAttack(_player, attacker, target, AttackIntentionType.Conquer, _game, _context.Random);
            Refresh();

            var casualties = Enum.GetValues<ShipType>().Select(t => $"{new ResourceKind.Ship(t).DisplayName}: {Math.Max(0, before[t] - attacker.Ships[t])}");
            var report = $"{AutoAttackResultText(engagement.Result, target)}\n\nCasualties:\n{string.Join('\n', casualties)}";
            ShowInfo("Auto Attack", report);
        }));
    }

    /// <summary>
    /// Ministry of War > Launch LAMs (DESIGN.PAS: LaunchLAM, :48-261). The launching world (BaseID)
    /// must be the player's own and actually have LAMs; targets are every fleet/planet/starbase the
    /// player knows about (<see cref="Game.Known"/>), isn't their own, and is within range 5 of the
    /// launching world -- same order/filter as <see cref="Reconstructed4021.Tui"/>'s own GameShell.
    /// </summary>
    private void LaunchLams()
    {
        var source = FindWorldAt(_cursor);
        if (source is null || !ReferenceEquals(source.Owner, _player) || source.Defenses[DefenseType.Lam] <= 0)
        {
            ShowInfo("Launch LAMs", "Move the cursor onto one of your own worlds with LAMs first.");
            return;
        }

        var baseLocation = source.Location;
        var targets = _game.Galaxy.Fleets.Cast<ISectorObject>()
            .Concat(_game.Galaxy.Planets.Cast<ISectorObject>())
            .Concat(_game.Galaxy.Starbases.Cast<ISectorObject>())
            .Where(o => Game.Known(_player, o) && !ReferenceEquals(o.Owner, _player) && baseLocation.DistanceTo(o.Location) <= 5)
            .ToList();

        if (targets.Count == 0)
        {
            ShowInfo("Launch LAMs", $"No targets can be reached from {DisplayName(source)}.");
            return;
        }

        _overlays.Add(new ObjectPickerOverlay(targets, _player, target => PromptForLamCount(source, target)));
    }

    // GetTarget's own InputIntegerDisplayScreen prompt (DESIGN.PAS:201-209): a positive number no
    // greater than the base's own LAM count, re-prompting (not closing) on an out-of-range answer --
    // same TextPromptOverlay-with-error-folded-into-the-label idiom as PromptForTrillum above.
    private void PromptForLamCount(IEconomicWorld source, ISectorObject target, string? error = null)
    {
        var maxLams = source.Defenses[DefenseType.Lam];
        var targetName = DisplayName(target);
        targetName = targetName.Length > 20 ? targetName[..20] : targetName;
        var label = error ?? $"LAMs to launch at {targetName} (max {maxLams}):";

        _overlays.Add(new TextPromptOverlay("Launch LAMs", label, string.Empty, text =>
        {
            if (!int.TryParse(text, out var amount) || amount < 0)
            {
                PromptForLamCount(source, target, $"You must use a positive number, {MyLord()}!");
                return;
            }

            if (amount > maxLams)
            {
                PromptForLamCount(source, target, $"There aren't that many LAMs at {DisplayName(source)}, {MyLord()}.");
                return;
            }

            FinishLaunchLams(source, target, amount);
        }));
    }

    // LaunchLAM's own tail (DESIGN.PAS:211-247): CombatStandalone.LAMAttack already applies the outcome
    // (news, fleet destruction, defense reduction) the same way NpeToolkit's own LAM strikes do -- only
    // the base's own LAM count and the casualty report are this command's responsibility.
    private void FinishLaunchLams(IEconomicWorld source, ISectorObject target, int lamsToUse)
    {
        var lines = new List<string>();
        if (lamsToUse > 0)
        {
            var (shipsDestroyed, defensesDestroyed) = CombatStandalone.LAMAttack(_player, lamsToUse, (IShipCargoHolder)target, _game);
            source.Defenses[DefenseType.Lam] -= lamsToUse;
            Refresh();

            if (target is Fleet)
            {
                lines.AddRange(Enum.GetValues<ShipType>().Where(t => shipsDestroyed[t] > 0).Select(t => $"{shipsDestroyed[t]} {new ResourceKind.Ship(t).DisplayName} were destroyed."));
            }
            else
            {
                lines.AddRange(Enum.GetValues<DefenseType>().Where(t => defensesDestroyed[t] > 0).Select(t => $"{defensesDestroyed[t]} {new ResourceKind.Defense(t).DisplayName} were destroyed."));
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(target is Fleet ? "No ships were destroyed." : "No defenses were destroyed.");
        }

        ShowInfo("Launch LAMs", string.Join('\n', lines));
    }

    /// <summary>Empire menu &gt; Send Message (DESIGN.PAS's SendMessageCommand).</summary>
    private void SendMessageCommand()
    {
        _overlays.Add(new EmpireMultiSelectOverlay(_game.Empires, chosen =>
            _overlays.Add(new MessageBodyOverlay(lines =>
                _overlays.Add(new ConfirmOverlay("Send Message", SendMessageConfirmText(chosen), yes =>
                {
                    if (!yes)
                    {
                        ShowInfo("Send Message", $"Message aborted, {MyLord()}.");
                        return;
                    }

                    MessageLifecycle.SendMessage(_game, _player, chosen, lines, _context.Random);
                    ShowInfo("Send Message", $"Message sent, {MyLord()}.");
                }))))));
    }

    private static string SendMessageConfirmText(IReadOnlySet<Empire> recipients) => recipients.Count == 1
        ? $"Ready to send message to {recipients.Single().Name}."
        : $"Ready to send messages to:\n{string.Join('\n', recipients.Select(e => e.Name))}";

    /// <summary>Empire menu &gt; Read Messages (DESIGN.PAS's ReadMessageCommand).</summary>
    private void ReadMessagesCommand()
    {
        var messages = _game.MessagesFor(_player).ToList();
        if (messages.Count == 0)
        {
            ShowInfo("Read Messages", $"You have not received any messages this year, {MyLord()}.");
            return;
        }

        _overlays.Add(new ReadMessagesOverlay(messages, _player, message => MessageLifecycle.MarkRead(_game, message, _player)));
    }

    // ResultMessage (ATTCOMM.PAS:1652-1686) -- only these three cases are ever reached (DefCapturedART
    // is declared but never assigned anywhere in real Pascal, see CombatOutcome.cs's own note).
    private string AutoAttackResultText(AttackResultType result, ISectorObject subject) => result switch
    {
        AttackResultType.AttackerDestroyed => $"I'm sorry, {MyLord()}, the entire attack force has been destroyed.",
        AttackResultType.AttackerRetreats => $"I'm sorry, {MyLord()}, the fleet was forced to retreat.",
        AttackResultType.DefenderConquered => subject is Fleet
            ? $"The enemy fleet has been destroyed, {MyLord()}."
            : Honorifics.SovereigntyDeclaration(_player, subject),
        _ => $"Result: {result}",
    };

    private string MyLord() => Honorifics.MyLord(_player.IsEmpress);

    private string DisplayName(ISectorObject obj) => CloseUpOverlay.DisplayName(obj, _player);

    // GameShell.ConfirmQuit: real Quit (PLAYTURN.PAS's XXXCom) only unwinds back to the main menu, not
    // a full process exit.
    private void ConfirmQuit() =>
        _overlays.Add(new ConfirmOverlay("Quit", "Are you sure you want to quit? You'll return to the main menu.",
            yes =>
            {
                if (yes)
                {
                    NextScreen = _context.MakeTitleScreen();
                }
            }));

    // GameShell.ConfirmExitToOs: no Pascal equivalent, a TUI-only convenience once Quit stopped exiting
    // the app outright.
    private void ConfirmExitToOs() =>
        _overlays.Add(new ConfirmOverlay("Exit to OS", "Are you sure you want to exit to the operating system?",
            yes =>
            {
                if (yes)
                {
                    NextScreen = QuitScreen.Instance;
                }
            }));

    // Fleet menu > Probe (FLTCOMM.PAS: LaunchProbeCommand, :761-786): unlike every other Fleet-menu
    // command, real Pascal never ties this to a specific fleet -- just a destination coordinate,
    // reusing the map cursor the same way Deploy's own destination pick does, with no source-fleet
    // step first.
    private void LaunchProbe()
    {
        if (_player.ProbesInTransit.Count >= Empire.MaxProbesInTransit)
        {
            ShowInfo("Probe", "There are no more probes available.");
            return;
        }

        BeginPick("Launch Probe -- move cursor to target, Enter: select, Esc: cancel", destination =>
        {
            var probeNumber = _player.ProbesInTransit.Count + 1;
            _player.TryLaunchProbe(destination);
            ShowInfo("Probe", $"Probe {probeNumber} of {Empire.MaxProbesInTransit} sent.");
        });
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

    /// <summary>Build menu &gt; New (CONSTR.PAS: ConstructCommand).</summary>
    private void NewConstruction()
    {
        var available = Enum.GetValues<ConstructionType>().Where(t => _player.Technology.Constructions.Contains(t)).ToList();
        if (available.Count == 0)
        {
            ShowInfo("Construction", $"{MyLord()}, you don't have the technology to build anything!");
            return;
        }

        _overlays.Add(new ConstructionTypePickerOverlay(available, PickConstructionCoordinate));
    }

    private void PickConstructionCoordinate(ConstructionType type) =>
        BeginPick("Construction -- move cursor to begin construction, Enter: select, Esc: cancel", coordinate =>
        {
            if (_game.Galaxy.GetObjectAt(coordinate) is not null)
            {
                ShowInfo("Construction", $"{MyLord()}, that sector is already occupied.");
                PickConstructionCoordinate(type);
                return;
            }

            var site = ConstructionLifecycle.StartConstruction(_game, _player, type, coordinate);
            Refresh();

            var years = ConstructionCatalog.YearsToBuild[type];
            var cost = ConstructionCatalog.RawMaterialPerYear[type]
                .Where(kv => kv.Value > 0)
                .Select(kv => $"{kv.Value} {new ResourceKind.Cargo(kv.Key).DisplayName} per year.");

            ShowInfo("Construction",
                $"Starting construction of {Noun(ConstructionCatalog.DisplayName(type))} at {DisplayName(site)}.\n\n" +
                $"Construction will take approximately {years} years to finish and will\n" +
                "require the following quantities of raw material:\n" +
                string.Join('\n', cost));
        });

    // Noun (STRG.PAS:86-92) -- 'a'/'an' by leading letter, Y counted as a vowel like Pascal's own does.
    private static string Noun(string word) => "AEIOUY".Contains(char.ToUpperInvariant(word[0])) ? $"an {word}" : $"a {word}";

    /// <summary>Build menu &gt; Abort (CONSTR.PAS: AbortConstructionCommand).</summary>
    private void AbortConstruction()
    {
        var site = _game.Galaxy.ConstructionSites.FirstOrDefault(c => c.Location == _cursor && ReferenceEquals(c.Owner, _player));
        if (site is null)
        {
            ShowInfo("Abort Construction", "Move the cursor onto one of your own construction sites first.");
            return;
        }

        var name = DisplayName(site);
        _overlays.Add(new ConfirmOverlay("Abort Construction", $"Are you sure you want to abort {name}?", yes =>
        {
            if (!yes)
            {
                return;
            }

            _game.Galaxy.ConstructionSites.Remove(site);
            Refresh();
            var capitalized = char.ToUpperInvariant(name[0]) + name[1..];
            ShowInfo("Abort Construction", $"{capitalized} aborted, {MyLord()}.");
        }));
    }

    // NAMES.PAS: AddNameCommand/DeleteNameCommand, generalized to whatever's at the cursor rather than
    // a world specifically -- real Pascal lets a player name anything they can see, not just their own
    // worlds, so this resolves via ObjectsAt (the same Game.Visible gate Close Up itself uses) rather
    // than an own-worlds-only lookup, with the same picker-on-2+ ExamineCursor already uses.
    private void ResolveNameTarget(string title, Action<ISectorObject> onResolved)
    {
        var objects = ObjectsAt(_cursor);
        switch (objects.Count)
        {
            case 0:
                ShowInfo(title, "Move the cursor onto something you can see first.");
                break;
            case 1:
                onResolved(objects[0]);
                break;
            default:
                _overlays.Add(new ObjectPickerOverlay(objects, _player, onResolved));
                break;
        }
    }

    private void AddNameCommand() => ResolveNameTarget("Add Name", obj =>
        _overlays.Add(new TextPromptOverlay("Name", "New name (blank to clear):", obj.Names.GetValueOrDefault(_player, string.Empty), newName =>
        {
            if (newName.Length == 0)
            {
                obj.Names.Remove(_player);
            }
            else
            {
                obj.Names[_player] = newName;
            }

            Refresh();
        })));

    private void DeleteNameCommand() => ResolveNameTarget("Delete Name", obj =>
    {
        if (!obj.Names.Remove(_player))
        {
            ShowInfo("Delete Name", "That hasn't been named.");
            return;
        }

        Refresh();
    });

    // GameShell.EndTurn: PLAYTURN.PAS's own command loop runs entirely before UpdateTurn is called --
    // TurnEngine.BeginTurn (fog-of-war refresh) already ran before this screen was even shown, so
    // ending a turn here only needs TurnEngine.EndTurn's own half: erase news, move fleets, and hand
    // the game off to the next empire. TurnLoop.Start then decides what (if anything) runs next.
    private void EndTurn()
    {
        _context.TurnEngine.EndTurn(_game);
        AutoSave();
        NextScreen = TurnLoop.Start(_game, _context);
    }

    private const int MaxAutoSaves = 10;

    // GameShell.AutoSave: best-effort and silent on failure, unlike a *manual* save's own error popup --
    // an autosave failing mid-turn shouldn't interrupt play. Overwrites this same turn's own file if
    // called twice (no AvoidCollision): the suggested name is deterministic ({player}-{year}), and
    // AutoSave only ever runs once per this empire's own turn anyway, so there's nothing to collide
    // with -- collision-avoiding it would just accumulate a new file per turn instead of ever replacing
    // the prior one, which is what actually happened when this was first written with AvoidCollision.
    private void AutoSave()
    {
        var autoDir = Path.Combine(_context.RepoRoot, "saves", "auto");
        Directory.CreateDirectory(autoDir);

        var path = Path.Combine(autoDir, $"{_player.Name}-{_game.Year}.json");
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, GameJson.Serialize(_game, _context.NpeProvider));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (IOException)
        {
            // Best-effort -- don't interrupt play over this.
        }

        var stale = new DirectoryInfo(autoDir).GetFiles("*.json")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxAutoSaves);
        foreach (var file in stale)
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // Best-effort pruning -- same "don't interrupt play over this" reasoning as the save itself.
            }
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
        _menuBar.Draw(fb, menuRow, MenuBarFg, MenuBarBg, MenuHotColor, DropdownFg, DropdownBg, MenuSelectedFg, MenuSelectedBg);
        DrawStatusLine(fb, statusRow);

        foreach (var overlay in _overlays)
        {
            overlay.Draw(fb);
        }

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
        var width = Math.Min(50, fb.Width);
        var height = Math.Min(4, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + 1, y + 1, "Filename to save to:", ConsoleColor.White, ConsoleColor.Black);
        _savePrompt!.Draw(fb, x + 1, y + 2, Math.Max(0, width - 2), TextInputField.DefaultFg, TextInputField.DefaultBg);
    }

    private const string ContinueHint = "Press any key to continue...";

    private void DrawInfoPopup(FrameBuffer fb)
    {
        var lines = _infoMessage!.Split('\n');
        // Every other overlay in this project clamps its frame to fb.Width/fb.Height before centering
        // -- this one (and DrawSavePrompt above) predate that convention and didn't, so a long message
        // (or a narrow terminal) could compute a negative x and write clipped/garbled columns instead
        // of just a smaller box. Same Math.Min/Math.Max(0, ...) clamp as everywhere else now.
        //
        // ContinueHint's own length has to factor into width too -- it used to be sized only off the
        // message/title, so a short message (e.g. "Game saved to a.json.") produced a box too narrow
        // for the fixed hint string, which then overran the right border: the hint is drawn at x+2 (one
        // column deeper than the box's x+1 interior), so the safe text length from there is width-3, not
        // width-2 -- the old clip used width-2, letting the hint's last character land on the border.
        var width = Math.Min(Math.Max(Math.Max(lines.Max(l => l.Length), _infoTitle!.Length + 2), ContinueHint.Length) + 4, fb.Width);
        var height = Math.Min(lines.Length + 5, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = $" {_infoTitle} ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);
        for (var i = 0; i < lines.Length && i + 2 < height; i++)
        {
            fb.DrawText(x + 2, y + 2 + i, lines[i], ConsoleColor.White, ConsoleColor.Black, maxWidth: width - 3);
        }

        fb.DrawText(x + 2, y + height - 2, ContinueHint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 3);
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
        // A pending pick's own prompt replaces the F-key legend entirely while it's active -- matching
        // GameShell's own pickerPromptLabel taking over the same screen real estate.
        var leftText = _pendingPick?.Prompt
            ?? "F1:Help  F3:Status  F5:Fleet  F7:News  F8:Empire  F9:Names";
        fb.DrawText(1, row, leftText, HelpLineFg, HelpLineBg);

        var coordinateText = RelativeCoordinate.Format(_cursor, _origin);
        fb.DrawText(Math.Max(0, fb.Width - coordinateText.Length - 1), row, coordinateText, HelpLineFg, HelpLineBg);
    }

}
