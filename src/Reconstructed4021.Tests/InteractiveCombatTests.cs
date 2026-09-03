using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// InteractiveCombatState (ATTCOMM.PAS's own Engage/GroupEngage/GroupMove/GroupTarget/GroupRetreat) --
/// the player-driven round-by-round engine, distinct from CombatResolution's fully automatic one.
/// CanAdvance/CanRetreat are checked against ATTCOMM.PAS's own boolean expressions directly, not
/// re-derived.
/// </summary>
public class InteractiveCombatTests
{
    private static readonly CombatDataRecord PlanetCombatData = new(
        new Planet { Location = new Coordinate(0, 0), Owner = Empire.Independent, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Bio },
        TechLevel.Bio, AShipAdj: 100, DShipAdj: 100, DGrndAdj: 100, MaxGdm: 0, RevIndex: 0);

    private static readonly CombatDataRecord FleetCombatData = new(
        new Fleet { Location = new Coordinate(0, 0), Owner = Empire.Independent },
        TechLevel.Bio, AShipAdj: 100, DShipAdj: 100, DGrndAdj: 100, MaxGdm: 0, RevIndex: 0);

    private static GroupRecord Group(AttackType typ, ShellPosition pos, GroupStatus sta = GroupStatus.Ready) =>
        new() { Typ = typ, Num = 10, Pos = pos, Sta = sta };

    private static InteractiveCombatState State(CombatDataRecord combatData, params GroupRecord[] groups) =>
        new([.. groups], new EnemyForces(), combatData);

    [Test]
    public async Task CanAdvance_DestroyedGroup_IsFalse()
    {
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.DeepSpace, GroupStatus.Destroyed));
        await Assert.That(state.CanAdvance(state.Groups[0])).IsFalse();
    }

    [Test]
    public async Task CanAdvance_AtGround_IsFalse()
    {
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.Ground));
        await Assert.That(state.CanAdvance(state.Groups[0])).IsFalse();
    }

    [Test]
    public async Task CanAdvance_FleetTarget_AtOrbit_IsFalse()
    {
        // Fleet combat has no SubOrbit/Ground shell to fight into -- Orbit is as deep as it goes.
        var state = State(FleetCombatData, Group(AttackType.Fighter, ShellPosition.Orbit));
        await Assert.That(state.CanAdvance(state.Groups[0])).IsFalse();
    }

    [Test]
    public async Task CanAdvance_WorldTarget_AtOrbit_IsTrue()
    {
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.Orbit));
        await Assert.That(state.CanAdvance(state.Groups[0])).IsTrue();
    }

    [Test]
    public async Task CanAdvance_AtSubOrbit_OnlyFighterTransportJumptransport_CanReachGround()
    {
        var state = State(PlanetCombatData,
            Group(AttackType.Fighter, ShellPosition.SubOrbit),
            Group(AttackType.Transport, ShellPosition.SubOrbit),
            Group(AttackType.Jumptransport, ShellPosition.SubOrbit),
            Group(AttackType.HunterKiller, ShellPosition.SubOrbit),
            Group(AttackType.Jumpship, ShellPosition.SubOrbit),
            Group(AttackType.Penetrator, ShellPosition.SubOrbit),
            Group(AttackType.Starship, ShellPosition.SubOrbit));

        await Assert.That(state.CanAdvance(state.Groups[0])).IsTrue();
        await Assert.That(state.CanAdvance(state.Groups[1])).IsTrue();
        await Assert.That(state.CanAdvance(state.Groups[2])).IsTrue();
        await Assert.That(state.CanAdvance(state.Groups[3])).IsFalse();
        await Assert.That(state.CanAdvance(state.Groups[4])).IsFalse();
        await Assert.That(state.CanAdvance(state.Groups[5])).IsFalse();
        await Assert.That(state.CanAdvance(state.Groups[6])).IsFalse();
    }

    [Test]
    public async Task CanRetreat_DestroyedGroup_IsFalse()
    {
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.DeepSpace, GroupStatus.Destroyed));
        await Assert.That(state.CanRetreat(state.Groups[0])).IsFalse();
    }

    [Test]
    public async Task CanRetreat_AtGround_OnlyFighterCanRetreat()
    {
        var state = State(PlanetCombatData,
            Group(AttackType.Fighter, ShellPosition.Ground),
            Group(AttackType.Legion, ShellPosition.Ground),
            Group(AttackType.Transport, ShellPosition.Ground));

        await Assert.That(state.CanRetreat(state.Groups[0])).IsTrue();
        await Assert.That(state.CanRetreat(state.Groups[1])).IsFalse();
        await Assert.That(state.CanRetreat(state.Groups[2])).IsFalse();
    }

    [Test]
    public async Task CanRetreat_AtDeepSpace_IsFalse()
    {
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.DeepSpace));
        await Assert.That(state.CanRetreat(state.Groups[0])).IsFalse();
    }

    [Test]
    public async Task CanRetreat_MidShells_IsTrue()
    {
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.SubOrbit));
        await Assert.That(state.CanRetreat(state.Groups[0])).IsTrue();
    }

    [Test]
    public async Task SetTarget_DoesNotConsumeARoundOrChangeResult()
    {
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.DeepSpace));
        state.SetTarget(state.Groups[0], AttackType.DefenseSatellite);

        await Assert.That(state.Groups[0].Trg).IsEqualTo(AttackType.DefenseSatellite);
        await Assert.That(state.Result).IsEqualTo(AttackResultType.None);
        await Assert.That(state.Casualties[AttackType.Fighter]).IsEqualTo(0);
    }

    [Test]
    public async Task SetTarget_Null_ClearsTrg()
    {
        var group = Group(AttackType.Fighter, ShellPosition.DeepSpace);
        group.Trg = AttackType.Gdm;
        var state = State(PlanetCombatData, group);

        state.SetTarget(state.Groups[0], null);

        await Assert.That(state.Groups[0].Trg).IsNull();
    }

    [Test]
    public async Task CancelAllQueuedMoves_RevertsAdvancingAndRetreatingToReady_LeavesDestroyedAlone()
    {
        var state = State(PlanetCombatData,
            Group(AttackType.Fighter, ShellPosition.DeepSpace, GroupStatus.Advancing),
            Group(AttackType.HunterKiller, ShellPosition.HighOrbit, GroupStatus.Retreating),
            Group(AttackType.Jumpship, ShellPosition.Orbit, GroupStatus.Destroyed));

        state.CancelAllQueuedMoves();

        await Assert.That(state.Groups[0].Sta).IsEqualTo(GroupStatus.Ready);
        await Assert.That(state.Groups[1].Sta).IsEqualTo(GroupStatus.Ready);
        await Assert.That(state.Groups[2].Sta).IsEqualTo(GroupStatus.Destroyed);
    }

    [Test]
    public async Task Engage_ThrowsOnceTheEngagementIsOver()
    {
        // A single already-destroyed group -- the very first RunRound sees AllGroupsDestroyed and ends it.
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.DeepSpace, GroupStatus.Destroyed));

        state.Engage(new FixedRandom(0));
        await Assert.That(state.IsOver).IsTrue();
        await Assert.That(state.Result).IsEqualTo(AttackResultType.AttackerDestroyed);

        await Assert.That(() => state.Engage(new FixedRandom(0))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Retreat_ForcesAttackerRetreats_AndEndsTheEngagement()
    {
        // A lone group at DeepSpace with no target, against an empty EnemyForces -- nothing can kill it
        // this round, so AllGroupsDestroyed stays false and Result should land on AttackerRetreats.
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.DeepSpace));

        state.Retreat(new FixedRandom(0));

        await Assert.That(state.IsOver).IsTrue();
        await Assert.That(state.Result).IsEqualTo(AttackResultType.AttackerRetreats);
    }

    [Test]
    public async Task Retreat_StillYieldsAttackerDestroyed_IfTheForcedRoundWipesEveryGroupOut()
    {
        // AllGroupsDestroyed is checked unconditionally every round, even one Retreat forced -- a
        // single already-destroyed group makes RunRound see it immediately, overriding the pre-set
        // AttackerRetreats back to AttackerDestroyed (ATTCOMM.PAS's own GroupEngage order: destroyed
        // check first, retreat-forced surrender-suppression second).
        var state = State(PlanetCombatData, Group(AttackType.Fighter, ShellPosition.DeepSpace, GroupStatus.Destroyed));

        state.Retreat(new FixedRandom(0));

        await Assert.That(state.Result).IsEqualTo(AttackResultType.AttackerDestroyed);
    }

    [Test]
    public async Task BeginEngagement_ComputesCombatDataAndEnemyForces_LikeAttackCommandsOwnSetup()
    {
        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 0);
        attacker.Capital = new Planet { Location = new Coordinate(0, 0), Owner = attacker, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Bio };

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.Bio, restlessness: 0, centralModifier: false, foundingYear: 0);
        var target = new Planet { Location = new Coordinate(0, 0), Owner = defender, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Bio };
        target.Ships.Fighters = 5;

        var expectedCombatData = CombatEngine.CalculateCombatData(attacker, target);
        var expectedEnemy = CombatEngine.GetEnemy(target);

        var state = InteractiveCombat.BeginEngagement(attacker, target, []);

        await Assert.That(state.CombatData).IsEqualTo(expectedCombatData);
        foreach (var pos in Enum.GetValues<ShellPosition>()) {
            foreach (var type in Enum.GetValues<AttackType>()) {
                await Assert.That(state.Enemy[pos, type]).IsEqualTo(expectedEnemy[pos, type]);
            }
        }
        await Assert.That(state.Groups).IsEmpty();
    }

    [Test]
    public async Task AutoTarget_Off_LeavesTrgUnsetDespiteRealCandidatesExisting()
    {
        var enemy = new EnemyForces();
        enemy[ShellPosition.DeepSpace, AttackType.Fighter] = 50;
        var group = Group(AttackType.Fighter, ShellPosition.DeepSpace);
        var state = new InteractiveCombatState([group], enemy, PlanetCombatData); // AutoTarget defaults false

        state.Engage(new FixedRandom(0));

        await Assert.That(group.Trg).IsNull();
    }

    [Test]
    public async Task AutoTarget_On_AssignsTargetToUnpinnedGroup()
    {
        var enemy = new EnemyForces();
        enemy[ShellPosition.DeepSpace, AttackType.Fighter] = 50;
        var group = Group(AttackType.Fighter, ShellPosition.DeepSpace);
        var state = new InteractiveCombatState([group], enemy, PlanetCombatData) { AutoTarget = true };

        state.Engage(new FixedRandom(0));

        await Assert.That(group.Trg).IsEqualTo(AttackType.Fighter); // the only real candidate present
    }

    [Test]
    public async Task AutoTarget_On_PreservesManuallyPinnedTarget_ButStillAssignsUnpinnedGroups()
    {
        var enemy = new EnemyForces();
        enemy[ShellPosition.DeepSpace, AttackType.Fighter] = 50; // no Gdm forces at all -- auto would never pick Gdm on its own
        var pinnedGroup = Group(AttackType.Fighter, ShellPosition.DeepSpace);
        var unpinnedGroup = Group(AttackType.Fighter, ShellPosition.DeepSpace);
        var state = new InteractiveCombatState([pinnedGroup, unpinnedGroup], enemy, PlanetCombatData) { AutoTarget = true };
        state.SetTarget(pinnedGroup, AttackType.Gdm);

        state.Engage(new FixedRandom(0));

        await Assert.That(pinnedGroup.Trg).IsEqualTo(AttackType.Gdm); // preserved despite no real Gdm candidates
        await Assert.That(unpinnedGroup.Trg).IsEqualTo(AttackType.Fighter); // auto picked the only real candidate
    }

    [Test]
    public async Task SetTarget_Null_UnpinsGroup_AutoTakesOverNextRound()
    {
        var enemy = new EnemyForces();
        enemy[ShellPosition.DeepSpace, AttackType.Fighter] = 50;
        var group = Group(AttackType.Fighter, ShellPosition.DeepSpace);
        var state = new InteractiveCombatState([group], enemy, PlanetCombatData) { AutoTarget = true };
        state.SetTarget(group, AttackType.Gdm);
        state.SetTarget(group, null); // un-pin

        state.Engage(new FixedRandom(0));

        await Assert.That(group.Trg).IsEqualTo(AttackType.Fighter); // auto took back over once the pin was cleared
    }

    [Test]
    public async Task AutoTarget_WorldTarget_GroupAtGround_AlwaysTargetsLegion()
    {
        var group = Group(AttackType.Fighter, ShellPosition.Ground);
        var state = new InteractiveCombatState([group], new EnemyForces(), PlanetCombatData) { AutoTarget = true };

        state.Engage(new FixedRandom(0));

        await Assert.That(group.Trg).IsEqualTo(AttackType.Legion); // WorldEngageTargetting's own unconditional Ground override
    }

    [Test]
    public async Task AutoTarget_FleetTarget_DoesNotForceGroundGroupToLegion()
    {
        var group = Group(AttackType.Fighter, ShellPosition.Ground);
        var state = new InteractiveCombatState([group], new EnemyForces(), FleetCombatData) { AutoTarget = true };

        state.Engage(new FixedRandom(0));

        await Assert.That(group.Trg).IsNull(); // no real candidates, and no Ground override for a Fleet target
    }
}
