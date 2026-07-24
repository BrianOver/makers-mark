using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Crafting;

/// <summary>
/// Wave 4 (U19, "Signed Works") end-to-end through <see cref="CraftingHandlers"/>: a craft whose
/// captured performance is excellent enough mints a Signed Work and emits
/// <see cref="ItemSigned"/>; an ordinary craft stays unsigned. Uses the same Tier1Weapon +
/// grade-2-material-uncaps-the-ceiling setup as <c>ActiveQualityModelTests</c>: a maxed
/// <c>PerformanceGrade</c> (1000) against an uncapped ceiling lands in the Masterwork band for
/// EVERY possible jitter roll (975..1025, band floor 930) — deterministic regardless of the real
/// RNG stream, so this test needs no fixed-roll seam.
/// </summary>
public class ArtifactSigningIntegrationTests
{
    private static readonly Recipe Tier1Weapon = ProfessionRegistry.Blacksmith.Recipes.Values
        .First(r => r.Tier == 1 && r.Slot == ItemSlot.Weapon);

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
                Materials = state.Player.Materials.SetItem("iron", 10), // grade 2 vs. Tier1's grade 1 -> uncapped
            },
        };
    }

    [Fact]
    public void ExcellentActiveCraft_MastersworkWithHighSubScores_SignsTheWork_AndEmitsItemSigned()
    {
        var action = new CraftAction(
            Tier1Weapon.RecipeId, "iron", PerformanceGrade: 1000,
            SubScores: ImmutableList.Create(950, 960, 999));

        var result = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(action));

        Assert.Empty(result.Rejected);
        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal(QualityGrade.Masterwork, item.Quality);
        Assert.True(item.IsSigned);
        Assert.False(string.IsNullOrWhiteSpace(item.SignedName));

        var signed = Assert.Single(result.NewState.EventLog.OfType<ItemSigned>());
        Assert.Equal(item.Id, signed.Item);
        Assert.Equal(item.SignedName, signed.SignedName);

        // ItemCrafted still fires first, exactly once — the signing is additive, not a replacement.
        Assert.Single(result.NewState.EventLog.OfType<ItemCrafted>());
    }

    [Fact]
    public void ExcellentActiveCraft_OneSubScoreBelowThreshold_StaysUnsigned_NoItemSignedEvent()
    {
        var action = new CraftAction(
            Tier1Weapon.RecipeId, "iron", PerformanceGrade: 1000,
            SubScores: ImmutableList.Create(950, 949, 999)); // one point under ArtifactSigning.SubScoreThreshold

        var result = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(action));

        var item = Assert.Single(result.NewState.Items).Value;
        Assert.Equal(QualityGrade.Masterwork, item.Quality); // still an excellent craft...
        Assert.False(item.IsSigned);                          // ...but not a Signed Work
        Assert.Null(item.SignedName);
        Assert.Empty(result.NewState.EventLog.OfType<ItemSigned>());
    }

    [Fact]
    public void OrdinaryAutoCraft_NoSubScores_NeverSigns()
    {
        // No PerformanceGrade, no SubScores — the auto-craft path (competent-but-capped at
        // Superior, PKD4) can never reach Masterwork and never carries sub-scores either way.
        var action = new CraftAction(Tier1Weapon.RecipeId, "iron");

        var result = Kernel.Tick(StateWithIron(), ImmutableList.Create<PlayerAction>(action));

        var item = Assert.Single(result.NewState.Items).Value;
        Assert.False(item.IsSigned);
        Assert.Empty(result.NewState.EventLog.OfType<ItemSigned>());
    }

    [Fact]
    public void SigningProc_IsDeterministic_SameSeedSameInputs_SameLegendName()
    {
        var action = new CraftAction(
            Tier1Weapon.RecipeId, "iron", PerformanceGrade: 1000,
            SubScores: ImmutableList.Create(950, 960, 999));

        var a = Kernel.Tick(StateWithIron(seed: 7), ImmutableList.Create<PlayerAction>(action));
        var b = Kernel.Tick(StateWithIron(seed: 7), ImmutableList.Create<PlayerAction>(action));

        Assert.Equal(a.NewState.Items.Values.Single().SignedName, b.NewState.Items.Values.Single().SignedName);
    }
}
