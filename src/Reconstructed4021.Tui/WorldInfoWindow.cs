using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using System.Collections.Generic;
using System.Linq;

namespace Reconstructed4021.Tui;

/// <summary>
/// One of the player's own worlds, shown as a tabbed window: Close Up, Designate, ISSP, Production
/// -- four separate real Pascal commands (CLSCOMM.PAS: CloseUpCom/ProductionCom, DESIGN.PAS:
/// DesignateCommand/ChangeISSPCom) that had no way to share one screen in 1988's Turbo Vision, but
/// have no real reason not to now.
///
/// Not built on Terminal.Gui's own Tabs control: its tab-strip chrome adds five lines around the
/// content (border, tab line, a line above and below the tabs), its own arrow-key navigation
/// intercepts Left/Right before a tab's own content (ISSP's stepper) reliably gets real keyboard
/// focus -- confirmed live, not assumed -- and its header rendering has no public customization
/// surface (every drawing-related member on Tabs is private). Swapping a single content view in and
/// out of a plain bordered Window sidesteps all three: ordinary Window chrome is two lines total,
/// only one content view is ever a focusable child so SetFocus is unambiguous, and the tab names
/// live in the Title text instead of a framework-drawn strip.
///
/// Tab names show in the Title -- Window.Title has no per-run styling (TextFormatter's only styling
/// hook is the single-character HotKeySpecifier), so the active tab is marked with brackets rather
/// than a color -- and switch on Ctrl+PageUp/Ctrl+PageDown, a chord none of the four tabs' own
/// content ever reads.
///
/// Only reachable for a world the player owns (<see cref="GameShell.ShowCloseUp"/>'s own routing) --
/// a fleet or another empire's world still goes through the standalone <see cref="CloseUpWindow"/>,
/// which needs its own general-purpose Known/Scouted redaction this window doesn't.
/// </summary>
internal sealed class WorldInfoWindow : Window
{
    private readonly string worldName;
    private readonly List<(string Name, View View)> tabs;
    private int currentIndex;

    public WorldInfoWindow(IEconomicWorld world, string worldName, Game game, Empire viewer, Random random, string initialTab, Action<WorldType> onDesignateSelected)
    {
        Width = 88;
        Height = 24;
        X = Pos.Center();
        Y = Pos.Center();
        CanFocus = true;
        this.worldName = worldName;

        var closeUpTab = new WorldCloseUpTabView(world, game, viewer);
        var designateTab = new DesignateTabView(world, onDesignateSelected);
        var productionTab = new ProductionWindow(world, random);

        // ISSP has nothing to edit on a starbase (real Pascal's own GetISSP/SetISSP hardcode its
        // dial at 0 -- see IsspEditor's own doc comment) -- omitted there rather than shown inert.
        var isspTab = world is Planet planet ? new IsspEditor(planet.SelfSufficiency) : null;

        tabs = [("Close Up", closeUpTab), ("Designate", designateTab)];
        if (isspTab is not null) {
            tabs.Add(("ISSP", isspTab));
        }
        tabs.Add(("Production", productionTab));

        var initialView = initialTab switch {
            "Designate" => designateTab,
            "ISSP" => (View?)isspTab ?? closeUpTab,
            "Production" => productionTab,
            _ => closeUpTab,
        };
        currentIndex = Math.Max(0, tabs.FindIndex(t => ReferenceEquals(t.View, initialView)));
        Add(tabs[currentIndex].View);
        UpdateTitle();

        KeyDown += (_, key) => {
            if (!key.IsCtrl) {
                return;
            }
            switch (key.NoAlt.NoCtrl.NoShift.KeyCode) {
                case KeyCode.PageUp:
                    StepTab(-1);
                    key.Handled = true;
                    break;
                case KeyCode.PageDown:
                    StepTab(1);
                    key.Handled = true;
                    break;
            }
        };

        Initialized += (_, _) => tabs[currentIndex].View.SetFocus();
    }

    private void StepTab(int direction)
    {
        Remove(tabs[currentIndex].View);
        currentIndex = (currentIndex + direction + tabs.Count) % tabs.Count;
        Add(tabs[currentIndex].View);
        tabs[currentIndex].View.SetFocus();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        Title = $"{worldName}  " + string.Join("  ", tabs.Select((t, i) => i == currentIndex ? $"[{t.Name}]" : t.Name));
    }
}
