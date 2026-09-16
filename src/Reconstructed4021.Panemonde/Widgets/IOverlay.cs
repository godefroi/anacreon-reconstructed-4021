namespace Reconstructed4021.Panemonde.Widgets;

// A modal panel stacked on top of a screen that keeps running underneath it (Close Up over the
// galaxy map, then a sector-object picker over that) -- GameShell's own AddCloseUpOverlay/AddPanel
// precedent, minus Terminal.Gui's Toplevel child-view mechanics. The host screen owns a stack of
// these; only the top one ever receives a key, and it's popped the frame after IsDismissed goes true
// so a key that dismisses it doesn't also leak through to whatever's now on top.
public interface IOverlay
{
    void HandleKey(ConsoleKeyInfo key);

    void Draw(FrameBuffer fb);

    bool IsDismissed { get; }
}
