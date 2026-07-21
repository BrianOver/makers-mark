using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// PA5 (plan 2026-07-21-002): <see cref="CounterPlayer"/> — the scripted stepped-counter policy
/// beside (never forking) <see cref="BaselinePlayer"/>. Covers purity (same state, same actions,
/// every call), the two "nothing to do" mornings (empty shelf, no customer) resolving without
/// exception, the band-center haggle response matching <see cref="WillingnessModel"/> exactly, and
/// a full stepped morning — driven ENTIRELY by the policy, one tick at a time — closing a real
/// haggled sale through the production kernel.
/// </summary>
public class CounterPlayerTests
{
    private static Hero MakeHero(int id, string classId, int gold, GearSet? gear = null) => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, ItemSlot slot, int attack, int defense, int weight, string name = "Item") => new(
        new ItemId(id), "test-recipe", name, slot, QualityGrade.Common,
        new ItemStats(attack, defense, weight), Mark: null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    private static GameState BaseState(ImmutableSortedDictionary<int, Hero> heroes, params Item[] items) =>
        GameFactory.NewGame(seed: 4004) with
        {
            Heroes = heroes,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    private static GameState WithShelf(GameState state, int gold, params ShelfEntry[] shelf) =>
        state with { Player = PlayerState.NewGame(gold) with { Shelf = shelf.ToImmutableList() } };

    private static GameKernel Kernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    [Fact]
    public void ActionsFor_NonMorningPhase_ReturnsEmpty_NeverOpensOutsideMorning()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100))) with { Phase = DayPhase.Evening };

        Assert.Empty(CounterPlayer.ActionsFor(state));
    }

    [Fact]
    public void ActionsFor_NoSessionYet_OpensTheCounter()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100)));

        var actions = CounterPlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new OpenCounterAction()), actions);
    }

    [Fact]
    public void ActionsFor_SameStateTwice_ProducesTheSameActions_EveryCall()
    {
        // Purity (no IO/RNG/clock): calling twice on an IDENTICAL state must be byte-for-byte
        // the same decision, whatever the session shape.
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(MakeHero(1, "striker", 200)), sword), gold: 0, new ShelfEntry(sword.Id, 50));
        var withSession = state with
        {
            Counter = CounterState.Empty with
            {
                Queue = ImmutableList.Create(new HeroId(1)),
                Active = new HeroId(1),
                PatienceRounds = WillingnessModel.InitialPatienceRounds,
            },
        };

        var first = CounterPlayer.ActionsFor(withSession);
        var second = CounterPlayer.ActionsFor(withSession);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void ActionsFor_NoActiveCustomer_ClosesTheCounter_NeverThrows()
    {
        // An open session with an empty queue (no living heroes left to serve) is a valid state
        // (PKD6) — the policy's job here is done, so it closes rather than stalling.
        var state = BaseState(Roster(MakeHero(1, "striker", 100))) with
        {
            Counter = CounterState.Empty,
        };

        var actions = CounterPlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new CloseCounterAction()), actions);
    }

    [Fact]
    public void ActionsFor_EmptyShelf_WithActiveCustomer_ClosesInsteadOfStalling()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100))) with
        {
            Counter = CounterState.Empty with { Queue = ImmutableList.Create(new HeroId(1)), Active = new HeroId(1) },
        };

        var actions = CounterPlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new CloseCounterAction()), actions);
    }

    [Fact]
    public void ActionsFor_ClosedSession_DoesNothing()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100))) with
        {
            Counter = CounterState.Empty with { Closed = true },
        };

        Assert.Empty(CounterPlayer.ActionsFor(state));
    }

    [Fact]
    public void ActionsFor_PresentsTheBestRoleFitItem_ShieldToAShieldAllowedAnchorOverAPlainWeapon()
    {
        var vanguard = MakeHero(1, ClassRegistry.VanguardId, gold: 500);
        var shield = MakeItem(1, ItemSlot.Shield, attack: 0, defense: 8, weight: 4, name: "Buckler");
        var sword = MakeItem(2, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3, name: "Sword");
        var state = WithShelf(
            BaseState(Roster(vanguard), shield, sword),
            gold: 0, new ShelfEntry(shield.Id, 40), new ShelfEntry(sword.Id, 60)) with
        {
            Counter = CounterState.Empty with { Queue = ImmutableList.Create(new HeroId(1)), Active = new HeroId(1) },
        };

        var actions = CounterPlayer.ActionsFor(state);

        var present = Assert.IsType<PresentItemAction>(Assert.Single(actions));
        Assert.Equal(shield.Id, present.Item);
    }

    [Fact]
    public void ActionsFor_StandingOffer_CountersAtExactlyTheRoundsBandCenter()
    {
        var hero = MakeHero(1, "striker", gold: 1000);
        var state = BaseState(Roster(hero)) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 100)) },
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, MakeItem(1, ItemSlot.Weapon, 6, 0, 3)),
            Counter = CounterState.Empty with
            {
                Queue = ImmutableList.Create(new HeroId(1)),
                Active = new HeroId(1),
                Round = 1,
                Presented = new ItemId(1),
                StandingOfferGold = 82, // round-1 floor, per WillingnessModelTests' pinned table
            },
        };

        var trueWillingness = WillingnessModel.TrueWillingness(100, hero.Gold, hero.ClassId, interestPermille: 0, moodPermille: 0);
        var (floor, ceiling) = WillingnessModel.Band(trueWillingness, round: 1);
        var expectedCenter = (floor + ceiling) / 2;

        var actions = CounterPlayer.ActionsFor(state);

        var haggle = Assert.IsType<HaggleResponseAction>(Assert.Single(actions));
        Assert.Equal(HaggleResponseKind.Counter, haggle.Kind);
        Assert.Equal(expectedCenter, haggle.Price);
    }

    [Fact]
    public void DrivenStandalone_ThroughTheKernel_ClosesAHaggledSale_ViaCounter_NotAccept()
    {
        // The full script the policy itself produces, one tick at a time, through the SAME
        // scoped kernel HaggleEconomicsTests uses (PA3/PA4's own systems) — proves CounterPlayer
        // is not just individually-correct decisions but an actually-playable stepped morning.
        var hero = MakeHero(1, "striker", gold: 1000);
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(hero), sword), gold: 0, new ShelfEntry(sword.Id, 100));
        var kernel = Kernel();

        CounterSaleClosed? sale = null;
        for (var i = 0; i < 10 && state.Counter is not { Closed: true } && sale is null; i++)
        {
            var actions = CounterPlayer.ActionsFor(state);
            var result = kernel.Tick(state, actions);
            Assert.Empty(result.Rejected);
            sale ??= result.Events.OfType<CounterSaleClosed>().FirstOrDefault();
            state = result.NewState;
        }

        Assert.NotNull(sale);
        Assert.Equal(hero.Id, sale!.Hero);
        Assert.Equal(sword.Id, sale.Item);
        Assert.Equal(90, sale.Price); // round-1 band [82,98] center — CounterPlayer's own "counter at band-center"
    }
}
