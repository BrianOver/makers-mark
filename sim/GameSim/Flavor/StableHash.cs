namespace GameSim.Flavor;

/// <summary>
/// Project-owned FNV-1a 64-bit hash over integer inputs, used for flavor variant picking (KTD2).
///
/// <para><b>Why this exists:</b> variant selection must be byte-identical across OS, process,
/// and run — flavor lines are sim state (gossip lines serialize into saves and chronicles), so
/// a pick must never move. The .NET runtime's built-in string hash is randomized per process
/// by design (hash-flooding hardening) and must NEVER be used for anything serialized or
/// replayed — hence this project-owned hash.
/// The kernel RNG stream is equally forbidden here: flavor draws no RNG today, and starting to
/// draw would shift every seed's world. This hash depends on nothing but its arguments.</para>
///
/// <para><b>Algorithm:</b> canonical FNV-1a 64 (offset basis 0xCBF29CE484222325, prime
/// 0x100000001B3). Integer inputs fold little-endian (low byte first) via explicit shifts —
/// no <c>BitConverter</c>, so byte order is fixed regardless of platform endianness. Strings
/// fold per UTF-16 code unit (low byte, then high byte): ordinal, no culture, no normalization.
/// Golden pins in <c>FlavorEngineTests</c> guard against accidental algorithm change.</para>
/// </summary>
public static class StableHash
{
    /// <summary>Canonical FNV-1a 64-bit offset basis (0xCBF29CE484222325).</summary>
    public const ulong OffsetBasis = 14695981039346656037UL;

    /// <summary>Canonical FNV-1a 64-bit prime (0x100000001B3).</summary>
    public const ulong Prime = 1099511628211UL;

    /// <summary>Hash two values (allocation-free overload; same result as the params overload).</summary>
    public static ulong Mix(ulong a, ulong b) => MixValue(MixValue(OffsetBasis, a), b);

    /// <summary>Hash three values (allocation-free overload; same result as the params overload).</summary>
    public static ulong Mix(ulong a, ulong b, ulong c) =>
        MixValue(MixValue(MixValue(OffsetBasis, a), b), c);

    /// <summary>Hash any number of values. Zero values hashes to <see cref="OffsetBasis"/>.</summary>
    public static ulong Mix(params ulong[] values)
    {
        var hash = OffsetBasis;
        foreach (var value in values)
        {
            hash = MixValue(hash, value);
        }

        return hash;
    }

    /// <summary>
    /// Hash a string ordinally: each UTF-16 code unit folds as low byte then high byte.
    /// The empty string hashes to <see cref="OffsetBasis"/> by FNV definition.
    /// </summary>
    public static ulong HashString(string value)
    {
        var hash = OffsetBasis;
        foreach (var ch in value)
        {
            hash = MixByte(hash, unchecked((byte)ch));
            hash = MixByte(hash, unchecked((byte)((uint)ch >> 8)));
        }

        return hash;
    }

    private static ulong MixValue(ulong hash, ulong value)
    {
        for (var shift = 0; shift < 64; shift += 8)
        {
            hash = MixByte(hash, unchecked((byte)(value >> shift)));
        }

        return hash;
    }

    private static ulong MixByte(ulong hash, byte b)
    {
        unchecked
        {
            hash ^= b;
            return hash * Prime;
        }
    }
}
