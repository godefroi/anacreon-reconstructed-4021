using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui.Overlays;


// Fleet menu > Resupply's own cargo-type-and-amount step (GameShell.PickResupplyCargoAndAmount) --
// a list of cargo types (how much is available at the source, the actual max you could shuttle --
// fleet room capped at source availability, issue #60 -- and how much the destination already has on
// hand -- port-only addition, no Pascal precedent, since that's useful context for deciding whether a
// shuttle run is even worth it), Enter on a row moves into an amount field pre-filled with that row's
// own max; Enter there commits, Esc there backs out to the list, Esc on the list cancels the whole
// thing.
internal sealed class ResupplyCargoOverlay : IOverlay
{
    private const int Width = 60;
    private static readonly int ListHeight = Enum.GetValues<CargoType>().Length;

    // header(1) + list(ListHeight) + blank/label/field/error/footer(5) + border(2).
    private static readonly int Height = ListHeight + 1 + 5 + 2;

    // "megatons of chemicals" and friends run well past a flat guess (GameShell's own
    // CargoTypeListItem.NameColumnWidth hit this same "Available column didn't line up" bug first) --
    // sized to the longest real display name instead.
    private static readonly int NameColumnWidth = Enum.GetValues<CargoType>().Max(t => new ResourceKind.Cargo(t).DisplayName.Length) + 1;

    private sealed record Row(CargoType Type, int Available, int MaxAmount, int DestAmount)
    {
        public string DisplayName => new ResourceKind.Cargo(Type).DisplayName;
    }

    private readonly string _sourceName;
    private readonly Action<CargoType, int> _onCommitted;
    private readonly ListBox<Row> _list;
    private TextInputField? _amountField;
    private string _error = string.Empty;

    public bool IsDismissed { get; private set; }

    public ResupplyCargoOverlay(Planet source, Planet destination, Fleet fleet, string sourceName, Action<CargoType, int> onCommitted)
    {
        _sourceName = sourceName;
        _onCommitted = onCommitted;
        var rows = Enum.GetValues<CargoType>().Select(t => new Row(t, source.Cargo[t],
            Math.Max(0, Math.Min(Math.Min(FleetLogistics.FleetCargoSpaceFor(t, fleet.Ships, fleet.Cargo), PascalMath.MaxResources - fleet.Cargo[t]), source.Cargo[t])),
            destination.Cargo[t])).ToList();
        _list = new ListBox<Row>(rows, FormatRow);
    }

    private static string FormatRow(Row row) => $"{row.DisplayName.PadRight(NameColumnWidth)}{row.Available,9}  {row.MaxAmount,9}  {row.DestAmount,9}";

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_amountField is not null)
        {
            HandleAmountKey(key);
            return;
        }

        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem is { } row:
                if (row.Available <= 0)
                {
                    _error = $"No {row.DisplayName} available at {_sourceName}.";
                    return;
                }

                if (row.MaxAmount <= 0)
                {
                    _error = "The fleet has no room for that.";
                    return;
                }

                _error = string.Empty;
                _amountField = new TextInputField(row.MaxAmount.ToString(), 5);
                return;
            case ConsoleKey.Escape:
                IsDismissed = true;
                return;
        }
    }

    private void HandleAmountKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _amountField = null;
                _error = string.Empty;
                return;
            case ConsoleKey.Enter:
                SubmitAmount();
                return;
        }

        _amountField!.HandleKey(key);
    }

    private void SubmitAmount()
    {
        var row = _list.SelectedItem!;
        var text = _amountField!.Text.Trim();

        int amount;
        if (text.Length == 0)
        {
            amount = row.MaxAmount;
        }
        else if (!int.TryParse(text, out amount))
        {
            _error = "Enter a whole number.";
            return;
        }
        else if (amount == 0)
        {
            amount = row.MaxAmount;
        }
        else if (amount < 0)
        {
            _error = "Enter a positive number.";
            return;
        }
        else if (amount > row.MaxAmount)
        {
            _error = $"The maximum amount allowable is {row.MaxAmount}.";
            return;
        }

        IsDismissed = true;
        _onCommitted(row.Type, amount);
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 10) / 2), y, " Resupply ", ConsoleColor.White, ConsoleColor.Black);

        fb.DrawText(x + 1, y + 1, $"{"Cargo".PadRight(NameColumnWidth)}{"Available",9}  {"Max",9}  {"Dest",9}", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        _list.Draw(fb, x + 1, y + 2, width - 2, ListHeight, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);

        var row = y + 2 + ListHeight;
        if (_amountField is { } field)
        {
            var selected = _list.SelectedItem!;
            fb.DrawText(x + 1, row + 1, $"{selected.DisplayName} to shuttle from {_sourceName} (max {selected.MaxAmount}):", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
            field.Draw(fb, x + 1, row + 2, width - 2, TextInputField.DefaultFg, TextInputField.DefaultBg);
            fb.DrawText(x + 1, row + 3, _error, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
            fb.DrawText(x + 1, row + 4, "Enter: confirm   Esc: back", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }
        else
        {
            fb.DrawText(x + 1, row + 3, _error, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
            fb.DrawText(x + 1, row + 4, "Enter: select   Esc: cancel", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }
    }
}
