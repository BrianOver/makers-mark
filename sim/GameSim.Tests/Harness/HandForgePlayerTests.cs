using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// 2026-09-03 owner ruling: <see cref="HandForgePlayer"/> is the first harness policy that ever
/// constructs a <see cref="ForgeTraceInput"/> — closing the blind spot #686 found (every other
/// policy either auto-crafts or stamps a raw <c>PerformanceGrade</c>, never a real hand-forge).
/// Anti-fork coverage mirrors <c>SkilledSmithPlayerTests</c> exactly: every non-craft action, and
/// the recipe/material choice itself, must be byte-identical to what <see cref="BaselinePlayer"/>
/// already decided — nothing here may re-derive that choice. The hand-forge tests below are the
/// grep-level/assertion-level proof this policy genuinely submits a <see cref="ForgeTraceInput"/>
/// (never merely assumed) and that it is genuinely scored by <see cref="ForgeScorer"/> end-to-end.
/// </summary>
public class HandForgePlayerTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState ExpeditionStateWithMaterials(int day, int nextItemId, int copper = 20, int slots = 5)
    {
        // Same fixture shape as SkilledSmithPlayerTests.ExpeditionStateWithMaterials: one ungeared,
        // alive hero gives BaselinePlayer's HasBuyer check a real gap to craft into.
        var hero = new Hero(
            new HeroId(1), "Test Hero", ClassRegistry.VanguardId, Level: 1, MaxHp: 20, Gold: 500,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var state = GameFactory.NewGame(seed: 99, ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero));
        return state with
        {
            Day = day,
            Phase = DayPhase.Expedition,
            NextItemId = nextItemId,
            ActionSlotsRemaining = slots,
            Player = state.Player with { Materials = state.Player.Materials.SetItem("copper", copper) },
        };
    }

    // ---- Anti-fork: non-craft actions and phases pass through untouched -----------------------

    [Fact]
    public void ActionsFor_MorningPhase_IsIdenticalToBaseline()
    {
        var state = GameFactory.NewGame(seed: 5) with { Day = 5, Phase = DayPhase.Morning };

        var baseline = BaselinePlayer.ActionsFor(state);
        var handForge = HandForgePlayer.ActionsFor(state);

        Assert.Equal(baseline, handForge);
    }

    [Fact]
    public void ActionsFor_EveningPhase_IsIdenticalToBaseline()
    {
        var state = GameFactory.NewGame(seed: 8) with
        {
            Day = 8,
            Phase = DayPhase.Evening,
            OpenOreOffers = ImmutableList.Create(new OreOffered(new HeroId(1), "copper", Quantity: 5, UnitPrice: 1)),
        };

        var baseline = BaselinePlayer.ActionsFor(state);
        var handForge = HandForgePlayer.ActionsFor(state);

        Assert.NotEmpty(baseline);
        Assert.Equal(baseline, handForge);
    }

    [Theory]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void ActionsFor_PhasesBaselineNeverCrafts_PassThroughUnchanged(DayPhase phase)
    {
        var state = GameFactory.NewGame(seed: 77) with { Phase = phase };

        var baseline = BaselinePlayer.ActionsFor(state);
        var handForge = HandForgePlayer.ActionsFor(state);

        Assert.Empty(baseline); // D5: BaselinePlayer's own documented behaviour for these phases
        Assert.Equal(baseline, handForge);
    }

    [Fact]
    public void ActionsFor_ExpeditionPhase_NoLegalCraft_MatchesEmptyBaseline()
    {
        // No heroes, no materials: BaselinePlayer's own craft loop (HasBuyer) emits nothing.
        var state = GameFactory.NewGame(seed: 3) with { Day = 1, Phase = DayPhase.Expedition };

        var baseline = BaselinePlayer.ActionsFor(state);
        var handForge = HandForgePlayer.ActionsFor(state);

        Assert.Empty(baseline);
        Assert.Empty(handForge);
    }

    [Fact]
    public void ActionsFor_SameState_TwoRuns_IdenticalSequences()
    {
        var state = ExpeditionStateWithMaterials(day: 30, nextItemId: 88);

        var first = HandForgePlayer.ActionsFor(state);
        var second = HandForgePlayer.ActionsFor(state);

        // NOT a plain Assert.Equal(first, second): ImmutableList<int> (ForgeTraceInput's Samples/
        // Strikes) has no value equality of its own, so two SEPARATELY-BUILT instances with
        // identical content compare unequal under the records' auto-generated Equals — a quirk of
        // ImmutableList<T>, not a determinism bug. Compare content field-by-field instead; the
        // ImmutableList<int> fields themselves DO compare correctly through Assert.Equal's own
        // IEnumerable-aware comparer once handed the lists directly (not nested one Equals call
        // deep inside a record).
        AssertActionsContentEqual(first, second);
    }

    private static void AssertActionsContentEqual(ImmutableList<PlayerAction> expected, ImmutableList<PlayerAction> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            if (expected[i] is CraftAction ec && actual[i] is CraftAction ac)
            {
                Assert.Equal(ec.RecipeId, ac.RecipeId);
                Assert.Equal(ec.MaterialKey, ac.MaterialKey);
                Assert.Equal(ec.PerformanceGrade, ac.PerformanceGrade);
                if (ec.Puzzle is ForgeTraceInput et && ac.Puzzle is ForgeTraceInput at)
                {
                    Assert.Equal(et.Samples, at.Samples);
                    Assert.Equal(et.Strikes, at.Strikes);
                    Assert.Equal(et.PathSeed, at.PathSeed);
                }
                else
                {
                    Assert.Equal(ec.Puzzle is null, ac.Puzzle is null);
                }
            }
            else
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }
    }

    // ---- The hand-forge proof itself: genuinely a ForgeTraceInput, not an assumption ----------

    [Fact]
    public void ActionsFor_ExpeditionPhase_SubmitsAGenuineForgeTraceInput_ForTheSameRecipeBaselineChose()
    {
        var state = ExpeditionStateWithMaterials(day: 12, nextItemId: 40);

        var baseline = BaselinePlayer.ActionsFor(state);
        var handForge = HandForgePlayer.ActionsFor(state);

        var baselineCraft = Assert.Single(baseline.OfType<CraftAction>());
        var firstCraft = Assert.IsType<CraftAction>(handForge[0]);

        Assert.Equal(baselineCraft.RecipeId, firstCraft.RecipeId);
        Assert.Equal(baselineCraft.MaterialKey, firstCraft.MaterialKey);
        Assert.Null(baselineCraft.Puzzle); // baseline never hand-forges — the blind spot this closes

        // Grep/assertion-level proof: the Puzzle really IS a ForgeTraceInput, never merely assumed.
        var trace = Assert.IsType<ForgeTraceInput>(firstCraft.Puzzle);
        Assert.NotEmpty(trace.Samples);
        Assert.NotEmpty(trace.Strikes);
        Assert.Equal(0, trace.Samples.Count % 2); // flat (x,y) pairs, per ForgeTraceInput's contract
        Assert.Equal(0, trace.Strikes.Count % 2);
    }

    [Fact]
    public void ActionsFor_ExpeditionPhase_OneSlotLeft_HandForgesButAddsNoEchoCopy()
    {
        var state = ExpeditionStateWithMaterials(day: 1, nextItemId: 1, slots: 1);

        var actions = HandForgePlayer.ActionsFor(state);

        var only = Assert.Single(actions);
        Assert.IsType<ForgeTraceInput>(Assert.IsType<CraftAction>(only).Puzzle);
    }

    // ---- End-to-end through the real kernel: genuinely SCORED, not just constructed -----------

    [Fact]
    public void HandForgedCraft_ScoresThroughForgeScorer_AndSeedsBatchEchoAt800_MatchingAutoCraftGrade()
    {
        var state = ExpeditionStateWithMaterials(day: 1, nextItemId: 1, slots: 1);
        var actions = HandForgePlayer.ActionsFor(state);

        var result = Kernel.Tick(state, actions);

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal(3, item.CraftSubScores.Count); // ForgeScorer's three zone sub-scores, stamped
        Assert.All(item.CraftSubScores, s => Assert.InRange(s, 0, 1000));

        var echo = result.NewState.Player.BatchEcho;
        Assert.NotNull(echo);
        Assert.Equal(0, echo!.Uses);
        // The "average human forge" design call this unit pins (see HandForgePlayer's class doc):
        // with zero blacksmith talents unlocked, a constant 50-per-mille deviation nets EXACTLY the
        // same 800 per-mille grade QualityRoller's own private AutoCraftGrade constant already uses
        // for the shop's safe auto-craft baseline — a deliberate equivalence, pinned here so a
        // future change to either constant is a visible, reviewed diff.
        Assert.Equal(800, echo.SeedGrade);
    }

    [Fact]
    public void BatchEchoCoverage_SubmitsFollowUpAutoCrafts_AndTheyConsumeTheEcho()
    {
        // Enough slots+materials for the hand-forge plus several identical follow-up copies —
        // the ONLY way a real sweep can ever observe the batch-echo mechanism (see class doc: no
        // existing policy submits more than one craft per day).
        var state = ExpeditionStateWithMaterials(day: 1, nextItemId: 1, copper: 30, slots: 5);
        var actions = HandForgePlayer.ActionsFor(state);

        Assert.True(actions.Count > 1, "fixture assumption: slots+materials allow at least one echo copy");
        Assert.True(actions.Skip(1).All(a => a is CraftAction craft && craft.Puzzle is null),
            "every action after the hand-forge is a plain, puzzle-less echo copy");

        var result = Kernel.Tick(state, actions);

        Assert.Empty(result.Rejected);
        Assert.True(result.NewState.Items.Count > 1, "the hand-forge plus at least one echoed copy minted");
        var echo = result.NewState.Player.BatchEcho;
        Assert.NotNull(echo);
        Assert.True(echo!.Uses > 0, "at least one auto-craft copy consumed the echo");
        Assert.Equal(actions.Count - 1, echo.Uses); // every echo copy submitted legally consumed it
    }
}
