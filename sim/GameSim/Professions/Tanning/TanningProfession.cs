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
/// U3b (plan <c>2026-07-28-004</c>): tanning goes ACTIVE — crafts are graded by the tanning-frame
/// overlay via <see cref="TanningScrapeScorer"/> (<c>QualityRoller.RollActive</c>), exactly the
/// blacksmith/alchemy PA2/PKD3 pattern. <see cref="ProfessionQualityModel.FlatShifts"/>/
/// <see cref="ProfessionQualityModel.SlotShifts"/> are EMPTY (the double-count fix: <c>RollActive</c>
/// never reads them) — the retired quality-shift chain (Steady Hand/Supple Work/Master Tanner) and
/// the Armor-scoped Armorer specialist are remapped 1:1 into <see cref="MinigameAssists"/> at
/// alchemy's 50/70/80 ladder, consumed by <see cref="TanningScrapeScorer"/> as scrape forgiveness.
/// <see cref="HideMastery"/> stays on the quality model — the material-mastery axis is untouched by
/// the flip. Integer stats only, no RNG, no wall clock, no floats, no Godot references — constant
/// data (KTD2/KTD4). Materials are the shared Mine ore keys (grade proxy) until the P4 material
/// registry lands. All collections are <c>ImmutableSorted*</c> with <see cref="StringComparer.Ordinal"/>,
/// so iteration order never depends on registration order.
/// </summary>
public static class TanningProfession
{
    /// <summary>Profession key — matches every recipe's <see cref="Recipe.Profession"/> (lowercase kebab).</summary>
    public const string Id = "tanning";

    // ---- Talent node ids ----------------------------------------------------------------
    // The retired quality-shift chain (now scrape-assist data — see MinigameAssists below), an
    // Armor slot specialist (also retired to an assist), a material-efficiency → mastery pair,
    // and the tier unlock gates — the same shape as alchemy's post-flip tree.
    public const string SteadyHand = "tanning-steady-hand";       // assist 50‰ (chain root)
    public const string SuppleWork = "tanning-supple-work";       // assist 70‰ (needs steady-hand)
    public const string MasterTanner = "tanning-master-tanner";   // assist 80‰ (needs supple-work)
    public const string Armorer = "tanning-armorer";              // assist 50‰, Armor recipes only (needs steady-hand)
    public const string Thrift = "tanning-thrift";                // material efficiency (-1, floor 1)
    public const string HideMastery = "tanning-hide-mastery";     // material counts +1 grade (needs thrift)
    public const string Tier2Tanning = "tanning-tier-2";          // unlocks tier 2 recipes
    public const string Tier3Tanning = "tanning-tier-3";          // unlocks tier 3 recipes (needs tier 2)

    /// <summary>Talent mini-tree, keyed by node id. Sorted for deterministic iteration.</summary>
    private static readonly ImmutableSortedDictionary<string, TalentNode> Talents = new[]
    {
        new TalentNode(SteadyHand,   "Steady Hand",   "Scrape scoring forgives small mistakes.",                 ImmutableList<string>.Empty),
        new TalentNode(SuppleWork,   "Supple Work",   "Scrape scoring forgives more (stacks with Steady Hand).", ImmutableList.Create(SteadyHand)),
        new TalentNode(MasterTanner, "Master Tanner", "The capstone — scrape scoring forgives most (stacks with the chain).", ImmutableList.Create(SuppleWork)),
        new TalentNode(Armorer,      "Armorer",       "Extra scrape forgiveness on armor recipes.",              ImmutableList.Create(SteadyHand)),
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
    /// The Tanning profession definition — ACTIVE (U3b). Tier gates on tiers 2/3.
    /// <see cref="ProfessionQualityModel.FlatShifts"/>/<see cref="ProfessionQualityModel.SlotShifts"/>
    /// are EMPTY (the double-count fix); <see cref="HideMastery"/> stays as the material-mastery
    /// axis. The four retired quality nodes are remapped 1:1 to <see cref="MinigameAssists"/>,
    /// consumed sim-side by <see cref="TanningScrapeScorer"/> as flat per-mille scrape forgiveness.
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
            FlatShifts: ImmutableSortedDictionary<string, int>.Empty,
            SlotShifts: ImmutableSortedDictionary<string, SlotShift>.Empty,
            MaterialMasteryNode: HideMastery),
        ActiveCraft: true,
        MinigameAssists: new Dictionary<string, MinigameAssist>
        {
            // Steady Hand: a steadier hand — small scrape mistakes are forgiven (mirrors Measured Pour's 50).
            [SteadyHand] = new MinigameAssist(SweetZoneWidthBonus: 50, DriftRateReduction: 0, OffBeatForgiveness: 0),
            // Supple Work: cleaner work — more forgiveness (mirrors Careful Distillation's 70).
            [SuppleWork] = new MinigameAssist(SweetZoneWidthBonus: 0, DriftRateReduction: 70, OffBeatForgiveness: 0),
            // Master Tanner: the capstone — the most forgiveness (mirrors Master Alchemist's 80).
            [MasterTanner] = new MinigameAssist(SweetZoneWidthBonus: 0, DriftRateReduction: 0, OffBeatForgiveness: 80),
            // Armorer: extra forgiveness on Armor recipes only (the scorer scopes this by the
            // recipe's slot — mirrors Potent Brews' consumable-scoped 50).
            [Armorer] = new MinigameAssist(SweetZoneWidthBonus: 50, DriftRateReduction: 0, OffBeatForgiveness: 0),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal));
}
