using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// GrantIndependenceCommand (DESIGN.PAS:546-653), minus its own menu/confirmation prompts (a UI
/// concern for whichever front end calls this). Liberate's own IsACap check ("Not your capital... if
/// you wish for a coup d'grace, try abdicating") is likewise left to the caller -- it's a validation
/// gate on the command, not part of what actually happens once it's allowed to run.
/// </summary>
public static class WorldOwnership
{
    /// <summary>
    /// Gives <paramref name="world"/> to <paramref name="recipient"/> (Independent, or another empire).
    /// <see cref="WorldDesignation.Redesignate"/> runs first, while <paramref name="world"/> still
    /// belongs to its old owner -- Redesignate's own Capital-swap branch reads that owner, and
    /// <see cref="IEconomicWorld.Reassign"/> would clobber it before Redesignate ever saw it (matching
    /// Pascal's own DesignateWorld-then-SetStatus order). A starbase never gets typed Independent
    /// (real Pascal's own <c>WorldID.ObjTyp&lt;&gt;Base</c> guard) -- redesignating it to its own
    /// current type is a real Pascal no-op-looking call, kept only for Redesignate's shared
    /// efficiency-penalty/self-sufficiency-reset side effects, not an actual type change.
    /// </summary>
    public static void Liberate(IEconomicWorld world, Empire recipient, Random random)
    {
        var previousOwner = world.Owner;
        var newType = recipient.IsIndependent && world is not Starbase ? WorldType.Independent : world.Type;

        WorldDesignation.Redesignate(world, newType, random);
        world.InitializeSelfSufficiency();
        world.Reassign(recipient);
        recipient.AddNews(NewsType.WorldGivenToYou, world, otherEmpire: previousOwner);
    }
}
