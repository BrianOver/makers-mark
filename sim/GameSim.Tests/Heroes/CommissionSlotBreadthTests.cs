using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Commissions can now ask for Consumable and Trinket, not just Weapon/Shield/Armor.
///
/// <para><b>Why that mattered enough to change:</b> a commission is this game's only NARRATIVE demand
/// signal — a named hero asking the player personally, with a premium, a deadline, a mood consequence
/// and an attribution beat. Consumables and trinkets could always be SOLD off the shelf (a five-run
/// playtest confirmed it: "Your Field Poultice sold"), but they could never be the subject of a
/// commission. So 13 of 39 recipes — most of the Alchemist's book — were shut out of the story path in
/// a project whose thesis is "your craft writes the legends".</para>
///
/// <para>These tests pin the three properties that make the widening safe: the new asks actually
/// appear, they appear in survival-first order, and — the one that would have been a silent
/// money-loss bug — a fulfilled consumable reaches the hero's PACK rather than vanishing into a gear
/// slot that does not exist.</para>
/// </summary>
public class CommissionSlotBreadthTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    [Fact]
    public void KittedButUnsuppliedHero_IsAskedForAConsumable()
    {
        var (items, gear) = FullGear();
        var hero = Hero1(gear); // pack empty — carries no heal
        var state = World(hero, items);

        var (_, events) = Run(state);

        var posted = Assert.Single(events.OfType<CommissionPosted>());
        Assert.Equal(ItemSlot.Consumable, posted.Slot);
    }

    [Fact]
    public void SuppliedHero_IsNotAskedForAConsumable()
    {
        var (items, gear) = FullGear();
        var heal = Heal(200);
        var hero = Hero1(gear) with { Pack = ImmutableList.Create(heal.Id) };
        var state = World(hero, items.Add(heal.Id.Value, heal));

        var (_, events) = Run(state);

        Assert.DoesNotContain(
            events.OfType<CommissionPosted>(),
            e => e.Slot == ItemSlot.Consumable);
    }

    /// <summary>
    /// Survival before augments: a hero missing BOTH a potion and a trinket is asked for the potion.
    /// The order in <c>FindGapSlot</c> is a deliberate ranking, not incidental iteration.
    /// </summary>
    [Fact]
    public void ConsumableOutranksTrinket_WhenBothAreMissing()
    {
        var (items, gear) = FullGear();
        var hero = Trusted(Hero1(gear)); // Regular band, so a trinket ask is permitted
        var state = World(hero, items);

        var (_, events) = Run(state);

        var posted = Assert.Single(events.OfType<CommissionPosted>());
        Assert.Equal(ItemSlot.Consumable, posted.Slot);
    }

    /// <summary>
    /// A trinket is a favour, not survival kit. Gating it on the relationship band keeps "nobody needs
    /// anything right now" reachable — without the gate, every kitted-and-supplied hero would ask for
    /// an accessory forever and the three-slot board would sit permanently saturated.
    /// </summary>
    [Fact]
    public void Stranger_KittedAndSupplied_AsksForNothing()
    {
        var (items, gear) = FullGear();
        var heal = Heal(200);
        var hero = Hero1(gear) with { Pack = ImmutableList.Create(heal.Id) };
        var state = World(hero, items.Add(heal.Id.Value, heal));

        var (after, events) = Run(state);

        Assert.Empty(events.OfType<CommissionPosted>());
        Assert.Empty(after.Commissions);
    }

    [Fact]
    public void TrustedHero_KittedAndSupplied_IsAskedForATrinket()
    {
        var (items, gear) = FullGear();
        var heal = Heal(200);
        var hero = Trusted(Hero1(gear) with { Pack = ImmutableList.Create(heal.Id) });
        var state = World(hero, items.Add(heal.Id.Value, heal));

        var (_, events) = Run(state);

        var posted = Assert.Single(events.OfType<CommissionPosted>());
        Assert.Equal(ItemSlot.Trinket, posted.Slot);
    }

    /// <summary>
    /// THE SILENT-LOSS GUARD. <c>GearSet</c> has no Consumable field and <c>WithSlot</c>'s default arm
    /// returns the set unchanged, so routing a fulfilled potion through gear would take the hero's
    /// gold, emit <see cref="CommissionFulfilled"/>, and hand them nothing at all. The item must land
    /// in <see cref="Hero.Pack"/> — where <c>TryQuaff</c> can actually find and drink it.
    /// </summary>
    [Fact]
    public void FulfilledConsumable_LandsInThePack_NotLostToGear()
    {
        var (items, gear) = FullGear();
        var heal = Heal(300);
        var hero = Hero1(gear);

        var commission = new Commission(
            hero.Id, ItemSlot.Consumable, QualityGrade.Common, DeadlineDay: 99, PremiumGold: 10)
        {
            Accepted = true,
        };

        var world = World(hero, items.Add(heal.Id.Value, heal));
        var state = world with
        {
            Commissions = ImmutableList.Create(commission),
            Player = world.Player with { Shelf = ImmutableList.Create(new ShelfEntry(heal.Id, 10)) },
        };

        // Fulfilment is wired into the shopping pass (CommissionHandlers.TryFulfillFromShelf), which
        // is where an accepted commission gets first refusal on a matching shelf item.
        var sink = new TestSink();
        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), sink);

        Assert.Single(sink.Events.OfType<CommissionFulfilled>());

        var served = after.Heroes[hero.Id.Value];
        Assert.Contains(heal.Id, served.Pack);

        // And the gear set is untouched — the potion did not silently overwrite or vanish into it.
        Assert.Equal(gear, served.Gear);
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────

    private static Hero Hero1(GearSet gear) => new(
        new HeroId(1), "Hero1", "vanguard", Level: 1, MaxHp: 25, Gold: 500,
        gear, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>Lift a hero to Regular band or better. <c>RelationshipBands</c> derives the band from
    /// mood plus owned player-made items, so a generous mood is the cheapest honest way to get there
    /// without fabricating a purchase history.</summary>
    private static Hero Trusted(Hero hero) => hero with { MoodPermille = 900 };

    private static Item Gear(int id, ItemSlot slot) => new(
        new ItemId(id), "test-recipe", $"Kit {slot}", slot, QualityGrade.Common,
        new ItemStats(1, 1, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Heal(int id) => new(
        new ItemId(id), "test-draught", "Draught", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty,
        Effect: new ConsumableEffect(ConsumableKind.Heal, 10));

    private static (ImmutableSortedDictionary<int, Item> Items, GearSet Gear) FullGear()
    {
        var weapon = Gear(101, ItemSlot.Weapon);
        var shield = Gear(102, ItemSlot.Shield);
        var armor = Gear(103, ItemSlot.Armor);
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(101, weapon).Add(102, shield).Add(103, armor);
        return (items, new GearSet(weapon.Id, shield.Id, armor.Id));
    }

    private static GameState World(Hero hero, ImmutableSortedDictionary<int, Item> items) =>
        GameFactory.NewGame(seed: 901) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = items,
        };

    private static (GameState State, List<GameEvent> Events) Run(GameState state)
    {
        var sink = new TestSink();
        var after = new CommissionSystem().Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }
}
