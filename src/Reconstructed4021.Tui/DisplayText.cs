namespace Reconstructed4021.Tui;

/// <summary>Small display-text formatting helpers shared across windows that reproduce real Pascal wording.</summary>
internal static class DisplayText
{
    /// <summary>OrdinalString (STRG.PAS:48-65): the ordinal suffix for a number (1st, 2nd, 3rd, 4th... the 11th-13th stay "th", per Penultim=1).</summary>
    public static string OrdinalSuffix(int num)
    {
        var ultim = num % 10;
        var penultim = num % 100 / 10;

        if (penultim == 1 || ultim > 3 || ultim == 0) {
            return "th";
        }

        return ultim switch { 3 => "rd", 2 => "nd", 1 => "st", _ => "th" };
    }
}
