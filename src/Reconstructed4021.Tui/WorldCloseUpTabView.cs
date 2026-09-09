using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using TgAttribute = Terminal.Gui.Drawing.Attribute;

namespace Reconstructed4021.Tui;

/// <summary>
/// "Close Up" tab inside <see cref="WorldInfoWindow"/> -- always for one of the player's own worlds
/// (<see cref="WorldInfoWindow"/>'s only caller, <c>GameShell</c>, routes anything else -- fleets,
/// other empires' worlds -- to the standalone <see cref="CloseUpWindow"/> instead, which still needs
/// its own general-purpose Known/Scouted redaction since it can't assume ownership). Because
/// ownership is a given here, this is CLSCOMM.PAS's own DisplayBasicInfo/DisplayCargoInfo/
/// DisplayMilitaryInfo/DisplayBackground with every "owned?"/"scouted?" branch collapsed to its
/// owned-and-scouted case -- a genuinely simpler screen, not a trimmed copy of
/// <see cref="CloseUpWindow"/>'s own more general one.
/// </summary>
internal sealed class WorldCloseUpTabView : View
{
    private static readonly TgAttribute DispWindAttribute = new(StandardColor.LightGray, StandardColor.Blue); // SYSDispWind = 23

    public WorldCloseUpTabView(IEconomicWorld world, Game game, Empire viewer)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
        SetScheme(new Scheme(DispWindAttribute));

        AddAt(1, 0, " Cls:"); AddAt(1, 1, "Tech:"); AddAt(1, 2, " Pop:");
        AddAt(23, 0, "Eff:"); AddAt(23, 1, "Amb:"); AddAt(23, 2, "Rev:");
        AddAt(7, 0, world.EffectiveClass.ToString());
        AddAt(7, 1, world.TechLevel.ToString());
        AddAt(7, 2, world.Population.ToString());
        AddAt(28, 0, $"{world.Efficiency}%");
        AddAt(28, 1, world.IsAddictedToAmbrosia ? "yes" : "no");
        AddAt(28, 2, world.RevolutionIndex.ToString());

        AddAt(51, 0, "amb  che  met  sup  tri");
        AddAt(49, 1, $"{world.Cargo.Ambrosia,5}{world.Cargo.Chemicals,5}{world.Cargo.Metals,5}{world.Cargo.Supplies,5}{world.Cargo.Trillum,5}");

        AddAt(2, 4, "fgt  hkr  jmp  jtn  pen  str  trn    men  nnj    LAM  def  GDM  ion");
        var s = world.Ships;
        var c = world.Cargo;
        var d = world.Defenses;
        AddAt(0, 5,
            $"{s.Fighters,5}{s.HunterKillers,5}{s.Jumpships,5}{s.Jumptransports,5}{s.Penetrators,5}{s.Starships,5}{s.Transports,5}" +
            $"  {c.Legions,5}{c.NinjaLegions,5}" +
            $"  {d.Lams,5}{d.DefenseSatellites,5}{d.Gdms,5}{d.IonCannons,5}");

        if (world is Planet { Redirection.Destination: { } redirectDestination }) {
            var origin = viewer.Capital?.Location ?? new Coordinate(0, 0);
            AddAt(1, 6, $"Redirecting new production -> ({RelativeCoordinate.Format(redirectDestination, origin)})");
        }

        if (Game.FindWorldBackgroundText(game, world, viewer, conquer: false) is { } background) {
            for (var i = 0; i < background.Count; i++) {
                AddAt(1, 7 + i, background[i]);
            }
        }

        Add(new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "D: Deploy from here" });
    }

    private void AddAt(int x, int y, string text) => Add(new Label { X = x, Y = y, Text = text });
}
