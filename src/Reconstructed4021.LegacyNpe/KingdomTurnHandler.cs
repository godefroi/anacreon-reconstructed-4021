using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// AI for Kingdom1 (passive) and Kingdom2 (aggressive) NPE empires — NPE02.PAS's
/// ImplementKingdom1NPE, the one real implementation both persona presets share (they differ only
/// in NPECharacterRecord's seed values, not in code). Persona/per-fleet mission state/per-enemy
/// diplomacy state live as this class's own instance fields (see docs/PORT_DESIGN.md) — one instance
/// per Kingdom empire, via Game.TurnHandlers.
/// </summary>
public sealed class KingdomTurnHandler : ITurnHandler
{
    public bool IsHuman => false;

    private readonly Random _random;
    private readonly PolicyType _defaultPolicy;
    private readonly NpeCharacter _persona = new();
    private readonly Dictionary<Empire, StateDeptRecord> _state = new();
    private readonly Dictionary<Fleet, KingdomFleetState> _fleetStates = new();

    /// <summary>
    /// InitializeKingdom1NPE/InitializeKingdom2NPE (NPE02.PAS:97-185). Real Pascal calls this from
    /// NEWGAME.PAS's CreateNPEmpire (via InitializeNPE) immediately after CreateEmpire, during
    /// scenario load itself — not lazily on this empire's first turn — so <paramref name="random"/>
    /// must be the exact same <see cref="Random"/> instance driving the rest of scenario load: these
    /// draws are part of the one real Pascal RandSeed stream, not a private per-empire draw.
    /// </summary>
    public KingdomTurnHandler(Empire empire, NpeEmpireType npeType, Random random)
    {
        _random = random;

        if (npeType == NpeEmpireType.Kingdom2) {
            _defaultPolicy = PolicyType.Harass;
            _persona.ImperialistGene = Rnd(random, 50, 100);
            _persona.DefensiveGene = Rnd(random, 5, 10);
            _persona.OffensiveGene = Rnd(random, 50, 100);
            _persona.FactorGene = 25;
            _persona.RandomGene = 50;
            _persona.Provoke = Rnd(random, 50, 100);
            _persona.SphereX = Rnd(random, 25, 100);
        } else {
            _defaultPolicy = PolicyType.Neutral;
            _persona.ImperialistGene = Rnd(random, 1, 5);
            _persona.DefensiveGene = Rnd(random, 50, 75);
            _persona.OffensiveGene = Rnd(random, 1, 2);
            _persona.FactorGene = 15;
            _persona.RandomGene = 50;
            _persona.Provoke = 75;
            _persona.SphereX = Rnd(random, 25, 75);
        }

        _persona.Defensive = _persona.DefensiveGene;
        _persona.Offensive = _persona.OffensiveGene;
        _persona.Techno = 50;
        _persona.Imperialist = _persona.ImperialistGene;
        _persona.WorldPower = Rnd(random, 25, 75);
        _persona.Honorable = 50;
        _persona.Clock = 0;
        _persona.Offset = Rnd(random, 1, 10);

        NpeToolkit.SetEmpireDefenses(empire, random);
    }

    /// <summary>
    /// `.SAV`/native-JSON import (<see cref="SaveFormat.SavGameLoader"/>/<see cref="SaveFormat.GameJson"/>):
    /// reconstructs a handler from state a save file already recorded, rather than freshly
    /// rolling a new persona — real Pascal's own `LoadNPEData` reads a `Kingdom1DataRecord`
    /// wholesale into memory, it doesn't re-run `InitializeKingdom1NPE`/`2NPE`. No
    /// `SetEmpireDefenses` call here for the same reason: that seeds a *new* empire's starting
    /// defenses, and a loaded empire's `DefenseSettings` already came from Empire Data.
    /// </summary>
    internal KingdomTurnHandler(NpeEmpireType npeType, NpeCharacter persona, Dictionary<Empire, StateDeptRecord> state, Dictionary<Fleet, KingdomFleetState> fleetStates, Random random)
    {
        _random = random;
        _defaultPolicy = npeType == NpeEmpireType.Kingdom2 ? PolicyType.Harass : PolicyType.Neutral;
        _persona = persona;
        _state = state;
        _fleetStates = fleetStates;
    }

    /// <summary>Read-back seam for `.SAV`/native-JSON export — the inverse of the constructor above.</summary>
    internal NpeCharacter Persona => _persona;

    /// <summary>See <see cref="Persona"/>.</summary>
    internal IReadOnlyDictionary<Empire, StateDeptRecord> State => _state;

    /// <summary>See <see cref="Persona"/>.</summary>
    internal IReadOnlyDictionary<Fleet, KingdomFleetState> FleetStates => _fleetStates;

    /// <summary>See <see cref="Persona"/>.</summary>
    internal PolicyType DefaultPolicy => _defaultPolicy;

    public void PlayTurn(Empire empire, Game game)
    {
        NpeToolkit.EnforceNpeDataLinks(_fleetStates, game);

        if (_persona.Clock == 0) {
            // ImplementKingdom1NPE (NPE02.PAS:280-284) — "Initialize things first year."
            NpeToolkit.StateDeptReport(empire, _state, _defaultPolicy, game);
        }

        var regionCapitals = NpeToolkit.CreateRegionArray(empire, game);

        UpdateFleets(empire, regionCapitals, game);
        NpeToolkit.ReviewNews(empire, regionCapitals, _fleetStates, _persona, _state, _defaultPolicy, game, _random);

        // Wars and foreign affairs (NPE02.PAS:291-293).
        NpeToolkit.StateDepartment(empire, _persona, _state, _defaultPolicy, game, _random);
        NpeToolkit.WarCabinet(empire, regionCapitals, _fleetStates, _persona, _state, _defaultPolicy, game, _random);

        NpeToolkit.DefendEmpire(empire, regionCapitals, _fleetStates, _persona, game, _random);
        NpeToolkit.ImperialExpansion(empire, regionCapitals, _fleetStates, _persona, game, _random);

        if ((_persona.Clock + _persona.Offset) % 7 == 0) {
            NpeToolkit.StateDeptReport(empire, _state, _defaultPolicy, game);
            NpeToolkit.ReDesignateEmpire(empire, regionCapitals, game, _random);
        }

        NpeToolkit.ExplorationAndProbing(empire, regionCapitals, _persona, game, _random);

        _persona.Clock++;
    }

    /// <summary>
    /// UpdateFleets (NPE02.PAS:187-264) — dispatches every active fleet this empire's AI is tracking
    /// to its current mission's Implement*MSN handler. Snapshots <see cref="_fleetStates"/>' keys
    /// before iterating (mission handlers mutate the dictionary — Conquer/JumpAttack can destroy the
    /// fleet being processed) and re-checks each fleet's liveness against
    /// <see cref="Galaxy.Galaxy.Fleets"/> before dispatching it — matching real Pascal's own
    /// <c>IF (FltID.Index>0) AND (FltID.Index IN SetOfActiveFleets)</c> guard, needed here because a
    /// same-empire fleet can be destroyed as a side effect of an earlier fleet's own mission this
    /// same loop (ImplementStackMSN's DumpStuff can empty out and destroy a guard fleet still pending
    /// its own turn in this list).
    /// </summary>
    private void UpdateFleets(Empire empire, List<IEconomicWorld> regionCapitals, Game game)
    {
        foreach (var (fleet, state) in _fleetStates.ToList()) {
            if (!game.Galaxy.Fleets.Contains(fleet)) {
                continue;
            }

            NpeToolkit.MidCourseCorrection(empire, fleet, state, regionCapitals);

            if (fleet.Status != FleetStatus.Ready) {
                continue;
            }

            var homeBase = NpeToolkit.GetRegionalCapital(fleet, regionCapitals);
            var target = state.Target;

            switch (state.Mission) {
                case NpeMissionType.Return:
                    if (target is IEconomicWorld returnTarget) {
                        NpeToolkit.ImplementReturnMSN(fleet, returnTarget, game);
                    }
                    break;

                case NpeMissionType.Refuel:
                    if (target is IShipCargoHolder refuelTarget) {
                        NpeToolkit.ImplementRefuelMSN(fleet, refuelTarget, game);
                    }
                    break;

                case NpeMissionType.Guard:
                    if (target is IEconomicWorld guardTarget) {
                        NpeToolkit.ImplementGuardMSN(fleet, guardTarget, game);
                    }
                    break;

                case NpeMissionType.Stack:
                    NpeToolkit.ImplementStackMSN(fleet, _fleetStates, game);
                    break;

                case NpeMissionType.RaidTransports:
                    if (homeBase is not null) {
                        NpeToolkit.ImplementRaidTrnMSN(empire, fleet, homeBase, _fleetStates, game, _random);
                    }
                    break;

                case NpeMissionType.Supply:
                    if (target is IEconomicWorld supplyTarget && homeBase is not null) {
                        NpeToolkit.ImplementSupplyMSN(fleet, supplyTarget, homeBase, _fleetStates, game);
                    }
                    break;

                case NpeMissionType.SupplyTransports:
                    if (target is IEconomicWorld supplyTrnTarget) {
                        NpeToolkit.ImplementReturnMSN(fleet, supplyTrnTarget, game);
                    }
                    break;

                case NpeMissionType.Conquer:
                    if (target is IEconomicWorld conquerTarget && homeBase is not null) {
                        var enemyEmp = conquerTarget.Owner;
                        var result = NpeToolkit.ImplementConquerMSN(empire, fleet, conquerTarget, homeBase, _fleetStates, game, _random);
                        if (result == AttackResultType.DefenderConquered) {
                            NpeToolkit.NPEConquest(empire, conquerTarget, result, regionCapitals, _persona, game, _random);
                            GetOrCreateState(enemyEmp).Balance++;
                        }
                    }
                    break;

                case NpeMissionType.SlowAttack:
                case NpeMissionType.JumpAttack:
                    if (target is IEconomicWorld attackTarget && homeBase is not null) {
                        var enemyEmp = attackTarget.Owner;
                        var result = NpeToolkit.ImplementJumpAttackMSN(empire, fleet, attackTarget, homeBase, game, _random);
                        if (result == AttackResultType.DefenderConquered) {
                            NpeToolkit.NPEConquest(empire, attackTarget, result, regionCapitals, _persona, game, _random);
                            GetOrCreateState(enemyEmp).Balance++;

                            // leave fleet as garrison
                            CombatOutcome.AbortFleet(fleet, attackTarget, report: true);
                            CombatOutcome.DestroyFleet(fleet, game);
                        } else if (result == AttackResultType.None) {
                            NpeToolkit.SetRaidingFleetNewTarget(empire, fleet, attackTarget, homeBase, _fleetStates, _persona, game, _random);
                        } else {
                            NpeToolkit.SetFleetReturn(fleet, homeBase, _fleetStates);
                        }
                    }
                    break;

                default:
                    // Every mission Kingdom itself never assigns (Pirate/Berserker-only mission
                    // types, or the None sentinel) — real Pascal's own ELSE arm.
                    CombatOutcome.DestroyFleet(fleet, game);
                    break;
            }
        }
    }

    /// <summary>
    /// State[Emp] (NPETYPES.PAS's StateDeptArray) — see <see cref="NpeToolkit.GetOrCreateState"/> for
    /// why entries are created lazily rather than pre-seeded, including for
    /// <see cref="Empire.Independent"/>'s own slot.
    /// </summary>
    private StateDeptRecord GetOrCreateState(Empire emp) => NpeToolkit.GetOrCreateState(_state, emp, _defaultPolicy);
}
