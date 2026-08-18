using ThreeLn.Reconstruction4021.Core.Entities;

namespace ThreeLn.Reconstruction4021.Core.Turns;

/// <summary>Refreshes one empire's fog-of-war immediately before it acts. Fog-of-war phase.</summary>
public interface IVisibilityHandler
{
    void RefreshVisibility(Empire empire, Game game);
}
