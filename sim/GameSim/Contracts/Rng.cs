namespace GameSim.Contracts;

/// <summary>
/// The only randomness source allowed inside the sim (KTD4). One stream, owned by the
/// kernel, threaded to phase systems in registration order — draw order is part of the
/// determinism contract. Implementations must be pure integer math (no transcendentals).
/// </summary>
public interface IDeterministicRng
{
    /// <summary>Next raw 32-bit draw.</summary>
    uint NextUInt();

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>Uniform roll in [0, 100) — the standard percentage roll.</summary>
    int Roll100();
}

/// <summary>Serializable RNG stream state (PCG32). Lives inside <see cref="GameState"/>.</summary>
public readonly record struct RngState(ulong State, ulong Inc)
{
    /// <summary>Derive a full stream state from a campaign seed (splitmix64 expansion).</summary>
    public static RngState FromSeed(ulong seed)
    {
        ulong Mix(ulong z)
        {
            z = unchecked(z + 0x9E3779B97F4A7C15UL);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        return new RngState(Mix(seed), Mix(seed ^ 0xDEADBEEFCAFEF00DUL) | 1UL);
    }
}
