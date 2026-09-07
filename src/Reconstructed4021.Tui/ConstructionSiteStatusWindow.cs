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
/// Build menu &gt; Site Status (CONSTR.PAS: ConstrStatusCommand). Single-pane, same up/down/PageUp/
/// PageDown/Home/End scrolling and Enter-jumps-to-Close-Up convention as <see cref="NewsWindow"/> --
/// one flat list, no per-headline dispatch to reproduce here.
///
/// The "materials needed" columns are <c>ConsCargoNeeded[Building] - CargoAvail</c>, clamped at zero
/// (<c>GreaterInt(0,CargoNeeded)</c>) -- <c>CargoAvail</c> is the same-owner fleets already sitting at
/// the site's own location, matching <c>ConstrStatusCommand</c>'s own <c>GetFleets</c>/<c>CargoFleets
/// * SetOfFleetsOf[Player]</c> exactly. che/met/tri only (men/nnj/amb/sup are never drawn on by
/// construction, matching <see cref="ConstructionCatalog.RawMaterialPerYear"/>'s own shape).
/// </summary>
internal sealed class ConstructionSiteStatusWindow : Window
{
    private const int NoOfLines = 15;

    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue);
    private static readonly TgAttribute DispHighAttribute = new(StandardColor.White, StandardColor.Blue);
    private static readonly TgAttribute BorderAttribute = new(StandardColor.LightGray, StandardColor.Black);
    private static readonly TgAttribute SelectedAttribute = new(StandardColor.Black, StandardColor.LightGray);

    private readonly Game _game;
    private readonly Empire _viewer;
    private readonly Action<ISectorObject> _onSelectSite;
    private readonly IReadOnlyList<ConstructionSite> _rows;
    private readonly Label[] _lineLabels = new Label[NoOfLines];
    private int _beginIndex;
    private int _selectedIndex;

    public ConstructionSiteStatusWindow(Game game, Empire viewer, Action<ISectorObject> onSelectSite)
    {
        _game = game;
        _viewer = viewer;
        _onSelectSite = onSelectSite;
        _rows = game.Galaxy.ConstructionSites.Where(c => ReferenceEquals(c.Owner, viewer)).ToList();

        Title = "Construction Status";
        Width = 80;
        Height = NoOfLines + 4;
        X = Pos.Center();
        Y = Pos.Center();
        BorderStyle = LineStyle.Single;
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));
        Border.View?.SetScheme(new Scheme(BorderAttribute));

        if (_rows.Count == 0) {
            Add(new Label { X = 0, Y = 0, Text = $"{Honorifics.MyLord(viewer.IsEmpress)}, there are no active construction sites." });
        } else {
            var header = new Label { X = 0, Y = 0, Text = "Site      Type                Completion         che  met  tri" };
            header.SetScheme(new Scheme(DispHighAttribute));
            Add(header);
        }

        for (var i = 0; i < NoOfLines; i++) {
            _lineLabels[i] = new Label { X = 0, Y = i + 1, Text = string.Empty };
            Add(_lineLabels[i]);
        }

        Redraw();

        KeyDown += (_, key) => {
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.CursorUp: MoveSelection(-1); key.Handled = true; break;
                case KeyCode.CursorDown: MoveSelection(1); key.Handled = true; break;
                case KeyCode.PageUp: MoveSelection(-NoOfLines); key.Handled = true; break;
                case KeyCode.PageDown: MoveSelection(NoOfLines); key.Handled = true; break;
                case KeyCode.Home: SetSelection(0); key.Handled = true; break;
                case KeyCode.End: SetSelection(_rows.Count - 1); key.Handled = true; break;
                case KeyCode.Enter when _rows.Count > 0:
                    _onSelectSite(_rows[_selectedIndex]);
                    key.Handled = true;
                    break;
            }
        };
    }

    private int MaxBeginIndex() => Math.Max(0, _rows.Count - NoOfLines);

    private void MoveSelection(int delta) => SetSelection(_selectedIndex + delta);

    private void SetSelection(int index)
    {
        if (_rows.Count == 0) {
            return;
        }

        _selectedIndex = Math.Clamp(index, 0, _rows.Count - 1);
        if (_selectedIndex < _beginIndex) {
            _beginIndex = _selectedIndex;
        } else if (_selectedIndex >= _beginIndex + NoOfLines) {
            _beginIndex = _selectedIndex - NoOfLines + 1;
        }
        _beginIndex = Math.Clamp(_beginIndex, 0, MaxBeginIndex());
        Redraw();
    }

    private void Redraw()
    {
        for (var i = 0; i < NoOfLines; i++) {
            var index = _beginIndex + i;
            var selected = index == _selectedIndex;
            _lineLabels[i].SetScheme(new Scheme(selected ? SelectedAttribute : DispWindAttribute));
            _lineLabels[i].Text = index < _rows.Count ? FormatLine(_rows[index]) : string.Empty;
        }
    }

    private string FormatLine(ConstructionSite site)
    {
        var name = (site.Names.GetValueOrDefault(_viewer) ?? CloseUpWindow.DescribeLocation(site, _viewer)).PadRight(10)[..10];
        var type = ConstructionCatalog.DisplayName(site.Building).PadRight(20)[..20];
        var completion = (_game.Year + site.YearsToCompletion).ToString().PadRight(19);

        var needed = ConstructionCatalog.RawMaterialPerYear[site.Building];
        var available = new Dictionary<CargoType, int>();
        foreach (var fleet in _game.Galaxy.Fleets.Where(f => f.Location == site.Location && ReferenceEquals(f.Owner, site.Owner))) {
            foreach (var cargoType in needed.Keys) {
                available[cargoType] = available.GetValueOrDefault(cargoType) + fleet.Cargo[cargoType];
            }
        }

        string Remaining(CargoType type) =>
            Math.Max(0, needed.GetValueOrDefault(type, 0) - available.GetValueOrDefault(type, 0)).ToString().PadLeft(5);

        return $"{name}{type}{completion}{Remaining(CargoType.Chemicals)}{Remaining(CargoType.Metals)}{Remaining(CargoType.Trillum)}";
    }
}
