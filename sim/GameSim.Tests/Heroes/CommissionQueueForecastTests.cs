using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// P2-PEOPLE-17 ("stocking a piece names the morning queue that will reach it first", decision 1 —
/// sell the good one or hold it for the hero who needs it): <see cref="CommissionHandlers.Satisfies"/>
/// (the forge-request match predicate extracted out of <see cref="CommissionHandlers.TryFulfillFromShelf"/>'s
/// own shelf scan) and <see cref="CommissionHandlers.ForecastQueueFor"/> (which accepted commission a
/// piece would fill, and who shops ahead of that hero — read from
/// <see cref="HeroShoppingSystem.MorningShoppingOrder"/>, never re-sorted here).
///
/// The property under test throughout: a forecast must never disagree with what
/// <see cref="HeroShoppingSystem.Process"/> ACTUALLY does. A forecast that predicts one outcome
/// while the real Morning pass produces another is exactly the "second copy that drifted" failure
/// this unit exists to close — several tests below cross-check the forecast against a REAL Process()
/// run rather than trusting the forecast in isolation.
/// </summary>
public class CommissionQueueForecastTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, string classId, int gold, string? name = null) => new(
        new HeroId(id), name ?? $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, QualityGrade quality, int weight = 1, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, quality,
        new ItemStats(1, 1, weight), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState World(
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableSortedDictionary<int, Item> items,
        ImmutableList<ShelfEntry> shelf,
        ImmutableList<Commission> commissions) =>
        GameFactory.NewGame(seed: 1) with
        {
            Heroes = heroes,
            Items = items,
            Player = PlayerState.NewGame(0) with { Shelf = shelf },
            Commissions = commissions,
        };

    private static (GameState State, List<GameEvent> Events) RunMorningShopping(GameState state)
    {
        var sink = new TestSink();
        var after = new HeroShoppingSystem().Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    // ── Satisfies: the property, iterated across slot x class x quality x weight ─────────────────
    // (never one hand-written case — every ItemSlot x every registered class, quality both above
    // and below the commission's floor, and the weight-cap boundary for the one class that has one)

    public static IEnumerable<object[]> SatisfiesCases()
    {
        var classIds = new[] { ClassRegistry.VanguardId, ClassRegistry.StrikerId, ClassRegistry.MysticId };
        var slots = new[] { ItemSlot.Weapon, ItemSlot.Shield, ItemSlot.Armor, ItemSlot.Consumable, ItemSlot.Trinket };

        foreach (var classId in classIds)
        {
            var heroClass = ClassRegistry.Require(classId);
            foreach (var slot in slots)
            {
                // Below the commission's quality floor always fails, regardless of slot/class.
                yield return new object[] { classId, slot, QualityGrade.Fine, QualityGrade.Common, 1, false };

                // Meeting the floor passes UNLESS this slot physically can't reach this class
                // (Shield on a non-shield class) — the "can this hero use it" fact a bespoke
                // commission still enforces even though it bypasses ShoppingAi's preference gates.
                var shieldBlocked = slot == ItemSlot.Shield && !heroClass.AllowsShield;
                yield return new object[] { classId, slot, QualityGrade.Common, QualityGrade.Common, 1, !shieldBlocked };

                // Over the class's per-slot weight cap fails ONLY for a class that has one (Mystic)
                // — skipped when the slot is already shield-blocked (that case would fail on shield
                // alone and would never actually exercise the weight check).
                if (!shieldBlocked)
                {
                    var overWeight = heroClass.MaxItemWeight is { } cap ? cap + 1 : 999;
                    var weightBlocked = heroClass.MaxItemWeight is { } cap2 && overWeight > cap2;
                    yield return new object[] { classId, slot, QualityGrade.Common, QualityGrade.Common, overWeight, !weightBlocked };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(SatisfiesCases))]
    public void Satisfies_AgreesWithTheRealFulfillmentGate(
        string classId, ItemSlot slot, QualityGrade commissionMinQuality, QualityGrade itemQuality, int itemWeight, bool expected)
    {
        var hero = MakeHero(1, classId, gold: 1000);
        var item = MakeItem(1, slot, itemQuality, itemWeight);
        var commission = new Commission(hero.Id, slot, commissionMinQuality, DeadlineDay: 99, PremiumGold: 10) { Accepted = true };

        var predicted = CommissionHandlers.Satisfies(commission, item, ClassRegistry.Require(classId));
        Assert.Equal(expected, predicted);

        // Cross-check: the same verdict must be what the REAL Morning pass produces from this exact
        // shelf item — never a Satisfies() that agrees with itself but disagrees with the sim.
        var state = World(
            ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            ImmutableSortedDictionary<int, Item>.Empty.Add(1, item),
            ImmutableList.Create(new ShelfEntry(item.Id, 5)),
            ImmutableList.Create(commission));

        var (_, events) = RunMorningShopping(state);
        Assert.Equal(expected, events.OfType<CommissionFulfilled>().Any());
    }

    // ── ForecastQueueFor: no match renders no ask, never an invented one ──────────────────────────

    [Fact]
    public void ForecastQueueFor_NoCommissionsAtAll_ReturnsNull()
    {
        var hero = MakeHero(1, ClassRegistry.VanguardId, gold: 100);
        var item = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common);
        var state = World(
            ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            ImmutableSortedDictionary<int, Item>.Empty.Add(1, item),
            ImmutableList<ShelfEntry>.Empty,
            ImmutableList<Commission>.Empty);

        Assert.Null(CommissionHandlers.ForecastQueueFor(state, item));
    }

    [Fact]
    public void ForecastQueueFor_OpenButNotYetAccepted_ReturnsNull_NoGuaranteeToInvent()
    {
        // TryFulfillFromShelf never touches an unaccepted commission either — it carries no
        // guaranteed sale or premium, so naming one here would promise something the sim hasn't.
        var hero = MakeHero(1, ClassRegistry.VanguardId, gold: 100);
        var item = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common);
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20);
        var state = World(
            ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
            ImmutableSortedDictionary<int, Item>.Empty.Add(1, item),
            ImmutableList<ShelfEntry>.Empty,
            ImmutableList.Create(commission));

        Assert.Null(CommissionHandlers.ForecastQueueFor(state, item));
    }

    [Fact]
    public void ForecastQueueFor_WrongSlotOrBelowQuality_ReturnsNull()
    {
        var hero = MakeHero(1, ClassRegistry.VanguardId, gold: 100);
        var weapon = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common);
        var wrongSlot = new Commission(hero.Id, ItemSlot.Shield, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20) { Accepted = true };
        var tooLowQuality = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 10, PremiumGold: 20) { Accepted = true };

        foreach (var commission in new[] { wrongSlot, tooLowQuality })
        {
            var state = World(
                ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
                ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon),
                ImmutableList<ShelfEntry>.Empty,
                ImmutableList.Create(commission));

            Assert.Null(CommissionHandlers.ForecastQueueFor(state, weapon));
        }
    }

    // ── ForecastQueueFor: AheadInQueue is the real Morning queue, never re-derived ─────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ForecastQueueFor_AheadInQueue_IsExactlyEveryHeroEarlierInTheRealQueuePosition(int targetIndex)
    {
        const int heroCount = 4;
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty;
        for (var i = 1; i <= heroCount; i++)
        {
            heroes = heroes.Add(i, MakeHero(i, ClassRegistry.VanguardId, gold: 1000, name: $"Hero{i}"));
        }

        var targetHeroId = targetIndex + 1; // hero ids ascend 1..4 — HeroShoppingSystem.MorningShoppingOrder's own order
        var weapon = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common);
        var commission = new Commission(new HeroId(targetHeroId), ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 99, PremiumGold: 15)
        {
            Accepted = true,
        };

        var state = World(
            heroes,
            ImmutableSortedDictionary<int, Item>.Empty.Add(1, weapon),
            ImmutableList<ShelfEntry>.Empty, // this test is only about the queue math, not a real sale
            ImmutableList.Create(commission));

        var forecast = CommissionHandlers.ForecastQueueFor(state, weapon);
        Assert.NotNull(forecast);
        Assert.Equal(commission, forecast!.Commission);

        var expectedAhead = Enumerable.Range(1, targetIndex).Select(i => new HeroId(i)).ToImmutableArray();
        // ImmutableArray<T>.Equals compares the BACKING ARRAY by reference, not by content — a
        // direct Assert.Equal(ImmutableArray, ImmutableArray) can report "differ" while printing
        // identical elements. .AsEnumerable() routes it through xUnit's real element-wise compare.
        Assert.Equal(expectedAhead.AsEnumerable(), forecast.AheadInQueue.AsEnumerable());
    }

    /// <summary>
    /// THE regression proof: Torvald (queue position 1, no commission) has an empty weapon slot, so
    /// his ORDINARY gear shopping wants this exact sword. Kael (position 2) holds an ACCEPTED
    /// commission for it. <see cref="HeroShoppingSystem.ShopOnce"/> only checks a hero's OWN
    /// commission on that hero's OWN turn — Torvald's turn runs first, and his ordinary pass buys the
    /// sword before Kael's commission check ever gets to run. This is the exact bug shape
    /// P2-PEOPLE-17 makes visible, and the forecast must predict it: Torvald ahead of Kael.
    /// </summary>
    [Fact]
    public void ForecastQueueFor_PredictsExactlyWhoSnipesThePiece_BeforeTheCommissionHoldersTurn()
    {
        var torvald = MakeHero(1, ClassRegistry.VanguardId, gold: 1000, name: "Torvald");
        var kael = MakeHero(2, ClassRegistry.VanguardId, gold: 1000, name: "Kael");
        var sword = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common, name: "Iron Sword");
        var commission = new Commission(kael.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 99, PremiumGold: 40)
        {
            Accepted = true,
        };

        var state = World(
            ImmutableSortedDictionary<int, Hero>.Empty.Add(1, torvald).Add(2, kael),
            ImmutableSortedDictionary<int, Item>.Empty.Add(1, sword),
            ImmutableList.Create(new ShelfEntry(sword.Id, 20)),
            ImmutableList.Create(commission));

        // The forecast, computed before anyone has actually shopped:
        var forecast = CommissionHandlers.ForecastQueueFor(state, sword);
        Assert.NotNull(forecast);
        Assert.Equal(commission, forecast!.Commission);
        Assert.Equal(new[] { torvald.Id }.AsEnumerable(), forecast.AheadInQueue.AsEnumerable());

        // The real Morning pass: Torvald's ordinary shopping takes it first.
        var (_, events) = RunMorningShopping(state);

        var sold = Assert.Single(events.OfType<ItemSold>());
        Assert.Equal(torvald.Id, sold.Buyer); // Torvald bought it via ORDINARY shopping — not Kael
        Assert.Empty(events.OfType<CommissionFulfilled>()); // Kael's commission never got a turn at it
    }

    /// <summary>The mirror case: swap the queue positions so Kael (the commission holder) shops
    /// FIRST. The forecast must predict an empty queue ahead of him, and the real pass must actually
    /// fulfil the commission — proving the forecast is not just "always predicts a snipe".</summary>
    [Fact]
    public void ForecastQueueFor_WhenTheCommissionHolderShopsFirst_PredictsEmptyQueue_AndTheRealPassFulfillsIt()
    {
        var kael = MakeHero(1, ClassRegistry.VanguardId, gold: 1000, name: "Kael"); // first in queue now
        var torvald = MakeHero(2, ClassRegistry.VanguardId, gold: 1000, name: "Torvald");
        var sword = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common, name: "Iron Sword");
        var commission = new Commission(kael.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 99, PremiumGold: 40)
        {
            Accepted = true,
        };

        var state = World(
            ImmutableSortedDictionary<int, Hero>.Empty.Add(1, kael).Add(2, torvald),
            ImmutableSortedDictionary<int, Item>.Empty.Add(1, sword),
            ImmutableList.Create(new ShelfEntry(sword.Id, 20)),
            ImmutableList.Create(commission));

        var forecast = CommissionHandlers.ForecastQueueFor(state, sword);
        Assert.NotNull(forecast);
        Assert.Empty(forecast!.AheadInQueue);

        var (_, events) = RunMorningShopping(state);
        var fulfilled = Assert.Single(events.OfType<CommissionFulfilled>());
        Assert.Equal(kael.Id, fulfilled.Hero);
    }

    [Fact]
    public void ForecastQueueFor_DeadOrCounterServedHeroesAreNeverCountedInTheQueue()
    {
        var deadHero = MakeHero(1, ClassRegistry.VanguardId, gold: 1000, name: "Fallen") with { Alive = false };
        var kael = MakeHero(2, ClassRegistry.VanguardId, gold: 1000, name: "Kael");
        var sword = MakeItem(1, ItemSlot.Weapon, QualityGrade.Common, name: "Iron Sword");
        var commission = new Commission(kael.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 99, PremiumGold: 40)
        {
            Accepted = true,
        };

        var state = World(
            ImmutableSortedDictionary<int, Hero>.Empty.Add(1, deadHero).Add(2, kael),
            ImmutableSortedDictionary<int, Item>.Empty.Add(1, sword),
            ImmutableList<ShelfEntry>.Empty,
            ImmutableList.Create(commission));

        var forecast = CommissionHandlers.ForecastQueueFor(state, sword);
        Assert.NotNull(forecast);
        Assert.Empty(forecast!.AheadInQueue); // a dead hero never occupies a queue slot
    }
}
