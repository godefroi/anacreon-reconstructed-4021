using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using ThreeLn.Reconstruction4021.Core;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace ThreeLn.Reconstruction4021.Tui;

/// <summary>
/// Phase 8 top-level shell (docs/TUI_SURFACES_MAPPING.md's "Deliberate deviation" section): unlike the
/// original DOS game, where the galaxy map was just one of several swappable F-key panels, here the map
/// is the permanent base view -- everything else (menu, status bar, future overlay windows) sits on top
/// of it, never replaces it. Menu/status bar leaf items are stubbed to a "not yet implemented" MessageBox
/// for now; each gets a real implementation as its own surface is built out.
/// </summary>
internal sealed class GameShell : Window
{
    // COLORS.INC's ColorScrColor (the color-mode palette; BW/Mono variants aren't relevant here). DOS
    // attribute byte = (bg &lt;&lt; 4) | fg, decoded against the standard 16-color CGA/EGA palette.
    private static readonly TgAttribute MenuBarAttribute = new(StandardColor.White, StandardColor.Red); // SYSMenuBar = 79
    private static readonly TgAttribute DropdownAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSMenu = 7
    private static readonly TgAttribute HelpLineAttribute = new(StandardColor.Red, StandardColor.Black); // SYSHelpLine = 4

    public GameShell(Game game)
    {
        Title = $"Anacreon -- {game.Galaxy.Size}x{game.Galaxy.Size} galaxy";
        Width = Dim.Fill();
        Height = Dim.Fill();

        // No window chrome -- the menu bar/status bar/map fill the whole screen edge to edge, same as
        // the original's full-screen text display (there was never a bordered "app window" to draw).
        BorderStyle = LineStyle.None;
        Border.Thickness = new Thickness(0);

        // GAUNTLET.SCN's file order puts CreatePlayerEmpire before every CreateNPEmpire, so the human
        // player is Empires[0] -- matches every dos_131 scenario's authoring convention (checked across
        // the committed set), not something the loader guarantees structurally.
        var galaxyView = new GalaxyView(game.Galaxy, game.Empires[0]) {
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

        Add(galaxyView);
        Add(menuBar);
        Add(statusBar);
        Add(coordinateLabel);

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
            if (key.KeyCode != KeyCode.Esc || galaxyView.HasFocus == false) {
                return;
            }

            menuBar.NewKeyDownEvent(Key.G.WithAlt);
            key.Handled = true;
        };
    }

    private void ConfirmQuit()
    {
        // MessageBox's last button is the default (focused) one -- "No" here, so a stray Enter doesn't quit.
        if (MessageBox.Query(App!, "Quit", "Are you sure you want to quit?", "Yes", "No") == 0) {
            App?.RequestStop();
        }
    }

    private void Stub(string label) => MessageBox.Query(App!, label, "Not yet implemented.", "OK");

    private MenuBarItem[] BuildMenus() => [
        new MenuBarItem("⌂", new MenuItem[] {
            new("_About Anacreon", Key.Empty, () => Stub("About Anacreon")),
        }),
        new MenuBarItem("_Game", new MenuItem[] {
            new("_Pause", Key.Empty, () => MessageBox.Query(App!, "Paused", "Time has stopped. Press OK to continue.", "OK")),
            new("_Status Hardcopy", Key.Empty, () => Stub("Status Hardcopy")),
            new("_Next Turn", Key.Empty, () => Stub("Next Turn")),
            new("_Quit", Key.Empty, ConfirmQuit),
        }),
        new MenuBarItem("_Empire", new MenuItem[] {
            new("_Send Message", Key.Empty, () => Stub("Send Message")),
            new("_Read Messages", Key.Empty, () => Stub("Read Messages")),
            new("_Trade Technology", Key.Empty, () => Stub("Trade Technology")),
        }),
        new MenuBarItem("_Worlds", new MenuItem[] {
            new("_Close Up", Key.Empty, () => Stub("Close Up")),
            new("_Designate", Key.Empty, () => Stub("Designate")),
            new("P_roduction", Key.Empty, () => Stub("Production")),
            new("_ISSP", Key.Empty, () => Stub("ISSP")),
            new("_Add Name", Key.Empty, () => Stub("Add Name")),
            new("Delete _Name", Key.Empty, () => Stub("Delete Name")),
            new("_Liberate", Key.Empty, () => Stub("Liberate")),
            new("_Self-Destruct", Key.Empty, () => Stub("Self-Destruct")),
        }),
        new MenuBarItem("_Fleet", new MenuItem[] {
            new("_Deploy", Key.Empty, () => Stub("Deploy Fleet")),
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
            new("_Attack", Key.Empty, () => Stub("Attack")),
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
