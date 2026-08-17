namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Per-raw-material import/export self-sufficiency dial (ISSP), one of the 11 discrete levels
/// 0-10 defined in the balance tables (DATACNST.PAS's ISSP array — out of scope until that phase;
/// this just holds the player-chosen index per material). Pascal packs these 4 values into one
/// 16-bit ImpExp field's nibbles; that packing buys nothing in C#, so they're plain fields here.
/// </summary>
public sealed class SelfSufficiencySettings
{
    public int Chemical { get; set; }
    public int Metal { get; set; }
    public int Supply { get; set; }
    public int Trillum { get; set; }
}
