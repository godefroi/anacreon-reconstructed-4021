using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Reconstructed4021.Panemonde;

// conpty (the pseudo-console Windows Terminal hosts) only interprets ANSI/VT escape sequences on an
// output handle that has ENABLE_VIRTUAL_TERMINAL_PROCESSING set -- .NET does not set this for you.
// Explicit, not assumed: see the design doc discussion of why this can't just be trusted to already
// be on.
internal static class ConsoleMode
{
    private const int StdOutputHandle = -11;
    private const uint EnableWrapAtEolOutput = 0x0002;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    [SupportedOSPlatform("windows")]
    public static void EnableVirtualTerminalOutput()
    {
        var handle = GetStdHandle(StdOutputHandle);
        if (!GetConsoleMode(handle, out var mode))
        {
            throw new InvalidOperationException("GetConsoleMode failed.");
        }

        // Clearing wrap-at-EOL matters because every dirty run this app draws is explicitly
        // positioned via CUP -- it never relies on natural line-wrap -- so the one thing
        // ENABLE_WRAP_AT_EOL_OUTPUT can do for us is the classic bottom-right-corner bug: writing
        // the last cell of the last row auto-scrolls the buffer up one line, shifting the whole
        // frame we just carefully positioned.
        var desired = (mode | EnableVirtualTerminalProcessing) & ~EnableWrapAtEolOutput;
        if (desired == mode)
        {
            return;
        }

        if (!SetConsoleMode(handle, desired))
        {
            throw new InvalidOperationException("SetConsoleMode failed to set VT output mode.");
        }
    }
}
