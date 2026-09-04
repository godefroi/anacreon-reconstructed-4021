using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Ministry of War > Defenses (MSCCOMM.PAS: DefenseCommand, :222-436): a 7x5 grid (ship type x
/// orbital shell), each cell a 0..100 percentage of that ship type's garrison placed at that shell --
/// the same <see cref="ShellDefensePlan"/> <see cref="Combat.CombatEngine.GetEnemy"/> already reads
/// when a world's own defending fleet distribution is computed. Edits <see cref="DefenseSettings.Fleets"/>
/// directly and live -- real Pascal's own <c>SetDefenseSettings</c> call at the very end has no
/// equivalent here since there's no local copy to write back, same simplification Fleet Group
/// Configuration already made. <see cref="DefenseSettings.Starbases"/> (Pascal's <c>StarbaseDefDist</c>)
/// is untouched -- confirmed dead in real Pascal (declared, round-tripped by Get/SetDefenseSettings,
/// never read anywhere else in either source tree); <c>DefenseCommand</c> itself never edits it, so
/// neither does this screen.
///
/// No cancel path, same as Fleet Group Configuration: real Pascal's own REPEAT loop only exits on Esc,
/// and only once the grid is already legal (<c>CheckForIllegalAmounts</c> finding no violation).
/// Pressing Esc on an illegal grid (a row not summing to 100, or a ship type parked on the ground that
/// can't legally be there) shows the violation message and <c>Normalize</c>s it (illegal ground
/// percentages swept into sub-orbit, over/under-100 rows rebalanced) but keeps the screen open --
/// <c>Normalize</c> always leaves the grid legal afterward, so a second Esc then closes it.
/// </summary>
internal sealed class DefensesEditor : Window
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray); // SYSDispSelect = 112

    private static readonly ShipType[] ShipRows = Enum.GetValues<ShipType>(); // fgt..trn, Pascal's declared order
    private static readonly ShellPosition[] ShellColumns = Enum.GetValues<ShellPosition>(); // DpSpc..Grnd, Pascal's declared order

    private readonly ShellDefensePlan plan;
    private readonly Label[,] cells = new Label[ShipRows.Length, ShellColumns.Length];
    private readonly Label[] totalCells = new Label[ShipRows.Length];
    private readonly Label promptLabel;
    private readonly Label errorLabel;

    private int row;
    private int col;
    private string? editBuffer;

    /// <summary>Fired once the grid is legal and the player presses Esc a second time -- DefenseCommand's own <c>UNTIL (Ch=EscKey) AND NOT(Error)</c>.</summary>
    public event EventHandler? Done;

    public DefensesEditor(ShellDefensePlan plan)
    {
        this.plan = plan;

        Title = "Defenses";
        Width = 70;
        Height = 14;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        Add(new Label { X = 18, Y = 0, Text = "DeepSp  HighOrb   Orbit   SubOrb  Ground   Total" });
        for (var r = 0; r < ShipRows.Length; r++) {
            Add(new Label { X = 0, Y = r + 1, Text = ShipRowName(ShipRows[r]) });
            for (var c = 0; c < ShellColumns.Length; c++) {
                var cell = new Label { X = 18 + c * 8, Y = r + 1, Text = string.Empty };
                cells[r, c] = cell;
                Add(cell);
            }
            totalCells[r] = new Label { X = 18 + ShellColumns.Length * 8, Y = r + 1, Text = string.Empty };
            Add(totalCells[r]);
        }

        promptLabel = new Label { X = 0, Y = ShipRows.Length + 2, Text = string.Empty };
        Add(promptLabel);
        errorLabel = new Label { X = 0, Y = ShipRows.Length + 3, Text = string.Empty };
        Add(errorLabel);
        Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Arrows: move   0-9: set %   Enter: type a value   Esc: done" });

        KeyDown += OnKeyDown;
        UpdateDisplay();
    }

    // UpdateLine (MSCCOMM.PAS:330-356), all 7 rows at once rather than one row at a time -- this
    // screen has no equivalent to Pascal's own "redraw only the row(s) that changed" optimization,
    // matching ResourceDistributionEditor's own UpdateDisplay precedent.
    private void UpdateDisplay()
    {
        for (var r = 0; r < ShipRows.Length; r++) {
            var ship = ShipRows[r];
            var total = 0;
            for (var c = 0; c < ShellColumns.Length; c++) {
                var value = plan[ShellColumns[c]][ship];
                total += value;
                cells[r, c].Text = $"{value,4}";
            }
            totalCells[r].Text = $"{total,4}";
        }

        UpdatePointerHighlight();
    }

    private void UpdatePointerHighlight()
    {
        for (var r = 0; r < ShipRows.Length; r++) {
            for (var c = 0; c < ShellColumns.Length; c++) {
                var scheme = new Scheme(r == row && c == col ? SelectedAttribute : DispWindAttribute);
                cells[r, c].SetScheme(scheme);
                cells[r, c].SetNeedsDraw();
            }
        }
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (editBuffer is not null) {
            HandleEditKey(key);
            return;
        }

        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.CursorUp:
                row = row > 0 ? row - 1 : ShipRows.Length - 1;
                UpdatePointerHighlight();
                key.Handled = true;
                return;
            case KeyCode.CursorDown:
                row = row < ShipRows.Length - 1 ? row + 1 : 0;
                UpdatePointerHighlight();
                key.Handled = true;
                return;
            case KeyCode.CursorLeft:
                col = col > 0 ? col - 1 : ShellColumns.Length - 1;
                UpdatePointerHighlight();
                key.Handled = true;
                return;
            case KeyCode.CursorRight:
                col = col < ShellColumns.Length - 1 ? col + 1 : 0;
                UpdatePointerHighlight();
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
        if (char.IsDigit(ch)) {
            BeginEdit(ch.ToString());
            key.Handled = true;
        }
    }

    // ChangePercent (MSCCOMM.PAS:302-328): a small inline line-editor for one cell's percentage,
    // seeded by whichever digit opened it (a bare Enter seeds an empty line, matching Ch=ReturnKey).
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
                // (Line=EscKey) -- NewPercent:=Defenses[OrbI,ShpI], i.e. keep the current value, not
                // "cancel back out of the whole grid" (that's a bare Esc while just browsing cells,
                // handled in OnKeyDown once editBuffer is null).
                CancelEdit();
                key.Handled = true;
                return;
            case KeyCode.Enter:
                SubmitEdit();
                key.Handled = true;
                return;
        }

        var ch = (char)key.AsRune.Value;
        if (char.IsDigit(ch)) {
            editBuffer += ch;
            RenderEditPrompt();
            key.Handled = true;
        }
    }

    private void CancelEdit()
    {
        editBuffer = null;
        promptLabel.Text = "";
        UpdateDisplay();
    }

    private void SubmitEdit()
    {
        // (Line='') -- a bare Enter with nothing typed also keeps the current value.
        if (editBuffer!.Length == 0) {
            CancelEdit();
            return;
        }

        if (!int.TryParse(editBuffer, out var newPercent)) {
            errorLabel.Text = "Enter a whole number.";
            editBuffer = "";
            RenderEditPrompt();
            return;
        }

        // ChangePercent's own clamp (MSCCOMM.PAS:325-326): out of 0..100 resets to ZERO, not the
        // nearest bound -- a real Pascal quirk (typing "150" blanks the cell), not a typo to "fix."
        if (newPercent is > 100 or < 0) {
            newPercent = 0;
        }

        plan[ShellColumns[col]][ShipRows[row]] = newPercent;
        editBuffer = null;
        promptLabel.Text = "";
        errorLabel.Text = "";
        UpdateDisplay();
    }

    private void RenderEditPrompt() =>
        promptLabel.Text = $"New setting for {ShipRowName(ShipRows[row]).Trim()} at {ShellName(ShellColumns[col])}: {editBuffer}";

    // DefenseCommand's own EscKey branch (MSCCOMM.PAS:416-426): CheckForIllegalAmounts, then
    // Normalize unconditionally (even when already legal -- Pascal runs both every time, not just on
    // a violation), then only close if this pass found nothing illegal.
    private void TryFinish()
    {
        var illegal = CheckForIllegalAmounts(out var message);
        Normalize();
        row = 0;
        col = 0;
        UpdateDisplay();

        if (illegal) {
            errorLabel.Text = message;
            return;
        }

        errorLabel.Text = "";
        Done?.Invoke(this, EventArgs.Empty);
    }

    // CheckForIllegalAmounts (MSCCOMM.PAS:273-300): Pascal's own loop keeps overwriting ErrorStr as it
    // scans fgt..trn, so only the last violated row's message ever survives to display -- reproduced
    // by just checking in the same order and keeping the latest hit, not the first.
    private bool CheckForIllegalAmounts(out string message)
    {
        var illegal = false;
        message = "";

        foreach (var ship in ShipRows) {
            var total = ShellColumns.Sum(shell => plan[shell][ship]);
            if (total != 100) {
                illegal = true;
                message = "Total for each ship type must be 100 -- normalizing.";
            }

            if (plan[ShellPosition.Ground][ship] != 0 && ship is not (ShipType.Fighter or ShipType.Transport or ShipType.Jumptransport)) {
                illegal = true;
                message = "Only fighters, transports, and jumptransports can be on the ground -- normalizing.";
            }
        }

        return illegal;
    }

    // Normalize (MSCCOMM.PAS:231-271): sweeps any ground percentage off ship types that can't legally
    // be there into sub-orbit for every row (unconditionally, not just illegal ones -- Pascal's own
    // Normalize runs regardless of what CheckForIllegalAmounts found), then rebalances each row's
    // total back to exactly 100 (topping up sub-orbit if under, proportionally scaling down then
    // topping up sub-orbit if over).
    private void Normalize()
    {
        foreach (var ship in ShipRows) {
            if (ship is not (ShipType.Fighter or ShipType.Transport or ShipType.Jumptransport)) {
                plan[ShellPosition.SubOrbit][ship] += plan[ShellPosition.Ground][ship];
                plan[ShellPosition.Ground][ship] = 0;
            }

            var total = ShellColumns.Sum(shell => plan[shell][ship]);

            if (total < 100) {
                plan[ShellPosition.SubOrbit][ship] += 100 - total;
            } else if (total > 100) {
                foreach (var shell in ShellColumns) {
                    plan[shell][ship] = (int)(plan[shell][ship] / (double)total * 100); // Trunc, matching Pascal's Trunc (toward zero, all values non-negative)
                }

                total = ShellColumns.Sum(shell => plan[shell][ship]);
                if (total < 100) {
                    plan[ShellPosition.SubOrbit][ship] += 100 - total;
                }
            }
        }
    }

    // ThingNames (DATACNST.PAS:100-112), fgt..trn only, padded to a fixed column width -- same names
    // as GameShell's own copy (Auto Attack/Launch LAMs), TacticalBattleDisplayWindow's own TypeName.
    private static string ShipRowName(ShipType ship) => ship switch {
        ShipType.Fighter => "fighter squadrons",
        ShipType.HunterKiller => "hunter-killers",
        ShipType.Jumpship => "jumpships",
        ShipType.Jumptransport => "jumptransports",
        ShipType.Penetrator => "penetrators",
        ShipType.Starship => "starships",
        ShipType.Transport => "transports",
        _ => throw new ArgumentOutOfRangeException(nameof(ship)),
    };

    // Same spellings as TacticalBattleDisplayWindow's own PosName.
    private static string ShellName(ShellPosition shell) => shell switch {
        ShellPosition.DeepSpace => "deep space",
        ShellPosition.HighOrbit => "high orbit",
        ShellPosition.Orbit => "orbit",
        ShellPosition.SubOrbit => "sub-orbit",
        ShellPosition.Ground => "ground",
        _ => throw new ArgumentOutOfRangeException(nameof(shell)),
    };
}
