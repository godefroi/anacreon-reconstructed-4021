using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Which specific ships/defenses/constructions/resource-types an empire has unlocked. Pascal tracks
/// this as one set over its master enum (TechnologySet); split per-category here for the same reason
/// the category enums themselves are split (see the architecture plan).
///
/// <see cref="Resources"/> reuses <see cref="CargoType"/> rather than a dedicated enum — Pascal's
/// TechnologySet resource span (men,nnj,amb,che,met,sup,tri) is exactly CargoType's 7 members in the
/// same order. It exists because NewTechLevel's outer guard (UPDATE.PAS:386, "TechSet&lt;&gt;TechDev[Tech]")
/// is a real set-equality check across every category including resources — production's own
/// CargoTechAvailable (AnnualTickHandler.Production.cs) still gates purely on TechLevel via a
/// derived min-tech-level table, not this set; the two are deliberately not wired together yet (see
/// docs/ROADMAP.md's Commit 5 design decisions).
/// </summary>
public sealed class UnlockedTechnology
{
    public HashSet<ShipType> Ships { get; } = [];
    public HashSet<DefenseType> Defenses { get; } = [];
    public HashSet<ConstructionType> Constructions { get; } = [];
    public HashSet<CargoType> Resources { get; } = [];

    /// <summary>
    /// Overwrites this set's contents with <paramref name="other"/>'s (ConquerEmpire's NewCapital,
    /// ATTACK.PAS:1003-1019's <c>SetEmpireTechnology(Emp,NewTech,TechDev[...])</c> — a full replace, not
    /// a union, unlike NewTechLevel's own incremental grants).
    /// </summary>
    public void ReplaceWith(UnlockedTechnology other)
    {
        Ships.Clear(); Ships.UnionWith(other.Ships);
        Defenses.Clear(); Defenses.UnionWith(other.Defenses);
        Constructions.Clear(); Constructions.UnionWith(other.Constructions);
        Resources.Clear(); Resources.UnionWith(other.Resources);
    }
}
