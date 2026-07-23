using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Professions;

/// <summary>
/// PA2/PKD2 byte-identical regression pin, amended by Phase B: engineering and tanning are the
/// remaining PASSIVE professions and must stay wired to the untouched
/// <see cref="QualityRoller.Roll"/> threshold table. Blacksmith (PA2) and now ALCHEMY (Phase B,
/// the reagent-puzzle profession) are the two ACTIVE professions — alchemy's fixed-seed case
/// below pins its new auto-craft golden (null grade + null puzzle → the competent-but-capped
/// <see cref="QualityRoller.RollActive"/> path), the deliberate re-pin this flip requires. Each
/// case crafts through the real, full pipeline (<see cref="CraftingHandlers"/> via the kernel,
/// exactly as the game does) with a fixed seed and pins the resulting item's quality, stats, and
/// effect magnitude against golden values captured from this exact code path. If a future change
/// to either roll, the shared quality math, or these professions' data ever moves these numbers,
/// this test fails — which is the point: the passive professions stay byte-identical, full stop,
/// and the active auto-craft baseline is pinned too.
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
    public void Alchemy_FixedSeedAutoCraft_RoutesActive_MatchesGoldenItem()
    {
        // Phase B flipped alchemy ACTIVE: a puzzle-less craft is now the auto-craft path
        // (RollActive, grade 550 + jittered by the same single Roll100 draw the passive roll
        // used — the draw COUNT is unchanged, so no other module's stream moves). Golden
        // captured from this exact code path at the flip.
        var item = CraftOne("alchemy", "alchemy-minor-elixir", "copper", qty: 2, seed: 4242);

        Assert.Equal(QualityGrade.Fine, item.Quality);
        Assert.Equal(new ItemStats(Attack: 0, Defense: 0, Weight: 0), item.Stats);
        Assert.NotNull(item.Effect);
        Assert.Equal(ConsumableKind.Heal, item.Effect!.Kind);
        Assert.Equal(6, item.Effect.Magnitude); // base 6 * ItemForge's Fine percent = 6 (integer division)
        Assert.Empty(item.CraftSubScores); // auto-craft carries no puzzle sub-scores
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
        // Structural half of the pin, Phase B edition: blacksmith (PA2) and alchemy (Phase B)
        // are the two active professions; engineering and tanning stay PASSIVE — their pins
        // here are deliberately unweakened.
        Assert.True(AlchemyProfession.Definition.ActiveCraft);
        Assert.NotEmpty(AlchemyProfession.Definition.MinigameAssists);
        Assert.Empty(AlchemyProfession.Definition.Quality.FlatShifts);  // PKD3 double-count fix
        Assert.Empty(AlchemyProfession.Definition.Quality.SlotShifts);

        Assert.False(EngineeringProfession.Definition.ActiveCraft);
        Assert.False(TanningProfession.Definition.ActiveCraft);
        Assert.Empty(EngineeringProfession.Definition.MinigameAssists);
        Assert.Empty(TanningProfession.Definition.MinigameAssists);
    }
}
