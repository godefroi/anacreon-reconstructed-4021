using Microsoft.Extensions.Configuration;

namespace Reconstructed4021.Tui;

/// <summary>
/// Optional repo-root appsettings.json (same repo-root convention Program.cs's own FindRepoRoot uses
/// for scenarios/saves/logs, rather than a bin-output-relative file), loaded through .NET's own
/// Microsoft.Extensions.Configuration pipeline rather than a hand-rolled JSON read. A missing file, or
/// a missing key inside it, both default to false -- a normal checkout with no such file behaves
/// exactly as if it read <c>{ "useLegacyEmpireColors": false }</c>. Grouped into one record (rather
/// than threading each toggle through as its own constructor parameter) so a future display toggle
/// doesn't mean widening every constructor between here and Program.cs again.
/// </summary>
public sealed record TuiSettings(bool UseLegacyEmpireColors = false)
{
    public static TuiSettings Load(string repoRoot)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(repoRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        return configuration.Get<TuiSettings>() ?? new TuiSettings();
    }
}
