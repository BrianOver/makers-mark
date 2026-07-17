using GameSim.Flavor;

namespace GameArt;

/// <summary>
/// The generation seed for an asset, as a pure function of its <see cref="AssetSpec.Id"/> — reusing the
/// sim's exact FNV-1a + SplitMix64 finalizer (<see cref="StableHash"/>). Nobody hand-picks a seed, so
/// nobody can pick a <i>colliding</i> one: seed collision reduces to id collision (which conformance
/// already forbids). No RNG, no wall-clock, no float.
///
/// <para><b>Provenance, not reproducibility.</b> SDXL is not byte-reproducible across GPUs, so this is
/// the DEFAULT first candidate the master art-Claude renders, recorded in the build-half. Curation may
/// override it (e.g. to escape a bad draw); the real reproducibility guarantee is the committed PNG +
/// its sha256, the art analogue of golden-replay.</para>
/// </summary>
public static class AssetSeed
{
    /// <summary>
    /// The default seed for <paramref name="id"/>: the avalanche-finalized FNV-1a hash of the id,
    /// masked to a non-negative 31-bit range (a valid, positive generator seed).
    /// </summary>
    public static uint SeedFor(string id) =>
        (uint)(StableHash.Avalanche(StableHash.HashString(id)) & 0x7FFF_FFFFUL);
}
