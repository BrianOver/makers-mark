using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Phase C U-C1 slice 2: the player composes craft modifiers at the forge via
/// <see cref="CraftAction.RequestQuenchOil"/>/<see cref="CraftAction.RequestRune"/>/
/// <see cref="CraftAction.RequestFitting"/>, and <see cref="CraftingHandlers"/> stamps them onto the
/// crafted item, gated by the finished grade's slot count + the material tier cap. This is what makes
/// the slice-1 modifier effects actually reachable in play (they were dormant without a way to attach
/// them). The rolled quality grade is seed-dependent, so these assert RELATIVE to the grade the craft
/// actually produced rather than pinning a specific grade.
/// </summary>
public class CraftModifierCompositionTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState StateWith(params (string Key, int Qty)[] materials)
    {
        var state = GameFactory.NewGame(seed: 42);
        var stores = state.Player.Materials;
        foreach (var (key, qty) in materials)
        {
            stores = stores.SetItem(key, qty);
        }

        return state with { Player = state.Player with { Materials = stores } };
    }

    private static Item CraftWithRequests(GameState state, string material, string? oil, string? rune, string? fitting)
    {
        var action = new CraftAction("dagger", material, RequestQuenchOil: oil, RequestRune: rune, RequestFitting: fitting);
        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));
        Assert.Empty(result.Rejected);
        return result.NewState.Items.Values.Single();
    }

    [Fact]
    public void AllThreeRequested_StampsExactlyTheGradesSlotCount_InFamilyOrder()
    {
        var item = CraftWithRequests(StateWith(("copper", 5)), "copper",
            CraftModifiers.CowardsOil, CraftModifiers.LeechRune, CraftModifiers.LodestoneFitting);

        var slots = CraftModifiers.SlotsForGrade(item.Quality);
        var stamped = item.Modifiers.ToList();
        Assert.Equal(System.Math.Min(3, slots), stamped.Count);

        // Requests fill in family order: oil, then rune, then fitting.
        var expectedFamilies = new[] { ModifierFamily.QuenchOil, ModifierFamily.Rune, ModifierFamily.Fitting }
            .Take(System.Math.Min(3, slots)).ToArray();
        Assert.Equal(expectedFamilies, stamped.Select(m => m.Family).ToArray());
    }

    [Fact]
    public void UnknownModifierId_IsSilentlyDropped_CraftStillSucceeds()
    {
        var item = CraftWithRequests(StateWith(("copper", 5)), "copper", "no-such-oil", null, null);
        Assert.Null(item.QuenchOil);
        Assert.DoesNotContain(item.Modifiers, m => m.Id == "no-such-oil");
    }

    [Fact]
    public void MaterialTierCapsModifierTier_IronOne_MithrilTwo()
    {
        var iron = CraftWithRequests(StateWith(("iron", 5)), "iron", null, CraftModifiers.LeechRune, null);
        var mith = CraftWithRequests(StateWith(("mithril", 5)), "mithril", null, CraftModifiers.LeechRune, null);

        // Only assert tier when the grade actually held the modifier (Poor grade holds none).
        if (iron.Rune is { } ironRune)
        {
            Assert.True(ironRune.Tier <= 1 || iron.Quality == QualityGrade.Masterwork); // iron caps T1 (+1 overshoot only on masterwork)
        }

        if (mith.Rune is { } mithRune)
        {
            Assert.True(mithRune.Tier >= 2); // mithril allows T2
        }
    }

    [Fact]
    public void NoRequests_LeavesEverySlotNull_BackwardCompatible()
    {
        var item = CraftWithRequests(StateWith(("copper", 5)), "copper", null, null, null);
        Assert.Null(item.QuenchOil);
        Assert.Null(item.Rune);
        Assert.Null(item.Fitting);
        Assert.Empty(item.Modifiers);
    }
}
