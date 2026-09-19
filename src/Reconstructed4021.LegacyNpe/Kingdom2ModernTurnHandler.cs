using Reconstructed4021.Core;
using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// An experimental NPE turn handler for this session's evolutionary-search work -- NOT a real
/// Pascal persona, and never wired into <see cref="LegacyNpeProvider"/> or any real scenario/save
/// path. Exists so the genetic algorithm harness can hand an arbitrary evolved
/// <see cref="NpeCharacter"/> (including experimental genes no real persona ever sets, e.g.
/// <see cref="NpeCharacter.CompositionGene"/>) to a real turn-handler loop without touching
/// <see cref="KingdomTurnHandler"/>'s own internal constructor or adding more no-op-gated fields to
/// the class real games depend on. Its <see cref="PlayTurn"/> body is the same per-turn call
/// sequence as <see cref="KingdomTurnHandler.PlayTurn"/> -- both call the same <see cref="NpeToolkit"/>
/// statics, this class just owns its own dispatch shell instead of sharing one.
/// </summary>
public sealed class Kingdom2ModernTurnHandler : ITurnHandler
{
    public bool IsHuman => false;

    private readonly Random _random;
    private readonly PolicyType _defaultPolicy;
    private readonly NpeCharacter _persona;
    private readonly Dictionary<Empire, StateDeptRecord> _state = new();
    private readonly Dictionary<Fleet, KingdomFleetState> _fleetStates = new();

    public Kingdom2ModernTurnHandler(NpeCharacter persona, PolicyType defaultPolicy, Random random)
    {
        _random = random;
        _defaultPolicy = defaultPolicy;
        _persona = persona;
    }

    /// <summary>Read-back seam, matching <see cref="KingdomTurnHandler.Persona"/>.</summary>
    internal NpeCharacter Persona => _persona;

    public void PlayTurn(Empire empire, Game game)
    {
        NpeToolkit.EnforceNpeDataLinks(_fleetStates, game);

        if (_persona.Clock == 0) {
            NpeToolkit.StateDeptReport(empire, _state, _defaultPolicy, game);
        }

        var regionCapitals = NpeToolkit.CreateRegionArray(empire, game);

        UpdateFleets(empire, regionCapitals, game);
        NpeToolkit.ReviewNews(empire, regionCapitals, _fleetStates, _persona, _state, _defaultPolicy, game, _random);

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

    /// <summary>Same shape as <see cref="KingdomTurnHandler"/>'s own private <c>UpdateFleets</c> -- see its doc comment.</summary>
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

                            CombatOutcome.AbortFleet(fleet, attackTarget, report: true);
                            CombatOutcome.DestroyFleet(fleet, game);
                        } else if (result == AttackResultType.None) {
                            NpeToolkit.SetRaidingFleetNewTarget(empire, fleet, attackTarget, homeBase, _fleetStates, _persona, regionCapitals, game, _random);
                        } else {
                            NpeToolkit.SetFleetReturn(fleet, homeBase, _fleetStates);
                        }
                    }
                    break;

                default:
                    CombatOutcome.DestroyFleet(fleet, game);
                    break;
            }
        }
    }

    private StateDeptRecord GetOrCreateState(Empire emp) => NpeToolkit.GetOrCreateState(_state, emp, _defaultPolicy);
}
