using System.Collections.ObjectModel;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Top-level shell (docs/TUI_SURFACES_MAPPING.md's "Deliberate deviation" section): unlike the
/// original DOS game, where the galaxy map was just one of several swappable F-key panels, here the map
/// is the permanent base view -- everything else (menu, status bar, overlay windows) sits on top
/// of it, never replaces it. Menu/status bar leaf items are stubbed to a "not yet implemented" MessageBox;
/// each gets a real implementation as its own surface is built out.
///
/// One human's one turn, not the whole play session: <see cref="Program"/>'s own loop owns cycling
/// through every empire's turn (human or NPE alike), constructing a fresh <see cref="GameShell"/>
/// only when a human empire comes up. Ending this turn (<see cref="EndTurn"/>) advances
/// <paramref name="game"/> by exactly one empire-turn and hands control back to that loop --
/// nothing here loops across multiple empires itself.
/// </summary>
internal sealed class GameShell : Window
{
    // COLORS.INC's ColorScrColor (the color-mode palette; BW/Mono variants aren't relevant here). DOS
    // attribute byte = (bg &lt;&lt; 4) | fg, decoded against the standard 16-color CGA/EGA palette.
    private static readonly TgAttribute MenuBarAttribute = new(StandardColor.White, DosColors.Red); // SYSMenuBar = 79
    private static readonly TgAttribute DropdownAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSMenu = 7
    private static readonly TgAttribute HelpLineAttribute = new(DosColors.Red, StandardColor.Black); // SYSHelpLine = 4

    private readonly Game game;
    private readonly TurnEngine turnEngine;
    private readonly Empire human;
    private readonly Random random;
    private readonly GalaxyView galaxyView;
    private readonly Label pickerPromptLabel;

    // Set while a command (Deploy, Attack) is waiting for the player to move the map cursor onto a
    // target sector and confirm -- the map-cursor-reuse pattern TUI_SURFACES_MAPPING.md calls for
    // ("map cursor reuse for launch/destination") in place of a separate coordinate-entry dialog.
    private Action<Coordinate>? pendingPick;

    public GameShell(Game game, TurnEngine turnEngine, Empire human, Random random)
    {
        this.game = game;
        this.turnEngine = turnEngine;
        this.human = human;
        this.random = random;

        Title = $"Anacreon -- {game.Galaxy.Size}x{game.Galaxy.Size} galaxy";
        Width = Dim.Fill();
        Height = Dim.Fill();

        // No window chrome -- the menu bar/status bar/map fill the whole screen edge to edge, same as
        // the original's full-screen text display (there was never a bordered "app window" to draw).
        BorderStyle = LineStyle.None;
        Border.Thickness = new Thickness(0);

        galaxyView = new GalaxyView(game.Galaxy, human) {
            X = 0,
            Y = 1, // below the menu bar
            Width = Dim.Fill(),
            Height = Dim.Fill(1), // leave the bottom row for the status bar
        };

        var menuItems = BuildMenus();
        var menuBar = new MenuBar { Menus = menuItems };
        menuBar.SetScheme(new Scheme(MenuBarAttribute));

        // Each top-level item's dropdown is a separate Menu (either a PopoverMenu or an inline SubMenu,
        // depending on Terminal.Gui's internal choice) that doesn't inherit the bar's own Scheme, hence
        // coloring it separately here to match SYSMenu.
        foreach (var item in menuItems) {
            item.PopoverMenu?.Root?.SetScheme(new Scheme(DropdownAttribute));
            item.SubMenu?.SetScheme(new Scheme(DropdownAttribute));
        }

        var statusBar = BuildStatusBar();
        statusBar.SetScheme(new Scheme(HelpLineAttribute));

        // Bottom-right coordinate readout (MAPWIND.PAS's DrawMapCursor/CoordLine): its own Label rather
        // than a StatusBar Shortcut, since it needs to live-update from GalaxyView and isn't a command.
        var coordinateLabel = new Label {
            X = Pos.AnchorEnd(),
            Y = Pos.AnchorEnd(),
            CanFocus = false,
            Text = galaxyView.CursorCoordinateText,
        };
        coordinateLabel.SetScheme(new Scheme(HelpLineAttribute));
        galaxyView.CursorCoordinateChanged += (_, text) => coordinateLabel.Text = text;

        pickerPromptLabel = new Label { X = 1, Y = Pos.AnchorEnd(2), Visible = false };
        pickerPromptLabel.SetScheme(new Scheme(HelpLineAttribute));

        Add(galaxyView);
        Add(menuBar);
        Add(statusBar);
        Add(coordinateLabel);
        Add(pickerPromptLabel);

        // The galaxy map is the permanent shell (see docs/TUI_SURFACES_MAPPING.md), not one of several
        // swappable panels, so it gets initial focus, not the menu bar.
        galaxyView.SetFocus();

        // Esc toggles focus to the menu bar rather than quitting outright (Quit lives behind Game > Quit,
        // with its own confirm) -- simulating the Game menu's own Alt+G hotkey (rather than the generic
        // F10 activation key) so it opens Game specifically instead of MenuBar's default of "whichever
        // item is first," which would be the leftmost ⌂ entry. Reaching into the menu bar's internal
        // Active/focus state directly isn't possible from outside the library, hence going through the
        // same HotKey path a real keypress would. The reverse direction (Esc while a menu is open) is the
        // menu bar's own built-in behavior: it closes the open dropdown and deactivates itself, so this
        // handler never has to run for that case.
        KeyDown += (_, key) => {
            if (pendingPick is not null) {
                if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Enter) {
                    ActivateCursor();
                    key.Handled = true;
                } else if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                    EndPick();
                    key.Handled = true;
                }
                // Anything else (arrows, PageUp/Down, Home/End) falls through unhandled so
                // GalaxyView's own KeyDown still moves the cursor while a pick is pending.
                return;
            }

            if (galaxyView.HasFocus && key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Enter) {
                ActivateCursor();
                key.Handled = true;
                return;
            }

            if (key.KeyCode != KeyCode.Esc || galaxyView.HasFocus == false) {
                return;
            }

            menuBar.NewKeyDownEvent(Key.G.WithAlt);
            key.Handled = true;
        };

        // A click behaves like Enter on the same sector, per the user's own explicit request --
        // routed through the exact same ActivateCursor the keyboard path uses, so the two can never
        // drift apart (pending-pick confirm during Deploy/Attack destination selection, Close Up
        // otherwise).
        galaxyView.SectorActivated += (_, _) => ActivateCursor();
    }

    /// <summary>Enter (keyboard) or a plain click (mouse, via <see cref="GalaxyView.SectorActivated"/>) on the current cursor sector: confirms a pending coordinate pick if one's active, otherwise examines whatever's there.</summary>
    private void ActivateCursor()
    {
        if (pendingPick is { } pick) {
            EndPick();
            pick(galaxyView.CursorLocation);
        } else {
            ExamineCursor();
        }
    }

    /// <summary>
    /// Puts the shell into "move the cursor and confirm" mode for one coordinate pick -- Deploy's
    /// destination, or (a future command's) target sector. Only one pick can be pending at a time;
    /// nothing here needs more than that yet.
    /// </summary>
    private void BeginPick(string prompt, Action<Coordinate> onConfirm)
    {
        pickerPromptLabel.Text = prompt;
        pickerPromptLabel.Visible = true;
        pendingPick = onConfirm;
        galaxyView.SetFocus();
    }

    private void EndPick()
    {
        pendingPick = null;
        pickerPromptLabel.Visible = false;
    }

    /// <summary>Which way this window's Run ended -- <see cref="ExitChoice.None"/> if it's still showing.</summary>
    public enum ExitChoice { None, EndTurn, MainMenu, ExitToOs }

    public ExitChoice Choice { get; private set; }

    /// <summary>
    /// PLAYTURN.PAS's own command loop runs entirely before UpdateTurn is called -- so ending a turn
    /// here means advancing <see cref="game"/> by exactly this one empire's turn and handing control
    /// back to <see cref="Program"/>'s own loop, which decides what (if anything) to show next.
    /// </summary>
    private void EndTurn()
    {
        turnEngine.AdvanceOneTurn(game);
        Choice = ExitChoice.EndTurn;
        App?.RequestStop();
    }

    private void ConfirmQuit()
    {
        // PLAYTURN.PAS:1075/1178 (XXXCom) -- Quit here just sets ExitGame:=True, which unwinds the
        // per-turn loop back to ANACREON.PAS's outer REPEAT, landing back on Prologue (the main menu),
        // not a full process exit. MessageBox's last button is the default (focused) one -- "No" here,
        // so a stray Enter doesn't quit. The leading underscores give Y/N as hotkeys too (same
        // HotKeyBindings mechanism as the menu items below -- both the bare key and Alt+key are bound,
        // and it works regardless of which button currently has focus).
        if (MessageBox.Query(App!, "Quit", "Are you sure you want to quit? You'll return to the main menu.", "_Yes", "_No") == 0) {
            Choice = ExitChoice.MainMenu;
            App?.RequestStop();
        }
    }

    // No Pascal equivalent -- real Quit (PLAYTURN.PAS's XXXCom) only ever returns to the main menu; a
    // full exit is a separate command on Prologue's own menu bar, one screen further back. Added here as
    // a TUI-only convenience once Quit stopped exiting the app outright, so leaving the game still has a
    // one-step way out instead of forcing a trip back through the main menu first.
    private void ConfirmExitToOs()
    {
        if (MessageBox.Query(App!, "Exit to OS", "Are you sure you want to exit to the operating system?", "_Yes", "_No") == 0) {
            Choice = ExitChoice.ExitToOs;
            App?.RequestStop();
        }
    }

    private void Stub(string label) => MessageBox.Query(App!, label, "Not yet implemented.", "OK");

    private IEconomicWorld? FindWorldAt(Coordinate location) =>
        (IEconomicWorld?)game.Galaxy.Planets.FirstOrDefault(p => p.Location == location)
        ?? game.Galaxy.Starbases.FirstOrDefault(s => s.Location == location);

    private List<ISectorObject> ObjectsAt(Coordinate location)
    {
        var result = new List<ISectorObject>();
        result.AddRange(game.Galaxy.Planets.Where(p => p.Location == location));
        result.AddRange(game.Galaxy.Starbases.Where(s => s.Location == location));
        result.AddRange(game.Galaxy.Fleets.Where(f => f.Location == location));
        result.AddRange(game.Galaxy.Stargates.Where(g => g.Location == location));
        result.AddRange(game.Galaxy.ConstructionSites.Where(c => c.Location == location));
        return result;
    }

    /// <summary>
    /// Enter on the map, or Worlds menu > Close Up (MAPWIND.PAS: GetMapObject/SelectPoint feeding
    /// PLAYTURN.PAS's InfoCom/CLSCOMM.PAS's CloseUpCom): a single object at the cursor opens
    /// <see cref="CloseUpWindow"/> directly; 2+ opens a picker first (TUI_SURFACES_MAPPING.md's
    /// "Sector Selected Popup"); none is a silent no-op, matching real Pascal's own behavior when
    /// Enter finds nothing there.
    /// </summary>
    private void ExamineCursor()
    {
        var objects = ObjectsAt(galaxyView.CursorLocation);
        switch (objects.Count) {
            case 0: return;
            case 1: ShowCloseUp(objects[0]); break;
            default: ShowSectorPicker(objects); break;
        }
    }

    // Both overlays below are added/removed directly as children of this running Toplevel rather
    // than run via a nested Application.Run -- see CloseUpWindow's own doc comment.
    private void ShowCloseUp(ISectorObject obj)
    {
        var window = new CloseUpWindow(obj, human, game);
        var dismiss = AddModal(window);
        window.KeyDown += (_, key) => {
            dismiss();
            key.Handled = true;
        };
    }

    private void ShowSectorPicker(List<ISectorObject> objects)
    {
        var picker = new FrameView {
            Title = "Select an Object",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 40,
            Height = objects.Count + 4,
        };

        var listView = new ListView<ObjectListItem> { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        listView.SetSource(new ObservableCollection<ObjectListItem>(objects.Select(o => new ObjectListItem(o))));
        listView.Index = 0; // SetSource alone leaves nothing selected -- default to the first item.
        picker.Add(listView);
        picker.Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Enter: choose   Esc: cancel" });

        var dismiss = AddModal(picker);
        picker.KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.Enter:
                    var chosen = listView.Value;
                    dismiss();
                    if (chosen is not null) {
                        ShowCloseUp(chosen.Object);
                    }
                    key.Handled = true;
                    break;
                case KeyCode.Esc:
                    dismiss();
                    key.Handled = true;
                    break;
            }
        };
    }

    /// <summary>
    /// Makes <paramref name="popup"/> a real modal overlay, per the user's own explicit request:
    /// added on top of a full-screen transparent backdrop that swallows every click outside the
    /// popup's own bounds and dismisses it (the popup itself, being added after and thus on top,
    /// still gets first crack at any click landing on its own area). Returns the dismiss action so
    /// the caller can also trigger it from its own key handling (Enter/Esc/any-key, whichever fits
    /// that popup). Both overlays this shell uses are added/removed directly as children of this
    /// running Toplevel rather than run via a nested Application.Run -- see CloseUpWindow's own doc
    /// comment for why.
    /// </summary>
    private Action AddModal(View popup)
    {
        var backdrop = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = false };
        // ViewportSettingsFlags.Transparent is Terminal.Gui's own documented mechanism for this
        // ("the Viewport will not be cleared when the View is drawn") -- canceling DrawingContent
        // (an earlier attempt) only skips this view's own custom draw, not the base ClearViewport
        // pass that runs ahead of it, so galaxyView's own pixels underneath were still getting wiped
        // to a solid fill first. Deliberately not combined with TransparentMouse: the backdrop's
        // whole job is catching clicks outside the popup, so it must stay opaque to the mouse.
        backdrop.ViewportSettings = ViewportSettingsFlags.Transparent;

        void Dismiss()
        {
            Remove(popup);
            Remove(backdrop);
            galaxyView.SetFocus();
        }

        backdrop.MouseEvent += (_, mouse) => {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)) {
                Dismiss();
                mouse.Handled = true;
            }
        };

        Add(backdrop);
        Add(popup);
        popup.SetFocus();

        return Dismiss;
    }

    /// <summary>Display wrapper for <see cref="ShowSectorPicker"/>'s ListView -- ISectorObject implementors are plain domain entities with no display-formatting concern of their own.</summary>
    private sealed record ObjectListItem(ISectorObject Object)
    {
        public override string ToString() => CloseUpWindow.DescribeKind(Object);
    }

    /// <summary>
    /// Fleet menu > Deploy (FLTCOMM.PAS: LaunchFleetCommand). MVP simplification, called out rather
    /// than silent: deploys the entire current Ships of the world under the cursor, no cargo, no
    /// fleet name -- the Resource Distribution Editor and a name prompt are real UI
    /// TUI_SURFACES_MAPPING.md's own "Suggested build order" defers past this branch's actual job
    /// (proving the combat loop end to end). Destination reuses the map cursor, matching that same
    /// doc's prescribed primitive for this command.
    /// </summary>
    private void DeployFleet()
    {
        var source = FindWorldAt(galaxyView.CursorLocation);
        if (source is null || !ReferenceEquals(source.Owner, human)) {
            MessageBox.Query(App!, "Deploy Fleet", "Move the cursor onto one of your own worlds first.", "OK");
            return;
        }

        var s = source.Ships;
        if (s.Fighters + s.HunterKillers + s.Jumpships + s.Jumptransports + s.Penetrators + s.Starships + s.Transports == 0) {
            MessageBox.Query(App!, "Deploy Fleet", "This world has no ships to deploy.", "OK");
            return;
        }

        // A snapshot, not source.Ships itself: FleetLifecycle.ChangeCompositionOfFleet (which
        // DeployFleet calls internally) overwrites the launch world's own Ships in place before
        // copying the "new fleet" composition onto the fleet -- passing the live object here aliases
        // the two, so the fleet would end up copying its own just-zeroed source (confirmed by hitting
        // this exact bug: the fleet was created with every ship count at 0). Every real Core caller
        // avoids this by building composition off GetFleetComposition, never a world's own live Ships.
        var deployedShips = new ShipCounts {
            Fighters = s.Fighters, HunterKillers = s.HunterKillers, Jumpships = s.Jumpships,
            Jumptransports = s.Jumptransports, Penetrators = s.Penetrators, Starships = s.Starships, Transports = s.Transports,
        };

        BeginPick("Deploy Fleet -- move cursor to destination, Enter: launch, Esc: cancel", destination => {
            FleetLifecycle.DeployFleet(human, source, deployedShips, new CargoHold(), destination, game);
            galaxyView.Refresh();
        });
    }

    /// <summary>
    /// Ministry of War menu > Attack (ATTCOMM.PAS: GetTarget/AttackCommand). Target selection order
    /// is transcribed directly from GetTarget's own nested CreateMenu (ATTCOMM.PAS:663-715): every
    /// enemy Fleet in the sector goes on the target list first, and the world itself (Planet/
    /// Starbase) is only ever offered "if no fleets" (ListSize=0) -- a world defended by any enemy
    /// fleet cannot be attacked directly, the fleet(s) must be dealt with first. Confirmed with the
    /// user after an initial pass got this backwards (checked the world before any defending fleet)
    /// and produced a wildly wrong result: an undamaged 50-ship Kingdom defense fleet sat next to a
    /// heavily-refortified capital, and attacking the capital directly wiped out a 3000-ship human
    /// fleet against a world whose own war economy had ballooned during the several turns the attack
    /// took to arrive -- attacking the (comparatively tiny) defending fleet first is what real Pascal
    /// would have forced instead. MVP gate, called out rather than silent: attacker and target must
    /// already be in the same sector (i.e. the fleet has arrived) -- real Pascal's exact range rule
    /// wasn't re-derived here. No target picker if more than one enemy fleet occupies the sector
    /// (whichever is found first wins) -- TUI_SURFACES_MAPPING.md calls for a ListView/Dialog there,
    /// matching GetTarget's own menu for that case, but nothing this branch's own fixture produces
    /// ever hits it. Auto-resolved through CombatResolution.NPEAttack -- the same entry point
    /// Kingdom's own AI calls -- with AttackIntentionType.Conquer and CombatEngine's own default
    /// distribution/grouping standing in for the deferred Fleet Group Configuration/Tactical Battle
    /// Display screens.
    /// </summary>
    private void Attack()
    {
        var cursor = galaxyView.CursorLocation;
        var attacker = game.Galaxy.Fleets.FirstOrDefault(f => f.Location == cursor && ReferenceEquals(f.Owner, human));
        if (attacker is null) {
            MessageBox.Query(App!, "Attack", "Move the cursor onto one of your own fleets first.", "OK");
            return;
        }

        object? target = game.Galaxy.Fleets.FirstOrDefault(f => f.Location == cursor && !ReferenceEquals(f.Owner, human))
            ?? (object?)game.Galaxy.Planets.FirstOrDefault(p => p.Location == cursor && !ReferenceEquals(p.Owner, human))
            ?? game.Galaxy.Starbases.FirstOrDefault(s => s.Location == cursor && !ReferenceEquals(s.Owner, human));

        if (target is null) {
            MessageBox.Query(App!, "Attack", "No enemy target in this sector.", "OK");
            return;
        }

        var result = CombatResolution.NPEAttack(human, attacker, target, AttackIntentionType.Conquer, game, random);
        galaxyView.Refresh();
        MessageBox.Query(App!, "Attack", $"Result: {result.Result}", "OK");
    }

    private MenuBarItem[] BuildMenus() => [
        new MenuBarItem("⌂", new MenuItem[] {
            new("_About Anacreon", Key.Empty, () => Stub("About Anacreon")),
        }),
        new MenuBarItem("_Game", new MenuItem[] {
            new("_Pause", Key.Empty, () => MessageBox.Query(App!, "Paused", "Time has stopped. Press OK to continue.", "OK")),
            new("_Status Hardcopy", Key.Empty, () => Stub("Status Hardcopy")),
            new("_Next Turn", Key.Empty, EndTurn),
            new("_Quit", Key.Empty, ConfirmQuit),
            new("E_xit to OS", Key.Empty, ConfirmExitToOs),
        }),
        new MenuBarItem("_Empire", new MenuItem[] {
            new("_Send Message", Key.Empty, () => Stub("Send Message")),
            new("_Read Messages", Key.Empty, () => Stub("Read Messages")),
            new("_Trade Technology", Key.Empty, () => Stub("Trade Technology")),
        }),
        new MenuBarItem("_Worlds", new MenuItem[] {
            new("_Close Up", Key.Empty, ExamineCursor),
            new("_Designate", Key.Empty, () => Stub("Designate")),
            new("P_roduction", Key.Empty, () => Stub("Production")),
            new("_ISSP", Key.Empty, () => Stub("ISSP")),
            new("_Add Name", Key.Empty, () => Stub("Add Name")),
            new("Delete _Name", Key.Empty, () => Stub("Delete Name")),
            new("_Liberate", Key.Empty, () => Stub("Liberate")),
            new("_Self-Destruct", Key.Empty, () => Stub("Self-Destruct")),
        }),
        new MenuBarItem("_Fleet", new MenuItem[] {
            new("_Deploy", Key.Empty, DeployFleet),
            new("_Change Destination", Key.Empty, () => Stub("Change Destination")),
            new("_Transfer", Key.Empty, () => Stub("Transfer Fleet")),
            new("_Abort/Join", Key.Empty, () => Stub("Abort/Join Fleet")),
            new("_Refuel", Key.Empty, () => Stub("Refuel Fleet")),
            new("_SRM Sweep", Key.Empty, () => Stub("Mine Sweeper")),
            new("_Orders", Key.Empty, () => Stub("Fleet Orders")),
            new("Canc_el Orders", Key.Empty, () => Stub("Cancel Orders")),
            new("_Probe", Key.Empty, () => Stub("Launch Probe")),
        }),
        new MenuBarItem("_Build", new MenuItem[] {
            new("_Site Status", Key.Empty, () => Stub("Construction Site Status")),
            new("_New", Key.Empty, () => Stub("New Construction Site")),
            new("_Abort", Key.Empty, () => Stub("Abort Construction")),
        }),
        new MenuBarItem("_Ministry of War", new MenuItem[] {
            new("_Attack", Key.Empty, Attack),
            new("Auto A_ttack", Key.Empty, () => Stub("Auto Attack")),
            new("Launch _LAMs", Key.Empty, () => Stub("Launch LAMs")),
            new("_Defenses", Key.Empty, () => Stub("Defenses")),
        }),
    ];

    private StatusBar BuildStatusBar() => new([
        new Shortcut(Key.F1, "Help", () => Stub("Help"), ""),
        new Shortcut(Key.F3, "Status", () => Stub("Status"), ""),
        new Shortcut(Key.F5, "Fleet", () => Stub("Fleet"), ""),
        new Shortcut(Key.F7, "News", () => Stub("News"), ""),
        new Shortcut(Key.F8, "Empire", () => Stub("Empire"), ""),
        new Shortcut(Key.F9, "Names", () => Stub("Names"), ""),
    ]);
}
