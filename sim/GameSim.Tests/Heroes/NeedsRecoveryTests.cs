using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;
using GameSim; // GameComposition

namespace GameSim.Tests.Heroes;

/// <summary>
/// B4 recovery narration (regression): the "welcome back to the counter" beat must actually fire
/// when a telegraphed hero returns to the player's shop. The original condition (streak == 0 the day
/// of the purchase) was dead code — every caller reads <see cref="NeedsSystem.Snapshot"/> from the
/// post-Evening-tick state ("tomorrow's Morning"), by which point the purchase is dated
/// <c>Day-1</c> and the streak has already ticked back to 1, so the exact-0 day is never observed.
/// No test asserted the line ever fired, which is why it slipped. These drive Snapshot at explicit
/// days around a controlled player-shop purchase.
/// </summary>
public class NeedsRecoveryTests
{
    private const int HeroId2 = 2; // Torvald — a starting-six hero, alive, arrival day 1.

    private static GameState WithPurchaseOnDay(int purchaseDay, int observeDay)
    {
        var state = GameComposition.NewCampaign(seed: 1);
        var sold = new ItemSold(new ItemId(1), new HeroId(HeroId2), 50, FromPlayerShop: true) { Day = purchaseDay };
        return state with { Day = observeDay, EventLog = ImmutableList.Create<GameEvent>(sold) };
    }

    private static NeedsEntry? EntryFor(GameState state, int heroId) =>
        NeedsSystem.Snapshot(state).FirstOrDefault(e => e.Hero.Value == heroId);

    [Fact]
    public void Recovery_FiresTheMorningAfterAReturn_EndingATelegraphedDrought()
    {
        // Bought on day 8 after a long drought (streak as-of day 7 = 6 >= TelegraphThreshold 4).
        // Observed at day 9 (post-tick "tomorrow's Morning") — the recovery beat must fire.
        var entry = EntryFor(WithPurchaseOnDay(purchaseDay: 8, observeDay: 9), HeroId2);
        Assert.NotNull(entry);
        Assert.True(entry!.RecoveredToday);
    }

    [Fact]
    public void Recovery_DoesNotFireOnThePurchaseDayItself()
    {
        // At day 8 (the purchase day) the post-tick narration hasn't run yet — no beat. The hero
        // bought today (streak 0), isn't telegraphed, and recovery is keyed to the day AFTER.
        var entry = EntryFor(WithPurchaseOnDay(purchaseDay: 8, observeDay: 8), HeroId2);
        Assert.True(entry is null || !entry.RecoveredToday);
    }

    [Fact]
    public void Recovery_IsAOneShot_NotRepeatedOnLaterDays()
    {
        var entry = EntryFor(WithPurchaseOnDay(purchaseDay: 8, observeDay: 10), HeroId2);
        Assert.True(entry is null || !entry.RecoveredToday);
    }

    [Fact]
    public void Recovery_DoesNotFireForAReturnThatWasNeverTelegraphed()
    {
        // Bought on day 3 — the streak the day before (as-of day 2 = 1) never reached the telegraph
        // window, so returning is unremarkable: no "welcome back" for a hero we never warned about.
        var entry = EntryFor(WithPurchaseOnDay(purchaseDay: 3, observeDay: 4), HeroId2);
        Assert.True(entry is null || !entry.RecoveredToday);
    }
}
