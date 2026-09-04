using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
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
    public void RightReagentsWrongOrder_IsTheIndifferentHand_AndAnchorsToMidCommon()
    {
        // minor-elixir ideal: Sunpetal, Dewroot, Glimmercap. Rotate it — every pour is a
        // called-for reagent in the wrong position: 3 * 1 pt of a 6-pt maximum.
        //
        // P2-OQ11 re-baseline (was 500, the bare points/max fraction): this exact hand IS Alchemy's
        // INDIFFERENT hand — it knows every reagent and none of the order — so the shared
        // CraftCurve anchors it to CraftCurve.IndifferentAnchorPermille by definition. That is what
        // makes it Common rather than the Fine-to-Superior it used to grade, and it is the whole of
        // this unit's fix for "Alchemy's best work cannot be made": the scale now spends its range
        // on the order, which is the skill this craft tests.
        var rotated = ImmutableList.Create(AlchemyReagents.Glimmercap, AlchemyReagents.Sunpetal, AlchemyReagents.Dewroot);
        var score = Score("alchemy-minor-elixir", rotated);

        Assert.Equal(CraftCurve.IndifferentAnchorPermille, score.GradePermille);
        Assert.Equal(450, score.GradePermille); // spelled out too — a renamed constant must not hide a moved curve
        Assert.Equal(0, score.ExactPermille);
        Assert.Equal(1000, score.PlacedPermille);
    }

    [Fact]
    public void SpammingOneReagent_CannotFarmPartialCredit_MultisetAware()
    {
        // Ideal calls for exactly one Sunpetal (position 0). Pouring Sunpetal three times earns
        // the ONE exact match and nothing for the copies — 2 pts of 6.
        // P2-OQ11 re-baseline (was 333): 2 pts is BELOW the 3-pt indifferent hand, so it lands on
        // CraftCurve's compressed lower segment — 2 * 450 / 3 = 300. Spamming is now worse than an
        // honest wrong-order pour, which it always should have been.
        var spam = ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Sunpetal, AlchemyReagents.Sunpetal);
        var score = Score("alchemy-minor-elixir", spam);

        Assert.Equal(300, score.GradePermille);
        Assert.Equal(333, score.ExactPermille);   // sub-axes are raw readings, untouched by the curve
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
        // 2 + 1 = 3 pts of 8.
        // P2-OQ11 re-baseline (was 375): a 4-pour recipe's indifferent hand is 4 pts, so 3 pts sits
        // on CraftCurve's lower segment — 3 * 450 / 4 = 337. Two pours out of four is less than an
        // indifferent attempt at all four, and now grades that way.
        var pour = ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Sunpetal);
        Assert.Equal(337, Score("alchemy-greater-elixir", pour).GradePermille);
    }

    [Fact]
    public void TalentAssists_AddFlatForgiveness_CappedAt1000()
    {
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(Alc.Recipes["alchemy-minor-elixir"]);
        // 4 pts of 6. P2-OQ11 re-baseline (was 666): one pt above the 3-pt indifferent hand on
        // CraftCurve's upper segment — 450 + 1 * 550 / 3 = 633.
        var oneWrong = ideal.SetItem(2, AlchemyReagents.Voidsalt);

        Assert.Equal(633, Score("alchemy-minor-elixir", oneWrong).GradePermille);

        // Measured Pour alone: +50. Full chain + Potent Brews on a Consumable: +250. The assist is
        // added AFTER the curve, so these are still plain addition — mastery raises the floor and
        // never flattens the slope (§11.7.11's shape, now shared by all four crafts).
        Assert.Equal(683, Score("alchemy-minor-elixir", oneWrong, ImmutableSortedSet.Create(AlchemyProfession.MeasuredPour)).GradePermille);
        var all = ImmutableSortedSet.Create(
            AlchemyProfession.MeasuredPour, AlchemyProfession.CarefulDistillation,
            AlchemyProfession.MasterAlchemist, AlchemyProfession.PotentBrews);
        Assert.Equal(883, Score("alchemy-minor-elixir", oneWrong, all).GradePermille);

        // A perfect pour stays clamped at 1000 — assists never push past the top.
        Assert.Equal(1000, Score("alchemy-minor-elixir", ideal, all).GradePermille);
    }

    [Fact]
    public void PotentBrews_IsConsumableScoped_LikeWeaponSpecialist()
    {
        // The robe is Armor: Potent Brews contributes nothing there, Measured Pour still does.
        var ideal = AlchemyPuzzleScorer.IdealSequenceFor(Alc.Recipes["alchemy-alchemical-robe"]);
        // robe ideal has no Voidsalt → 4 pts of 6 → 633 after the shared curve (P2-OQ11 re-baseline,
        // was 666); the point of this test is the scoping, and the base is the same either way.
        var oneWrong = ideal.SetItem(2, AlchemyReagents.Voidsalt);

        var potentOnly = ImmutableSortedSet.Create(AlchemyProfession.PotentBrews);
        Assert.Equal(633, Score("alchemy-alchemical-robe", oneWrong, potentOnly).GradePermille);

        var potentPlusPour = potentOnly.Add(AlchemyProfession.MeasuredPour);
        Assert.Equal(683, Score("alchemy-alchemical-robe", oneWrong, potentPlusPour).GradePermille);
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
