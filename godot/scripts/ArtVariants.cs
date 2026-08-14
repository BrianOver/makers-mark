using System;
using System.Collections.Generic;
using System.Text;

namespace GodotClient;

/// <summary>
/// Picks ONE art id out of a pool of interchangeable variants, deterministically, from a stable
/// sim identity. This is how "every hero, villager, monster and crafted item looks a little
/// unique" is delivered without generating anything at runtime: the pool is committed pixels, the
/// pick is a pure function of an id the sim already owns.
///
/// <para><b>The id convention.</b> The base id IS variant 1 — <c>town2d-hero-vanguard</c>,
/// <c>item-iron-sword</c>, <c>monster-cave-rat</c>. Extra variants suffix a contiguous run from 2:
/// <c>-v2</c>, <c>-v3</c>, … So adding variation to any existing art is purely additive: nothing
/// is renamed, every id already committed keeps resolving, and a base id with no siblings simply
/// returns itself. An animated sprite varies as a WHOLE — the pick returns the variant's base id
/// and the caller appends its own frame suffix (<c>town2d-hero-vanguard-v3</c> +
/// <c>_walk2</c>), so a figure never mixes frames from two different bodies.</para>
///
/// <para><b>Why a hand-rolled hash and not <see cref="string.GetHashCode()"/>.</b> .NET randomizes
/// string hashing per process by default, so <c>GetHashCode</c> would hand the same hero a
/// different body on every launch — and, worse, a different body after a save/load inside the
/// same campaign, which is a visible continuity break, not a cosmetic one. FNV-1a over UTF-8 is
/// stable across processes, machines and OSes, which is the same reason the sim's own kernel
/// never reads a wall clock. <b>Nothing here touches the injected RNG stream</b> (KTD2): this is
/// presentation-side and reads only ids the sim already decided.</para>
///
/// <para><b>Null-tolerant by construction.</b> Every path returns a real id. A pool of one returns
/// the base id; an absent base id returns itself unchanged, so the caller's own miss-warning
/// (<c>UiKit.ArtRect</c>, <c>TownAssets2D.Placeholder</c>) still fires on the id a human would
/// expect to see named, never on a synthesized <c>-v7</c> that was never committed.</para>
/// </summary>
public static class ArtVariants
{
    /// <summary>The suffix that opens a variant id, e.g. <c>"-v2"</c>. Public so tests and the
    /// generator-side coverage checks compose the exact same string this resolver splits on.</summary>
    public const string VariantPrefix = "-v";

    /// <summary>Hard ceiling on how far <see cref="PoolFor"/> probes past the base id. Well above
    /// any plausible hand-authored pool (the town bodies ship 4 per class), and it exists so a
    /// corrupt manifest listing thousands of ids can never turn one draw into an unbounded scan.
    /// The probe stops at the first GAP regardless, so this only bounds the pathological case.</summary>
    public const int MaxVariants = 32;

    private static readonly Dictionary<string, IReadOnlyList<string>> PoolCache = new(StringComparer.Ordinal);

    /// <summary>
    /// The variant of <paramref name="baseId"/> that <paramref name="stableKey"/> always maps to.
    /// <paramref name="stableKey"/> must be something the sim owns and never re-issues within a
    /// campaign — a <c>HeroId</c>, an <c>ItemId</c>, a villager's spawn index — never a display
    /// name (renameable), never a loop counter over a re-sorted collection (unstable), and never
    /// anything derived from wall-clock or RNG.
    /// </summary>
    public static string Pick(string baseId, string stableKey)
    {
        if (string.IsNullOrEmpty(baseId)) return baseId;

        var pool = PoolFor(baseId);
        return pool.Count <= 1 ? baseId : pool[(int)(StableHash(stableKey) % (uint)pool.Count)];
    }

    /// <summary>Convenience overload for the common "one integer sim id" key, with a namespace
    /// prefix so hero 3 and item 3 do not land on the same pool index in two different pools that
    /// happen to be the same size — a coincidence nobody would ever debug, and free to avoid.</summary>
    public static string Pick(string baseId, string keyspace, int id) => Pick(baseId, $"{keyspace}:{id}");

    /// <summary>
    /// Every committed variant of <paramref name="baseId"/>, base id first. Probes the manifest
    /// for <c>-v2</c>, <c>-v3</c>, … and stops at the FIRST absent index: the run must be
    /// contiguous, so a half-generated batch that committed <c>-v2</c> and <c>-v4</c> yields a
    /// two-entry pool rather than silently pointing a third of the roster at a missing texture.
    /// Cached per base id — a town of villagers re-resolving on every rebuild never re-probes.
    /// </summary>
    public static IReadOnlyList<string> PoolFor(string baseId)
    {
        if (PoolCache.TryGetValue(baseId, out var cached)) return cached;

        var pool = new List<string>(capacity: 4) { baseId };
        for (var n = 2; n <= MaxVariants; n++)
        {
            var candidate = baseId + VariantPrefix + n;
            if (!IconRegistry.Has(candidate)) break;
            pool.Add(candidate);
        }

        PoolCache[baseId] = pool;
        return pool;
    }

    /// <summary>True iff <paramref name="id"/> is a variant of some other id (ends in a
    /// <c>-v&lt;n&gt;</c> with n ≥ 2). Used by the coverage tests to assert a variant never ships
    /// without its whole frame set.</summary>
    public static bool IsVariantId(string id)
    {
        var cut = id.LastIndexOf(VariantPrefix, StringComparison.Ordinal);
        if (cut < 0) return false;

        var digits = id[(cut + VariantPrefix.Length)..];
        return digits.Length > 0
            && int.TryParse(digits, out var n)
            && n >= 2
            && digits == n.ToString();
    }

    /// <summary>The base id a variant id belongs to, or the id itself when it is already a base.</summary>
    public static string BaseIdOf(string id) =>
        IsVariantId(id) ? id[..id.LastIndexOf(VariantPrefix, StringComparison.Ordinal)] : id;

    /// <summary>
    /// FNV-1a (32-bit) over the key's UTF-8 bytes. Chosen for being fully specified by a constant
    /// and two operations — the value this returns is a property of the string, identical on every
    /// machine and every run, which is the entire requirement. Not a security hash and never used
    /// as one.
    /// </summary>
    public static uint StableHash(string key)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(key ?? string.Empty))
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    /// <summary>Test seam: drops the pool cache so a test that stubs the manifest sees fresh
    /// probes. Never called by game code — the cache is process-lifetime by design.</summary>
    public static void ResetPoolCacheForTests() => PoolCache.Clear();
}
