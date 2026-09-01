using System.Collections.Immutable;
using System.Reflection;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-MEMORY-03: the beat names its channel. <see cref="ProvenanceQuery"/> is a pure read model
/// over the event log — no mutation, no RNG draw, no wall clock — deriving how an item reached a
/// hero from the already-logged sale/commission/delivery events. No new event, no Contracts edit.
/// </summary>
public class ProvenanceQueryTests
{
    private static GameState WithLog(params GameEvent[] events) =>
        NewWorld() with { EventLog = events.ToImmutableList() };

    [Fact]
    public void Channel_NoQualifyingEvent_IsNull_AndClauseRendersNothing()
    {
        var state = WithLog(); // empty log — item never sold, commissioned, or delivered

        var channel = ProvenanceQuery.Channel(state, new ItemId(10));

        Assert.Null(channel);
        Assert.Equal(string.Empty, ProvenanceQuery.Clause(channel, asOf: 5));
    }

    [Fact]
    public void Channel_Shelf_ReadsFromPlayerShopItemSold()
    {
        var state = WithLog(
            new ItemSold(new ItemId(10), new HeroId(1), Price: 20, FromPlayerShop: true) with { Id = new EventId(1), Day = 3 });

        var channel = ProvenanceQuery.Channel(state, new ItemId(10));

        Assert.Equal(new ItemChannelInfo(ItemChannel.Shelf, 3, new HeroId(1)), channel);
        Assert.Equal(
            "It left your shelf 2 days ago — you never met over it.",
            ProvenanceQuery.Clause(channel, asOf: 5));
    }

    [Fact]
    public void Channel_ShelfIgnoresVendorStock_ItemSoldNotFromPlayerShop()
    {
        // A rival-vendor sale (FromPlayerShop: false) is not one of the four honest channels —
        // it never happened through the player at all.
        var state = WithLog(
            new ItemSold(new ItemId(10), new HeroId(1), Price: 20, FromPlayerShop: false) with { Id = new EventId(1), Day = 3 });

        Assert.Null(ProvenanceQuery.Channel(state, new ItemId(10)));
    }

    [Fact]
    public void Channel_Counter_ReadsUnpinnedCounterSale()
    {
        var state = WithLog(
            new CounterSaleClosed(new HeroId(2), new ItemId(11), Price: 40, Pinned: false) with { Id = new EventId(1), Day = 4 });

        var channel = ProvenanceQuery.Channel(state, new ItemId(11));

        Assert.Equal(ItemChannel.Counter, channel!.Channel);
        Assert.Equal("Haggled off your counter today.", ProvenanceQuery.Clause(channel, asOf: 4));
    }

    [Fact]
    public void Channel_CounterPinned_ReadsPinnedCounterSale()
    {
        var state = WithLog(
            new CounterSaleClosed(new HeroId(2), new ItemId(11), Price: 40, Pinned: true) with { Id = new EventId(1), Day = 4 });

        var channel = ProvenanceQuery.Channel(state, new ItemId(11));

        Assert.Equal(ItemChannel.CounterPinned, channel!.Channel);
        Assert.Equal(
            "You named the fair price at the counter, and it was paid — a day ago.",
            ProvenanceQuery.Clause(channel, asOf: 5));
    }

    [Fact]
    public void Channel_Commission_ReadsCommissionFulfilled()
    {
        var state = WithLog(
            new CommissionFulfilled(new HeroId(3), new ItemId(12), Premium: 15) with { Id = new EventId(1), Day = 2 });

        var channel = ProvenanceQuery.Channel(state, new ItemId(12));

        Assert.Equal(ItemChannel.Commission, channel!.Channel);
        Assert.Equal("Commissioned, and delivered 6 days ago.", ProvenanceQuery.Clause(channel, asOf: 8));
    }

    [Fact]
    public void Channel_Runner_ReadsSupplyDelivered_ClauseCarriesNoDayGap()
    {
        var state = WithLog(
            new SupplyDelivered(new HeroId(4), new ItemId(13), Fee: 5) with { Id = new EventId(1), Day = 1 });

        var channel = ProvenanceQuery.Channel(state, new ItemId(13));

        Assert.Equal(ItemChannel.Runner, channel!.Channel);
        Assert.Equal(
            "You put it in the runner's hands yourself, at the vigil.",
            ProvenanceQuery.Clause(channel, asOf: 99)); // day gap plays no part in this clause
    }

    [Fact]
    public void Channel_PicksTheMostRecentQualifyingEvent()
    {
        // Same item resold twice (e.g. traded back and re-shelved) — the log is chronological
        // (DayLog's own invariant), so the later entry must win.
        var state = WithLog(
            new CounterSaleClosed(new HeroId(1), new ItemId(14), Price: 10, Pinned: false) with { Id = new EventId(1), Day = 1 },
            new ItemSold(new ItemId(14), new HeroId(2), Price: 12, FromPlayerShop: true) with { Id = new EventId(2), Day = 5 });

        var channel = ProvenanceQuery.Channel(state, new ItemId(14));

        Assert.Equal(ItemChannel.Shelf, channel!.Channel);
        Assert.Equal(5, channel.Day);
        Assert.Equal(new HeroId(2), channel.Hero);
    }

    [Fact]
    public void Channel_FiltersByItemId_IgnoringOtherItemsInTheLog()
    {
        var state = WithLog(
            new CounterSaleClosed(new HeroId(1), new ItemId(20), Price: 10, Pinned: false) with { Id = new EventId(1), Day = 1 },
            new CommissionFulfilled(new HeroId(2), new ItemId(21), Premium: 8) with { Id = new EventId(2), Day = 2 });

        Assert.Equal(ItemChannel.Commission, ProvenanceQuery.Channel(state, new ItemId(21))!.Channel);
        Assert.Equal(ItemChannel.Counter, ProvenanceQuery.Channel(state, new ItemId(20))!.Channel);
        Assert.Null(ProvenanceQuery.Channel(state, new ItemId(999)));
    }

    [Fact]
    public void HeirloomClause_ReadsTheStampedLineageAsASentence()
    {
        var item = PlayerItem(30, "Iron Blade", ItemSlot.Weapon, 5, 0) with
        {
            HeirloomLineage = "forged from the Iron Blade of Torvald",
        };

        Assert.Equal("Forged from the Iron Blade of Torvald.", ProvenanceQuery.HeirloomClause(item));
    }

    [Fact]
    public void HeirloomClause_NullForOrdinaryStock()
    {
        var item = PlayerItem(31, "Iron Blade", ItemSlot.Weapon, 5, 0);

        Assert.Null(ProvenanceQuery.HeirloomClause(item));
    }

    [Fact]
    public void ProvenanceQuery_HasNoRngInAnyPublicSignature()
    {
        // Structural purity (KTD2): the derivation draws no RNG because the API takes none — a
        // reflection assertion so a future overload that slipped an rng parameter in would fail
        // the build, mirroring ExpeditionNarratorTests' own tripwire.
        foreach (var method in typeof(ProvenanceQuery).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.False(
                    typeof(IDeterministicRng).IsAssignableFrom(parameter.ParameterType)
                    || parameter.ParameterType.Name.Contains("Rng", System.StringComparison.Ordinal),
                    $"{method.Name} takes an RNG parameter ({parameter.ParameterType.Name}) — ProvenanceQuery must be pure");
            }
        }
    }
}
