using ThreeLn.Reconstruction4021.Core.Galaxy;
using ThreeLn.Reconstruction4021.Core.Types;

namespace ThreeLn.Reconstruction4021.Core.Entities;

/// <summary>
/// Common shape the annual-tick economy pipeline (AnnualTickHandler) needs from a per-tick world,
/// implemented by both <see cref="Planet"/> and <see cref="Starbase"/> — an industrial-complex
/// starbase runs almost the same UpdateWorld sequence a planet does (UPDATE.PAS:1392-1430). Most
/// members below are properties both already declared identically; the four that aren't plain
/// properties exist because Pascal's own per-ObjTyp accessors (PRIMINTR.PAS) treat a starbase
/// differently from a planet for exactly these four things, so each implementation encodes one
/// accessor's Base-case behavior instead of a shared field.
/// </summary>
public interface IEconomicWorld : ISectorObject, IShipCargoHolder
{
    /// <summary>
    /// Changes this world's owner (Rebellion in UPDATE.PAS reassigns to Indep; GalaxySetup's
    /// ApplySetup assigns the starting owner). A method rather than exposing a settable <c>Owner</c>
    /// through this interface: <see cref="ISectorObject.Owner"/> stays read-only everywhere except
    /// through this one choke point, so any future invariant ownership changes need to keep in sync
    /// (e.g. Empire-side bookkeeping) has one place to live instead of every call site that currently
    /// assigns <c>.Owner</c> directly.
    /// </summary>
    void Reassign(Empire newOwner);

    WorldType Type { get; set; }
    TechLevel TechLevel { get; set; }
    int Efficiency { get; set; }
    int RevolutionIndex { get; set; }
    bool IsAddictedToAmbrosia { get; set; }
    int Population { get; set; }
    IndustryLevels Industry { get; }
    DefenseCounts Defenses { get; }

    /// <summary>GetClass (PRIMINTR.PAS:414-420): a planet's own <see cref="Planet.Class"/>; ArtCls
    /// for every starbase — a starbase has no world-class field of its own in Pascal.</summary>
    WorldClass EffectiveClass { get; }

    /// <summary>
    /// GetISSP (PRIMINTR.PAS:500-529), restricted to the four raw-material industries it actually
    /// varies by (Chemical/Mining/Supply/TrillumMining — Bio/SYG/SYJ/SYS/SYT always return a fixed
    /// index there, already baked into AnnualTickHandler's gamma tables, never read through here): a
    /// planet's own dial; always index 0 (lowest self-sufficiency) for a starbase, which has no
    /// ImpExp field of its own — GetISSP's Base case is a hardcoded 0, not a per-starbase setting.
    /// </summary>
    int SelfSufficiencyIndex(IndustryType industry);

    /// <summary>TrillumReserves/PutTrillumReserves (PRIMINTR.PAS:482-498): a planet's own reserve;
    /// for a starbase, reads as MaxResources (effectively unlimited) and writes are silently
    /// discarded — a starbase has no TriReserve field in Pascal at all.</summary>
    int TrillumReserve { get; set; }

    /// <summary>InitializeISSP (PRIMINTR.PAS:627-633), called by Rebellion when a world goes
    /// independent: resets a planet's dials to DefaultISSP (index 5); a no-op for a starbase, which
    /// InitializeISSP's own Base-less CASE statement never touches.</summary>
    void InitializeSelfSufficiency();

    /// <summary>WorldID.ObjTyp=Pln, read at the one call site that actually branches on it:
    /// ReportPlanetLack's ship/cargo-shortfall call from Production (UPDATE.PAS:904, "IF
    /// ID.ObjTyp&lt;&gt;Base THEN") only bumps RevolutionIndex for a planet. UpdateIndustry's own
    /// ReportPlanetLack call (UPDATE.PAS:971) has no such guard — it fires for both.</summary>
    bool IsPlanet { get; }
}
