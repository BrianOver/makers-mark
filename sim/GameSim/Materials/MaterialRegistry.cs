using System.Collections.Immutable;

namespace GameSim.Materials;

/// <summary>
/// The single source of truth for material keys → {unit price, crafting grade} (M1/P2, mirrors
/// <c>VenueRegistry</c>/<c>FactionRegistry</c>/<c>ClassRegistry</c>). Retires the two hand-written
/// switches that used to hold this data independently: <c>Drama/OrePricing.UnitPrice</c> and
/// <c>Crafting/RecipeTable.MaterialGrades</c> now BOTH derive from this registry (delegation, not
/// duplication), so a material's price and grade are stated once and can never drift apart.
///
/// The five Mine ores are seeded at their EXACT prior values (copper 3/1 … adamant 18/5 — byte-identical,
/// pinned by <c>MaterialRegistryTests</c>); the Crownsguard's regal materials (electrum, orichalcum) are
/// registered too but are NOT reachable by any live path — no live venue mints them and the Crownsguard
/// faction is unregistered — so they provably cannot move balance bands (R4: lookup-only, draw-neutral).
///
/// <para><b>Priced pool = the live surface.</b> <see cref="PricedPool"/> is the frozen in-path subset,
/// the material analogue of <c>VenueRegistry.LiveRotation</c> and <c>ClassRegistry.RecruitPool</c>:
/// registration does NOT make a material live. <c>OrePricing</c> prices only pool members and
/// <c>RecipeTable.MaterialGrades</c> grades only pool members, so both stay byte-identical to their old
/// five-key selves. Expanding the pool (to price/grade electrum for a new venue) is a determinism-gated
/// re-baseline — data, not a code edit to the choke points. That relocation of mechanism → data IS the
/// retirement of gate G8.</para>
///
/// Pure data: NO Godot reference, NO RNG, integer-only, ordinal string keys. Deterministic iteration.
/// </summary>
public static class MaterialRegistry
{
    // ---- The five Mine ores (the frozen priced pool) — ids mirror VenueRegistry.Mine floor ore keys.
    public const string Copper = "copper";
    public const string Iron = "iron";
    public const string Steel = "steel";
    public const string Mithril = "mithril";
    public const string Adamant = "adamant";

    // ---- Regal materials — REGISTERED but not in the priced pool (Crownsguard-supplied; no live path).
    public const string Electrum = "electrum";
    public const string Orichalcum = "orichalcum";

    /// <summary>Venue id whose floors mint the five Mine ores. Matches <c>VenueRegistry.MineId</c>
    /// (literal here to keep the material registry a leaf — no Venues dependency).</summary>
    private const string MineVenue = "mine";

    /// <summary>
    /// All registered materials, keyed by id. Sorted (Ordinal) for deterministic iteration. The five
    /// Mine ores carry their EXACT prior price/grade (byte-identical, pinned); the two regal materials
    /// extend the price/grade ladder above adamant and ship inert (not in <see cref="PricedPool"/>).
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, MaterialDefinition> All = new[]
    {
        // Mine ores — byte-identical to the old OrePricing / MaterialGrades switches (floor 1 → 5).
        new MaterialDefinition(Copper,     UnitPrice: 3,  Grade: 1, Tags: ImmutableArray<string>.Empty, SourceVenue: MineVenue),
        new MaterialDefinition(Iron,       UnitPrice: 5,  Grade: 2, Tags: ImmutableArray<string>.Empty, SourceVenue: MineVenue),
        new MaterialDefinition(Steel,      UnitPrice: 8,  Grade: 3, Tags: ImmutableArray<string>.Empty, SourceVenue: MineVenue),
        new MaterialDefinition(Mithril,    UnitPrice: 12, Grade: 4, Tags: ImmutableArray<string>.Empty, SourceVenue: MineVenue),
        new MaterialDefinition(Adamant,    UnitPrice: 18, Grade: 5, Tags: ImmutableArray<string>.Empty, SourceVenue: MineVenue),

        // Regal materials (Crownsguard) — inert: no live venue mints them, faction unregistered. Prices
        // and grades continue the ladder above adamant. Draw-neutral until a determinism-gated re-baseline
        // adds them to PricedPool (R4).
        new MaterialDefinition(Electrum,   UnitPrice: 24, Grade: 6, Tags: ImmutableArray<string>.Empty, SourceVenue: ""),
        new MaterialDefinition(Orichalcum, UnitPrice: 30, Grade: 7, Tags: ImmutableArray<string>.Empty, SourceVenue: ""),
        new MaterialDefinition("greenheart", UnitPrice: 36, Grade: 8, Tags: ImmutableArray<string>.Empty, SourceVenue: "gloomwood"),
        new MaterialDefinition("amberpitch", UnitPrice: 42, Grade: 9, Tags: ImmutableArray<string>.Empty, SourceVenue: "gloomwood"),
        new MaterialDefinition("moonresin", UnitPrice: 48, Grade: 10, Tags: ImmutableArray<string>.Empty, SourceVenue: "gloomwood"),
        new MaterialDefinition("heartwood", UnitPrice: 54, Grade: 11, Tags: ImmutableArray<string>.Empty, SourceVenue: "gloomwood"),
    }.ToImmutableSortedDictionary(m => m.Id, m => m, StringComparer.Ordinal);

    /// <summary>
    /// The materials that are LIVE — priced by <c>OrePricing</c> and graded by
    /// <c>RecipeTable.MaterialGrades</c>. THIS IS THE PRICED-POOL CONTRACT (same rule as
    /// <c>VenueRegistry.LiveRotation</c> / <c>ClassRegistry.RecruitPool</c>): a registered material is
    /// NOT automatically live. Frozen at the five Mine ores so the priced/graded surfaces stay
    /// byte-identical; expanding it is a deferred determinism-gated re-baseline. Registered add-on
    /// materials (electrum, orichalcum, future herbs/leathers) live in <see cref="All"/> but never here
    /// until that re-baseline.
    /// </summary>
    public static readonly ImmutableArray<string> PricedPool =
        ImmutableArray.Create(Copper, Iron, Steel, Mithril, Adamant);

    /// <summary>Resolve a material definition by key.</summary>
    public static bool TryGet(string id, out MaterialDefinition? definition)
    {
        var found = All.TryGetValue(id, out var def);
        definition = def;
        return found;
    }

    /// <summary>Whether a material key is registered.</summary>
    public static bool IsRegistered(string id) => All.ContainsKey(id);

    /// <summary>Whether a material key is in the frozen priced pool (priced/graded on the live path).</summary>
    public static bool IsPriced(string id) => PricedPool.Contains(id, StringComparer.Ordinal);

    /// <summary>
    /// Resolve a material definition by key or throw — the production path for a material id that always
    /// comes from a registration or a save written from a registered id, so an unregistered id is a
    /// malformed-data defect that should fail loudly.
    /// </summary>
    public static MaterialDefinition Require(string id) =>
        All.TryGetValue(id, out var def)
            ? def
            : throw new KeyNotFoundException($"Material id '{id}' is not registered.");

    /// <summary>Registered unit price of any material (throws for an unregistered id). The
    /// registry-wide lookup a faction-conformance check reads; <c>OrePricing</c> gates this behind
    /// <see cref="IsPriced"/> to stay byte-identical on the live path.</summary>
    public static int UnitPrice(string id) => Require(id).UnitPrice;

    /// <summary>Registered crafting grade of any material (throws for an unregistered id).</summary>
    public static int Grade(string id) => Require(id).Grade;
}
