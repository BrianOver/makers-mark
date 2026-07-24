using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Wave 5 (U23c) end-to-end through <see cref="CraftingHandlers"/>: a blacksmith craft that carries a
/// <see cref="ForgeTraceInput"/> (Anvil-Map) is scored SIM-SIDE by <see cref="ForgeScorer"/> — the
/// trace's grade drives quality, its three zone sub-scores are stamped on the item (and feed U19
/// signing), and its earned moments mint the item's first History entry ("your craft writes the
/// legends"). Uses the same Tier1Weapon + grade-2-iron uncapped-ceiling setup as
/// <c>ArtifactSigningIntegrationTests</c>, so a perfect trace (grade 1000) lands Masterwork for every
/// jitter roll. The path-as-samples "perfect trace" mirrors <c>ForgeScorerTests</c>.
/// </summary>
public class ForgeTraceCraftTests
{
    private static readonly Recipe Tier1Weapon = ProfessionRegistry.Blacksmith.Recipes.Values
        .First(r => r.Tier == 1 && r.Slot == ItemSlot.Weapon);

    private static readonly ImmutableList<int> PerfectStrikes = ImmutableList.Create(400, 0, 500, 0, 600, 0);

    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState StateWithIron(ulong seed = 42)
    {
        var state = GameFactory.NewGame(seed);
        return state with
        {
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem("iron", 10),
            },
        };
    }

    private static ForgeTraceInput PerfectTrace(int pathSeed)
    {
        var path = ForgePath.Generate(Tier1Weapon.Tier, Tier1Weapon.Slot, Tier1Weapon.BaseStats.Weight, pathSeed);
        return new ForgeTraceInput(path, PerfectStrikes, pathSeed);
    }

    [Fact]
    public void PerfectForgeTrace_ScoresSimSide_StampsThreeSubScores_SignsAndWritesForgeHistory()
    {
        var action = new CraftAction(Tier1Weapon.RecipeId, "iron", Puzzle: PerfectTrace(100));

        var result = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(action));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;

        // Grade + sub-scores came from ForgeScorer, not the (null) captured PerformanceGrade/SubScores.
        Assert.Equal(QualityGrade.Masterwork, item.Quality);
        Assert.Equal(3, item.CraftSubScores.Count);
        Assert.All(item.CraftSubScores, s => Assert.InRange(s, 0, 1000));

        // A perfect forge signs (U19 reads the scorer's sub-scores) ...
        Assert.True(item.IsSigned);
        Assert.Single(result.NewState.EventLog.OfType<ItemSigned>());

        // ... and the forging itself is the item's first History entry (moments earned).
        Assert.Contains(item.History, h => h.Kind == "forged");
    }

    [Fact]
    public void ForgeTraceCraft_IsDeterministic_SameSeedSameTrace_SameQualityAndSubScores()
    {
        var action = new CraftAction(Tier1Weapon.RecipeId, "iron", Puzzle: PerfectTrace(101));

        var a = Kernel.Tick(StateWithIron(seed: 7), ImmutableList.Create<PlayerAction>(action)).NewState.Items.Values.Single();
        var b = Kernel.Tick(StateWithIron(seed: 7), ImmutableList.Create<PlayerAction>(action)).NewState.Items.Values.Single();

        Assert.Equal(a.Quality, b.Quality);
        Assert.Equal(a.CraftSubScores, b.CraftSubScores);
        Assert.Equal(a.SignedName, b.SignedName);
    }

    [Fact]
    public void AutoCraft_NoPuzzle_StillUnsigned_NoForgeHistory_WiringRegression()
    {
        // The null-puzzle auto-craft path must be untouched by the Wave 5 wiring.
        var action = new CraftAction(Tier1Weapon.RecipeId, "iron");

        var result = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(action));

        var item = Assert.Single(result.NewState.Items).Value;
        Assert.False(item.IsSigned);
        Assert.Empty(item.CraftSubScores);
        Assert.DoesNotContain(item.History, h => h.Kind == "forged");
    }
}
