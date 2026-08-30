using System.Collections.Frozen;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Turns;

/// <summary>
/// Military buildup, revolution/rebellion, and hostile life (UPDATE.PAS:1386,1388-1390 planet /
/// :1425-1426 starbase). See AnnualTickHandler.cs for the type's overall file layout.
/// </summary>
public sealed partial class AnnualTickHandler
{
    /// <summary>% of population in military at 50 military index, by world type (DATACNST.PAS:351-356).</summary>
    private static readonly FrozenDictionary<WorldType, int> _optimumMilitaryByType = new Dictionary<WorldType, int> {
        [WorldType.Agricultural] = 5,
        [WorldType.Ambrosia] = 100,
        [WorldType.Base] = 200,
        [WorldType.BaseStarbase] = 200,
        [WorldType.Capital] = 200,
        [WorldType.Chemical] = 10,
        [WorldType.Independent] = 80,
        [WorldType.JumpshipBase] = 100,
        [WorldType.JumpshipBaseStarbase] = 100,
        [WorldType.Mine] = 20,
        [WorldType.NinjaWorld] = 150,
        [WorldType.Outpost] = 0,
        [WorldType.RawMaterialMine] = 20,
        [WorldType.RawMaterialMineStarbase] = 20,
        [WorldType.StarshipBase] = 150,
        [WorldType.StarshipBaseStarbase] = 150,
        [WorldType.TransportBase] = 80,
        [WorldType.TransportBaseStarbase] = 80,
        [WorldType.University] = 1,
        [WorldType.Terraform] = 10,
        [WorldType.TrillumMine] = 30,
    }.ToFrozenDictionary();

    /// <summary>
    /// UPDATE.PAS:606-617. Grows a world's military (Cargo.Legions) toward the optimum for its type
    /// and population; only ever grows it, never shrinks it — an already-above-optimum world (e.g.
    /// one being reinforced ahead of an attack) is left alone here, not walked back down.
    /// </summary>
    private void UpdateMilitary(IEconomicWorld world)
    {
        var optimumMilitary = ClampResource(Jitter(
            PascalRound(world.Population / 150.0 * _optimumMilitaryByType[world.Type]), 10));
        if (optimumMilitary > world.Cargo.Legions)
            world.Cargo.Legions = ClampResource(
                world.Cargo.Legions + world.Population / 10.0 * (_optimumMilitaryByType[world.Type] / 100.0));
    }

    /// <summary>
    /// UPDATE.PAS:681-755. The rebellion-trigger tail (UPDATE.PAS:737-753) is a mutually-exclusive
    /// tier cascade, not one boolean condition: <c>RevolutionIndex&gt;75</c> either calls
    /// <see cref="Rebellion"/> or (roll failed, or the world is a capital) fires
    /// <c>RebellionWarning4</c> — it does NOT fall through to the lower tiers, which are real
    /// <c>else if</c> alternatives to the &gt;75 case, each firing exactly one warning headline with no
    /// state effect of its own.
    /// </summary>
    private void UpdateRevolution(IEconomicWorld world, Game game, Dictionary<Empire, int> newTotalRevIndex)
    {
        var owner = world.Owner;

        // Decrease revolution index (UPDATE.PAS:698-703).
        var empRevAdj = Jitter(TotalRevIndex(owner), 50);
        if (world.Type == WorldType.Capital)
            ChangeRevIndex(world, -Rnd(20, 30));
        else
            ChangeRevIndex(world, empRevAdj + Rnd(-5, 2));

        if (owner.IsIndependent)
            return;

        // Military presence affects revolution (UPDATE.PAS:705-735).
        var optimumMilitary = ClampResource(Jitter(
            PascalRound(world.Population / 150.0 * _optimumMilitaryByType[world.Type]), 10));
        var military = ClampResource(world.Cargo.Legions + 5.0 * world.Cargo.NinjaLegions);

        if (military > optimumMilitary) {
            if (world.RevolutionIndex > 30) {
                var factor = Rnd(1, (military - optimumMilitary) / 100);
                ChangeRevIndex(world, -factor);
                if (factor > 5)
                    owner.AddNews(NewsType.RebellionQuietedByMilitary, world);
            } else if (world.Type != WorldType.Capital && world.Type != WorldType.Base) {
                if (Rnd(1, 5) == 1) {
                    ChangeRevIndex(world, Rnd(5, 15));
                    owner.AddNews(NewsType.TroopsWantOut, world);
                }
            }
        }

        // Rebellion trigger cascade (UPDATE.PAS:737-753).
        if (world.RevolutionIndex > 75) {
            if (Rnd(1, 100) < world.RevolutionIndex && world.Type != WorldType.Capital)
                Rebellion(world, military, game, newTotalRevIndex);
            else
                owner.AddNews(NewsType.RebellionWarning4, world);
        } else if (world.RevolutionIndex > 70) {
            owner.AddNews(NewsType.RebellionWarning4, world);
        } else if (world.RevolutionIndex > 66) {
            owner.AddNews(NewsType.RebellionWarning3, world);
        } else if (world.RevolutionIndex > 43) {
            owner.AddNews(NewsType.RebellionWarning2, world);
        } else if (world.RevolutionIndex > 30) {
            owner.AddNews(NewsType.RebellionWarning1, world);
        }
    }

    private void Rebellion(IEconomicWorld world, int military, Game game, Dictionary<Empire, int> newTotalRevIndex)
    {
        var owner = world.Owner;
        var rebels = Math.Max(1, ClampResource(Math.Sqrt(world.Population) * 65));
        var menLost = rebels / 5;
        var chanceToEndRebel = military / Math.Sqrt(rebels) * 1.414213;

        var lost = Math.Min(world.Cargo.Legions, menLost);
        world.Cargo.Legions -= lost;
        menLost -= lost;
        lost = Math.Min(world.Cargo.NinjaLegions, menLost / 5);
        world.Cargo.NinjaLegions -= lost;

        if (Rnd(1, 100) < chanceToEndRebel) {
            // Empire puts down the rebellion.
            ChangeRevIndex(world, Rnd(-15, 5));
            owner.AddNews(NewsType.RebellionSuppressed, world, p1: menLost);
            newTotalRevIndex[owner] = newTotalRevIndex.GetValueOrDefault(owner) - Rnd(1, 5);
        } else {
            // World rebels and goes independent.
            world.Reassign(Empire.Independent);
            world.Type = WorldType.Independent;
            // InitializeISSP resets a planet's self-sufficiency to DefaultISSP ($5555 — all four
            // dials at the midpoint of the 0-10 range, DATACNST.PAS:516); a no-op for a starbase
            // (PRIMINTR.PAS:627-633 has no Base case) — see IEconomicWorld.
            world.InitializeSelfSufficiency();
            ChangeRevIndex(world, -Rnd(40, 50));
            // GLBRev/Rebel fire with the *old* owner ("owner", captured above) even though Reassign
            // already flipped world.Owner to Independent — matching Pascal's own Emp local, captured
            // once at Rebellion's start and never re-read (UPDATE.PAS:637,656,666-667).
            game.AddGlobalNews([owner], world, NewsType.WorldRevoltedGlobal, otherEmpire: owner);
            owner.AddNews(NewsType.WorldRebelled, world);
            world.Cargo.Legions = rebels;
            newTotalRevIndex[owner] = newTotalRevIndex.GetValueOrDefault(owner) + Rnd(5, 10);
        }

        world.Population = ClampResource(world.Population - military / 1000.0);
        world.Efficiency = Math.Max(0, world.Efficiency - Rnd(5, 15));
    }

    private void HostileLife(Planet planet)
    {
        var owner = planet.Owner;
        var menAdj = (planet.Efficiency + 50) * ((planet.Cargo.Legions + 5 * planet.Cargo.NinjaLegions) / 100.0);
        var chanceOfAttack = Math.Max(0, 25 - PascalRound((menAdj - 2000) / 100.0));

        if (Rnd(1, 100) <= chanceOfAttack) {
            if (Rnd(1, 100) <= 25) {
                var popKilled = Math.Min(planet.Population, Rnd(10, 50));
                planet.Population -= popKilled;
                ChangeRevIndex(planet, Rnd(5, 15));
                owner.AddNews(NewsType.HostileLifeKilledPopulation, planet, p1: popKilled);
            } else {
                var menKilled = Math.Min(planet.Cargo.Legions, Rnd(200, 300));
                var nnjKilled = Math.Min(planet.Cargo.NinjaLegions, Rnd(20, 50));
                planet.Cargo.Legions -= menKilled;
                planet.Cargo.NinjaLegions -= nnjKilled;
                owner.AddNews(NewsType.HostileLifeAttackedTroops, planet, p1: menKilled, p2: nnjKilled);
            }
        } else if (!owner.IsIndependent && Rnd(1, 100) <= 20) {
            var aliensJoined = Rnd(50, 150);
            planet.Cargo.NinjaLegions += aliensJoined;
            owner.AddNews(NewsType.HostileLifeJoinedTroops, planet, p1: aliensJoined);
        }
    }

    /// <summary>Empire's revolution-index feedback into per-world changes (PRIMINTR.PAS:1097-1105).</summary>
    private static int TotalRevIndex(Empire empire) =>
        empire.IsIndependent ? 0 : empire.TotalRevolutionIndex + empire.RevolutionFactor;
}
