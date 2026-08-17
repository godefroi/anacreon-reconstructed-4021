using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Which specific ships/defenses/constructions an empire has unlocked. Pascal tracks this as one
/// set over its master enum (TechnologySet); split per-category here for the same reason the
/// category enums themselves are split (see the architecture plan) — a cargo type is produced, not
/// "unlocked", so CargoType has no membership here.
/// </summary>
public sealed class UnlockedTechnology
{
    public HashSet<ShipType> Ships { get; } = [];
    public HashSet<DefenseType> Defenses { get; } = [];
    public HashSet<ConstructionType> Constructions { get; } = [];
}
