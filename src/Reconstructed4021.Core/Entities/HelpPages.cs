using System.Reflection;
using KdlSharp;

namespace Reconstructed4021.Core.Entities;

/// <summary>
/// F1 Help (HLPWIND.PAS) content, loaded from the embedded <c>Assets/help.kdl</c> resource --
/// that file's own header comment has the full provenance (real Pascal's binary ANACREON.HLP, decoded
/// once and transcribed; the correction made to its stale page 2; and the IndexPageNo-is-a-0-based-
/// Seek-offset correction every <see cref="IndexEntry.PageNumber"/> below already carries). Parsed
/// once, statically: this is fixed content with no per-game state, so there's nothing to re-parse per
/// session.
/// </summary>
public static class HelpPages
{
    public readonly record struct IndexEntry(string Label, int PageNumber);

    private static readonly KdlNode _root = Load();

    public static readonly IReadOnlyList<IndexEntry> Index = [.. _root.Children
        .Single(n => n.Name == "index").Children
        .Select(n => new IndexEntry(n.Arguments[0].AsString()!, n.GetProperty("page")!.AsInt32()!.Value))];

    public static readonly IReadOnlyList<IReadOnlyList<string>> Pages = BuildPages();

    private static KdlNode Load()
    {
        // LogicalName="help.kdl" on the csproj's own EmbeddedResource item pins this lookup key --
        // otherwise it'd default to RootNamespace+folder-derived ("Reconstructed4021.Core.Assets.help.kdl"),
        // which silently breaks if the file moves or the project is ever renamed again (it already
        // has been once -- see the stale ThreeLn.Reconstruction4021.Core artifacts still sitting in bin/obj).
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("help.kdl")
            ?? throw new InvalidOperationException("Embedded resource help.kdl not found.");
        var document = KdlDocument.ParseStream(stream);
        return document.Nodes.Single(n => n.Name == "help");
    }

    private static List<IReadOnlyList<string>> BuildPages()
    {
        var pageNodes = _root.Children.Where(n => n.Name == "page").OrderBy(n => n.Arguments[0].AsInt32()).ToList();
        var pages = new List<IReadOnlyList<string>>();

        foreach (var pageNode in pageNodes) {
            var text = pageNode.Children.Single(n => n.Name == "text").Arguments[0].AsString()!;
            pages.Add(text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'));
        }

        return pages;
    }

    /// <summary>Case-insensitive substring scan over every page's own lines -- 14 pages of ~15
    /// lines each is small enough that a linear scan is the whole implementation, no index needed.</summary>
    public static IEnumerable<(int PageNumber, string Line)> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) {
            yield break;
        }

        for (var i = 0; i < Pages.Count; i++) {
            foreach (var line in Pages[i]) {
                if (line.Contains(query, StringComparison.OrdinalIgnoreCase)) {
                    yield return (i + 1, line.Trim());
                }
            }
        }
    }
}
