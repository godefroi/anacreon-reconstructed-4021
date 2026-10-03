namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs for FleetMoveTests.MatchesGoldenFile — GetNewPos (FLEET.PAS:437-450) and
/// PassingThroughGate/PassingThroughFortress (relocated into reference/verify/patched/INTRFACE.PAS's
/// own trimmed copy), against one Empire1 fleet. GateKind/DestGateKind: 0=none,
/// 1=public gate (gte), 2=private link (lnk). Gate owners are 1=Empire1, 2=Empire2; the formatter
/// sends the 0-based Empire ordinal runworld.pas casts with Empire(parts[N]).
///
/// Cases start away from (0,0), which is GetNewPos's Limbo result for a step into dense nebula, so a
/// blocked step reads differently from a fleet that stayed put. Coordinates are passed through raw
/// to both sides and stay inside 1..19, valid in both Pascal's 1-based and C#'s 0-based galaxy.
/// </summary>
public sealed record FleetMoveCase(
    string Name, int PosX, int PosY, int DestX, int DestY,
    int NebulaX = 0, int NebulaY = 0,
    int GateKind = 0, int GateOwner = 1,
    int DestGateKind = 0, int DestGateOwner = 1, bool DestGateKnown = false,
    bool FortressAtPos = false) : INamedCase;

internal static class FleetMoveCases
{
    public static readonly IReadOnlyList<FleetMoveCase> All = [
        // No obstacles at all -- confirms the plain Sgn-toward-destination step (GetNewPos's own
        // NewPos.x+Sgn(...) arithmetic), no gate/fortress branch taken.
        new(Name: "PlainStepNoObstacles", PosX: 4, PosY: 4, DestX: 9, DestY: 9),

        // Negative steps on both axes, and on one axis with the other already aligned.
        new(Name: "StepTowardSmallerCoordinates", PosX: 10, PosY: 10, DestX: 3, DestY: 6),
        new(Name: "StepAlongOneAxisOnly", PosX: 10, PosY: 10, DestX: 10, DestY: 2),

        // Dense nebula sits exactly on the single Sgn-step candidate cell -- GetNewPos reverts to its
        // Limbo sentinel (0,0) instead of stepping, confirmed directly against source rather than
        // assumed from the C# port's own revert-to-old-position wrapper.
        new(Name: "DenseNebulaBlocksTheStep", PosX: 8, PosY: 8, DestX: 3, DestY: 3, NebulaX: 7, NebulaY: 7),

        // A public gate (gte) at the fleet's own position teleports regardless of the destination --
        // no matching gate needs to exist at DestX/DestY at all, and it needn't be the fleet's own.
        new(Name: "PublicGateAlwaysPasses", PosX: 3, PosY: 3, DestX: 15, DestY: 15, GateKind: 1, GateOwner: 2),

        // A private link (lnk) only passes when the destination cell itself holds a same-owner
        // gate/link the fleet's own owner already knows about -- all three conditions met here.
        new(Name: "PrivateLinkToKnownMatchingLinkPasses", PosX: 3, PosY: 3, DestX: 12, DestY: 12,
            GateKind: 2, GateOwner: 1, DestGateKind: 2, DestGateOwner: 1, DestGateKnown: true),

        // Same matching/owned link at the destination, but the fleet's owner has never scouted it --
        // the Known(...) conjunct alone should block the pass.
        new(Name: "PrivateLinkToUnknownMatchingLinkDoesNotPass", PosX: 3, PosY: 3, DestX: 12, DestY: 12,
            GateKind: 2, GateOwner: 1, DestGateKind: 2, DestGateOwner: 1, DestGateKnown: false),

        // Destination link exists and is known, but owned by a different empire than the origin link
        // -- the GetStatus(GateObj)=GetStatus(DestObj) conjunct alone should block the pass.
        new(Name: "PrivateLinkOwnerMismatchDoesNotPass", PosX: 3, PosY: 3, DestX: 12, DestY: 12,
            GateKind: 2, GateOwner: 1, DestGateKind: 2, DestGateOwner: 2, DestGateKnown: true),

        // Both links belong to Empire2, not to the fleet's Empire1, and the destination is known.
        new(Name: "PrivateLinkOwnedByOtherEmpire", PosX: 12, PosY: 12, DestX: 3, DestY: 3,
            GateKind: 2, GateOwner: 2, DestGateKind: 2, DestGateOwner: 2, DestGateKnown: true),

        // A fortress at the fleet's current position -- PassingThroughFortress's own GetBaseType=frt
        // check, independent of anything gate-related.
        new(Name: "FortressAtPositionDetected", PosX: 6, PosY: 6, DestX: 2, DestY: 9, FortressAtPos: true),
    ];

    /// <summary>MethodDataSource shape for FleetMoveTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<FleetMoveCase>> AsDataSource() => All.Select(c => (Func<FleetMoveCase>)(() => c));
}
