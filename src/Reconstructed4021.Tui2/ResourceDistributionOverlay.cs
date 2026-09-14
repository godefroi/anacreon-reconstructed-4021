using Reconstructed4021.Core.Entities;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2;

// InputNewDistribution (FLTCOMM.PAS:134-489), ported from Reconstructed4021.Tui's own
// ResourceDistributionEditor -- a two-row grid (fleet on top, "ground" below) across all 14 ship+cargo
// columns. Left/Right moves the column cursor, Up/Down fills/empties a column in one shot
// (ResourceDistribution.FillFleet/EmptyFleet), Enter (or a digit/+/-) opens a numeric transfer prompt
// (ResourceDistribution.TryTransfer), Esc/X ends the transfer if the result actually fits.
//
// Mouse and Shift/Ctrl+Up/Down quick-adjust are dropped -- Panemonde has no mouse path, and the
// keyboard-only fill/empty/exact-amount trio already covers every real Pascal input. The numeric edit
// buffer stays a raw string (not TextInputField): Esc while editing commits a zero-amount transfer
// (GetChange's own Line=EscKey branch), not "cancel the widget" -- a different semantic than any other
// text field in this project.
internal sealed class ResourceDistributionOverlay : IOverlay
{
    private const int FrameWidth = 80;
    private const int FrameHeight = 21;

    private const ConsoleColor BorderFg = ConsoleColor.Gray; // SYSWBorder = 7.
    private const ConsoleColor BorderBg = ConsoleColor.Black;
    private const ConsoleColor ContentFg = ConsoleColor.Gray; // SYSDispWind = 23.
    private const ConsoleColor ContentBg = ConsoleColor.DarkBlue;
    private const ConsoleColor SelectedFg = ConsoleColor.Black; // SYSDispSelect = 112.
    private const ConsoleColor SelectedBg = ConsoleColor.Gray;

    private readonly string _title;
    private readonly ShipCounts _fleetShips;
    private readonly CargoHold _fleetCargo;
    private readonly ShipCounts _groundShips;
    private readonly CargoHold _groundCargo;
    private readonly bool _groundIsPlayerOwned;
    private readonly bool _groundIsAFleet;
    private readonly Action _onCommitted;

    private int _pointIndex;
    private string? _editBuffer;
    private string _error = string.Empty;

    public bool IsDismissed { get; private set; }

    public ResourceDistributionOverlay(string title, ShipCounts fleetShips, CargoHold fleetCargo,
        ShipCounts groundShips, CargoHold groundCargo, bool groundIsPlayerOwned, bool groundIsAFleet, Action onCommitted)
    {
        _title = title;
        _fleetShips = fleetShips;
        _fleetCargo = fleetCargo;
        _groundShips = groundShips;
        _groundCargo = groundCargo;
        _groundIsPlayerOwned = groundIsPlayerOwned;
        _groundIsAFleet = groundIsAFleet;
        _onCommitted = onCommitted;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_editBuffer is not null)
        {
            HandleEditKey(key);
            return;
        }

        var shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
        var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                _pointIndex = _pointIndex > 0 ? _pointIndex - 1 : ResourceColumn.All.Length - 1;
                return;
            case ConsoleKey.RightArrow:
                _pointIndex = _pointIndex < ResourceColumn.All.Length - 1 ? _pointIndex + 1 : 0;
                return;
            case ConsoleKey.UpArrow when shift && !ctrl:
                Step(100);
                return;
            case ConsoleKey.DownArrow when shift && !ctrl:
                Step(-100);
                return;
            case ConsoleKey.UpArrow when ctrl && !shift:
                Step(1000);
                return;
            case ConsoleKey.DownArrow when ctrl && !shift:
                Step(-1000);
                return;
            case ConsoleKey.UpArrow:
                ResourceDistribution.FillFleet(ResourceColumn.All[_pointIndex], _fleetShips, _fleetCargo, _groundShips, _groundCargo, _groundIsPlayerOwned);
                _error = _groundIsPlayerOwned ? string.Empty : "This is not your territory.";
                return;
            case ConsoleKey.DownArrow:
                ResourceDistribution.EmptyFleet(ResourceColumn.All[_pointIndex], _fleetShips, _fleetCargo, _groundShips, _groundCargo, _groundIsAFleet);
                _error = string.Empty;
                return;
            case ConsoleKey.Enter:
                _editBuffer = string.Empty;
                return;
            case ConsoleKey.Escape:
                TryFinish();
                return;
        }

        var ch = key.KeyChar;
        if (ch is '+' or '-' || char.IsDigit(ch))
        {
            _editBuffer = ch.ToString();
        }
        else if (ch is 'x' or 'X')
        {
            TryFinish();
        }
    }

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
                CommitEdit(0);
                return;
            case ConsoleKey.Enter:
                SubmitEdit();
                return;
        }

        var ch = key.KeyChar;
        if (char.IsDigit(ch) || (ch is '+' or '-' && _editBuffer!.Length == 0))
        {
            _editBuffer += ch;
        }
    }

    private void SubmitEdit()
    {
        if (_editBuffer!.Length == 0 || _editBuffer is "+" or "-")
        {
            CommitEdit(0);
            return;
        }

        if (!int.TryParse(_editBuffer, out var amount))
        {
            _error = $"numbers from -{ResourceDistribution.MaxResources} to {ResourceDistribution.MaxResources}.";
            _editBuffer = string.Empty;
            return;
        }

        CommitEdit(amount);
    }

    private void CommitEdit(int amount)
    {
        var column = ResourceColumn.All[_pointIndex];
        if (amount != 0 && !ResourceDistribution.TryTransfer(column, _fleetShips, _fleetCargo, _groundShips, _groundCargo, _groundIsPlayerOwned, amount, out var error))
        {
            _error = error;
            _editBuffer = string.Empty;
            return;
        }

        _editBuffer = null;
        _error = string.Empty;
    }

    private void Step(int amount)
    {
        var column = ResourceColumn.All[_pointIndex];
        var onGround = column.Get(_groundShips, _groundCargo);
        var inFleet = column.Get(_fleetShips, _fleetCargo);

        int actual;
        if (amount > 0)
        {
            if (!_groundIsPlayerOwned)
            {
                _error = "This is not your territory.";
                return;
            }

            actual = Math.Min(amount, onGround);
        }
        else
        {
            actual = -Math.Min(-amount, inFleet);
        }

        if (actual != 0)
        {
            ResourceDistribution.TryTransfer(column, _fleetShips, _fleetCargo, _groundShips, _groundCargo, _groundIsPlayerOwned, actual, out _);
        }

        _error = string.Empty;
    }

    private void TryFinish()
    {
        if (FleetLogistics.FleetCargoSpace(_fleetShips, _fleetCargo) < 0)
        {
            _error = "There aren't enough transports in the fleet.";
            return;
        }

        if (_groundIsAFleet && FleetLogistics.FleetCargoSpace(_groundShips, _groundCargo) < 0)
        {
            if (!_groundIsPlayerOwned)
            {
                FleetLogistics.BalanceFleet(_groundShips, _groundCargo);
            }
            else
            {
                _error = "There aren't enough transports left in the fleet.";
                return;
            }
        }

        IsDismissed = true;
        _onCommitted();
    }

    public void Draw(FrameBuffer fb)
    {
        var w = Math.Min(FrameWidth, fb.Width);
        var h = Math.Min(FrameHeight, fb.Height);
        var x = Math.Max(0, (fb.Width - w) / 2);
        var y = Math.Max(0, (fb.Height - h) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, w, h, BorderFg, BorderBg);
        var titleText = $" {_title} ";
        fb.DrawText(x + Math.Max(1, (w - titleText.Length) / 2), y, titleText, ConsoleColor.White, BorderBg);

        var cx = x + 1;
        var cy = y + 1;
        var cw = w - 2;
        var ch = h - 2;
        for (var row = 0; row < ch; row++)
        {
            fb.DrawText(cx, cy + row, new string(' ', cw), ContentFg, ContentBg);
        }

        void At(int px, int py, string text)
        {
            if (px >= cw)
            {
                return;
            }

            fb.DrawText(cx + px, cy + py, text, ContentFg, ContentBg, maxWidth: cw - px);
        }

        At(0, 0, "  fgt   hk  jmp  jtn  pen  str  trn  men ninj  amb  che  met  sup  tri  Cargo");

        for (var i = 0; i < ResourceColumn.All.Length; i++)
        {
            var column = ResourceColumn.All[i];
            var fleetText = $"{column.Get(_fleetShips, _fleetCargo),5}";
            var groundText = _groundIsPlayerOwned ? $"{column.Get(_groundShips, _groundCargo),5}" : " ????";
            var selected = i == _pointIndex;
            var fg = selected ? SelectedFg : ContentFg;
            var bg = selected ? SelectedBg : ContentBg;
            if (cx + i * 5 < x + w - 1)
            {
                fb.DrawText(cx + i * 5, cy + 1, fleetText, fg, bg);
                fb.DrawText(cx + i * 5, cy + 2, groundText, fg, bg);
            }
        }

        At(ResourceColumn.All.Length * 5, 1, $"{FleetLogistics.FleetCargoSpace(_fleetShips, _fleetCargo),4} ");
        var groundCargoSpace = _groundIsAFleet && _groundIsPlayerOwned ? FleetLogistics.FleetCargoSpace(_groundShips, _groundCargo) : 0;
        At(ResourceColumn.All.Length * 5, 2, $"{groundCargoSpace,4} ");

        At(0, 8, "<RETURN> to select ship/cargo to change.");
        At(0, 9, "<Esc> to end transfer.");
        At(0, 10, "   +### to transfer from ground to fleet.");
        At(0, 11, "   -### to transfer from fleet to ground.");

        At(51, 8, "Transport Capacity:");
        var cargoRow = 9;
        foreach (var column in ResourceColumn.All)
        {
            if (column.CargoSpacePerUnit is { } space)
            {
                At(51, cargoRow++, $"{space,3} {column.Name}");
            }
        }

        if (_editBuffer is not null)
        {
            At(0, 15, $"How many {ResourceColumn.All[_pointIndex].Name} to transfer : {_editBuffer}");
        }

        if (_error.Length > 0)
        {
            At(0, 17, _error);
        }
    }
}
