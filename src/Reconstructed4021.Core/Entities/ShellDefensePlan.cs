using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Core.Entities;

/// <summary>What percentage of each ship type is assigned to each orbital shell when defending.</summary>
public sealed class ShellDefensePlan
{
    public ShipDistribution DeepSpace { get; } = new();
    public ShipDistribution HighOrbit { get; } = new();
    public ShipDistribution Orbit { get; } = new();
    public ShipDistribution SubOrbit { get; } = new();
    public ShipDistribution Ground { get; } = new();

    /// <summary>Pascal's DefenseDistributionArray[ShellPos,...] indexing — lets <see cref="Combat.CombatEngine.GetEnemy"/> loop over shells generically.</summary>
    public ShipDistribution this[ShellPosition position] => position switch {
        ShellPosition.DeepSpace => DeepSpace,
        ShellPosition.HighOrbit => HighOrbit,
        ShellPosition.Orbit => Orbit,
        ShellPosition.SubOrbit => SubOrbit,
        ShellPosition.Ground => Ground,
        _ => throw new ArgumentOutOfRangeException(nameof(position)),
    };

    /// <summary>
    /// DATACNST.PAS:373-379's InitDefenseRecord — the one real default fleet distribution every
    /// empire starts with, including the independent faction (LOADSAVE.PAS:496-507's
    /// InitializeIndependentRecord seeds it identically). <see cref="Entities.Empire.Independent"/>
    /// is the one Empire that never round-trips through save/load (<c>GameJson.Empire(-1)</c> always
    /// returns the same static instance rather than deserializing one), so this default is the only
    /// place it ever gets a real distribution from — without it, every independent world's ships sit
    /// at 0% across every shell and never show up in <see cref="Combat.CombatEngine.GetEnemy"/>
    /// regardless of how many it actually has.
    /// </summary>
    public static ShellDefensePlan CreateDefault()
    {
        var plan = new ShellDefensePlan();
        SetShell(plan.DeepSpace, fighters: 5, hunterKillers: 50, jumpships: 10, jumptransports: 0, penetrators: 15, starships: 0, transports: 0);
        SetShell(plan.HighOrbit, fighters: 10, hunterKillers: 10, jumpships: 20, jumptransports: 0, penetrators: 30, starships: 50, transports: 0);
        SetShell(plan.Orbit, fighters: 10, hunterKillers: 10, jumpships: 30, jumptransports: 0, penetrators: 30, starships: 30, transports: 0);
        SetShell(plan.SubOrbit, fighters: 55, hunterKillers: 30, jumpships: 40, jumptransports: 0, penetrators: 25, starships: 20, transports: 0);
        SetShell(plan.Ground, fighters: 20, hunterKillers: 0, jumpships: 0, jumptransports: 100, penetrators: 0, starships: 0, transports: 100);
        return plan;
    }

    private static void SetShell(ShipDistribution shell, int fighters, int hunterKillers, int jumpships, int jumptransports, int penetrators, int starships, int transports)
    {
        shell.Fighters = fighters;
        shell.HunterKillers = hunterKillers;
        shell.Jumpships = jumpships;
        shell.Jumptransports = jumptransports;
        shell.Penetrators = penetrators;
        shell.Starships = starships;
        shell.Transports = transports;
    }
}
