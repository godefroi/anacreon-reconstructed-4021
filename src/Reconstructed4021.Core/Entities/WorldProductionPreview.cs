using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;
using static Reconstructed4021.Core.PascalMath;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Worlds menu > Production (CLSCOMM.PAS: ProductionCom, :96-...). Real Pascal's own GetProdInfo/
/// GetIndusInfo reimplement a second copy of the exact math <see cref="AnnualTickHandler"/>'s real
/// per-tick <c>RunProductionPipeline</c> already applies -- exactly the kind of drift-prone
/// duplication this port avoids everywhere else. This instead runs the real pipeline against a
/// throwaway clone of the world (and a throwaway clone of its owner, so any shortfall news the
/// pipeline fires lands on a discarded scratch <see cref="Empire"/>, never the real one) and reads
/// off the real before/after difference -- the same numbers a faithful reimplementation would
/// produce, without a second formula to keep in sync or drift from.
///
/// Known gap, not silently dropped: SupplyLink/SurplusLink (an Industrial Complex starbase's own
/// cargo exchange with adjacent same-empire planets) never runs here. Enabling it would mean also
/// cloning that starbase's real neighbors, since real SupplyLink/SurplusLink mutate them directly --
/// and a preview screen mutating other real worlds as a side effect of being opened isn't
/// acceptable. An Industrial Complex starbase's own preview therefore underestimates by whatever
/// that exchange would have pulled in or pushed out this tick.
/// </summary>
public static class WorldProductionPreview
{
    /// <summary>
    /// <paramref name="Distribution"/> is GetIndustrialDistribution's own %-of-100 target per
    /// industry, and <paramref name="OptimalIndustry"/> is GetIndusInfo's own OptInd (CLSCOMM.PAS:
    /// <c>Round(ATIP*IndDist[IndI]*ClsAdj[IndI]/10000)</c>, floored to 1 when Distribution is nonzero
    /// but this would otherwise round to 0) -- both computed once, up front, against the real
    /// unmutated world, since neither depends on anything the pipeline itself touches. The rest are
    /// the clone's own state after running the real pipeline once.
    /// </summary>
    public sealed record Result(
        Dictionary<IndustryType, double> Distribution,
        Dictionary<IndustryType, int> OptimalIndustry,
        IndustryLevels ProjectedIndustry,
        ShipCounts ProjectedShips,
        CargoHold ProjectedCargo,
        int ProjectedTrillumReserve);

    public static Result Compute(IEconomicWorld world, Random random)
    {
        var distribution = AnnualTickHandler.GetIndustrialDistribution(world);
        var atip = AnnualTickHandler.TotalProd(world.Population, world.TechLevel);
        var optimalIndustry = new Dictionary<IndustryType, int>();
        foreach (var industry in Enum.GetValues<IndustryType>()) {
            var dist = distribution[industry];
            var clsAdj = AnnualTickHandler.ClassIndustryAdjustment[(world.EffectiveClass, industry)];
            var opt = PascalRound(atip * dist * clsAdj / 10000.0);
            optimalIndustry[industry] = opt == 0 && dist != 0 ? 1 : opt;
        }

        var scratchOwner = new Empire { Name = world.Owner.Name, IsIndependent = world.Owner.IsIndependent };
        scratchOwner.Technology.ReplaceWith(world.Owner.Technology);

        var clone = CloneWorld(world, scratchOwner);
        new AnnualTickHandler(random).RunProductionPipeline(clone, []);

        return new Result(distribution, optimalIndustry, clone.Industry, clone.Ships, clone.Cargo, clone.TrillumReserve);
    }

    private static IEconomicWorld CloneWorld(IEconomicWorld world, Empire scratchOwner) => world switch {
        Planet p => new Planet {
            Location = p.Location,
            Owner = scratchOwner,
            Class = p.Class,
            Type = p.Type,
            SelfSufficiency = CloneSelfSufficiency(p.SelfSufficiency),
            TechLevel = p.TechLevel,
            Efficiency = p.Efficiency,
            IsAddictedToAmbrosia = p.IsAddictedToAmbrosia,
            Population = p.Population,
            Ships = CombatEngine.CloneShips(p.Ships),
            Cargo = CombatEngine.CloneCargo(p.Cargo),
            Industry = CloneIndustry(p.Industry),
            TrillumReserve = p.TrillumReserve,
        },
        Starbase s => new Starbase {
            Location = s.Location,
            Owner = scratchOwner,
            Kind = s.Kind,
            Type = s.Type,
            TechLevel = s.TechLevel,
            Efficiency = s.Efficiency,
            IsAddictedToAmbrosia = s.IsAddictedToAmbrosia,
            Population = s.Population,
            Ships = CombatEngine.CloneShips(s.Ships),
            Cargo = CombatEngine.CloneCargo(s.Cargo),
            Industry = CloneIndustry(s.Industry),
        },
        _ => throw new ArgumentException($"WorldProductionPreview: unsupported world type {world.GetType()}", nameof(world)),
    };

    private static SelfSufficiencySettings CloneSelfSufficiency(SelfSufficiencySettings source) => new() {
        Chemical = source.Chemical,
        Metal = source.Metal,
        Supply = source.Supply,
        Trillum = source.Trillum,
    };

    private static IndustryLevels CloneIndustry(IndustryLevels source) => new() {
        Bioindustry = source.Bioindustry,
        Chemical = source.Chemical,
        Mining = source.Mining,
        ShipyardGeneral = source.ShipyardGeneral,
        ShipyardJump = source.ShipyardJump,
        ShipyardStarship = source.ShipyardStarship,
        ShipyardTransport = source.ShipyardTransport,
        Supply = source.Supply,
        TrillumMining = source.TrillumMining,
    };
}
