namespace Reconstructed4021.Tui;

/// <summary>
/// PRIMINTR.PAS's MyLord: an independently-randomized honorific, used wherever real Pascal calls
/// <c>MyLord(Player)</c> -- flavor text only, with no gameplay effect, so drawing from
/// <see cref="Random.Shared"/> rather than any per-game deterministic <see cref="Random"/> matches
/// real Pascal's own "just flavor" treatment of it. Shared here (rather than duplicated per call
/// site) once a third caller needed the exact same array/gender split.
/// </summary>
internal static class Honorifics
{
    public static string MyLord(bool isEmpress) => isEmpress
        ? new[] { "My Lady", "Your Highness", "Your Excellency", "My Empress" }[Random.Shared.Next(4)]
        : new[] { "My Lord", "Your Highness", "Your Majesty", "My Liege", "Your Excellency", "Sir" }[Random.Shared.Next(6)];
}
