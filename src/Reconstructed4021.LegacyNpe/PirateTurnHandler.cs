using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// AI for the Pirate NPE (NPE01.PAS) — a simpler personality than Kingdom: no diplomacy, no region
/// bookkeeping, no world redesignation. Two patrol-and-raid loops share one 30-slot fleet-state table
/// and a 20x20 "hunting ground" grid (<see cref="_huntingGround"/>) that tracks where enemy transport
/// activity has recently been spotted, biasing where new patrol fleets get sent.
/// </summary>
public sealed class PirateTurnHandler : ITurnHandler
{
    public bool IsHuman => false;

    private readonly Random _random;
    private readonly Dictionary<Fleet, PirateFleetState> _fleetStates = new();

    /// <summary>
    /// HuntingGround (NPETYPES.PAS's <c>ARRAY[1..20,1..20] OF Byte</c>) — indexed here
    /// <c>[x-1,y-1]</c> for Pascal coordinates <c>x,y</c> in <c>1..20</c>. A real Turbo Pascal
    /// <c>Byte</c>: <see cref="UpdateFleets"/>'s <c>Inc</c>/<c>Dec</c> wrap at 0/255 rather than
    /// clamping, matching source (no range check guards them there) — see that method's own doc
    /// comment.
    /// </summary>
    private readonly byte[,] _huntingGround;

    /// <summary>
    /// Sheep (NPETYPES.PAS:131, <c>ARRAY[Empire] OF Index</c>, 9 bytes) — declared and
    /// block-read/block-written by <c>LoadPirateNPE</c>/<c>SavePirateNPE</c> but never read or
    /// written by field access anywhere in the source tree (grepped NPE01.PAS/NPETYPES.PAS/every
    /// NPE0x.PAS). <c>InitializePirateNPE</c> never zeroes it either (only <c>FleetData</c>/
    /// <c>HuntingGround</c> get a <c>FillChar</c>), so a real save's bytes here are whatever was
    /// already sitting in that Turbo Pascal heap block at allocation time — confirmed directly:
    /// GAUNTLET_1.SAV's own Pirate blob holds a nonzero, non-uniform 9-byte pattern here. Carried
    /// verbatim, not reinitialized to zero, purely so a `.SAV` round trip reproduces the same bytes;
    /// it has no gameplay meaning to derive or compute.
    /// </summary>
    private readonly byte[] _sheep;

    /// <summary>InitializePirateNPE (NPE01.PAS:465-479): zero fleet-state table, HuntingGround uniformly seeded at 25, empire defenses rolled fresh.</summary>
    public PirateTurnHandler(Empire empire, Random random)
    {
        _random = random;
        _huntingGround = new byte[20, 20];
        for (var x = 0; x < 20; x++) {
            for (var y = 0; y < 20; y++) {
                _huntingGround[x, y] = 25;
            }
        }
        _sheep = new byte[9];

        NpeToolkit.SetEmpireDefenses(empire, random);
    }

    /// <summary>`.SAV`/native-JSON import: reconstructs a handler from state a save file already recorded, matching <see cref="KingdomTurnHandler"/>'s own load-path constructor.</summary>
    internal PirateTurnHandler(Dictionary<Fleet, PirateFleetState> fleetStates, byte[,] huntingGround, byte[] sheep, Random random)
    {
        _random = random;
        _fleetStates = fleetStates;
        _huntingGround = huntingGround;
        _sheep = sheep;
    }

    /// <summary>Read-back seam for `.SAV`/native-JSON export.</summary>
    internal IReadOnlyDictionary<Fleet, PirateFleetState> FleetStates => _fleetStates;

    /// <summary>See <see cref="FleetStates"/>.</summary>
    internal byte[,] HuntingGround => _huntingGround;

    /// <summary>See <see cref="FleetStates"/>.</summary>
    internal byte[] Sheep => _sheep;

    /// <summary>ImplementPirateNPE (NPE01.PAS:452-463).</summary>
    public void PlayTurn(Empire empire, Game game)
    {
        EnforceNpeDataLinks(game);
        ReviewNews(empire, game);
        DeployRaiders(empire, game);
        DeployNewFleets(empire, game);
        UpdateFleets(empire, game);
    }

    /// <summary>EnforceNPEDataLinks (NPEINTR.PAS:1661-1689) — a private copy rather than a shared/generic toolkit method, since <see cref="KingdomTurnHandler"/>'s own dictionary is a different value type (<see cref="KingdomFleetState"/> vs <see cref="PirateFleetState"/>) and there's no third caller yet to justify genericizing.</summary>
    private void EnforceNpeDataLinks(Game game)
    {
        var alive = new HashSet<Fleet>(game.Galaxy.Fleets);
        foreach (var fleet in _fleetStates.Keys.Where(f => !alive.Contains(f)).ToList()) {
            _fleetStates.Remove(fleet);
        }
    }

    /// <summary>ReviewNews (NPE01.PAS:419-450) — FltBlocked redirects to a fresh patrol point, NoFuel destroys the fleet. No diplomacy/enemy-attack handling at all (unlike Kingdom's own ReviewNews).</summary>
    private void ReviewNews(Empire empire, Game game)
    {
        foreach (var item in empire.News) {
            if (item.Subject is not Fleet fleet) {
                continue;
            }

            if (item.Headline == NewsType.FleetBlocked) {
                if (!_fleetStates.TryGetValue(fleet, out var state)) {
                    continue;
                }
                var (bx, by, xy) = GetPatrolDestination(game);
                FleetLifecycle.SetFleetDestination(fleet, xy);
                state.BlockX = bx;
                state.BlockY = by;
            } else if (item.Headline == NewsType.FleetOutOfFuel) {
                CombatOutcome.DestroyFleet(fleet, game);
            }
        }
    }

    /// <summary>
    /// DeployRaiders (NPE01.PAS:234-352) — from every owned world with enough hkr/jmp/jtn ships on
    /// hand, rolls a raider fleet and sends it to the best scoring raid target (<see cref="GetRaidTarget"/>).
    /// Real Pascal's <c>Slot=0</c> (fleet-data-array-full) guard can't happen with this port's
    /// Dictionary — dropped, same precedent as <see cref="NpeToolkit.DeployBattleFleet"/>. No
    /// fuel-range abort check here: Pirate calls FLEET.PAS's own <c>DeployFleet</c> directly, not
    /// NPEINTR.PAS's <c>DeployBattleFleet</c> wrapper that adds that check — confirmed by reading
    /// NPE01.PAS itself, not assumed from Kingdom's own call pattern.
    /// </summary>
    private void DeployRaiders(Empire empire, Game game)
    {
        foreach (var planet in game.Galaxy.Planets.Where(p => p.Owner == empire).ToList()) {
            var ships = planet.Ships;
            if (!(ships[ShipType.HunterKiller] > 1500 && ships[ShipType.Jumpship] > 2500 && ships[ShipType.Jumptransport] > 4000)) {
                continue;
            }

            var fltSh = new ShipCounts {
                HunterKillers = Math.Min(Rnd(_random, 1500, 5000), ships[ShipType.HunterKiller]),
                Jumpships = Math.Min(Rnd(_random, 2500, 9500), ships[ShipType.Jumpship]),
                Jumptransports = Math.Min(Rnd(_random, 4000, 9500), ships[ShipType.Jumptransport]),
            };
            var fltCr = new CargoHold {
                [CargoType.Legion] = Math.Min(ships[ShipType.Jumptransport] / 2, planet.Cargo[CargoType.Legion]),
            };

            var target = GetRaidTarget(empire, fltSh, fltCr[CargoType.Legion], game);
            if (target is null) {
                continue;
            }

            var fleet = FleetLifecycle.DeployFleet(empire, planet, fltSh, fltCr, target.Location, game);
            _fleetStates[fleet] = new PirateFleetState {
                Mission = NpeMissionType.AttackWorld,
                Waiting = 1,
                Target = target,
            };
        }
    }

    /// <summary>
    /// DeployRaiders' nested GetTarget (NPE01.PAS:243-307) — the best-scoring known enemy/independent
    /// world at least AtomicLvl tech, weaker than this raider's own power and richer in troops than
    /// its planned militia. Each candidate's raw score is jittered independently (RndVar, ±25%) before
    /// comparison; the running best stays the unjittered value (verbatim quirk, not a copy error —
    /// confirmed against source).
    /// </summary>
    private IEconomicWorld? GetRaidTarget(Empire empire, ShipCounts fltSh, int gat, Game game)
    {
        IEconomicWorld? target = null;
        var fltPower = Math.Max(1, fltSh[ShipType.Jumpship] + 2 * fltSh[ShipType.HunterKiller]);
        var bestValue = 0;

        foreach (var candidate in game.Galaxy.Planets) {
            if (candidate.Owner == empire || candidate.TechLevel < TechLevel.Atomic) {
                continue;
            }

            var cargo = candidate.Cargo;
            var defenses = candidate.Defenses;
            var ships = candidate.Ships;

            var gain = (long)cargo[CargoType.Chemicals] + cargo[CargoType.Metals] + 5L * cargo[CargoType.Trillum] + cargo[CargoType.Supplies] / 2;
            var protect = 10L * defenses[DefenseType.DefenseSatellite] + defenses[DefenseType.Gdm] + 5L * defenses[DefenseType.IonCannon]
                + 2L * ships[ShipType.HunterKiller] + ships[ShipType.Jumpship] + 4L * ships[ShipType.Penetrator] + 10L * ships[ShipType.Starship];

            var possible = 0;
            if (protect < fltPower && gat > cargo[CargoType.Legion] + 2 * cargo[CargoType.NinjaLegion]) {
                possible = PascalRound((1 - (double)protect / fltPower) * (gain / 10));
            }

            if (Jitter(_random, possible, 25) > bestValue) {
                target = candidate;
                bestValue = possible;
            }
        }

        return target;
    }

    /// <summary>
    /// DeployNewFleets (NPE01.PAS:354-417) — from every owned world, composes a patrol fleet per
    /// <see cref="GetPatrolFleetComposition"/> and sends it to a hunting-ground-weighted point to wait
    /// for passing transports. Skipped for a world whose composition comes out empty (no ships at
    /// all) — matches Pascal's own <c>NOT NoShips(FltSh)</c> guard.
    /// </summary>
    private void DeployNewFleets(Empire empire, Game game)
    {
        foreach (var planet in game.Galaxy.Planets.Where(p => p.Owner == empire).ToList()) {
            var fltSh = GetPatrolFleetComposition(planet.Ships);
            if (fltSh[ShipType.HunterKiller] == 0 && fltSh[ShipType.Jumpship] == 0 && fltSh[ShipType.Jumptransport] == 0) {
                continue;
            }

            var (bx, by, destination) = GetPatrolDestination(game);
            var fleet = FleetLifecycle.DeployFleet(empire, planet, fltSh, new CargoHold(), destination, game);
            _fleetStates[fleet] = new PirateFleetState {
                Mission = NpeMissionType.WaitForTransports,
                Waiting = Rnd(_random, 2, 5),
                BlockX = bx,
                BlockY = by,
            };
        }
    }

    /// <summary>DeployNewFleets' nested GetFleetComposition (NPE01.PAS:363-380) — a different, Pirate-only threshold-band composition, not <see cref="NpeToolkit.GetFleetComposition"/> (Kingdom's power/gat-budget version of the same-named procedure). Exactly one of the three bands ever fires.</summary>
    private ShipCounts GetPatrolFleetComposition(ShipCounts ships)
    {
        var flt = new ShipCounts();

        if (ships[ShipType.Jumptransport] > 4000 && ships[ShipType.Jumpship] > 2000) {
            flt[ShipType.HunterKiller] = Math.Min(Rnd(_random, 1000, 5000), ships[ShipType.HunterKiller]);
            flt[ShipType.Jumpship] = Math.Min(Rnd(_random, 1900, 9200), ships[ShipType.Jumpship]);
            flt[ShipType.Jumptransport] = Math.Min(Rnd(_random, 3900, 9200), ships[ShipType.Jumptransport]);
        } else if (ships[ShipType.Jumptransport] > 1000 && ships[ShipType.Jumpship] > 1000 && Rnd(_random, 1, 100) <= 75) {
            flt[ShipType.Jumpship] = Math.Min(Rnd(_random, 900, 3100), ships[ShipType.Jumpship]);
            flt[ShipType.Jumptransport] = Math.Min(Rnd(_random, 1900, 2100), ships[ShipType.Jumptransport]);
        } else if (ships[ShipType.HunterKiller] > 250) {
            flt[ShipType.HunterKiller] = Math.Min(Rnd(_random, 400, 2500), ships[ShipType.HunterKiller]);
        }

        return flt;
    }

    /// <summary>
    /// GetPatrolDestination (NPE01.PAS:204-232) — a hunting-ground cell chosen by weighted random
    /// draw (heavier cells more likely), then a random point inside that 5x5 galaxy block.
    /// <see cref="Rnd"/> already returns its low bound when high&lt;low, so an all-zero grid (Total=0)
    /// resolves to the first cell scanned rather than needing a special guard here.
    /// </summary>
    private (int BX, int BY, Coordinate XY) GetPatrolDestination(Game game)
    {
        var maxBX = Math.Max(1, Math.Min(20, game.Galaxy.Size / 5));
        var maxBY = maxBX;

        var total = 0;
        for (var x = 1; x <= maxBX; x++) {
            for (var y = 1; y <= maxBY; y++) {
                total += _huntingGround[x - 1, y - 1];
            }
        }

        var rn = Rnd(_random, 1, total);
        for (var x = 1; x <= maxBX; x++) {
            for (var y = 1; y <= maxBY; y++) {
                var cell = _huntingGround[x - 1, y - 1];
                if (rn <= cell) {
                    var xy = new Coordinate(Rnd(_random, 1 + (x - 1) * 5, x * 5), Rnd(_random, 1 + (y - 1) * 5, y * 5));
                    return (x, y, xy);
                }
                rn -= cell;
            }
        }

        // Unreached in practice for this port's reference scenarios (would need a grid so lopsided
        // or a running total so far off from the fresh per-cell sum above that the scan exhausts
        // without ever satisfying rn<=cell); real Pascal has no fallback here either (BX/BY/XY would
        // be left undefined). Falls back to the galaxy's own top-left block rather than propagating
        // an exception from ordinary turn processing.
        return (1, 1, new Coordinate(1, 1));
    }

    /// <summary>
    /// UpdateFleets (NPE01.PAS:80-202) — dispatches every tracked fleet still alive and
    /// <see cref="FleetStatus.Ready"/> to its current mission. Unlike <see cref="KingdomTurnHandler"/>,
    /// real Pascal's <c>CASE Mission OF</c> here has no ELSE arm — an unmatched mission value (can't
    /// happen for a Pirate-tracked fleet, since only this class ever assigns one) is a silent no-op,
    /// not a destroy.
    /// </summary>
    private void UpdateFleets(Empire empire, Game game)
    {
        foreach (var (fleet, state) in _fleetStates.ToList()) {
            if (!game.Galaxy.Fleets.Contains(fleet) || fleet.Status != FleetStatus.Ready) {
                continue;
            }

            switch (state.Mission) {
                case NpeMissionType.Return:
                    if (game.Galaxy.GetObjectAt(fleet.Location) is IEconomicWorld ground) {
                        NpeToolkit.ImplementReturnMSN(fleet, ground, game);
                    }
                    break;

                case NpeMissionType.WaitForTransports:
                    ImplementWaitForTrnMSN(empire, fleet, state, game);
                    break;

                case NpeMissionType.AttackTransports:
                    ImplementAttackTrnMSN(fleet, state, game);
                    break;

                case NpeMissionType.AttackWorld:
                    ImplementAttackWrldMSN(empire, fleet, state, game);
                    break;
            }
        }
    }

    private void ImplementWaitForTrnMSN(Empire empire, Fleet fleet, PirateFleetState state, Game game)
    {
        var target = FindTarget(empire, fleet, game);

        if (target is null) {
            if (state.Waiting == 0) {
                ReturnHome(empire, fleet, state, game);
                _huntingGround[state.BlockX - 1, state.BlockY - 1] = unchecked((byte)(_huntingGround[state.BlockX - 1, state.BlockY - 1] - 5));
            } else {
                state.Waiting--;
            }
            return;
        }

        // GetNewPos (FLEET.PAS:437-450): predicts the target's next step toward its own destination.
        // Real Pascal writes a sentinel (Limbo) into TargetXY on a dense-nebula block; this port's
        // GetNewPos returns null instead (see its own doc comment) -- chasing the target's current
        // position rather than an unreachable predicted one is the sane fallback here, not a silent
        // divergence: either way the fleet still heads toward the target, just without the one-step
        // lead.
        var predicted = FleetMovementHandler.GetNewPos(target.Location, target.Destination ?? target.Location, game) ?? target.Location;

        FleetLifecycle.SetFleetDestination(fleet, predicted);
        state.Waiting = Rnd(_random, 1, 2);
        state.Mission = NpeMissionType.AttackTransports;
        state.Target = target;
        _huntingGround[state.BlockX - 1, state.BlockY - 1] = unchecked((byte)(_huntingGround[state.BlockX - 1, state.BlockY - 1] + 15));
    }

    private void ImplementAttackTrnMSN(Fleet fleet, PirateFleetState state, Game game)
    {
        if (state.Target is not Fleet target) {
            return;
        }

        if (target.Location == fleet.Location && game.Galaxy.Fleets.Contains(target)) {
            // NPEAttack (CaptureTransports) can remove `target` from Game.Galaxy.Fleets outright --
            // ReturnHome nulls state.Target before the attack (not after) so a save taken mid-turn
            // never has to resolve a reference to a fleet that no longer exists. Safe: the Return
            // mission this transitions to never reads Target (it re-derives its ground object from
            // the fleet's own current location instead).
            ReturnHome(fleet.Owner, fleet, state, game);

            CombatResolution.NPEAttack(fleet.Owner, fleet, target, AttackIntentionType.CaptureTransports, game, _random);
            fleet.Ships[ShipType.Fighter] = 0;
            fleet.Ships[ShipType.Transport] = 0;
            FleetLogistics.BalanceFleet(fleet.Ships, fleet.Cargo);
        } else if (state.Waiting == 0) {
            ReturnHome(fleet.Owner, fleet, state, game);
        } else {
            state.Waiting--;
        }
    }

    private void ImplementAttackWrldMSN(Empire empire, Fleet fleet, PirateFleetState state, Game game)
    {
        if (state.Target is not IEconomicWorld target) {
            return;
        }

        var abortMission = false;
        foreach (var other in game.Galaxy.Fleets.Where(f => f.Location == target.Location && f.Owner != empire).ToList()) {
            if (abortMission) {
                break;
            }

            var result = CombatResolution.NPEAttack(empire, fleet, other, AttackIntentionType.CaptureTransports, game, _random);
            if (result.Result != AttackResultType.DefenderConquered) {
                abortMission = true;
            }
        }

        ReturnHome(empire, fleet, state, game);

        if (!abortMission) {
            var result = CombatResolution.NPEAttack(empire, fleet, target, AttackIntentionType.CaptureTransports, game, _random);
            if (result.Result == AttackResultType.DefenderConquered) {
                NpeToolkit.PlunderWorld(fleet, target);
            }
        }
    }

    /// <summary>
    /// Transitions to the Return mission. Also nulls <see cref="PirateFleetState.Target"/>: Return
    /// never reads it (<see cref="UpdateFleets"/>'s own Return arm re-derives its ground object from
    /// the fleet's current location), and leaving a stale <see cref="Fleet"/> reference behind is
    /// unsafe here in a way it wasn't for Kingdom -- <see cref="ImplementAttackTrnMSN"/>'s own
    /// transition into this can run the attack that destroys that exact fleet, and a destroyed
    /// fleet has no id a `.SAV`/JSON write can resolve.
    /// </summary>
    private static void ReturnHome(Empire empire, Fleet fleet, PirateFleetState state, Game game)
    {
        state.Mission = NpeMissionType.Return;
        state.Target = null;
        state.Waiting = 0;
        var baseXY = FindNearestBase(empire, fleet.Location, game);
        if (baseXY is { } xy) {
            FleetLifecycle.SetFleetDestination(fleet, xy);
        }
    }

    /// <summary>FindTarget (NPE01.PAS:37-67) — the first (galaxy-list-order, not scored) active enemy fleet within 5 sectors carrying transports and outgunned by this fleet.</summary>
    private static Fleet? FindTarget(Empire empire, Fleet fleet, Game game)
    {
        var fltSh = fleet.Ships;

        foreach (var candidate in game.Galaxy.Fleets) {
            if (candidate.Owner == empire || candidate.Location.DistanceTo(fleet.Location) > 5) {
                continue;
            }

            var sh = candidate.Ships;
            if (sh[ShipType.Jumptransport] + sh[ShipType.Transport] > 0
                && sh[ShipType.Jumpship] + sh[ShipType.HunterKiller] <= fltSh[ShipType.Jumpship] + fltSh[ShipType.HunterKiller]
                && sh[ShipType.Penetrator] + sh[ShipType.Starship] <= fltSh[ShipType.HunterKiller] / 2) {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// FindNearestBase (NPE01.PAS:69-78) — the nearest of this empire's own planets to a point.
    /// Real Pascal's GetNearestWorlds dereferences an unconditionally-assumed result; this port
    /// returns null instead when the empire owns no planets at all (can only happen if every one of
    /// its worlds was lost while a fleet it deployed was still in flight) rather than throwing.
    /// </summary>
    private static Coordinate? FindNearestBase(Empire empire, Coordinate from, Game game)
    {
        Planet? nearest = null;
        var bestDistance = int.MaxValue;

        foreach (var planet in game.Galaxy.Planets) {
            if (planet.Owner != empire) {
                continue;
            }
            var distance = planet.Location.DistanceTo(from);
            if (distance < bestDistance) {
                nearest = planet;
                bestDistance = distance;
            }
        }

        return nearest?.Location;
    }
}
