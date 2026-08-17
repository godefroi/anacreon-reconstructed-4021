namespace ThreeLn.Reconstruction4021.Core.Types;

/// <summary>
/// Industrial/economic designation of a planet or starbase. Names and order verified against
/// DATACNST.PAS's TypeName display table, not guessed from the manual alone (which only documents
/// 15 of these 21 as player-selectable designations).
/// </summary>
public enum WorldType
{
    Agricultural,
    Ambrosia,
    Base,
    BaseStarbase,
    Capital,
    Chemical,
    Independent,
    JumpshipBase,
    JumpshipBaseStarbase,
    Mine,
    NinjaWorld,
    Outpost,
    RawMaterialMine,
    RawMaterialMineStarbase,
    StarshipBase,
    StarshipBaseStarbase,
    TransportBase,
    TransportBaseStarbase,
    University,
    Terraform,
    TrillumMine,
}
