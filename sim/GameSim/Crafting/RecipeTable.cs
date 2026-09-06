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
/// reference consumable, then one row per LADDER RUNG on top of them — rung 0's Tier 4 Mine-ore
/// weapon (P2-END-01) and rungs 1-2's Tier 8-9 and Tier 12-14 sets, each gated by material
/// availability rather than a talent (see their own comments below). Stats scale with tier;
/// two-handed weapons and heavy shields/armor carry more weight than their tier peers.
/// Tier 2/3 recipes are gated behind the tier-unlock talent nodes (see
/// <see cref="TalentTree"/>). Consumables live in the SAME
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

        // ---- Rung 0 (P2-END-01, owner ruling 2026-09-06): the Mine's own ore, above Tier 3 ----
        // The ladder's gates are structural (ExpeditionResolver: PartyAveragePower < venue.Gate(floor)
        // halts at GateHeld, no roll), so the ONLY way past one is gear — and rung 0's gate (the Mine's
        // floor 5, 70) had no craftable answer that a party stuck under it could reach. Measured
        // (`arc-stall --policy baseline`, 200 seeds): seed 4 froze at power 67 for sixty days holding 26
        // units of Mine ore and 48 gold, and seed 5 froze at 72 against Gloomwood's 73. Both are
        // ABSORBING — every economic quantity stops moving on the same day — because the gear curve
        // above Tier 3 was strictly behind the gates it had to open.
        //
        // Mithril is the pinch point, and it is the ONE Mine ore that was orphaned. The Mine mints
        // copper/iron/steel/mithril/adamant on floors 1-5; floor 4's gate is 60, strictly BELOW floor
        // 5's 70, so a party three points short of graduating still farms mithril every night. It
        // graded 4 with no recipe of its own, which left the blacksmith's whole table topping out at
        // Tier 3 (steel, grade 3) — and Tier 2/3 are the only tiers ProfessionRegistry.Blacksmith's
        // TierGate locks behind a talent, which is itself behind a 400g Forge Tier purchase the stalled
        // economy cannot fund. Tier 4 carries no TierGate row, so this row is gated by MATERIAL
        // AVAILABILITY alone — the same rule, for the same reason, as the Tier 8-14 rung rows below.
        //
        // Attack 46 sits on the existing curve (Tier 3 greatsword 40 -> this 46 -> Tier 8 gloomsteel 60),
        // deliberately NOT a leap: a Common-grade Mithril Warblade beats the rival catalog's best blade
        // (Attack 20, the AE3 cap) by enough to move a party average, while an indifferent hand's Poor
        // roll is still refused outright by any floor-3+ veteran (ShoppingAi.VeteranMinQualityGrade), so
        // the gate stays a wall for a smith who does not earn the grade. Weight 8: mithril is light for
        // its bite, and it is still far over ShoppingAi.MysticMaxWeight (4), so no mystic carries it.
        //
        // Measured effect, 200 seeds x 100 days, `arc-stall --policy baseline`: 2 stalls -> 0, with the
        // healthy seeds' ending-day distribution unmoved (median 28 before, 28 after). See §11.8.1.
        new Recipe("mithril-warblade", "Mithril Warblade", BlacksmithProfession, ItemSlot.Weapon, Tier: 4, "mithril", MaterialQuantity: 4,
            new ItemStats(Attack: 46, Defense: 0, Weight: 8)),

        // ---- Rung 1 (the forward ladder, plan 2026-08-10-003 L3): Gloomwood-ore recipes ---
        // Tier 8-9, gated by MATERIAL AVAILABILITY, not a talent node — greenheart/amberpitch/
        // moonresin only enter Player.Materials via Gloomwood loot, which only flows once a
        // party graduates (Hero.LadderRank 0 -> 1, L1). Pre-graduation, BaselinePlayer's
        // Expedition-phase craft loop (OrderByDescending(Tier) — these sort FIRST) always finds
        // them ActionLegality-illegal for want of material and falls through to the Tier 1-3
        // rows unchanged (confirmed draw-neutral pre-graduation by characterization, this PR).
        // This is also the QualityRoller correctness fix the plan names: recipes topped out at
        // Tier 3 while Gloomwood ore grades 8-11, so a grade-11 material against a Tier-3 recipe
        // produced an oversized shift under the passive model; a Tier 8-9 home for grade 8-11
        // material keeps that shift bounded (pinned by QualityRollerTests).
        new Recipe("gloomsteel-blade", "Gloomsteel Blade", BlacksmithProfession, ItemSlot.Weapon, Tier: 8, "greenheart", MaterialQuantity: 4,
            new ItemStats(Attack: 60, Defense: 0, Weight: 12)),
        new Recipe("wardenweave-mail", "Wardenweave Mail", BlacksmithProfession, ItemSlot.Armor, Tier: 9, "amberpitch", MaterialQuantity: 5,
            new ItemStats(Attack: 0, Defense: 50, Weight: 14)),
        // Moonresin Draught: the salve upgrade the plan names by number — Heal 18 vs Field
        // Salve's 6, a real answer to floors where monsters hit for 25-30 (§11.8/plan design).
        new Recipe("moonresin-draught", "Moonresin Draught", BlacksmithProfession, ItemSlot.Consumable, Tier: 9, "moonresin", MaterialQuantity: 2,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 18)),

        // ---- Rung 2 (the forward ladder, plan 2026-08-10-003 L4): Emberfall-ore recipes ---
        // Tier 12-14, same shape and same reason as rung 1's Tier 8-9 rows above: gated by MATERIAL
        // AVAILABILITY, not a talent node — firebrick/slagiron/emberglass only enter Player.Materials
        // via Emberfall loot, which only flows once a party graduates Gloomwood (Hero.LadderRank 1 ->
        // 2, L1). Pre-graduation, BaselinePlayer's Tier-descending craft loop sorts these FIRST but
        // ActionLegality rejects them for want of material and falls through unchanged — draw-neutral
        // pre-graduation (same proof shape as L3's rung-1 rows). This is again the QualityRoller
        // correctness fix: Emberfall ore is grade 12-16, so a Tier 12-14 home keeps the material-grade
        // shift bounded (pinned by QualityRollerTests' RungTwo cases) instead of stacking on the
        // Tier 3 ceiling the way it would pre-ladder.
        //
        // Material choice mirrors L3's own precedent of never keying a craftable recipe to the BOSS
        // floor's ore (moonresin-draught used floor 3's moonresin, not floor 4's boss-drop
        // heartwood): the weapon and armor use floor 1/2's firebrick/slagiron (grade == tier, exactly
        // matching gloomsteel-blade/wardenweave-mail's own pattern), and the salve uses floor 4's
        // emberglass — one grade ABOVE its own tier, deliberately, the same +1 shape as
        // moonresin-draught — never floor 5's heartcoal, which a party cannot farm until it has
        // already beaten the boss this salve exists to help against.
        new Recipe("cinderforge-blade", "Cinderforge Blade", BlacksmithProfession, ItemSlot.Weapon, Tier: 12, "firebrick", MaterialQuantity: 4,
            new ItemStats(Attack: 90, Defense: 0, Weight: 14)),
        new Recipe("ashguild-plate", "Ashguild Plate", BlacksmithProfession, ItemSlot.Armor, Tier: 13, "slagiron", MaterialQuantity: 5,
            new ItemStats(Attack: 0, Defense: 75, Weight: 17)),
        // Emberglass Draught: the top salve the plan names by number — Heal 30, the rung-2 answer to
        // Emberfall's deeper floors the same way moonresin-draught answered Gloomwood's.
        new Recipe("emberglass-draught", "Emberglass Draught", BlacksmithProfession, ItemSlot.Consumable, Tier: 14, "emberglass", MaterialQuantity: 2,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 30)),
    }.ToImmutableSortedDictionary(r => r.RecipeId, r => r, StringComparer.Ordinal);

    /// <summary>Lookup by recipe id.</summary>
    public static bool TryGet(string recipeId, out Recipe? recipe)
    {
        var found = All.TryGetValue(recipeId, out var r);
        recipe = r;
        return found;
    }
}
