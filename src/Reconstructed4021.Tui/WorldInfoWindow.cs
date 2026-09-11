using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tui;

/// <summary>
/// One of the player's own worlds, shown as a tabbed window: Close Up, Production, ISSP, Designate
/// -- four separate real Pascal commands (CLSCOMM.PAS: CloseUpCom/ProductionCom, DESIGN.PAS:
/// DesignateCommand/ChangeISSPCom) that had no way to share one screen in 1988's Turbo Vision, but
/// have no real reason not to now. The tab mechanism itself (<see cref="TabbedWindow"/>) is shared
/// with <see cref="CloseUpWindow"/> -- see that class's own doc comment for why it's not built on
/// Terminal.Gui's own Tabs control.
///
/// Only reachable for a world the player owns (<see cref="GameShell.ShowCloseUp"/>'s own routing) --
/// a fleet or another empire's world still goes through the standalone <see cref="CloseUpWindow"/>,
/// which needs its own general-purpose Known/Scouted redaction this window doesn't.
/// </summary>
internal sealed class WorldInfoWindow : Window
{
    private readonly TabbedWindow tabs;

    public WorldInfoWindow(IEconomicWorld world, string worldName, Game game, Empire viewer, string initialTab, Action<WorldType> onDesignateSelected, Action onPickRedirectDestination)
    {
        Width = 88;
        Height = 24;
        X = Pos.Center();
        Y = Pos.Center();
        CanFocus = true;

        tabs = new TabbedWindow(this, worldName);

        var closeUpTab = new WorldCloseUpTabView(world, game, viewer);
        var designateTab = new DesignateTabView(world, onDesignateSelected);
        var productionTab = new ProductionWindow(world);

        tabs.AddTab("Close Up", closeUpTab);
        tabs.AddTab("Production", productionTab, onActivated: productionTab.Refresh); // stale otherwise -- ISSP dial changes on the sibling tab never re-run the preview

        // ISSP has nothing to edit on a starbase (real Pascal's own GetISSP/SetISSP hardcode its
        // dial at 0 -- see IsspEditor's own doc comment) -- omitted there rather than shown inert.
        if (world is Planet planet) {
            tabs.AddTab("ISSP", new IsspEditor(planet.SelfSufficiency));
        }

        tabs.AddTab("Designate", designateTab);

        if (world is Planet redirectPlanet) {
            var origin = viewer.Capital?.Location ?? new Coordinate(0, 0);
            var redirectTab = new RedirectTabView(redirectPlanet.Redirection, origin);
            redirectTab.PickDestinationRequested += (_, _) => onPickRedirectDestination();
            tabs.AddTab("Redirect", redirectTab);
        }

        tabs.Show(initialTab == "CloseUp" ? "Close Up" : initialTab);

        Initialized += (_, _) => tabs.FocusCurrentTab();
    }

    /// <summary>Updates the title after a rename (F2, GameShell's own RenameObject) -- <paramref name="worldName"/> was only ever a snapshot taken at construction.</summary>
    public void RefreshTitle(string newName) => tabs.Rename(newName);
}
