namespace Reconstructed4021.Panemonde;

// One full-screen piece of the game (the TMA splash, the main menu, eventually the galaxy map) --
// owns input and drawing for as long as it's current. Animation state lives in fields on the screen
// itself and is driven by Update's elapsed-time parameter, not a separate timer/callback system.
public interface IScreen
{
    // Non-null once this screen is done and wants ScreenHost to switch to a different screen (or,
    // for QuitScreen.Instance specifically, to end the run loop). Checked after every HandleKey and
    // Update call.
    IScreen? NextScreen { get; }

    void HandleKey(ConsoleKeyInfo key);

    void Update(TimeSpan elapsed);

    void Draw(FrameBuffer fb);
}
