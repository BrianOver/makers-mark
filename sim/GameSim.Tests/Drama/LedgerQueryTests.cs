using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// The Evening Ledger read model (R12): per-hero return cards projected purely from
/// the event log — no state changes, callable any number of times.
/// </summary>
public class LedgerQueryTests
{
    private static GameState RevealedDay()
    {
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, 8, 0);
        var state = Equip(NewWorld(), 1, blade);
        var result = Result(
            party: [1, 2], survivors: [1], deaths: [2],
            targetFloor: 2, deepestCleared: 2,
            floors: [new FloorOutcome(2, true, [Combat(2, 2, "Tunnel Spider", taken: 30)])],
            beats: [new AttributionBeat(BeatType.KillingBlow, blade.Id, new HeroId(1), 2, "Fine Iron Blade landed the killing blow on the Tunnel Spider")],
            loot: [new OreLoot(new HeroId(1), "iron", 2)],
            gold: [(1, 16)]);

        return TickEvening(AtEvening(state, result)).NewState;
    }

    [Fact]
    public void ReturnCards_ProjectSurvivorAndDead_InHeroIdOrder()
    {
        var state = RevealedDay();

        var cards = LedgerQuery.ReturnCards(state, day: 1);

        Assert.Equal(2, cards.Count);

        var torvald = cards[0];
        Assert.Equal(new HeroId(1), torvald.Hero);
        Assert.Equal("Torvald", torvald.HeroName);
        Assert.True(torvald.Survived);
        Assert.Equal(2, torvald.FloorReached); // iron ore + record + beat all say floor 2
        Assert.Equal(56, torvald.GoldOnHand);  // 40 starting + 16 loot
        var beat = Assert.Single(torvald.Beats);
        Assert.Equal(BeatType.KillingBlow, beat.Beat);
        var ore = Assert.Single(torvald.OreOffers);
        Assert.Equal("iron", ore.MaterialKey);
        Assert.Equal(2, ore.Quantity);

        var brunhilde = cards[1];
        Assert.Equal(new HeroId(2), brunhilde.Hero);
        Assert.False(brunhilde.Survived);
        Assert.Equal(2, brunhilde.FloorReached); // died on floor 2
        Assert.Empty(brunhilde.Beats);
        Assert.Empty(brunhilde.OreOffers);
    }

    [Fact]
    public void ReturnCards_QuietDay_ProducesNoCards()
    {
        var state = RevealedDay();

        Assert.Empty(LedgerQuery.ReturnCards(state, day: 90));
    }

    [Fact]
    public void ReturnCards_ArePureAndRepeatable()
    {
        var state = RevealedDay();

        var first = LedgerQuery.ReturnCards(state, day: 1);
        var second = LedgerQuery.ReturnCards(state, day: 1);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Hero, second[i].Hero);
            Assert.Equal(first[i].Survived, second[i].Survived);
            Assert.Equal(first[i].FloorReached, second[i].FloorReached);
        }
    }

    [Fact]
    public void MarkTally_CountsLifetimeKillsAndSaves()
    {
        var blade = PlayerItem(10, "Fine Iron Blade", ItemSlot.Weapon, 8, 0);
        blade = blade with
        {
            History = ImmutableList.Create(
                new ItemHistoryEntry(1, "kill", "a"),
                new ItemHistoryEntry(2, "kill", "b"),
                new ItemHistoryEntry(2, "save", "c"),
                new ItemHistoryEntry(3, "bearer", "sold to Torvald")),
        };
        var state = WithItem(NewWorld(), blade);

        Assert.Equal((2, 1), LedgerQuery.MarkTally(state, blade.Id));
        Assert.Equal((0, 0), LedgerQuery.MarkTally(state, new ItemId(999)));
    }

    [Fact]
    public void MarkTally_GrowsAsRevealsAccumulate() // AE1's "lifetime tally increments"
    {
        var state = RevealedDay();
        Assert.Equal((1, 0), LedgerQuery.MarkTally(state, new ItemId(10)));

        var again = Result(
            party: [1], survivors: [1], deaths: [],
            targetFloor: 2, deepestCleared: 2,
            beats: [new AttributionBeat(BeatType.KillingBlow, new ItemId(10), new HeroId(1), 2, "again")]);
        state = TickEvening(AtEvening(state, again)).NewState;

        Assert.Equal((2, 0), LedgerQuery.MarkTally(state, new ItemId(10)));
    }
}
