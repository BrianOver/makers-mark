namespace GameSim.Drama;

/// <summary>
/// Floor-scaled ore ask prices for the Evening ore market (R6). Keyed by the material
/// key that <c>MonsterTable.OreKey</c> mints, so deeper floors ask more per unit.
/// Integer gold only. Tuning belongs to U10's balance gate.
/// </summary>
public static class OrePricing
{
    /// <summary>Gold per unit a returning hero asks for one unit of the material.</summary>
    public static int UnitPrice(string materialKey) => materialKey switch
    {
        "copper" => 3,   // floor 1
        "iron" => 5,     // floor 2
        "steel" => 8,    // floor 3
        "mithril" => 12, // floor 4
        "adamant" => 18, // floor 5
        _ => throw new ArgumentOutOfRangeException(
            nameof(materialKey), materialKey, "Unknown ore material key — extend OrePricing alongside MonsterTable.OreKey."),
    };
}
