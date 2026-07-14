using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers R16 end-to-end: player storefront vs rival baseline through the REAL kernel
/// composition — <see cref="RivalRestockSystem"/> registered BEFORE
/// <see cref="HeroShoppingSystem"/> (heroes must see a stocked rival shelf), real
/// starting roster from <see cref="HeroRoster"/>. These tests pin the composition
/// order as a contract: swapping it changes every Morning outcome.
/// </summary>
public class HeroShopChoiceTests
{
    /// <summary>The U7 Morning composition: restock first, then shopping.</summary>
    private static GameKernel EconomyKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new RivalRestockSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new ShopHandlers(), new OreMarketHandlers()));

    /// <summary>A player-crafted longsword; attack picked per quality (Fine 23, Masterwork 32).</summary>
    private static Item PlayerLongsword(int id, QualityGrade quality, int attack) => new(
        new ItemId(id), "longsword", "Longsword", ItemSlot.Weapon, quality,
        new ItemStats(attack, Defense: 0, Weight: 5),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState NewTown(Item playerItem) =>
        HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 5)) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(playerItem.Id.Value, playerItem),
            NextItemId = playerItem.Id.Value + 1,
        };

    [Fact]
    public void PlayerItem_BeatsRival_WhenQualityPerGoldWins()
    {
        // Masterwork longsword (+32 gear score) at 30g crushes every rival line's
        // gain-per-gold — Torvald (H1, shops first) must pick it over the whole
        // freshly restocked rival shelf.
        var sword = PlayerLongsword(100, QualityGrade.Masterwork, attack: 32);
        var kernel = EconomyKernel();

        var tick = kernel.Tick(NewTown(sword), ImmutableList.Create<PlayerAction>(
            new StockAction(sword.Id, 30)));

        Assert.Empty(tick.Rejected);
        var playerSale = Assert.Single(tick.Events.OfType<ItemSold>(), e => e.FromPlayerShop);
        Assert.Equal(sword.Id, playerSale.Item);
        Assert.Equal(new HeroId(1), playerSale.Buyer);
        Assert.Equal(30, playerSale.Price);
        Assert.Equal(GameFactory.StartingPlayerGold + 30, tick.NewState.Player.Gold);
        Assert.Equal(sword.Id, tick.NewState.Heroes[1].Gear.Weapon);
        Assert.Empty(tick.NewState.Player.Shelf);
    }

    [Fact]
    public void OverpricedPlayerItem_LosesToRival_ThenPriceCutFlipsNextMorning()
    {
        // Day 1 Morning: Fine longsword at 200g — nobody can afford it; the rival
        // takes the whole morning. Day 1 Evening: price cut to 10g. Day 2 Morning:
        // Kael (H3, 19g left, empty weapon slot) buys it — the cut flipped the choice.
        var sword = PlayerLongsword(100, QualityGrade.Fine, attack: 23);
        var kernel = EconomyKernel();

        // Day 1 Morning: stock at 200, rival restocks, heroes shop.
        var morning1 = kernel.Tick(NewTown(sword), ImmutableList.Create<PlayerAction>(
            new StockAction(sword.Id, 200)));
        Assert.Empty(morning1.Rejected);
        Assert.DoesNotContain(morning1.Events.OfType<ItemSold>(), e => e.FromPlayerShop);
        Assert.NotEmpty(morning1.Events.OfType<ItemSold>()); // rival won the morning
        Assert.Contains(morning1.NewState.Player.Shelf, e => e.Item == sword.Id);

        // Every hero that looked and passed left a legible reason on the player item (R8).
        Assert.Contains(morning1.Events.OfType<HeroPassedOnItem>(), p => p.Item == sword.Id);

        // Day 1 Expedition: nothing.
        var expedition1 = kernel.Tick(morning1.NewState, ImmutableList<PlayerAction>.Empty);

        // Day 1 Evening: cut the price.
        var evening1 = kernel.Tick(expedition1.NewState, ImmutableList.Create<PlayerAction>(
            new SetPriceAction(sword.Id, 10)));
        Assert.Empty(evening1.Rejected);

        // Day 2 Morning: restock refills sold rival slots, then the flip happens.
        var morning2 = kernel.Tick(evening1.NewState, ImmutableList<PlayerAction>.Empty);
        var flip = Assert.Single(morning2.Events.OfType<ItemSold>(), e => e.FromPlayerShop);
        Assert.Equal(sword.Id, flip.Item);
        Assert.Equal(new HeroId(3), flip.Buyer); // Kael: 19g left, +23 gear score for 10g
        Assert.Equal(10, flip.Price);
        Assert.Empty(morning2.NewState.Player.Shelf);
        Assert.Equal(sword.Id, morning2.NewState.Heroes[3].Gear.Weapon);
    }

    [Fact]
    public void RestockBeforeShopping_HeroesBuyFromRival_OnDayOneMorning()
    {
        // No player stock at all: with restock composed FIRST, day-1 heroes still walk
        // into a stocked rival shop. If the order were flipped, morning 1 would be a
        // ghost town (empty shelves) — this test pins the composition contract.
        var kernel = EconomyKernel();
        var start = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 5));

        var tick = kernel.Tick(start, ImmutableList<PlayerAction>.Empty);

        var sales = tick.Events.OfType<ItemSold>().ToList();
        Assert.NotEmpty(sales);
        Assert.All(sales, s => Assert.False(s.FromPlayerShop));
        Assert.Equal(GameFactory.StartingPlayerGold, tick.NewState.Player.Gold); // rival sales never credit the player
    }
}
