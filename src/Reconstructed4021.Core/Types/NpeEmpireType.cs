namespace Reconstructed4021.Core.Types;

/// <summary>
/// Which computer-controlled AI personality an NPE empire runs (NPETYPES.PAS:21-27's
/// NPEmpireTypes) — dispatch key only, not a description of state shape (each personality has its
/// own private data record in real Pascal; this port's equivalent is each personality's own
/// ITurnHandler implementation, see PORT_DESIGN.md). Declared in Pascal's own ordinal order so
/// ScenarioLoader can cast NextInteger's raw ordinal directly, same convention as WorldType.
/// </summary>
public enum NpeEmpireType
{
    /// <summary>
    /// NPETYPES.PAS's NoNPE (ordinal 0) — kept only so the direct ordinal cast lines up; no real
    /// .SCN scenario ever writes this ordinal to CreateNPEmpire (all 12 dos_131 files use 1-4), and
    /// Empire.NpeType is nullable specifically so "not an NPE" is null, not this value.
    /// </summary>
    None,
    Pirate,
    Kingdom1,
    Kingdom2,
    Berserker,
    Guardian,
    Trader,
}
