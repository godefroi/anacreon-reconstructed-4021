using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core.Entities;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds menu > ISSP (DESIGN.PAS: ChangeISSPCom, :263-390): a 4-row stepper over
/// <see cref="SelfSufficiencySettings"/>'s own Chemical/Metal(Mining)/Supply/Trillum dials, each
/// 0-10 (<see cref="SelfSufficiencySettings.Multipliers"/>'s own 11 bands). Left/Right nudges the
/// selected row by one step, clamped at both ends (no wrap, unlike the row cursor itself); Up/Down/
/// PageUp/PageDown moves the row cursor, wrapping. No separate commit/cancel step -- edits apply
/// directly to <see cref="settings"/> as they're made (ChangeISSPCom's own DisplayISSP loop always
/// applies whatever's currently set regardless of Esc vs Enter, so there was never really a
/// "cancel" to preserve). Switching away to another tab in <see cref="WorldInfoWindow"/> just leaves
/// whatever's set.
///
/// Only reachable for a <see cref="Planet"/>: real Pascal's own GetISSP/SetISSP hardcode a
/// starbase's own dial at 0 with no real per-starbase field to write (<see cref="IEconomicWorld"/>'s
/// own doc comment) -- GameShell's own caller only ever constructs this over
/// <see cref="Planet.SelfSufficiency"/>, the one place a settable dial actually exists.
///
/// A plain <see cref="View"/>, not a <see cref="Window"/>: this is one tab page swapped into
/// <see cref="WorldInfoWindow"/>'s content area, which already draws the border -- an inner Window
/// border here would double up against that.
/// </summary>
internal sealed class IsspEditor : View
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray); // SYSDispSelect = 112

    // ChangeISSPCom's own ISSPStr (DESIGN.PAS:305-317), minus its fixed trailing padding.
    private static readonly string[] BandDescriptions = [
        "1%  (imports 99% of need)",
        "10%  (imports 90% of need)",
        "25%  (imports 75% of need)",
        "50%  (imports 50% of need)",
        "75%  (imports 25% of need)",
        "100%  (no import/export)",
        "150%  (exports 33% of production)",
        "200%  (exports 50% of production)",
        "300%  (exports 67% of production)",
        "400%  (exports 75% of production)",
        "500%  (exports 80% of production)",
    ];

    // GetISSPArray/SetISSPArray's own fixed row order (DESIGN.PAS:278-292): Chemical, Mining, Supply, Trillum.
    private static readonly (string Name, Func<SelfSufficiencySettings, int> Get, Action<SelfSufficiencySettings, int> Set)[] Rows = [
        ("Chemical industry:", s => s.Chemical, (s, v) => s.Chemical = v),
        ("Mining industry:", s => s.Metal, (s, v) => s.Metal = v),
        ("Supply industry:", s => s.Supply, (s, v) => s.Supply = v),
        ("Trillum industry:", s => s.Trillum, (s, v) => s.Trillum = v),
    ];

    private readonly SelfSufficiencySettings settings;
    private readonly Label[] rowLabels = new Label[Rows.Length];
    private int row;

    public IsspEditor(SelfSufficiencySettings settings)
    {
        this.settings = settings;

        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        // Deliberately doesn't say "imports"/"exports" happen -- confirmed against source and the
        // manual: nothing ships anywhere on its own except a narrow starbase/adjacency case this
        // screen has no bearing on (SupplyLink/SurplusLink, UPDATE.PAS:517-604). This dial only sets
        // how much the world itself produces versus needs; moving the difference is still the
        // player's own job, by transport fleet.
        Add(new Label {
            X = 1, Y = 0, Width = Dim.Fill(1), Height = 4,
            Text = "How much of a raw material a world produces relative to what it needs. Below 100%, the shortfall must be shipped in by transport; above 100%, the surplus sits there for you to ship out.",
        });

        for (var i = 0; i < Rows.Length; i++) {
            rowLabels[i] = new Label { X = 1, Y = 5 + i, Text = string.Empty };
            Add(rowLabels[i]);
        }

        Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Up/Down: select industry   Left/Right: change" });

        KeyDown += OnKeyDown;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        for (var i = 0; i < Rows.Length; i++) {
            var value = Rows[i].Get(settings);
            rowLabels[i].Text = $"{Rows[i].Name,-19}{BandDescriptions[value]}";
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
        // Ctrl+PageUp/Ctrl+PageDown is WorldInfoWindow's own tab-switch chord -- bail before the
        // modifier-stripped switch below would otherwise treat it as bare PageUp/PageDown and
        // swallow it here instead of letting it bubble up.
        if (key.IsCtrl) {
            return;
        }

        switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
            case KeyCode.CursorLeft: {
                var value = Rows[row].Get(settings);
                if (value > 0) {
                    Rows[row].Set(settings, value - 1);
                    UpdateDisplay();
                }
                key.Handled = true;
                return;
            }
            case KeyCode.CursorRight: {
                var value = Rows[row].Get(settings);
                if (value < SelfSufficiencySettings.Multipliers.Length - 1) {
                    Rows[row].Set(settings, value + 1);
                    UpdateDisplay();
                }
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
        }
    }
}
