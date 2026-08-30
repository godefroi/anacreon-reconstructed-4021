namespace Reconstructed4021.Core.Turns;

/// <summary>Runs once per full round through all empires (Pascal's UpdateUniverse). Economy phase.</summary>
public interface IAnnualTickHandler
{
    void RunAnnualTick(Game game);
}
