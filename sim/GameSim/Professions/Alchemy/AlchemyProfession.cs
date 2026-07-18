using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>
/// The Alchemy profession, expressed entirely as data (add-on content, P1 kernel). A potion
/// brewer and transmuter: its signature is a tiered line of healing consumables (the
/// <see cref="ItemSlot.Consumable"/> heal ladder is the depth axis) plus a couple of light
/// alchemical trinkets/robe. It plugs into the profession-agnostic crafting pipeline
/// (<c>CraftingHandlers</c>, <c>QualityRoller</c>, CLI, Forge panel) through a single
/// registration line the orchestrator applies to <see cref="ProfessionRegistry.All"/> — no code
/// changes outside this directory (see docs/addon-guide.md).
///
/// Structure mirrors the blacksmith/tanner exactly (nothing bespoke): a quality-shift chain,
/// one slot specialist (Consumable-scoped, since potions are the identity), a
/// material-efficiency → material-mastery pair, and the tier-2/tier-3 unlock gates. Integer
/// stats only, no RNG, no wall clock, no floats, no Godot references — constant data
/// (KTD2/KTD4). Materials are the shared Mine ore keys (grade proxy) until the P4 material
/// registry lands. All collections are <c>ImmutableSorted*</c> with
/// <see cref="StringComparer.Ordinal"/>, so iteration order never depends on registration order.
///
/// NOTE (contract): <see cref="ConsumableKind"/> is currently <c>{ Heal }</c> only, so every
/// consumable here — including the "utility brew" Transmuter's Tonic — ships as a Heal-kind
/// effect; the tonic's utility identity is flavor/name until a Buff kind is added by contract
/// micro-PR. Tiered heal MAGNITUDE is the whole consumable depth axis (see report's wishlist).
/// </summary>
public static class AlchemyProfession
{
    /// <summary>Profession key — matches every recipe's <see cref="Recipe.Profession"/> (lowercase kebab).</summary>
    public const string Id = "alchemy";

    // ---- Talent node ids ----------------------------------------------------------------
    // Quality-shift chain (+5 / +7 / +8), a Consumable slot specialist (potions are the
    // identity), a material-efficiency → mastery pair, and the tier unlock gates — the same
    // shape as the blacksmith/tanner tree.
    public const string MeasuredPour = "alchemy-measured-pour";               // flat +5 (chain root)
    public const string CarefulDistillation = "alchemy-careful-distillation"; // flat +7 (needs measured-pour)
    public const string MasterAlchemist = "alchemy-master-alchemist";         // flat +8 (needs careful-distillation)
    public const string PotentBrews = "alchemy-potent-brews";                 // Consumable slot +5 (needs measured-pour)
    public const string FrugalReagents = "alchemy-frugal-reagents";           // material efficiency (-1, floor 1)
    public const string ReagentMastery = "alchemy-reagent-mastery";           // material counts +1 grade (needs frugal-reagents)
    public const string Tier2Alchemy = "alchemy-tier-2";                      // unlocks tier 2 recipes
    public const string Tier3Alchemy = "alchemy-tier-3";                      // unlocks tier 3 recipes (needs tier 2)

    /// <summary>Talent mini-tree, keyed by node id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, TalentNode> Talents = new[]
    {
        new TalentNode(MeasuredPour,        "Measured Pour",        "Quality roll +5.",                                 ImmutableList<string>.Empty),
        new TalentNode(CarefulDistillation, "Careful Distillation", "Quality roll +7 (stacks with Measured Pour).",     ImmutableList.Create(MeasuredPour)),
        new TalentNode(MasterAlchemist,     "Master Alchemist",     "Quality roll +8 (stacks with the chain).",         ImmutableList.Create(CarefulDistillation)),
        new TalentNode(PotentBrews,         "Potent Brews",         "Quality roll +5 on consumable recipes.",           ImmutableList.Create(MeasuredPour)),
        new TalentNode(FrugalReagents,      "Frugal Reagents",      "Recipes consume one fewer material (minimum 1).",  ImmutableList<string>.Empty),
        new TalentNode(ReagentMastery,      "Reagent Mastery",      "Material counts as one grade higher for quality.", ImmutableList.Create(FrugalReagents)),
        new TalentNode(Tier2Alchemy,        "Tier 2 Alchemy",       "Unlocks tier 2 recipes.",                          ImmutableList<string>.Empty),
        new TalentNode(Tier3Alchemy,        "Tier 3 Alchemy",       "Unlocks tier 3 recipes.",                          ImmutableList.Create(Tier2Alchemy)),
    }.ToImmutableSortedDictionary(n => n.NodeId, n => n, StringComparer.Ordinal);

    /// <summary>Recipe blueprints, keyed by recipe id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, Recipe> Recipes = new[]
    {
        // ---- Healing consumable ladder (P2 spine): the signature line. Zero combat stats,
        //      a Heal effect whose MAGNITUDE tiers up and is scaled by the shared quality
        //      table. Effect DATA drives shopping/packs/in-combat use/attribution — no
        //      resolver or handler edits, ever. -------------------------------------------
        new Recipe("alchemy-minor-elixir",     "Minor Healing Elixir",  Id, ItemSlot.Consumable, Tier: 1, "copper", MaterialQuantity: 2,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 6)),
        new Recipe("alchemy-healing-draught",  "Healing Draught",       Id, ItemSlot.Consumable, Tier: 1, "copper", MaterialQuantity: 3,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 10)),
        // Utility brew — a transmuter's restorative. Its identity is flavor/name; mechanically
        // a strong Heal until a Buff ConsumableKind exists (contract change, not an add-on).
        new Recipe("alchemy-transmuters-tonic","Transmuter's Tonic",    Id, ItemSlot.Consumable, Tier: 2, "iron",   MaterialQuantity: 3,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 15)),
        new Recipe("alchemy-greater-elixir",   "Greater Healing Elixir",Id, ItemSlot.Consumable, Tier: 2, "iron",   MaterialQuantity: 3,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 22)),
        new Recipe("alchemy-panacea",          "Panacea",               Id, ItemSlot.Consumable, Tier: 3, "steel",  MaterialQuantity: 4,
            new ItemStats(Attack: 0, Defense: 0, Weight: 0), new ConsumableEffect(ConsumableKind.Heal, Magnitude: 30)),

        // ---- Alchemical gear/trinkets (light; charms and a robe) ---------------------------
        new Recipe("alchemy-alchemical-robe",  "Alchemical Robe",       Id, ItemSlot.Armor,   Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 0, Defense: 6,  Weight: 1)),
        new Recipe("alchemy-quicksilver-charm","Quicksilver Charm",     Id, ItemSlot.Trinket, Tier: 1, "copper", MaterialQuantity: 2, new ItemStats(Attack: 3, Defense: 0,  Weight: 0)),
        new Recipe("alchemy-philosophers-stone","Philosopher's Stone",  Id, ItemSlot.Trinket, Tier: 3, "steel",  MaterialQuantity: 4, new ItemStats(Attack: 0, Defense: 12, Weight: 1)),
    }.ToImmutableSortedDictionary(r => r.RecipeId, r => r, StringComparer.Ordinal);

    /// <summary>
    /// The Alchemy profession definition. Tier gates on tiers 2/3; quality chain and the
    /// Consumable specialist supply the per-talent shifts; the universal quality math (±8/grade,
    /// threshold table) is shared by every profession and lives in <see cref="QualityRoller"/>.
    /// </summary>
    public static readonly ProfessionDefinition Definition = new(
        Id: Id,
        DisplayName: "Alchemy",
        Recipes: Recipes,
        TalentNodes: Talents,
        TierGate: new Dictionary<int, string>
        {
            [2] = Tier2Alchemy,
            [3] = Tier3Alchemy,
        }.ToImmutableSortedDictionary(),
        MaterialEfficiencyNode: FrugalReagents,
        Quality: new ProfessionQualityModel(
            FlatShifts: new Dictionary<string, int>
            {
                [MeasuredPour] = 5,
                [CarefulDistillation] = 7,
                [MasterAlchemist] = 8,
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            SlotShifts: new Dictionary<string, SlotShift>
            {
                [PotentBrews] = new SlotShift(ItemSlot.Consumable, 5),
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            MaterialMasteryNode: ReagentMastery));
}
