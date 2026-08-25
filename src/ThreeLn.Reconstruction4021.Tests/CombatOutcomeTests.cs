using ThreeLn.Reconstruction4021.Core.Combat;
using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.NewGame;
using ThreeLn.Reconstruction4021.Core.Turns;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Tests;

/// <summary>
/// Phase 5 commit 5f: outcome application (Core/Combat/CombatOutcome.cs). NpeAttackTests.MatchesGoldenFile
/// already covers ConquerWorld/ConquerEmpire/RestoreCombatant/ResolveAttack end to end against real
/// Pascal output; the tests here cover the one real branch the ground-truth harness structurally can't
/// reach — a human empire's defeat (ATTACK.PAS:1120-1131's <c>EmpirePlayer(EnemyEmp)</c> branch) — since
/// the Pascal harness never marks an empire <c>IsAPlayer</c>.
/// </summary>
public class CombatOutcomeTests
{
    private sealed class HumanTurnHandler : ITurnHandler
    {
        public bool IsHuman => true;
        public void PlayTurn(Empire empire, Core.Game game) { }
    }

    private sealed class NonHumanTurnHandler : ITurnHandler
    {
        public bool IsHuman => false;
        public void PlayTurn(Empire empire, Core.Game game) { }
    }

    /// <summary>
    /// ConquerEmpire's human branch (ATTACK.PAS:1122-1128): a human empire whose capital just fell,
    /// with no other world to fall back to, is left in play with its Capital cleared and DefeatedBy set
    /// — not torn down via DestroyEmpire, and not removed from Game.Empires.
    /// </summary>
    [Test]
    public async Task ConquerEmpire_HumanEmpireWithNoOtherWorldIsDefeatedInPlace()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var conquerorCapital = new Planet { Location = new Coordinate(0, 0), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var human = EmpireFactory.CreateEmpire("Human", "pw", isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var humanCapital = new Planet { Location = new Coordinate(50, 50), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Independent, TechLevel = TechLevel.Jump };
        human.Capital = humanCapital;
        // Not in game.Galaxy.Planets under the human's own ownership -- ConquerEmpire's per-planet loop
        // finds nothing left to consider, matching a capital that was already ConquerWorld'd (reassigned)
        // by ResolveAttack before this call, the same real precondition ConquerEmpire always runs under.

        game.Empires.Add(conqueror);
        game.Empires.Add(human);
        game.TurnHandlers[conqueror] = new NonHumanTurnHandler();
        game.TurnHandlers[human] = new HumanTurnHandler();

        CombatOutcome.ConquerEmpire(conqueror, human, game, new FixedRandom(0));

        await Assert.That(human.Capital).IsNull();
        await Assert.That(human.DefeatedBy).IsEqualTo(conqueror);
        await Assert.That(game.Empires).Contains(human);
        await Assert.That(game.AnyHumanPlayersRemain).IsFalse();
    }

    /// <summary>
    /// ConquerEmpire's non-human branch (ATTACK.PAS:1130): an NPE empire in the same no-worlds-left
    /// situation is torn down via DestroyEmpire instead — removed from Game.Empires entirely, not left
    /// defeated in place.
    /// </summary>
    [Test]
    public async Task ConquerEmpire_NonHumanEmpireWithNoOtherWorldIsDestroyed()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var conqueror = EmpireFactory.CreateEmpire("Conqueror", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var conquerorCapital = new Planet { Location = new Coordinate(0, 0), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        conqueror.Capital = conquerorCapital;
        galaxy.Planets.Add(conquerorCapital);

        var npe = EmpireFactory.CreateEmpire("Npe", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var npeCapital = new Planet { Location = new Coordinate(50, 50), Owner = conqueror, Class = WorldClass.EarthLike, Type = WorldType.Independent, TechLevel = TechLevel.Jump };
        npe.Capital = npeCapital;

        game.Empires.Add(conqueror);
        game.Empires.Add(npe);
        game.TurnHandlers[conqueror] = new NonHumanTurnHandler();
        game.TurnHandlers[npe] = new NonHumanTurnHandler();

        CombatOutcome.ConquerEmpire(conqueror, npe, game, new FixedRandom(0));

        await Assert.That(game.Empires).DoesNotContain(npe);
    }
}
