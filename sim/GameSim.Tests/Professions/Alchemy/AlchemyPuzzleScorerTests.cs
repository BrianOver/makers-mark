using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Professions;

namespace GameSim.Tests.Professions.Alchemy;

/// <summary>
/// Phase B: the alchemist's PURE in-sim puzzle scorer (PKD1 dual-mode seam) — the exact-order /
/// misplaced / wrong scoring rule, multiset-aware partial credit, talent-assist forgiveness with
/// Potent Brews' Consumable scoping, total-function robustness (null/overlong/garbage input), and
/// determinism (same puzzle in, same grade out — the property the balance gate leans on).
/// </summary>
public class AlchemyPuzzleScorerTests
{
    private static readonly ProfessionDefinition Alc = AlchemyProfession.Definition;
    private static readonly ImmutableSortedSet<string> NoTalents = ImmutableSortedSet<string>.Empty;

    private static AlchemyBrewScore Score(string recipeId, ImmutableList<int> reagents, ImmutableSortedSet<string>? talents = null) =>
        AlchemyPuzzleScorer.Score(Alc.Recipes[recipeId], new AlchemyReagentPuzzle(reagents), talents ?? NoTalents, Alc);

    [Fact]
    public void EveryAlchemyRecipe_HasAnIdealSequence_OfTierScaledLength()
    {
        foreach (var recipe in Alc.Recipes.Values)
        {
            var ideal = AlchemyPuzzleScorer.IdealSequenceFor(recipe);
            Assert.Equal(recipe.Tier + 2, ideal.Count); // t1=3, t2=4, t3=5 pours
            Assert.All(ideal, id => Assert.InRange(id, 0, AlchemyReagents.Count - 1));
        }
    }

    [Fact]
    public void PerfectPour_ScoresExactly1000()
    {
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(Alc.Recipes["alchemy-minor-elixir"]);
        var score = Score("alchemy-minor-elixir", ideal);

        Assert.Equal(1000, score.GradePermille);
        Assert.Equal(1000, score.ExactPermille);
        Assert.Equal(1000, score.PlacedPermille);
    }

    [Fact]
    public void EmptyPour_ScoresZero_AndNullReagentsIsDefensivelyEmpty()
    {
        Assert.Equal(0, Score("alchemy-minor-elixir", ImmutableList<int>.Empty).GradePermille);
        Assert.Equal(0, Score("alchemy-minor-elixir", null!).GradePermille);
    }

    [Fact]
    public void RightReagentsWrongOrder_ScoreHalfCredit()
    {
        // minor-elixir ideal: Sunpetal, Dewroot, Glimmercap. Rotate it — every pour is a
        // called-for reagent in the wrong position: 3 * 1 pt of a 6-pt maximum = 500.
        var rotated = ImmutableList.Create(AlchemyReagents.Glimmercap, AlchemyReagents.Sunpetal, AlchemyReagents.Dewroot);
        var score = Score("alchemy-minor-elixir", rotated);

        Assert.Equal(500, score.GradePermille);
        Assert.Equal(0, score.ExactPermille);
        Assert.Equal(1000, score.PlacedPermille);
    }

    [Fact]
    public void SpammingOneReagent_CannotFarmPartialCredit_MultisetAware()
    {
        // Ideal calls for exactly one Sunpetal (position 0). Pouring Sunpetal three times earns
        // the ONE exact match and nothing for the copies — 2 pts of 6 = 333.
        var spam = ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Sunpetal, AlchemyReagents.Sunpetal);
        var score = Score("alchemy-minor-elixir", spam);

        Assert.Equal(333, score.GradePermille);
        Assert.Equal(333, score.ExactPermille);
        Assert.Equal(333, score.PlacedPermille);
    }

    [Fact]
    public void WrongAndUnknownReagents_ScoreZero_NeverThrow()
    {
        // Voidsalt/Cinderbark aren't in the minor elixir at all; 99 isn't a reagent id.
        var garbage = ImmutableList.Create(AlchemyReagents.Voidsalt, AlchemyReagents.Cinderbark, 99);
        Assert.Equal(0, Score("alchemy-minor-elixir", garbage).GradePermille);
    }

    [Fact]
    public void PoursBeyondTheIdealLength_AreIgnored()
    {
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(Alc.Recipes["alchemy-minor-elixir"]);
        var overlong = ideal.Add(AlchemyReagents.Voidsalt).Add(AlchemyReagents.Voidsalt);
        Assert.Equal(1000, Score("alchemy-minor-elixir", overlong).GradePermille);
    }

    [Fact]
    public void DuplicateReagentRecipe_CreditsEachCalledForCopyOnce()
    {
        // greater-elixir ideal: Sunpetal, Dewroot, Glimmercap, Sunpetal (Sunpetal twice).
        // Pour Sunpetal at 0 (exact) and at 1 (misplaced — consumes the second Sunpetal slot):
        // 2 + 1 = 3 pts of 8 = 375.
        var pour = ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Sunpetal);
        Assert.Equal(375, Score("alchemy-greater-elixir", pour).GradePermille);
    }

    [Fact]
    public void TalentAssists_AddFlatForgiveness_CappedAt1000()
    {
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(Alc.Recipes["alchemy-minor-elixir"]);
        var oneWrong = ideal.SetItem(2, AlchemyReagents.Voidsalt); // 4 pts of 6 = 666 base

        Assert.Equal(666, Score("alchemy-minor-elixir", oneWrong).GradePermille);

        // Measured Pour alone: +50. Full chain + Potent Brews on a Consumable: +250.
        Assert.Equal(716, Score("alchemy-minor-elixir", oneWrong, ImmutableSortedSet.Create(AlchemyProfession.MeasuredPour)).GradePermille);
        var all = ImmutableSortedSet.Create(
            AlchemyProfession.MeasuredPour, AlchemyProfession.CarefulDistillation,
            AlchemyProfession.MasterAlchemist, AlchemyProfession.PotentBrews);
        Assert.Equal(916, Score("alchemy-minor-elixir", oneWrong, all).GradePermille);

        // A perfect pour stays clamped at 1000 — assists never push past the top.
        Assert.Equal(1000, Score("alchemy-minor-elixir", ideal, all).GradePermille);
    }

    [Fact]
    public void PotentBrews_IsConsumableScoped_LikeWeaponSpecialist()
    {
        // The robe is Armor: Potent Brews contributes nothing there, Measured Pour still does.
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(Alc.Recipes["alchemy-alchemical-robe"]);
        var oneWrong = ideal.SetItem(2, AlchemyReagents.Voidsalt); // robe ideal has no Voidsalt → 666 base

        var potentOnly = ImmutableSortedSet.Create(AlchemyProfession.PotentBrews);
        Assert.Equal(666, Score("alchemy-alchemical-robe", oneWrong, potentOnly).GradePermille);

        var potentPlusPour = potentOnly.Add(AlchemyProfession.MeasuredPour);
        Assert.Equal(716, Score("alchemy-alchemical-robe", oneWrong, potentPlusPour).GradePermille);
    }

    [Fact]
    public void LockedTalents_ContributeNothing()
    {
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(Alc.Recipes["alchemy-minor-elixir"]);
        var oneWrong = ideal.SetItem(2, AlchemyReagents.Voidsalt);
        Assert.Equal(
            Score("alchemy-minor-elixir", oneWrong).GradePermille,
            Score("alchemy-minor-elixir", oneWrong, NoTalents).GradePermille);
    }

    [Fact]
    public void SamePuzzleTwice_SameScore_PureFunction()
    {
        var pour = ImmutableList.Create(AlchemyReagents.Dewroot, AlchemyReagents.Sunpetal, AlchemyReagents.Voidsalt);
        var first = Score("alchemy-healing-draught", pour);
        var second = Score("alchemy-healing-draught", pour);
        Assert.Equal(first, second);
    }

    [Fact]
    public void UnknownFutureRecipe_GetsDeterministicFallbackSequence()
    {
        var future = Alc.Recipes["alchemy-minor-elixir"] with { RecipeId = "alchemy-not-yet-invented", Tier = 2 };
        var a = AlchemyPuzzleScorer.IdealSequenceFor(future);
        var b = AlchemyPuzzleScorer.IdealSequenceFor(future);

        Assert.Equal(a, b);
        Assert.Equal(4, a.Count); // tier 2 → 4 pours
        Assert.All(a, id => Assert.InRange(id, 0, AlchemyReagents.Count - 1));
    }
}
