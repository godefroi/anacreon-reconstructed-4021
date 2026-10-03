using Reconstructed4021.Core.Combat;
using Reconstructed4021.Core.Entities;
using Reconstructed4021.Core.Types;
using Reconstructed4021.Tests.PascalGroundTruth;

namespace Reconstructed4021.Tests;

/// <summary>
/// <see cref="FleetGroupConfiguration"/> against reference/verify/golden/getgroups.golden: the real
/// ATTCOMM.PAS GetGroups, driven headless by a keystroke script (runattcomm.pas). This test replays
/// the same script through the port's primitives, doing what GetGroups' main loop does with each
/// key, then compares the groups the real procedure returned.
/// </summary>
public class FleetGroupConfigurationGroundTruthTests
{
    private static readonly AttackType[] TypeCycle = [
        AttackType.Fighter, AttackType.HunterKiller, AttackType.Jumpship, AttackType.Jumptransport,
        AttackType.Penetrator, AttackType.Starship, AttackType.Transport,
    ];

    [Test]
    [DependsOn<GoldenFileTests>(nameof(GoldenFileTests.RegenerateAllGoldenFiles), ProceedOnFailure = true)]
    [MethodDataSource(typeof(GetGroupsCases), nameof(GetGroupsCases.AsDataSource))]
    public async Task MatchesGoldenFile(GetGroupsCase c)
    {
        var expected = GoldenFile.Load("getgroups.golden")[c.Name];

        var groups = Replay(c);

        await Assert.That(groups.Count).IsEqualTo(int.Parse(expected["ngroups"]));
        for (var i = 0; i < groups.Count; i++) {
            var g = groups[i];
            // Pascal's AttackTypes starts with a NoRes sentinel the port's enum leaves out, so its
            // ordinals are one higher.
            await Assert.That((int)g.Typ + 1).IsEqualTo(int.Parse(expected[$"g{i + 1}_typ"]));
            await Assert.That(g.Num).IsEqualTo(int.Parse(expected[$"g{i + 1}_num"]));
            await Assert.That(g.Gat).IsEqualTo(int.Parse(expected[$"g{i + 1}_gat"]));
            // Pascal zeroes GAT without clearing GATTyp (ATTCOMM.PAS:902-906), so an empty group keeps a
            // stale type; the port nulls it. Nothing reads GATTyp while GAT is 0, so only compare it
            // for a loaded group.
            if (g.Gat > 0) {
                await Assert.That((int)g.GatTyp!.Value + 1).IsEqualTo(int.Parse(expected[$"g{i + 1}_gattyp"]));
            }
        }
    }

    /// <summary>GetGroups' main loop (ATTCOMM.PAS:1002-1033) and the "Add how many" prompt (:921-949).</summary>
    private static List<GroupRecord> Replay(GetGroupsCase c)
    {
        var shipPool = new ShipCounts {
            Fighters = c.Fgt, HunterKillers = c.Hkr, Jumpships = c.Jmp, Jumptransports = c.Jtn,
            Penetrators = c.Pen, Starships = c.Ssp, Transports = c.Trn,
        };
        var cargoPool = new CargoHold { Legions = c.Men, NinjaLegions = c.Nnj };
        var groups = Enumerable.Range(0, FleetGroupConfiguration.MaxGroups).Select(_ => new GroupRecord { Typ = AttackType.Fighter }).ToList();
        var selected = 0;
        var currentType = AttackType.Fighter;

        // Space and a committed amount both move the selection on after loading (ATTCOMM.PAS:911-915).
        void Load(int amount)
        {
            FleetGroupConfiguration.ChangeGroupType(shipPool, cargoPool, groups[selected], currentType);
            FleetGroupConfiguration.LoadShips(shipPool, cargoPool, groups[selected], amount);
            if (selected < groups.Count - 1) {
                selected++;
            }
        }

        for (var k = 0; k < c.Keys.Length; k++) {
            switch (char.ToUpperInvariant(c.Keys[k])) {
                case GetGroupsKeys.Up:
                    if (selected > 0) {
                        currentType = groups[--selected].Typ;
                    }
                    break;
                case GetGroupsKeys.Down:
                    if (selected < groups.Count - 1) {
                        currentType = groups[++selected].Typ;
                    }
                    break;
                case GetGroupsKeys.Right:
                    currentType = TypeCycle[(Array.IndexOf(TypeCycle, currentType) + 1) % TypeCycle.Length];
                    break;
                case GetGroupsKeys.Left:
                    currentType = TypeCycle[(Array.IndexOf(TypeCycle, currentType) + TypeCycle.Length - 1) % TypeCycle.Length];
                    break;
                case ' ':
                    Load(shipPool[currentType.AsShipType()!.Value]);
                    break;
                case 'M':
                    FleetGroupConfiguration.LoadTroops(cargoPool, groups[selected], CargoType.Legion);
                    break;
                case 'N':
                    FleetGroupConfiguration.LoadTroops(cargoPool, groups[selected], CargoType.NinjaLegion);
                    break;
                case '+' or '-' or (>= '0' and <= '9'):
                    var end = c.Keys.IndexOf(GetGroupsKeys.Return, k);
                    Load(int.Parse(c.Keys[k..end]));
                    k = end;
                    break;
                case GetGroupsKeys.Esc:
                    return FleetGroupConfiguration.Finalize(groups, cargoPool);
                default:
                    throw new InvalidOperationException($"Case {c.Name}: key {(int)c.Keys[k]} at {k} isn't handled by the replay.");
            }
        }

        throw new InvalidOperationException($"Case {c.Name}: key script doesn't end in Esc.");
    }
}
