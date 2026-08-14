using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// Covers the day action-slot budget (Game-Feel Plan G3): real-work actions (craft, restock/buy,
/// negotiate — <see cref="ActionBudget.ConsumesSlot"/>) spend one of the day's slots; the kernel's
/// day-boundary reset (<see cref="GameKernel.Tick"/>) is the ONLY place it refills.
/// </summary>
public class ActionBudgetTests
{
    /// <summary>
    /// The nine action types whose handlers actually decrement <c>ActionSlotsRemaining</c>, found by
    /// grepping for that decrement rather than by reading the predicate that is supposed to describe
    /// them. Until 2026-08-14 the predicate named only the first four.
    /// </summary>
    private static readonly Type[] ConsumingTypes =
    [
        typeof(CraftAction), typeof(BuyOreAction), typeof(BuyMaterialAction), typeof(PostBountyAction),
        typeof(ReforgeHeirloomAction), typeof(BuyForgeSupplyAction), typeof(UpgradeForgeAction),
        typeof(MasterworkAttemptAction), typeof(CommissionLegendaryWorkAction),
    ];

    /// <summary>
    /// Every other concrete <see cref="PlayerAction"/> — free moves that never compete for the day's
    /// attention budget. Listed EXPLICITLY rather than inferred as "not consuming", because the
    /// whole point of <see cref="ConsumesSlot_EveryActionTypeIsExplicitlyClassified"/> is that a new
    /// action type must be a deliberate decision, not a silent default.
    /// </summary>
    private static readonly Type[] FreeTypes =
    [
        typeof(StockAction), typeof(SetPriceAction), typeof(UnstockAction), typeof(UnlockTalentAction),
        typeof(SendSupplyAction), typeof(RecallPartyAction), typeof(SetProfessionsAction),
        typeof(OpenCounterAction), typeof(CloseCounterAction), typeof(PresentItemAction),
        typeof(SuggestItemAction), typeof(HaggleResponseAction), typeof(AcceptCommissionAction),
        typeof(DeclineCommissionAction), typeof(HonorMemorialAction),
    ];

    /// <summary>
    /// The tripwire the old "exactly the four" test could not be: reflect over every concrete
    /// <see cref="PlayerAction"/> in the assembly and require each to appear in exactly one of the
    /// two lists above. A tenth action type then fails HERE, by name, instead of defaulting to free
    /// and quietly under-reporting the day's cost — which is precisely how the predicate came to
    /// name four types while nine handlers spent slots.
    ///
    /// <para>Mirrors the advisor's own legality-parity test. Deliberately NOT built by driving all
    /// nine actions through their real handlers: <c>MasterworkAttemptHandlers</c> alone rejects on
    /// eight preconditions before it decrements, so that shape would move the hand-maintained list
    /// into nine fixtures needing current knowledge of nine modules — the same drift risk in a
    /// costlier package.</para>
    /// </summary>
    [Fact]
    public void ConsumesSlot_EveryActionTypeIsExplicitlyClassified()
    {
        var classified = ConsumingTypes.Concat(FreeTypes).ToHashSet();

        var allActionTypes = typeof(PlayerAction).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                        && typeof(PlayerAction).IsAssignableFrom(t))
            .ToList();

        // Fixture-assumption guard: a broken reflection query would make every assertion below pass
        // by having nothing to check — the vacuous-green shape this repo keeps re-finding.
        Assert.True(allActionTypes.Count >= classified.Count,
            $"reflection found only {allActionTypes.Count} PlayerAction subtypes but {classified.Count} are " +
            "classified — the query is broken, not the classification");
        Assert.NotEmpty(ConsumingTypes);
        Assert.NotEmpty(FreeTypes);

        var unclassified = allActionTypes.Where(t => !classified.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(unclassified.Count == 0,
            "every PlayerAction must be explicitly classified as slot-consuming or free in " +
            $"ActionBudgetTests. Unclassified: {string.Join(", ", unclassified)}. If the new action's " +
            "handler decrements ActionSlotsRemaining, add it to ActionBudget.ConsumesSlot AND " +
            "ConsumingTypes; otherwise add it to FreeTypes.");

        var overlap = ConsumingTypes.Intersect(FreeTypes).Select(t => t.Name).ToList();
        Assert.True(overlap.Count == 0, $"classified as both consuming and free: {string.Join(", ", overlap)}");
    }

    [Fact]
    public void ConsumesSlot_AgreesWithTheClassification()
    {
        Assert.True(ActionBudget.ConsumesSlot(new CraftAction("dagger", "copper")));
        Assert.True(ActionBudget.ConsumesSlot(new BuyOreAction(new HeroId(1), "iron", 1)));
        Assert.True(ActionBudget.ConsumesSlot(new BuyMaterialAction("copper", 1)));
        Assert.True(ActionBudget.ConsumesSlot(new PostBountyAction(1, 10)));

        // The five that nine handlers spent slots for while the predicate denied it.
        Assert.True(ActionBudget.ConsumesSlot(new ReforgeHeirloomAction(new ItemId(1), "dagger", "copper")));
        Assert.True(ActionBudget.ConsumesSlot(new BuyForgeSupplyAction("coal", 1)));
        Assert.True(ActionBudget.ConsumesSlot(new UpgradeForgeAction()));
        Assert.True(ActionBudget.ConsumesSlot(new MasterworkAttemptAction("dagger", "copper")));
        Assert.True(ActionBudget.ConsumesSlot(new CommissionLegendaryWorkAction("dagger", "copper")));

        // Free/UI moves never compete for the day's attention budget.
        Assert.False(ActionBudget.ConsumesSlot(new StockAction(new ItemId(1), 10)));
        Assert.False(ActionBudget.ConsumesSlot(new SetPriceAction(new ItemId(1), 10)));
        Assert.False(ActionBudget.ConsumesSlot(new UnstockAction(new ItemId(1))));
        Assert.False(ActionBudget.ConsumesSlot(new UnlockTalentAction("keen-eye", "blacksmith")));
        Assert.False(ActionBudget.ConsumesSlot(new SendSupplyAction(new HeroId(1), new ItemId(1))));
        Assert.False(ActionBudget.ConsumesSlot(new RecallPartyAction(new HeroId(1))));
        Assert.False(ActionBudget.ConsumesSlot(new HonorMemorialAction(new HeroId(1))));
    }

    // ---- Craft: consume on success, gate at zero, existing rejections stay byte-identical -----

    private static readonly GameKernel CraftKernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new CraftingHandlers()));

    private static GameState CraftReadyState(int slotsRemaining) =>
        GameFactory.NewGame(seed: 42) with
        {
            Player = GameFactory.NewGame(seed: 42).Player with { Materials = ImmutableSortedDictionary<string, int>.Empty.Add("copper", 5) },
            ActionSlotsRemaining = slotsRemaining,
        };

    [Fact]
    public void Craft_Success_ConsumesExactlyOneSlot()
    {
        var state = CraftReadyState(ActionBudget.SlotsPerDay);
        var result = CraftKernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        Assert.Empty(result.Rejected);
        Assert.Equal(ActionBudget.SlotsPerDay - 1, result.NewState.ActionSlotsRemaining);
    }

    [Fact]
    public void Craft_ZeroSlotsRemaining_IsRejected_StateUnchanged()
    {
        var state = CraftReadyState(0);
        var result = CraftKernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("No action slots left today (0/5)", rejection.Reason);
        Assert.Empty(result.NewState.Items); // nothing minted
        Assert.Equal(5, result.NewState.Player.Materials["copper"]); // nothing consumed
        Assert.Equal(0, result.NewState.ActionSlotsRemaining); // stays at zero, never negative
    }

    [Fact]
    public void Craft_UnknownRecipe_KeepsItsOwnRejectionReason_EvenAtZeroSlots()
    {
        // The budget gate is checked LAST: an invalid recipe must fail for its OWN reason first,
        // whether or not slots remain — the slot gate must never mask an existing precondition.
        var state = CraftReadyState(0);
        var result = CraftKernel.Tick(state, ImmutableList.Create<PlayerAction>(new CraftAction("no-such-recipe", "copper")));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("Unknown recipe", rejection.Reason);
    }

    [Fact]
    public void UnlockTalent_NeverConsumesASlot_EvenAtZeroRemaining()
    {
        var state = CraftReadyState(0);
        var result = CraftKernel.Tick(state, ImmutableList.Create<PlayerAction>(new UnlockTalentAction("keen-eye", "blacksmith")));

        Assert.Empty(result.Rejected);
        Assert.Equal(0, result.NewState.ActionSlotsRemaining); // untouched — still zero, not negative
        Assert.Contains("keen-eye", result.NewState.Player.TalentsFor("blacksmith"));
    }

    // ---- PostBounty: same gate/consume contract -------------------------------------------

    private static readonly GameKernel BountyKernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new BountyHandlers()));

    [Fact]
    public void PostBounty_Success_ConsumesExactlyOneSlot()
    {
        var state = GameFactory.NewGame(seed: 1) with { ActionSlotsRemaining = ActionBudget.SlotsPerDay };
        var result = BountyKernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(1, 10)));

        Assert.Empty(result.Rejected);
        Assert.Equal(ActionBudget.SlotsPerDay - 1, result.NewState.ActionSlotsRemaining);
    }

    [Fact]
    public void PostBounty_ZeroSlotsRemaining_IsRejected_EscrowUntouched()
    {
        var state = GameFactory.NewGame(seed: 1) with { ActionSlotsRemaining = 0 };
        var beforeGold = state.Player.Gold;

        var result = BountyKernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(1, 10)));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("No action slots left today (0/5)", rejection.Reason);
        Assert.Equal(beforeGold, result.NewState.Player.Gold); // escrow never left the till
        Assert.Empty(result.NewState.Bounties);
    }

    // ---- Kernel day-boundary reset: the ONLY place the budget refills ----------------------

    [Fact]
    public void DayBoundary_Evening_To_Morning_ResetsSlots_EvenIfFullySpent()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 4400) with { ActionSlotsRemaining = 0 };
        Assert.Equal(DayPhase.Morning, state.Phase);

        // Walk the exhausted-budget state through the remaining four phases of THIS day —
        // slots must stay at 0 the whole way; only the Evening->Morning wrap refills it.
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // -> Expedition
        Assert.Equal(0, state.ActionSlotsRemaining);
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // -> Camp
        Assert.Equal(0, state.ActionSlotsRemaining);
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // -> ExpeditionDeep
        Assert.Equal(0, state.ActionSlotsRemaining);
        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // -> Evening
        Assert.Equal(0, state.ActionSlotsRemaining);
        Assert.Equal(1, state.Day);

        state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState; // -> Day 2 Morning
        Assert.Equal(2, state.Day);
        Assert.Equal(DayPhase.Morning, state.Phase);
        Assert.Equal(ActionBudget.SlotsPerDay, state.ActionSlotsRemaining); // refilled
    }

    [Fact]
    public void FreshCampaign_StartsWithAFullBudget()
    {
        var state = GameComposition.NewCampaign(seed: 1);
        Assert.Equal(ActionBudget.SlotsPerDay, state.ActionSlotsRemaining);
    }
}
