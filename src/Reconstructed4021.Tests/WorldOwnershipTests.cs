using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>GrantIndependenceCommand's own effect (DESIGN.PAS:546-653), transcribed as <see cref="WorldOwnership.Liberate"/>. Hardcoded, same rationale as WorldDesignationTests (its own Redesignate is the bulk of what this wraps).</summary>
public class WorldOwnershipTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    private sealed class FixedRandom(double nextDouble, int next) : Random
    {
        public override double NextDouble() => nextDouble;
        public override int Next(int maxValue) => next;
    }

    [Test]
    public async Task Liberate_Planet_ToIndependent_ChangesTypeAndOwnerAndNotifies()
    {
        var owner = NewEmpire("Owner");
        var world = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Type = WorldType.Agricultural,
            Efficiency = 80,
        };
        var random = new FixedRandom(nextDouble: 0.5, next: 0); // 1.5+0.5=2.0 -- 80/2.0=40

        WorldOwnership.Liberate(world, Empire.Independent, random);

        await Assert.That(world.Type).IsEqualTo(WorldType.Independent);
        await Assert.That(world.Owner).IsEqualTo(Empire.Independent);
        await Assert.That(world.Efficiency).IsEqualTo(40);
        // Empire.AddNews no-ops for Independent (IsIndependent guard) -- nothing to assert on its News.
    }

    [Test]
    public async Task Liberate_Planet_ToAnotherEmpire_KeepsTypeButTransfersOwnerAndNotifies()
    {
        var owner = NewEmpire("Owner");
        var recipient = NewEmpire("Recipient");
        var world = new Planet {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Type = WorldType.Agricultural,
            Efficiency = 80,
        };
        var random = new FixedRandom(nextDouble: 0.5, next: 0);

        WorldOwnership.Liberate(world, recipient, random);

        await Assert.That(world.Type).IsEqualTo(WorldType.Agricultural);
        await Assert.That(world.Owner).IsEqualTo(recipient);
        await Assert.That(world.Efficiency).IsEqualTo(40);
        await Assert.That(recipient.News).Contains(n => n.Headline == NewsType.WorldGivenToYou && n.OtherEmpire == owner);
    }

    [Test]
    public async Task Liberate_Starbase_ToIndependent_NeverBecomesIndependentType()
    {
        // Real Pascal's own WorldID.ObjTyp<>Base guard: a starbase liberated to Independent keeps its
        // own type (redesignated to itself, for Redesignate's shared side effects only).
        var owner = NewEmpire("Owner");
        var starbase = new Starbase {
            Location = new Coordinate(0, 0),
            Owner = owner,
            Kind = StarbaseKind.Fortress,
            Type = WorldType.BaseStarbase,
            Efficiency = 80,
        };
        var random = new FixedRandom(nextDouble: 0.5, next: 0);

        WorldOwnership.Liberate(starbase, Empire.Independent, random);

        await Assert.That(starbase.Type).IsEqualTo(WorldType.BaseStarbase);
        await Assert.That(starbase.Owner).IsEqualTo(Empire.Independent);
    }
}
