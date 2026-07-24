using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Wave 3 "Commissions" (plan 2026-07-24-003, U14): accept/decline handling
/// (<see cref="CommissionHandlers"/>) and fulfillment/expiry resolution
/// (<see cref="CommissionHandlers.TryFulfillFromShelf"/>, wired into
/// <see cref="HeroShoppingSystem"/>; expiry lives in <see cref="CommissionSystem"/>).
///
/// The CRITICAL asymmetry under test: an ACCEPTED-then-missed commission stings (mood down +
/// <see cref="CommissionExpired"/>); a POSTED-but-never-accepted commission that lapses is SILENT —
/// no event, no mood change — so ignoring the board is always safe.
/// </summary>
public class CommissionFulfillmentTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, int gold, int deepestFloor = 0, int mood = 0) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: deepestFloor, DiedOnDay: null)
    {
        MoodPermille = mood,
    };

    private static Item MakeItem(int id, ItemSlot slot, QualityGrade quality, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, quality,
        new ItemStats(1, 1, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    [Fact]
    public void AcceptedCommission_FulfilledByMatchingShelfItem_SoldAtListPlusPremium_MoodUp_AndRemoved()
    {
        var weapon = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common);
        var hero = MakeHero(1, gold: 100);
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 25)
        {
            Accepted = true,
        };

        var state = GameFactory.NewGame(seed: 1) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(weapon.Id, 20)) },
            Commissions = ImmutableList.Create(commission),
        };

        var sink = new TestSink();
        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), sink);

        var sold = Assert.Single(sink.Events.OfType<ItemSold>());
        Assert.Equal(weapon.Id, sold.Item);
        Assert.Equal(hero.Id, sold.Buyer);
        Assert.Equal(45, sold.Price); // list 20 + premium 25
        Assert.True(sold.FromPlayerShop);

        var fulfilled = Assert.Single(sink.Events.OfType<CommissionFulfilled>());
        Assert.Equal(hero.Id, fulfilled.Hero);
        Assert.Equal(weapon.Id, fulfilled.Item);
        Assert.Equal(25, fulfilled.Premium);

        Assert.Equal(55, after.Heroes[1].Gold); // 100 - 45
        Assert.Equal(45, after.Player.Gold);
        Assert.Empty(after.Player.Shelf);
        Assert.Equal(weapon.Id, after.Heroes[1].Gear.Weapon);
        Assert.Empty(after.Commissions);
        Assert.Equal(CommissionHandlers.FulfillMoodBonus, after.Heroes[1].MoodPermille);
    }

    [Fact]
    public void AcceptedCommission_GuaranteedSale_BypassesVeteranQualityGate()
    {
        // A floor-3+ veteran normally refuses sub-Common work (ShoppingAi.VeteranMinQualityGrade).
        // An accepted commission is a bespoke forge request — it must still go through even for a
        // grade the hero's ordinary shopping AI would categorically reject.
        var poorWeapon = MakeItem(1, ItemSlot.Weapon, QualityGrade.Poor);
        var veteran = MakeHero(1, gold: 100, deepestFloor: 4);
        var commission = new Commission(veteran.Id, ItemSlot.Weapon, QualityGrade.Poor, DeadlineDay: 10, PremiumGold: 10)
        {
            Accepted = true,
        };

        var state = GameFactory.NewGame(seed: 2) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, veteran),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, poorWeapon),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(poorWeapon.Id, 5)) },
            Commissions = ImmutableList.Create(commission),
        };

        var sink = new TestSink();
        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), sink);

        var sold = Assert.Single(sink.Events.OfType<ItemSold>());
        Assert.Equal(poorWeapon.Id, sold.Item);
        Assert.Single(sink.Events.OfType<CommissionFulfilled>());
        Assert.Empty(after.Commissions);
    }

    [Fact]
    public void AcceptedCommission_CannotAffordGuaranteedPrice_NoSale_FallsThroughToOrdinaryShopping()
    {
        var weapon = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common);
        var hero = MakeHero(1, gold: 10); // can't cover list(20) + premium(25) = 45
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 25)
        {
            Accepted = true,
        };

        var state = GameFactory.NewGame(seed: 3) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(weapon.Id, 20)) },
            Commissions = ImmutableList.Create(commission),
        };

        var sink = new TestSink();
        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), sink);

        Assert.Empty(sink.Events.OfType<ItemSold>());
        Assert.Empty(sink.Events.OfType<CommissionFulfilled>());
        Assert.Single(after.Commissions); // still open/accepted, untouched
        Assert.Equal(10, after.Heroes[1].Gold);
    }

    /// <summary>Fully and adequately kitted gear (Common, matching the floor-1 bar a lone hero
    /// musters against) so <see cref="CommissionSystem"/>'s posting half finds no gap and never
    /// re-commissions the same hero in the same tick that its expiry half just cleared them —
    /// isolating the expiry assertion from the (separately tested) posting behavior.</summary>
    private static ImmutableSortedDictionary<int, Item> FullCommonGearCatalog(out GearSet gear)
    {
        var weapon = MakeItem(101, ItemSlot.Weapon, QualityGrade.Common, "Kit Weapon");
        var shield = MakeItem(102, ItemSlot.Shield, QualityGrade.Common, "Kit Shield");
        var armor = MakeItem(103, ItemSlot.Armor, QualityGrade.Common, "Kit Armor");
        gear = new GearSet(weapon.Id, shield.Id, armor.Id);
        return ImmutableSortedDictionary<int, Item>.Empty
            .Add(weapon.Id.Value, weapon).Add(shield.Id.Value, shield).Add(armor.Id.Value, armor);
    }

    [Fact]
    public void AcceptedCommission_PastDeadline_ExpiresWithEventAndMoodPenalty()
    {
        var items = FullCommonGearCatalog(out var gear);
        var hero = MakeHero(1, gold: 100) with { Gear = gear };
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 5, PremiumGold: 25)
        {
            Accepted = true,
        };

        var state = GameFactory.NewGame(seed: 4) with
        {
            Day = 6, // past the deadline
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = items,
            Commissions = ImmutableList.Create(commission),
        };

        var sink = new TestSink();
        var after = new CommissionSystem().Process(state, new Pcg32(state.Rng), sink);

        var expired = Assert.Single(sink.Events.OfType<CommissionExpired>());
        Assert.Equal(hero.Id, expired.Hero);
        Assert.Equal(ItemSlot.Weapon, expired.Slot);
        Assert.Empty(after.Commissions); // expired and NOT re-posted (fully/adequately kitted)
        Assert.Equal(-CommissionSystem.ExpireMoodPenalty, after.Heroes[1].MoodPermille);
    }

    [Fact]
    public void PostedButNeverAccepted_PastDeadline_SilentlyExpires_NoEventNoMoodChange()
    {
        var items = FullCommonGearCatalog(out var gear);
        var hero = MakeHero(1, gold: 100, mood: 42) with { Gear = gear };
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 5, PremiumGold: 25);
        // Accepted defaults to false — posted, never accepted.

        var state = GameFactory.NewGame(seed: 5) with
        {
            Day = 6, // past the deadline
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Items = items,
            Commissions = ImmutableList.Create(commission),
        };

        var sink = new TestSink();
        var after = new CommissionSystem().Process(state, new Pcg32(state.Rng), sink);

        Assert.Empty(sink.Events.OfType<CommissionExpired>());
        Assert.Empty(sink.Events); // no event of any kind for the silent path
        Assert.Empty(after.Commissions); // silently dropped and NOT re-posted (fully/adequately kitted)
        Assert.Equal(42, after.Heroes[1].MoodPermille); // unchanged
    }

    [Fact]
    public void Accept_FlipsAcceptedTrue()
    {
        var hero = new HeroId(1);
        var commission = new Commission(hero, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 25);
        var state = GameFactory.NewGame(seed: 6) with { Commissions = ImmutableList.Create(commission) };

        var (after, rejected) = new CommissionHandlers().Apply(
            state, new AcceptCommissionAction(hero), new Pcg32(state.Rng), new TestSink());

        Assert.Null(rejected);
        Assert.True(Assert.Single(after.Commissions).Accepted);
    }

    [Fact]
    public void Decline_RemovesCommission_NoMoodChange()
    {
        var hero = new HeroId(1);
        var heroObj = MakeHero(1, gold: 100, mood: 10);
        var commission = new Commission(hero, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 25);
        var state = GameFactory.NewGame(seed: 7) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, heroObj),
            Commissions = ImmutableList.Create(commission),
        };

        var (after, rejected) = new CommissionHandlers().Apply(
            state, new DeclineCommissionAction(hero), new Pcg32(state.Rng), new TestSink());

        Assert.Null(rejected);
        Assert.Empty(after.Commissions);
        Assert.Equal(10, after.Heroes[1].MoodPermille);
    }

    [Fact]
    public void Accept_NoOpenCommission_Rejected()
    {
        var state = GameFactory.NewGame(seed: 8);

        var (after, rejected) = new CommissionHandlers().Apply(
            state, new AcceptCommissionAction(new HeroId(1)), new Pcg32(state.Rng), new TestSink());

        Assert.NotNull(rejected);
        Assert.Empty(after.Commissions);
    }

    [Fact]
    public void Decline_NoOpenCommission_Rejected()
    {
        var state = GameFactory.NewGame(seed: 9);

        var (after, rejected) = new CommissionHandlers().Apply(
            state, new DeclineCommissionAction(new HeroId(1)), new Pcg32(state.Rng), new TestSink());

        Assert.NotNull(rejected);
        Assert.Empty(after.Commissions);
    }
}
