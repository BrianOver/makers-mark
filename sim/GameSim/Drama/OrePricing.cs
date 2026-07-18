using GameSim.Materials;

namespace GameSim.Drama;

/// <summary>
/// Floor-scaled ore ask prices for the Evening ore market (R6). Keyed by the material
/// key that <c>VenueFloor.OreKey</c> mints, so deeper floors ask more per unit.
/// Integer gold only. Tuning belongs to U10's balance gate.
///
/// M1: this is now a THIN delegation over <see cref="MaterialRegistry"/> — the single source of
/// truth for material price + grade. It prices exactly the frozen <see cref="MaterialRegistry.PricedPool"/>
/// (the five Mine ores) and throws for everything else, so its observable behavior is byte-identical to
/// the old hand-written switch: registered-but-unpriced materials (electrum, orichalcum) and genuinely
/// unknown keys both throw <see cref="ArgumentOutOfRangeException"/> exactly as before. Retiring the
/// switch = the price data now lives once, in the registry; widening what's priced = adding to the pool
/// (a determinism-gated re-baseline), never editing this method (R4).
/// </summary>
public static class OrePricing
{
    /// <summary>Gold per unit a returning hero asks for one unit of the material.</summary>
    public static int UnitPrice(string materialKey) =>
        MaterialRegistry.IsPriced(materialKey)
            ? MaterialRegistry.UnitPrice(materialKey)
            : throw new ArgumentOutOfRangeException(
                nameof(materialKey), materialKey,
                "Unknown ore material key — register it in MaterialRegistry and add it to PricedPool (a determinism-gated re-baseline).");
}
