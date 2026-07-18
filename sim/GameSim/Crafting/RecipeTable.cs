using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Materials;

namespace GameSim.Crafting;

/// <summary>
/// One craftable blueprint (R4). <see cref="Profession"/> is the profession key that owns
/// the recipe (e.g. "blacksmith"); it selects the profession definition that supplies the
/// tier gates, material-efficiency node, and quality model for the craft (see
/// <c>GameSim.Professions.ProfessionRegistry</c>). <see cref="MaterialKey"/> is the recipe's
/// baseline material (grade == tier); the player may substitute any stocked material — the
/// substituted grade relative to <see cref="Tier"/> shifts the quality roll
/// (see <see cref="QualityRoller"/>). <see cref="BaseStats"/> are Common-grade stats;
/// <see cref="ItemForge"/> applies the quality multiplier.
/// </summary>
public sealed record Recipe(
    string RecipeId,
    string Name,
    string Profession,
    ItemSlot Slot,
    int Tier,
    string MaterialKey,
    int MaterialQuantity,
    ItemStats BaseStats,
    ConsumableEffect? Effect = null);

/// <summary>
/// Static recipe data (U4/P2): 15 gear recipes (5 per gear slot, tiers 1–3) plus the
/// reference consumable. Stats scale with tier; two-handed weapons and heavy shields/armor
/// carry more weight than their tier peers. Tier 2/3 recipes are gated behind the
/// tier-unlock talent nodes (see <see cref="TalentTree"/>). Consumables live in the SAME
/// table as gear — one recipe pipeline, one lookup path — distinguished purely by
/// <see cref="Recipe.Effect"/> data, so an add-on profession ships consumables the same
/// way it ships gear (see docs/addon-guide.md).
/// </summary>
public static class RecipeTable
{
    /// <summary>Material grade per key (R4/R6): grade feeds the quality-roll shift. Derived from
    /// <see cref="MaterialRegistry"/> (the single source of truth, M1) — the grades of the frozen
    /// priced pool (the five Mine ores), byte-identical to the old hand-written map. Registered
    /// add-on materials (electrum, orichalcum) are deliberately absent: they are not in the priced
    /// pool, so a craft with them is still rejected exactly as before (draw-neutral, R4).</summary>
    public static readonly ImmutableSortedDictionary<string, int> MaterialGrades =
        MaterialRegistry.PricedPool.ToImmutableSortedDictionary(
            key => key,
            MaterialRegistry.Grade,
            StringComparer.Ordinal);

    /// <summary>All recipes, keyed by <see cref="Recipe.RecipeId"/>. Sorted for deterministic iteration.</summary>
    /// <summary>The profession key every recipe in this table belongs to (R4/P1).</summary>
    public const string BlacksmithProfession = "blacksmith";

    public static readonly ImmutableSortedDictionary<string, Recipe> All = new[]
    {
        // ---- Weapons (attack; two-handed = higher weight) --------------------------------
        new Recipe("dagger",       "Dagger",       BlacksmithProfession, ItemSlot.Weapon, Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 8,  Defense: 0,  Weight: 2)),
        new Recipe("shortsword",   "Shortsword",   BlacksmithProfession, ItemSlot.Weapon, Tier: 1, "copper", MaterialQuantity: 3, new ItemStats(Attack: 10, Defense: 0,  Weight: 4)),
        new Recipe("longsword",    "Longsword",    BlacksmithProfession, ItemSlot.Weapon, Tier: 2, "iron",   MaterialQuantity: 3, new ItemStats(Attack: 20, Defense: 0,  Weight: 5)),
        new Recipe("greataxe",     "Greataxe",     BlacksmithProfession, ItemSlot.Weapon, Tier: 2, "iron",   MaterialQuantity: 4, new ItemStats(Attack: 26, Defense: 0,  Weight: 9)),  // two-handed
        new Recipe("greatsword",   "Greatsword",   BlacksmithProfession, ItemSlot.Weapon, Tier: 3, "steel",  MaterialQuantity: 5, new ItemStats(Attack: 40, Defense: 0,  Weight: 10)), // two-handed

        // ---- Shields (defense; tower/bulwark = heavy) ------------------------------------
        new Recipe("buckler",      "Buckler",      BlacksmithProfession, ItemSlot.Shield, Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 0,  Defense: 6,  Weight: 2)),
        new Recipe("round-shield", "Round Shield", BlacksmithProfession, ItemSlot.Shield, Tier: 1, "copper", MaterialQuantity: 3, new ItemStats(Attack: 0,  Defense: 8,  Weight: 4)),
        new Recipe("kite-shield",  "Kite Shield",  BlacksmithProfession, ItemSlot.Shield, Tier: 2, "iron",   MaterialQuantity: 3, new ItemStats(Attack: 0,  Defense: 16, Weight: 6)),
        new Recipe("tower-shield", "Tower Shield", BlacksmithProfession, ItemSlot.Shield, Tier: 2, "iron",   MaterialQuantity: 5, new ItemStats(Attack: 0,  Defense: 22, Weight: 10)), // heavy
        new Recipe("bulwark",      "Bulwark",      BlacksmithProfession, ItemSlot.Shield, Tier: 3, "steel",  MaterialQuantity: 5, new ItemStats(Attack: 0,  Defense: 34, Weight: 12)), // heavy

        // ---- Armor (defense; plate = heavy) -----------------------------------------------
        new Recipe("chain-vest",   "Chain Vest",   BlacksmithProfession, ItemSlot.Armor,  Tier: 1, "copper", MaterialQuantity: 3, new ItemStats(Attack: 0,  Defense: 7,  Weight: 4)), // mystic-wearable (ShoppingAi.MysticMaxWeight)
        new Recipe("scale-mail",   "Scale Mail",   BlacksmithProfession, ItemSlot.Armor,  Tier: 1, "copper", MaterialQuantity: 4, new ItemStats(Attack: 0,  Defense: 9,  Weight: 7)),
        new Recipe("hauberk",      "Hauberk",      BlacksmithProfession, ItemSlot.Armor,  Tier: 2, "iron",   MaterialQuantity: 4, new ItemStats(Attack: 0,  Defense: 18, Weight: 9)),
        new Recipe("half-plate",   "Half Plate",   BlacksmithProfession, ItemSlot.Armor,  Tier: 2, "iron",   MaterialQuantity: 5, new ItemStats(Attack: 0,  Defense: 24, Weight: 12)), // heavy
        new Recipe("full-plate",   "Full Plate",   BlacksmithProfession, ItemSlot.Armor,  Tier: 3, "steel",  MaterialQuantity: 6, new ItemStats(Attack: 0,  Defense: 38, Weight: 15)), // heavy

        // ---- Consumables (P2 reference: proves the loadout spine end-to-end) --------------
        // Field Salve: tier 1, 2x copper (zero new material keys), no combat stats,
        // Heal(6) scaled by the same quality table as gear stats.
        new Recipe("field-salve",  "Field Salve",  BlacksmithProfession, ItemSlot.Consumable, Tier: 1, "copper", MaterialQuantity: 2,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 6)),
    }.ToImmutableSortedDictionary(r => r.RecipeId, r => r, StringComparer.Ordinal);

    /// <summary>Lookup by recipe id.</summary>
    public static bool TryGet(string recipeId, out Recipe? recipe)
    {
        var found = All.TryGetValue(recipeId, out var r);
        recipe = r;
        return found;
    }
}
