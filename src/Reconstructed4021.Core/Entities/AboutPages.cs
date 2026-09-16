using System.Reflection;
using KdlSharp;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// Home menu &gt; About Anacreon (TMA.PAS: AboutAnacreon), loaded from the embedded
/// <c>Assets/about.kdl</c> resource -- that file's own header comment has the full provenance.
/// Parsed once, statically: this is fixed content with no per-game state.
/// </summary>
public static class AboutPages
{
    public static readonly IReadOnlyList<IReadOnlyList<string>> Pages = BuildPages();

    private static List<IReadOnlyList<string>> BuildPages()
    {
        // LogicalName="about.kdl" on the csproj's own EmbeddedResource item pins this lookup key --
        // same reasoning as HelpPages' own Load().
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("about.kdl")
            ?? throw new InvalidOperationException("Embedded resource about.kdl not found.");
        var document = KdlDocument.ParseStream(stream);
        var root = document.Nodes.Single(n => n.Name == "about");

        var pageNodes = root.Children.Where(n => n.Name == "page").OrderBy(n => n.Arguments[0].AsInt32()).ToList();
        var pages = new List<IReadOnlyList<string>>();

        foreach (var pageNode in pageNodes) {
            var text = pageNode.Children.Single(n => n.Name == "text").Arguments[0].AsString()!;
            pages.Add(text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
        }

        return pages;
    }
}
