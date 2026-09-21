using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Counter;

/// <summary>
/// P2-HONEST-45 (docs/design/MAKERS-MARK.md §11.16, "The counter's opener shows an upgrade, not a
/// role token") — decision 2, price for the sale or the relationship.
///
/// <para><b>What these guard, and why they are phrased as rules.</b> The opener these replace added
/// a flat bonus for one SLOT (Shield) shown to one CLASS FAMILY (shield-capable), so it kept opening
/// on the shield the customer was already wearing: 38% of measured walks read "current Buckler is
/// better" / "current Kite Shield is better". A guard that pinned "a vanguard is shown the buckler"
/// would have gone green through the entire defect, and would stop covering the family the moment a
/// seventh class or a fifth slot lands. So every assertion here names the RULE the choice has to
/// satisfy — the customer can wear it, can afford it, and it is the biggest such gain on the shelf —
/// and sweeps the rule across <see cref="ClassRegistry.All"/> and every <see cref="ItemSlot"/>
/// rather than across a hand-listed fixture.</para>
/// </summary>
public class CounterOpenerTests
{
    private static Hero MakeHero(int id, string classId, int gold, GearSet? gear = null) => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(
        int id, ItemSlot slot, int attack, int defense, int weight,
        string name = "Item", ConsumableEffect? effect = null) => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty)
    { Effect = effect };

    private static GameState StateWith(Hero hero, params (Item Item, int Price)[] shelf)
    {
        var items = shelf.Select(s => s.Item).ToImmutableSortedDictionary(i => i.Id.Value, i => i);
        return GameFactory.NewGame(seed: 7701) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = items,
            Player = PlayerState.NewGame(0) with
            {
                Shelf = shelf.Select(s => new ShelfEntry(s.Item.Id, s.Price)).ToImmutableList(),
            },
            Counter = CounterState.Empty with
            {
                Queue = ImmutableList.Create(hero.Id),
                Active = hero.Id,
                PatienceRounds = WillingnessModel.InitialPatienceRounds,
            },
        };
    }

    private static ItemId Opener(GameState state)
    {
        var actions = CounterPlayer.ActionsFor(state);
        return Assert.IsType<PresentItemAction>(Assert.Single(actions)).Item;
    }

    /// <summary>The rule, not an instance: whenever the shelf holds at least one piece this customer
    /// could both wear and pay for, the opener is one of those pieces. The role-token opener failed
    /// exactly here — it would open on an unaffordable or already-beaten shield while a usable piece
    /// sat one slot over. Swept over every registered class and every gear slot so a new class or a
    /// new slot is covered the day it lands, with no edit here.</summary>
    [Theory]
    [MemberData(nameof(EveryClassAndGearSlot))]
    public void Opener_IsAlwaysSomethingTheCustomerCanWearAndAfford_WhenTheShelfHoldsOne(string classId, ItemSlot slot)
    {
        var heroClass = ClassRegistry.Require(classId);
        var hero = MakeHero(1, classId, gold: 100);

        // A piece in the swept slot the hero may or may not be able to use, priced out of reach;
        // plus one plain light Weapon every class can carry, priced within reach. Whatever the swept
        // slot turns out to be for this class, the weapon is always the wearable+affordable answer.
        var swept = MakeItem(1, slot, attack: 30, defense: 30, weight: 1, name: "Swept");
        var affordable = MakeItem(2, ItemSlot.Weapon, attack: 3, defense: 0, weight: 1, name: "Plain Blade");
        var state = StateWith(hero, (swept, 5_000), (affordable, 10));

        var opener = Opener(state);
        var chosen = state.Items[opener.Value];
        var price = state.Player.Shelf.Single(e => e.Item == opener).Price;

        Assert.False(
            chosen.Slot == ItemSlot.Shield && !heroClass.AllowsShield,
            $"{classId} was opened on a shield they cannot use.");
        Assert.False(
            heroClass.MaxItemWeight is { } cap && chosen.Stats.Weight > cap,
            $"{classId} was opened on a piece heavier than they carry.");
        Assert.True(price <= hero.Gold, $"{classId} was opened on a {price}g piece holding {hero.Gold}g.");
    }

    public static TheoryData<string, ItemSlot> EveryClassAndGearSlot()
    {
        var data = new TheoryData<string, ItemSlot>();
        foreach (var classId in ClassRegistry.All.Keys)
        {
            foreach (var slot in Enum.GetValues<ItemSlot>())
            {
                if (slot == ItemSlot.Consumable)
                {
                    continue; // consumables have their own rule (pack target) and their own test below
                }

                data.Add(classId, slot);
            }
        }

        return data;
    }

    /// <summary>The defect itself, stated as its rule: between two pieces the customer can wear and
    /// afford, the BIGGER gear-score gain opens — the slot it happens to occupy never outranks the
    /// gain. Asserted over every registered shield-capable class, because "shield beats everything"
    /// was precisely the old flat bonus.</summary>
    [Theory]
    [MemberData(nameof(EveryShieldCapableClass))]
    public void Opener_PrefersTheLargerGain_NotTheShieldSlot(string classId)
    {
        var hero = MakeHero(1, classId, gold: 500);
        var weakShield = MakeItem(1, ItemSlot.Shield, attack: 0, defense: 2, weight: 3, name: "Buckler");
        var strongSword = MakeItem(2, ItemSlot.Weapon, attack: 9, defense: 0, weight: 3, name: "Longsword");

        Assert.Equal(strongSword.Id, Opener(StateWith(hero, (weakShield, 40), (strongSword, 60))));
    }

    /// <summary>And the same rule in the other direction — a shield that IS the larger gain still
    /// wins. Pinning only the first direction would let "never present a shield" pass as a fix.</summary>
    [Theory]
    [MemberData(nameof(EveryShieldCapableClass))]
    public void Opener_StillPicksTheShield_WhenTheShieldIsTheLargerGain(string classId)
    {
        var hero = MakeHero(1, classId, gold: 500);
        var strongShield = MakeItem(1, ItemSlot.Shield, attack: 0, defense: 9, weight: 3, name: "Kite Shield");
        var weakSword = MakeItem(2, ItemSlot.Weapon, attack: 2, defense: 0, weight: 3, name: "Dirk");

        Assert.Equal(strongShield.Id, Opener(StateWith(hero, (strongShield, 40), (weakSword, 60))));
    }

    public static TheoryData<string> EveryShieldCapableClass()
    {
        var data = new TheoryData<string>();
        foreach (var definition in ClassRegistry.All.Values.Where(c => c.AllowsShield))
        {
            data.Add(definition.Id);
        }

        return data;
    }

    /// <summary>A customer whose pack is under their own stocking target
    /// (<see cref="TraitEffects.ConsumableStockTargetFor"/>) is offered the consumable rather than
    /// gear they would refuse — the opener asks the trait, it does not assume a target of its own.
    /// The shelf here holds a piece they cannot use at all, which is the situation §11.16 measured.
    /// </summary>
    [Fact]
    public void Opener_OffersAConsumable_WhenThePackIsUnderTheHerosOwnStockTarget()
    {
        var hero = MakeHero(1, ClassRegistry.MysticId, gold: 500);
        Assert.True(hero.Pack.Count < TraitEffects.ConsumableStockTargetFor(hero), "fixture precondition");

        var tooHeavy = MakeItem(1, ItemSlot.Weapon, attack: 30, defense: 0, weight: 99, name: "Greataxe");
        var salve = MakeItem(2, ItemSlot.Consumable, 0, 0, 1, "Salve", new ConsumableEffect(ConsumableKind.Heal, 10));

        Assert.Equal(salve.Id, Opener(StateWith(hero, (tooHeavy, 40), (salve, 15))));
    }

    /// <summary>The other arm of the same rule: a hero already carrying their target does NOT get
    /// opened on a consumable when a real upgrade is on the shelf. Without this, "always present the
    /// potion" would pass the test above.</summary>
    [Fact]
    public void Opener_SkipsTheConsumable_WhenThePackAlreadyMeetsTheTarget()
    {
        var salve = MakeItem(2, ItemSlot.Consumable, 0, 0, 1, "Salve", new ConsumableEffect(ConsumableKind.Heal, 10));
        var upgrade = MakeItem(1, ItemSlot.Weapon, attack: 7, defense: 0, weight: 2, name: "Blade");
        var hero = MakeHero(1, ClassRegistry.MysticId, gold: 500) with
        {
            Pack = ImmutableList.Create(new ItemId(99)),
        };
        Assert.True(hero.Pack.Count >= TraitEffects.ConsumableStockTargetFor(hero), "fixture precondition");

        Assert.Equal(upgrade.Id, Opener(StateWith(hero, (upgrade, 40), (salve, 15))));
    }

    /// <summary>An unaffordable consumable is never the opener over a piece they can actually buy —
    /// affordability is asked of <see cref="ShoppingAi"/>, not assumed from the slot.</summary>
    [Fact]
    public void Opener_SkipsAConsumableTheCustomerCannotAfford()
    {
        var blade = MakeItem(1, ItemSlot.Weapon, attack: 4, defense: 0, weight: 2, name: "Blade");
        var salve = MakeItem(2, ItemSlot.Consumable, 0, 0, 1, "Salve", new ConsumableEffect(ConsumableKind.Heal, 10));
        var hero = MakeHero(1, ClassRegistry.StrikerId, gold: 30);

        Assert.Equal(blade.Id, Opener(StateWith(hero, (blade, 20), (salve, 400))));
    }

    /// <summary>A one-item shelf the customer cannot use must still produce an opener, never a stall
    /// and never a throw — the morning has to move. (The old scorer guaranteed this by never
    /// excluding anything; the tiered one has to keep that guarantee.)</summary>
    [Fact]
    public void Opener_StillPresentsSomething_WhenNothingOnTheShelfIsUsable()
    {
        var hero = MakeHero(1, ClassRegistry.StrikerId, gold: 1);
        var unusable = MakeItem(1, ItemSlot.Shield, attack: 0, defense: 5, weight: 2, name: "Buckler");

        Assert.Equal(unusable.Id, Opener(StateWith(hero, (unusable, 900))));
    }

    /// <summary>Determinism: the opener is a pure function of state. Two independent runs over the
    /// same seeded state — shelf built in the opposite order the second time, so any hidden
    /// enumeration dependence would show — pick the same item, and a score tie falls to the LOWEST
    /// item id both times.</summary>
    [Fact]
    public void Opener_IsDeterministic_AcrossTwoRunsOfOneSeed_AndTiesFallToTheLowestItemId()
    {
        var hero = MakeHero(1, ClassRegistry.StrikerId, gold: 500);
        var twinA = MakeItem(1, ItemSlot.Weapon, attack: 5, defense: 0, weight: 2, name: "Twin A");
        var twinB = MakeItem(2, ItemSlot.Weapon, attack: 5, defense: 0, weight: 2, name: "Twin B");

        var forward = Opener(StateWith(hero, (twinA, 40), (twinB, 40)));
        var reversed = Opener(StateWith(hero, (twinB, 40), (twinA, 40)));

        Assert.Equal(forward, reversed);
        Assert.Equal(twinA.Id, forward); // identical scores — lowest item id settles it, both ways
    }

    /// <summary>A census, not an anecdote: sweep 20 seeds' worth of generated customers and shelves
    /// and count how often the opener is a piece the customer cannot wear or cannot afford. The
    /// role-token opener scored that case on purpose (a +1,000 slot bonus outranked any gain and any
    /// price); the rule is that it may now happen ONLY when the shelf offers no usable piece at all.
    /// The generator is a pure integer LCG over the seed — no RNG service, no clock — so this census
    /// is reproducible run to run.</summary>
    [Fact]
    public void Census_OverTwentySeeds_TheOpenerIsUnusableOnlyWhenTheShelfHasNothingUsable()
    {
        var classIds = ClassRegistry.All.Keys.ToImmutableArray();
        var openers = 0;
        var unusableOpeners = 0;
        var shelvesWithNothingUsable = 0;

        for (var seed = 1; seed <= 20; seed++)
        {
            var draw = (uint)(seed * 2_654_435_761u);
            int Next(int bound)
            {
                draw = unchecked((draw * 1_664_525u) + 1_013_904_223u);
                return (int)(draw >> 16) % bound;
            }

            for (var customer = 0; customer < 12; customer++)
            {
                var classId = classIds[Next(classIds.Length)];
                var heroClass = ClassRegistry.Require(classId);
                var hero = MakeHero(1, classId, gold: 10 + Next(120));

                var shelf = new List<(Item Item, int Price)>();
                var pieces = 1 + Next(4);
                for (var i = 0; i < pieces; i++)
                {
                    var slot = (ItemSlot)Next(Enum.GetValues<ItemSlot>().Length);
                    var effect = slot == ItemSlot.Consumable ? new ConsumableEffect(ConsumableKind.Heal, 10) : null;
                    shelf.Add((MakeItem(i + 1, slot, Next(10), Next(10), 1 + Next(8), $"P{i}", effect), 1 + Next(150)));
                }

                var state = StateWith(hero, shelf.ToArray());
                var opener = Opener(state);
                openers++;

                bool Usable(Item item, int price) =>
                    !(item.Slot == ItemSlot.Shield && !heroClass.AllowsShield)
                    && !(heroClass.MaxItemWeight is { } cap && item.Stats.Weight > cap)
                    && price <= hero.Gold;

                var anyUsable = shelf.Any(s => Usable(s.Item, s.Price));
                if (!anyUsable)
                {
                    shelvesWithNothingUsable++;
                }

                var chosen = state.Items[opener.Value];
                var chosenPrice = state.Player.Shelf.Single(e => e.Item == opener).Price;
                if (!Usable(chosen, chosenPrice))
                {
                    unusableOpeners++;
                    Assert.False(anyUsable, $"seed {seed}: opened on an unusable piece while a usable one was shelved.");
                }
            }
        }

        Assert.Equal(240, openers);
        Assert.Equal(shelvesWithNothingUsable, unusableOpeners); // exactly the forced cases, never one more
        Assert.True(shelvesWithNothingUsable < openers, "census generated no usable shelves — it would prove nothing.");
    }
}
