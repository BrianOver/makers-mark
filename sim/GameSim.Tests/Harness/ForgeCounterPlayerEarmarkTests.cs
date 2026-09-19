using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-PEOPLE-28 ("hold it for Torvald"), decision 1's first measured occurrence:
/// <see cref="ForgeCounterPlayer"/>'s own commission-earmark hand, added alongside
/// <see cref="ForgeCounterPlayerTests"/>' existing coverage of the same class rather than inside
/// it. A piece <see cref="Harness.BaselinePlayer"/>'s stocking loop proposes THIS SAME tick that
/// satisfies an accepted (or this-tick-accepted) commission gets held for that hero instead of
/// going to whoever shops first — <see cref="CommissionHandlers.Satisfies"/> is the exact match
/// rule asked, never re-derived.
/// </summary>
public class ForgeCounterPlayerEarmarkTests
{
    private static Hero MakeHero(int id, string classId, int gold) => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeCraftedWeapon(int id, int attack = 6, int weight = 3, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, Defense: 0, weight), new MakersMark("You", CraftedOnDay: 1),
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    private static GameState BaseState(
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableList<Commission> commissions,
        params Item[] items) =>
        GameFactory.NewGame(seed: 9009) with
        {
            Heroes = heroes,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
            Commissions = commissions,
        };

    [Fact]
    public void StockedPiece_SatisfiesAnAcceptedCommission_GetsEarmarkedForThatHero()
    {
        var hero = MakeHero(1, "striker", gold: 200);
        var item = MakeCraftedWeapon(1);
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: true);
        var state = BaseState(Roster(hero), ImmutableList.Create(commission), item);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Contains(actions, a => a is StockAction s && s.Item == item.Id);
        var earmark = Assert.Single(actions.OfType<EarmarkAction>());
        Assert.Equal(item.Id, earmark.Item);
        Assert.Equal(hero.Id, earmark.Hero);
    }

    [Fact]
    public void StockedPiece_SatisfiesACommissionAcceptedThisSameTick_StillGetsEarmarked()
    {
        // Not yet Accepted in state, but BaselinePlayer's own routine queues an
        // AcceptCommissionAction for it in this same tick's action list -- the earmark hand must
        // read THAT, not just the persisted Accepted flag, or a commission's very first tick would
        // lose the race to ordinary shopping every time.
        var hero = MakeHero(1, "striker", gold: 200);
        var item = MakeCraftedWeapon(1);
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: false);
        var state = BaseState(Roster(hero), ImmutableList.Create(commission), item);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Contains(actions, a => a is AcceptCommissionAction accept && accept.Hero == hero.Id);
        var earmark = Assert.Single(actions.OfType<EarmarkAction>());
        Assert.Equal(hero.Id, earmark.Hero);
    }

    [Fact]
    public void UnacceptedCommission_NotAcceptedThisTickEither_NeverEarmarks()
    {
        // Consumable commissions are the one case BaselinePlayer never auto-accepts (its own
        // Slot != Consumable guard) -- exactly the "not accepted, not accepting now" shape this
        // hand must stay silent on.
        var hero = MakeHero(1, "striker", gold: 200);
        var item = new Item(
            new ItemId(1), "test-recipe", "Salve", ItemSlot.Consumable, QualityGrade.Common,
            new ItemStats(0, 0, 0), new MakersMark("You", CraftedOnDay: 1),
            ImmutableList<ItemHistoryEntry>.Empty, Effect: new ConsumableEffect(ConsumableKind.Heal, Magnitude: 10));
        var commission = new Commission(hero.Id, ItemSlot.Consumable, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: false);
        var state = BaseState(Roster(hero), ImmutableList.Create(commission), item);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Empty(actions.OfType<EarmarkAction>());
    }

    [Fact]
    public void CommissionForADeadHero_NeverEarmarks()
    {
        var hero = MakeHero(1, "striker", gold: 200) with { Alive = false, DiedOnDay = 1 };
        var item = MakeCraftedWeapon(1);
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: true);
        var state = BaseState(Roster(hero), ImmutableList.Create(commission), item);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Empty(actions.OfType<EarmarkAction>());
    }

    [Fact]
    public void StockedPiece_WrongSlotForTheCommission_NeverEarmarks()
    {
        var hero = MakeHero(1, "striker", gold: 200);
        var item = MakeCraftedWeapon(1); // Weapon
        var commission = new Commission(hero.Id, ItemSlot.Armor, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: true);
        var state = BaseState(Roster(hero), ImmutableList.Create(commission), item);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Empty(actions.OfType<EarmarkAction>());
    }

    [Fact]
    public void TwoStockedPieces_TwoDifferentHeroesCommissions_EachGetsItsOwnEarmark()
    {
        var heroA = MakeHero(1, "striker", gold: 200);
        var heroB = MakeHero(2, "vanguard", gold: 200);
        var itemA = MakeCraftedWeapon(1, name: "A");
        var itemB = MakeCraftedWeapon(2, name: "B");
        var commissions = ImmutableList.Create(
            new Commission(heroA.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: true),
            new Commission(heroB.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: true));
        var state = BaseState(Roster(heroA, heroB), commissions, itemA, itemB);

        var earmarks = ForgeCounterPlayer.ActionsFor(state).OfType<EarmarkAction>().ToList();

        Assert.Equal(2, earmarks.Count);
        Assert.Contains(earmarks, e => e.Item == itemA.Id && e.Hero == heroA.Id);
        Assert.Contains(earmarks, e => e.Item == itemB.Id && e.Hero == heroB.Id);
    }

    [Fact]
    public void TwoStockedPiecesSatisfyTheSameHero_OnlyOneGetsClaimed()
    {
        // One hero holds at most one open/accepted commission at a time -- the second stocked
        // piece that would also satisfy it must NOT get a second earmark for the same hero.
        var hero = MakeHero(1, "striker", gold: 200);
        var itemA = MakeCraftedWeapon(1, name: "A");
        var itemB = MakeCraftedWeapon(2, name: "B");
        var commission = new Commission(hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20, Accepted: true);
        var state = BaseState(Roster(hero), ImmutableList.Create(commission), itemA, itemB);

        var earmarks = ForgeCounterPlayer.ActionsFor(state).OfType<EarmarkAction>().ToList();

        Assert.Single(earmarks);
    }
}
