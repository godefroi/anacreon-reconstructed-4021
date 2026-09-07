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
using Reconstructed4021.Core.SaveFormat;
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
public sealed class GameShell : Window
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

        // A double-click behaves like Enter on the same sector (a single click just moves the cursor
        // there, per GalaxyView's own OnMouseEvent) -- routed through the exact same ActivateCursor
        // the keyboard path uses, so the two can never drift apart (pending-pick confirm during
        // Deploy/Attack destination selection, Close Up otherwise).
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
        // A still-open panel (Fleet/Status/News/etc -- AddPanel's own openPanelDismiss) sits on top of
        // AddModal's own galaxyView.Enabled=false, which only clears once every open modal is gone.
        // Close Up's own C/T/J/A shortcuts dismiss *themselves* before calling into here, but if they
        // were stacked on top of an open panel (AddCloseUpOverlay's own doc comment), that panel's own
        // AddModal call is still open underneath and still holds galaxyView disabled -- found live: F5
        // (Fleet Window), Enter to Close Up, C for Change Destination left the map cursor completely
        // unmovable. Dismissing the panel here, not just whatever popup got us here, is what actually
        // frees the map back up.
        openPanelDismiss?.Invoke();

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
        AutoSave();
        Choice = ExitChoice.EndTurn;
        App?.RequestStop();
    }

    private void ConfirmQuit()
    {
        // PLAYTURN.PAS:1075/1178 (XXXCom) -- Quit here just sets ExitGame:=True, which unwinds the
        // per-turn loop back to ANACREON.PAS's outer REPEAT, landing back on Prologue (the main menu),
        // not a full process exit. 0 = Yes here (DosDialogWindow's own null/0/1 shape).
        ShowConfirm("Quit", "Are you sure you want to quit? You'll return to the main menu.", choice => {
            if (choice == 0) {
                Choice = ExitChoice.MainMenu;
                App?.RequestStop();
            }
        });
    }

    // No Pascal equivalent -- real Quit (PLAYTURN.PAS's XXXCom) only ever returns to the main menu; a
    // full exit is a separate command on Prologue's own menu bar, one screen further back. Added here as
    // a TUI-only convenience once Quit stopped exiting the app outright, so leaving the game still has a
    // one-step way out instead of forcing a trip back through the main menu first.
    private void ConfirmExitToOs()
    {
        ShowConfirm("Exit to OS", "Are you sure you want to exit to the operating system?", choice => {
            if (choice == 0) {
                Choice = ExitChoice.ExitToOs;
                App?.RequestStop();
            }
        });
    }

    // SaveTheGame (PROLOG.PAS:694-755): prompts for a filename (real Pascal prefills it with
    // CurrentGame, the already-loaded save's own name -- this port has no such tracked state, so it
    // suggests {empire name}-{year} instead), Esc cancels with no message, matching source.
    private void PromptForSaveName()
    {
        var dialog = new Window {
            Title = "Save Game",
            X = Pos.Center(), Y = Pos.Center(),
            Width = 55, Height = 5,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        dialog.SetScheme(new Scheme(DialogNormalAttribute));
        dialog.Border.View?.SetScheme(new Scheme(DialogBorderAttribute));

        var nameField = new TextField { X = 1, Y = 1, Width = Dim.Fill(1), Text = $"{human.Name}-{game.Year}" };
        dialog.Add(new Label { X = 1, Y = 0, Text = "Filename to save to:" });
        dialog.Add(nameField);
        dialog.Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: confirm   Esc: cancel" });

        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);
        nameField.SetFocus();

        nameField.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Enter) {
                return;
            }
            key.Handled = true;

            var name = nameField.Text?.Trim() ?? "";
            if (name.Length == 0) {
                return; // nothing to save to -- stay open rather than write a blank filename
            }

            dismiss();
            SaveGameAs(name);
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
    /// SaveGame (LOADSAVE.PAS:645-714). Real Pascal shows no "saved successfully" popup at all
    /// (silent on success, PROLOG.PAS:751-753 just closes the window) -- this port adds one anyway,
    /// matching this file's own established convention of a brief confirmation wherever Pascal's own
    /// comm-line text has no TUI equivalent (e.g. SrmSweep's own "completed" message). The actual
    /// write is <see cref="WriteSaveFile"/>, shared with <see cref="AutoSave"/>.
    /// </summary>
    private void SaveGameAs(string name)
    {
        var fileName = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.json";
        var saveDir = FindSaveDirectory();
        fileName = AvoidCollision(saveDir, fileName);
        if (!WriteSaveFile(saveDir, fileName, out var error)) {
            ShowInfo("Save Game", $"Could not save: {error}");
            return;
        }

        ShowInfo("Save Game", $"Game saved to {fileName}.");
    }

    /// <summary>
    /// No Pascal equivalent -- real Pascal's own SaveGame just overwrites whatever file already sits
    /// at that name. This port's own default suggested name (<see cref="PromptForSaveName"/>'s
    /// <c>"{human.Name}-{game.Year}"</c>) makes picking the same name twice easy to do by accident, so
    /// a second save under that name would otherwise silently destroy the first one with no
    /// confirmation. Appends " (1)", " (2)", etc. instead, matching the familiar file-manager
    /// convention -- every distinct save the player actually made is kept.
    /// </summary>
    private static string AvoidCollision(string directory, string fileName)
    {
        if (!File.Exists(Path.Combine(directory, fileName))) {
            return fileName;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 1; ; i++) {
            var candidate = $"{baseName} ({i}){extension}";
            if (!File.Exists(Path.Combine(directory, candidate))) {
                return candidate;
            }
        }
    }

    private const int MaxAutoSaves = 10;

    /// <summary>
    /// No Pascal equivalent -- a TUI-only convenience, one snapshot after every turn so a crash mid-
    /// session (the very thing this port's own crash log exists for) never costs more than one turn's
    /// progress. Silent on both success and failure: surfacing a popup every single turn would defeat
    /// the point of it being automatic, and a failed autosave isn't worth interrupting play over the
    /// way a failed *manual* save is. Capped at <see cref="MaxAutoSaves"/> files (oldest by write time
    /// first) so a long game doesn't grow this directory without bound.
    /// </summary>
    private void AutoSave()
    {
        var autoDir = Path.Combine(FindSaveDirectory(), "auto");
        Directory.CreateDirectory(autoDir);
        WriteSaveFile(autoDir, $"{human.Name}-{game.Year}.json", out _);

        var stale = new DirectoryInfo(autoDir).GetFiles("*.json")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(MaxAutoSaves);
        foreach (var file in stale) {
            try {
                file.Delete();
            } catch (IOException) {
                // Best-effort pruning -- same "don't interrupt play over this" reasoning as the save itself.
            }
        }
    }

    /// <summary>
    /// SaveGame (LOADSAVE.PAS:645-714): real Pascal writes to a temp <c>.BAK</c> file first and only
    /// swaps it in on success, so a failed write can never corrupt an existing save -- kept here even
    /// though this port's own format is JSON, not the DOS binary layout, since it's a real correctness
    /// property, not a DOS-era artifact.
    /// </summary>
    private bool WriteSaveFile(string directory, string fileName, out string? error)
    {
        var path = Path.Combine(directory, fileName);
        var tempPath = path + ".tmp";

        try {
            File.WriteAllText(tempPath, GameJson.Serialize(game));
            File.Move(tempPath, path, overwrite: true);
            error = null;
            return true;
        } catch (IOException ex) {
            error = ex.Message;
            return false;
        }
    }

    // saves/ is gitignored (same precedent as Program.cs's own logs/, for crash logs) -- a player's
    // own save files, never committed. Not assets/saves/, which is a directory of committed test
    // fixtures (GarrisonedOutpostFixtureTests etc.), not a save destination. auto/ (AutoSave's own
    // subdirectory) lives under here too -- same gitignore already covers it.
    private static string FindSaveDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Reconstructed4021.slnx"))) {
            dir = dir.Parent;
        }

        var savesDir = Path.Combine(dir?.FullName ?? AppContext.BaseDirectory, "saves");
        Directory.CreateDirectory(savesDir);
        return savesDir;
    }

    private void Stub(string label) => ShowInfo(label, "Not yet implemented.");

    private IEconomicWorld? FindWorldAt(Coordinate location) =>
        (IEconomicWorld?)game.Galaxy.Planets.FirstOrDefault(p => p.Location == location)
        ?? game.Galaxy.Starbases.FirstOrDefault(s => s.Location == location);

    /// <summary>
    /// Worlds menu > Designate/ISSP/Production, and Close Up on one of the player's own worlds --
    /// all four now live as tabs in one <see cref="WorldInfoWindow"/> rather than four separate
    /// screens (see that class's own doc comment for why). <paramref name="initialTab"/> just picks
    /// which tab is focused when it opens; every tab is always built and always reachable from
    /// there, so a wrong initial guess costs nothing.
    /// </summary>
    private void ShowWorldInfo(IEconomicWorld? world, string initialTab)
    {
        if (world is null || !ReferenceEquals(world.Owner, human)) {
            ShowInfo(initialTab, "Move the cursor onto one of your own worlds first.");
            return;
        }

        var window = new WorldInfoWindow(world, DisplayName(world), game, human, initialTab,
            newType => ConfirmDesignate(world, newType));
        var dismiss = AddCloseUpOverlay(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                dismiss();
                key.Handled = true;
                return;
            }

            // Same D:Deploy CloseUpWindow itself offers for any object -- this window replaced
            // CloseUpWindow for owned worlds, so it needs to keep offering it too.
            if (char.ToUpperInvariant((char)key.AsRune.Value) == 'D') {
                dismiss();
                DeployFleet(world);
                key.Handled = true;
            }
        };
    }

    // None of these five onSelect callbacks dismiss their own panel before calling ShowCloseUp --
    // AddCloseUpOverlay stacks Close Up on top of the still-open panel instead (see its own doc
    // comment), so Esc from Close Up hands focus straight back to this exact instance, scroll
    // position and highlighted row untouched.
    private void Status()
    {
        var window = new StatusWindow(game, human, ShowCloseUp);
        var dismiss = AddPanel(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode is KeyCode.Esc or KeyCode.F3) {
                dismiss();
                key.Handled = true;
            }
        };
    }

    private void ShowFleetWindow()
    {
        var window = new FleetWindow(game, human, ShowCloseUp);
        var dismiss = AddPanel(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode is KeyCode.Esc or KeyCode.F5) {
                dismiss();
                key.Handled = true;
            }
        };
    }

    private void ShowNewsWindow()
    {
        var window = new NewsWindow(human, ShowCloseUp);
        var dismiss = AddPanel(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode is KeyCode.Esc or KeyCode.F7) {
                dismiss();
                key.Handled = true;
            }
        };
    }

    private void ShowEmpireWindow()
    {
        var window = new EmpireWindow(game, human, ShowCloseUp);
        var dismiss = AddPanel(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode is KeyCode.Esc or KeyCode.F8) {
                dismiss();
                key.Handled = true;
            }
        };
    }

    private void ShowNamesWindow()
    {
        var window = new NamesWindow(game, human, ShowCloseUp);
        var dismiss = AddPanel(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode is KeyCode.Esc or KeyCode.F9) {
                dismiss();
                key.Handled = true;
            }
        };
    }

    private void ShowHelpWindow()
    {
        var window = new HelpWindow();
        var dismiss = AddPanel(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode is KeyCode.Esc or KeyCode.F1) {
                dismiss();
                key.Handled = true;
            }
        };
    }

    private void ShowTechTree()
    {
        var window = new TechTreeWindow(human);
        var dismiss = AddModal(window);
        window.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode == KeyCode.Esc) {
                dismiss();
                key.Handled = true;
            }
        };
    }

    private void Designate() => ShowWorldInfo(FindWorldAt(galaxyView.CursorLocation), "Designate");
    private void Issp() => ShowWorldInfo(FindWorldAt(galaxyView.CursorLocation), "ISSP");
    private void Production() => ShowWorldInfo(FindWorldAt(galaxyView.CursorLocation), "Production");

    // DesignateCommand's own local TypeN (DESIGN.PAS:672-694) -- "a/an X" phrasing for its own
    // confirm dialogs and final report; distinct from WorldDesignation.TypeName's bare noun (read
    // by Production's Type: field).
    private static string DesignationArticleName(WorldType type) => type switch {
        WorldType.Agricultural => "an agricultural world",
        WorldType.Ambrosia => "an ambrosia world",
        WorldType.Base => "a base planet",
        WorldType.BaseStarbase => "a specialized base planet",
        WorldType.Capital => "the capital of the empire",
        WorldType.Chemical => "a chemical factory world",
        WorldType.Independent => "an independent world",
        WorldType.JumpshipBase => "a jumpship complex",
        WorldType.JumpshipBaseStarbase => "a specialized jumpship complex",
        WorldType.Mine => "a metal-mining world",
        WorldType.NinjaWorld => "a ninja world",
        WorldType.Outpost => "an outpost",
        WorldType.RawMaterialMine => "a mining world",
        WorldType.RawMaterialMineStarbase => "a specialized mining world",
        WorldType.StarshipBase => "a starship complex",
        WorldType.StarshipBaseStarbase => "a specialized starship complex",
        WorldType.TransportBase => "a warpship complex",
        WorldType.TransportBaseStarbase => "a specialized warpship complex",
        WorldType.University => "a research university world",
        WorldType.Terraform => "a terraforming world",
        WorldType.TrillumMine => "a trillum-mining world",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    // DesignateCommand's own risk-confirm chain (DESIGN.PAS:785-832): at most one of these fires,
    // in this exact order (an ELSE IF chain in real Pascal). Declining used to re-open the type
    // picker (matching DesignateCommand's own outer REPEAT); now that Designate is a persistent tab
    // rather than a transient picker, there's nothing left to "reopen" -- the tab's still right there.
    private void ConfirmDesignate(IEconomicWorld world, WorldType newType)
    {
        var cls = world.EffectiveClass;
        var warning = newType switch {
            WorldType.Capital =>
                $"{MyLord()}, changing the capital will result in short-term loss of efficiency\nand increased unrest among the people of the empire.",
            _ when world is Starbase { Kind: StarbaseKind.IndustrialComplex } && newType is not
                (WorldType.Base or WorldType.JumpshipBase or WorldType.StarshipBase or WorldType.TransportBase or WorldType.Capital or WorldType.NinjaWorld) =>
                $"But {MyLord()}, an industrial complex would be wasted on such a trivial designation.",
            WorldType.University when world.TechLevel < human.Capital!.TechLevel =>
                $"But {MyLord()}, {DisplayName(world)} is not yet as advanced as the capital.\nAs a university world it wouldn't be of much use.",
            WorldType.Mine or WorldType.RawMaterialMine or WorldType.TrillumMine when cls is WorldClass.GasGiant or WorldClass.Ice or WorldClass.Ocean or WorldClass.Poisonous =>
                $"{MyLord()}, the environment of {DisplayName(world)} is not really suited to\nlarge scale mining operations.",
            WorldType.Agricultural when cls is WorldClass.Arid or WorldClass.Artificial or WorldClass.Barren or WorldClass.Desert or WorldClass.Ice or WorldClass.Poisonous or WorldClass.Underground or WorldClass.Volcanic =>
                $"I hope you will reconsider, {MyLord()}, {DisplayName(world)} would not be\nan ideal agricultural world.",
            _ => null,
        };

        if (warning is null) {
            FinishDesignate(world, newType);
            return;
        }

        ShowConfirm("Designate", $"{warning}\nAre you sure about this command?", choice => {
            if (choice == 0) {
                FinishDesignate(world, newType);
            }
        });
    }

    private void FinishDesignate(IEconomicWorld world, WorldType newType)
    {
        WorldDesignation.Redesignate(world, newType, random);
        galaxyView.Refresh();
        ShowInfo("Designate",
            $"{DisplayName(world)} has been designated as {DesignationArticleName(newType)}.\n" +
            $"All industries are being re-distributed.  New efficiency: {world.Efficiency}%");
    }

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
                ShowInfo(title, "Move the cursor onto one of your own fleets first.");
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
    //
    // Fleet/Deploy action shortcuts (per the user's own explicit request): D deploys from whatever
    // world is at obj's own Location, regardless of obj's own type (see DeployFleet(Coordinate)'s own
    // doc comment); C/T/J/A additionally run Change Destination/Transfer/Abort-Join/Attack when obj is
    // one of the player's own fleets, using it directly instead of going back through
    // PickOwnFleetAtCursor's own map-cursor pick. CloseUpWindow itself renders the matching hint line
    // -- no Pascal precedent to transcribe here, this is a TUI-only convenience layered on top of an
    // already-open Close Up. Any other key keeps the original "any key dismisses" behavior
    // (CloseUpWindow's own doc comment on why that's already a deviation from real Pascal's non-modal
    // CloseUpCom).
    private void ShowCloseUp(ISectorObject obj)
    {
        // Picking a row in the Status/Fleet/News windows can name an object anywhere on the map, far
        // from wherever the cursor already was -- move it there so the map (and its coordinate
        // readout) reflects what Close Up is now showing, not stale prior state. A no-op when the
        // cursor's own map-driven route already put it here (MoveCursorTo's own same-coordinate guard).
        galaxyView.MoveCursorTo(obj.Location);

        // One of the player's own worlds gets the full tabbed WorldInfoWindow (Close Up is just its
        // first tab) instead of the standalone CloseUpWindow -- see WorldInfoWindow's own doc comment.
        if (obj is IEconomicWorld ownWorld && ReferenceEquals(ownWorld.Owner, human)) {
            ShowWorldInfo(ownWorld, "CloseUp");
            return;
        }

        var window = new CloseUpWindow(obj, human, game);
        var dismiss = AddCloseUpOverlay(window);
        window.KeyDown += (_, key) => {
            var letter = char.ToUpperInvariant((char)key.AsRune.Value);
            if (letter == 'D') {
                dismiss();
                DeployFleet(obj);
                key.Handled = true;
                return;
            }

            if (obj is Fleet fleet && ReferenceEquals(fleet.Owner, human) &&
                ResolveFleetContextAction(letter) is { } action) {
                dismiss();
                action(fleet);
                key.Handled = true;
                return;
            }

            dismiss();
            key.Handled = true;
        };
    }

    /// <summary>
    /// C/T/J/A -- Change Destination/Transfer/Abort-Join/Attack, the four Fleet/Ministry-of-War
    /// commands reachable directly off a selected fleet (Close Up and the Sector Selected Popup),
    /// shared so the two surfaces can't drift on which letter maps to which command. Deploy (D) isn't
    /// here -- it's handled separately by both call sites since it applies to any selected object, not
    /// just the player's own fleets. Not gated on "is this action actually useful right now" (e.g.
    /// Attack with no enemy present) -- same idiom PickOwnFleetAtCursor/Attack/PickGround already use
    /// everywhere else in this file: offer the command, let its own existing MessageBox explain why it
    /// didn't apply.
    /// </summary>
    private Action<Fleet>? ResolveFleetContextAction(char key) => key switch {
        'C' => ChangeDestination,
        'T' => TransferFleet,
        'J' => AbortJoinFleet,
        'A' => Attack,
        _ => null,
    };

    /// <summary>
    /// Sector Selected Popup hint line, contextual to <paramref name="obj"/> (per the user's own
    /// explicit request -- an earlier pass always showed every letter regardless of the highlighted
    /// item, which read as confusing/misleading for objects none of them actually apply to). D shows
    /// for your own world or your own fleet (the two cases <see cref="DeployFleet(ISectorObject)"/>
    /// itself directly supports); C/T/J/A only for your own fleet. Empty for anything else --
    /// <see cref="ListView{T}.ValueChanged"/> keeps this in sync as the highlighted item changes.
    /// </summary>
    private string FleetActionHint(ISectorObject? obj)
    {
        var isOwnedWorld = obj is IEconomicWorld world && ReferenceEquals(world.Owner, human);
        var isOwnedFleet = obj is Fleet fleet && ReferenceEquals(fleet.Owner, human);

        if (isOwnedFleet) {
            return "D:deploy  C:dest  T:transfer  J:abort/join  A:attack";
        }

        return isOwnedWorld ? "D:deploy" : "";
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
        ShowObjectPicker(string.Empty, objects, ShowCloseUp, allowFleetActions: true);

    /// <summary>
    /// Shared "pick one of these objects" popup -- DISPLAY.PAS's own GetIDMenuChoice/DisplayMenu, the
    /// primitive both MAPWIND.PAS's GetMapObject (2+ objects in one sector, <see cref="ShowSectorPicker"/>)
    /// and FLTCOMM.PAS's GetGround (<see cref="PickGround"/>, Transfer/Abort-Join/Refuel's own
    /// target picker) build on top of in real Pascal. "Name  (Owner)" display format matches both of
    /// those procedures' own AddGround/CreateMenu (MAPWIND.PAS:858-859, FLTCOMM.PAS:87-88) verbatim
    /// -- identical text in both sources, not a coincidence.
    /// </summary>
    /// <param name="allowFleetActions">
    /// Only true for <see cref="ShowSectorPicker"/> -- the other two callers (<see cref="PickGround"/>,
    /// Attack's own enemy-fleet target picker) already give Enter a specific meaning ("this is the
    /// transfer/abort/attack target"), so C/T/J/A must not double as fleet-command shortcuts there.
    /// </param>
    private void ShowObjectPicker(string title, IReadOnlyList<ISectorObject> objects, Action<ISectorObject> onChosen, bool allowFleetActions = false)
    {
        var picker = new Window {
            Title = title, // DisplayMenu's own OpenWindow passes '' for the sector-picker case too.
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = allowFleetActions ? 55 : 45, // wide enough for the fleet-action hint line below
            Height = allowFleetActions ? 8 : 7, // one hint line, contextual to the highlighted item
            BorderStyle = LineStyle.Single, // ThinBRD
            CanFocus = true,
        };
        picker.SetScheme(new Scheme(PickerNormalAttribute));
        picker.Border.View?.SetScheme(new Scheme(PickerBorderAttribute));

        // No in-window "Enter: choose  Esc: cancel" hint: real Pascal's own GetIDMenuChoice puts
        // that on the shared help line (WriteHelpLine) instead of inside the menu window itself --
        // not reproduced, same simplification depth as CloseUpWindow dropping its own former
        // "Press any key" line for the same reason (it isn't real Pascal content either). The
        // fleet-action hint line below is the one exception -- no Pascal equivalent exists to match,
        // so it gets an explicit legend instead of staying silent like Enter/Esc do.
        var listView = new ListView<ObjectListItem> { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(allowFleetActions ? 1 : 0) };
        listView.SetScheme(new Scheme { Normal = PickerNormalAttribute, Focus = PickerSelectedAttribute });
        listView.SetSource(new ObservableCollection<ObjectListItem>(objects.Select(o => new ObjectListItem(o, human))));
        listView.Index = 0; // SetSource alone leaves nothing selected -- default to the first item.
        if (allowFleetActions) {
            // ListView's own type-ahead search (KeystrokeNavigator) intercepts every plain letter
            // key inside its own OnKeyDown override, BEFORE the public KeyDown event ever fires --
            // confirmed by decompiling it (dotnet-inspect): it returns true even when nothing
            // matches, since GetNextMatchingItem's "no better match" result still counts as
            // handled. That swallows C/T/J/A silently. None of these object names ("Outpost",
            // "Fleet", "Warfleet"...) benefit from letter-search anyway, so disabling it here is a
            // clean fix, not a workaround for a real feature this popup needs.
            listView.KeystrokeNavigator = null;
        }
        picker.Add(listView);

        Label? hintLabel = null;
        if (allowFleetActions) {
            hintLabel = new Label { X = 0, Y = Pos.AnchorEnd(1), Text = FleetActionHint(listView.Value?.Object) };
            picker.Add(hintLabel);
            listView.ValueChanged += (_, _) => hintLabel.Text = FleetActionHint(listView.Value?.Object);
        }

        var dismiss = AddModal(picker);

        // Fleet-action letters are wired on listView's own KeyDown, not picker's: ListView has a
        // built-in type-ahead-search key binding that consumes a bare letter internally before it
        // ever bubbles up to the picker Window's KeyDown (confirmed by instrumenting both -- Enter/Esc
        // reach picker.KeyDown fine since ListView leaves those unhandled, but C/T/J/A never did).
        // View.KeyDown (the C# event) fires before a view's own internal key-binding table, so
        // attaching here pre-empts that search feature for exactly the four letters we care about.
        // Gated to exactly the letters FleetActionHint actually advertises for the highlighted item
        // -- offering a key the on-screen hint doesn't list would be the same "confusing" complaint
        // that made the hint contextual in the first place.
        if (allowFleetActions) {
            listView.KeyDown += (_, key) => {
                if (listView.Value?.Object is not { } obj) {
                    return;
                }

                var isOwnedWorld = obj is IEconomicWorld world && ReferenceEquals(world.Owner, human);
                var isOwnedFleet = obj is Fleet fleet && ReferenceEquals(fleet.Owner, human);
                var letter = char.ToUpperInvariant((char)key.AsRune.Value);

                if (letter == 'D' && (isOwnedWorld || isOwnedFleet)) {
                    dismiss();
                    DeployFleet(obj);
                    key.Handled = true;
                    return;
                }

                if (isOwnedFleet && ResolveFleetContextAction(letter) is { } action) {
                    dismiss();
                    action((Fleet)obj);
                    key.Handled = true;
                }
            };
        }

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
            ShowInfo(title, emptyMessage);
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
    /// <param name="refocusOnDismiss">
    /// Where to send focus once this popup closes, instead of the default <see cref="galaxyView"/> --
    /// used when this popup was stacked on top of a still-open background panel (see
    /// <see cref="AddCloseUpOverlay"/>) rather than opened straight off the map, so closing it hands
    /// keyboard control straight back to that panel instead of the map underneath everything.
    /// </param>
    private Action AddModal(View popup, bool dismissOnOutsideClick = true, View? refocusOnDismiss = null)
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
        // the popup's lifetime blocks that regardless of the exact internal mechanism. galaxyView
        // gets the same treatment for the same reason, found later: a popup whose own KeyDown handler
        // doesn't mark Tab as handled (Fleet Group Configuration) lets Terminal.Gui's default
        // focus-advance binding fire, which can hand focus to galaxyView -- a sibling child of this
        // Window, not gated by z-order either -- and its own arrow-key handler then moves the map
        // cursor instead of the popup's own grid, with no visible sign focus ever moved. A depth
        // counter, not a bare bool: AddModal isn't reentrant on its own (Deploy Fleet chains a
        // name-prompt popup straight into the distribution-editor popup), and a bare
        // `menuBar.Enabled = true` on Dismiss would re-enable the bar (and the map) the moment the
        // inner popup of two ever closed while the outer one was still up.
        openModalCount++;
        menuBar.Enabled = false;
        galaxyView.Enabled = false;

        void Dismiss()
        {
            Remove(popup);
            Remove(backdrop);
            openModalCount--;
            menuBar.Enabled = openModalCount == 0;
            galaxyView.Enabled = openModalCount == 0;
            (refocusOnDismiss ?? galaxyView).SetFocus();
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

    // Whichever "background panel" (Close Up/World Info, Status, Fleet, News, Empire, Names, Help) is
    // currently open, if any -- at most one of these is ever visible at once (per the user's own
    // explicit request: opening one used to stack it on top of whatever was already open, e.g. F8
    // over an open Close Up, rather than replacing it). Distinct from AddModal's own general stacking,
    // which every transient dialog/picker (MessageBox, ResourceDistributionEditor, the various pickers
    // chained off a panel) still uses unchanged -- those are meant to layer on top of a panel, not
    // replace it. openPanelWindow is also read by AddCloseUpOverlay below, to decide whether Close Up
    // should stack on top of an already-open panel instead of replacing it.
    private View? openPanelWindow;
    private Action? openPanelDismiss;

    private Action AddPanel(View popup, bool dismissOnOutsideClick = true)
    {
        openPanelDismiss?.Invoke();

        var dismiss = AddModal(popup, dismissOnOutsideClick);
        openPanelWindow = popup;
        Action wrapped = () => {
            dismiss();
            openPanelWindow = null;
            openPanelDismiss = null;
        };
        openPanelDismiss = wrapped;
        return wrapped;
    }

    /// <summary>
    /// Close Up's own entry point (both <see cref="CloseUpWindow"/> and <see cref="WorldInfoWindow"/>):
    /// per the user's own explicit request, picking a row in Status/Fleet/News/Empire/Names should
    /// return to that exact same window -- same scroll position, same highlighted row -- rather than
    /// dropping back to the bare map. Since none of those callers dismiss their own panel before
    /// calling this (their onSelect callbacks just call <see cref="ShowCloseUp"/> directly now), the
    /// panel is still sitting right there in <see cref="openPanelWindow"/>; stacking Close Up on top of
    /// it via a plain <see cref="AddModal"/> call (not <see cref="AddPanel"/>, which would wrongly
    /// dismiss it first) and refocusing it on dismiss is the entire mechanism -- Remove() never
    /// Disposes a view (confirmed by decompiling Terminal.Gui's own View.Remove), so the panel's own
    /// fields (StatusWindow's selection/scroll position, etc.) were never touched to begin with. Opened
    /// directly off the map (no panel open), this becomes the panel itself instead, via AddPanel,
    /// unchanged from before.
    /// </summary>
    private Action AddCloseUpOverlay(View window) =>
        openPanelWindow is { } panel ? AddModal(window, refocusOnDismiss: panel) : AddPanel(window);

    /// <summary>
    /// Styled "press any key" notice (<see cref="DosDialogWindow"/>), added via <see cref="AddModal"/>
    /// rather than <see cref="DosDialogWindow.ShowInfo"/>'s own blocking nested <c>Application.Run</c>
    /// -- found the hard way (live-tested, not guessed): a nested <c>Run</c> never touches this
    /// Window's own <c>Focused</c> child, so <see cref="galaxyView"/> stays focused throughout, and
    /// this constructor's own Esc-opens-the-Game-menu shortcut (<c>KeyDown</c>, above) steals Esc
    /// before the nested dialog's own handler ever sees it. <c>AddModal</c> doesn't have this problem
    /// -- it calls <c>popup.SetFocus()</c>, which is what <see cref="DosMessageWindow"/> (the
    /// Attack-specific version of this same idea) already relied on. <see cref="DosDialogWindow.ShowInfo"/>'s
    /// static blocking form stays right for contexts with no such shortcut to collide with
    /// (<c>Program.cs</c>'s top-level script, <c>AnacreonTitleWindow</c>, <c>TacticalBattleDisplayWindow</c>'s
    /// own already-focus-redirected popup).
    /// </summary>
    private void ShowInfo(string title, string body)
    {
        var dialog = new DosDialogWindow(title, body);
        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);
        dialog.Answered += (_, _) => dismiss();
    }

    /// <summary>
    /// Styled Yes/No/Esc confirm (<see cref="DosDialogWindow"/>), AddModal-based for the same reason
    /// as <see cref="ShowInfo"/>. <paramref name="onAnswered"/> gets the same null/0/1 shape
    /// <c>MessageBox.Query</c> always returned (null = Esc/cancel, 0 = Yes, 1 = No), so a caller
    /// converting from that just wraps its old post-call branching in this lambda instead.
    /// </summary>
    private void ShowConfirm(string title, string body, Action<int?> onAnswered)
    {
        var dialog = new DosDialogWindow(title, body, isConfirm: true);
        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);
        dialog.Answered += (_, _) => {
            dismiss();
            onAnswered(dialog.ButtonIndex);
        };
    }

    /// <summary>
    /// Display wrapper for <see cref="ShowSectorPicker"/>'s ListView -- ISectorObject implementors
    /// are plain domain entities with no display-formatting concern of their own. Text format
    /// matches GetMapObject's own CreateMenu (MAPWIND.PAS:858-859): "Name  (Owner)".
    /// </summary>
    private sealed record ObjectListItem(ISectorObject Object, Empire Viewer)
    {
        public override string ToString() =>
            $"{Object.Names.GetValueOrDefault(Viewer) ?? CloseUpWindow.DescribeLocation(Object, Viewer)}  ({Object.Owner.Name})";
    }

    /// <summary>
    /// Fleet menu > Deploy (FLTCOMM.PAS: LaunchFleetCommand). Prompt order transcribed from
    /// PLAYTURN.PAS's own <c>ParameterData</c> table (:209-218) — the real per-command parameter
    /// list for <c>FLaunchCom</c> is <c>NewNameParm, IDParm2 (source), XYParm (destination)</c>, in
    /// that fixed order (real Pascal asks whichever of these wasn't already typed on the command
    /// line, in this order); <c>LaunchFleetCommand</c> itself then calls <c>InputNewDistribution</c>
    /// (the Resource Distribution Editor) last, before actually deploying. An earlier pass here had
    /// source before the name and put the editor before the destination pick — fixed to match source:
    /// name, then source, then destination, then composition. Source and destination both reuse the
    /// map cursor (matching TUI_SURFACES_MAPPING.md's own "map cursor reuse for launch/destination").
    /// </summary>
    private void DeployFleet() => PromptForFleetName(knownSource: null);

    /// <summary>
    /// Contextual Deploy from the Sector Selected Popup or Close Up: when <paramref name="contextObject"/>
    /// is one of the player's own fleets, that fleet is the deploy source itself -- splitting off a
    /// new fleet from it, no map-cursor source pick needed. <see cref="FleetLifecycle.DeployFleet"/>
    /// accepts a Fleet source generically (its own doc comment: "must be a Fleet or IEconomicWorld"),
    /// same primitive NpeToolkit's own Deploy*Fleet procedures already use, just not previously
    /// reachable by the player. For anything else, falls back to whatever world is at
    /// <paramref name="contextObject"/>'s own <see cref="ISectorObject.Location"/>, same as picking
    /// that world directly. Still asks the fleet name first regardless, matching
    /// <c>LaunchFleetCommand</c>'s own real parameter order (name before source is ever validated).
    /// </summary>
    private void DeployFleet(ISectorObject contextObject) => PromptForFleetName(contextObject);

    private static readonly TgAttribute DialogNormalAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7, matching the sector picker's own popup style
    private static readonly TgAttribute DialogBorderAttribute = new(StandardColor.LightGray, StandardColor.Black);

    // FleetName (FLTCOMM.PAS's own LaunchFleetCommand parameter, Question 7 "What name shall we use
    // for this fleet?") -- a small text prompt, matching PlayerSetupWindow's own established
    // TextField-in-a-popup pattern rather than a nested Application.Run.
    private void PromptForFleetName(ISectorObject? knownSource)
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
            if (knownSource is Fleet ownFleet && ReferenceEquals(ownFleet.Owner, human)) {
                ValidateDeploySource(ownFleet, name);
            } else if (knownSource is not null) {
                PickDeploySource(knownSource.Location, name);
            } else {
                BeginPick("Deploy Fleet -- move cursor to a world to launch from, Enter: select, Esc: cancel",
                    pickedLocation => PickDeploySource(pickedLocation, name));
            }
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

    // IDParm2 (Question 8, "Where shall we deploy the fleet from?") -- the map-cursor and
    // world-under-a-selected-object paths both land here.
    private void PickDeploySource(Coordinate location, string fleetName)
    {
        var source = FindWorldAt(location);
        if (source is null || !ReferenceEquals(source.Owner, human)) {
            ShowInfo("Deploy Fleet", "That isn't one of your own worlds.");
            return;
        }

        ValidateDeploySource(source, fleetName);
    }

    // Shared source validation for both a world (PickDeploySource) and a player-owned fleet
    // (DeployFleet(ISectorObject)'s own fleet-as-source branch).
    private void ValidateDeploySource(ISectorObject source, string fleetName)
    {
        if (!HasAnyShips(((IShipCargoHolder)source).Ships)) {
            ShowInfo("Deploy Fleet", "There are no ships here to deploy.");
            return;
        }

        // XYParm (Question 9, "What shall its destination be?").
        BeginPick("Deploy Fleet -- move cursor to destination, Enter: select, Esc: cancel",
            destination => BeginDeployDistribution(source, fleetName, destination));
    }

    private void BeginDeployDistribution(ISectorObject source, string fleetName, Coordinate destination)
    {
        var holder = (IShipCargoHolder)source;
        var groundShips = CloneShips(holder.Ships);
        var groundCargo = CloneCargo(holder.Cargo);
        var fleetShips = new ShipCounts();
        var fleetCargo = new CargoHold();
        var sourceName = source.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeLocation(source, human);

        var editor = new ResourceDistributionEditor(
            fleetShips, fleetCargo, groundShips, groundCargo,
            groundIsPlayerOwned: true, groundIsAFleet: source is Fleet,
            title: $"Deploy Fleet from {sourceName}");
        var dismiss = AddModal(editor, dismissOnOutsideClick: false);

        editor.Committed += (_, _) => {
            dismiss();

            // NoShips (MISC.PAS) -- LaunchFleetCommand's own "IF NOT NoShips(FltSh)" guard: nothing
            // was actually put aboard, so there's nothing to deploy.
            if (!HasAnyShips(fleetShips)) {
                return;
            }

            var fleet = FleetLifecycle.DeployFleet(human, holder, fleetShips, fleetCargo, destination, game);
            if (!string.IsNullOrWhiteSpace(fleetName)) {
                // LaunchFleetCommand's own FleetName[1]:=UpCase(FleetName[1]) (FLTCOMM.PAS:517).
                fleet.Names[human] = char.ToUpperInvariant(fleetName[0]) + fleetName[1..];
            }

            galaxyView.Refresh();
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
    private void TransferFleet() => PickOwnFleetAtCursor("Transfer Fleet", TransferFleet);

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
        var fleetName = fleet.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeLocation(fleet, human);
        var groundName = ground.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeLocation(ground, human);

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
    private void AbortJoinFleet() => PickOwnFleetAtCursor("Abort/Join Fleet", AbortJoinFleet);

    private void AbortJoinFleet(Fleet fleet) =>
        PickGround(fleet, playerOnly: false, includeFleet: false, "Abort/Join Fleet",
            "There is nothing here to abort the fleet to.",
            ground => ConfirmAbortJoin(fleet, ground));

    private void ConfirmAbortJoin(Fleet fleet, ISectorObject ground)
    {
        if (!ReferenceEquals(ground.Owner, human)) {
            var groundName = ground.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeLocation(ground, human);
            ShowConfirm("Abort/Join Fleet", $"{groundName} is not part of your empire. Are you sure you want to abort the fleet?", choice => {
                if (choice == 0) {
                    ConfirmAbortJoinOverflow(fleet, ground);
                }
            });
            return;
        }

        ConfirmAbortJoinOverflow(fleet, ground);
    }

    private void ConfirmAbortJoinOverflow(Fleet fleet, ISectorObject ground)
    {
        var groundHolder = (IShipCargoHolder)ground;
        var overflow = Enum.GetValues<ShipType>().Any(t => groundHolder.Ships[t] + fleet.Ships[t] > ResourceDistribution.MaxResources);
        if (overflow) {
            ShowConfirm("Abort/Join Fleet", "An object cannot hold so many ships -- some will be lost. Are you sure?", choice => {
                if (choice == 0) {
                    FleetLifecycle.AbortFleet(fleet, groundHolder, game);
                    galaxyView.Refresh();
                }
            });
            return;
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
            ShowInfo("Refuel Fleet", "There is no trillum available to refuel with.");
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
    /// Fleet menu > Change Destination (FLTCOMM.PAS: ChangeDestinationCommand, :614-630): reuses the
    /// same map-cursor destination pick Deploy's own XYParm step already uses. Real Pascal's
    /// SetFleetDestination is unconditional -- no legality check on the new destination beyond "a
    /// coordinate" -- matching FleetLifecycle.SetFleetDestination exactly (works whether the fleet is
    /// Ready or already InTransit).
    /// </summary>
    private void ChangeDestination() => PickOwnFleetAtCursor("Change Destination", ChangeDestination);

    private void ChangeDestination(Fleet fleet) =>
        BeginPick("Change Destination -- move cursor to new destination, Enter: select, Esc: cancel",
            destination => {
                FleetLifecycle.SetFleetDestination(fleet, destination);
                galaxyView.Refresh();
            });

    /// <summary>
    /// Fleet menu > Orders (FLTCOMM.PAS: FleetOrdersCommand): opens <see cref="FleetOrdersWindow"/> on
    /// the selected fleet -- see that class's own doc comment for the compile/commit flow.
    /// </summary>
    private void FleetOrders() => PickOwnFleetAtCursor("Orders", FleetOrders);

    private void FleetOrders(Fleet fleet)
    {
        var window = new FleetOrdersWindow(fleet, game);
        var dismiss = AddModal(window, dismissOnOutsideClick: false);

        window.Closed += (_, message) => {
            dismiss();
            if (message is not null) {
                ShowInfo("Orders", message);
            }
        };
    }

    /// <summary>
    /// Fleet menu > Cancel Orders (FLTCOMM.PAS: FleetCancelOrdersCommand, :917-931) -- no window at
    /// all, matching real Pascal's own body exactly: clear the queue, zero the resume cursor, report.
    /// </summary>
    private void CancelFleetOrders() => PickOwnFleetAtCursor("Cancel Orders", CancelFleetOrders);

    private void CancelFleetOrders(Fleet fleet)
    {
        fleet.Orders.Clear();
        fleet.NextOrder = 0;
        var fleetName = CloseUpWindow.DescribeLocation(fleet, human);
        ShowInfo("Cancel Orders", $"All orders to {fleetName} cancelled, {MyLord()}.");
    }

    /// <summary>
    /// Fleet menu > SRM Sweep (FLTCOMM.PAS: MineSweeperCommand, :788-812): reads the mine at the
    /// selected fleet's own location (<c>GetCoord(FltID,XY)</c>), same as real Pascal -- there's no
    /// separate destination pick, the fleet has to already be sitting on the minefield.
    /// <c>PutMine(XY,Indep)</c> is <see cref="Galaxy.ClearMine"/> (Indep-owned and unmined are the
    /// same "no mine" state on this side -- see <see cref="Galaxy.GetMineOwner"/>'s own doc comment);
    /// <c>AddNews</c> only fires when the mine belonged to someone else, matching the source's own
    /// <c>IF Emp&lt;&gt;Player THEN AddNews(...)</c> guard.
    /// </summary>
    private void SrmSweep() => PickOwnFleetAtCursor("SRM Sweep", SrmSweep);

    private void SrmSweep(Fleet fleet)
    {
        var location = fleet.Location;
        var mineOwner = game.Galaxy.GetMineOwner(location);
        if (mineOwner is null) {
            ShowInfo("SRM Sweep", "No SRMs found.");
            return;
        }

        if (!ReferenceEquals(mineOwner, human)) {
            mineOwner.AddNews(NewsType.MineFieldCleared, position: location, otherEmpire: human);
        }

        game.Galaxy.ClearMine(location);
        game.Galaxy.ClearMineScouted(location);
        galaxyView.Refresh();
        ShowInfo("SRM Sweep", "Mine sweeping completed.");
    }

    /// <summary>
    /// Fleet menu > Probe (FLTCOMM.PAS: LaunchProbeCommand, :761-786): unlike every other Fleet-menu
    /// command, real Pascal never ties this to a specific fleet (<c>GetProbe</c>/<c>LaunchProbe</c>
    /// take no FltID at all) -- just a destination coordinate, reusing the map cursor the same way
    /// Deploy's own destination pick does, with no source-fleet step first.
    /// </summary>
    private void LaunchProbe()
    {
        if (human.ProbesInTransit.Count >= Empire.MaxProbesInTransit) {
            ShowInfo("Probe", "There are no more probes available.");
            return;
        }

        BeginPick("Launch Probe -- move cursor to target, Enter: select, Esc: cancel", destination => {
            var probeNumber = human.ProbesInTransit.Count + 1;
            human.TryLaunchProbe(destination);
            ShowInfo("Probe", $"Probe {probeNumber} of {Empire.MaxProbesInTransit} sent.");
        });
    }

    /// <summary>
    /// Ministry of War menu > Attack (ATTCOMM.PAS: GetTarget/AttackCommand/CleanUp/EnemyConquered).
    /// Target selection order is transcribed directly from GetTarget's own nested CreateMenu
    /// (ATTCOMM.PAS:663-715): every enemy Fleet in the sector <see cref="Game.Scouted"/> by the
    /// player goes on the target list first (a picker if there's more than one) -- CreateMenu's own
    /// <c>IF (Status&lt;&gt;Player) AND Scouted(Player,Target)</c> guard, missed in an earlier pass
    /// here (dropped the Scouted half, so an unscouted enemy fleet the player couldn't even see on
    /// the map still got attacked instead of the world under it) -- and the world itself
    /// (Planet/Starbase) is only ever offered "if no fleets" (ListSize=0, no Scouted check of its
    /// own) -- a world defended by any (scouted) enemy fleet cannot be attacked directly, the
    /// fleet(s) must be dealt with first. Confirmed with the user after an initial pass got this
    /// backwards (checked the world before any defending fleet) and produced a wildly wrong result:
    /// an undamaged 50-ship Kingdom defense fleet sat next to a heavily-refortified capital, and
    /// attacking the capital directly wiped out a 3000-ship human fleet against a world whose own war
    /// economy had ballooned during the several turns the attack took to arrive. MVP gate, called out
    /// rather than silent: attacker and target must already be in the same sector (i.e. the fleet has
    /// arrived) -- real Pascal's exact range rule wasn't re-derived here.
    /// </summary>
    private void Attack() => PickOwnFleetAtCursor("Attack", Attack);

    private void Attack(Fleet attacker) => FindAttackTarget(attacker, target => BeginAttack(attacker, target));

    /// <summary>GetTarget (ATTCOMM.PAS:663-715), shared by both AttackCommand and AutoAttackCommand.</summary>
    private void FindAttackTarget(Fleet attacker, Action<object> onTargetFound)
    {
        var cursor = attacker.Location;
        var enemyFleets = game.Galaxy.Fleets.Where(f => f.Location == cursor && !ReferenceEquals(f.Owner, human) && Game.Scouted(human, f)).ToList();

        if (enemyFleets.Count > 1) {
            ShowObjectPicker("Attack", enemyFleets.Cast<ISectorObject>().ToList(), onTargetFound);
            return;
        }

        object? target = enemyFleets.Count == 1 ? enemyFleets[0]
            : (object?)game.Galaxy.Planets.FirstOrDefault(p => p.Location == cursor && !ReferenceEquals(p.Owner, human))
              ?? game.Galaxy.Starbases.FirstOrDefault(s => s.Location == cursor && !ReferenceEquals(s.Owner, human));

        if (target is null) {
            ShowInfo("Attack", "No enemy target in this sector.");
            return;
        }

        onTargetFound(target);
    }

    private void AutoAttack() => PickOwnFleetAtCursor("Auto Attack", AutoAttack);

    private void AutoAttack(Fleet attacker) => FindAttackTarget(attacker, target => BeginAutoAttack(attacker, target));

    /// <summary>
    /// AutoAttackCommand (ATTCOMM.PAS:1640-1746): same target pick as Attack, but skips Fleet Group
    /// Configuration/Tactical Battle Display entirely -- one confirm, then the whole engagement
    /// resolves in a single call to <see cref="CombatResolution.NPEAttack"/>, the same headless engine
    /// the Kingdom AI itself already uses (DefaultDistribution, hardcoded <see
    /// cref="AttackIntentionType.Conquer"/> -- Pascal's own hardcoded ConquerAIT). No
    /// OldShipsFound/AskToCapture/scenario background text here: NPEAttack already runs ResolveAttack
    /// internally with Capture hardcoded true, matching AutoAttackCommand's own plain ResultMessage +
    /// CasualtyReport pair rather than CleanUp's fuller EnemyConquered flow. The random "commander
    /// fought bravely"/"perhaps if you had been there" flavor line on a wipeout is dropped, same
    /// precedent as AskToCapture's own flavor text.
    /// </summary>
    private void BeginAutoAttack(Fleet attacker, object target)
    {
        var subject = (ISectorObject)target;
        ShowConfirm("Auto Attack", $"{DisplayName(attacker)} ready to attack {DisplayName(subject)}.\nGive confirmation order?", choice => {
            if (choice != 0) {
                return;
            }

            var before = new ShipCounts();
            foreach (var shipType in Enum.GetValues<ShipType>()) {
                before[shipType] = attacker.Ships[shipType];
            }

            var engagement = CombatResolution.NPEAttack(human, attacker, target, AttackIntentionType.Conquer, game, random);
            galaxyView.Refresh();

            var casualties = Enum.GetValues<ShipType>().Select(t => $"{ShipThingName(t)}: {Math.Max(0, before[t] - attacker.Ships[t])}");
            var report = $"{AutoAttackResultText(engagement.Result, subject)}\n\nCasualties:\n{string.Join('\n', casualties)}";
            ShowInfo("Auto Attack", report);
        });
    }

    // ThingNames (DATACNST.PAS:100-112), fgt..trn only -- same names as EmpireStatusWindow's own copy.
    private static string ShipThingName(ShipType ship) => ship switch {
        ShipType.Fighter => "fighter squadrons",
        ShipType.HunterKiller => "hunter-killers",
        ShipType.Jumpship => "jumpships",
        ShipType.Jumptransport => "jumptransports",
        ShipType.Penetrator => "penetrators",
        ShipType.Starship => "starships",
        ShipType.Transport => "transports",
        _ => throw new ArgumentOutOfRangeException(nameof(ship)),
    };

    /// <summary>
    /// Ministry of War > Launch LAMs (DESIGN.PAS: LaunchLAM, :48-261). The launching world (BaseID)
    /// resolution matches PLAYTURN.PAS's own ParameterData row for LAMCom (ErrorCond: NotPartOfEmp,
    /// NotAWorld, NoLAMs -- one of the player's own worlds, gated on actually having LAMs) via
    /// <see cref="FindWorldAt"/>, the same helper Close Up/Deploy already use for "the world at this
    /// coordinate." GetTarget's own nested target list (:68-180) is transcribed directly below rather
    /// than reusing <see cref="FindAttackTarget"/>: its filter (Known, not Scouted; Distance&lt;=5 of
    /// the launching world, not "same sector as the attacking fleet") is a genuinely different rule.
    /// </summary>
    private void LaunchLams()
    {
        var source = FindWorldAt(galaxyView.CursorLocation);
        if (source is null || !ReferenceEquals(source.Owner, human) || source.Defenses[DefenseType.Lam] <= 0) {
            ShowInfo("Launch LAMs", "Move the cursor onto one of your own worlds with LAMs first.");
            return;
        }

        var baseLocation = source.Location;
        var targets = game.Galaxy.Fleets.Cast<ISectorObject>()
            .Concat(game.Galaxy.Planets.Cast<ISectorObject>())
            .Concat(game.Galaxy.Starbases.Cast<ISectorObject>())
            .Where(o => Game.Known(human, o) && !ReferenceEquals(o.Owner, human) && baseLocation.DistanceTo(o.Location) <= 5)
            .ToList();

        if (targets.Count == 0) {
            ShowInfo("Launch LAMs", $"No targets can be reached from {DisplayName(source)}.");
            return;
        }

        ShowObjectPicker("Launch LAMs", targets, target => PromptForLamCount(source, target));
    }

    // GetTarget's own InputIntegerDisplayScreen prompt (DESIGN.PAS:201-209): a positive number no
    // greater than the base's own LAM count, re-prompting (not closing) on an out-of-range answer --
    // same REPEAT...UNTIL-as-retry-loop idiom as PromptForTrillum's own amountField handler.
    private void PromptForLamCount(IEconomicWorld source, ISectorObject target)
    {
        var maxLams = source.Defenses[DefenseType.Lam];
        var sourceName = DisplayName(source);
        var targetName = DisplayName(target);

        var dialog = new Window {
            Title = "Launch LAMs",
            X = Pos.Center(), Y = Pos.Center(),
            Width = 60, Height = 7,
            BorderStyle = LineStyle.Single,
            CanFocus = true,
        };
        dialog.SetScheme(new Scheme(DialogNormalAttribute));
        dialog.Border.View?.SetScheme(new Scheme(DialogBorderAttribute));

        var amountField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1) };
        var errorLabel = new Label { X = 1, Y = 3 };
        dialog.Add(new Label { X = 1, Y = 0, Text = $"{sourceName} targeting {targetName}." });
        dialog.Add(new Label { X = 1, Y = 1, Text = $"There are {maxLams} LAMs here.  Launch how many?" });
        dialog.Add(amountField);
        dialog.Add(errorLabel);
        dialog.Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter: confirm   Esc: cancel" });

        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);
        amountField.SetFocus();

        amountField.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Enter) {
                return;
            }
            key.Handled = true;

            var text = amountField.Text?.Trim() ?? "";
            if (!int.TryParse(text, out var amount) || amount < 0) {
                errorLabel.Text = $"You must use a positive number, {MyLord()}!";
                return;
            }
            if (amount > maxLams) {
                errorLabel.Text = $"There aren't that many LAMs at {sourceName}, {MyLord()}.";
                return;
            }

            dismiss();
            FinishLaunchLams(source, target, amount);
        };
        dialog.KeyDown += (_, key) => {
            if (key.NoAlt.NoCtrl.NoShift.KeyCode != KeyCode.Esc) {
                return;
            }

            dismiss();
            key.Handled = true;
        };
    }

    // LaunchLAM's own tail (DESIGN.PAS:211-247): CombatStandalone.LAMAttack already applies the
    // outcome (news, fleet destruction, defense reduction) the same way NpeToolkit's own LAM strikes
    // do -- only the base's own LAM count and the casualty report are this command's responsibility.
    private void FinishLaunchLams(IEconomicWorld source, ISectorObject target, int lamsToUse)
    {
        var lines = new List<string>();
        if (lamsToUse > 0) {
            var (shipsDestroyed, defensesDestroyed) = CombatStandalone.LAMAttack(human, lamsToUse, (IShipCargoHolder)target, game);
            source.Defenses[DefenseType.Lam] -= lamsToUse;
            galaxyView.Refresh();

            if (target is Fleet) {
                lines.AddRange(Enum.GetValues<ShipType>().Where(t => shipsDestroyed[t] > 0).Select(t => $"{shipsDestroyed[t]} {ShipThingName(t)} were destroyed."));
            } else {
                lines.AddRange(Enum.GetValues<DefenseType>().Where(t => defensesDestroyed[t] > 0).Select(t => $"{defensesDestroyed[t]} {DefenseThingName(t)} were destroyed."));
            }
        }

        if (lines.Count == 0) {
            lines.Add(target is Fleet ? "No ships were destroyed." : "No defenses were destroyed.");
        }

        ShowInfo("Launch LAMs", string.Join('\n', lines));
    }

    // ThingNames (DATACNST.PAS:100-112), LAM..ion only -- same spelling TacticalBattleDisplayWindow's
    // own TypeName already settled on ("def. satellites"/"ion cannons", not Pascal's raw "defense
    // satellites"/"ion canons").
    private static string DefenseThingName(DefenseType defense) => defense switch {
        DefenseType.Lam => "LAMs",
        DefenseType.DefenseSatellite => "def. satellites",
        DefenseType.Gdm => "GDMs",
        DefenseType.IonCannon => "ion cannons",
        _ => throw new ArgumentOutOfRangeException(nameof(defense)),
    };

    // Ministry of War > Defenses (MSCCOMM.PAS: DefenseCommand) -- see DefensesEditor's own doc
    // comment for the grid itself; this just opens it over the player's own DefenseSettings.Fleets
    // the same way BeginAttack opens FleetGroupConfigurationWindow.
    private void Defenses()
    {
        var editor = new DefensesEditor(human.DefenseSettings.Fleets);
        var dismiss = AddModal(editor, dismissOnOutsideClick: false);
        editor.Done += (_, _) => dismiss();
    }

    // ResultMessage (ATTCOMM.PAS:1652-1686) -- only these three cases are ever reached (DefCapturedART
    // is declared but never assigned anywhere in real Pascal, see CombatOutcome.cs's own note).
    private string AutoAttackResultText(AttackResultType result, ISectorObject subject) => result switch {
        AttackResultType.AttackerDestroyed => $"I'm sorry, {MyLord()}, the entire attack force has been destroyed.",
        AttackResultType.AttackerRetreats => $"I'm sorry, {MyLord()}, the fleet was forced to retreat.",
        AttackResultType.DefenderConquered => subject is Fleet
            ? $"The enemy fleet has been destroyed, {MyLord()}."
            : SovereigntyDeclaration(subject),
        _ => $"Result: {result}",
    };

    // AttackCommand's own "Standard battle configuration (Y/n)?" fork (ATTCOMM.PAS:1608-1616): Y
    // (default) skips Fleet Group Configuration and uses DefaultDistribution, matching what this
    // command always did before this screen existed; N opens the real GetGroups-equivalent screen.
    // Esc abandons the attack entirely (IF Ans<>EscKey), matching AttackCommand's own guard.
    //
    // Port-only addition, no Pascal equivalent, per the user's own explicit request: A jumps straight
    // to BeginAutoAttack instead -- same single "ready to attack... confirm?" dialog Ministry of War's
    // own Auto Attack menu item already shows, not a second layer on top of this one. Not using the
    // shared ShowConfirm/DosDialogWindow(isConfirm:true) convenience here since A must NOT become a
    // third button on every other Yes/No/Esc confirm in the app -- constructed directly instead, with
    // a second KeyDown subscriber layered on top for just this one dialog (the dialog's own internal
    // handler leaves A unhandled -- neither Y/N/Enter/Esc -- so it still reaches this one). hint: shows
    // the extra key on this one dialog's own legend without changing DosDialogWindow's shared default.
    private void BeginAttack(Fleet attacker, object target)
    {
        var dialog = new DosDialogWindow("Attack", "Standard battle configuration?", isConfirm: true,
            hint: "(Y)es / (N)o / (A)uto   Esc: cancel");
        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);

        dialog.KeyDown += (_, key) => {
            if (char.ToUpperInvariant((char)key.AsRune.Value) != 'A') {
                return;
            }

            dismiss();
            key.Handled = true;
            BeginAutoAttack(attacker, target);
        };

        dialog.Answered += (_, _) => {
            dismiss();

            if (dialog.ButtonIndex is not { } choice) {
                return;
            }

            if (choice == 0) {
                StartEngagement(attacker, target, CombatEngine.DefaultDistribution(attacker));
                return;
            }

            var configWindow = new FleetGroupConfigurationWindow(attacker.Ships, attacker.Cargo);
            var dismissConfig = AddModal(configWindow, dismissOnOutsideClick: false);
            configWindow.Committed += (_, _) => {
                dismissConfig();
                StartEngagement(attacker, target, [.. configWindow.Groups]);
            };
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
            // Refresh (data rebuild + its own SetNeedsDraw, see that method's own doc comment) before
            // the outcome dialogs below, not after -- otherwise the map sat behind those dialogs still
            // showing whatever it looked like before the whole battle, dismissed popup or not, until
            // every dialog closed and this line was finally reached.
            galaxyView.Refresh();
            ApplyAttackOutcome(attacker, target, state);
        };
    }

    // CleanUp (ATTCOMM.PAS:1564-1588): RestoreCombatant for both sides first, then (only on a
    // successful conquest) OldShipsFound/AskToCapture/EnemyConquered's own DisplayBackground call --
    // all three run *before* ResolveAttack, which is what actually reassigns ownership (ConquerWorld).
    // Calling FindWorldBackgroundText(conquer:true) here, before ResolveAttack, is load-bearing: its
    // 'A:' condition reads the target's *current* (pre-conquest) owner, so calling it after would mean
    // it can never match (see docs/ROADMAP.md and ScenarioLoaderWorldBackgroundTests for the ordering
    // bug this fixed).
    //
    // Split into a continuation chain (ShowOldShipsFound -> the capture confirm -> FinishAttackOutcome)
    // rather than the straight-line sequence this used to be: DosMessageWindow is a real AddModal popup
    // (Terminal.Gui's own MessageBox has no color/scheme API at all, confirmed via dotnet-inspect, so it
    // couldn't give these screens DOS-accurate colors). AddModal's popups are event-driven, not blocking
    // -- a later pass (DosDialogWindow, used everywhere else this same MessageBox-styling gap existed)
    // showed a hand-rolled nested Application.Run works fine for a single confirm, but this specific
    // chain -- three dialogs where whether the second and third even appear depends on the first two's
    // own answers -- reads more clearly as a callback chain than three sequential blocking calls
    // threaded through shared locals, so it keeps the callback style Deploy Fleet/Fleet Group
    // Configuration already use for the same reason.
    private void ApplyAttackOutcome(Fleet attackerFleet, object target, InteractiveCombatState state)
    {
        var subject = (ISectorObject)target;
        var hkSurprise = CombatEngine.ForcesUnknown(attackerFleet, subject.Owner);

        CombatOutcome.RestoreCombatant(attackerFleet, state.Casualties);
        CombatOutcome.RestoreCombatant(target, state.Killed);

        if (state.Result != AttackResultType.DefenderConquered) {
            var report = state.Result == AttackResultType.AttackerRetreats
                ? Retreated()
                : BattleLostMessage((ISectorObject)target);
            FinishAttackOutcome(attackerFleet, target, state, hkSurprise, capture: true, report);
            return;
        }

        ShowOldShipsFound(target, () => {
            if (target is Fleet targetFleet && HasAnyShips(targetFleet.Ships)) {
                // AskToCapture's own body (ATTCOMM.PAS:1349-1356) and its inverted polarity
                // (:1358-1380): "Y" (destroy) sets Capture:=False; anything else -- the default,
                // including a bare Enter -- sets Capture:=True. The random "commander begs for his
                // life" flavor line (:1358-1372) is skipped -- it costs an extra Rnd(1,3) call this
                // port would need to place very carefully relative to ConquestMessage's own Rnd(1,3)
                // (EnemyConquered's later, separate draw for Message1/2/3), and it's flavor text, not
                // content.
                var captured = Enum.GetValues<ShipType>()
                    .Where(t => targetFleet.Ships[t] > 0)
                    .Select(t => $"{targetFleet.Ships[t]} {t}");
                var body = $"You have captured:\n{string.Join('\n', captured)}";
                var confirm = new DosMessageWindow(game, human, body, confirmDestroy: true);
                var dismissConfirm = AddModal(confirm, dismissOnOutsideClick: false);
                confirm.Answered += (_, _) => {
                    dismissConfirm();
                    // Deferred one tick (see DosMessageWindow's own doc comment): without this, opening
                    // the next popup happens synchronously inside the very key dispatch that just
                    // dismissed this one, and a fast-repeated key (mashing Enter through a chain of
                    // dialogs, or psmux sending keys back-to-back) can land on the new popup before the
                    // player ever sees it -- silently picking Capture's default for a choice they never
                    // actually saw.
                    App!.AddTimeout(TimeSpan.Zero, () => {
                        FinishConquest(attackerFleet, subject, target, state, hkSurprise, capture: !confirm.Destroy);
                        return false;
                    });
                };
            } else {
                FinishConquest(attackerFleet, subject, target, state, hkSurprise, capture: true);
            }
        });
    }

    private void FinishConquest(Fleet attackerFleet, ISectorObject subject, object target, InteractiveCombatState state, bool hkSurprise, bool capture)
    {
        var report = Game.FindWorldBackgroundText(game, subject, human, conquer: true) is { } lines
            ? string.Join('\n', lines)
            : ConquestMessage(attackerFleet, subject);

        FinishAttackOutcome(attackerFleet, target, state, hkSurprise, capture, report);
    }

    private void FinishAttackOutcome(Fleet attackerFleet, object target, InteractiveCombatState state, bool hkSurprise, bool capture, string report)
    {
        CombatOutcome.ResolveAttack(state.Result, attackerFleet, target, hkSurprise, capture, state.Casualties, state.Killed, game, random);

        var result = new DosMessageWindow(game, human, report);
        var dismissResult = AddModal(result, dismissOnOutsideClick: false);
        result.Answered += (_, _) => dismissResult();
    }

    // Retreated (ATTCOMM.PAS:1324-1336) -- CleanUp's own AttackerRetreats branch. Real Pascal's doc
    // comment says "Revolution is added, though not as much as if the ships had been destroyed" --
    // that's ResolveAttack's own job (ChangeTotalRevIndex), already called right before this text is
    // shown; nothing else to port here, the message itself is a single fixed line.
    private string Retreated() => $"The attacking force has retreated, {MyLord()}.";

    // BattleLost (ATTCOMM.PAS:1224-1322) -- CleanUp's own AttackerDestroyed branch: a flavor message
    // picked at random from 4 variants, with different odds against an Independent target (1/12, 2/12,
    // 10/12 for Message1/2/4 -- Message3 never fires, it needs another *empire* to name) versus a real
    // empire (1/4 each). Same "no re-roll on redraw" simplification this file's other flavor pickers
    // already use (ConquestMessage, NewsWindow's LackArticle) -- there's no golden-file/replay
    // requirement for on-screen flavor text the way there is for Core simulation RNG.
    private string BattleLostMessage(ISectorObject subject)
    {
        var messageNumber = subject.Owner.IsIndependent
            ? PascalMath.Rnd(random, 1, 12) switch { 1 => 1, 2 => 2, _ => 4 }
            : PascalMath.Rnd(random, 1, 4);

        return messageNumber switch {
            1 => BattleLostMessage1(),
            2 => BattleLostMessage2(),
            3 => BattleLostMessage3(),
            _ => BattleLostMessage4(),
        };
    }

    // Message1 (ATTCOMM.PAS:1233-1266): fixed text, plus -- if the attacker's own most-restless world
    // exceeds RevIndex 20 -- a named callout naming it. World<>'' (Pascal's own guard for "no owned
    // planets at all") is just "does the player own any planets" here, since DisplayName always returns
    // something real, unlike ObjectName on an invalid ID.
    private string BattleLostMessage1()
    {
        var ownWorlds = game.Galaxy.Planets.Where(p => p.Owner == human).ToList();
        var mostRestless = ownWorlds.Count > 0 ? ownWorlds.MaxBy(p => p.RevolutionIndex) : null;
        var callout = mostRestless is not null && mostRestless.RevolutionIndex > 20
            ? $"\nDo not forget that {DisplayName(mostRestless)} is quickly growing doubtful of the Empire's\nability to defend itself.  "
            : "";

        return $"I'm sorry, {MyLord()}, the entire attack force has been destroyed.\n" +
            "I hope I do not have to remind you about the repercussion that this\n" +
            "loss will have.  Cetain factions within the Empire are already counting\n" +
            $"on fear to incite rebellion.{callout}";
    }

    // Message2 (ATTCOMM.PAS:1268-1275): fixed text, no randomness beyond the outer pick.
    private string BattleLostMessage2() =>
        $"{MyLord()}, I'm sorry to report that the entire attack force was lost\n" +
        "in the battle.  At the risk of offending Your Highness, I would like to\n" +
        "point out that an option to retreat was open at all times.  Although\n" +
        "sacrifice is something that all your troops know, it is often best to\n" +
        "allow them the luxury of living to fight another day.";

    // Message3 (ATTCOMM.PAS:1277-1290): names a random other active empire -- real Pascal rejection-
    // samples over the fixed Empire1..Empire8 range until it lands on one that both isn't Player and is
    // EmpireActive; ported as a direct pick over the empires that already satisfy that, one Rnd call
    // (see BattleLostMessage's own doc comment on why draw-count parity doesn't matter here). Only
    // reachable when subject.Owner isn't Independent, so subject.Owner itself is always at least one
    // eligible candidate -- the empty-list branch is defensive, not a modeled game state.
    private string BattleLostMessage3()
    {
        var others = game.Empires.Where(e => !ReferenceEquals(e, human) && e.Status == EmpireStatus.Active).ToList();
        var other = others.Count > 0 ? others[PascalMath.Rnd(random, 1, others.Count) - 1] : human;

        return $"{MyLord()}, the entire attack force was destroyed in battle.\n" +
            "Although I certainly do not question the orders and decision of Your\n" +
            "Highness, I should like to mention that this defeat will not go\n" +
            $"unnoticed in the Galaxy.  Already {other.Name} is starting to\n" +
            "believe that this Empire would not be an overly costly target.";
    }

    // Message4 (ATTCOMM.PAS:1292-1300): fixed intro, plus a Rnd(1,3) flavor tail.
    private string BattleLostMessage4()
    {
        var tail = PascalMath.Rnd(random, 1, 3) switch {
            1 => "You must be careful, Your Highness, or greater battles will be lost.",
            2 => "Do not think that this defeat will go unnoticed in the Galaxy.",
            _ => "You must be careful, other star systems grow suspicious of your defenses.",
        };

        return $"{MyLord()}, the attack force has been totally destroyed by the enemy.\n{tail}";
    }

    // OldShipsFound (ATTCOMM.PAS:1485-1526): an Independent planet may hold ships too obsolete for its
    // own tech level (left behind by a since-advanced empire) -- read-only info, no state effect.
    // `continuation` runs immediately when there's nothing to show, matching real Pascal's own
    // unconditional fall-through into EnemyConquered right after (ATTCOMM.PAS:1376-1381 calls this
    // before EnemyConquered regardless of whether it found anything).
    private void ShowOldShipsFound(object target, Action continuation)
    {
        if (target is not Planet planet || !planet.Owner.IsIndependent) {
            continuation();
            return;
        }

        var obsolete = Enum.GetValues<ShipType>()
            .Where(t => planet.Ships[t] > 0 && TechCatalog.MinTechForShip[t] > planet.TechLevel)
            .Select(t => $"{planet.Ships[t]} {t}")
            .ToList();

        if (obsolete.Count == 0) {
            continuation();
            return;
        }

        var dialog = new DosMessageWindow(game, human, $"We have found the following ships in orbit:\n{string.Join('\n', obsolete)}");
        var dismiss = AddModal(dialog, dismissOnOutsideClick: false);
        dialog.Answered += (_, _) => {
            dismiss();
            // Deferred one tick -- see the AskToCapture confirm's own comment on why chaining straight
            // into the next popup here is unsafe.
            App!.AddTimeout(TimeSpan.Zero, () => {
                continuation();
                return false;
            });
        };
    }

    /// <summary>
    /// Post-battle report screens (ATTCOMM.PAS's OldShipsFound/AskToCapture, and the final Result line)
    /// all draw into the exact same DisplayWindow <see cref="CloseUpWindow"/> already reproduces
    /// (DISPLAY.PAS:161-162: ThinBRD border, C.SYSDispWind content / C.SYSWBorder border), with an
    /// "Attack:" header line in C.SYSDispHigh repeated verbatim at the top of every one of these
    /// procedures (e.g. ATTCOMM.PAS:1347,1520). Terminal.Gui's <c>MessageBox</c> has no color/scheme
    /// parameter at all (confirmed via dotnet-inspect), so it can never reproduce this -- these three
    /// call sites needed a real window instead.
    /// </summary>
    private sealed class DosMessageWindow : Window
    {
        private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
        private static readonly TgAttribute DispHighAttribute = new(StandardColor.White, StandardColor.Blue);
        private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);

        /// <summary>Fires once, on whatever key dismisses the dialog. For a confirm dialog, read <see cref="Destroy"/> at that point.</summary>
        public event EventHandler? Answered;

        public bool Destroy { get; private set; }

        public DosMessageWindow(Game game, Empire viewer, string body, bool confirmDestroy = false)
        {
            Title = CloseUpWindow.DisplayWindowTitle(game, viewer);
            Width = 80;
            Height = 21;
            X = Pos.Center();
            Y = Pos.Center();
            BorderStyle = LineStyle.Single;
            CanFocus = true;
            SetScheme(new Scheme(DispWindAttribute));
            Border.View?.SetScheme(new Scheme(BorderAttribute));

            var header = new Label { X = 1, Y = 0, Text = "Attack:" };
            header.SetScheme(new Scheme(DispHighAttribute));
            Add(header);
            Add(new Label { X = 1, Y = 2, Width = Dim.Fill(1), Height = Dim.Fill(2), Text = body });

            // AskToCapture's own prompt sits right under its content (WriteString at Lines+5,
            // ATTCOMM.PAS:1376), not pinned to the bottom of the screen -- only the plain OK dialogs
            // (which have no equivalent "right under the content" line in source) anchor to the bottom,
            // matching TurnStartGreetingWindow's own established "press any key" convention.
            var bodyLineCount = body.Length == 0 ? 0 : body.Split('\n').Length;
            Add(new Label {
                X = 1,
                Y = confirmDestroy ? 2 + bodyLineCount + 1 : Pos.AnchorEnd(1),
                Text = confirmDestroy ? "Do you wish to destroy the enemy fleet (y/N) ? " : "Press any key to continue...",
            });

            // Latched, not just "one key handled": AddModal's Dismiss (GameShell.cs) has no
            // idempotence guard of its own, and every other AddModal caller only ever fires its
            // Committed/similar event once. This is the first popup where *any* key dismisses -- OS key
            // repeat (or two sends arriving close together, e.g. over psmux) can otherwise deliver a
            // second KeyDown before Remove(popup) has taken this window out of the tree, firing
            // Answered twice and driving GameShell's openModalCount negative, which permanently
            // disables the menu bar and map for the rest of the turn.
            var answered = false;
            KeyDown += (_, key) => {
                if (answered) {
                    return;
                }

                if (!confirmDestroy) {
                    answered = true;
                    key.Handled = true;
                    Answered?.Invoke(this, EventArgs.Empty);
                    return;
                }

                var ch = char.ToUpperInvariant((char)key.AsRune.Value);
                if (ch != 'Y' && ch != 'N' && key.KeyCode != KeyCode.Enter) {
                    return;
                }

                answered = true;
                key.Handled = true;
                Destroy = ch == 'Y';
                Answered?.Invoke(this, EventArgs.Empty);
            };
        }
    }

    // EnemyConquered's own three fallback congratulatory messages (ATTCOMM.PAS:1403-1431), verbatim --
    // Message1 always wins for a conquered capital (GetType(Target)=CapTyp, checked here before
    // ResolveAttack clears it back to Independent); otherwise one of the three is picked uniformly at
    // random, consuming exactly one Rnd(1,3) call to match Pascal's own RNG-order contract.
    private string ConquestMessage(Fleet attackerFleet, ISectorObject subject)
    {
        var isCapital = subject is IEconomicWorld { Type: WorldType.Capital };
        var messageNumber = isCapital ? 1 : PascalMath.Rnd(random, 1, 3);

        return messageNumber switch {
            1 => SovereigntyDeclaration(subject),
            2 => $"Congratulations {MyLord()}, {DisplayName(attackerFleet)} has succeeded in its attack against\n{DisplayName(subject)}.  No doubt some of your enemies will in the future\nbe more careful when challenging this empire.",
            _ => $"Congratulations on your victory, {MyLord()}, but remember that not\nall battles will be this easy.",
        };
    }

    // EnemyConquered's own Message1 (ATTCOMM.PAS:1403-1412) -- shared verbatim with AutoAttackCommand's
    // own DefConqueredART/non-Fleet branch (ATTCOMM.PAS:1673-1683), which duplicates this exact text
    // rather than calling EnemyConquered itself.
    private string SovereigntyDeclaration(ISectorObject subject)
    {
        var empireName = human.Name;
        var noun = subject switch { Fleet => "fleet", Starbase => "starbase", _ => "planet" };

        return human.IsEmpress
            ? $"In the name of Her Imperial Majesty, Lady of {empireName}, I hereby declare\nthis {noun} to be under the sovereign jurisdiction of the\n{empireName} Empire."
            : $"In the name of His Imperial Majesty, Lord of {empireName}, I hereby declare\nthis {noun} to be under the sovereign jurisdiction of the\n{empireName} Empire.";
    }

    private string DisplayName(ISectorObject obj) => obj.Names.GetValueOrDefault(human) ?? CloseUpWindow.DescribeLocation(obj, human);

    private string MyLord() => Honorifics.MyLord(human.IsEmpress);

    private MenuBarItem[] BuildMenus() => [
        new MenuBarItem("⌂", new MenuItem[] {
            new("_About Anacreon", Key.Empty, () => Stub("About Anacreon")),
        }),
        new MenuBarItem("_Game", new MenuItem[] {
            new("_Pause", Key.Empty, () => ShowInfo("Paused", "Time has stopped. Press any key to continue.")),
            new("_Status Hardcopy", Key.Empty, () => Stub("Status Hardcopy")),
            new("Sa_ve", Key.Empty, PromptForSaveName),
            new("_Next Turn", Key.Empty, EndTurn),
            new("_Quit", Key.Empty, ConfirmQuit),
            new("E_xit to OS", Key.Empty, ConfirmExitToOs),
        }),
        new MenuBarItem("_Empire", new MenuItem[] {
            new("_Send Message", Key.Empty, () => Stub("Send Message")),
            new("_Read Messages", Key.Empty, () => Stub("Read Messages")),
            new("_Trade Technology", Key.Empty, () => Stub("Trade Technology")),
            new("Te_ch Tree", Key.Empty, ShowTechTree),
        }),
        new MenuBarItem("_Worlds", new MenuItem[] {
            new("_Close Up", Key.Empty, ExamineCursor),
            new("_Designate", Key.Empty, Designate),
            new("P_roduction", Key.Empty, Production),
            new("_ISSP", Key.Empty, Issp),
            new("_Add Name", Key.Empty, () => Stub("Add Name")),
            new("Delete _Name", Key.Empty, () => Stub("Delete Name")),
            new("_Liberate", Key.Empty, () => Stub("Liberate")),
            new("_Self-Destruct", Key.Empty, () => Stub("Self-Destruct")),
        }),
        new MenuBarItem("_Fleet", new MenuItem[] {
            new("_Deploy", Key.Empty, DeployFleet),
            new("_Change Destination", Key.Empty, ChangeDestination),
            new("_Transfer", Key.Empty, TransferFleet),
            new("_Abort/Join", Key.Empty, AbortJoinFleet),
            new("_Refuel", Key.Empty, RefuelFleet),
            new("_SRM Sweep", Key.Empty, SrmSweep),
            new("_Orders", Key.Empty, FleetOrders),
            new("Canc_el Orders", Key.Empty, CancelFleetOrders),
            new("_Probe", Key.Empty, LaunchProbe),
        }),
        new MenuBarItem("_Build", new MenuItem[] {
            new("_Site Status", Key.Empty, () => Stub("Construction Site Status")),
            new("_New", Key.Empty, () => Stub("New Construction Site")),
            new("_Abort", Key.Empty, () => Stub("Abort Construction")),
        }),
        new MenuBarItem("_Ministry of War", new MenuItem[] {
            new("_Attack", Key.Empty, Attack),
            new("Auto A_ttack", Key.Empty, AutoAttack),
            new("Launch _LAMs", Key.Empty, LaunchLams),
            new("_Defenses", Key.Empty, Defenses),
        }),
    ];

    private StatusBar BuildStatusBar() => new([
        new Shortcut(Key.F1, "Help", ShowHelpWindow, ""),
        new Shortcut(Key.F3, "Status", Status, ""),
        new Shortcut(Key.F5, "Fleet", ShowFleetWindow, ""),
        new Shortcut(Key.F7, "News", ShowNewsWindow, ""),
        new Shortcut(Key.F8, "Empire", ShowEmpireWindow, ""),
        new Shortcut(Key.F9, "Names", ShowNamesWindow, ""),
    ]);
}
