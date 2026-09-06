using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// `ORDERS.PAS`'s own compile/decompile pair (`ParseLine`/`CompileOrders`/`DeCompileOrders`,
/// `:129-312`) plus the destination half of `GetLocation`/`GetDestination`
/// (`PRIMINTR.PAS:1530-1573`) adapted to this port's data -- the real mini scripting language a player
/// types into <see cref="Tui.FleetOrdersWindow"/>, kept here so it's testable without any window.
///
/// v1.31 is the baseline (confirmed identical to v2 for `FLTCOMM.PAS`; v2's only `ORDERS.PAS` changes
/// are a `SRMS` order token behind the v2 SRM-sweep feature and a still-commented-out `ABOR` stub --
/// both out of scope here, `docs/PASCAL_V1_VS_V2_DIFF.md`).
///
/// Destination resolution deliberately omits `Name2Fleet`'s raw `FLEET&lt;n&gt;`/`ENEMY&lt;n&gt;`
/// slot-index convention -- already a documented, deliberate omission elsewhere in this port
/// (`Tui.CloseUpWindow.DescribeLocation`'s own comment: "no slot index in this port to mirror
/// exactly"); skipped here for the same reason.
/// </summary>
public static class FleetOrderCompiler
{
    public sealed record CompileResult(IReadOnlyList<FleetOrder> Orders, int ErrorLine = 0, string? ErrorMessage = null);

    /// <summary>
    /// CompileOrders (`ORDERS.PAS:234-266`). Blank lines produce no order (`IF Comm.Typ&lt;&gt;NoCOM
    /// THEN AddOrders`, silently skipped, not an error) -- everything else is one of the four real
    /// commands or a compile error naming the 1-based line it failed on.
    /// </summary>
    public static CompileResult Compile(Game game, Empire owner, IReadOnlyList<string> lines)
    {
        var orders = new List<FleetOrder>();

        for (var i = 0; i < lines.Count; i++) {
            var (order, error) = ParseLine(game, owner, lines[i]);
            if (error is not null) {
                return new CompileResult([], i + 1, error);
            }

            if (order is not null) {
                orders.Add(order);
            }
        }

        return new CompileResult(orders);
    }

    /// <summary>
    /// ParseLine (`ORDERS.PAS:191-232`). The command token is matched case-insensitively on its first
    /// 4 characters (`Parm[1]:=Copy(Parm[1],1,4)`), matching `AllUpCase(Line)` running on the whole
    /// line before splitting. Simplified from `SplitLine`'s own 4-parameter cap (real Pascal collapses
    /// all whitespace past the 4th token into one combined trailing parameter) -- no real command here
    /// ever reads a 4th token, so a plain space-split is equivalent for every actual use of this
    /// language; only multi-word destination names would ever notice the difference, and real Pascal
    /// itself can't accept those either (`GetDestination` only ever reads a single token).
    /// </summary>
    private static (FleetOrder? Order, string? Error) ParseLine(Game game, Empire owner, string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) {
            return (null, null);
        }

        var token = (parts[0].Length > 4 ? parts[0][..4] : parts[0]).ToUpperInvariant();

        switch (token) {
            case "DEST": {
                var destinationText = parts.Length > 1 ? parts[1] : "";
                if (!ResolveDestination(game, owner, destinationText, out var destinationObject, out var destinationPosition)) {
                    return (null, "Unknown destination in line");
                }
                return (new FleetOrder(CommandType.Destination, destinationObject, destinationPosition), null);
            }
            case "TRAN": {
                // GetResourceType runs before GetTransfer and neither is gated on the other having
                // already failed (ORDERS.PAS:206-208) -- GetTransfer's own failure unconditionally
                // overwrites Error, so a line bad in both ways reports "Bad transfer value", not
                // "Unknown resource". Ported in that same order for the same result.
                var resourceText = parts.Length > 2 ? parts[2] : "";
                var resourceResolved = ResolveResource(resourceText, out var ship, out var cargo);

                var amountText = parts.Length > 1 ? parts[1] : "";
                if (!int.TryParse(amountText, out var amount)) {
                    return (null, "Bad transfer value in line");
                }

                if (!resourceResolved) {
                    return (null, "Unknown resource in line");
                }

                return (new FleetOrder(CommandType.Transfer, TransferShip: ship, TransferCargo: cargo, TransferAmount: amount), null);
            }
            case "REPE":
                return (new FleetOrder(CommandType.Repeat), null);
            case "WAIT":
                return (new FleetOrder(CommandType.Wait), null);
            default:
                return (null, "Unknown command in line");
        }
    }

    /// <summary>
    /// GetResourceType (`ORDERS.PAS:160-172`): the 3-letter code, case-insensitive, against the same
    /// table <see cref="ResourceAbbreviation"/> already provides for display -- ships checked before
    /// cargo, matching the real combined `ResourceTypes` enum's own declaration order (`fgt..trn` before
    /// `men..tri`), though no code collides between the two so the order can't actually change the
    /// result.
    /// </summary>
    private static bool ResolveResource(string text, out ShipType? ship, out CargoType? cargo)
    {
        foreach (var t in Enum.GetValues<ShipType>()) {
            if (string.Equals(ResourceAbbreviation.Of(t), text, StringComparison.OrdinalIgnoreCase)) {
                ship = t;
                cargo = null;
                return true;
            }
        }

        foreach (var t in Enum.GetValues<CargoType>()) {
            if (string.Equals(ResourceAbbreviation.Of(t), text, StringComparison.OrdinalIgnoreCase)) {
                ship = null;
                cargo = t;
                return true;
            }
        }

        ship = null;
        cargo = null;
        return false;
    }

    /// <summary>
    /// GetDestination/GetLocation (`PRIMINTR.PAS:1530-1573`, minus `Name2Fleet` -- see this class's own
    /// doc comment). Two real cases, tried in order: a player-given name (`Name2Index`, the same
    /// per-empire <see cref="ISectorObject.Names"/> dictionary this port already populates from
    /// `.SAV` load and starbase/stargate construction naming), then a bounded `"x,y"` coordinate
    /// (`Name2Coord`) -- landing on whatever object already occupies that cell, if any, otherwise a
    /// bare position. No galaxy-wrap: confirmed this port's <see cref="Galaxy.Galaxy"/> has no
    /// torus/wraparound anywhere, so a plain bounds check is the whole job (real Pascal's own
    /// `AbsoluteX`/`AbsoluteY` wrap has no equivalent need here).
    /// </summary>
    private static bool ResolveDestination(Game game, Empire owner, string text, out ISectorObject? destinationObject, out Coordinate? destinationPosition)
    {
        foreach (ISectorObject candidate in NamedObjects(game.Galaxy)) {
            if (candidate.Names.TryGetValue(owner, out var name) && string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) {
                destinationObject = candidate;
                destinationPosition = null;
                return true;
            }
        }

        var comma = text.IndexOf(',');
        if (comma > 0
            && int.TryParse(text[..comma], out var x)
            && int.TryParse(text[(comma + 1)..], out var y)
            && x >= 1 && x <= game.Galaxy.Size && y >= 1 && y <= game.Galaxy.Size) {
            var coordinate = new Coordinate(x, y);
            destinationObject = game.Galaxy.GetObjectAt(coordinate);
            destinationPosition = destinationObject is null ? coordinate : null;
            return true;
        }

        destinationObject = null;
        destinationPosition = null;
        return false;
    }

    private static IEnumerable<ISectorObject> NamedObjects(Galaxy.Galaxy galaxy)
    {
        foreach (var p in galaxy.Planets) yield return p;
        foreach (var s in galaxy.Starbases) yield return s;
        foreach (var g in galaxy.Stargates) yield return g;
        foreach (var c in galaxy.ConstructionSites) yield return c;
        foreach (var f in galaxy.Fleets) yield return f;
    }

    /// <summary>
    /// DeCompileOrders (`ORDERS.PAS:268-312`) -- exact command-word text (`"DESTination "`,
    /// `"TRANsfer "`, `"REPEat"`, `"WAIT"`) so the result round-trips straight back through
    /// <see cref="Compile"/> unchanged. A destination's name uses <paramref name="viewer"/>'s own
    /// <see cref="ISectorObject.Names"/> entry when the object has one; otherwise (or for a bare
    /// position) falls back to its plain `"X,Y"` coordinate -- <b>not</b>
    /// <see cref="Tui.CloseUpWindow.DescribeLocation"/>'s relative-to-capital display format, which
    /// <see cref="ResolveDestination"/> can't parse back.
    /// </summary>
    public static IReadOnlyList<string> Decompile(Empire viewer, IReadOnlyList<FleetOrder> orders)
    {
        var lines = new List<string>(orders.Count);

        foreach (var order in orders) {
            lines.Add(order.Type switch {
                CommandType.Destination => $"DESTination {DestinationText(viewer, order)}",
                CommandType.Transfer => $"TRANsfer {order.TransferAmount} {TransferResourceText(order)}",
                CommandType.Repeat => "REPEat",
                CommandType.Wait => "WAIT",
                _ => throw new ArgumentOutOfRangeException(nameof(orders), order.Type, "Unexpected compiled command type."),
            });
        }

        return lines;
    }

    private static string DestinationText(Empire viewer, FleetOrder order)
    {
        if (order.DestinationObject is { } destinationObject) {
            return destinationObject.Names.TryGetValue(viewer, out var name)
                ? name
                : $"{destinationObject.Location.X},{destinationObject.Location.Y}";
        }

        var position = order.DestinationPosition!.Value;
        return $"{position.X},{position.Y}";
    }

    private static string TransferResourceText(FleetOrder order) => order switch {
        { TransferShip: { } ship } => ResourceAbbreviation.Of(ship),
        { TransferCargo: { } cargo } => ResourceAbbreviation.Of(cargo),
        _ => throw new ArgumentException("A compiled Transfer order always sets exactly one of TransferShip/TransferCargo.", nameof(order)),
    };
}
