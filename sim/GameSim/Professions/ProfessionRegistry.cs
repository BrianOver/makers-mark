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
    /// the tier gates, material-efficiency node, and quality shifts are the constants that used
    /// to be hardcoded in <c>CraftingHandlers</c>/<c>QualityRoller</c> — copied verbatim so the
    /// crafting behaviour and quality distribution stay byte-identical.
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
            FlatShifts: new Dictionary<string, int>
            {
                [TalentTree.KeenEye] = 5,
                [TalentTree.MasterTouch] = 7,
                [TalentTree.LegendaryCraft] = 8,
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            SlotShifts: new Dictionary<string, SlotShift>
            {
                [TalentTree.WeaponSpecialist] = new SlotShift(ItemSlot.Weapon, 5),
            }.ToImmutableSortedDictionary(StringComparer.Ordinal),
            MaterialMasteryNode: TalentTree.MaterialMastery));

    /// <summary>All registered professions, keyed by id. Sorted for deterministic iteration.</summary>
    public static readonly ImmutableSortedDictionary<string, ProfessionDefinition> All = new[]
    {
        Blacksmith,
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
