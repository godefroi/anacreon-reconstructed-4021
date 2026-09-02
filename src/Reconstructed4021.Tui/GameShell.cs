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
/// only when a human empire comes up -- and already ran that empire's <see cref="TurnEngine.BeginTurn"/>
/// (fog-of-war refresh) before doing so. Ending this turn (<see cref="EndTurn"/>) runs the other half,
/// <see cref="TurnEngine.EndTurn"/>, and hands control back to that loop -- nothing here loops across
/// multiple empires itself.
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
    private readonly MenuBar menuBar;
    private int openModalCount;

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
        menuBar = new MenuBar { Menus = menuItems };
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
    /// PLAYTURN.PAS's own command loop runs entirely before UpdateTurn is called -- <see cref="Program"/>
    /// already ran this empire's <see cref="TurnEngine.BeginTurn"/> (fog-of-war refresh) before this
    /// shell was even shown, so ending a turn here only needs <see cref="TurnEngine.EndTurn"/>'s own
    /// half: erase news, move fleets, and hand <see cref="game"/> off to the next empire. Control then
    /// returns to <see cref="Program"/>'s own loop, which decides what (if anything) to show next.
    /// </summary>
    private void EndTurn()
    {
        turnEngine.EndTurn(game);
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

    /// <summary>
    /// Resolves "the player's own fleet under the cursor" for every Fleet-menu command below plus
    /// Attack. Real Pascal disambiguates multiple fleets in one sector by having the player type the
    /// target fleet's own name (PLAYTURN.PAS's GetParameters/InterpretObj); this port's map-cursor-
    /// driven UI has no typed-command layer to reuse for that, so instead this reuses ExamineCursor's
    /// own "exactly one auto-picks, 2+ opens a picker" rule (MAPWIND.PAS's GetMapObject), scoped down
    /// to just the player's own fleets at that sector since none of these commands can ever target
    /// someone else's fleet directly. Confirmed necessary, not hypothetical: nothing stops two of the
    /// player's own fleets from sharing a sector (e.g. Transfer between them).
    /// </summary>
    private void PickOwnFleetAtCursor(string title, Action<Fleet> onChosen)
    {
        var fleets = game.Galaxy.Fleets.Where(f => f.Location == galaxyView.CursorLocation && ReferenceEquals(f.Owner, human)).ToList();
        switch (fleets.Count) {
            case 0:
                MessageBox.Query(App!, title, "Move the cursor onto one of your own fleets first.", "OK");
                break;
            case 1:
                onChosen(fleets[0]);
                break;
            default:
                ShowObjectPicker(title, fleets.Cast<ISectorObject>().ToList(), o => onChosen((Fleet)o));
                break;
        }
    }

    /// <summary>
    /// Every object at <paramref name="location"/> the human can actually see (<see cref="Game.Visible"/>)
    /// -- MAPWIND.PAS's own map cursor (GetMapObject) only ever finds <c>Sector[x]^[y].Obj</c>, a single
    /// slot gated the same Known-first way <see cref="GalaxyView"/>'s own world glyph is; fleets need
    /// the identical filter here too, mirroring MAPWIND.PAS's EnemyFleetInSector. Feeds Close Up/the
    /// sector picker (<see cref="ExamineCursor"/>), so nothing the map itself would hide from the player
    /// can be picked or inspected via this path.
    /// </summary>
    private List<ISectorObject> ObjectsAt(Coordinate location)
    {
        var result = new List<ISectorObject>();
        result.AddRange(game.Galaxy.Planets.Where(p => p.Location == location));
        result.AddRange(game.Galaxy.Starbases.Where(s => s.Location == location));
        result.AddRange(game.Galaxy.Fleets.Where(f => f.Location == location));
        result.AddRange(game.Galaxy.Stargates.Where(g => g.Location == location));
        result.AddRange(game.Galaxy.ConstructionSites.Where(c => c.Location == location));
        return result.Where(o => Game.Visible(human, o)).ToList();
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

    // DISPLAY.PAS's own GetIDMenuChoice/DisplayMenu (DISPLAY.PAS:51-74, MENU.PAS:116-156) -- the
    // shared "ID/menu choice picker" primitive real Pascal builds every target/ground/empire picker
    // on top of (TUI_SURFACES_MAPPING.md's own "Prompts & dialogs" section), and specifically what
    // MAPWIND.PAS's GetMapObject uses for this exact "2+ objects in one sector" case. Real Pascal
    // opens it at a fixed screen position (col 5, row 12); centered here instead, matching
    // CloseUpWindow's own precedent for translating a real absolute-position window onto this
    // port's variable terminal size.
    private static readonly TgAttribute PickerBorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7
    private static readonly TgAttribute PickerNormalAttribute = new(StandardColor.LightGray, StandardColor.Black); // DisplayMenu's own Col param
    private static readonly TgAttribute PickerSelectedAttribute = new(StandardColor.Black, StandardColor.LightGray); // SYSDispSelect = 112

    private void ShowSectorPicker(List<ISectorObject> objects) =>
        ShowObjectPicker(string.Empty, objects, ShowCloseUp);

    /// <summary>
    /// Shared "pick one of these objects" popup -- DISPLAY.PAS's own GetIDMenuChoice/DisplayMenu, the
    /// primitive both MAPWIND.PAS's GetMapObject (2+ objects in one sector, <see cref="ShowSectorPicker"/>)
    /// and FLTCOMM.PAS's GetGround (<see cref="PickGround"/>, Transfer/Abort-Join/Refuel's own
    /// target picker) build on top of in real Pascal. "Name  (Owner)" display format matches both of
    /// those procedures' own AddGround/CreateMenu (MAPWIND.PAS:858-859, FLTCOMM.PAS:87-88) verbatim
    /// -- identical text in both sources, not a coincidence.
    /// </summary>
    private void ShowObjectPicker(string title, IReadOnlyList<ISectorObject> objects, Action<ISectorObject> onChosen)
    {
        var picker = new Window {
            Title = title, // DisplayMenu's own OpenWindow passes '' for the sector-picker case too.
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = 45,
            Height = 7,
            BorderStyle = LineStyle.Single, // ThinBRD
            CanFocus = true,
        };
        picker.SetScheme(new Scheme(PickerNormalAttribute));
        picker.Border.View?.SetScheme(new Scheme(PickerBorderAttribute));

        // No in-window "Enter: choose  Esc: cancel" hint: real Pascal's own GetIDMenuChoice puts
        // that on the shared help line (WriteHelpLine) instead of inside the menu window itself --
        // not reproduced, same simplification depth as CloseUpWindow dropping its own former
        // "Press any key" line for the same reason (it isn't real Pascal content either).
        var listView = new ListView<ObjectListItem> { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        listView.SetScheme(new Scheme { Normal = PickerNormalAttribute, Focus = PickerSelectedAttribute });
        listView.SetSource(new ObservableCollection<ObjectListItem>(objects.Select(o => new ObjectListItem(o, human))));
        listView.Index = 0; // SetSource alone leaves nothing selected -- default to the first item.
        picker.Add(listView);

        var dismiss = AddModal(picker);
        picker.KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.Enter:
                    var chosen = listView.Value;
                    dismiss();
                    if (chosen is not null) {
                        onChosen(chosen.Object);
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
    /// GetGround (FLTCOMM.PAS:51-132): every fleet at <paramref name="source"/>'s own location
    /// (minus <paramref name="source"/> itself unless <paramref name="includeFleet"/>, matching real
    /// Pascal's one actual use of that flag -- CreateMenu never filters *other* fleets on it, only
    /// ever the given one), plus the planet/base there, filtered to the player's own when
    /// <paramref name="playerOnly"/>. No separate <see cref="Game.Visible"/> gating needed even for the
    /// <paramref name="playerOnly"/>: false callers (Transfer/Abort-Join, which can target an enemy
    /// fleet or world) -- <paramref name="source"/> is one of the player's own active fleets, and
    /// <see cref="VisibilityHandler"/>'s own per-fleet ScoutAdjacent call always scouts a fleet's own
    /// location (its offset list includes (0,0)), so anything sharing that exact sector is already
    /// guaranteed Known/Scouted. Real Pascal just opens an empty menu when nothing qualifies; a plain
    /// "nothing here" message reads better.
    /// </summary>
    private void PickGround(Fleet source, bool playerOnly, bool includeFleet, string title, string emptyMessage, Action<ISectorObject> onPicked)
    {
        var candidates = new List<ISectorObject>();
        foreach (var f in game.Galaxy.Fleets) {
            if (f.Location != source.Location) {
                continue;
            }
            if (!includeFleet && ReferenceEquals(f, source)) {
                continue;
            }
            if (playerOnly && !ReferenceEquals(f.Owner, human)) {
                continue;
            }
            candidates.Add(f);
        }

        if (FindWorldAt(source.Location) is { } world && (!playerOnly || ReferenceEquals(world.Owner, human))) {
            candidates.Add(world);
        }

        if (candidates.Count == 0) {
            MessageBox.Query(App!, title, emptyMessage, "OK");
            return;
        }

        ShowObjectPicker(title, candidates, onPicked);
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
    /// <param name="dismissOnOutsideClick">
    /// False for a popup with no real Pascal click-to-cancel equivalent (the Resource Distribution
    /// Editor, the fleet-name prompt): a stray click outside it would otherwise silently discard
    /// whatever the player had already entered with zero feedback, since <see cref="ResourceDistributionEditor.Committed"/>
    /// never gets a chance to fire. An outside click there is just swallowed instead -- real Pascal's
    /// own version of these is keyboard-only anyway, no click-out ever existed to reproduce.
    /// </param>
    private Action AddModal(View popup, bool dismissOnOutsideClick = true)
    {
        var backdrop = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = false };
        // ViewportSettingsFlags.Transparent is Terminal.Gui's own documented mechanism for this
        // ("the Viewport will not be cleared when the View is drawn") -- canceling DrawingContent
        // (an earlier attempt) only skips this view's own custom draw, not the base ClearViewport
        // pass that runs ahead of it, so galaxyView's own pixels underneath were still getting wiped
        // to a solid fill first. Deliberately not combined with TransparentMouse: the backdrop's
        // whole job is catching clicks outside the popup, so it must stay opaque to the mouse.
        backdrop.ViewportSettings = ViewportSettingsFlags.Transparent;

        // MenuBar tracks mouse hover to keep its own highlight/focus in sync even when nothing has
        // been clicked -- confirmed from real testing: hovering it while a modal popup (e.g. the
        // Resource Distribution Editor) was open stole focus straight off the popup with no click at
        // all, since that hover tracking isn't gated by z-order the way a click is. Disabling it for
        // the popup's lifetime blocks that regardless of the exact internal mechanism. A depth
        // counter, not a bare bool: AddModal isn't reentrant on its own (Deploy Fleet chains a
        // name-prompt popup straight into the distribution-editor popup), and a bare
        // `menuBar.Enabled = true` on Dismiss would re-enable the bar the moment the inner popup of
        // two ever closed while the outer one was still up.
        openModalCount++;
        menuBar.Enabled = false;

        void Dismiss()
        {
            Remove(popup);
            Remove(backdrop);
            openModalCount--;
            menuBar.Enabled = openModalCount == 0;
            galaxyView.SetFocus();
        }

        backdrop.MouseEvent += (_, mouse) => {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)) {
                if (dismissOnOutsideClick) {
                    Dismiss();
                }

                mouse.Handled = true;
            }
        };

        Add(backdrop);
        Add(popup);
        popup.SetFocus();

        return Dismiss;
    }

    /// <summary>
    /// Display wrapper for <see cref="ShowSectorPicker"/>'s ListView -- ISectorObject implementors
    /// are plain domain entities with no display-formatting concern of their own. Text format
    /// matches GetMapObject's own CreateMenu (MAPWIND.PAS:858-859): "Name  (Owner)".
    /// </summary>
    private sealed record ObjectListItem(ISectorObject Object, Empire Viewer)
    {
        public override string ToString() =>
            $"{Object.Names.GetValueOrDefault(Viewer) ?? CloseUpWindow.DescribeKind(Object)}  ({Object.Owner.Name})";
    }

    /// <summary>
    /// Fleet menu > Deploy (FLTCOMM.PAS: LaunchFleetCommand): source and destination both reuse the
    /// map cursor (matching TUI_SURFACES_MAPPING.md's own "map cursor reuse for launch/destination"),
    /// a name prompt (LaunchFleetCommand's own FleetName parameter), and the real Resource
    /// Distribution Editor (<see cref="ResourceDistributionEditor"/>, InputNewDistribution) for
    /// picking which ships/cargo actually go -- not an all-or-nothing dump of the source world's
    /// Ships anymore.
    /// </summary>
    private void DeployFleet() =>
        BeginPick("Deploy Fleet -- move cursor to a world to launch from, Enter: select, Esc: cancel", PickDeploySource);

    private void PickDeploySource(Coordinate location)
    {
        var source = FindWorldAt(location);
        if (source is null || !ReferenceEquals(source.Owner, human)) {
            MessageBox.Query(App!, "Deploy Fleet", "That isn't one of your own worlds.", "OK");
            return;
        }

        if (!HasAnyShips(source.Ships)) {
            MessageBox.Query(App!, "Deploy Fleet", "This world has no ships to deploy.", "OK");
            return;
        }

        PromptForFleetName(source);
    }

    private static readonly TgAttribute DialogNormalAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7, matching the sector picker's own popup style
    private static readonly TgAttribute DialogBorderAttribute = new(StandardColor.LightGray, StandardColor.Black);

    // FleetName (FLTCOMM.PAS's own LaunchFleetCommand parameter) -- a small text prompt, matching
    // PlayerSetupWindow's own established TextField-in-a-popup pattern rather than a nested
    // Application.Run.
    private void PromptForFleetName(IEconomicWorld source)
    {
        var dialog = new Window {
            Title = "Name This Fleet",
            X = Pos.Center(), Y = Pos.Center(),
            Width = 50, Height = 5,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        dialog.SetScheme(new Scheme(DialogNormalAttribute));
        dialog.Border.View?.SetScheme(new Scheme(DialogBorderAttribute));

        var nameField = new TextField { X = 1, Y = 1, Width = Dim.Fill(1) };
        dialog.Add(new Label { X = 1, Y = 0, Text = "Fleet name (optional):" });
        dialog.Add(nameField);
        dialog.Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: confirm   Esc: cancel" });

        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);
        nameField.SetFocus();

        nameField.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Enter) {
                return;
            }

            var name = nameField.Text?.Trim() ?? "";
            dismiss();
            BeginDeployDistribution(source, name);
            key.Handled = true;
        };
        dialog.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Esc) {
                return;
            }

            dismiss();
            key.Handled = true;
        };
    }

    private void BeginDeployDistribution(IEconomicWorld source, string fleetName)
    {
        var groundShips = CloneShips(source.Ships);
        var groundCargo = CloneCargo(source.Cargo);
        var fleetShips = new ShipCounts();
        var fleetCargo = new CargoHold();
        var sourceName = source.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeKind(source);

        var editor = new ResourceDistributionEditor(
            fleetShips, fleetCargo, groundShips, groundCargo,
            groundIsPlayerOwned: true, groundIsAFleet: false,
            title: $"Deploy Fleet from {sourceName}");
        var dismiss = AddModal(editor, dismissOnOutsideClick: false);

        editor.Committed += (_, _) => {
            dismiss();

            // NoShips (MISC.PAS) -- LaunchFleetCommand's own "IF NOT NoShips(FltSh)" guard: nothing
            // was actually put aboard, so there's nothing to deploy.
            if (!HasAnyShips(fleetShips)) {
                return;
            }

            BeginPick($"Deploy Fleet -- move cursor to destination, Enter: launch, Esc: cancel", destination => {
                var fleet = FleetLifecycle.DeployFleet(human, source, fleetShips, fleetCargo, destination, game);
                if (!string.IsNullOrWhiteSpace(fleetName)) {
                    // LaunchFleetCommand's own FleetName[1]:=UpCase(FleetName[1]) (FLTCOMM.PAS:517).
                    fleet.Names[human] = char.ToUpperInvariant(fleetName[0]) + fleetName[1..];
                }

                galaxyView.Refresh();
            });
        };
    }

    private static bool HasAnyShips(ShipCounts s) =>
        s.Fighters + s.HunterKillers + s.Jumpships + s.Jumptransports + s.Penetrators + s.Starships + s.Transports > 0;

    // A snapshot, not the source's own live Ships/Cargo: FleetLifecycle.ChangeCompositionOfFleet
    // (which DeployFleet calls internally) overwrites the launch world's own Ships in place, and
    // recomputes the actual ground remainder itself from whatever ships ultimately get deployed --
    // passing the live object into the editor would let its FillFleet/EmptyFleet mutate the world's
    // real inventory before the player even confirms. Every real Core caller avoids the same trap by
    // building composition off a copy, never a world's own live Ships (confirmed by hitting this
    // exact aliasing bug once already, before this snapshot existed).
    private static ShipCounts CloneShips(ShipCounts s) => new() {
        Fighters = s.Fighters, HunterKillers = s.HunterKillers, Jumpships = s.Jumpships,
        Jumptransports = s.Jumptransports, Penetrators = s.Penetrators, Starships = s.Starships, Transports = s.Transports,
    };

    private static CargoHold CloneCargo(CargoHold c) => new() {
        Legions = c.Legions, NinjaLegions = c.NinjaLegions, Ambrosia = c.Ambrosia,
        Chemicals = c.Chemicals, Metals = c.Metals, Supplies = c.Supplies, Trillum = c.Trillum,
    };

    /// <summary>
    /// Fleet menu > Transfer (FLTCOMM.PAS: TransferFleetCommand): the same Resource Distribution
    /// Editor Deploy uses -- InputNewDistribution is the exact same call in real Pascal, just with a
    /// fleet standing in for LaunchFleetCommand's launch world -- between one of the player's own
    /// fleets under the map cursor and whatever <see cref="PickGround"/> picks as the other side
    /// (any owner, matching TransferFleetCommand's own GetGround(...,PlayerOnly:=False,...)).
    /// </summary>
    private void TransferFleet() => PickOwnFleetAtCursor("Transfer Fleet", fleet =>
        PickGround(fleet, playerOnly: false, includeFleet: false, "Transfer Fleet",
            "There is nothing here to transfer with.",
            ground => BeginTransferDistribution(fleet, ground)));

    private void BeginTransferDistribution(Fleet fleet, ISectorObject ground)
    {
        var groundHolder = (IShipCargoHolder)ground;
        var fleetShips = CloneShips(fleet.Ships);
        var fleetCargo = CloneCargo(fleet.Cargo);
        var groundShips = CloneShips(groundHolder.Ships);
        var groundCargo = CloneCargo(groundHolder.Cargo);
        var fleetName = fleet.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeKind(fleet);
        var groundName = ground.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeKind(ground);

        var editor = new ResourceDistributionEditor(
            fleetShips, fleetCargo, groundShips, groundCargo,
            groundIsPlayerOwned: ReferenceEquals(ground.Owner, human), groundIsAFleet: ground is Fleet,
            title: $"Transfer -- {fleetName} <-> {groundName}");
        var dismiss = AddModal(editor, dismissOnOutsideClick: false);

        editor.Committed += (_, _) => {
            dismiss();
            // ChangeCompositionOfFleet (FLEET.PAS:282-389) -- may destroy either side, see that
            // method's own doc comment; TransferFleetCommand calls it unconditionally too.
            FleetLifecycle.ChangeCompositionOfFleet(fleet, groundHolder, fleetShips, fleetCargo, groundShips, groundCargo, game);
            galaxyView.Refresh();
        };
    }

    /// <summary>
    /// Fleet menu > Abort/Join (FLTCOMM.PAS: AbortFleetCommand): dumps the whole fleet onto whatever
    /// <see cref="PickGround"/> picks -- no distribution grid, matching real Pascal exactly
    /// (InputNewDistribution is never called here, unlike Transfer). Two confirmations, both
    /// transcribed from source: target not the player's own, and any single ship type's combined
    /// total exceeding <see cref="ResourceDistribution.MaxResources"/> ("some will be lost" --
    /// purely an echo of Pascal's own warning; this port's plain int counters never actually overflow
    /// on the write itself, see <see cref="FleetLifecycle.AbortFleet"/>'s own doc comment). One
    /// deliberate deviation: real Pascal still shows the second confirmation even after the player
    /// already declined the first (both just set the same <c>Ok:=False</c>, so the outcome is
    /// identical either way) -- declining here returns immediately instead of asking a second,
    /// already-moot question.
    /// </summary>
    private void AbortJoinFleet() => PickOwnFleetAtCursor("Abort/Join Fleet", fleet =>
        PickGround(fleet, playerOnly: false, includeFleet: false, "Abort/Join Fleet",
            "There is nothing here to abort the fleet to.",
            ground => ConfirmAbortJoin(fleet, ground)));

    private void ConfirmAbortJoin(Fleet fleet, ISectorObject ground)
    {
        if (!ReferenceEquals(ground.Owner, human)) {
            var groundName = ground.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeKind(ground);
            if (MessageBox.Query(App!, "Abort/Join Fleet", $"{groundName} is not part of your empire. Are you sure you want to abort the fleet?", "_Yes", "_No") != 0) {
                return;
            }
        }

        var groundHolder = (IShipCargoHolder)ground;
        var overflow = Enum.GetValues<ShipType>().Any(t => groundHolder.Ships[t] + fleet.Ships[t] > ResourceDistribution.MaxResources);
        if (overflow) {
            if (MessageBox.Query(App!, "Abort/Join Fleet", "An object cannot hold so many ships -- some will be lost. Are you sure?", "_Yes", "_No") != 0) {
                return;
            }
        }

        FleetLifecycle.AbortFleet(fleet, groundHolder, game);
        galaxyView.Refresh();
    }

    /// <summary>
    /// Fleet menu > Refuel (FLTCOMM.PAS: RefuelFleetCommand): <see cref="PickGround"/> restricted to
    /// the player's own (RefuelFleetCommand's own GetGround(...,PlayerOnly:=True,IncludeFleet:=True,...)
    /// -- IncludeFleet lets the fleet refuel from trillum already in its own cargo, matching that
    /// procedure's own SameID(FltID,Ground) message branch), then a numeric prompt for tons of
    /// trillum to spend.
    /// </summary>
    private void RefuelFleet() => PickOwnFleetAtCursor("Refuel Fleet", fleet =>
        PickGround(fleet, playerOnly: true, includeFleet: true, "Refuel Fleet",
            "There is no world or fleet of yours here to refuel from.",
            ground => PromptForTrillum(fleet, (IShipCargoHolder)ground)));

    private void PromptForTrillum(Fleet fleet, IShipCargoHolder ground)
    {
        var maxTri = FleetLifecycle.MaxTrillumToRefuel(fleet, ground);
        if (maxTri <= 0) {
            MessageBox.Query(App!, "Refuel Fleet", "There is no trillum available to refuel with.", "OK");
            return;
        }

        var dialog = new Window {
            Title = "Refuel Fleet",
            X = Pos.Center(), Y = Pos.Center(),
            Width = 50, Height = 6,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        dialog.SetScheme(new Scheme(DialogNormalAttribute));
        dialog.Border.View?.SetScheme(new Scheme(DialogBorderAttribute));

        var amountField = new TextField { X = 1, Y = 1, Width = Dim.Fill(1) };
        var errorLabel = new Label { X = 1, Y = 2 };
        dialog.Add(new Label { X = 1, Y = 0, Text = $"Tons of trillum (max {maxTri}, 0 = max):" });
        dialog.Add(amountField);
        dialog.Add(errorLabel);
        dialog.Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: confirm   Esc: cancel" });

        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);
        amountField.SetFocus();

        // GetTrillumToUse (FLTCOMM.PAS:692-724): 0 (or a blank field here) defaults to the max,
        // out-of-range re-prompts with an error instead of closing -- the REPEAT...UNTIL Ok retry
        // loop, minus the retyped literal since the field just stays open and focused.
        amountField.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Enter) {
                return;
            }
            key.Handled = true;

            var text = amountField.Text?.Trim() ?? "";
            if (!int.TryParse(text, out var amount) && text.Length > 0) {
                errorLabel.Text = "Enter a whole number of tons.";
                return;
            }
            if (amount == 0) {
                amount = maxTri;
            } else if (amount < 0) {
                errorLabel.Text = "That is a most bizarre request.";
                return;
            } else if (amount > maxTri) {
                errorLabel.Text = $"The maximum amount allowable is {maxTri} tons.";
                return;
            }

            dismiss();
            FleetLifecycle.RefuelFleet(fleet, ground, amount);
            galaxyView.Refresh();
        };
        dialog.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Esc) {
                return;
            }

            dismiss();
            key.Handled = true;
        };
    }

    /// <summary>
    /// Ministry of War menu > Attack (ATTCOMM.PAS: GetTarget/AttackCommand/CleanUp/EnemyConquered).
    /// Target selection order is transcribed directly from GetTarget's own nested CreateMenu
    /// (ATTCOMM.PAS:663-715): every enemy Fleet in the sector goes on the target list first (a picker
    /// if there's more than one), and the world itself (Planet/Starbase) is only ever offered "if no
    /// fleets" (ListSize=0) -- a world defended by any enemy fleet cannot be attacked directly, the
    /// fleet(s) must be dealt with first. Confirmed with the user after an initial pass got this
    /// backwards (checked the world before any defending fleet) and produced a wildly wrong result:
    /// an undamaged 50-ship Kingdom defense fleet sat next to a heavily-refortified capital, and
    /// attacking the capital directly wiped out a 3000-ship human fleet against a world whose own war
    /// economy had ballooned during the several turns the attack took to arrive. MVP gate, called out
    /// rather than silent: attacker and target must already be in the same sector (i.e. the fleet has
    /// arrived) -- real Pascal's exact range rule wasn't re-derived here.
    /// </summary>
    private void Attack() => PickOwnFleetAtCursor("Attack", attacker => {
        var cursor = attacker.Location;
        var enemyFleets = game.Galaxy.Fleets.Where(f => f.Location == cursor && !ReferenceEquals(f.Owner, human)).ToList();

        if (enemyFleets.Count > 1) {
            ShowObjectPicker("Attack", enemyFleets.Cast<ISectorObject>().ToList(), o => BeginAttack(attacker, o));
            return;
        }

        object? target = enemyFleets.Count == 1 ? enemyFleets[0]
            : (object?)game.Galaxy.Planets.FirstOrDefault(p => p.Location == cursor && !ReferenceEquals(p.Owner, human))
              ?? game.Galaxy.Starbases.FirstOrDefault(s => s.Location == cursor && !ReferenceEquals(s.Owner, human));

        if (target is null) {
            MessageBox.Query(App!, "Attack", "No enemy target in this sector.", "OK");
            return;
        }

        BeginAttack(attacker, target);
    });

    // AttackCommand's own "Standard battle configuration (Y/n)?" fork (ATTCOMM.PAS:1608-1616): Y
    // (default) skips Fleet Group Configuration and uses DefaultDistribution, matching what this
    // command always did before this screen existed; N opens the real GetGroups-equivalent screen.
    // Esc abandons the attack entirely (IF Ans<>EscKey), matching AttackCommand's own guard.
    private void BeginAttack(Fleet attacker, object target)
    {
        var choice = MessageBox.Query(App!, "Attack", "Standard battle configuration?", "_Yes", "_No");
        if (choice is null) {
            return;
        }

        if (choice == 0) {
            StartEngagement(attacker, target, CombatEngine.DefaultDistribution(attacker));
            return;
        }

        var configWindow = new FleetGroupConfigurationWindow(attacker.Ships, attacker.Cargo);
        var dismiss = AddModal(configWindow, dismissOnOutsideClick: false);
        configWindow.Committed += (_, _) => {
            dismiss();
            StartEngagement(attacker, target, [.. configWindow.Groups]);
        };
    }

    private void StartEngagement(Fleet attacker, object target, List<GroupRecord> groups)
    {
        // AttackCommand's own IF NoOfGroups>0 (ATTCOMM.PAS:1619) -- zero groups skips the battle
        // entirely (no Engage, no CleanUp), matching real Pascal exactly rather than fighting an
        // empty engagement.
        if (groups.Count == 0) {
            return;
        }

        var state = InteractiveCombat.BeginEngagement(human, (IShipCargoHolder)target, groups);
        var targetName = DisplayName((ISectorObject)target);

        var display = new TacticalBattleDisplayWindow(state, random, targetName);
        var dismiss = AddModal(display, dismissOnOutsideClick: false);
        display.BattleEnded += (_, _) => {
            dismiss();
            ApplyAttackOutcome(attacker, target, state);
            galaxyView.Refresh();
        };
    }

    // CleanUp (ATTCOMM.PAS:1564-1588): RestoreCombatant for both sides first, then (only on a
    // successful conquest) OldShipsFound/AskToCapture/EnemyConquered's own DisplayBackground call --
    // all three run *before* ResolveAttack, which is what actually reassigns ownership (ConquerWorld).
    // Calling FindWorldBackgroundText(conquer:true) here, before ResolveAttack, is load-bearing: its
    // 'A:' condition reads the target's *current* (pre-conquest) owner, so calling it after would mean
    // it can never match (see docs/ROADMAP.md and ScenarioLoaderWorldBackgroundTests for the ordering
    // bug this fixed).
    private void ApplyAttackOutcome(Fleet attackerFleet, object target, InteractiveCombatState state)
    {
        var subject = (ISectorObject)target;
        var hkSurprise = CombatEngine.ForcesUnknown(attackerFleet, subject.Owner);

        CombatOutcome.RestoreCombatant(attackerFleet, state.Casualties);
        CombatOutcome.RestoreCombatant(target, state.Killed);

        var capture = true;
        string? report = null;

        if (state.Result == AttackResultType.DefenderConquered) {
            ShowOldShipsFound(target);

            if (target is Fleet targetFleet && HasAnyShips(targetFleet.Ships)) {
                // AskToCapture's own inverted polarity (ATTCOMM.PAS:1358-1380): "Y" (destroy) sets
                // Capture:=False; anything else -- the default -- sets Capture:=True.
                capture = MessageBox.Query(App!, "Attack", "Do you wish to destroy the enemy fleet?", "_Yes", "_No") != 0;
            }

            report = Game.FindWorldBackgroundText(game, subject, human, conquer: true) is { } lines
                ? string.Join('\n', lines)
                : ConquestMessage(attackerFleet, subject);
        }

        CombatOutcome.ResolveAttack(state.Result, attackerFleet, target, hkSurprise, capture, state.Casualties, state.Killed, game, random);

        MessageBox.Query(App!, "Attack", report ?? $"Result: {state.Result}", "OK");
    }

    // OldShipsFound (ATTCOMM.PAS:1485-1526): an Independent planet may hold ships too obsolete for its
    // own tech level (left behind by a since-advanced empire) -- read-only info, no state effect.
    private void ShowOldShipsFound(object target)
    {
        if (target is not Planet planet || !planet.Owner.IsIndependent) {
            return;
        }

        var obsolete = Enum.GetValues<ShipType>()
            .Where(t => planet.Ships[t] > 0 && TechCatalog.MinTechForShip[t] > planet.TechLevel)
            .Select(t => $"{planet.Ships[t]} {t}")
            .ToList();

        if (obsolete.Count > 0) {
            MessageBox.Query(App!, "Attack", $"We have found the following ships in orbit:\n{string.Join('\n', obsolete)}", "OK");
        }
    }

    // EnemyConquered's own three fallback congratulatory messages (ATTCOMM.PAS:1403-1431), verbatim --
    // Message1 always wins for a conquered capital (GetType(Target)=CapTyp, checked here before
    // ResolveAttack clears it back to Independent); otherwise one of the three is picked uniformly at
    // random, consuming exactly one Rnd(1,3) call to match Pascal's own RNG-order contract.
    private string ConquestMessage(Fleet attackerFleet, ISectorObject subject)
    {
        var empireName = human.Name;
        var isCapital = subject is IEconomicWorld { Type: WorldType.Capital };
        var messageNumber = isCapital ? 1 : PascalMath.Rnd(random, 1, 3);
        var noun = subject switch { Fleet => "fleet", Starbase => "starbase", _ => "planet" };

        return messageNumber switch {
            1 => human.IsEmpress
                ? $"In the name of Her Imperial Majesty, Lady of {empireName}, I hereby declare\nthis {noun} to be under the sovereign jurisdiction of the\n{empireName} Empire."
                : $"In the name of His Imperial Majesty, Lord of {empireName}, I hereby declare\nthis {noun} to be under the sovereign jurisdiction of the\n{empireName} Empire.",
            2 => $"Congratulations {MyLord()}, {DisplayName(attackerFleet)} has succeeded in its attack against\n{DisplayName(subject)}.  No doubt some of your enemies will in the future\nbe more careful when challenging this empire.",
            _ => $"Congratulations on your victory, {MyLord()}, but remember that not\nall battles will be this easy.",
        };
    }

    private string DisplayName(ISectorObject obj) => obj.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeKind(obj);

    // PRIMINTR.PAS's MyLord, same independently-randomized-honorific precedent as
    // TurnStartGreetingWindow's own private copy -- flavor text only, no gameplay effect, so reusing
    // Random.Shared rather than this empire's deterministic combat `random` is consistent with that
    // existing choice, not a new one.
    private string MyLord() => human.IsEmpress
        ? new[] { "My Lady", "Your Highness", "Your Excellency", "My Empress" }[Random.Shared.Next(4)]
        : new[] { "My Lord", "Your Highness", "Your Majesty", "My Liege", "Your Excellency", "Sir" }[Random.Shared.Next(6)];

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
            new("_Transfer", Key.Empty, TransferFleet),
            new("_Abort/Join", Key.Empty, AbortJoinFleet),
            new("_Refuel", Key.Empty, RefuelFleet),
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
