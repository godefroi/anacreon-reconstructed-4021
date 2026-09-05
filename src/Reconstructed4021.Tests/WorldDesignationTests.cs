using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Galaxy;
using Reconstructed4021.Core.NewGame;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>DesignateWorld (INTRFACE.PAS:189-223), transcribed as <see cref="WorldDesignation.Redesignate"/>. Hardcoded, same rationale as NpeToolkitTests.</summary>
public class WorldDesignationTests
{
    private static Empire NewEmpire(string name) =>
        EmpireFactory.CreateEmpire(name, null, isEmpress: false, TechLevel.Jump, restlessness: 0, centralModifier: false, foundingYear: 0);

    /// <summary>Fixed NextDouble() (the efficiency-penalty formula's own <c>Random</c> draw) and a fixed <c>Next(int)</c> (Rnd's draw for ChangeTotalRevIndex's own amount).</summary>
    private sealed class FixedRandom(double nextDouble, int next) : Random
    {
        public override double NextDouble() => nextDouble;
        public override int Next(int maxValue) => next;
    }

    [Test]
    public async Task Redesignate_NonCapital_AppliesEfficiencyPenaltyAndChangesType()
    {
        var world = new Planet {
            Location = new Coordinate(0, 0),
            Owner = NewEmpire("Owner"),
            Type = WorldType.Agricultural,
            Efficiency = 80,
        };
        var random = new FixedRandom(nextDouble: 0.5, next: 0); // 1.5+0.5=2.0 -- 80/2.0=40

        WorldDesignation.Redesignate(world, WorldType.Mine, random);

        await Assert.That(world.Type).IsEqualTo(WorldType.Mine);
        await Assert.That(world.Efficiency).IsEqualTo(40);
    }

    [Test]
    public async Task Redesignate_NonCapital_NoTypeChange_StillAppliesEfficiencyPenalty()
    {
        // Real Pascal never special-cases a no-op redesignation -- the efficiency hit fires even
        // when NewType equals the world's current type.
        var world = new Planet {
            Location = new Coordinate(0, 0),
            Owner = NewEmpire("Owner"),
            Type = WorldType.Mine,
            Efficiency = 80,
        };
        var random = new FixedRandom(nextDouble: 0.5, next: 0);

        WorldDesignation.Redesignate(world, WorldType.Mine, random);

        await Assert.That(world.Type).IsEqualTo(WorldType.Mine);
        await Assert.That(world.Efficiency).IsEqualTo(40);
    }

    [Test]
    public async Task Redesignate_ToCapital_NewCapitalHigherTech_ResetsToPredecessorTechSet()
    {
        var empire = NewEmpire("Owner");
        var oldCapital = new Planet {
            Location = new Coordinate(0, 0), Owner = empire, Type = WorldType.Capital,
            TechLevel = TechLevel.Warp, Efficiency = 80,
        };
        empire.Capital = oldCapital;
        empire.TechnologyLevel = TechLevel.Warp;

        var newCapital = new Planet {
            Location = new Coordinate(1, 1), Owner = empire, Type = WorldType.Base,
            TechLevel = TechLevel.Jump, Efficiency = 60,
        };
        var random = new FixedRandom(nextDouble: 0.5, next: 5); // ChangeTotalRevIndex's Rnd(35,45) -> 5+35=40

        WorldDesignation.Redesignate(newCapital, WorldType.Capital, random);

        // oldTech(Warp) < newTech(Jump) -- SetEmpireTechnology(emp, Jump, Warp): the empire's overall
        // level jumps to the new capital's, but its unlocked set resets to what Warp alone would grant.
        await Assert.That(empire.TechnologyLevel).IsEqualTo(TechLevel.Jump);
        await Assert.That(empire.Capital).IsSameReferenceAs(newCapital);
        await Assert.That(oldCapital.Type).IsEqualTo(WorldType.Base);
        await Assert.That(oldCapital.Efficiency).IsEqualTo(40); // 80/(1.5+0.5)=40 -- same formula, same fixed draw
        await Assert.That(empire.TotalRevolutionIndex).IsEqualTo(40); // ChangeTotalRevIndex(+40)
        await Assert.That(newCapital.Type).IsEqualTo(WorldType.Capital);
        await Assert.That(newCapital.Efficiency).IsEqualTo(30); // 60/(1.5+0.5)=30 -- the unconditional penalty on the redesignated world itself
    }

    [Test]
    public async Task Redesignate_ToCapital_NewCapitalLowerTech_ResetsToItsOwnTechSet()
    {
        var empire = NewEmpire("Owner");
        var oldCapital = new Planet {
            Location = new Coordinate(0, 0), Owner = empire, Type = WorldType.Capital,
            TechLevel = TechLevel.Jump, Efficiency = 80,
        };
        empire.Capital = oldCapital;
        empire.TechnologyLevel = TechLevel.Jump;

        var newCapital = new Planet {
            Location = new Coordinate(1, 1), Owner = empire, Type = WorldType.Base,
            TechLevel = TechLevel.Warp, Efficiency = 60,
        };
        var random = new FixedRandom(nextDouble: 0.5, next: 5);

        WorldDesignation.Redesignate(newCapital, WorldType.Capital, random);

        // oldTech(Jump) > newTech(Warp) -- SetEmpireTechnology(emp, Warp, Warp): both the level and the
        // unlocked set drop straight to what the new (lower-tech) capital itself would grant.
        await Assert.That(empire.TechnologyLevel).IsEqualTo(TechLevel.Warp);
        await Assert.That(empire.Capital).IsSameReferenceAs(newCapital);
    }
}
