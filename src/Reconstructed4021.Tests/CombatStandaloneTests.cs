using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="Core.Combat.CombatStandalone"/>: DestroyConstructionOrGate and
/// SelfDestructObject. Neither has a golden-file domain — DestroyConstruction/DestroyStargate need
/// Intrface, which the patch-based harness doesn't link (see ATTACK.PAS.patch's own comment), and
/// SelfDestructObject has no Pascal-side ground truth to run at all (SBASE.PAS was never patched in).
/// Both are simple enough (plain removal, a linear Rnd(min,max) RevIndex change, no branching beyond
/// which of two target kinds) that hardcoded tests against a real Game/Galaxy fixture are proportionate
/// — the same "harness can't reach it, cover it directly" precedent as CombatOutcomeTests' own
/// human-defeat branch. LAMAttack, which has genuine formula risk, is golden-file-backed instead (see
/// LamAttackTests/lamattack.golden).
/// </summary>
public class CombatStandaloneTests
{
    [Test]
    public async Task DestroyConstructionOrGate_ConstructionSite_RemovesSiteAndChangesRevIndex()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        owner.TotalRevolutionIndex = 10;

        var site = new ConstructionSite { Location = new Coordinate(5, 5), Owner = owner, Building = ConstructionType.Fortress };
        galaxy.ConstructionSites.Add(site);

        CombatStandalone.DestroyConstructionOrGate(attacker, hkSurprise: false, site, game, new FixedRandom(0));

        await Assert.That(galaxy.ConstructionSites).DoesNotContain(site);
        await Assert.That(owner.TotalRevolutionIndex).IsEqualTo(13); // Rnd(3,7) with FixedRandom(0) = 3
        await Assert.That(owner.News).Contains(n => n.Headline == NewsType.ConstructionSiteDestroyed && n.OtherEmpire == attacker);
    }

    [Test]
    public async Task DestroyConstructionOrGate_ForcesUnknown_ReportsUnknownAttacker()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

        var site = new ConstructionSite { Location = new Coordinate(5, 5), Owner = owner, Building = ConstructionType.Fortress };
        galaxy.ConstructionSites.Add(site);

        CombatStandalone.DestroyConstructionOrGate(attacker, hkSurprise: true, site, game, new FixedRandom(0));

        await Assert.That(owner.News).Contains(n => n.Headline == NewsType.ConstructionDestroyedByUnknown && n.OtherEmpire == null);
    }

    [Test]
    public async Task DestroyConstructionOrGate_Stargate_RemovesGateAndChangesRevIndex()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var attacker = EmpireFactory.CreateEmpire("Attacker", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        owner.TotalRevolutionIndex = 10;

        var gate = new Stargate { Location = new Coordinate(5, 5), Owner = owner, Kind = StargateKind.Gate };
        galaxy.Stargates.Add(gate);

        CombatStandalone.DestroyConstructionOrGate(attacker, hkSurprise: false, gate, game, new FixedRandom(0));

        await Assert.That(galaxy.Stargates).DoesNotContain(gate);
        await Assert.That(owner.TotalRevolutionIndex).IsEqualTo(17); // Rnd(7,15) with FixedRandom(0) = 7
        await Assert.That(owner.News).Contains(n => n.Headline == NewsType.StargateDestroyed && n.OtherEmpire == attacker);
    }

    [Test]
    public async Task SelfDestructObject_Starbase_RemovesBaseAndDestroysEveryFleetAtItsLocation()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        var foreigner = EmpireFactory.CreateEmpire("Foreigner", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

        var starbase = new Starbase { Location = new Coordinate(5, 5), Owner = owner, Kind = StarbaseKind.Fortress };
        galaxy.Starbases.Add(starbase);

        // A scouted foreign observer, told BseSD via AddGlobalNews.
        var observer = EmpireFactory.CreateEmpire("Observer", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        observer.Starbases.MarkScouted(starbase);
        game.Empires.Add(owner);
        game.Empires.Add(foreigner);
        game.Empires.Add(observer);

        // Own fleet: destroyed silently, no news to its own owner.
        var ownFleet = new Fleet { Location = new Coordinate(5, 5), Owner = owner };
        ownFleet.Ships.Fighters = 5;
        galaxy.Fleets.Add(ownFleet);

        // Foreign fleet caught in the blast: destroyed, and its owner is told.
        var foreignFleet = new Fleet { Location = new Coordinate(5, 5), Owner = foreigner };
        foreignFleet.Ships.Fighters = 3;
        galaxy.Fleets.Add(foreignFleet);

        // Unrelated fleet elsewhere: untouched.
        var elsewhereFleet = new Fleet { Location = new Coordinate(50, 50), Owner = foreigner };
        galaxy.Fleets.Add(elsewhereFleet);

        CombatStandalone.SelfDestructObject(starbase, game);

        await Assert.That(galaxy.Starbases).DoesNotContain(starbase);
        await Assert.That(galaxy.Fleets).DoesNotContain(ownFleet);
        await Assert.That(galaxy.Fleets).DoesNotContain(foreignFleet);
        await Assert.That(galaxy.Fleets).Contains(elsewhereFleet);
        await Assert.That(owner.News).IsEmpty();
        await Assert.That(foreigner.News).Contains(n => n.Headline == NewsType.FleetsDestroyedInExplosion);
        await Assert.That(foreigner.News).Contains(n => n.Headline == NewsType.DestructionDetail && n.Parm1 == 3);
        await Assert.That(observer.News).Contains(n => n.Headline == NewsType.StarbaseSelfDestructed && n.OtherEmpire == owner);
    }

    [Test]
    public async Task SelfDestructObject_Stargate_RemovesGate()
    {
        var galaxy = new Galaxy(size: 100);
        var game = new Core.Game(galaxy);

        var owner = EmpireFactory.CreateEmpire("Owner", null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);
        game.Empires.Add(owner);

        var gate = new Stargate { Location = new Coordinate(5, 5), Owner = owner, Kind = StargateKind.Gate };
        galaxy.Stargates.Add(gate);

        CombatStandalone.SelfDestructObject(gate, game);

        await Assert.That(galaxy.Stargates).DoesNotContain(gate);
    }
}
