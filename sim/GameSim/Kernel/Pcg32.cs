using GameSim.Contracts;

namespace GameSim.Kernel;

/// <summary>
/// PCG32 (O'Neill) — pure integer math, serializable state, identical output on every
/// OS and JIT. The sim's single RNG stream (KTD4).
/// </summary>
public sealed class Pcg32 : IDeterministicRng
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _inc;

    public Pcg32(RngState state)
    {
        _state = state.State;
        _inc = state.Inc | 1UL; // stream increment must be odd
    }

    /// <summary>Current serializable stream state.</summary>
    public RngState Snapshot() => new(_state, _inc);

    public uint NextUInt()
    {
        var old = _state;
        _state = unchecked(old * Multiplier + _inc);
        var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        var rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive");
        }

        var range = (uint)(maxExclusive - minInclusive);

        // Debiased modulo (Lemire-style rejection) so every value is equally likely.
        var threshold = (uint)(-range) % range;
        while (true)
        {
            var r = NextUInt();
            if (r >= threshold)
            {
                return (int)(r % range) + minInclusive;
            }
        }
    }

    public int Roll100() => NextInt(0, 100);
}
