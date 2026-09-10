using Reconstructed4021.Core.Entities;

namespace Reconstructed4021.Core.SaveFormat;

/// <summary>The narrow slice of <c>SavGameWriter</c>'s own per-object id bookkeeping (its private <c>ObjectIdIndex</c>) an <see cref="Turns.INpeHandlerProvider"/> needs to write a mission's own object references.</summary>
public interface ISavObjectIds
{
    SavIdNumber IdOf(ISectorObject sectorObject);
}

/// <summary>The narrow slice of <c>SavGameWriter</c>'s own empire-slot bookkeeping (its private <c>EmpireSlotIndex</c>) an <see cref="Turns.INpeHandlerProvider"/> needs for a personality's fixed per-empire state table.</summary>
public interface ISavEmpireSlots
{
    /// <summary>Ordinal 0-7 (Empire1..Empire8) — any empire (real or orphan) assigned that slot, or null if none ever was. Independent (ordinal 8) isn't a slot; callers handle it themselves.</summary>
    Empire? AnyEmpireAt(int ordinal);
}

/// <summary>Read-side counterpart <c>SavGameLoader</c> itself implements — not a 1:1 mirror of the two write-side interfaces above, since the loader resolves both kinds of reference through one object.</summary>
public interface ISavRefResolver
{
    /// <summary>Ordinal 0-7 = Empire1..Empire8, 8 = Independent.</summary>
    Empire ResolveEmpire(int ordinal);

    ISectorObject? ResolveObject(SavIdNumber id);
}
