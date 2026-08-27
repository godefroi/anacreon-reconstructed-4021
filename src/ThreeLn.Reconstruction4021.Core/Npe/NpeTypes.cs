using ThreeLn.Reconstruction4021.Core.Entities;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Npe;

/// <summary>
/// A fleet mission an NPE empire has assigned (NPETYPES.PAS's MissionTypes) — real Pascal ordinal
/// order preserved even though only a handful of these are read/written by anything ported so far
/// (Phase 6c's NpeToolkit); the rest are set up by NpeToolkit's Deploy*/Implement*MSN procedures,
/// which land later (Phase 6c-2/6d, once fleet-creation primitives exist — see NpeToolkit.cs's own
/// doc comment).
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
/// One NPE empire's personality knobs (NPETYPES.PAS's NPECharacterRecord) — a Kingdom1/Kingdom2
/// empire's persona, seeded once at creation (NPE02.PAS's InitializeKingdom1NPE/
/// InitializeKingdom2NPE, Phase 6d) and read by NpeToolkit's targeting/designation logic. Ported in
/// full even though Phase 6c's own toolkit methods only read <see cref="Defensive"/> and
/// <see cref="WorldPower"/> — the rest (diplomacy's <see cref="Offensive"/>, and the gene/evolution
/// fields NPE00.PAS's per-turn character drift reads/writes) are real fields the same record needs
/// once 6d/6e land, not speculative additions.
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

/// <summary>
/// One Kingdom fleet's AI mission state — the survivors of NPETYPES.PAS's FleetDataRecord field
/// audit (Phase 6c). Homed in a <c>Dictionary&lt;Fleet, KingdomFleetState&gt;</c> owned by
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
/// Not ported now — this phase builds Kingdom only; add when Pirate lands.</item>
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
    /// reference already in this port (see Combat/CombatOutcome.cs's own target-dispatch precedent).
    /// </summary>
    public object? Target { get; set; }

    /// <summary>HomeBaseID — always a world (every real call site sources it from GetRegionalCapital, which only ever returns a base/capital planet).</summary>
    public IEconomicWorld? HomeBase { get; set; }

    public int Waiting { get; set; }
}
