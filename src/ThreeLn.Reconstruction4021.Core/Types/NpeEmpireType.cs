namespace ThreeLn.Reconstruction4021.Core.Types;

/// <summary>
/// Which computer-controlled AI personality an NPE empire runs (NPETYPES.PAS:21-27's
/// NPEmpireTypes) — dispatch key only, not a description of state shape (each personality has its
/// own private data record in real Pascal; this port's equivalent is each personality's own
/// ITurnHandler implementation, see PORT_DESIGN.md). Declared in Pascal's own ordinal order so
/// ScenarioLoader can cast NextInteger's raw ordinal directly, same convention as WorldType.
/// </summary>
public enum NpeEmpireType
{
    None,
    Pirate,
    Kingdom1,
    Kingdom2,
    Berserker,
    Guardian,
    Trader,
}
