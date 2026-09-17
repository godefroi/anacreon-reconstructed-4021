using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;
using Reconstructed4021.Tui.Screens;


namespace Reconstructed4021.Tui.Overlays;


// MAPWIND.PAS's GetMapObject/DISPLAY.PAS's DisplayMenu: 2+ objects at one cursor sector need a pick
// first (ExamineCursor's own 2+ branch) before Close Up can open on any one of them -- the first real
// second overlay-stack consumer (GalaxyMapScreen pushes this on top of itself, then this pushes a
// CloseUpOverlay on top of itself in turn once something's chosen). Also the picker every
// PickOwnFleetAtCursor call site uses (Resupply, Refuel, SRM Sweep, Transfer, Abort/Join) when 2+ of
// the player's own fleets share a sector -- FleetStatusTag below exists for exactly that case: telling
// an idle fleet apart from one already busy at a glance, ported from Reconstructed4021.Tui's own
// GameShell.ObjectListItem (same status tag, same reasoning).
internal sealed class ObjectPickerOverlay : IOverlay
{
    private const int Width = 45;
    private readonly ListBox<ISectorObject> _list;
    private readonly Empire _viewer;
    private readonly Action<ISectorObject> _onChosen;
    private readonly Func<char, Fleet, Action<Fleet>?>? _resolveFleetAction;

    public bool IsDismissed { get; private set; }

    // resolveFleetAction is only passed by ExamineCursor's own sector picker -- Enter already means
    // "this is the target" at every other call site (PickGround, PickOwnFleetAtCursor's own fleet-only
    // picker), so C/T/J/R must not double as fleet-command shortcuts there, matching tui1's own
    // ShowObjectPicker(allowFleetActions:) gating.
    public ObjectPickerOverlay(IReadOnlyList<ISectorObject> objects, Empire viewer, Action<ISectorObject> onChosen, Func<char, Fleet, Action<Fleet>?>? resolveFleetAction = null)
    {
        _viewer = viewer;
        _onChosen = onChosen;
        _resolveFleetAction = resolveFleetAction;
        _list = new ListBox<ISectorObject>(objects, Format);
    }

    private string Format(ISectorObject obj)
    {
        var name = CloseUpOverlay.DisplayName(obj, _viewer);
        var status = obj is Fleet fleet ? FleetStatusTag(fleet) : "";
        return $"{name}  ({obj.Owner.Name}){status}";
    }

    /// <summary>
    /// Same redaction rule as <see cref="CloseUpWindowText.DescribeFleetStatus"/>: Ready/InTransit/
    /// Inactive is real Pascal status info (CLSCOMM.PAS's own FltStatusName), visible for any fleet
    /// the viewer owns or has scouted. Whether a fleet has orders queued at all is not -- Pascal never
    /// exposes another empire's order queue, so "orders pending" only ever shows for the viewer's own
    /// fleets, not a scouted enemy's. "Orders pending" matters as much as "in transit" here: a Ready
    /// fleet with a full queue looks idle but isn't, it'll act the moment
    /// <see cref="Core.Turns.FleetMovementHandler.ResolveOrders"/> next runs -- exactly the distinction
    /// that matters when scanning several of the player's own fleets at one sector for one that's
    /// actually free to hand a new Resupply mission to.
    /// </summary>
    private string FleetStatusTag(Fleet fleet)
    {
        var owned = ReferenceEquals(fleet.Owner, _viewer);
        if (!owned && !Game.Scouted(_viewer, fleet))
        {
            return "";
        }

        return fleet.Status switch
        {
            FleetStatus.InTransit => " [in transit]",
            FleetStatus.Inactive => " [out of fuel]",
            _ when owned && fleet.NextOrder > 0 => " [orders pending]",
            _ => "",
        };
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (_list.HandleKey(key))
        {
            return;
        }

        if (_resolveFleetAction is not null && !key.Modifiers.HasFlag(ConsoleModifiers.Control) &&
            _list.SelectedItem is Fleet ownFleet && ReferenceEquals(ownFleet.Owner, _viewer) &&
            _resolveFleetAction(char.ToUpperInvariant(key.KeyChar), ownFleet) is { } action)
        {
            IsDismissed = true;
            action(ownFleet);
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                IsDismissed = true;
                if (_list.SelectedItem is { } chosen)
                {
                    _onChosen(chosen);
                }

                break;
            case ConsoleKey.Escape:
                IsDismissed = true;
                break;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        // reservesHintRow depends only on _resolveFleetAction (fixed for this overlay's whole
        // lifetime), never on which item is currently highlighted -- the box's own size has to stay
        // constant regardless of selection, or it visibly grows/shrinks by a row every time the
        // highlight moves on/off an owned fleet. The hint text itself still varies per-selection; only
        // the row it occupies is unconditionally reserved.
        var reservesHintRow = _resolveFleetAction is not null;
        var hint = reservesHintRow && _list.SelectedItem is Fleet ownFleet && ReferenceEquals(ownFleet.Owner, _viewer)
            ? GalaxyMapScreen.FleetActionHint(ownFleet, _resolveFleetAction!)
            : "";

        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(_list.Items.Count + 2 + (reservesHintRow ? 1 : 0), Math.Max(3, fb.Height - 2));
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var listHeight = height - 2 - (reservesHintRow ? 1 : 0);
        _list.Draw(fb, x + 1, y + 1, width - 2, listHeight, ConsoleColor.Gray, ConsoleColor.Black, ConsoleColor.Black, ConsoleColor.Gray);

        if (reservesHintRow)
        {
            fb.DrawText(x + 1, y + 1 + listHeight, hint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }
    }
}
