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

    private static Hero MakeHero(int id, int gold, int deepestFloor = 0, int mood = 0, bool alive = true) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: deepestFloor, DiedOnDay: alive ? null : 1)
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
    /// <summary>The id of the heal this catalog stocks — put it in <see cref="Hero.Pack"/> via
    /// <see cref="FullyProvisioned"/> to make a hero read as wanting nothing.</summary>
    private const int KitHealId = 104;

    /// <summary>
    /// A hero kitted AND supplied — the state that now means "no commission wanted".
    ///
    /// <para>Commissions gained Consumable and Trinket as askable slots, so worn gear alone no longer
    /// implies a hero wants nothing: one carrying no potion is asked for a potion. Tests whose premise
    /// is "adequately kitted, so nothing is re-posted" therefore have to provision the pack too, or
    /// they are asserting against a hero the sim now (correctly) considers under-supplied.</para>
    /// </summary>
    private static Hero FullyProvisioned(Hero hero, GearSet gear) =>
        hero with { Gear = gear, Pack = ImmutableList.Create(new ItemId(KitHealId)) };

    private static ImmutableSortedDictionary<int, Item> FullCommonGearCatalog(out GearSet gear)
    {
        var weapon = MakeItem(101, ItemSlot.Weapon, QualityGrade.Common, "Kit Weapon");
        var shield = MakeItem(102, ItemSlot.Shield, QualityGrade.Common, "Kit Shield");
        var armor = MakeItem(103, ItemSlot.Armor, QualityGrade.Common, "Kit Armor");
        var heal = MakeItem(KitHealId, ItemSlot.Consumable, QualityGrade.Common, "Kit Draught")
            with { Effect = new ConsumableEffect(ConsumableKind.Heal, 10) };
        gear = new GearSet(weapon.Id, shield.Id, armor.Id);
        return ImmutableSortedDictionary<int, Item>.Empty
            .Add(weapon.Id.Value, weapon).Add(shield.Id.Value, shield).Add(armor.Id.Value, armor)
            .Add(heal.Id.Value, heal);
    }

    [Fact]
    public void AcceptedCommission_PastDeadline_ExpiresWithEventAndMoodPenalty()
    {
        var items = FullCommonGearCatalog(out var gear);
        var hero = FullyProvisioned(MakeHero(1, gold: 100), gear);
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
        var hero = FullyProvisioned(MakeHero(1, gold: 100, mood: 42), gear);
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

    /// <summary>
    /// T10 U48: the bug shape being fixed. An ACCEPTED commission whose hero has since died must NOT
    /// dock mood or fire <see cref="CommissionExpired"/> when the deadline arrives — a dead hero
    /// cannot give up waiting. Before the fix, ExpireCommissions never checked <see
    /// cref="Hero.Alive"/>, so this rendered as "{Hero} gave up waiting on that Weapon commission."
    /// </summary>
    [Fact]
    public void AcceptedCommission_HeroDiedBeforeDeadline_ExpiresSilently_NoEventNoMoodChange()
    {
        var hero = MakeHero(1, gold: 100, mood: 17, alive: false);
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 5, PremiumGold: 25)
        {
            Accepted = true,
        };

        var state = GameFactory.NewGame(seed: 6) with
        {
            Day = 6, // past the deadline
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Commissions = ImmutableList.Create(commission),
        };

        var sink = new TestSink();
        var after = new CommissionSystem().Process(state, new Pcg32(state.Rng), sink);

        Assert.Empty(sink.Events.OfType<CommissionExpired>());
        Assert.Empty(sink.Events); // no event of any kind — a dead hero cannot give up waiting
        Assert.Empty(after.Commissions); // voided, not re-posted (PostCommissions also skips the dead)
        Assert.Equal(17, after.Heroes[1].MoodPermille); // unchanged — no penalty on a corpse
    }

    /// <summary>
    /// The other half: a dead hero's commission is voided the MOMENT the hero is found dead, not
    /// merely at the deadline — checked BEFORE <see cref="Commission.DeadlineDay"/>, so it never
    /// lingers in <see cref="GameState.Commissions"/> for the rest of the deadline window.
    /// </summary>
    [Fact]
    public void Commission_HeroDiedWellBeforeDeadline_VoidedImmediately_NotLeftLingering()
    {
        var hero = MakeHero(1, gold: 100, alive: false);
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 30, PremiumGold: 25)
        {
            Accepted = true,
        };

        var state = GameFactory.NewGame(seed: 7) with
        {
            Day = 2, // days away from the deadline — the old code would have kept this
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            Commissions = ImmutableList.Create(commission),
        };

        var sink = new TestSink();
        var after = new CommissionSystem().Process(state, new Pcg32(state.Rng), sink);

        Assert.Empty(sink.Events);
        Assert.Empty(after.Commissions); // gone now, not parked until day 30
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
