namespace Reconstructed4021.Tests;

/// <summary>
/// A from-scratch port of this project's own fpc 3.2.2 runtime's real Random/RandSeed algorithm — a
/// Mersenne Twister variant with fpc-specific reseed/tempering behavior (rtl/inc/system.inc's
/// mtwist_* routines in the fpc release_3_2_2 source tree), not the classic Turbo Pascal LCG one might
/// expect from a DOS-era codebase, and not the newer Xoshiro128** generator later fpc releases moved
/// to. Verified empirically against this repo's actual installed fpc (not trusted from source alone —
/// see reference/verify/runworld.pas's RunRngCase and PascalRandomTests.MatchesGoldenFile) across
/// multiple seeds/ranges, a mid-run reseed, and a 624-word state-block boundary crossing.
///
/// Exists so a golden-file test can drive a real, non-degenerate multi-call RNG sequence — every other
/// domain's ForcedRandomValue convention returns the same fixed offset for every call, which is fine
/// for a single Rnd() roll but breaks any real Pascal loop that retries until a condition holds (e.g.
/// GetRandomXY's collision retry) that would resolve differently under a real sequence.
/// </summary>
public sealed class PascalRandom(uint seed) : Random
{
    private const int N = 624;
    private const int M = 397;
    private const uint UpperMask = 0x80000000;
    private const uint LowerMask = 0x7FFFFFFF;
    private const uint MatrixA = 0x9908B0DF;

    private readonly uint[] _state = new uint[N];
    private int _index = N + 1;
    private uint _randSeed = seed;
    private uint _oldRandSeed;

    public override int Next(int maxValue) => (int)((ulong)NextU32() * (uint)maxValue >> 32);

    private uint NextU32()
    {
        var index = _index;
        _index++;
        if (_randSeed != _oldRandSeed || index >= N + 1) {
            Init(_randSeed);
            _randSeed = ~_randSeed;
            _oldRandSeed = _randSeed;
            index = N;
        }
        if (index == N) {
            UpdateState();
            index = 0;
            _index = 1;
        }

        var result = _state[index];
        result ^= result >> 11;
        result ^= (result << 7) & 0x9D2C5680;
        result ^= (result << 15) & 0xEFC60000;
        result ^= result >> 18;
        return result;
    }

    private void Init(uint seedValue)
    {
        _state[0] = seedValue;
        for (var i = 1; i < N; i++)
            _state[i] = 1812433253u * (_state[i - 1] ^ (_state[i - 1] >> 30)) + (uint)i;
        _index = N;
    }

    private void UpdateState()
    {
        for (var i = 0; i < N - M; i++)
            _state[i] = _state[i + M] ^ Twist(_state[i], _state[i + 1]);
        for (var i = N - M; i < N - 1; i++)
            _state[i] = _state[i + (M - N)] ^ Twist(_state[i], _state[i + 1]);
        _state[N - 1] = _state[M - 1] ^ Twist(_state[N - 1], _state[0]);
        _index = 0;
    }

    private static uint Twist(uint u, uint v)
    {
        var mix = (u & UpperMask) | (v & LowerMask);
        return (mix >> 1) ^ ((v & 1) != 0 ? MatrixA : 0);
    }
}
