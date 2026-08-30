namespace Reconstructed4021.Tests.PascalGroundTruth;

/// <summary>
/// Named inputs for FleetMoveTests.MatchesGoldenFile — GetNewPos (FLEET.PAS:437-450) and
/// PassingThroughGate/PassingThroughFortress (relocated into reference/verify/patched/INTRFACE.PAS's
/// own trimmed copy), against one Empire1 fleet. GateKind/DestGateKind: 0=none,
/// 1=public gate (gte), 2=private link (lnk). Gate owners are raw Empire ordinals (1=Empire1,
/// 2=Empire2) matching runworld.pas's own Empire(parts[N]) cast.
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
        new(Name: "PlainStepNoObstacles", PosX: 0, PosY: 0, DestX: 5, DestY: 5),

        // Dense nebula sits exactly on the single Sgn-step candidate cell -- GetNewPos reverts to its
        // Limbo sentinel (0,0) instead of stepping, confirmed directly against source rather than
        // assumed from the C# port's own revert-to-old-position wrapper.
        new(Name: "DenseNebulaBlocksTheStep", PosX: 0, PosY: 0, DestX: 5, DestY: 5, NebulaX: 1, NebulaY: 1),

        // A public gate (gte) at the fleet's own position teleports regardless of the destination --
        // no matching gate needs to exist at DestX/DestY at all.
        new(Name: "PublicGateAlwaysPasses", PosX: 0, PosY: 0, DestX: 15, DestY: 15, GateKind: 1, GateOwner: 1),

        // A private link (lnk) only passes when the destination cell itself holds a same-owner
        // gate/link the fleet's own owner already knows about -- all three conditions met here.
        new(Name: "PrivateLinkToKnownMatchingLinkPasses", PosX: 0, PosY: 0, DestX: 12, DestY: 12,
            GateKind: 2, GateOwner: 1, DestGateKind: 2, DestGateOwner: 1, DestGateKnown: true),

        // Same matching/owned link at the destination, but the fleet's owner has never scouted it --
        // the Known(...) conjunct alone should block the pass.
        new(Name: "PrivateLinkToUnknownMatchingLinkDoesNotPass", PosX: 0, PosY: 0, DestX: 12, DestY: 12,
            GateKind: 2, GateOwner: 1, DestGateKind: 2, DestGateOwner: 1, DestGateKnown: false),

        // Destination link exists and is known, but owned by a different empire than the origin link
        // -- the GetStatus(GateObj)=GetStatus(DestObj) conjunct alone should block the pass.
        new(Name: "PrivateLinkOwnerMismatchDoesNotPass", PosX: 0, PosY: 0, DestX: 12, DestY: 12,
            GateKind: 2, GateOwner: 1, DestGateKind: 2, DestGateOwner: 2, DestGateKnown: true),

        // A fortress at the fleet's current position -- PassingThroughFortress's own GetBaseType=frt
        // check, independent of anything gate-related.
        new(Name: "FortressAtPositionDetected", PosX: 0, PosY: 0, DestX: 5, DestY: 5, FortressAtPos: true),
    ];

    /// <summary>MethodDataSource shape for FleetMoveTests.MatchesGoldenFile.</summary>
    public static IEnumerable<Func<FleetMoveCase>> AsDataSource() => All.Select(c => (Func<FleetMoveCase>)(() => c));
}
