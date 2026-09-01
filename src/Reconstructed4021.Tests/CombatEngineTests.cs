using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// The group/shell combat engine core (<see cref="Core.Combat.CombatEngine"/>). MatchesGoldenFile
/// checks CalculateCombatData/GetEnemy/DefaultDistribution/Battle/EnemySurrenders together against
/// reference/verify/golden/combat.golden — see CombatCases' own doc comment for the fixed fleet/target
/// shape every case shares. ShipsDestroyed/InRangeOfDefense's branch coverage (the advancing-group
/// damage bonus, the hunter-killer cloak, out-of-range defenses) is hardcoded below instead: each is a
/// small, fully hand-derivable formula once RNG is fixed, the same split DefensesTests uses for its own
/// starbase-only branches.
/// </summary>
public class CombatEngineTests
{
    [Test]
    [DependsOn<PascalGroundTruth.GoldenFileTests>(nameof(PascalGroundTruth.GoldenFileTests.RegenerateAllGoldenFiles))]
    [MethodDataSource(typeof(PascalGroundTruth.CombatCases), nameof(PascalGroundTruth.CombatCases.AsDataSource))]
    public async Task MatchesGoldenFile(PascalGroundTruth.CombatCase c)
    {
        var golden = PascalGroundTruth.GoldenFile.Load("combat.golden");
        var expected = golden[c.Name];

        var attacker = new Empire { Name = "Attacker" };
        attacker.Capital = new Planet { Location = new Coordinate(0, 0), Owner = attacker, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = c.AttackerCapTech };

        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = attacker };
        fleet.Ships.Fighters = c.AttackerFgt;
        fleet.Ships.HunterKillers = c.AttackerHkr;
        fleet.Ships.Jumpships = c.AttackerJmp;
        fleet.Ships.Penetrators = c.AttackerPen;
        fleet.Ships.Starships = c.AttackerSsp;

        var defender = EmpireFactory.CreateEmpire("Defender", null, isEmpress: false, TechLevel.PreTech, restlessness: 0, centralModifier: false, foundingYear: 0);
        var target = new Planet {
            Location = new Coordinate(0, 0), Owner = defender, Class = c.DefenderClass, Type = WorldType.Capital,
            TechLevel = c.DefenderTech, RevolutionIndex = c.DefenderRevIndex,
        };
        target.Ships.Fighters = c.DefenderFgt;
        target.Ships.HunterKillers = c.DefenderHkr;
        target.Ships.Jumpships = c.DefenderJmp;
        target.Ships.Jumptransports = c.DefenderJtn;
        target.Ships.Penetrators = c.DefenderPen;
        target.Ships.Starships = c.DefenderSsp;
        target.Ships.Transports = c.DefenderTrn;
        target.Defenses.Lams = c.DefenderLam;
        target.Defenses.DefenseSatellites = c.DefenderDef;
        target.Defenses.Gdms = c.DefenderGdm;
        target.Defenses.IonCannons = c.DefenderIon;
        defender.Capital = target;

        var combatData = CombatEngine.CalculateCombatData(attacker, target);
        var enemy = CombatEngine.GetEnemy(target);
        var groups = CombatEngine.DefaultDistribution(fleet);
        if (c.FighterGroupTarget is { } fighterTarget) {
            groups[0].Trg = fighterTarget;
        }

        var details = new CombatDetails();
        var casualties = new AttackTally();
        var killed = new AttackTally();
        var random = new FixedRandom(c.RngFixedValue);

        CombatEngine.Battle(groups, enemy, ShellPosition.DeepSpace, combatData, details, casualties, killed, random);
        var surrenders = CombatEngine.EnemySurrenders(groups, enemy, casualties, killed, combatData);

        await Assert.That(groups).Count().IsEqualTo(5);
        for (var i = 0; i < groups.Count; i++) {
            await Assert.That(groups[i].Num).IsEqualTo(int.Parse(expected[$"g{i + 1}num"]));
            await Assert.That((int)groups[i].Sta).IsEqualTo(int.Parse(expected[$"g{i + 1}sta"]));
        }

        await Assert.That(enemy[ShellPosition.DeepSpace, AttackType.Fighter]).IsEqualTo(int.Parse(expected["en_fgt"]));
        await Assert.That(enemy[ShellPosition.DeepSpace, AttackType.HunterKiller]).IsEqualTo(int.Parse(expected["en_hkr"]));
        await Assert.That(enemy[ShellPosition.DeepSpace, AttackType.Jumpship]).IsEqualTo(int.Parse(expected["en_jmp"]));
        await Assert.That(enemy[ShellPosition.DeepSpace, AttackType.Penetrator]).IsEqualTo(int.Parse(expected["en_pen"]));
        await Assert.That(enemy[ShellPosition.DeepSpace, AttackType.Starship]).IsEqualTo(int.Parse(expected["en_ssp"]));

        // Pascal AttackTypes ordinals 5-10 (fgt,hkr,jmp,jtn,pen,ssp) -- see runworld.pas's RunCombatCase.
        await AssertTally(AttackType.Fighter, 5);
        await AssertTally(AttackType.HunterKiller, 6);
        await AssertTally(AttackType.Jumpship, 7);
        await AssertTally(AttackType.Jumptransport, 8);
        await AssertTally(AttackType.Penetrator, 9);
        await AssertTally(AttackType.Starship, 10);

        await Assert.That(surrenders).IsEqualTo(expected["surrenders"] == "1");

        async Task AssertTally(AttackType type, int pascalOrdinal)
        {
            await Assert.That(killed[type]).IsEqualTo(int.Parse(expected[$"kill{pascalOrdinal}"]));
            await Assert.That(casualties[type]).IsEqualTo(int.Parse(expected[$"cas{pascalOrdinal}"]));
        }
    }

    // Real bug, found via a hand-built playtest scenario rather than this suite: DefaultDistribution
    // used to mutate the live fleet.Ships/fleet.Cargo objects directly (fleet.Ships[ship]=0 inside
    // DefaultGroup) instead of a local snapshot -- real Pascal's own DefaultDistribution (ATTACK.PAS:
    // 1344-1367) declares Sh/Cr as local VAR arrays, populated via GetShips/GetCargo (Pascal array
    // assignment copies by value) and never PutShips/PutCargo'd back, so the fleet's own persistent
    // Ships/Cargo are never touched by this step. With the bug, every attacking fleet's live Ships
    // silently zeroed out for the rest of the engagement, so CombatOutcome.RestoreCombatant's later
    // `ships[t] -= casualties[t]` always computed `0 - casualties` instead of the real survivor count
    // -- an attacking fleet had zero ships left after literally any attack, win or lose, regardless of
    // actual losses. Fixed by cloning Ships/Cargo before handing them to DefaultGroup. No existing
    // MatchesGoldenFile case caught this: it only ever asserts on the returned GroupRecords/tallies,
    // never on the fleet's own post-call Ships -- this test is that missing assertion.
    [Test]
    public async Task DefaultDistribution_DoesNotMutateTheFleetsOwnLiveShipsOrCargo()
    {
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = new Empire { Name = "Attacker" } };
        fleet.Ships.Fighters = 100;
        fleet.Ships.HunterKillers = 20;
        fleet.Ships.Transports = 10;
        fleet.Cargo.Legions = 50;

        var groups = CombatEngine.DefaultDistribution(fleet);

        await Assert.That(fleet.Ships.Fighters).IsEqualTo(100);
        await Assert.That(fleet.Ships.HunterKillers).IsEqualTo(20);
        await Assert.That(fleet.Ships.Transports).IsEqualTo(10);
        await Assert.That(fleet.Cargo.Legions).IsEqualTo(50);
        await Assert.That(groups.Sum(g => g.Num)).IsEqualTo(130);
    }

    // ShipsDestroyed(100,Fighter,HunterKiller,100): temp=100*(15/100)=15.0 exactly (CombatTable's
    // Fighter-vs-HunterKiller entry is 15) -- an integral result, so the Rnd(1,100)<temp2 remainder
    // roll never fires (temp2=0) regardless of which value Random supplies.
    [Test]
    public async Task ShipsDestroyed_IntegralResultNeedsNoRemainderRoll()
    {
        await Assert.That(CombatEngine.ShipsDestroyed(new FixedRandom(0), 100, AttackType.Fighter, AttackType.HunterKiller, 100)).IsEqualTo(15);
    }

    // ShipsDestroyed(33,Fighter,HunterKiller,100): temp=33*0.15=4.95, temp3=4, temp2=95 -- a real
    // fractional remainder. FixedRandom(0) makes Rnd(1,100) return 1, which is < 95, so the extra kill
    // fires; FixedRandom(99) makes it return 100, which isn't < 95, so it doesn't.
    [Test]
    public async Task ShipsDestroyed_FractionalRemainderRollsAgainstRandom()
    {
        await Assert.That(CombatEngine.ShipsDestroyed(new FixedRandom(0), 33, AttackType.Fighter, AttackType.HunterKiller, 100)).IsEqualTo(5);
        await Assert.That(CombatEngine.ShipsDestroyed(new FixedRandom(99), 33, AttackType.Fighter, AttackType.HunterKiller, 100)).IsEqualTo(4);
    }

    // ShipsDestroyed's own Adj=0 guard (ATTACK.PAS:456-457): treated as 1, not a division by zero.
    [Test]
    public async Task ShipsDestroyed_ZeroAdjustmentTreatedAsOne()
    {
        var withZeroAdj = CombatEngine.ShipsDestroyed(new FixedRandom(0), 100, AttackType.Fighter, AttackType.HunterKiller, 0);
        var withOneAdj = CombatEngine.ShipsDestroyed(new FixedRandom(0), 100, AttackType.Fighter, AttackType.HunterKiller, 1);
        await Assert.That(withZeroAdj).IsEqualTo(withOneAdj);
    }

    [Test]
    public async Task ForcesUnknown_TrueForSmallUnscoutedHunterKillerFleet()
    {
        var owner = new Empire { Name = "Attacker" };
        var target = new Empire { Name = "Target" };
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.HunterKillers = 500;

        await Assert.That(CombatEngine.ForcesUnknown(fleet, target)).IsTrue();
    }

    [Test]
    public async Task ForcesUnknown_FalseOnceTargetHasScoutedTheFleet()
    {
        var owner = new Empire { Name = "Attacker" };
        var target = new Empire { Name = "Target" };
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.HunterKillers = 500;
        target.Fleets.MarkScouted(fleet);

        await Assert.That(CombatEngine.ForcesUnknown(fleet, target)).IsFalse();
    }

    [Test]
    public async Task ForcesUnknown_FalseAboveFiveHundredHunterKillers()
    {
        var owner = new Empire { Name = "Attacker" };
        var target = new Empire { Name = "Target" };
        var fleet = new Fleet { Location = new Coordinate(0, 0), Owner = owner };
        fleet.Ships.HunterKillers = 501;

        await Assert.That(CombatEngine.ForcesUnknown(fleet, target)).IsFalse();
    }

    // GroupAttack (ATTACK.PAS:761-762): an advancing group's damage output gets a +50% bonus against
    // ssp/trn/def -- but not against another ship type like hkr, which this case targets instead to
    // isolate the bonus from ShipsDestroyed's own formula (ship-vs-ship is always in range regardless
    // of shell, so InRangeOfDefense can't also be the thing masking a difference here).
    [Test]
    public async Task Battle_AdvancingGroupGetsNoBonusAgainstAShipTypeOutsideTheBonusSet()
    {
        var (readyDamage, _) = RunSingleGroupAttack(GroupStatus.Ready, AttackType.HunterKiller);
        var (advancingDamage, _) = RunSingleGroupAttack(GroupStatus.Advancing, AttackType.HunterKiller);
        await Assert.That(advancingDamage).IsEqualTo(readyDamage);
    }

    [Test]
    public async Task Battle_AdvancingGroupGetsFiftyPercentBonusAgainstStarship()
    {
        var (readyDamage, _) = RunSingleGroupAttack(GroupStatus.Ready, AttackType.Starship);
        var (advancingDamage, _) = RunSingleGroupAttack(GroupStatus.Advancing, AttackType.Starship);
        await Assert.That(advancingDamage).IsEqualTo(readyDamage + readyDamage / 2);
    }

    [Test]
    public async Task Battle_HunterKillerUncloaksOnceItAttacksSomething()
    {
        var (_, group) = RunSingleGroupAttack(GroupStatus.Ready, AttackType.Starship, attackerType: AttackType.HunterKiller);
        await Assert.That(group.Flg).IsTrue();
    }

    // InRangeOfDefense (ATTACK.PAS:730-745): DefenseSatellite is only reachable from Orbit -- a group
    // attacking it from DeepSpace deals zero damage even though CombatTable's own Fighter-vs-def entry
    // is nonzero (5), a real "in range" gate this test isolates from ShipsDestroyed's own formula.
    [Test]
    public async Task Battle_OutOfRangeDefenseTakesNoDamage()
    {
        // DefenseSatellite is only in range from Orbit (InRangeOfDefense, ATTACK.PAS:737-738) -- attack
        // it from DeepSpace with ample stock waiting there too, so a zero result can only mean the range
        // gate fired, not "there was nothing to hit."
        var damage = RunSingleGroupAttack(GroupStatus.Ready, AttackType.DefenseSatellite, groupPosition: ShellPosition.DeepSpace).Damage;
        await Assert.That(damage).IsEqualTo(0);
    }

    [Test]
    public async Task Battle_InRangeDefenseTakesRealDamage()
    {
        var damage = RunSingleGroupAttack(GroupStatus.Ready, AttackType.DefenseSatellite, groupPosition: ShellPosition.Orbit).Damage;
        await Assert.That(damage).IsGreaterThan(0);
    }

    /// <summary>
    /// Runs one Battle round with a single attacking group (default type Fighter, 100 strong, aimed at
    /// <paramref name="target"/>) against an EnemyForces stocked directly with a large, arbitrary count
    /// of <paramref name="target"/> at <paramref name="groupPosition"/> — bypassing GetEnemy entirely so
    /// the stock is never clamped away by UpdateEnemyDestroyed's own <c>Math.Min</c>, letting
    /// GroupAttack's raw output come through in <c>killed[target]</c> unmasked. CombatDataRecord's own
    /// Target is an empty dummy Fleet: none of these tests exercise Target-kind-dependent behavior
    /// (BuildPriority1's transport-priority branch, DGrndAdj's ground-troop path), only DShipAdj, which
    /// doesn't depend on what kind of thing Target is.
    /// </summary>
    private static (int Damage, GroupRecord Group) RunSingleGroupAttack(
        GroupStatus status, AttackType target, AttackType attackerType = AttackType.Fighter, ShellPosition groupPosition = ShellPosition.DeepSpace)
    {
        var attacker = new Empire { Name = "Attacker" };
        attacker.Capital = new Planet { Location = new Coordinate(0, 0), Owner = attacker, Class = WorldClass.EarthLike, Type = WorldType.Capital, TechLevel = TechLevel.Jump };
        var dummyDefenderFleet = new Fleet { Location = new Coordinate(0, 0), Owner = new Empire { Name = "Defender" } };
        var combatData = CombatEngine.CalculateCombatData(attacker, dummyDefenderFleet);

        var group = new GroupRecord { Typ = attackerType, Num = 100, Pos = groupPosition, Sta = status, Trg = target };
        var groups = new List<GroupRecord> { group };

        var enemy = new EnemyForces { [groupPosition, target] = 999_999 };

        var details = new CombatDetails();
        var casualties = new AttackTally();
        var killed = new AttackTally();
        CombatEngine.Battle(groups, enemy, groupPosition, combatData, details, casualties, killed, new FixedRandom(0));

        return (killed[target], group);
    }
}
