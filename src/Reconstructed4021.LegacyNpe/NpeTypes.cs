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

    /// <summary>
    /// Not real Pascal — no NPECharacterRecord field exists for this. How strongly
    /// <see cref="NpeToolkit.GetBestTarget"/> discounts a candidate's value by its distance from the
    /// attacker's nearest regional capital: 0 reproduces the original distance-blind scoring exactly
    /// (real Pascal has no notion of "nearby" when picking an attack target, only when picking which
    /// of the attacker's own bases launches from), higher values increasingly favor a nearby, merely
    /// decent target over a farther, more valuable one. Defaults to 0 everywhere except the genetic
    /// algorithm harness, which evolves it.
    /// </summary>
    public int ProximityGene { get; set; }

    /// <summary>
    /// Not real Pascal — no NPECharacterRecord field exists for this. How much weight
    /// <see cref="NpeToolkit.EnemyPriority"/> gives a specific enemy's recent military-strength
    /// decline (<see cref="StateDeptRecord.Trend"/>) when ranking who's worth focusing on: 0 means the
    /// ranking ignores trend entirely (today's behavior — StateDepartment/WarCabinet only ever look at
    /// an enemy's current strength, never its trajectory). Defaults to 0 everywhere except the genetic
    /// algorithm harness, which evolves it.
    /// </summary>
    public int TrendWeightGene { get; set; }

    /// <summary>
    /// Not real Pascal — no NPECharacterRecord field exists for this. How much weight
    /// <see cref="NpeToolkit.EnemyPriority"/> gives an enemy empire's overall distance (this empire's
    /// capital to that empire's capital — the "center of gravity," distinct from
    /// <see cref="ProximityGene"/>'s own per-world distance once a target is already chosen): 0 means
    /// the ranking ignores it entirely. Defaults to 0 everywhere except the genetic algorithm harness,
    /// which evolves it.
    /// </summary>
    public int CenterOfGravityGene { get; set; }

    /// <summary>
    /// Not real Pascal — no NPECharacterRecord field exists for this. How strongly
    /// <see cref="NpeToolkit.WarCabinet"/> concentrates aggression on the single highest-<see
    /// cref="NpeToolkit.EnemyPriority"/> active enemy instead of treating every enemy independently
    /// (today's behavior, and what 0 reproduces exactly): raises the top-priority enemy's effective
    /// attack chance and lowers everyone else's, by up to half of this gene's own value. Defaults to 0
    /// everywhere except the genetic algorithm harness, which evolves it.
    /// </summary>
    public int FocusGene { get; set; }

    /// <summary>
    /// New, no Pascal precedent (0-100, arbitrary): reserves this fraction of a JumpAttack fleet's
    /// power budget for Penetrator/Starship before falling back to the mission's own default
    /// cheap-first sequence for the rest (<see cref="NpeToolkit.GetFleetComposition"/>) — JumpAttack
    /// is the AI's dominant offensive mission and its default sequence can never include either ship
    /// type today. Defaults to 0 everywhere except the genetic algorithm harness, which evolves it.
    /// </summary>
    public int CompositionGene { get; set; }

    /// <summary>
    /// New, no Pascal precedent (0-100, interpreted directly as a Chebyshev-sector distance cap):
    /// gates <see cref="CompositionGene"/>'s heavy wave to targets within this many sectors of the
    /// launching base, since a dedicated Starship/Penetrator fleet is still absolutely slow
    /// (<see cref="NpeToolkit.GetHeavyFleetComposition"/>'s own doc comment) even once it's no
    /// longer merged into the fast escort — the same fixed 1-sector-per-year tax costs far more
    /// real time against a distant target than a nearby one. 0 means no gating at all (today's
    /// behavior post-split, and what every real call site reproduces exactly). Defaults to 0
    /// everywhere except the genetic algorithm harness, which evolves it.
    /// </summary>
    public int HeavyRangeGene { get; set; }

    /// <summary>
    /// New, no Pascal precedent (signed, -100 to 100): shifts <see cref="NpeToolkit.WarCabinet"/>'s
    /// fixed JumpAttack-vs-SlowAttack split at the Conflict/War policy tiers (75/25 and 50/50 in real
    /// Pascal, identical for every persona today) toward JumpAttack (frequent, small fleets) at
    /// positive values or toward SlowAttack (rare, large fleets) at negative values, by up to half of
    /// this gene's magnitude. 0 reproduces the exact original fixed splits at both tiers. Defaults to
    /// 0 everywhere except the genetic algorithm harness, which evolves it.
    /// </summary>
    public int AttackSizeGene { get; set; }

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

    /// <summary>
    /// Not real Pascal. An exponential moving average of this enemy's fractional TotalMilitary change
    /// each time <see cref="NpeToolkit.StateDeptReport"/> runs (roughly every 7 turns, see
    /// <see cref="Turns.KingdomTurnHandler.PlayTurn"/>) — positive means declining, negative means
    /// growing. Smoothed rather than a raw single-report delta so one noisy report doesn't read as a
    /// trend; only ever read by <see cref="NpeToolkit.EnemyPriority"/>, weighted by
    /// <see cref="NpeCharacter.TrendWeightGene"/>.
    /// </summary>
    public double Trend { get; set; }

    /// <summary>
    /// Not real Pascal. Chebyshev distance between this enemy's capital and the reporting empire's own
    /// capital, refreshed by <see cref="NpeToolkit.StateDeptReport"/> whenever both are known/alive —
    /// stale (keeps its last real value) otherwise, since a missing capital isn't "far away," it's
    /// "unknown," and overwriting with a sentinel would misrank it. Only ever read by
    /// <see cref="NpeToolkit.EnemyPriority"/>, weighted by <see cref="NpeCharacter.CenterOfGravityGene"/>.
    /// </summary>
    public int Distance { get; set; }
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

/// <summary>
/// One Pirate fleet's AI mission state (NPETYPES.PAS's FleetDataRecord, Pirate's own field usage —
/// audited directly against NPE01.PAS, not carried over from <see cref="KingdomFleetState"/>'s own
/// audit): <c>HomeBaseID</c> is dead for Pirate too (grepped: no read or write site anywhere in
/// NPE01.PAS — a returning fleet re-derives its base fresh via FindNearestBase every time, it never
/// remembers one), unlike Kingdom where the same field is real. <c>BlockX</c>/<c>BlockY</c> are real
/// here (they're Pirate-only to begin with — see <see cref="KingdomFleetState"/>'s own doc comment):
/// the hunting-ground grid cell this fleet's patrol/chase pressure is charged against.
/// </summary>
public sealed class PirateFleetState
{
    public NpeMissionType Mission { get; set; }

    /// <summary>TargetID — a <see cref="Fleet"/> (the transport being chased/attacked, WaitForTransports/AttackTransports) or an <see cref="IEconomicWorld"/> (the raid target, AttackWorld).</summary>
    public object? Target { get; set; }

    public int Waiting { get; set; }
    public int BlockX { get; set; }
    public int BlockY { get; set; }
}

/// <summary>
/// A Berserker starbase's own mission (NPETYPES.PAS's BaseMissionTypes) — real Pascal ordinal
/// order preserved.
/// </summary>
public enum BaseMissionType
{
    None,
    Defend,
    Attack,
    FindHome,
    Refuel,
    WaitForAttack,
    WanderAround,
}

/// <summary>
/// One Berserker fleet's AI mission state (NPETYPES.PAS's FleetDataRecord, Berserker's own field
/// usage — audited directly against NPE04.PAS, not carried over from <see cref="KingdomFleetState"/>'s
/// own audit, matching the precedent <see cref="PirateFleetState"/> already set): <c>Waiting</c>/
/// <c>BlockX</c>/<c>BlockY</c> are dead here (grepped NPE04.PAS — none referenced). <c>Target</c> and
/// <c>HomeBase</c> are narrower than Kingdom/Pirate's <c>object?</c>: every real read/write site in
/// NPE04.PAS assigns a world (never a bare <see cref="Fleet"/>) — an enemy planet being attacked, the
/// launching <see cref="Entities.Starbase"/> a fleet returns to, or the home planet an escort fleet
/// departs from.
/// </summary>
public sealed class BerserkerFleetState
{
    public NpeMissionType Mission { get; set; }

    /// <summary>TargetID — the enemy world being attacked (BerserkerAttack), or the starbase this fleet is returning to merge into (BerserkerReturn).</summary>
    public IEconomicWorld? Target { get; set; }

    /// <summary>HomeBaseID — the starbase that dispatched this fleet (BerserkerAttack), or the home planet an escort fleet departed from (a refuel run's own BerserkerReturn fleet). Unread for a refuel fleet's own mission handling, but set the same way every DeployFleet call sets it.</summary>
    public IEconomicWorld? HomeBase { get; set; }
}

/// <summary>
/// One Berserker starbase's own AI mission state (NPETYPES.PAS's BaseDataRecord). No pruning
/// function exists for this dictionary — real Pascal never enforces/prunes BaseData either;
/// <see cref="BerserkerTurnHandler"/> drives its per-base loop off live starbases each turn and
/// lazily creates an entry for any owned command-base/fortress that doesn't have one yet, so a
/// destroyed starbase's stale entry is simply never visited again.
/// </summary>
public sealed class BerserkerBaseState
{
    public BaseMissionType Mission { get; set; }

    /// <summary>TargetID — always a planet: the enemy world being watched/attacked (Attack/WaitForAttack), or the home world being returned to for resupply (FindHome/Refuel).</summary>
    public IEconomicWorld? Target { get; set; }

    public int Count { get; set; }
}
