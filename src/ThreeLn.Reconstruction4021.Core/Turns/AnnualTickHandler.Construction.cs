using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Turns;

public sealed partial class AnnualTickHandler
{
    /// <summary>Pascal's amb TO tri loop range (UPDATE.PAS:138) — men/nnj are never drawn on by construction.</summary>
    private static readonly CargoType[] _constructionRawMaterialTypes = [
        CargoType.Ambrosia, CargoType.Chemicals, CargoType.Metals, CargoType.Supplies, CargoType.Trillum,
    ];

    /// <summary>
    /// UPDATE.PAS:103-220 (UpdateConstruction, nested UseUpRawMaterial) plus ConstructStarbase/
    /// ConstructStargate (UPDATE.PAS:58-100) for the completion branch. Skips AddNews and the naming
    /// system (Location2Index/GetDefinedName/DeleteName/AddName) — neither exists in the port yet,
    /// same precedent as every other AddNews skip. Sector-occupancy clearing
    /// (Sector[XY.x]^[XY.y].Obj:=EmptyQuadrant) has no C# equivalent to update — Galaxy defers
    /// sector-occupancy indexing to the movement phase.
    /// </summary>
    private void UpdateConstruction(ConstructionSite site, Game game)
    {
        var fleets = game.Galaxy.Fleets.Where(f => f.Location == site.Location && f.Owner == site.Owner).ToList();
        if (!UseUpRawMaterial(site.Building, fleets))
            return;

        site.YearsToCompletion--;
        if (site.YearsToCompletion > 0)
            return;

        game.Galaxy.ConstructionSites.Remove(site);

        switch (site.Building) {
            case ConstructionType.Minefield:
                game.Galaxy.SetMine(site.Location, site.Owner);
                break;
            case ConstructionType.Gate or ConstructionType.WarpLink or ConstructionType.Disrupter:
                game.Galaxy.Stargates.Add(CreateStargate(site));
                break;
            default: // CommandBase, Fortress, IndustrialComplex, Outpost
                game.Galaxy.Starbases.Add(CreateStarbase(site));
                break;
        }
    }

    /// <summary>
    /// UPDATE.PAS:113-170. Draws down every co-located, same-empire fleet's cargo against
    /// ConsCargoNeeded on a local scratch copy first; only commits the scratch copy back to the real
    /// fleets if every cargo type in <see cref="_constructionRawMaterialTypes"/> was fully satisfied —
    /// a shortfall partway through consumes *nothing*, not even the types already found sufficient
    /// (Pascal's goto ExitLoop discards the whole scratch copy on the first shortfall). PutCargo
    /// (PRIMINTR.PAS:599-607) is a plain unclamped assignment, not PutTotalCargo's ThgLmt-clamped one
    /// — no clamp needed on write-back here either.
    /// </summary>
    private static bool UseUpRawMaterial(ConstructionType building, List<Fleet> fleets)
    {
        var needed = _constructionCargoNeeded[building];
        var scratch = fleets.ToDictionary(f => f, f => _constructionRawMaterialTypes.ToDictionary(t => t, t => f.Cargo[t]));

        foreach (var cargoType in _constructionRawMaterialTypes) {
            var rawNeeded = needed.GetValueOrDefault(cargoType, 0);
            foreach (var fleet in fleets) {
                var available = scratch[fleet][cargoType];
                var used = Math.Min(rawNeeded, available);
                scratch[fleet][cargoType] = available - used;
                rawNeeded -= used;
            }

            if (rawNeeded > 0)
                return false;
        }

        foreach (var fleet in fleets)
            foreach (var cargoType in _constructionRawMaterialTypes)
                fleet.Cargo[cargoType] = scratch[fleet][cargoType];

        return true;
    }

    /// <summary>ConstructStarbase (UPDATE.PAS:58-90).</summary>
    private Starbase CreateStarbase(ConstructionSite site)
    {
        var kind = site.Building switch {
            ConstructionType.CommandBase => StarbaseKind.CommandBase,
            ConstructionType.Fortress => StarbaseKind.Fortress,
            ConstructionType.IndustrialComplex => StarbaseKind.IndustrialComplex,
            ConstructionType.Outpost => StarbaseKind.Outpost,
            _ => throw new ArgumentOutOfRangeException(nameof(site), site.Building, "Not a starbase-building ConstructionType."),
        };

        var starbase = new Starbase {
            Location = site.Location,
            Owner = site.Owner,
            Kind = kind,
            TechLevel = site.Owner.TechnologyLevel,
            Efficiency = Rnd(10, 20),
        };

        switch (kind) {
            case StarbaseKind.Outpost:
                starbase.Population = 1;
                starbase.Type = WorldType.Outpost;
                break;
            case StarbaseKind.IndustrialComplex:
                starbase.Population = Rnd(400, 700);
                starbase.Type = WorldType.Base;
                var optimum = GetOptimumIndustry(starbase);
                foreach (var industry in Enum.GetValues<IndustryType>())
                    starbase.Industry[industry] = optimum[industry];
                break;
            default: // CommandBase, Fortress
                starbase.Population = Rnd(10, 20);
                starbase.Type = WorldType.Base;
                break;
        }

        return starbase;
    }

    /// <summary>
    /// GetOptimumIndus (INTRFACE.PAS:1264-1287). Unlike GetIndustrialDistribution/UpdateIndustry,
    /// this does NOT clamp TIP to 999 — a real Pascal asymmetry (the 999 cap only bounds the internal
    /// distribution-percentage math those two use), preserved here rather than "fixed" into
    /// consistency. Internal (not private) and static (no instance state involved): new-game world
    /// placement (Core/NewGame/) is a second real consumer.
    /// </summary>
    internal static IndustryLevels GetOptimumIndustry(IEconomicWorld world)
    {
        var dist = GetIndustrialDistribution(world);
        double tip = TotalProd(world.Population, world.TechLevel);
        if (world.IsAddictedToAmbrosia)
            tip = PascalRound(tip * AmbrosiaAdj);

        var result = new IndustryLevels();
        foreach (var industry in Enum.GetValues<IndustryType>())
            result[industry] = PascalRound(tip * (dist[industry] / 100.0) * (_classIndustryAdjustment[(world.EffectiveClass, industry)] / 100.0));
        return result;
    }

    /// <summary>ConstructStargate (UPDATE.PAS:94-100). LinkedTo starts null (Pascal's Dest:=Limbo) — establishing a real link is a separate, not-yet-ported mechanic.</summary>
    private static Stargate CreateStargate(ConstructionSite site)
    {
        var kind = site.Building switch {
            ConstructionType.Gate => StargateKind.Gate,
            ConstructionType.WarpLink => StargateKind.WarpLink,
            ConstructionType.Disrupter => StargateKind.Disrupter,
            _ => throw new ArgumentOutOfRangeException(nameof(site), site.Building, "Not a stargate-building ConstructionType."),
        };

        return new Stargate { Location = site.Location, Owner = site.Owner, Kind = kind, LinkedTo = null };
    }
}
