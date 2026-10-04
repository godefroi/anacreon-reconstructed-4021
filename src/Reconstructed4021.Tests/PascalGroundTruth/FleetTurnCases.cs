namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs for FleetTurnTests.MatchesGoldenFile: one Empire1 fleet run for <see cref="FleetTurnCase.Turns"/>
/// rounds of the real per-turn fleet update (FLEET.PAS:646-905, UpdateFleet via UpdateAllFleets), with
/// its orders compiled from <see cref="FleetTurnCase.Orders"/> by the real CompileOrders (ORDERS.PAS:234-266)
/// on the Pascal side and FleetOrderCompiler on the C# side.
///
/// A round is UpdateAllFleets(Empire2,Empire1) then UpdateAllFleets(Empire1,Empire2), and on the C# side
/// AdvanceFleets with the same two empire pairs, so each fleet type moves on the call it would in a real
/// two-empire game. The C# side runs FleetMovementHandler with useLegacyOrderResolution: orders execute
/// on arrival inside the fleet update, the way Pascal's UpdateFleet runs ExecuteFleetOrders. The default
/// two-phase ResolveOrders (TurnEngine.BeginTurn/EndTurn) is a documented port addition with no Pascal
/// counterpart and isn't covered here.
///
/// The fleet starts Ready at Pos with no destination; a moving case starts its orders with a DEST, which
/// executes on the first update (so movement begins in round 2). DEST coordinates are relative to
/// Empire1's capital at (10,10), with Y flipped; <see cref="FleetTurnCases.Rel"/> converts. Mines, the
/// disrupter and the public gate belong to Empire2, the fortress (at Pos) to Empire1, and the transfer
/// world to <see cref="FleetTurnCase.WorldOwner"/>. Each sector holds one object, so cases keep the capital,
/// gate, disrupter, fortress and world on separate cells, all within 1..19.
///
/// <see cref="FleetTurnCase.Fleet"/>/<see cref="FleetTurnCase.World"/> list ships and cargo as
/// space-separated "key=amount" pairs, keys fgt hkr jmp jtn pen ssp trn men nnj amb che met sup tri.
/// </summary>
public sealed record FleetTurnCase(
    string Name, int PosX, int PosY, string Fleet, int Turns, string[] Orders,
    int Fuel = 1000, int RngFixedValue = 0,
    int NebulaX = 0, int NebulaY = 0,
    int MineX = 0, int MineY = 0,
    int DisrupterX = 0, int DisrupterY = 0,
    int GateX = 0, int GateY = 0,
    bool FortressAtPos = false,
    int WorldX = 0, int WorldY = 0, int WorldOwner = 1, string World = "",
    int InactiveDestX = 0, int InactiveDestY = 0,
    bool InTransitWherePascalSaysInactive = false) : INamedCase
{
    /// <summary>The fleet starts Inactive and headed to (InactiveDestX,InactiveDestY), as after running dry en route.</summary>
    public bool StartsInactive => InactiveDestX != 0 || InactiveDestY != 0;

    public bool HasMine => MineX != 0 || MineY != 0;
    public bool HasWorld => WorldX != 0 || WorldY != 0;
}

internal static class FleetTurnCases
{
    /// <summary>The ship then cargo resource keys, in Pascal's ResourceTypes order (fgt..trn, men..tri).</summary>
    public static readonly string[] ResourceKeys = ["fgt", "hkr", "jmp", "jtn", "pen", "ssp", "trn", "men", "nnj", "amb", "che", "met", "sup", "tri"];

    /// <summary>An absolute coordinate as Empire1 types it in an order: relative to its capital at (10,10), Y flipped.</summary>
    public static string Rel(int x, int y) => $"{x - 10},{10 - y}";

    /// <summary>Parses a "key=amount ..." spec into 14 amounts in <see cref="ResourceKeys"/> order.</summary>
    public static int[] Amounts(string spec)
    {
        var amounts = new int[ResourceKeys.Length];
        foreach (var pair in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            var eq = pair.IndexOf('=');
            amounts[Array.IndexOf(ResourceKeys, pair[..eq])] = int.Parse(pair[(eq + 1)..]);
        }
        return amounts;
    }

    /// <summary>The runworld.pas fleetturn tuple: the integer fields, '|', then the order lines joined with '/'.</summary>
    public static string Format(FleetTurnCase c) =>
        string.Join(",", [
            c.PosX, c.PosY, .. Amounts(c.Fleet), c.Fuel, c.Turns, c.RngFixedValue,
            c.NebulaX, c.NebulaY, c.MineX, c.MineY, c.DisrupterX, c.DisrupterY, c.GateX, c.GateY,
            c.FortressAtPos ? 1 : 0, c.WorldX, c.WorldY, c.WorldOwner - 1, .. Amounts(c.World),
            c.InactiveDestX, c.InactiveDestY,
        ]) + "|" + string.Join("/", c.Orders);

    public static readonly IReadOnlyList<FleetTurnCase> All = [
        // Movement and fuel.

        // A Standard fleet moves one cell per round. Two lone fighters burn a fraction of a fuel unit
        // a year; Pascal's SetFleetFuel truncates the stored fuel on every write.
        new(Name: "StandardFleetStepsAndBurnsFuel", PosX: 3, PosY: 3, Fleet: "fgt=2", Turns: 4, Fuel: 100,
            Orders: [$"DEST {Rel(8, 3)}"]),

        // A jump fleet covers 10 cells a round and arrives (Ready) partway through its second move.
        new(Name: "JumpFleetCrossesTheMapAndArrives", PosX: 2, PosY: 4, Fleet: "jmp=10", Turns: 3,
            Orders: [$"DEST {Rel(17, 16)}"]),

        // Fuel too low for the move, trillum in cargo: UseUpFuel converts trillum and retries.
        new(Name: "OutOfFuelRefuelsFromCargo", PosX: 2, PosY: 4, Fleet: "jmp=10 jtn=5 tri=30", Turns: 3, Fuel: 0,
            Orders: [$"DEST {Rel(17, 4)}"]),

        // A year's burn needs more than the cargo's one ton yields: the retry converts it, comes up short
        // again with no trillum left, and the fleet goes Inactive with the converted fuel still aboard.
        new(Name: "CargoTrillumRunsOutMidRefuel", PosX: 2, PosY: 4, Fleet: "trn=2000 tri=1", Turns: 3, Fuel: 0,
            Orders: [$"DEST {Rel(10, 4)}"]),

        // Exactly one ton of cargo trillum covers the move.
        new(Name: "LastTonOfCargoTrillumRefuels", PosX: 2, PosY: 4, Fleet: "trn=40 tri=1", Turns: 3, Fuel: 0,
            Orders: [$"DEST {Rel(10, 4)}"]),

        // No fuel and no trillum: the fleet goes Inactive and stays where it is.
        new(Name: "OutOfFuelWithoutTrillumGoesInactive", PosX: 2, PosY: 4, Fleet: "trn=40", Turns: 4, Fuel: 0,
            Orders: [$"DEST {Rel(10, 4)}"]),

        // A fleet that went Inactive en route and has since gained fuel (an Abort/Join, say) is still
        // updated, and moves again. Pascal leaves it marked Inactive while it travels; the port shows
        // InTransit (a deliberate divergence, development/PORT_DESIGN.md "Fleet status").
        new(Name: "InactiveFleetWithFuelMovesAgain", PosX: 2, PosY: 4, Fleet: "fgt=5", Turns: 2, Fuel: 100,
            InactiveDestX: 10, InactiveDestY: 4, InTransitWherePascalSaysInactive: true, Orders: []),

        // Same, but with cargo trillum instead: UseUpFuel refuels from it and the fleet moves.
        new(Name: "InactiveFleetWithCargoTrillumMovesAgain", PosX: 2, PosY: 4, Fleet: "trn=40 tri=5", Turns: 2, Fuel: 0,
            InactiveDestX: 10, InactiveDestY: 4, Orders: []),

        // Dense nebula on the next cell: FltBlocked, and the fleet stays on the last clear cell.
        new(Name: "StandardFleetBlockedByDenseNebula", PosX: 3, PosY: 3, Fleet: "fgt=5", Turns: 4,
            NebulaX: 5, NebulaY: 3, Orders: [$"DEST {Rel(8, 3)}"]),
        new(Name: "JumpFleetBlockedByDenseNebula", PosX: 3, PosY: 3, Fleet: "jmp=5", Turns: 2,
            NebulaX: 7, NebulaY: 7, Orders: [$"DEST {Rel(12, 12)}"]),

        // Mines and disrupters. Both stop only JumpFleet/HKFleet movement.

        // Rnd(1,100)=1: 1+Round(10*0.4)=5 jumpships lost, five survive and land on the mined cell.
        new(Name: "JumpFleetPartialMineLoss", PosX: 3, PosY: 3, Fleet: "jmp=10", Turns: 2,
            MineX: 6, MineY: 3, Orders: [$"DEST {Rel(15, 3)}"]),

        // Rnd(1,100)=51: more than the fleet has, so it's destroyed.
        new(Name: "JumpFleetDestroyedByMines", PosX: 3, PosY: 3, Fleet: "jmp=10", Turns: 2, RngFixedValue: 50,
            MineX: 6, MineY: 3, Orders: [$"DEST {Rel(15, 3)}"]),

        // 15 jumptransports: 1+Round(15*0.7)=1+Round(10.5), a banker's-rounding tie. The survivors hold
        // less, so BalanceFleet trims the trillum.
        new(Name: "JumpTransportsMineLossRebalancesCargo", PosX: 3, PosY: 3, Fleet: "jmp=5 jtn=15 tri=300", Turns: 2,
            MineX: 5, MineY: 5, Orders: [$"DEST {Rel(15, 15)}"]),

        // An HK fleet takes mine damage the same way.
        new(Name: "HkFleetPartialMineLoss", PosX: 3, PosY: 3, Fleet: "hkr=100", Turns: 2,
            MineX: 4, MineY: 4, Orders: [$"DEST {Rel(15, 15)}"]),
        new(Name: "HkFleetDestroyedByMines", PosX: 3, PosY: 3, Fleet: "hkr=10", Turns: 2, RngFixedValue: 50,
            MineX: 4, MineY: 4, Orders: [$"DEST {Rel(15, 15)}"]),

        // A Standard fleet walks through the same minefield untouched.
        new(Name: "StandardFleetIgnoresMines", PosX: 3, PosY: 3, Fleet: "fgt=10", Turns: 3,
            MineX: 4, MineY: 3, Orders: [$"DEST {Rel(8, 3)}"]),

        // The disrupter at (9,6) is within 3 of (6,3): the fleet stops on that cell.
        new(Name: "JumpFleetStoppedByDisrupter", PosX: 3, PosY: 3, Fleet: "jmp=10", Turns: 2,
            DisrupterX: 9, DisrupterY: 6, Orders: [$"DEST {Rel(16, 3)}"]),

        // The disrupter at (9,8) never comes within 3 of row 3.
        new(Name: "JumpFleetOutOfDisrupterRange", PosX: 3, PosY: 3, Fleet: "jmp=10", Turns: 2,
            DisrupterX: 9, DisrupterY: 8, Orders: [$"DEST {Rel(16, 3)}"]),

        // Fortresses and gates.

        // Destination more than 5 away: the fortress hop walks toward it, then the normal step runs.
        new(Name: "FortressHopTowardFarDestination", PosX: 3, PosY: 12, Fleet: "fgt=5", Turns: 2,
            FortressAtPos: true, Orders: [$"DEST {Rel(17, 12)}"]),

        // Dense nebula on the hop's third cell stops the hop early, and the normal step is then blocked.
        new(Name: "FortressHopStoppedByDenseNebula", PosX: 3, PosY: 12, Fleet: "fgt=5", Turns: 2,
            FortressAtPos: true, NebulaX: 6, NebulaY: 12, Orders: [$"DEST {Rel(17, 12)}"]),

        // Destination within 5: the fleet teleports straight there.
        new(Name: "FortressTeleportsToNearDestination", PosX: 3, PosY: 12, Fleet: "fgt=5", Turns: 2,
            FortressAtPos: true, Orders: [$"DEST {Rel(8, 15)}"]),

        // A public gate under a Standard fleet teleports it to any destination.
        new(Name: "StandardFleetThroughPublicGate", PosX: 3, PosY: 15, Fleet: "fgt=5", Turns: 2,
            GateX: 3, GateY: 15, Orders: [$"DEST {Rel(17, 2)}"]),

        // A jump fleet on a gate moves on the other UpdateAllFleets call, then arrives and runs its
        // next order on its own call the same round.
        new(Name: "JumpFleetThroughPublicGate", PosX: 3, PosY: 15, Fleet: "jmp=5", Turns: 2,
            GateX: 3, GateY: 15, Orders: [$"DEST {Rel(17, 2)}", $"DEST {Rel(15, 2)}"]),

        // A gate can't deliver into dense nebula: NebGate news, and the fleet stays on the gate.
        new(Name: "GateIntoDenseNebulaRefused", PosX: 3, PosY: 15, Fleet: "fgt=5", Turns: 2,
            GateX: 3, GateY: 15, NebulaX: 17, NebulaY: 2, Orders: [$"DEST {Rel(17, 2)}"]),

        // A Standard fleet walks onto a gate, then its next DEST sends it through.
        new(Name: "StandardFleetArrivesAtGateThenGates", PosX: 3, PosY: 15, Fleet: "fgt=5", Turns: 4,
            GateX: 5, GateY: 15, Orders: [$"DEST {Rel(5, 15)}", $"DEST {Rel(17, 2)}"]),

        // Orders: WAIT and REPEAT.

        // Pick up, wait a round, drop off, repeat. IgnoreRepeat lets REPEAT jump back once per update.
        new(Name: "TransferWaitRepeatLoop", PosX: 5, PosY: 5, Fleet: "trn=10", Turns: 3,
            WorldX: 5, WorldY: 5, World: "met=500",
            Orders: ["TRAN 20 MET", "WAIT", "TRAN -20 MET", "REPEAT"]),

        // A lone REPEAT jumps to itself once, then runs off the end and clears the orders.
        new(Name: "RepeatToSelf", PosX: 5, PosY: 5, Fleet: "fgt=1", Turns: 1, Orders: ["REPEAT"]),

        // A jump-transport shuttle between a world and the capital, repeating.
        new(Name: "ShuttleBetweenWorldAndCapitalRepeats", PosX: 3, PosY: 3, Fleet: "jmp=5 jtn=10", Turns: 6,
            WorldX: 3, WorldY: 3, World: "met=1000",
            Orders: ["TRAN 100 MET", $"DEST {Rel(10, 10)}", "TRAN -100 MET", $"DEST {Rel(3, 3)}", "REPEAT"]),

        // Transfer clamps (ExecuteTransCOM's LesserInt chain), all at the fleet's own world. A transport
        // holds 3 metal, so each case sizes the fleet to keep the other clamps out of the way and its
        // starting cargo within the hold (BalanceFleet would otherwise trim it).

        // Picking up more than the world has.
        new(Name: "PickupClampedToWorldStock", PosX: 5, PosY: 5, Fleet: "trn=10", Turns: 1,
            WorldX: 5, WorldY: 5, World: "met=20", Orders: ["TRAN 500 MET"]),

        // Picking up more than the hold fits.
        new(Name: "PickupClampedToCargoSpace", PosX: 5, PosY: 5, Fleet: "trn=1", Turns: 1,
            WorldX: 5, WorldY: 5, World: "met=9000", Orders: ["TRAN 5000 MET"]),

        // Picking up past the fleet's own 9999 cap.
        new(Name: "PickupClampedToMaxResources", PosX: 5, PosY: 5, Fleet: "trn=9999 met=9990", Turns: 1,
            WorldX: 5, WorldY: 5, World: "met=500", Orders: ["TRAN 100 MET"]),

        // Dropping off more than the fleet has.
        new(Name: "DropoffClampedToFleetStock", PosX: 5, PosY: 5, Fleet: "trn=20 met=40", Turns: 1,
            WorldX: 5, WorldY: 5, Orders: ["TRAN -500 MET"]),

        // Dropping off past the world's 9999 cap.
        new(Name: "DropoffClampedToWorldMaxResources", PosX: 5, PosY: 5, Fleet: "trn=40 met=100", Turns: 1,
            WorldX: 5, WorldY: 5, World: "met=9990", Orders: ["TRAN -100 MET"]),

        // Picking up ships changes the fleet's fuel capacity; ChangeCompositionOfFleet tops the tank up
        // from the world's trillum.
        new(Name: "ShipPickupTopsUpFuelFromWorld", PosX: 5, PosY: 5, Fleet: "trn=10", Turns: 1, Fuel: 10,
            WorldX: 5, WorldY: 5, World: "trn=20 tri=500", Orders: ["TRAN 15 TRN"]),

        // Dropping every ship aborts the fleet into the world, fuel returning as trillum.
        new(Name: "DropAllShipsAbortsFleet", PosX: 5, PosY: 5, Fleet: "trn=10 met=25", Turns: 1, Fuel: 500,
            WorldX: 5, WorldY: 5, World: "tri=5", Orders: ["TRAN -10 TRN"]),

        // A world owned by another empire is skipped.
        new(Name: "TransferAtForeignWorldIgnored", PosX: 5, PosY: 5, Fleet: "trn=10", Turns: 1,
            WorldX: 5, WorldY: 5, WorldOwner: 2, World: "met=500", Orders: ["TRAN 50 MET"]),
    ];

    /// <summary>MethodDataSource shape for FleetTurnTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<FleetTurnCase>> AsDataSource() => All.Select(c => (Func<FleetTurnCase>)(() => c));
}
