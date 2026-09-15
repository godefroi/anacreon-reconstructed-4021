using Microsoft.Extensions.Configuration;

namespace Reconstructed4021.Tui2;

// Same appsettings.json-optional-file pattern as Reconstructed4021.Tui's own TuiSettings; a separate
// type (not a shared one both projects reference) since neither project depends on the other. Both
// can read the same repo-root appsettings.json file without clashing -- Configuration.Get<T>() only
// binds the keys it recognizes. Doesn't mirror TuiSettings.UseLegacyEmpireColors: that flag gates a
// Terminal.Gui-specific quirk in Tui's own GalaxyView (falling back to an empty ColorScheme attribute
// map), and this project's BuildEmpireColors has no equivalent legacy path to gate.
public sealed record Tui2Settings(bool UseLegacyOrderResolution = false, bool SmartAutoAttackRetreat = true)
{
    public static Tui2Settings Load(string repoRoot)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        return configuration.Get<Tui2Settings>() ?? new Tui2Settings();
    }
}
