using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// Worlds/Ministry menu bar's F8 shortcut (EMPWIND.PAS: EmpireWindow). Single-pane, unlike
/// Status/Fleet's two-pane split -- one row per <see cref="EmpireWindowReport.BuildRows"/> entry, the
/// viewer's own row first (always full detail) then every other still-active empire whose capital the
/// viewer knows about, each full or redacted depending on whether that capital is currently scouted
/// (<c>GetEmpireStatusLine</c>'s own <c>Full</c> parameter). At most 8 rows total (one viewer plus up
/// to 7 others -- real Pascal only ever has 8 empire slots), so unlike Status/Fleet/News this never
/// scrolls; no viewport/<c>_beginIndex</c> needed.
///
/// The column header is a plain content <see cref="Label"/>, not <see cref="View.Title"/>, matching
/// the header-alignment fix already applied to Status/Fleet -- Title's own bracket glyphs sit at a
/// different offset than a content row's left edge.
///
/// Port-only addition, no Pascal equivalent: Up/Down move a highlighted-row marker, and Enter opens
/// the highlighted empire's own capital in Close Up via <paramref name="onSelectCapital"/> -- real
/// Pascal's own EmpireWindow is display-only, matching the same addition already made to
/// Status/Fleet/News.
/// </summary>
internal sealed class EmpireWindow : Window
{
    private const int HeaderRow = 0;
    private const int DataStartRow = HeaderRow + 1;

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);

    // DATACNST.PAS's TechStr -- ordinal-aligned with TechLevel (already verified against the Pascal
    // enum's own declared order for StatusWindow's identical copy).
    private static readonly string[] TechCodes = ["pt", " p", "pa", " a", "pw", " w", " j", " b", " s", "pg", " g"];
    private static readonly ShipType[] ShipColumns = [ShipType.Fighter, ShipType.HunterKiller, ShipType.Jumpship, ShipType.Jumptransport, ShipType.Penetrator, ShipType.Starship, ShipType.Transport];

    private readonly Game _game;
    private readonly IReadOnlyList<EmpireWindowReport.Row> _rows;
    private readonly Action<IEconomicWorld> _onSelectCapital;
    private readonly Label[] _rowLabels;
    private int _selectedIndex;

    public EmpireWindow(Game game, Empire viewer, Action<IEconomicWorld> onSelectCapital)
    {
        _game = game;
        _onSelectCapital = onSelectCapital;
        _rows = EmpireWindowReport.BuildRows(game, viewer);
        _rowLabels = new Label[_rows.Count];

        Title = "Empire";
        Width = 82; // real Pascal's own header/row text is 80 columns wide, sized for its bare 80-column screen; widened by 2 so nothing runs into the border.
        Height = _rows.Count + 3;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        var header = new Label {
            X = 0, Y = HeaderRow,
            Text = "Empire       Tl Pln SInd   Pop    fgt    hkr    jmp    jtn    pen    str    trn ",
        };
        header.SetScheme(new Scheme(BorderAttribute));
        Add(header);

        for (var i = 0; i < _rows.Count; i++) {
            _rowLabels[i] = new Label { X = 0, Y = DataStartRow + i, Text = string.Empty };
            Add(_rowLabels[i]);
        }

        Redraw();

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorUp: SetSelection(_selectedIndex - 1); key.Handled = true; break;
                case KeyCode.CursorDown: SetSelection(_selectedIndex + 1); key.Handled = true; break;
                case KeyCode.Home: SetSelection(0); key.Handled = true; break;
                case KeyCode.End: SetSelection(_rows.Count - 1); key.Handled = true; break;
                case KeyCode.Enter when _rows.Count > 0 && _rows[_selectedIndex].Empire.Capital is { } capital:
                    _onSelectCapital(capital);
                    key.Handled = true;
                    break;
            }
        };
    }

    private void SetSelection(int index)
    {
        if (_rows.Count == 0) {
            return;
        }

        _selectedIndex = Math.Clamp(index, 0, _rows.Count - 1);
        Redraw();
    }

    private void Redraw()
    {
        for (var i = 0; i < _rows.Count; i++) {
            var attribute = i == _selectedIndex ? SelectedAttribute : DispWindAttribute;
            _rowLabels[i].SetScheme(new Scheme(attribute));
            _rowLabels[i].Text = FormatRow(_rows[i]);
        }
    }

    private string FormatRow(EmpireWindowReport.Row row)
    {
        var emp = row.Empire;
        var name = emp.Name.PadRight(12)[..12];
        var capital = emp.Capital;
        var techCode = capital is not null ? TechCodes[(int)capital.TechLevel] : TechCodes[(int)TechLevel.PreTech];
        var (planets, totalPop, shipyardIndustry, totalShips) = EmpireWindowReport.GetEmpireStatus(emp, _game);

        var tail = row.Full
            ? string.Concat(ShipColumns.Select(t => $"{totalShips[t],6} "))
            : " ----   ----   ----   ----   ----   ----   ----  ";

        return $"{name} {techCode} {planets,3} {shipyardIndustry / 10.0,4:F1} {totalPop / 100.0,5:F1} {tail}";
    }
}
