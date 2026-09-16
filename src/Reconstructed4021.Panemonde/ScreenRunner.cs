namespace Reconstructed4021.Panemonde;

// The screen-switching logic shared by ScreenHost (real console, real input thread, real clock) and
// PanemondeDriver (a script feeds synthetic keys and a synthetic clock, no console involved at all).
// Kept separate from both so the driver never has to touch a real Console or a background thread to
// exercise a screen -- it just calls HandleKey/Update/Draw directly, same as any other unit test would.
public sealed class ScreenRunner
{
    public FrameBuffer FrameBuffer { get; }
    public IScreen Current { get; private set; }
    public bool Quit { get; private set; }

    public ScreenRunner(IScreen initial, FrameBuffer frameBuffer)
    {
        Current = initial;
        FrameBuffer = frameBuffer;
    }

    public void HandleKey(ConsoleKeyInfo key)
    {
        if (Quit)
        {
            return;
        }

        Current.HandleKey(key);
        AdvanceIfDone();
    }

    public void Update(TimeSpan elapsed)
    {
        if (Quit)
        {
            return;
        }

        Current.Update(elapsed);
        AdvanceIfDone();
    }

    public void Draw()
    {
        if (Quit)
        {
            return;
        }

        Current.Draw(FrameBuffer);
    }

    private void AdvanceIfDone()
    {
        if (Current.NextScreen is not { } next)
        {
            return;
        }

        if (ReferenceEquals(next, QuitScreen.Instance))
        {
            Quit = true;
            return;
        }

        Current = next;
    }
}
