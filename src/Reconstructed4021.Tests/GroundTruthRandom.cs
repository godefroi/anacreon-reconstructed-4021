namespace Reconstructed4021.Tests;

/// <summary>
/// The C# twin of <c>reference/verify/patches/INT.PAS.patch</c>'s <c>GroundTruthNextU32</c> — a
/// 32-bit LCG (Numerical Recipes constants: multiplier 1664525, increment 1013904223, implicit
/// mod-2^32 wraparound) this project designed and owns on both sides, replacing the ground-truth
/// harness's former dependency on reverse-engineering fpc's actual Random/RandSeed algorithm (see
/// <see cref="PascalRandom"/>'s own doc comment for that now-superseded approach, and
/// <c>reference/verify/README.md</c>'s own entry on why this replacement exists). The two
/// implementations only need to agree with each other, not be statistically strong or match any
/// real Pascal runtime — deliberately the simplest generator that does the job.
///
/// Same Lemire-style high-bit scale as <see cref="PascalRandom.Next"/>: a 64-bit product of the raw
/// 32-bit draw and the requested range, shifted right 32, maps into <c>[0,maxValue)</c> with no
/// modulo bias — only the underlying generator changed, not this scaling contract, so
/// <c>Rnd</c>/<c>PascalMath.Rnd</c> consume this exactly the way they already consume
/// <see cref="PascalRandom"/> or <see cref="FixedRandom"/>.
/// </summary>
public sealed class GroundTruthRandom(uint seed) : Random
{
    private uint _state = seed;

    public override int Next(int maxValue) => (int)((ulong)NextU32() * (uint)maxValue >> 32);

    private uint NextU32()
    {
        _state = _state * 1664525u + 1013904223u;
        return _state;
    }
}
