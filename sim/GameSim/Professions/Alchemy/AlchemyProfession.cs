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
/// Phase B (active professions fan-out): alchemy is the SECOND active-craft profession after
/// the blacksmith — but the IN-SIM-SCORED shape (PKD1 dual mode): the player's reagent choices
/// ride <c>CraftAction.Puzzle</c> as an <see cref="AlchemyReagentPuzzle"/> and
/// <see cref="AlchemyPuzzleScorer"/> grades them inside the pure sim (the blacksmith's Godot
/// overlay computes its own grade instead). Following the blacksmith's PA2/PKD3 talent remap
/// exactly: the retired quality-shift nodes (measured-pour/careful-distillation/
/// master-alchemist/potent-brews) no longer touch any roll — they became
/// <see cref="ProfessionDefinition.MinigameAssists"/> data the scorer consumes as forgiveness —
/// and <see cref="ReagentMastery"/> (the material axis) is KEPT on the quality model, still
/// raising the dominance roll's material ceiling. Integer stats only, no RNG, no wall clock,
/// no floats, no Godot references — constant data (KTD2/KTD4). Materials are the shared Mine
/// ore keys (grade proxy) until the P4 material registry lands. All collections are
/// <c>ImmutableSorted*</c> with <see cref="StringComparer.Ordinal"/>, so iteration order never
/// depends on registration order.
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
    // The retired quality-shift chain (now brew-assist data — see MinigameAssists below), a
    // Consumable specialist (potions are the identity), a material-efficiency → mastery pair,
    // and the tier unlock gates — the same shape as the blacksmith's post-PA2 tree.
    public const string MeasuredPour = "alchemy-measured-pour";               // assist 50‰ (chain root)
    public const string CarefulDistillation = "alchemy-careful-distillation"; // assist 70‰ (needs measured-pour)
    public const string MasterAlchemist = "alchemy-master-alchemist";         // assist 80‰ (needs careful-distillation)
    public const string PotentBrews = "alchemy-potent-brews";                 // assist 50‰, consumables only (needs measured-pour)
    public const string FrugalReagents = "alchemy-frugal-reagents";           // material efficiency (-1, floor 1)
    public const string ReagentMastery = "alchemy-reagent-mastery";           // material counts +1 grade (needs frugal-reagents)
    public const string Tier2Alchemy = "alchemy-tier-2";                      // unlocks tier 2 recipes
    public const string Tier3Alchemy = "alchemy-tier-3";                      // unlocks tier 3 recipes (needs tier 2)

    /// <summary>Talent mini-tree, keyed by node id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, TalentNode> Talents = new[]
    {
        new TalentNode(MeasuredPour,        "Measured Pour",        "Brew scoring forgives small reagent mistakes.",    ImmutableList<string>.Empty),
        new TalentNode(CarefulDistillation, "Careful Distillation", "Brew scoring forgives more (stacks with Measured Pour).", ImmutableList.Create(MeasuredPour)),
        new TalentNode(MasterAlchemist,     "Master Alchemist",     "The capstone — brew scoring forgives most (stacks with the chain).", ImmutableList.Create(CarefulDistillation)),
        new TalentNode(PotentBrews,         "Potent Brews",         "Extra brew forgiveness on consumable recipes.",    ImmutableList.Create(MeasuredPour)),
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
    /// The Alchemy profession definition — ACTIVE (Phase B). Tier gates on tiers 2/3.
    /// <see cref="ProfessionQualityModel.FlatShifts"/>/<see cref="ProfessionQualityModel.SlotShifts"/>
    /// are EMPTY (the PA2/PKD3 double-count fix: a node granting brew forgiveness AND still
    /// shifting a roll would count mastery twice); <see cref="ReagentMastery"/> stays as the
    /// material-mastery axis (no overlap with the puzzle — it raises the dominance roll's
    /// material ceiling, exactly like the blacksmith's material-mastery). The four retired
    /// quality nodes are remapped 1:1 to <see cref="ProfessionDefinition.MinigameAssists"/>,
    /// consumed sim-side by <see cref="AlchemyPuzzleScorer"/> as flat per-mille forgiveness
    /// (this profession's "adapter" IS the sim scorer — see the scorer's own doc).
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
            FlatShifts: ImmutableSortedDictionary<string, int>.Empty,
            SlotShifts: ImmutableSortedDictionary<string, SlotShift>.Empty,
            MaterialMasteryNode: ReagentMastery),
        ActiveCraft: true,
        MinigameAssists: new Dictionary<string, MinigameAssist>
        {
            // Measured Pour: a steadier hand — small brew mistakes are forgiven (mirrors Keen Eye's 50).
            [MeasuredPour] = new MinigameAssist(SweetZoneWidthBonus: 50, DriftRateReduction: 0, OffBeatForgiveness: 0),
            // Careful Distillation: cleaner separation — more forgiveness (mirrors Master's Touch's 70).
            [CarefulDistillation] = new MinigameAssist(SweetZoneWidthBonus: 0, DriftRateReduction: 70, OffBeatForgiveness: 0),
            // Master Alchemist: the capstone — the most forgiveness (mirrors Legendary Craft's 80).
            [MasterAlchemist] = new MinigameAssist(SweetZoneWidthBonus: 0, DriftRateReduction: 0, OffBeatForgiveness: 80),
            // Potent Brews: extra forgiveness on Consumable recipes only (the scorer scopes this
            // by the recipe's slot — mirrors Weapon Specialist's weapon-scoped 50).
            [PotentBrews] = new MinigameAssist(SweetZoneWidthBonus: 50, DriftRateReduction: 0, OffBeatForgiveness: 0),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal));
}
