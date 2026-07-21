using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;

namespace GameSim.Professions;

/// <summary>
/// The single lookup the crafting pipeline uses to resolve a profession key to its
/// <see cref="ProfessionDefinition"/> (P1 kernel). Blacksmith is registered here, built
/// entirely from the EXISTING <see cref="RecipeTable"/> and <see cref="TalentTree"/> data —
/// nothing about blacksmith behaviour changes; its rules are merely relocated into a
/// definition so the pipeline is profession-agnostic. New professions register by adding a
/// definition to <see cref="All"/> (an add-on task, not core work).
/// </summary>
public static class ProfessionRegistry
{
    /// <summary>Blacksmith's profession key (matches every recipe's <see cref="Recipe.Profession"/>).</summary>
    public const string BlacksmithId = RecipeTable.BlacksmithProfession;

    /// <summary>
    /// The blacksmith, re-expressed as data. Recipes and talent nodes are the existing tables;
    /// the tier gates and material-efficiency node are the constants that used to be hardcoded
    /// in <c>CraftingHandlers</c> — copied verbatim so THOSE stay byte-identical.
    ///
    /// PA2/PKD2/PKD3 flips the blacksmith to the ACTIVE dominance model
    /// (<see cref="ActiveCraft"/> in <see cref="ProfessionDefinition"/>): the old quality-shift
    /// talents (keen-eye/master-touch/legendary-craft/weapon-specialist) no longer touch the
    /// roll at all, so <see cref="ProfessionQualityModel.FlatShifts"/>/<see
    /// cref="ProfessionQualityModel.SlotShifts"/> are EMPTY here (double-count resolved — the
    /// same node unlocking a minigame assist AND still shifting the retired roll would double-
    /// count mastery). <see cref="ProfessionQualityModel.MaterialMasteryNode"/> is KEPT — the
    /// material axis has no overlap with the minigame and still raises the roll's ceiling. The
    /// four retired nodes are remapped 1:1 to <see cref="ProfessionDefinition.MinigameAssists"/>
    /// data for the PA6 forge-overlay adapter to read.
    /// </summary>
    public static readonly ProfessionDefinition Blacksmith = new(
        Id: BlacksmithId,
        DisplayName: "Blacksmith",
        Recipes: RecipeTable.All,
        TalentNodes: TalentTree.Nodes,
        TierGate: new Dictionary<int, string>
        {
            [2] = TalentTree.Tier2Smithing,
            [3] = TalentTree.Tier3Smithing,
        }.ToImmutableSortedDictionary(),
        MaterialEfficiencyNode: TalentTree.MaterialEfficiency,
        Quality: new ProfessionQualityModel(
            FlatShifts: ImmutableSortedDictionary<string, int>.Empty,
            SlotShifts: ImmutableSortedDictionary<string, SlotShift>.Empty,
            MaterialMasteryNode: TalentTree.MaterialMastery),
        ActiveCraft: true,
        MinigameAssists: new Dictionary<string, MinigameAssist>
        {
            // Keen Eye: widen the smelt/quench sweet zones — a sharper eye reads the gauge.
            [TalentTree.KeenEye] = new MinigameAssist(SweetZoneWidthBonus: 50, DriftRateReduction: 0, OffBeatForgiveness: 0),
            // Master's Touch: a steadier hand slows heat/shaping drift.
            [TalentTree.MasterTouch] = new MinigameAssist(SweetZoneWidthBonus: 0, DriftRateReduction: 70, OffBeatForgiveness: 0),
            // Legendary Craft: the capstone — forgives off-beat forge strikes.
            [TalentTree.LegendaryCraft] = new MinigameAssist(SweetZoneWidthBonus: 0, DriftRateReduction: 0, OffBeatForgiveness: 80),
            // Weapon Specialist: extra sweet-zone width, weapon recipes only (the adapter scopes
            // this by the recipe's slot — the sim just exposes the data).
            [TalentTree.WeaponSpecialist] = new MinigameAssist(SweetZoneWidthBonus: 50, DriftRateReduction: 0, OffBeatForgiveness: 0),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal));

    /// <summary>All registered professions, keyed by id. Sorted for deterministic iteration.</summary>
    public static readonly ImmutableSortedDictionary<string, ProfessionDefinition> All = new[]
    {
        Blacksmith,
        TanningProfession.Definition,
        EngineeringProfession.Definition,
        AlchemyProfession.Definition,
    }.ToImmutableSortedDictionary(p => p.Id, p => p, StringComparer.Ordinal);

    /// <summary>
    /// Every recipe across every registered profession, keyed by recipe id — the global
    /// lookup the crafting handler uses to resolve a <c>CraftAction</c> to its recipe (and,
    /// via <see cref="Recipe.Profession"/>, to its owning definition). Recipe ids are unique
    /// across professions.
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, Recipe> AllRecipes =
        All.Values
            .SelectMany(p => p.Recipes.Values)
            .ToImmutableSortedDictionary(r => r.RecipeId, r => r, StringComparer.Ordinal);

    /// <summary>Resolve a profession definition by key.</summary>
    public static bool TryGet(string professionId, out ProfessionDefinition? definition)
    {
        var found = All.TryGetValue(professionId, out var def);
        definition = def;
        return found;
    }

    /// <summary>Whether a profession key is registered.</summary>
    public static bool IsRegistered(string professionId) => All.ContainsKey(professionId);

    /// <summary>Resolve a recipe (and its owning profession) by recipe id.</summary>
    public static bool TryGetRecipe(string recipeId, out Recipe? recipe)
    {
        var found = AllRecipes.TryGetValue(recipeId, out var r);
        recipe = r;
        return found;
    }
}
