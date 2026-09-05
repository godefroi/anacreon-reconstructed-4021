using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Tests;

/// <summary>F1 Help content, loaded from the embedded Assets/help.kdl resource -- <see cref="HelpPages"/>.</summary>
public class HelpPagesTests
{
    [Test]
    public async Task Index_HasAllFifteenEntries()
    {
        await Assert.That(HelpPages.Index.Count).IsEqualTo(15);
    }

    [Test]
    public async Task Index_EveryEntry_PointsToARealPage()
    {
        foreach (var entry in HelpPages.Index) {
            await Assert.That(entry.PageNumber).IsBetween(1, HelpPages.Pages.Count);
        }
    }

    [Test]
    public async Task Pages_HasFourteenPages()
    {
        await Assert.That(HelpPages.Pages.Count).IsEqualTo(14);
    }

    [Test]
    public async Task Page1_ExactContent()
    {
        var page = HelpPages.Pages[0];

        await Assert.That(page).IsEquivalentTo((string[])[
            " Index",
            "",
            "     ,   Moves the cursor.",
            " <Enter>   Selects an entry.",
            "   <Esc>   Exits the index.",
        ]);
    }

    [Test]
    public async Task Page3_FunctionKeyList_MatchesThisPortsActualFKeys()
    {
        var page = HelpPages.Pages[2];
        var joined = string.Join('\n', page);

        await Assert.That(joined).Contains("<F1>");
        await Assert.That(joined).Contains("<F3>");
        await Assert.That(joined).Contains("<F5>");
        await Assert.That(joined).Contains("<F7>");
        await Assert.That(joined).Contains("<F8>");
        await Assert.That(joined).Contains("<F9>");
        await Assert.That(joined).Contains("<F10>");
        await Assert.That(joined).DoesNotContain("<F2>");
        await Assert.That(joined).DoesNotContain("<F4>");
        await Assert.That(joined).DoesNotContain("<F6>");
    }

    // Regression test for a real bug found via live testing: IndexPageNo's own literal values
    // (HLPWIND.PAS:46-49) are 0-based Seek offsets, one less than this class's own 1-based Pages
    // index -- transcribing them directly sent "Defenses" to the Technology page instead of Ships and
    // Defenses. Each entry's target page's own title line should actually be about that topic.
    [Test]
    [Arguments("Combat", "Combat")]
    [Arguments("Construction", "Construction")]
    [Arguments("Defenses", "Ships and Defenses")]
    [Arguments("Fleet Orders", "Fleet Orders")]
    [Arguments("Function Keys", "Status Windows")]
    [Arguments("ISSP", "Industrial Self Sufficiency")]
    [Arguments("Materials", "Materials")]
    [Arguments("Raw Materials", "Materials")]
    [Arguments("Ships", "Ships and Defenses")]
    [Arguments("Technology Levels", "Technology")]
    [Arguments("Windows", "Status Windows")]
    [Arguments("World Classes", "World Classes")]
    [Arguments("World Types", "World Types")]
    public async Task Index_EntryPageNumber_LandsOnTheRightTopic(string labelPrefix, string expectedTitleSubstring)
    {
        var entry = HelpPages.Index.Single(e => e.Label.StartsWith(labelPrefix, StringComparison.Ordinal));
        var pageTitle = HelpPages.Pages[entry.PageNumber - 1][0];

        await Assert.That(pageTitle).Contains(expectedTitleSubstring);
    }

    [Test]
    public async Task Search_FindsKnownLine()
    {
        var results = HelpPages.Search("trillum").ToList();

        await Assert.That(results).IsNotEmpty();
        await Assert.That(results.Any(r => r.PageNumber == 8)).IsTrue();
    }

    [Test]
    public async Task Search_EmptyQuery_ReturnsNothing()
    {
        await Assert.That(HelpPages.Search("")).IsEmpty();
    }
}
