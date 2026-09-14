using System.Collections.Concurrent;
using System.Diagnostics;

namespace Reconstructed4021.Panemonde;

// Runs a chain of IScreens in a real terminal: a background thread does the blocking Console.ReadKey
// read and pushes onto a queue; the main thread paces frames off a Stopwatch, drains queued input,
// updates the current screen with real elapsed time, and draws. No artificial delay on either side --
// the two mechanisms that made Terminal.Gui's own input dispatch and iteration cadence lag (see
// docs/TUI_LIBRARY_RECOMMENDATION.md) simply aren't present here.
public static class ScreenHost
{
    private const int TargetFps = 60;

    public static void Run(IScreen initial)
    {
        if (OperatingSystem.IsWindows())
        {
            ConsoleMode.EnableVirtualTerminalOutput();
        }

        Console.CursorVisible = false;

        // Cosmetic only, so a terminal/environment that can't set it (redirected output, an unusual
        // console host) shouldn't take the whole run down over it.
        if (!Console.IsOutputRedirected)
        {
            try
            {
                Console.Title = "Anacreon";
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        var width = Console.IsOutputRedirected ? 120 : Console.WindowWidth;
        var height = Console.IsOutputRedirected ? 30 : Console.WindowHeight;
        var runner = new ScreenRunner(initial, new FrameBuffer(width, height, Console.OpenStandardOutput()));

        var keys = new ConcurrentQueue<ConsoleKeyInfo>();
        var inputThread = new Thread(() => {
            // A thread blocked in Console.ReadKey can't be cancelled cleanly -- it's a background
            // thread so the process can exit without joining it, rather than trying to interrupt it.
            while (true)
            {
                keys.Enqueue(Console.ReadKey(intercept: true));
            }
        }) { IsBackground = true, Name = "Panemonde input" };

        // Our own SGR codes are left set on the real terminal after our last write, and we've
        // switched into the alternate screen buffer -- neither unwinds itself on process exit the way
        // a real application teardown would, so without this the user's shell prompt inherits our
        // last frame's colors and stays on the alternate buffer. Cover both the normal quit path and
        // Ctrl+C.
        Console.CancelKeyPress += (_, _) => ResetTerminal();
        Console.Out.Write("\x1b[?1049h"); // enter alternate screen buffer -- leaves the user's scrollback untouched.
        Console.Out.Flush();

        try
        {
            inputThread.Start();

            var frameInterval = TimeSpan.FromSeconds(1.0 / TargetFps);
            var lastFrame = Stopwatch.GetTimestamp();

            while (!runner.Quit)
            {
                // Real terminal windows resize live -- redirected output (the driver, a piped/logged
                // run) never does, and Console.WindowWidth/Height either throw or return meaningless
                // values there, so this only ever runs against a real console. Cheap enough (two
                // property reads) to check every frame rather than on some poll interval.
                if (!Console.IsOutputRedirected)
                {
                    var currentWidth = Console.WindowWidth;
                    var currentHeight = Console.WindowHeight;
                    if (currentWidth != runner.FrameBuffer.Width || currentHeight != runner.FrameBuffer.Height)
                    {
                        runner.FrameBuffer.Resize(currentWidth, currentHeight);

                        // A real clear, not just relying on Present()'s own diff: growing the window
                        // exposes real terminal cells our own front/back buffers have never touched --
                        // if the next frame's content there also happens to be blank, Present() would
                        // treat back==front and skip writing it, leaving whatever stale glass content
                        // (or the terminal's own default fill) showing through uncleared.
                        Console.Out.Write("\x1b[2J");
                        Console.Out.Flush();
                    }
                }

                while (keys.TryDequeue(out var key))
                {
                    runner.HandleKey(key);
                    if (runner.Quit)
                    {
                        break;
                    }
                }

                if (runner.Quit)
                {
                    break;
                }

                var now = Stopwatch.GetTimestamp();
                runner.Update(Stopwatch.GetElapsedTime(lastFrame, now));
                lastFrame = now;

                if (runner.Quit)
                {
                    break;
                }

                runner.Draw();
                runner.FrameBuffer.Present();

                var toSleep = frameInterval - Stopwatch.GetElapsedTime(now);
                if (toSleep > TimeSpan.Zero)
                {
                    Thread.Sleep(toSleep);
                }
            }
        }
        finally
        {
            ResetTerminal();
        }
    }

    private static void ResetTerminal()
    {
        Console.Out.Write("\x1b[0m"); // reset SGR attributes.
        Console.Out.Write("\x1b[?1049l"); // leave alternate screen buffer, restoring the user's scrollback.
        Console.Out.Flush();
        Console.CursorVisible = true;
    }
}
