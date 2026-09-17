using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui2.Overlays;


// Ministry of War > Defenses (MSCCOMM.PAS: DefenseCommand, :222-436), ported from Reconstructed4021.Tui's
// own DefensesEditor: a 7x5 grid (ship type x orbital shell), each cell a 0..100 percentage of that
// ship type's garrison placed at that shell -- the same ShellDefensePlan Combat.CombatEngine.GetEnemy
// already reads when a world's own defending fleet distribution is computed. Edits DefenseSettings.Fleets
// directly and live -- real Pascal's own SetDefenseSettings call at the very end has no equivalent here
// since there's no local copy to write back, same simplification Fleet Group Configuration already made.
// DefenseSettings.Starbases (Pascal's StarbaseDefDist) is untouched -- confirmed dead in real Pascal
// (declared, round-tripped by Get/SetDefenseSettings, never read anywhere else); DefenseCommand itself
// never edits it, so neither does this screen.
//
// No cancel path, same as Fleet Group Configuration: real Pascal's own REPEAT loop only exits on Esc,
// and only once the grid is already legal (CheckForIllegalAmounts finding no violation). Pressing Esc
// on an illegal grid (a row not summing to 100, or a ship type parked on the ground that can't legally
// be there) shows the violation message and Normalizes it (illegal ground percentages swept into
// sub-orbit, over/under-100 rows rebalanced) but keeps the screen open -- Normalize always leaves the
// grid legal afterward, so a second Esc then closes it.
internal sealed class DefensesOverlay : IOverlay
{
    private const int Width = 70;
    private const int Height = 14;

    private static readonly ShipType[] ShipRows = Enum.GetValues<ShipType>(); // fgt..trn, Pascal's declared order
    private static readonly ShellPosition[] ShellColumns = Enum.GetValues<ShellPosition>(); // DpSpc..Grnd, Pascal's declared order

    private readonly ShellDefensePlan _plan;
    private int _row;
    private int _col;
    private string? _editBuffer;
    private string _error = "";

    public bool IsDismissed { get; private set; }

    public DefensesOverlay(ShellDefensePlan plan)
    {
        _plan = plan;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_editBuffer is not null)
        {
            HandleEditKey(key);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _row = _row > 0 ? _row - 1 : ShipRows.Length - 1;
                return;
            case ConsoleKey.DownArrow:
                _row = _row < ShipRows.Length - 1 ? _row + 1 : 0;
                return;
            case ConsoleKey.LeftArrow:
                _col = _col > 0 ? _col - 1 : ShellColumns.Length - 1;
                return;
            case ConsoleKey.RightArrow:
                _col = _col < ShellColumns.Length - 1 ? _col + 1 : 0;
                return;
            case ConsoleKey.Enter:
                _editBuffer = "";
                return;
            case ConsoleKey.Escape:
                TryFinish();
                return;
        }

        if (char.IsDigit(key.KeyChar))
        {
            _editBuffer = key.KeyChar.ToString();
        }
    }

    // ChangePercent (MSCCOMM.PAS:302-328): a small inline line-editor for one cell's percentage, seeded
    // by whichever digit opened it (a bare Enter seeds an empty line, matching Ch=ReturnKey).
    private void HandleEditKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                if (_editBuffer!.Length > 0)
                {
                    _editBuffer = _editBuffer[..^1];
                }

                return;
            case ConsoleKey.Escape:
                // (Line=EscKey) -- NewPercent:=Defenses[OrbI,ShpI], i.e. keep the current value, not
                // "cancel back out of the whole grid" (that's a bare Esc while just browsing cells,
                // handled above once _editBuffer is null).
                _editBuffer = null;
                return;
            case ConsoleKey.Enter:
                SubmitEdit();
                return;
        }

        if (char.IsDigit(key.KeyChar))
        {
            _editBuffer += key.KeyChar;
        }
    }

    private void SubmitEdit()
    {
        // (Line='') -- a bare Enter with nothing typed also keeps the current value.
        if (_editBuffer!.Length == 0)
        {
            _editBuffer = null;
            return;
        }

        if (!int.TryParse(_editBuffer, out var newPercent))
        {
            _error = "Enter a whole number.";
            _editBuffer = "";
            return;
        }

        // ChangePercent's own clamp (MSCCOMM.PAS:325-326): out of 0..100 resets to ZERO, not the
        // nearest bound -- a real Pascal quirk (typing "150" blanks the cell), not a typo to "fix."
        if (newPercent is > 100 or < 0)
        {
            newPercent = 0;
        }

        _plan[ShellColumns[_col]][ShipRows[_row]] = newPercent;
        _editBuffer = null;
        _error = "";
    }

    // DefenseCommand's own EscKey branch (MSCCOMM.PAS:416-426): CheckForIllegalAmounts, then Normalize
    // unconditionally (even when already legal -- Pascal runs both every time, not just on a violation),
    // then only close if this pass found nothing illegal.
    private void TryFinish()
    {
        var illegal = CheckForIllegalAmounts(out var message);
        Normalize();
        _row = 0;
        _col = 0;

        if (illegal)
        {
            _error = message;
            return;
        }

        _error = "";
        IsDismissed = true;
    }

    // CheckForIllegalAmounts (MSCCOMM.PAS:273-300): Pascal's own loop keeps overwriting ErrorStr as it
    // scans fgt..trn, so only the last violated row's message ever survives to display -- reproduced by
    // just checking in the same order and keeping the latest hit, not the first.
    private bool CheckForIllegalAmounts(out string message)
    {
        var illegal = false;
        message = "";

        foreach (var ship in ShipRows)
        {
            var total = ShellColumns.Sum(shell => _plan[shell][ship]);
            if (total != 100)
            {
                illegal = true;
                message = "Total for each ship type must be 100 -- normalizing.";
            }

            if (_plan[ShellPosition.Ground][ship] != 0 && ship is not (ShipType.Fighter or ShipType.Transport or ShipType.Jumptransport))
            {
                illegal = true;
                message = "Only fighters, transports, and jumptransports can be on the ground -- normalizing.";
            }
        }

        return illegal;
    }

    // Normalize (MSCCOMM.PAS:231-271): sweeps any ground percentage off ship types that can't legally
    // be there into sub-orbit for every row (unconditionally, not just illegal ones -- Pascal's own
    // Normalize runs regardless of what CheckForIllegalAmounts found), then rebalances each row's total
    // back to exactly 100 (topping up sub-orbit if under, proportionally scaling down then topping up
    // sub-orbit if over).
    private void Normalize()
    {
        foreach (var ship in ShipRows)
        {
            if (ship is not (ShipType.Fighter or ShipType.Transport or ShipType.Jumptransport))
            {
                _plan[ShellPosition.SubOrbit][ship] += _plan[ShellPosition.Ground][ship];
                _plan[ShellPosition.Ground][ship] = 0;
            }

            var total = ShellColumns.Sum(shell => _plan[shell][ship]);

            if (total < 100)
            {
                _plan[ShellPosition.SubOrbit][ship] += 100 - total;
            }
            else if (total > 100)
            {
                foreach (var shell in ShellColumns)
                {
                    _plan[shell][ship] = (int)(_plan[shell][ship] / (double)total * 100); // Trunc, matching Pascal's Trunc (toward zero, all values non-negative)
                }

                total = ShellColumns.Sum(shell => _plan[shell][ship]);
                if (total < 100)
                {
                    _plan[ShellPosition.SubOrbit][ship] += 100 - total;
                }
            }
        }
    }

    // Same spellings as TacticalBattleScreen's own PosName.
    private static string ShellName(ShellPosition shell) => shell switch
    {
        ShellPosition.DeepSpace => "deep space",
        ShellPosition.HighOrbit => "high orbit",
        ShellPosition.Orbit => "orbit",
        ShellPosition.SubOrbit => "sub-orbit",
        ShellPosition.Ground => "ground",
        _ => throw new ArgumentOutOfRangeException(nameof(shell)),
    };

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 10) / 2), y, " Defenses ", ConsoleColor.White, ConsoleColor.Black);

        fb.DrawText(x + 19, y + 1, "DeepSp  HighOrb   Orbit   SubOrb  Ground   Total", ConsoleColor.Gray, ConsoleColor.Black);

        for (var r = 0; r < ShipRows.Length; r++)
        {
            var ship = ShipRows[r];
            fb.DrawText(x + 1, y + 2 + r, new ResourceKind.Ship(ship).DisplayName, ConsoleColor.Gray, ConsoleColor.Black);

            var total = 0;
            for (var c = 0; c < ShellColumns.Length; c++)
            {
                var value = _plan[ShellColumns[c]][ship];
                total += value;
                var selected = r == _row && c == _col;
                fb.DrawText(x + 19 + c * 8, y + 2 + r, $"{value,4}", selected ? ConsoleColor.Black : ConsoleColor.Gray, selected ? ConsoleColor.Gray : ConsoleColor.Black);
            }

            fb.DrawText(x + 19 + ShellColumns.Length * 8, y + 2 + r, $"{total,4}", ConsoleColor.Gray, ConsoleColor.Black);
        }

        var promptRow = y + 2 + ShipRows.Length + 1;
        if (_editBuffer is not null)
        {
            fb.DrawText(x + 1, promptRow, $"New setting for {new ResourceKind.Ship(ShipRows[_row]).DisplayName} at {ShellName(ShellColumns[_col])}: {_editBuffer}", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }

        fb.DrawText(x + 1, promptRow + 1, _error, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        fb.DrawText(x + 1, y + height - 2, "Arrows: move   0-9: set %   Enter: type a value   Esc: done", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}
