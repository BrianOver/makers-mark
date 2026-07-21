using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Professions;

/// <summary>
/// PA2/PKD2 byte-identical regression pin: alchemy, engineering, and tanning are PASSIVE
/// professions and must stay wired to the untouched <see cref="QualityRoller.Roll"/> threshold
/// table — blacksmith is the ONLY profession PA2 flips to the active dominance model. Each case
/// crafts through the real, full pipeline (<see cref="CraftingHandlers"/> via the kernel, exactly
/// as the game does) with a fixed seed and pins the resulting item's quality, stats, and effect
/// magnitude against golden values captured from this exact code path. If a future change to the
/// passive roll, the shared quality math, or these professions' data ever moves these numbers,
/// this test fails — which is the point: passive professions are byte-identical, full stop.
/// </summary>
public class PassiveQualityRegressionTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static Item CraftOne(string profession, string recipeId, string materialKey, int qty, ulong seed)
    {
        var state = GameFactory.NewGame(seed);
        state = state with
        {
            Player = state.Player with
            {
                SelectedProfessions = ImmutableSortedSet.Create(profession),
                Materials = state.Player.Materials.SetItem(materialKey, qty),
            },
        };

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction(recipeId, materialKey)));
        Assert.Empty(result.Rejected);
        return Assert.Single(result.NewState.Items).Value;
    }

    [Fact]
    public void Alchemy_FixedSeedCraft_MatchesGoldenItem_UnchangedByPA2()
    {
        var item = CraftOne("alchemy", "alchemy-minor-elixir", "copper", qty: 2, seed: 4242);

        Assert.Equal(QualityGrade.Superior, item.Quality);
        Assert.Equal(new ItemStats(Attack: 0, Defense: 0, Weight: 0), item.Stats);
        Assert.NotNull(item.Effect);
        Assert.Equal(ConsumableKind.Heal, item.Effect!.Kind);
        Assert.Equal(8, item.Effect.Magnitude); // base 6 * Superior 135% = 8 (integer division)
        Assert.Empty(item.CraftSubScores); // no minigame on a passive profession
    }

    [Fact]
    public void Engineering_FixedSeedCraft_MatchesGoldenItem_UnchangedByPA2()
    {
        var item = CraftOne("engineering", "engineering-bolt-thrower", "copper", qty: 2, seed: 4242);

        Assert.Equal(QualityGrade.Superior, item.Quality);
        Assert.Equal(new ItemStats(Attack: 10, Defense: 0, Weight: 2), item.Stats); // base 8 attack * 135% = 10
        Assert.Null(item.Effect);
        Assert.Empty(item.CraftSubScores);
    }

    [Fact]
    public void Tanning_FixedSeedCraft_MatchesGoldenItem_UnchangedByPA2()
    {
        var item = CraftOne("tanning", "tanning-hide-jerkin", "copper", qty: 3, seed: 4242);

        Assert.Equal(QualityGrade.Superior, item.Quality);
        Assert.Equal(new ItemStats(Attack: 0, Defense: 9, Weight: 3), item.Stats); // base 7 defense * 135% = 9
        Assert.Null(item.Effect);
        Assert.Empty(item.CraftSubScores);
    }

    [Fact]
    public void PassiveProfessions_StillRouteThrough_TheUntouchedPassiveRoll_NeverActive()
    {
        // Structural half of the pin: PA2 must flip ONLY the blacksmith to the active model.
        Assert.False(AlchemyProfession.Definition.ActiveCraft);
        Assert.False(EngineeringProfession.Definition.ActiveCraft);
        Assert.False(TanningProfession.Definition.ActiveCraft);
        Assert.Empty(AlchemyProfession.Definition.MinigameAssists);
        Assert.Empty(EngineeringProfession.Definition.MinigameAssists);
        Assert.Empty(TanningProfession.Definition.MinigameAssists);
    }
}
