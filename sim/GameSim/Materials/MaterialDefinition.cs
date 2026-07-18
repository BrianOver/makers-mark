using System.Collections.Immutable;

namespace GameSim.Materials;

/// <summary>
/// One craftable/tradeable material expressed entirely as data (M1/P2, the fourth
/// Blacksmith-as-data relocation after professions, classes, and venues). The material's two
/// load-bearing numbers — the Evening ore ask price and the crafting grade — used to live in two
/// disjoint hand-written switches (<c>Drama/OrePricing.cs</c> and <c>Crafting/RecipeTable.MaterialGrades</c>);
/// they now live here, ONCE, and those two surfaces derive from this registry (delegation, not
/// duplication). A new material becomes one entry in <see cref="MaterialRegistry.All"/>.
///
/// Pure data: NO Godot reference, NO RNG, integer-only (no floats, no transcendental <c>Math.*</c>,
/// no wall clock, no <c>string.GetHashCode</c>). Determinism-safe by construction (KTD2).
/// </summary>
/// <param name="Id">Stable string key (lowercase kebab, e.g. "copper"). Matches the registry key and
/// every <c>VenueFloor.OreKey</c> / <c>FactionDefinition.SuppliesOreKeys</c> entry that names it.</param>
/// <param name="UnitPrice">Gold per unit a returning hero asks for one unit (the old
/// <c>OrePricing.UnitPrice</c> value). Positive integer.</param>
/// <param name="Grade">Crafting grade — feeds the quality-roll shift relative to a recipe's tier (the
/// old <c>RecipeTable.MaterialGrades</c> value). Positive integer.</param>
/// <param name="Tags">Forward-looking classification seam (forbidden / herb / leather / catalyst …) for
/// the M13 tax-engine and the herb/leather professions and T7b factions. Empty in M1 (nothing reads
/// tags yet — lookup-only scope, R4). <c>StringComparer.Ordinal</c>.</param>
/// <param name="SourceVenue">Traceability metadata: the venue id that mints this material as floor
/// loot ("mine" for the five Mine ores), or empty when no live venue mints it (a faction-supplied
/// material such as the Crownsguard's regalia). Read by nothing in M1.</param>
public sealed record MaterialDefinition(
    string Id,
    int UnitPrice,
    int Grade,
    ImmutableArray<string> Tags,
    string SourceVenue);
