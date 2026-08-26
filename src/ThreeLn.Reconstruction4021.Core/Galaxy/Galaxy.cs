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

    private readonly Dictionary<Coordinate, NebulaType> _nebulae = [];
    private readonly Dictionary<Coordinate, Empire> _minefields = [];
    private readonly Dictionary<Coordinate, HashSet<Empire>> _mineScoutedBy = [];

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

    /// <summary>
    /// Which empires know a minefield exists at this coordinate — a separate fact from
    /// <see cref="GetMineOwner"/> (who owns it), matching Pascal's own two independent per-sector
    /// fields (GALAXY.PAS:35's <c>MineScout: ScoutSet</c> bitmask vs. PutMine/EnemyMine's Special-byte
    /// nibble). SetMineScout/ClrMineScout (GALAXY.PAS:57-73) are ported as MarkMineScouted/
    /// ClearMineScouted below; no port-side reader exists yet beyond <see cref="IsMineScoutedBy"/>
    /// itself (real Pascal's only reader is MAPWIND.PAS's map-rendering code, Phase 8) — same
    /// "port the real write, leave it unread until its phase exists" precedent as Empire.DefeatedBy.
    /// </summary>
    public void MarkMineScouted(Empire empire, Coordinate coordinate)
    {
        if (!_mineScoutedBy.TryGetValue(coordinate, out var scouts)) {
            scouts = [];
            _mineScoutedBy[coordinate] = scouts;
        }

        scouts.Add(empire);
    }

    public void ClearMineScouted(Coordinate coordinate) => _mineScoutedBy.Remove(coordinate);

    public bool IsMineScoutedBy(Empire empire, Coordinate coordinate) =>
        _mineScoutedBy.TryGetValue(coordinate, out var scouts) && scouts.Contains(empire);

    /// <summary>
    /// GetObject's real occupancy model (PRIMINTR.PAS: <c>Sector[x]^[y].Obj</c>) — one non-fleet object
    /// slot per sector (a planet, starbase, stargate, or construction site; never more than one, since
    /// placement always checks this first), owner-blind. Fleets are tracked separately (Pascal's own
    /// per-sector <c>Flts: FleetSet</c>, this port's <see cref="Fleets"/> list) and never occupy this
    /// slot. Phase 6a's first real reader (stargate/fortress movement, starbase obstacle avoidance) —
    /// see that phase's notes in docs/ROADMAP.md for why a combined index wasn't needed until now.
    /// </summary>
    public ISectorObject? GetObjectAt(Coordinate location)
    {
        ISectorObject? found = Planets.FirstOrDefault(p => p.Location == location);
        found ??= Starbases.FirstOrDefault(s => s.Location == location);
        found ??= Stargates.FirstOrDefault(g => g.Location == location);
        found ??= ConstructionSites.FirstOrDefault(c => c.Location == location);
        return found;
    }
}
