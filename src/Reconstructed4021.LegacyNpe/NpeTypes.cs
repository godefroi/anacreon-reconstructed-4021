using Reconstructed4021.Core;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Turns;
using Reconstructed4021.Core.Types;

namespace Reconstructed4021.LegacyNpe;

/// <summary>
/// A fleet mission an NPE empire has assigned (NPETYPES.PAS's MissionTypes): real Pascal ordinal
/// order preserved even though <see cref="NpeToolkit"/>'s Kingdom-only procedures read/write only a
/// subset of these. The Berserker/Pirate-specific values (<see cref="HunterKiller"/>/
/// <see cref="WaitForTransports"/>/<see cref="AttackTransports"/>/<see cref="AttackWorld"/>/
/// <see cref="BerserkerAttack"/>/<see cref="BerserkerReturn"/>) have no Kingdom caller.
/// </summary>
public enum NpeMissionType
{
    None,
    Return,
    HunterKiller,
    WaitForTransports,
    AttackTransports,
    AttackWorld,
    BerserkerAttack,
    BerserkerReturn,
    Stack,
    Guard,
    Conquer,
    JumpAttack,
    Refuel,
    SlowAttack,
    RaidTransports,
    Supply,
    SupplyTransports,
}

/// <summary>
/// One NPE empire's personality knobs (NPETYPES.PAS's NPECharacterRecord): a Kingdom1/Kingdom2
/// empire's persona, seeded once at creation (NPE02.PAS's InitializeKingdom1NPE/
/// InitializeKingdom2NPE) and read by <see cref="NpeToolkit"/>'s targeting/designation logic,
/// diplomacy, and NPE00.PAS's per-turn character drift.
/// </summary>
public sealed class NpeCharacter
{
    public int ImperialistGene { get; set; }
    public int DefensiveGene { get; set; }
    public int OffensiveGene { get; set; }
    public int FactorGene { get; set; }
    public int RandomGene { get; set; }

    public int Defensive { get; set; }
    public int Offensive { get; set; }
    public int Techno { get; set; }
    public int Provoke { get; set; }
    public int Imperialist { get; set; }
    public int WorldPower { get; set; }
    public int Honorable { get; set; }
    public int SphereX { get; set; }

    public int Clock { get; set; }
    public int Offset { get; set; }
}

/// <summary>Kingdom's per-enemy diplomatic posture (NPETYPES.PAS's PolicyTypes) — real Pascal ordinal order preserved. <see cref="None"/> stands in for NoPLT (never assigned by anything ported so far — every real State slot starts at Neutral or Harass, per InitializeKingdom1NPE/2NPE).</summary>
public enum PolicyType
{
    None,
    Neutral,
    Defend,
    Harass,
    Preempt,
    Conflict,
    War,
}

/// <summary>
/// One empire's standing in this Kingdom empire's foreign office (NPETYPES.PAS's StateDeptRecord) —
/// indexed by the *other* empire, one record per enemy (including <see cref="Empire.Independent"/>;
/// see <see cref="Turns.KingdomTurnHandler"/>'s own doc comment on why that slot needs an entry even
/// though real Pascal's own seeding loop, <c>FOR EmpI:=Empire1 TO Empire8</c>, never touches it).
/// <see cref="TotalMilitary"/>/<see cref="Worlds"/>/<see cref="ThreatAssess"/> are written only by
/// <see cref="NpeToolkit.StateDeptReport"/> — real fields on the same record, kept here rather than
/// split out, since a split would just have to re-add them.
/// </summary>
public sealed class StateDeptRecord
{
    public PolicyType Policy { get; set; }
    public int AttackChance { get; set; }
    public long TotalMilitary { get; set; }
    public int Worlds { get; set; }
    public int ThreatAssess { get; set; }
    public int Aggressiveness { get; set; }
    public int Balance { get; set; }
}

/// <summary>
/// One Kingdom fleet's AI mission state — the survivors of NPETYPES.PAS's FleetDataRecord field
/// audit. Homed in a <c>Dictionary&lt;Fleet, KingdomFleetState&gt;</c> owned by
/// <see cref="Turns.KingdomTurnHandler"/> (one dictionary per Kingdom empire), not on <see cref="Fleet"/>
/// itself — this is AI bookkeeping only Kingdom-type fleets have, matching the precedent set by
/// keeping Galaxy's own mine-scouting state off Coordinate/Planet (docs/PORT_DESIGN.md).
///
/// Audited against every real read/write site in NPEINTR.PAS (not guessed at plan time — an earlier
/// PORT_DESIGN.md pass guessed both <c>Waiting</c> and <c>Midway</c> were derivable from state this
/// port already tracks; reading the source showed that guess was half right):
/// <list type="bullet">
/// <item><c>Mission</c>/<c>TargetID</c>/<c>HomeBaseID</c> — real, kept as <see cref="Mission"/>/
/// <see cref="Target"/>/<see cref="HomeBase"/>.</item>
/// <item><c>Waiting</c> — real, kept as <see cref="Waiting"/>. Not derivable from
/// <see cref="Fleet.Status"/>: it's a 0-5 dwell counter <c>ImplementRaidTrnMSN</c> increments each
/// turn a raiding fleet stays in a hostile sector before forcing a return
/// (NPEINTR.PAS:1267-1273), not a movement state.</item>
/// <item><c>Midway</c> — confirmed dead, not ported. Grepped across the entire 1.31 source tree:
/// declared at NPETYPES.PAS:86 and never read or written anywhere, including every NPE0x.PAS file —
/// same category as <c>TraderNPE</c> (see docs/PASCAL_ARCHITECTURE_NOTES.md).</item>
/// <item><c>BlockX</c>/<c>BlockY</c> — Pirate-only (grepped: all six hits are in NPE01.PAS, the
/// Pirate personality's own hunting-ground grid; NPEINTR.PAS's shared toolkit never touches them).
/// Not ported: this port models Kingdom only; add when Pirate is implemented.</item>
/// <item><c>Index</c> — dissolves into the owning Dictionary's key.</item>
/// </list>
/// </summary>
public sealed class KingdomFleetState
{
    public NpeMissionType Mission { get; set; }

    /// <summary>
    /// TargetID — the fleet's final destination or attack target. Pascal's IDNumber union: an
    /// <see cref="IEconomicWorld"/> (planet/starbase), a <see cref="ConstructionSite"/>, a
    /// <see cref="Stargate"/>, or a <see cref="Fleet"/>, matching every other IDNumber-typed
    /// reference already in this port (see <see cref="Combat.CombatOutcome"/>'s own target-dispatch
    /// precedent).
    /// </summary>
    public object? Target { get; set; }

    /// <summary>HomeBaseID — always a world (every real call site sources it from GetRegionalCapital, which only ever returns a base/capital planet).</summary>
    public IEconomicWorld? HomeBase { get; set; }

    public int Waiting { get; set; }
}
