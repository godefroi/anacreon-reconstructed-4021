using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// InputNewDistribution (FLTCOMM.PAS:134-489) -- the Resource Distribution Editor: a two-row grid
/// (fleet on top, a world or another fleet, "ground", below) across all 14 ship+cargo columns, with a
/// column cursor (Left/Right), per-column fill/empty (Up/Down), and a numeric transfer prompt
/// (digit/+/-/Enter). Everywhere this port reuses real Pascal's own primitive: Deploy Fleet today,
/// Fleet Transfer/Abort-Join later -- the transfer math itself lives in
/// <see cref="ResourceDistribution"/> (Core), this class only drives the grid's own input loop and
/// draws it. Real Pascal highlights the selected column with a direct video-memory poke
/// (EraseOldPointer/UpdatePointer, "WARNING! MACHINE SPECIFIC!") -- that's a DOS rendering trick, not
/// a game mechanic, so it's reproduced here as a per-column <see cref="Label"/> whose <see cref="Scheme"/>
/// toggles between SYSDispWind and SYSDispSelect instead.
///
/// Added/removed as a direct child of the running <see cref="GameShell"/> (via its own AddModal),
/// same convention as <see cref="CloseUpWindow"/>/the sector picker -- see that class's own doc
/// comment for why.
/// </summary>
internal sealed class ResourceDistributionEditor : Window
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray); // SYSDispSelect = 112

    private readonly ShipCounts fleetShips;
    private readonly CargoHold fleetCargo;
    private readonly ShipCounts groundShips;
    private readonly CargoHold groundCargo;
    private readonly bool groundIsPlayerOwned;
    private readonly bool groundIsAFleet;

    private readonly Label[] fleetCells = new Label[ResourceColumn.All.Length];
    private readonly Label[] groundCells = new Label[ResourceColumn.All.Length];
    private readonly Label fleetCargoSpaceLabel;
    private readonly Label groundCargoSpaceLabel;
    private readonly Label promptLabel;
    private readonly Label errorLabel;

    private int pointIndex;
    private string? editBuffer;

    /// <summary>Ships and cargo actually chosen for the fleet once the player ends the transfer (Esc/X) with a valid distribution -- read this after <see cref="Committed"/> fires.</summary>
    public ShipCounts FleetShips => fleetShips;
    public CargoHold FleetCargo => fleetCargo;

    /// <summary>Fired once the transfer session ends with a valid distribution (Esc or X, and the fleet's own cargo fits) -- InputNewDistribution's own `UNTIL EverythingOk` exit.</summary>
    public event EventHandler? Committed;

    public ResourceDistributionEditor(ShipCounts fleetShips, CargoHold fleetCargo,
        ShipCounts groundShips, CargoHold groundCargo, bool groundIsPlayerOwned, bool groundIsAFleet, string title)
    {
        this.fleetShips = fleetShips;
        this.fleetCargo = fleetCargo;
        this.groundShips = groundShips;
        this.groundCargo = groundCargo;
        this.groundIsPlayerOwned = groundIsPlayerOwned;
        this.groundIsAFleet = groundIsAFleet;

        Title = title;
        Width = 80;
        Height = 19;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        AddAt(0, 0, "  fgt   hk  jmp  jtn  pen  str  trn  men ninj  amb  che  met  sup  tri  Cargo");

        for (var i = 0; i < ResourceColumn.All.Length; i++) {
            fleetCells[i] = AddAt(i * 5, 1, string.Empty);
            groundCells[i] = AddAt(i * 5, 2, string.Empty);
        }

        fleetCargoSpaceLabel = AddAt(ResourceColumn.All.Length * 5, 1, string.Empty);
        groundCargoSpaceLabel = AddAt(ResourceColumn.All.Length * 5, 2, string.Empty);

        AddAt(0, 8, "<RETURN> to select ship/cargo to change.");
        AddAt(0, 9, "<Esc> to end transfer.");
        AddAt(0, 10, "   +### to transfer from ground to fleet.");
        AddAt(0, 11, "   -### to transfer from fleet to ground.");

        AddAt(51, 8, "Transport Capacity:");
        var cargoRow = 9;
        foreach (var column in ResourceColumn.All) {
            if (column.CargoSpacePerUnit is { } space) {
                AddAt(51, cargoRow++, $"{space,3} {column.Name}");
            }
        }

        promptLabel = AddAt(0, 13, string.Empty);
        errorLabel = AddAt(0, 15, string.Empty);

        KeyDown += OnKeyDown;
        UpdateDisplay();
    }

    private Label AddAt(int x, int y, string text)
    {
        var label = new Label { X = x, Y = y, Text = text };
        Add(label);
        return label;
    }

    // UpdateDisplay (FLTCOMM.PAS:162-209): both rows' per-column counts (ground redacted to "????"
    // for anyone but the player, matching InputNewDistribution's own GroundStatus<>Player check) plus
    // each side's own free cargo space.
    private void UpdateDisplay()
    {
        for (var i = 0; i < ResourceColumn.All.Length; i++) {
            var column = ResourceColumn.All[i];
            fleetCells[i].Text = $"{column.Get(fleetShips, fleetCargo),5}";
            groundCells[i].Text = groundIsPlayerOwned ? $"{column.Get(groundShips, groundCargo),5}" : " ????";
        }

        fleetCargoSpaceLabel.Text = $" {FleetLogistics.FleetCargoSpace(fleetShips, fleetCargo),4} ";

        // UpdateDisplay's own GrnCargoSpace (FLTCOMM.PAS:197-200): 0, not blank, whenever the ground
        // isn't itself a fleet the player owns -- still a real printed number in real Pascal, never
        // redacted the way the per-column counts above are.
        var groundCargoSpace = groundIsAFleet && groundIsPlayerOwned ? FleetLogistics.FleetCargoSpace(groundShips, groundCargo) : 0;
        groundCargoSpaceLabel.Text = $" {groundCargoSpace,4} ";

        UpdatePointerHighlight();
    }

    private void UpdatePointerHighlight()
    {
        for (var i = 0; i < ResourceColumn.All.Length; i++) {
            var scheme = new Scheme(i == pointIndex ? SelectedAttribute : DispWindAttribute);
            fleetCells[i].SetScheme(scheme);
            groundCells[i].SetScheme(scheme);
            // Not proven that SetScheme alone marks a Label dirty (no Text change happens on a pure
            // cursor move) -- SetNeedsDraw explicitly, since a missed repaint here would look
            // identical to "arrow keys aren't reaching the widget" during a playtest.
            fleetCells[i].SetNeedsDraw();
            groundCells[i].SetNeedsDraw();
        }
    }

    private void SetError(string message) => errorLabel.Text = message;

    private void OnKeyDown(object? sender, Key key)
    {
        if (editBuffer is not null) {
            HandleEditKey(key);
            return;
        }

        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.CursorLeft:
                pointIndex = pointIndex > 0 ? pointIndex - 1 : ResourceColumn.All.Length - 1;
                UpdatePointerHighlight();
                key.Handled = true;
                return;
            case KeyCode.CursorRight:
                pointIndex = pointIndex < ResourceColumn.All.Length - 1 ? pointIndex + 1 : 0;
                UpdatePointerHighlight();
                key.Handled = true;
                return;
            case KeyCode.CursorUp:
                ResourceDistribution.FillFleet(ResourceColumn.All[pointIndex], fleetShips, fleetCargo, groundShips, groundCargo, groundIsPlayerOwned);
                SetError(groundIsPlayerOwned ? "" : "This is not your territory.");
                UpdateDisplay();
                key.Handled = true;
                return;
            case KeyCode.CursorDown:
                ResourceDistribution.EmptyFleet(ResourceColumn.All[pointIndex], fleetShips, fleetCargo, groundShips, groundCargo, groundIsAFleet);
                SetError("");
                UpdateDisplay();
                key.Handled = true;
                return;
            case KeyCode.Enter:
                BeginEdit("");
                key.Handled = true;
                return;
            case KeyCode.Esc:
                TryFinish();
                key.Handled = true;
                return;
        }

        var ch = (char)key.AsRune.Value;
        if (ch is '+' or '-' || char.IsDigit(ch)) {
            BeginEdit(ch.ToString());
            key.Handled = true;
        } else if (ch is 'x' or 'X') {
            TryFinish();
            key.Handled = true;
        }
    }

    // GetChange (FLTCOMM.PAS:281-344): a small inline line-editor for one transfer amount, seeded by
    // whichever character opened it (a bare Enter seeds an empty line, matching Ch=ReturnKey there).
    private void BeginEdit(string seed)
    {
        editBuffer = seed;
        RenderEditPrompt();
    }

    private void HandleEditKey(Key key)
    {
        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.Backspace:
                if (editBuffer!.Length > 0) {
                    editBuffer = editBuffer[..^1];
                    RenderEditPrompt();
                }
                key.Handled = true;
                return;
            case KeyCode.Esc:
                // (Line=EscKey) -- a no-op transfer, not "cancel back out of the whole widget" (that's
                // a bare Esc while just browsing columns, handled in OnKeyDown once editBuffer is null).
                CommitEdit(0);
                key.Handled = true;
                return;
            case KeyCode.Enter:
                SubmitEdit();
                key.Handled = true;
                return;
        }

        var ch = (char)key.AsRune.Value;
        if (char.IsDigit(ch) || (ch is '+' or '-' && editBuffer!.Length == 0)) {
            editBuffer += ch;
            RenderEditPrompt();
            key.Handled = true;
        }
    }

    private void SubmitEdit()
    {
        // (Line='') -- an empty submission (Enter pressed with nothing typed) is also a no-op transfer.
        if (editBuffer!.Length == 0 || editBuffer is "+" or "-") {
            CommitEdit(0);
            return;
        }

        if (!int.TryParse(editBuffer, out var amount)) {
            SetError($"numbers from -{ResourceDistribution.MaxResources} to {ResourceDistribution.MaxResources}.");
            editBuffer = "";
            RenderEditPrompt();
            return;
        }

        CommitEdit(amount);
    }

    private void CommitEdit(int amount)
    {
        var column = ResourceColumn.All[pointIndex];
        if (amount != 0 && !ResourceDistribution.TryTransfer(column, fleetShips, fleetCargo, groundShips, groundCargo, groundIsPlayerOwned, amount, out var error)) {
            SetError(error);
            editBuffer = "";
            RenderEditPrompt();
            return;
        }

        editBuffer = null;
        promptLabel.Text = "";
        SetError("");
        UpdateDisplay();
    }

    private void RenderEditPrompt() =>
        promptLabel.Text = $"How many {ResourceColumn.All[pointIndex].Name} to transfer : {editBuffer}";

    // InputNewDistribution's own outer `UNTIL EverythingOk` (FLTCOMM.PAS:441-472): a deployed fleet
    // that ended up over its own transport capacity re-opens the editor with an error instead of
    // closing, exactly like Pascal's own retry loop.
    private void TryFinish()
    {
        if (FleetLogistics.FleetCargoSpace(fleetShips, fleetCargo) < 0) {
            SetError("There aren't enough transports in the fleet.");
            return;
        }

        if (groundIsAFleet && FleetLogistics.FleetCargoSpace(groundShips, groundCargo) < 0) {
            if (!groundIsPlayerOwned) {
                FleetLogistics.BalanceFleet(groundShips, groundCargo);
            } else {
                SetError("There aren't enough transports left in the fleet.");
                return;
            }
        }

        Committed?.Invoke(this, EventArgs.Empty);
    }
}
