using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Tests;

/// <summary>Home menu &gt; About Anacreon content, loaded from the embedded Assets/about.kdl resource -- <see cref="AboutPages"/>.</summary>
public class AboutPagesTests
{
    [Test]
    public async Task Pages_HasThreePages()
    {
        await Assert.That(AboutPages.Pages.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Page1_StartsWithTheReleaseBlurb()
    {
        await Assert.That(AboutPages.Pages[0][0]).IsEqualTo("ANACREON: Reconstruction 4021  ver 1.31");
    }

    [Test]
    public async Task Page2_IsTheLicenseText()
    {
        var joined = string.Join('\n', AboutPages.Pages[1]);

        await Assert.That(joined).Contains("copyright (c) 1988-2003 George Moromisato");
        await Assert.That(joined).Contains("PROVIDED BY THE COPYRIGHT HOLDERS");
    }

    [Test]
    public async Task Page3_DescribesThisPort()
    {
        var joined = string.Join('\n', AboutPages.Pages[2]);

        await Assert.That(joined).Contains("Reconstructed4021.Tui2");
        await Assert.That(joined).DoesNotContain("Claude");
    }
}
