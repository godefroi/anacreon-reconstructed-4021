using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Presentation;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;


namespace Reconstructed4021.Tui2.Overlays;


// Build menu > Site Status (CONSTR.PAS: ConstrStatusCommand), ported from Reconstructed4021.Tui's own
// ConstructionSiteStatusWindow. Single-pane, same Enter-jumps-to-Close-Up convention as NewsOverlay --
// one flat list, no per-headline dispatch to reproduce here.
//
// The "materials needed" columns are ConsCargoNeeded[Building] - CargoAvail, clamped at zero
// (GreaterInt(0,CargoNeeded)) -- CargoAvail is the same-owner fleets already sitting at the site's own
// location, matching ConstrStatusCommand's own GetFleets/CargoFleets * SetOfFleetsOf[Player] exactly.
// che/met/tri only (men/nnj/amb/sup are never drawn on by construction, matching
// ConstructionCatalog.RawMaterialPerYear's own shape).
internal sealed class ConstructionSiteStatusOverlay : IOverlay
{
    private const int Width = 82;
    private const string Header = "Site      Type                Completion         che  met  tri";

    private readonly Game _game;
    private readonly Empire _viewer;
    private readonly Action<ISectorObject> _onSelectSite;
    private readonly ListBox<ConstructionSite> _list;

    public bool IsDismissed { get; private set; }

    public ConstructionSiteStatusOverlay(Game game, Empire viewer, Action<ISectorObject> onSelectSite)
    {
        _game = game;
        _viewer = viewer;
        _onSelectSite = onSelectSite;
        var rows = game.Galaxy.ConstructionSites.Where(c => ReferenceEquals(c.Owner, viewer)).ToList();
        _list = new ListBox<ConstructionSite>(rows, FormatLine);
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter when _list.SelectedItem is { } site:
                _onSelectSite(site);
                break;
            case ConsoleKey.Escape:
                IsDismissed = true;
                break;
        }
    }

    private string FormatLine(ConstructionSite site)
    {
        var name = CloseUpOverlay.DisplayName(site, _viewer).PadRight(10)[..10];
        var type = ConstructionCatalog.DisplayName(site.Building).PadRight(20)[..20];
        var completion = (_game.Year + site.YearsToCompletion).ToString().PadRight(19);

        var needed = ConstructionCatalog.RawMaterialPerYear[site.Building];
        var available = new Dictionary<CargoType, int>();
        foreach (var fleet in _game.Galaxy.Fleets.Where(f => f.Location == site.Location && ReferenceEquals(f.Owner, site.Owner)))
        {
            foreach (var cargoType in needed.Keys)
            {
                available[cargoType] = available.GetValueOrDefault(cargoType) + fleet.Cargo[cargoType];
            }
        }

        string Remaining(CargoType type) =>
            Math.Max(0, needed.GetValueOrDefault(type, 0) - available.GetValueOrDefault(type, 0)).ToString().PadLeft(5);

        return $"{name}{type}{completion}{Remaining(CargoType.Chemicals)}{Remaining(CargoType.Metals)}{Remaining(CargoType.Trillum)}";
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Math.Max(_list.Items.Count, 1) + 3, fb.Height - 2);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(x + Math.Max(1, (width - 17) / 2), y, " Construction Status ", ConsoleColor.White, ConsoleColor.Black);

        if (_list.Items.Count == 0)
        {
            fb.DrawText(x + 1, y + 1, $"{Honorifics.MyLord(_viewer.IsEmpress)}, there are no active construction sites.", ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
            return;
        }

        fb.DrawText(x + 1, y + 1, Header, ConsoleColor.White, ConsoleColor.Black, maxWidth: width - 2);
        _list.Draw(fb, x + 1, y + 2, width - 2, height - 3, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);
    }
}
