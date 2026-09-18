using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-HONEST-30 (docs/design/MAKERS-MARK.md §11.13): <see cref="ForgeCounterPlayer"/> — the first
/// harness policy that both crafts/stocks (<see cref="BaselinePlayer"/>'s own routine) AND closes
/// real counter sales (<see cref="CounterPlayer"/>'s presenting opener), so decisions 1 ("sell the
/// good one or hold it") and 2 ("price for the sale or the relationship") get their first measured
/// occurrence. Covers purity (same state -> same actions), never submitting an illegal action
/// through the production kernel, and the campaign-level property this policy exists to prove: over
/// a real run it stocks, it closes sales, and at least one close actually moves a hero's mood —
/// the +60 pin bonus <see cref="HaggleResolver"/> has always been ABLE to pay, that no prior policy
/// ever triggered (see this project's own §11.13 measurement: <see cref="CounterPlayer"/> opens
/// 2,000 sessions across 20 seeds and closes zero sales).
/// </summary>
public class ForgeCounterPlayerTests
{
    private static Hero MakeHero(int id, string classId, int gold, int moodPermille = 0, GearSet? gear = null) => new Hero(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        gear ?? GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
    { MoodPermille = moodPermille };

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

    [Fact]
    public void ActionsFor_SameStateTwice_ProducesTheSameActions_EveryCall()
    {
        // Purity (no IO/RNG/clock): calling twice on an IDENTICAL state must be byte-for-byte the
        // same decision, whatever the session shape.
        var sword = MakeItem(1, ItemSlot.Weapon, attack: 6, defense: 0, weight: 3);
        var state = WithShelf(BaseState(Roster(MakeHero(1, "striker", 200)), sword), gold: 0, new ShelfEntry(sword.Id, 50));

        var first = ForgeCounterPlayer.ActionsFor(state);
        var second = ForgeCounterPlayer.ActionsFor(state);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ActionsFor_StrangerWithStandingOffer_AcceptsRatherThanCounters()
    {
        // Decision 2, stranger arm: no read on this hero yet — take their own number.
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
                StandingOfferGold = 82,
            },
        };

        var actions = ForgeCounterPlayer.ActionsFor(state);

        var haggle = Assert.IsType<HaggleResponseAction>(Assert.Single(actions));
        Assert.Equal(HaggleResponseKind.Accept, haggle.Kind);
    }

    [Fact]
    public void ActionsFor_RegularWithStandingOffer_CountersAtTheRoundsCeiling_ThePinRule()
    {
        // Decision 2, regular-or-better arm: this smith reads the hero and pins the price at the
        // round's own ceiling (guaranteed inside the pin window at round 1 — see the class doc).
        var hero = MakeHero(1, "striker", gold: 1000, moodPermille: RelationshipBands.RegularMinMood);
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
                StandingOfferGold = 82,
            },
        };
        Assert.True(RelationshipBands.For(hero.Id, state) >= RelationshipBand.Regular); // test premise

        var trueWillingness = WillingnessModel.TrueWillingness(
            100, hero.Gold, hero.ClassId, interestPermille: 0, moodPermille: hero.MoodPermille,
            traitPermille: TraitEffects.PriceSensitivityPermille(hero));
        var (_, expectedCeiling) = WillingnessModel.Band(trueWillingness, round: 1);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        var haggle = Assert.IsType<HaggleResponseAction>(Assert.Single(actions));
        Assert.Equal(HaggleResponseKind.Counter, haggle.Kind);
        Assert.Equal(expectedCeiling, haggle.Price);
    }

    [Fact]
    public void ActionsFor_NoSessionYet_OpensTheCounter_AlongsideTheMorningRoutine()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100)));

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Contains(new OpenCounterAction(), actions);
    }

    [Fact]
    public void ActionsFor_EmptyShelf_WithActiveCustomer_ClosesInsteadOfStalling()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100))) with
        {
            Counter = CounterState.Empty with { Queue = ImmutableList.Create(new HeroId(1)), Active = new HeroId(1) },
        };

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new CloseCounterAction()), actions);
    }

    [Fact]
    public void ActionsFor_NoActiveCustomer_ClosesTheCounter_NeverThrows()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100))) with
        {
            Counter = CounterState.Empty,
        };

        var actions = ForgeCounterPlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new CloseCounterAction()), actions);
    }

    [Fact]
    public void ActionsFor_ClosedSession_DoesNothing()
    {
        var state = BaseState(Roster(MakeHero(1, "striker", 100))) with
        {
            Counter = CounterState.Empty with { Closed = true },
        };

        Assert.Empty(ForgeCounterPlayer.ActionsFor(state));
    }

    // ---- Full-campaign properties: driven ENTIRELY by the policy, through the SAME production
    // kernel the batch farm and the CLI use, one tick at a time -------------------------------------

    [Fact]
    public void DrivenStandalone_ThroughTheProductionKernel_NeverSubmitsAnIllegalAction()
    {
        // Property, not an instance: whatever the campaign's shape turns out to be on ANY seed,
        // ActionLegality never has to reject one of this policy's own actions.
        var kernel = GameSim.GameComposition.BuildKernel();
        var state = GameSim.GameComposition.NewCampaign(seed: 24);

        while (state.Day <= 30)
        {
            var result = kernel.Tick(state, ForgeCounterPlayer.ActionsFor(state));
            Assert.Empty(result.Rejected);
            state = result.NewState;
        }
    }

    [Fact]
    public void DrivenStandalone_Over30Days_StocksAndClosesSales_AndAtLeastOnePinMovesMood()
    {
        // The property this unit exists to measure (§11.13): CounterPlayer opens 2,000 sessions
        // across 20 seeds and closes ZERO (never crafts/stocks), and BaselinePlayer never opens the
        // counter at all — so HaggleResolver.CloseSale's mood swing had never once fired in this
        // project's history before this policy. Seed 24 is not cherry-picked for a happy path: it is
        // the first of 40 explored seeds (docs/design/MAKERS-MARK.md §11.13 measurement) that closes
        // more than one pinned sale in a 30-day window, so the property is exercised rather than
        // merely possible in principle.
        var kernel = GameSim.GameComposition.BuildKernel();
        var state = GameSim.GameComposition.NewCampaign(seed: 24);

        var everStocked = false;
        var sales = new List<CounterSaleClosed>();
        var moodMovedByAPin = false;

        while (state.Day <= 30)
        {
            var before = state;
            everStocked = everStocked || before.Player.Shelf.Count > 0;

            var result = kernel.Tick(before, ForgeCounterPlayer.ActionsFor(before));
            state = result.NewState;

            foreach (var closed in result.Events.OfType<CounterSaleClosed>())
            {
                sales.Add(closed);
                if (closed.Pinned
                    && before.Heroes.TryGetValue(closed.Hero.Value, out var heroBefore)
                    && state.Heroes.TryGetValue(closed.Hero.Value, out var heroAfter)
                    && heroAfter.MoodPermille != heroBefore.MoodPermille)
                {
                    moodMovedByAPin = true;
                }
            }
        }

        Assert.True(everStocked, "the policy never put a single item on the shelf in 30 days");
        Assert.NotEmpty(sales); // decision 1 + 2's first measured occurrence: a real counter sale closed
        Assert.True(moodMovedByAPin, "no pinned close ever moved the hero's MoodPermille in 30 days");
    }
}
