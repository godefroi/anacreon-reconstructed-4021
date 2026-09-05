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
/// PageUp/PageDown moves the row cursor, wrapping. Esc or Enter both exit -- ChangeISSPCom's own
/// DisplayISSP loop always applies whatever's currently set (<c>SetISSPArray</c> runs unconditionally
/// right after), so there's no cancel path here either, same as Fleet Group Configuration/Defenses.
///
/// Only reachable for a <see cref="Planet"/>: real Pascal's own GetISSP/SetISSP hardcode a
/// starbase's own dial at 0 with no real per-starbase field to write (<see cref="IEconomicWorld"/>'s
/// own doc comment) -- GameShell's own caller only ever constructs this over
/// <see cref="Planet.SelfSufficiency"/>, the one place a settable dial actually exists.
/// </summary>
internal sealed class IsspEditor : Window
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black); // SYSWBorder = 7
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

    /// <summary>Fired on Esc or Enter -- ChangeISSPCom's own unconditional SetISSPArray, already live since this edits <see cref="settings"/> in place.</summary>
    public event EventHandler? Done;

    // ProductionCom's own header line ('Production: '+Name, CLSCOMM.PAS:124) shows which world a
    // per-world screen is for -- real ChangeISSPCom's own title is bare "ISSP:" with no name at all,
    // but that's fine for a single-player typing a command by hand; here, with no way to ask "which
    // world?" first, showing the name avoids editing the wrong one by mistake.
    public IsspEditor(SelfSufficiencySettings settings, string worldName)
    {
        this.settings = settings;

        Title = $"ISSP: {worldName}";
        Width = 66;
        Height = 13;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        Add(new Label {
            X = 1, Y = 0, Width = Dim.Fill(1), Height = 3,
            Text = "How much of a raw material a world produces relative to what it needs -- under 100% imports the rest, over 100% exports the surplus.",
        });

        for (var i = 0; i < Rows.Length; i++) {
            rowLabels[i] = new Label { X = 1, Y = 4 + i, Text = string.Empty };
            Add(rowLabels[i]);
        }

        Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Up/Down: select industry   Left/Right: change   Esc/Enter: done" });

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
            case KeyCode.Enter:
            case KeyCode.Esc:
                Done?.Invoke(this, EventArgs.Empty);
                key.Handled = true;
                return;
        }
    }
}
