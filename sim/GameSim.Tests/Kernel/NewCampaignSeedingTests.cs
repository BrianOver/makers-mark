using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Kernel;

/// <summary>
/// Covers the Playable Core U4 seeding contract (R4/KD3):
/// <see cref="GameComposition.NewCampaign(ulong, string)"/> selects exactly the chosen
/// profession, seeds <see cref="GameFactory.StarterCopper"/> copper, leaves the starting
/// roster and id counters byte-identical to the default overload, and makes day 1
/// immediately playable — every profession's cheapest tier-1 recipe crafts from starter
/// stock through the COMPOSED kernel at Morning with zero rejections.
/// </summary>
public class NewCampaignSeedingTests
{
    private const ulong Seed = 123;

    /// <summary>The cheapest tier-1 recipe of a profession (fewest materials, id tiebreak).</summary>
    private static Recipe CheapestTier1(string professionId) =>
        ProfessionRegistry.All[professionId].Recipes.Values
            .Where(r => r.Tier == 1)
            .OrderBy(r => r.MaterialQuantity)
            .ThenBy(r => r.RecipeId, StringComparer.Ordinal)
            .First();

    // ---- Happy path -----------------------------------------------------------------------

    [Fact]
    public void Tanning_SelectsProfession_SeedsStarterCopper()
    {
        var state = GameComposition.NewCampaign(Seed, "tanning");

        Assert.Equal(new[] { "tanning" }, state.Player.SelectedProfessions);
        Assert.Equal(GameFactory.StarterCopper, state.Player.Materials["copper"]);
        Assert.Equal(6, state.Player.Materials["copper"]); // pinned literal (R4: ~3 tier-1 crafts)
    }

    [Fact]
    public void Tanning_RosterAndIdCounters_IdenticalToDefaultOverload()
    {
        var chosen = GameComposition.NewCampaign(Seed, "tanning");
        var baseline = GameComposition.NewCampaign(Seed);

        Assert.Equal(baseline.Heroes.Keys, chosen.Heroes.Keys);
        Assert.Equal(
            baseline.Heroes.Values.Select(h => h.Name),
            chosen.Heroes.Values.Select(h => h.Name));
        Assert.Equal(baseline.NextHeroId, chosen.NextHeroId);
        Assert.Equal(baseline.NextItemId, chosen.NextItemId);
        Assert.Equal(baseline.NextEventId, chosen.NextEventId);
    }

    [Fact]
    public void Tanning_DiffersFromDefault_OnlyInPlayerState()
    {
        // The strongest "no drift" pin: swap the player back and the two worlds must
        // serialize byte-identical — profession choice touches NOTHING outside Player.
        var chosen = GameComposition.NewCampaign(Seed, "tanning");
        var baseline = GameComposition.NewCampaign(Seed);

        Assert.Equal(
            SaveCodec.Serialize(baseline),
            SaveCodec.Serialize(chosen with { Player = baseline.Player }));
    }

    // ---- Every profession: seeded AND day-1 playable ----------------------------------------

    [Theory]
    [InlineData("blacksmith")]
    [InlineData("tanning")]
    [InlineData("alchemy")]
    [InlineData("engineering")]
    public void EveryProfession_SeedsSelectionAndStarterStock(string professionId)
    {
        var state = GameComposition.NewCampaign(Seed, professionId);

        Assert.Equal(new[] { professionId }, state.Player.SelectedProfessions);
        Assert.Equal(GameFactory.StarterCopper, state.Player.Materials["copper"]);
    }

    [Theory]
    [InlineData("blacksmith")]
    [InlineData("tanning")]
    [InlineData("alchemy")]
    [InlineData("engineering")]
    public void EveryProfession_CheapestTier1Recipe_CraftsFromStarterStock_AtMorning(string professionId)
    {
        var state = GameComposition.NewCampaign(Seed, professionId);
        Assert.Equal(DayPhase.Morning, state.Phase); // day 1 opens at Morning

        var recipe = CheapestTier1(professionId);
        Assert.True(recipe.MaterialQuantity <= GameFactory.StarterCopper,
            $"{recipe.RecipeId} needs {recipe.MaterialQuantity}x {recipe.MaterialKey} — starter stock too small.");

        var kernel = GameComposition.BuildKernel();
        var result = kernel.Tick(state,
            ImmutableList.Create<PlayerAction>(new CraftAction(recipe.RecipeId, recipe.MaterialKey)));

        Assert.Empty(result.Rejected);
        Assert.Single(result.Events.OfType<ItemCrafted>());
        Assert.Equal(
            GameFactory.StarterCopper - recipe.MaterialQuantity,
            result.NewState.Player.Materials["copper"]);
    }

    // ---- Edge: explicit blacksmith vs the default overload -----------------------------------

    [Fact]
    public void ExplicitBlacksmith_SameSelection_ButStarterCopperDiffersFromDefault()
    {
        var chosen = GameComposition.NewCampaign(Seed, "blacksmith");
        var baseline = GameComposition.NewCampaign(Seed);

        // Selection identical — no double-seed drift.
        Assert.Equal(baseline.Player.SelectedProfessions, chosen.Player.SelectedProfessions);
        Assert.Equal(new[] { "blacksmith" }, chosen.Player.SelectedProfessions);

        // Materials deliberately DIFFER: the chosen path seeds starter copper, the
        // default (compatibility baseline for CLI/replays) starts empty.
        Assert.True(baseline.Player.Materials.IsEmpty);
        Assert.Equal(GameFactory.StarterCopper, chosen.Player.Materials["copper"]);
    }

    // ---- Error ------------------------------------------------------------------------------

    [Fact]
    public void UnregisteredProfession_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => GameComposition.NewCampaign(Seed, "weaving"));
        Assert.Contains("weaving", ex.Message);
    }

    // ---- Determinism regression pin -----------------------------------------------------------

    [Fact]
    public void DefaultOverload_StaysByteIdentical_ForFixedSeed()
    {
        // Two independent builds of the default campaign must serialize identically —
        // adding the profession overload must not perturb the single-arg path (KTD4).
        var a = SaveCodec.Serialize(GameComposition.NewCampaign(4242));
        var b = SaveCodec.Serialize(GameComposition.NewCampaign(4242));
        Assert.Equal(a, b);

        // And it round-trips: deserialize → reserialize is byte-stable.
        Assert.Equal(a, SaveCodec.Serialize(SaveCodec.Deserialize(a)));
    }
}
