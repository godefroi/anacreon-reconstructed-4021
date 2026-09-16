using System.Text;
using Reconstructed4021.Panemonde;

namespace Reconstructed4021.Tests;

/// <summary><see cref="FrameBuffer"/>'s own SGR emission for <see cref="UnderlineStyle"/>/overline -- Kitty's extended "CSI 4:n m" underline sub-parameter plus classic ECMA-48 overline (53/55).</summary>
public class FrameBufferTests
{
    [Test]
    public async Task Present_DottedUnderlineAndOverline_EmitsExpectedSgr()
    {
        var stream = new MemoryStream();
        var fb = new FrameBuffer(10, 1, stream);

        fb.DrawText(0, 0, "hi", ConsoleColor.Gray, ConsoleColor.Black, underline: UnderlineStyle.Dotted, overline: true);
        fb.Present();

        var written = Encoding.UTF8.GetString(stream.ToArray());

        await Assert.That(written).Contains("4:4"); // Dotted's own sub-parameter value.
        await Assert.That(written).Contains(";53"); // overline on.
    }

    [Test]
    public async Task Present_PlainCell_EmitsNoneAndOverlineOff()
    {
        var stream = new MemoryStream();
        var fb = new FrameBuffer(10, 1, stream);

        fb.DrawText(0, 0, "hi", ConsoleColor.Gray, ConsoleColor.Black);
        fb.Present();

        var written = Encoding.UTF8.GetString(stream.ToArray());

        await Assert.That(written).Contains("4:0"); // None's own sub-parameter value.
        await Assert.That(written).Contains(";55"); // overline off.
    }

    [Test]
    public async Task Present_StyleChangeMidRow_EmitsTwoSgrSequences()
    {
        var stream = new MemoryStream();
        var fb = new FrameBuffer(10, 1, stream);

        fb.DrawText(0, 0, "ab", ConsoleColor.Gray, ConsoleColor.Black);
        fb.DrawText(2, 0, "cd", ConsoleColor.Gray, ConsoleColor.Black, underline: UnderlineStyle.Single);
        fb.Present();

        var written = Encoding.UTF8.GetString(stream.ToArray());

        await Assert.That(written).Contains("4:0");
        await Assert.That(written).Contains("4:1");
    }
}
