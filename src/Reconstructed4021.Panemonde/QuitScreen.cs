namespace Reconstructed4021.Panemonde;

// Sentinel IScreen -- a screen sets NextScreen to QuitScreen.Instance to mean "end the run loop"
// rather than "switch to this screen." ScreenRunner checks for it by reference, never calls any of
// its members.
public sealed class QuitScreen : IScreen
{
    public static readonly QuitScreen Instance = new();

    private QuitScreen() { }

    public IScreen? NextScreen => null;

    public void HandleKey(ConsoleKeyInfo key) { }

    public void Update(TimeSpan elapsed) { }

    public void Draw(FrameBuffer fb) { }
}
