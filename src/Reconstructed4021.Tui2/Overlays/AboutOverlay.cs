using Reconstructed4021.Panemonde;
using Reconstructed4021.Panemonde.Widgets;

namespace Reconstructed4021.Tui2.Overlays;

// Home menu > About Anacreon (TMA.PAS: AboutAnacreon, :82-143). Real Pascal's own version is two
// screens (the 1.31 release blurb, then the BSD-style license) with an ASCII-art compass/globe logo
// down the left column of the first -- the logo isn't reproduced here (decorative CP437 line-drawing
// art, not project history worth preserving pixel-for-pixel), but every word of both screens' own text
// is kept, verbatim, as their own pages below. A third page, with no Pascal equivalent, covers this
// port itself -- added, not substituted, so the original two pages stay exactly what a player of the
// real 1.31 release would have seen here.
internal sealed class AboutOverlay : IOverlay
{
    private const int Width = 80;
    private const int Height = 23;

    private static readonly string[][] Pages =
    [
        [
            "ANACREON: Reconstruction 4021  ver 1.31",
            "Released September 24, 2003",
            "",
            "After thirteen years, Anacreon has finally been resurrected!  Thanks to George",
            "Moromisato for releasing the source code, and for his genius in creating the",
            "game in the first place.",
            "",
            "In version 1.31, the following long-standing bugs have been fixed:",
            "",
            "  - Any unnamed Fleet240 or Enemy240 was unviewable except through the F5",
            "    screen",
            "  - Advanced ships (eg. starships) were allowed to defend a planet, making",
            "    the planet unconquerable.",
            "",
            "Check out the Ardreil: Anacreon Reconstruction project at",
            "sourceforge.net/projects/ardreil/ for information on a network-aware Anacreon",
            "game.",
            "",
            "Report bugs to Adam Luker (zot@aapc.com)",
        ],
        [
            "Legal stuff:",
            "",
            "Based in part on Anacreon: Reconstruction 4021",
            "copyright (c) 1988-2003 George Moromisato.",
            "All rights reserved.",
            "",
            "THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS \"AS IS\"",
            "AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE",
            "IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE",
            "ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE",
            "LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR",
            "CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF",
            "SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS",
            "INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN",
            "CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)",
            "ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE",
            "POSSIBILITY OF SUCH DAMAGE.",
            "",
            "(Of course, this game isn't really just \"based in part on\" Anacreon. :)",
        ],
        [
            "About this reconstruction (Reconstructed4021)",
            "",
            "This is a from-scratch C#/.NET port of the Turbo Pascal source above (the real",
            "1.31 release, not a behavioral reverse-engineering) -- ATTACK.PAS, UPDATE.PAS,",
            "MESS.PAS, and the rest of the original unit files are this port's own primary",
            "source of truth, cited by file and line throughout its own code comments.",
            "",
            "Two presentation layers sit on top of the same Core game engine:",
            "Reconstructed4021.Tui (built on Terminal.Gui) and this one,",
            "Reconstructed4021.Tui2 -- a from-scratch immediate-mode renderer, built once",
            "Terminal.Gui's own frame-time ceiling became the bottleneck a real game loop",
            "needed past.",
        ],
    ];

    private int _pageIndex;

    public bool IsDismissed { get; private set; }

    public void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.PageUp or ConsoleKey.LeftArrow:
                _pageIndex = Math.Max(0, _pageIndex - 1);
                break;
            case ConsoleKey.PageDown or ConsoleKey.RightArrow:
                _pageIndex = Math.Min(Pages.Length - 1, _pageIndex + 1);
                break;
            case ConsoleKey.Escape:
                IsDismissed = true;
                break;
        }
    }

    public void Draw(FrameBuffer fb)
    {
        var width = Math.Min(Width, fb.Width);
        var height = Math.Min(Height, fb.Height);
        var x = Math.Max(0, (fb.Width - width) / 2);
        var y = Math.Max(0, (fb.Height - height) / 2);

        BoxDrawing.DrawSingleLine(fb, x, y, width, height, ConsoleColor.Gray, ConsoleColor.Black);
        var titleText = " About Anacreon ";
        fb.DrawText(x + Math.Max(1, (width - titleText.Length) / 2), y, titleText, ConsoleColor.White, ConsoleColor.Black);

        var lines = Pages[_pageIndex];
        for (var i = 0; i < lines.Length && i < height - 3; i++)
        {
            fb.DrawText(x + 1, y + 1 + i, lines[i], ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
        }

        var hint = $"Page {_pageIndex + 1} of {Pages.Length}   PgUp/PgDn: page   Esc: close";
        fb.DrawText(x + 1, y + height - 2, hint, ConsoleColor.Gray, ConsoleColor.Black, maxWidth: width - 2);
    }
}
