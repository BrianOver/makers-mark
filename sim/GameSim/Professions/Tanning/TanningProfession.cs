using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>
/// The Tanning profession, expressed entirely as data (add-on content, P1 kernel). A
/// leatherworker: light, low-weight <see cref="ItemSlot.Armor"/> and <see cref="ItemSlot.Shield"/>
/// pieces plus a healing field poultice consumable. It plugs into the profession-agnostic
/// crafting pipeline (<c>CraftingHandlers</c>, <c>QualityRoller</c>, CLI, Forge panel) through a
/// single registration line the orchestrator applies to <see cref="ProfessionRegistry.All"/> —
/// no code changes outside this directory (see docs/addon-guide.md).
///
/// Structure mirrors the blacksmith exactly (nothing bespoke): a quality-shift chain, one
/// slot specialist, a material-efficiency → material-mastery pair, and the tier-2/tier-3
/// unlock gates. Integer stats only, no RNG, no wall clock, no floats, no Godot references —
/// constant data (KTD2/KTD4). Materials are the shared Mine ore keys (grade proxy) until the
/// P4 material registry lands. All collections are <c>ImmutableSorted*</c> with
/// <see cref="StringComparer.Ordinal"/>, so iteration order never depends on registration order.
/// </summary>
public static class TanningProfession
{
    /// <summary>Profession key — matches every recipe's <see cref="Recipe.Profession"/> (lowercase kebab).</summary>
    public const string Id = "tanning";

    // ---- Talent node ids ----------------------------------------------------------------
    // Quality-shift chain (+5 / +7 / +8), an Armor slot specialist, a material-efficiency
    // → mastery pair, and the tier unlock gates — the same shape as the blacksmith tree.
    public const string SteadyHand = "tanning-steady-hand";       // flat +5 (chain root)
    public const string SuppleWork = "tanning-supple-work";       // flat +7 (needs steady-hand)
    public const string MasterTanner = "tanning-master-tanner";   // flat +8 (needs supple-work)
    public const string Armorer = "tanning-armorer";              // Armor slot +5 (needs steady-hand)
    public const string Thrift = "tanning-thrift";                // material efficiency (-1, floor 1)
    public const string HideMastery = "tanning-hide-mastery";     // material counts +1 grade (needs thrift)
    public const string Tier2Tanning = "tanning-tier-2";          // unlocks tier 2 recipes
    public const string Tier3Tanning = "tanning-tier-3";          // unlocks tier 3 recipes (needs tier 2)

    /// <summary>Talent mini-tree, keyed by node id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, TalentNode> Talents = new[]
    {
        new TalentNode(SteadyHand,   "Steady Hand",   "Quality roll +5.",                                 ImmutableList<string>.Empty),
        new TalentNode(SuppleWork,   "Supple Work",   "Quality roll +7 (stacks with Steady Hand).",       ImmutableList.Create(SteadyHand)),
        new TalentNode(MasterTanner, "Master Tanner", "Quality roll +8 (stacks with the chain).",         ImmutableList.Create(SuppleWork)),
        new TalentNode(Armorer,      "Armorer",       "Quality roll +5 on armor recipes.",                ImmutableList.Create(SteadyHand)),
        new TalentNode(Thrift,       "Thrift",        "Recipes consume one fewer material (minimum 1).",  ImmutableList<string>.Empty),
        new TalentNode(HideMastery,  "Hide Mastery",  "Material counts as one grade higher for quality.", ImmutableList.Create(Thrift)),
        new TalentNode(Tier2Tanning, "Tier 2 Tanning","Unlocks tier 2 recipes.",                          ImmutableList<string>.Empty),
        new TalentNode(Tier3Tanning, "Tier 3 Tanning","Unlocks tier 3 recipes.",                          ImmutableList.Create(Tier2Tanning)),
    }.ToImmutableSortedDictionary(n => n.NodeId, n => n, StringComparer.Ordinal);

    /// <summary>Recipe blueprints, keyed by recipe id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, Recipe> Recipes = new[]
    {
        // ---- Armor (light leather; low weight per tier — mystic-wearable at tier 1) ---------
        new Recipe("tanning-leather-cap",       "Leather Cap",       Id, ItemSlot.Armor,  Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 0, Defense: 5,  Weight: 1)),
        new Recipe("tanning-hide-jerkin",       "Hide Jerkin",       Id, ItemSlot.Armor,  Tier: 1, "copper", MaterialQuantity: 3, new ItemStats(Attack: 0, Defense: 7,  Weight: 3)),
        new Recipe("tanning-studded-leather",   "Studded Leather",   Id, ItemSlot.Armor,  Tier: 2, "iron",   MaterialQuantity: 3, new ItemStats(Attack: 0, Defense: 15, Weight: 5)),
        new Recipe("tanning-dragonhide-armor",  "Dragonhide Armor",  Id, ItemSlot.Armor,  Tier: 3, "steel",  MaterialQuantity: 5, new ItemStats(Attack: 0, Defense: 30, Weight: 8)),

        // ---- Shields (hide-bound; light and cheap) ------------------------------------------
        new Recipe("tanning-leather-buckler",   "Leather Buckler",   Id, ItemSlot.Shield, Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 0, Defense: 5,  Weight: 1)),
        new Recipe("tanning-hide-shield",       "Hide Shield",       Id, ItemSlot.Shield, Tier: 2, "iron",   MaterialQuantity: 3, new ItemStats(Attack: 0, Defense: 13, Weight: 4)),

        // ---- Consumable (P2 spine): Field Poultice, tier 1, 2x copper, no combat stats,
        //      Heal(5) scaled by the shared quality table. Effect data drives shopping/use. -----
        new Recipe("tanning-field-poultice",    "Field Poultice",    Id, ItemSlot.Consumable, Tier: 1, "copper", MaterialQuantity: 2,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 5)),
    }.ToImmutableSortedDictionary(r => r.RecipeId, r => r, StringComparer.Ordinal);

    /// <summary>
    /// The Tanning profession definition. Tier gates on tiers 2/3; quality chain and Armor
    /// specialist supply the per-talent shifts; the universal quality math (±8/grade, threshold
    /// table) is shared by every profession and lives in <see cref="QualityRoller"/>.
    /// </summary>
    public static readonly ProfessionDefinition Definition = new(
        Id: Id,
        DisplayName: "Tanning",
        Recipes: Recipes,
        TalentNodes: Talents,
        TierGate: new Dictionary<int, string>
        {
            [2] = Tier2Tanning,
            [3] = Tier3Tanning,
        }.ToImmutableSortedDictionary(),
        MaterialEfficiencyNode: Thrift,
        Quality: new ProfessionQualityModel(
            FlatShifts: new Dictionary<string, int>
            {
                [SteadyHand] = 5,
                [SuppleWork] = 7,
                [MasterTanner] = 8,
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            SlotShifts: new Dictionary<string, SlotShift>
            {
                [Armorer] = new SlotShift(ItemSlot.Armor, 5),
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            MaterialMasteryNode: HideMastery));
}
