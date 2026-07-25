using System.Collections.Generic;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;

namespace GameSim.Cli;

/// <summary>
/// U12 (craft-quality legibility, PKD4): a crafted item's quality feels like RNG from the CLI's
/// chair — the player can queue a craft with no idea what band it can even reach. This is the
/// pure "what's the ceiling" half, extracted the same way <see cref="EventNarration"/> and
/// <see cref="CampNarration"/> are (so it's unit-testable, not only reachable by parsing
/// Program.cs's stdout). It mirrors <c>Crafting/QualityRoller</c>'s ceiling math — material grade
/// vs. recipe tier (<c>QualityRoller.MaterialCeiling</c>, "THE TABLE") and the auto-craft hard
/// clamp (<c>QualityRoller.RollActive</c>) — WITHOUT drawing the roll: no RNG, no mutation, no
/// rule change. If that private table ever moves, this mirror needs updating alongside it.
/// </summary>
public static class CraftQualityHint
{
    /// <summary>
    /// The best band a craft of <paramref name="recipeId"/> with <paramref name="materialKey"/>
    /// (and, if given, a captured minigame <paramref name="performanceGrade"/> in hand) can reach —
    /// or <see langword="null"/> when the recipe/material can't be resolved (the kernel reports
    /// that rejection on submit; there's nothing useful to preview). Auto-craft
    /// (<paramref name="performanceGrade"/> is <see langword="null"/>) can never exceed Superior
    /// (PKD4's hard clamp) regardless of material; only the 3D forge minigame's captured grade,
    /// paired with an at-or-above-tier material, can reach Masterwork.
    /// </summary>
    public static QualityGrade? CeilingFor(GameState state, string recipeId, string materialKey, int? performanceGrade)
    {
        if (!ProfessionRegistry.TryGetRecipe(recipeId, out var recipe) || recipe is null
            || !RecipeTable.MaterialGrades.TryGetValue(materialKey, out var materialGrade))
        {
            return null;
        }

        var masteryGrade = 0;
        if (ProfessionRegistry.TryGet(recipe.Profession, out var profession)
            && profession!.Quality.MaterialMasteryNode is { } masteryNode
            && state.Player.TalentsFor(recipe.Profession).Contains(masteryNode))
        {
            masteryGrade = 1;
        }

        var materialStep = materialGrade + masteryGrade - recipe.Tier;
        var materialCeiling = MaterialCeiling(materialStep);

        var topBeforeMaterial = performanceGrade is null ? QualityGrade.Superior : QualityGrade.Masterwork;
        return materialCeiling is { } ceiling && ceiling < topBeforeMaterial ? ceiling : topBeforeMaterial;
    }

    /// <summary>
    /// A per-recipe-tier ceiling readout for one owned material grade, e.g. <c>"t1:Superior
    /// t2:Fine t3:Fine"</c> — lets 'mats' answer "what does this material cap out at" without the
    /// player having to pick a specific recipe first. Same table as <see cref="CeilingFor"/>,
    /// just swept across the three live recipe tiers instead of one recipe's fixed tier.
    /// </summary>
    public static string MaterialCeilingByTier(int materialGrade)
    {
        var labels = new List<string>();
        for (var tier = 1; tier <= 3; tier++)
        {
            var ceiling = MaterialCeiling(materialGrade - tier);
            var label = ceiling switch
            {
                QualityGrade.Fine => "Fine",
                QualityGrade.Superior => "Superior",
                _ => "uncapped",
            };
            labels.Add($"t{tier}:{label}");
        }

        return string.Join(" ", labels);
    }

    /// <summary>Mirrors the private <c>QualityRoller.MaterialCeiling</c> switch verbatim (grade
    /// below tier caps Fine; matched grade caps Superior; above tier is uncapped).</summary>
    private static QualityGrade? MaterialCeiling(int materialStep) => materialStep switch
    {
        <= -1 => QualityGrade.Fine,
        0 => QualityGrade.Superior,
        _ => null,
    };
}
