using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>
/// The Engineering profession, expressed entirely as data (add-on content, P1 kernel). A
/// gadgeteer: field tools and mechanized gear across <see cref="ItemSlot.Weapon"/>,
/// <see cref="ItemSlot.Shield"/>, <see cref="ItemSlot.Armor"/> and — first content of its kind —
/// the <see cref="ItemSlot.Trinket"/> slot (fully wired by the P2 contract, zero content until
/// now). Its signature consumable is a Field Repair Kit: a trap/turret-style expedition aid that
/// READS as a gadget but mechanically reskins the shared <see cref="ConsumableKind.Heal"/> effect
/// (there is no gadget/deployable kind in the contract yet — see report). It plugs into the
/// profession-agnostic crafting pipeline (<c>CraftingHandlers</c>, <c>QualityRoller</c>, CLI,
/// Forge panel) through a single registration line the orchestrator applies to
/// <see cref="ProfessionRegistry.All"/> — no code changes outside this directory
/// (see docs/addon-guide.md).
///
/// Structure mirrors the blacksmith/tanning exactly (nothing bespoke): a quality-shift chain, one
/// slot specialist (here the Trinket-scoped Gadgeteer), a material-efficiency → material-mastery
/// pair, and the tier-2/tier-3 unlock gates. Integer stats only, no RNG, no wall clock, no floats,
/// no Godot references — constant data (KTD2/KTD4). Materials are the shared Mine ore keys (grade
/// proxy) until the P4 material registry lands. All collections are <c>ImmutableSorted*</c> with
/// <see cref="StringComparer.Ordinal"/>, so iteration order never depends on registration order.
/// </summary>
public static class EngineeringProfession
{
    /// <summary>Profession key — matches every recipe's <see cref="Recipe.Profession"/> (lowercase kebab).</summary>
    public const string Id = "engineering";

    // ---- Talent node ids ----------------------------------------------------------------
    // Quality-shift chain (+5 / +7 / +8), a Trinket slot specialist, a material-efficiency
    // → mastery pair, and the tier unlock gates — the same shape as the blacksmith tree.
    public const string Precision = "engineering-precision";              // flat +5 (chain root)
    public const string FineTolerance = "engineering-fine-tolerance";     // flat +7 (needs precision)
    public const string MasterMachinist = "engineering-master-machinist"; // flat +8 (needs fine-tolerance)
    public const string Gadgeteer = "engineering-gadgeteer";              // Trinket slot +5 (needs precision)
    public const string Salvage = "engineering-salvage";                 // material efficiency (-1, floor 1)
    public const string AlloyMastery = "engineering-alloy-mastery";      // material counts +1 grade (needs salvage)
    public const string Tier2Engineering = "engineering-tier-2";         // unlocks tier 2 recipes
    public const string Tier3Engineering = "engineering-tier-3";         // unlocks tier 3 recipes (needs tier 2)

    /// <summary>Talent mini-tree, keyed by node id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, TalentNode> Talents = new[]
    {
        new TalentNode(Precision,        "Precision",        "Quality roll +5.",                                 ImmutableList<string>.Empty),
        new TalentNode(FineTolerance,    "Fine Tolerance",   "Quality roll +7 (stacks with Precision).",         ImmutableList.Create(Precision)),
        new TalentNode(MasterMachinist,  "Master Machinist", "Quality roll +8 (stacks with the chain).",         ImmutableList.Create(FineTolerance)),
        new TalentNode(Gadgeteer,        "Gadgeteer",        "Quality roll +5 on trinket recipes.",              ImmutableList.Create(Precision)),
        new TalentNode(Salvage,          "Salvage",          "Recipes consume one fewer material (minimum 1).",  ImmutableList<string>.Empty),
        new TalentNode(AlloyMastery,     "Alloy Mastery",    "Material counts as one grade higher for quality.", ImmutableList.Create(Salvage)),
        new TalentNode(Tier2Engineering, "Tier 2 Engineering", "Unlocks tier 2 recipes.",                        ImmutableList<string>.Empty),
        new TalentNode(Tier3Engineering, "Tier 3 Engineering", "Unlocks tier 3 recipes.",                        ImmutableList.Create(Tier2Engineering)),
    }.ToImmutableSortedDictionary(n => n.NodeId, n => n, StringComparer.Ordinal);

    /// <summary>Recipe blueprints, keyed by recipe id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, Recipe> Recipes = new[]
    {
        // ---- Weapons (mechanized tools; two-handed = heavier) -------------------------------
        new Recipe("engineering-bolt-thrower",     "Bolt Thrower",     Id, ItemSlot.Weapon,  Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 8,  Defense: 0,  Weight: 2)),
        new Recipe("engineering-clockwork-glaive", "Clockwork Glaive", Id, ItemSlot.Weapon,  Tier: 2, "iron",   MaterialQuantity: 3, new ItemStats(Attack: 20, Defense: 0,  Weight: 5)),

        // ---- Shield (deployable barrier; light) ---------------------------------------------
        new Recipe("engineering-deployable-bulwark", "Deployable Bulwark", Id, ItemSlot.Shield, Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 0, Defense: 5, Weight: 1)),

        // ---- Armor (powered gear; tier 1 mystic-wearable, a heavy tier-3 exo-frame) ---------
        new Recipe("engineering-powered-vest", "Powered Vest", Id, ItemSlot.Armor, Tier: 1, "copper", MaterialQuantity: 3, new ItemStats(Attack: 0, Defense: 7,  Weight: 3)),
        new Recipe("engineering-exo-frame",    "Exo-Frame",    Id, ItemSlot.Armor, Tier: 3, "steel",  MaterialQuantity: 5, new ItemStats(Attack: 0, Defense: 30, Weight: 8)),

        // ---- Trinket (FIRST content in this fully-wired slot; utility gadgets) ---------------
        new Recipe("engineering-utility-multitool", "Utility Multitool", Id, ItemSlot.Trinket, Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 2, Defense: 2, Weight: 1)),
        new Recipe("engineering-targeting-monocle", "Targeting Monocle", Id, ItemSlot.Trinket, Tier: 2, "iron",   MaterialQuantity: 3, new ItemStats(Attack: 6, Defense: 0, Weight: 1)),

        // ---- Consumable (P2 spine): Field Repair Kit, tier 1, 2x copper, no combat stats,
        //      Heal(5) scaled by the shared quality table. Reads as a gadget; effect DATA drives
        //      shopping/use. ConsumableKind has only Heal today — see report for a wished kind. ----
        new Recipe("engineering-field-repair-kit", "Field Repair Kit", Id, ItemSlot.Consumable, Tier: 1, "copper", MaterialQuantity: 2,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 5)),
    }.ToImmutableSortedDictionary(r => r.RecipeId, r => r, StringComparer.Ordinal);

    /// <summary>
    /// The Engineering profession definition. Tier gates on tiers 2/3; quality chain and the
    /// Trinket-scoped Gadgeteer specialist supply the per-talent shifts; the universal quality
    /// math (±8/grade, threshold table) is shared by every profession and lives in
    /// <see cref="QualityRoller"/>.
    /// </summary>
    public static readonly ProfessionDefinition Definition = new(
        Id: Id,
        DisplayName: "Engineering",
        Recipes: Recipes,
        TalentNodes: Talents,
        TierGate: new Dictionary<int, string>
        {
            [2] = Tier2Engineering,
            [3] = Tier3Engineering,
        }.ToImmutableSortedDictionary(),
        MaterialEfficiencyNode: Salvage,
        Quality: new ProfessionQualityModel(
            FlatShifts: new Dictionary<string, int>
            {
                [Precision] = 5,
                [FineTolerance] = 7,
                [MasterMachinist] = 8,
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            SlotShifts: new Dictionary<string, SlotShift>
            {
                [Gadgeteer] = new SlotShift(ItemSlot.Trinket, 5),
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            MaterialMasteryNode: AlloyMastery));
}
