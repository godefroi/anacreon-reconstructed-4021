using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Core.Presentation;

/// <summary>
/// PRIMINTR.PAS's MyLord: an independently-randomized honorific, used wherever real Pascal calls
/// <c>MyLord(Player)</c> -- flavor text only, with no gameplay effect, so drawing from
/// <see cref="Random.Shared"/> rather than any per-game deterministic <see cref="Random"/> matches
/// real Pascal's own "just flavor" treatment of it. Shared here (rather than duplicated per call
/// site) once a third caller needed the exact same array/gender split.
/// </summary>
public static class Honorifics
{
    private static readonly string[] EmpressHonorifics = ["My Lady", "Your Highness", "Your Excellency", "My Empress"];
    private static readonly string[] LordHonorifics = ["My Lord", "Your Highness", "Your Majesty", "My Liege", "Your Excellency", "Sir"];

    public static string MyLord(bool isEmpress)
    {
        var options = isEmpress ? EmpressHonorifics : LordHonorifics;
        return options[Random.Shared.Next(options.Length)];
    }

    /// <summary>
    /// EnemyConquered's own Message1 (ATTCOMM.PAS:1403-1412) -- always wins for a conquered capital, and
    /// shared verbatim by AutoAttackCommand's own DefConqueredART/non-Fleet branch (ATTCOMM.PAS:1673-1683),
    /// which duplicates this exact text in real Pascal too. Shared here (both Auto Attack's own headless
    /// resolution and the interactive Tactical Battle Display need the identical string) rather than a
    /// second UI-layer copy.
    /// </summary>
    public static string SovereigntyDeclaration(Empire player, ISectorObject subject)
    {
        var noun = subject switch { Fleet => "fleet", Starbase => "starbase", _ => "planet" };
        var lord = player.IsEmpress ? "Her Imperial Majesty, Lady" : "His Imperial Majesty, Lord";
        return $"In the name of {lord} of {player.Name}, I hereby declare\nthis {noun} to be under the sovereign jurisdiction of the\n{player.Name} Empire.";
    }
}
