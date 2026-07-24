using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// U1 (plan 2026-07-24-003): once-per-Morning systems must fire exactly once per calendar Morning
/// even when a stepped counter session (PA3/PKD5) holds the day at Morning across many ticks.
/// GameKernel re-runs every Morning-phase system on every Morning tick, so before U1 the rent
/// countdown (and recruit/gossip/rival-restock) burned once PER counter step. These tests drive a
/// held Morning through the REAL composed kernel and assert the rent clock advances by exactly one
/// day across the whole session — for BOTH close paths (explicit CloseCounterAction and natural
/// queue-exhaustion), the latter being why CounterQueueSystem must run ahead of the guarded systems.
/// </summary>
public class RentHeldMorningTests
{
    private static int InitialDue => RentState.CadenceDays;

    [Fact]
    public void HeldCounterMorning_ExplicitClose_RentAdvancesExactlyOneDay()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 7301);
        Assert.Equal(InitialDue, state.Rent.DaysUntilDue);

        // Open the counter, then sit in several HELD Morning ticks (empty actions never present an
        // item, so the session stays open and the day holds at Morning), then close explicitly.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase);
        for (var i = 0; i < 3; i++)
        {
            state = kernel.Tick(state, ImmutableList<PlayerAction>.Empty).NewState;
            Assert.Equal(DayPhase.Morning, state.Phase); // still held — session open
        }
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new CloseCounterAction())).NewState;

        // Five Morning ticks total; the rent countdown must have advanced ONE day, not five.
        Assert.Equal(InitialDue - 1, state.Rent.DaysUntilDue);
    }

    [Fact]
    public void HeldCounterMorning_NaturalQueueExhaustion_RentAdvancesExactlyOneDay()
    {
        // One striker + a shelved shield they will refuse (role mismatch) → presenting it walks the
        // only queued customer, exhausting the queue and closing the session from INSIDE
        // CounterQueueSystem this same tick. This is the path that regresses to "systems skip the
        // whole day" unless CounterQueueSystem runs ahead of the guarded systems (U1 reorder).
        var striker = new Hero(
            new HeroId(1), "Striker1", "striker", Level: 1, MaxHp: 25, Gold: 100,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var shield = new Item(
            new ItemId(1), "test-recipe", "Oak Shield", ItemSlot.Shield, QualityGrade.Common,
            new ItemStats(0, 5, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 7302) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, striker),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, shield),
            Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(shield.Id, 15)) },
            // Push id counters past the hand-picked fixtures so RivalRestock/Recruit (which mint on
            // the close tick) allocate non-colliding ids — mirrors the isolation CounterQueueSystemTests notes.
            NextItemId = 100,
            NextHeroId = 100,
        };
        Assert.Equal(InitialDue, state.Rent.DaysUntilDue);

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        Assert.Equal(DayPhase.Morning, state.Phase);

        // Present the shield → the striker walks → queue (size 1) exhausts → session closes this
        // tick and the day advances out of Morning.
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(shield.Id))).NewState;

        // Two Morning ticks (open + present-exhaust); the rent countdown advanced exactly one day —
        // never zero (the reorder guards against skipping) and never two (the guard stops per-tick burn).
        Assert.Equal(InitialDue - 1, state.Rent.DaysUntilDue);
    }
}
