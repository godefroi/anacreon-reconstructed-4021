using System.Reflection;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.SaveFormat;
using Reconstructed4021.Core.Types;
using Reconstructed4021.LegacyNpe;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="LegacyNpeProvider.ReadSav"/>'s Kingdom-specific decoding — narrow, hand-built byte
/// buffers rather than a real reference `.SAV` file, since the one thing under test here (a zeroed
/// state-dept slot) isn't reachable from any committed fixture.
/// </summary>
public class LegacyNpeProviderTests
{
    private sealed class FakeResolver(Empire[] empiresByOrdinal) : ISavRefResolver
    {
        public Empire ResolveEmpire(int ordinal) => empiresByOrdinal[ordinal];
        public ISectorObject? ResolveObject(SavIdNumber id) => throw new InvalidOperationException("Not expected: every fleet slot in this test's buffer is zeroed.");
    }

    /// <summary><see cref="KingdomTurnHandler.State"/> is internal (the provider's own read-back seam) with no `InternalsVisibleTo` grant to this test project — same precedent as <c>SavGameLoaderTests.GetPirateInternal</c>.</summary>
    private static IReadOnlyDictionary<Empire, StateDeptRecord> GetState(KingdomTurnHandler handler) =>
        (IReadOnlyDictionary<Empire, StateDeptRecord>)typeof(KingdomTurnHandler).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handler)!;

    [Test]
    public async Task ReadSav_ZeroedStateDeptSlot_SkippedRatherThanMaterializedAsPolicyNone()
    {
        var writer = new SavWriter();
        writer.WriteZeros(30 * 11); // fleet-tracking slots, all unused (Index=0)

        // Slot 0 (Empire1): real diplomacy data.
        writer.WriteByte((byte)PolicyType.Neutral);
        writer.WriteByte(10); // AttackChance
        writer.WriteLongInt(100); // TotalMilitary
        writer.WriteWord(2); // Worlds
        writer.WriteByte(5); // ThreatAssess
        writer.WriteByte(7); // Aggressiveness
        writer.WriteInteger(3); // Balance

        writer.WriteZeros(12 * 8); // slots 1-8 (Empire2..8, Independent) -- never written to, all zero

        writer.WriteZeros(16); // persona

        var empires = Enumerable.Range(0, 9).Select(i => new Empire { Name = $"Slot{i}" }).ToArray();
        var provider = new LegacyNpeProvider();
        var handler = (KingdomTurnHandler)provider.ReadSav(new SavReader(writer.ToArray()), empires[0], NpeEmpireType.Kingdom1, new FakeResolver(empires), new Random(0));
        var state = GetState(handler);

        await Assert.That(state).ContainsKey(empires[0]);
        await Assert.That(state[empires[0]].Policy).IsEqualTo(PolicyType.Neutral);

        for (var i = 1; i < 9; i++) {
            await Assert.That(state).DoesNotContainKey(empires[i]);
        }
    }
}
