using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Professions;

namespace GameSim.Tests.Advisor;

/// <summary>
/// Kernel-parity property test (plan 2026-07-19-002 U10): every action
/// <see cref="ActionLegality.LegalActions"/> reports legal, replayed through the real kernel in
/// isolation, yields zero <see cref="RejectedAction"/>. Drives a full 100-day seeded run with the
/// shared <see cref="BaselinePlayer"/> policy (same harness precedent as the Balance gate) so the
/// property is checked across a wide, evolving cross-section of game states — the standing drift
/// tripwire the plan calls for: any future handler guard that ActionLegality fails to mirror shows
/// up here as a false "legal" that the kernel actually rejects.
/// </summary>
public class ActionLegalityTests
{
    private const int Days = 100;
    private const ulong Seed = 4242;

    [Fact]
    public void EveryLegalAction_ReplayedThroughKernel_IsNeverRejected()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed);
        var checkedAny = false;

        for (var tick = 0; tick < Days * 5; tick++)
        {
            foreach (var candidate in ActionLegality.LegalActions(state, state.Phase))
            {
                checkedAny = true;
                var probe = kernel.Tick(state, ImmutableList.Create(candidate));
                Assert.True(probe.Rejected.IsEmpty,
                    $"Day {state.Day} phase {state.Phase}: LegalActions reported {candidate} legal, " +
                    $"but the kernel rejected it: {string.Join("; ", probe.Rejected.Select(r => r.Reason))}");
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
        }

        Assert.True(checkedAny, "The 100-day run never produced a single LegalActions candidate — the test is vacuous.");
    }

    [Theory]
    [InlineData(DayPhase.Morning)]
    [InlineData(DayPhase.Expedition)]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void BuyOreAction_OutsideEvening_IsIllegal_AndKernelRejectsIt(DayPhase wrongPhase)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed) with
        {
            Phase = wrongPhase,
            OpenOreOffers = ImmutableList.Create(new OreOffered(new HeroId(1), "copper", 5, 3)),
        };
        var heroState = state.Heroes[1] with { Alive = true };
        state = state with { Heroes = state.Heroes.SetItem(1, heroState), Player = state.Player with { Gold = 1000 } };

        var action = new BuyOreAction(new HeroId(1), "copper", 5);

        Assert.False(ActionLegality.IsLegal(state, action, wrongPhase));

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));
        Assert.Single(result.Rejected);
    }

    [Theory]
    [InlineData(DayPhase.Expedition)]
    [InlineData(DayPhase.Evening)]
    [InlineData(DayPhase.Camp)]
    [InlineData(DayPhase.ExpeditionDeep)]
    public void BuyMaterialAction_OutsideMorning_IsIllegal_AndKernelRejectsIt(DayPhase wrongPhase)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(Seed) with
        {
            Phase = wrongPhase,
            Player = GameComposition.NewCampaign(Seed).Player with { Gold = 1000 },
        };

        var action = new BuyMaterialAction("copper", 1);

        Assert.False(ActionLegality.IsLegal(state, action, wrongPhase));

        var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(action));
        Assert.Single(result.Rejected);
    }

    [Fact]
    public void CraftAction_UnknownMaterialKey_IsIllegal()
    {
        var state = GameComposition.NewCampaign(Seed);
        var recipe = ProfessionRegistry.AllRecipes.Values.First(r => r.Tier == 1);
        var action = new CraftAction(recipe.RecipeId, "not-a-real-material");

        Assert.False(ActionLegality.IsLegal(state, action, state.Phase));
    }

    [Fact]
    public void BuyMaterialAction_UnaffordableQuantity_IsIllegal()
    {
        var state = GameComposition.NewCampaign(Seed) with
        {
            Phase = DayPhase.Morning,
            Player = GameComposition.NewCampaign(Seed).Player with { Gold = 0 },
        };

        var action = new BuyMaterialAction("copper", 1000);

        Assert.False(ActionLegality.IsLegal(state, action, DayPhase.Morning));
    }
}
