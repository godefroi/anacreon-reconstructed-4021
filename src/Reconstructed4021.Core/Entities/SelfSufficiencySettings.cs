namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Per-raw-material import/export self-sufficiency dial (ISSP), one of the 11 discrete levels
/// 0-10 defined in the balance tables (DATACNST.PAS's ISSP array — out of scope until that phase;
/// this just holds the player-chosen index per material). Pascal packs these 4 values into one
/// 16-bit ImpExp field's nibbles; that packing buys nothing in C#, so they're plain fields here.
/// </summary>
public sealed class SelfSufficiencySettings
{
    // Pascal's CreatePlanet always calls InitializeISSP, setting ImpExp to DefaultISSP ($5555 — every
    // nibble at index 5, ISSP[5]=1.00) — so a newly-created planet's dials start here, not at 0.
    public int Chemical { get; set; } = 5;
    public int Metal { get; set; } = 5;
    public int Supply { get; set; } = 5;
    public int Trillum { get; set; } = 5;

    /// <summary>
    /// ISSP (DATACNST.PAS:557-558): the actual over/under-need multiplier at each of the 11 dial
    /// positions. Public here (not a private copy inside <see cref="Turns.AnnualTickHandler"/>, its
    /// other reader) since Designate's own hint (<see cref="WorldDesignation.DesignationHint"/>) and
    /// the ISSP editor screen both need the same "what does dial N actually mean" answer.
    /// </summary>
    public static readonly double[] Multipliers = [0.01, 0.10, 0.25, 0.50, 0.75, 1.00, 1.50, 2.00, 3.00, 4.00, 5.00];

    /// <summary>The dial's own percentage label ("100%" at index 5) -- ChangeISSPCom's own display, minus its parenthetical import/export gloss.</summary>
    public static string DisplayPercent(int index) => $"{Multipliers[index] * 100:0}%";
}
