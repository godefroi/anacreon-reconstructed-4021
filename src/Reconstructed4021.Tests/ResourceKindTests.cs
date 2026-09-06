using Reconstructed4021.Core.Types;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="ResourceKind"/>'s own two ordinal boundaries: <see cref="ResourceKind.Ordinal"/>/
/// <see cref="ResourceKind.FromOrdinal"/> (the combined <c>ResourceTypes</c> ordinal a real <c>.SAV</c>
/// or a pre-<see cref="Core.Entities.NewsItem.Resource"/> JSON save packs into <c>Parm1-3</c>), and
/// <see cref="AttackTypeExtensions.ToResourceKind"/>/<see cref="AttackTypeExtensions.ToAttackType(ResourceKind)"/>
/// (the bridge <c>CombatOutcome.ReportLosses</c>/<c>NpeToolkit.AttackSeverity</c> use for combat news).
/// </summary>
public class ResourceKindTests
{
    [Test]
    [Arguments(1, typeof(ResourceKind.Defense), DefenseType.Lam)]
    [Arguments(4, typeof(ResourceKind.Defense), DefenseType.IonCannon)]
    [Arguments(5, typeof(ResourceKind.Ship), ShipType.Fighter)]
    [Arguments(11, typeof(ResourceKind.Ship), ShipType.Transport)]
    [Arguments(12, typeof(ResourceKind.Cargo), CargoType.Legion)]
    [Arguments(18, typeof(ResourceKind.Cargo), CargoType.Trillum)]
    public async Task FromOrdinal_AndBackViaOrdinal_RoundTrips(int ordinal, Type expectedCase, object expectedType)
    {
        var kind = ResourceKind.FromOrdinal(ordinal);

        await Assert.That(kind.GetType()).IsEqualTo(expectedCase);
        await Assert.That(kind switch {
            ResourceKind.Defense d => (object)d.Type,
            ResourceKind.Ship s => s.Type,
            ResourceKind.Cargo c => c.Type,
            _ => throw new InvalidOperationException(),
        }).IsEqualTo(expectedType);
        await Assert.That(kind.Ordinal).IsEqualTo(ordinal);
    }

    [Test]
    public async Task ToResourceKind_AndBackViaToAttackType_RoundTripsEveryAttackType()
    {
        foreach (var attackType in Enum.GetValues<AttackType>()) {
            await Assert.That(attackType.ToResourceKind().ToAttackType()).IsEqualTo(attackType);
        }
    }

    [Test]
    public async Task ToAttackType_NonTroopCargo_Throws()
    {
        var ambrosia = new ResourceKind.Cargo(CargoType.Ambrosia);
        await Assert.That(() => ambrosia.ToAttackType()).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0, CargoType.Legion)]
    [Arguments(4, CargoType.Metals)]
    [Arguments(6, CargoType.Trillum)]
    public async Task FromLegacyCargoOnlyOrdinal_BugEraBareOrdinal_DecodesAsCargoNotDefense(int bareOrdinal, CargoType expected)
    {
        // The exact real bug: a Metals shortfall (bare CargoType ordinal 4) must decode as Cargo.Metals,
        // not fall through FromOrdinal's own 1-4 Defense range and render as "ion cannons".
        await Assert.That(ResourceKind.FromLegacyCargoOnlyOrdinal(bareOrdinal)).IsEqualTo(new ResourceKind.Cargo(expected));
    }

    [Test]
    [Arguments(12, CargoType.Legion)]
    [Arguments(18, CargoType.Trillum)]
    public async Task FromLegacyCargoOnlyOrdinal_CorrectlyOffsetOrdinal_StillDecodesCorrectly(int offsetOrdinal, CargoType expected)
    {
        await Assert.That(ResourceKind.FromLegacyCargoOnlyOrdinal(offsetOrdinal)).IsEqualTo(new ResourceKind.Cargo(expected));
    }

    [Test]
    [Arguments(7)]
    [Arguments(11)]
    [Arguments(19)]
    public async Task FromLegacyCargoOnlyOrdinal_OutOfEitherRange_Throws(int value)
    {
        await Assert.That(() => ResourceKind.FromLegacyCargoOnlyOrdinal(value)).Throws<ArgumentOutOfRangeException>();
    }
}
