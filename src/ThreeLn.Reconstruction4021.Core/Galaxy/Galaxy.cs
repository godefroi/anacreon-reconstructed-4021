using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Galaxy;

/// <summary>
/// Replaces Pascal's UniverseRecord/Universe. Entities live in plain growable lists — "is it live"
/// is just "is it in the list", with no fixed-capacity arrays or SetOfActiveXxx bitmasks to keep in
/// sync. Sector-occupancy indexing (what's at a given coordinate) is deferred to the movement phase.
///
/// Nebulae and minefields are sparse — on a full-size (100x100) galaxy the overwhelming majority of
/// sectors have neither, and Pascal itself queries them independently (GetNebula/PutNebula vs
/// PutMine/EnemyMine are separate accessors in PRIMINTR.PAS, packed into one byte's two nibbles only
/// for on-disk size, not because they're one concept). Rather than a dense Size*Size grid allocated
/// up front, each is a dictionary holding only the coordinates that actually have one — construction
/// is O(1) instead of O(Size^2), and an empty galaxy costs nothing beyond the two empty dictionaries.
/// </summary>
public sealed class Galaxy(int size)
{
    public int Size { get; } = size;

    private readonly Dictionary<Coordinate, NebulaType> _nebulae = new();
    private readonly Dictionary<Coordinate, Empire> _minefields = new();

    public List<Planet> Planets { get; } = [];
    public List<Starbase> Starbases { get; } = [];
    public List<Fleet> Fleets { get; } = [];
    public List<Stargate> Stargates { get; } = [];
    public List<ConstructionSite> ConstructionSites { get; } = [];

    public NebulaType GetNebula(Coordinate coordinate) => _nebulae.GetValueOrDefault(coordinate, NebulaType.None);

    public void SetNebula(Coordinate coordinate, NebulaType nebula)
    {
        if (nebula == NebulaType.None) {
            _nebulae.Remove(coordinate);
        } else {
            _nebulae[coordinate] = nebula;
        }
    }

    /// <summary>The empire whose minefield occupies this sector, or null if it isn't mined.</summary>
    public Empire? GetMineOwner(Coordinate coordinate) => _minefields.GetValueOrDefault(coordinate);

    public void SetMine(Coordinate coordinate, Empire owner) => _minefields[coordinate] = owner;

    public void ClearMine(Coordinate coordinate) => _minefields.Remove(coordinate);
}
