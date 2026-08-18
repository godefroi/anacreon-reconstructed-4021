namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Resources currently loaded/stockpiled. Real, stored state — how much space is available for
/// more is a separate, computed concern (see the fleet/economy phases), not a field here.
/// </summary>
public sealed class CargoHold
{
    public int Legions { get; set; }
    public int NinjaLegions { get; set; }
    public int Ambrosia { get; set; }
    public int Chemicals { get; set; }
    public int Metals { get; set; }
    public int Supplies { get; set; }
    public int Trillum { get; set; }
}
