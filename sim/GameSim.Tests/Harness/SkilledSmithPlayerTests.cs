using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// U2 (plan 2026-08-13-002): <see cref="SkilledSmithPlayer"/> delegates ENTIRELY to <see
/// cref="BaselinePlayer.ActionsFor"/> (KTD1 — "compose, never fork") and re-stamps only the
/// <see cref="CraftAction"/>s it gets back with a <see cref="SmithSkill"/> grade. These tests are
/// the anti-fork tripwire the plan calls for: every non-craft action, and the recipe/material
/// choice itself, must be byte-identical to what <see cref="BaselinePlayer"/> already decided —
/// nothing here may re-derive that choice.
/// </summary>
public class SkilledSmithPlayerTests
{
    private static GameState ExpeditionStateWithMaterials(int day, int nextItemId)
    {
        var state = GameFactory.NewGame(seed: 99);
        return state with
        {
            Day = day,
            Phase = DayPhase.Expedition,
            NextItemId = nextItemId,
            Player = state.Player with { Materials = state.Player.Materials.SetItem("copper", 20) },
        };
    }

    private static GameState MorningStateWithUnshelvedCraft(int day, int nextItemId)
    {
        var state = GameFactory.NewGame(seed: 33);
        var item = new Item(
            new ItemId(500), "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(8, 0, 2), new MakersMark("Test Smith", CraftedOnDay: 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        return state with
        {
            Day = day,
            Phase = DayPhase.Morning,
            NextItemId = nextItemId,
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(item.Id.Value, item),
        };
    }

    private static GameState EveningStateWithOreOffer(int day, int nextItemId)
    {
        var state = GameFactory.NewGame(seed: 55);
        return state with
        {
            Day = day,
            Phase = DayPhase.Evening,
            NextItemId = nextItemId,
            OpenOreOffers = ImmutableList.Create(new OreOffered(new HeroId(1), "copper", Quantity: 5, UnitPrice: 1)),
        };
    }

    // ---- Anti-fork: non-craft actions pass through untouched ---------------------------------

    [Fact]
    public void ActionsFor_MorningPhase_NonCraftActionsAreIdenticalToBaseline()
    {
        var state = MorningStateWithUnshelvedCraft(day: 5, nextItemId: 11);

        var baseline = BaselinePlayer.ActionsFor(state);
        var skilled = SkilledSmithPlayer.ActionsFor(state, SmithSkill.Veteran);

        Assert.True(baseline.Count > 1, "fixture assumption: Morning should emit more than one action here");
        Assert.DoesNotContain(baseline, a => a is CraftAction);
        Assert.Equal(baseline, skilled);
    }

    [Fact]
    public void ActionsFor_EveningPhase_NonCraftActionsAreIdenticalToBaseline()
    {
        var state = EveningStateWithOreOffer(day: 8, nextItemId: 3);

        var baseline = BaselinePlayer.ActionsFor(state);
        var skilled = SkilledSmithPlayer.ActionsFor(state, SmithSkill.Novice);

        Assert.NotEmpty(baseline);
        Assert.Equal(baseline, skilled);
    }

    [Theory]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void ActionsFor_PhasesBaselineNeverCrafts_PassThroughUnchanged(DayPhase phase)
    {
        var state = GameFactory.NewGame(seed: 77) with { Phase = phase };

        var baseline = BaselinePlayer.ActionsFor(state);
        var skilled = SkilledSmithPlayer.ActionsFor(state, SmithSkill.Veteran);

        Assert.Empty(baseline); // D5: BaselinePlayer's own documented behaviour for these phases
        Assert.Equal(baseline, skilled);
    }

    // ---- Craft stamping: grade added, recipe choice untouched ---------------------------------

    [Fact]
    public void ActionsFor_ExpeditionPhase_StampsGradeWithoutChangingRecipeChoice()
    {
        var state = ExpeditionStateWithMaterials(day: 12, nextItemId: 40);

        var baseline = BaselinePlayer.ActionsFor(state);
        var skilled = SkilledSmithPlayer.ActionsFor(state, SmithSkill.Veteran);

        var baselineCraft = Assert.Single(baseline.OfType<CraftAction>());
        var skilledCraft = Assert.Single(skilled.OfType<CraftAction>());

        Assert.Null(baselineCraft.PerformanceGrade);
        Assert.NotNull(skilledCraft.PerformanceGrade);
        Assert.Equal(baselineCraft.RecipeId, skilledCraft.RecipeId);
        Assert.Equal(baselineCraft.MaterialKey, skilledCraft.MaterialKey);

        // The wrapper changed EXACTLY the grade field — everything else on the record matches.
        Assert.Equal(baselineCraft with { PerformanceGrade = skilledCraft.PerformanceGrade }, skilledCraft);
    }

    [Fact]
    public void ActionsFor_SameSeedAndProfile_TwoRuns_IdenticalSequences()
    {
        var state = ExpeditionStateWithMaterials(day: 30, nextItemId: 88);

        var first = SkilledSmithPlayer.ActionsFor(state, SmithSkill.Novice);
        var second = SkilledSmithPlayer.ActionsFor(state, SmithSkill.Novice);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ActionsFor_NoviceProfile_NeverExceedsFineOnItsOwnMerits()
    {
        // KTD2's named risk: RollActive's isAutoCraft Superior cap only fires when
        // PerformanceGrade is null. The instant ANY non-null grade is stamped — including a
        // deliberately low novice one — that cap lifts for this craft too. So the novice profile
        // must stay Common/Fine (with occasional Poor) on the honesty of its OWN centre/spread,
        // never because RollActive's cap happened to save it.
        for (var day = 1; day <= 100; day++)
        {
            var state = ExpeditionStateWithMaterials(day, nextItemId: day * 4);
            var actions = SkilledSmithPlayer.ActionsFor(state, SmithSkill.Novice);

            foreach (var craft in actions.OfType<CraftAction>())
            {
                Assert.True(
                    craft.PerformanceGrade < 780,
                    $"novice grade {craft.PerformanceGrade} reached the Fine/Superior boundary on day {day}");
            }
        }
    }
}
