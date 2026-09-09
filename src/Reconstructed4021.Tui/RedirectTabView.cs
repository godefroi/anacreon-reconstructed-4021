using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds menu > Redirect (GitHub issue #8 -- no Pascal equivalent, see <see cref="RedirectionSettings"/>'s
/// own doc comment): a row-per-type stepper over <see cref="RedirectionSettings.Ships"/>/
/// <see cref="RedirectionSettings.IncludeLegions"/>/<see cref="RedirectionSettings.IncludeNinjaLegions"/>,
/// modeled directly on <see cref="IsspEditor"/>'s own Up/Down-select, Left/Right-cycle pattern. Only
/// reachable for a <see cref="Planet"/> -- see <see cref="WorldInfoWindow"/>'s own ISSP-tab precedent for
/// why (starbases have no equivalent settable field).
/// </summary>
internal sealed class RedirectTabView : View
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray); // SYSDispSelect = 112

    private static readonly RedirectionMode[] YesNo = [RedirectionMode.No, RedirectionMode.Yes];
    private static readonly RedirectionMode[] YesNoAsNeeded = [RedirectionMode.No, RedirectionMode.Yes, RedirectionMode.AsNeeded];

    private sealed record Row(string Name, Func<RedirectionSettings, RedirectionMode> Get, Action<RedirectionSettings, RedirectionMode> Set, RedirectionMode[] Cycle);

    private static Row ShipRow(string name, ShipType type, RedirectionMode[] cycle) => new(
        name,
        s => s.Ships.GetValueOrDefault(type, RedirectionMode.No),
        (s, v) => s.Ships[type] = v,
        cycle);

    private static readonly Row[] Rows = [
        ShipRow("Fighters:", ShipType.Fighter, YesNo),
        ShipRow("Hunter-killers:", ShipType.HunterKiller, YesNo),
        ShipRow("Jumpships:", ShipType.Jumpship, YesNo),
        ShipRow("Jumptransports:", ShipType.Jumptransport, YesNoAsNeeded),
        ShipRow("Penetrators:", ShipType.Penetrator, YesNo),
        ShipRow("Starships:", ShipType.Starship, YesNo),
        ShipRow("Transports:", ShipType.Transport, YesNoAsNeeded),
        new("Legions:", s => s.IncludeLegions ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.IncludeLegions = v == RedirectionMode.Yes, YesNo),
        new("Ninja legions:", s => s.IncludeNinjaLegions ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.IncludeNinjaLegions = v == RedirectionMode.Yes, YesNo),
        new("Join on arrival:", s => s.JoinOnArrival ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.JoinOnArrival = v == RedirectionMode.Yes, YesNo),
        new("Preserve overflow:", s => s.PreserveOverflowOnJoin ? RedirectionMode.Yes : RedirectionMode.No,
            (s, v) => s.PreserveOverflowOnJoin = v == RedirectionMode.Yes, YesNo),
    ];

    private static string DisplayText(RedirectionMode mode) => mode switch {
        RedirectionMode.No => "No",
        RedirectionMode.Yes => "Yes",
        RedirectionMode.AsNeeded => "As needed",
        _ => mode.ToString(),
    };

    private readonly RedirectionSettings settings;
    private readonly Coordinate origin;
    private readonly Label destinationLabel;
    private readonly Label[] rowLabels = new Label[Rows.Length];
    private int row;

    /// <summary>Raised when the player asks to (re)pick the destination -- <see cref="GameShell"/> owns
    /// the map-cursor picker, so it closes this window, runs the pick, and reopens back on this tab (same
    /// shape as Deploy Fleet's own map-cursor destination pick).</summary>
    public event EventHandler? PickDestinationRequested;

    public RedirectTabView(RedirectionSettings settings, Coordinate origin)
    {
        this.settings = settings;
        this.origin = origin;

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        Add(new Label {
            X = 1, Y = 0, Width = Dim.Fill(1), Height = 2,
            Text = "Newly produced ships/legions/ninja are auto-dispatched here every turn -- never the planet's existing stockpile.",
        });

        destinationLabel = new Label { X = 1, Y = 3, Text = string.Empty };
        Add(destinationLabel);

        for (var i = 0; i < Rows.Length; i++) {
            rowLabels[i] = new Label { X = 1, Y = 5 + i, Text = string.Empty };
            Add(rowLabels[i]);
        }

        Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Up/Down: select   Left/Right: change   Enter: pick dest.   X: clear dest." });

        KeyDown += OnKeyDown;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        destinationLabel.Text = settings.Destination is { } d
            ? $"Destination: ({RelativeCoordinate.Format(d, origin)})"
            : "Destination: not set";

        for (var i = 0; i < Rows.Length; i++) {
            rowLabels[i].Text = $"{Rows[i].Name,-19}{DisplayText(Rows[i].Get(settings))}";
        }

        UpdatePointerHighlight();
    }

    private void UpdatePointerHighlight()
    {
        for (var i = 0; i < Rows.Length; i++) {
            rowLabels[i].SetScheme(new Scheme(i == row ? SelectedAttribute : DispWindAttribute));
            rowLabels[i].SetNeedsDraw();
        }
    }

    private void OnKeyDown(object? sender, Key key)
    {
        // Ctrl+PageUp/Ctrl+PageDown is WorldInfoWindow's own tab-switch chord -- see IsspEditor's own
        // identical guard.
        if (key.IsCtrl) {
            return;
        }

        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.CursorLeft: {
                CycleRow(-1);
                key.Handled = true;
                return;
            }
            case KeyCode.CursorRight: {
                CycleRow(1);
                key.Handled = true;
                return;
            }
            case KeyCode.CursorUp:
            case KeyCode.PageUp:
                row = row == 0 ? Rows.Length - 1 : row - 1;
                UpdatePointerHighlight();
                key.Handled = true;
                return;
            case KeyCode.CursorDown:
            case KeyCode.PageDown:
                row = row == Rows.Length - 1 ? 0 : row + 1;
                UpdatePointerHighlight();
                key.Handled = true;
                return;
            case KeyCode.Enter:
                PickDestinationRequested?.Invoke(this, EventArgs.Empty);
                key.Handled = true;
                return;
        }

        if (char.ToUpperInvariant((char)key.AsRune.Value) == 'X') {
            settings.Destination = null;
            UpdateDisplay();
            key.Handled = true;
        }
    }

    private void CycleRow(int direction)
    {
        var cycle = Rows[row].Cycle;
        var current = Array.IndexOf(cycle, Rows[row].Get(settings));
        if (current < 0) {
            current = 0;
        }

        var next = ((current + direction) % cycle.Length + cycle.Length) % cycle.Length;
        Rows[row].Set(settings, cycle[next]);
        UpdateDisplay();
    }
}
