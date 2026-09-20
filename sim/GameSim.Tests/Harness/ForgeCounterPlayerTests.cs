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
    public void ActionsFor_RegularWithStandingOffer_OddIdDaySum_CountersAboveTheCeiling_TheFleeceArm()
    {
        // P2-HONEST-35 (§11.14): decision 2's second arm. Hero 2 on day 1 sums to an ODD id+day —
        // the fleece arm — deliberately the opposite parity from the pin test above (hero 1, day 1,
        // an EVEN sum), so the two tests together prove the split is real rather than one branch
        // dead code.
        var hero = MakeHero(2, "striker", gold: 1000, moodPermille: RelationshipBands.RegularMinMood);
        var state = BaseState(Roster(hero)) with
        {
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 100)) },
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, MakeItem(1, ItemSlot.Weapon, 6, 0, 3)),
            Counter = CounterState.Empty with
            {
                Queue = ImmutableList.Create(new HeroId(2)),
                Active = new HeroId(2),
                Round = 1,
                Presented = new ItemId(1),
                StandingOfferGold = 82,
            },
        };
        Assert.True(RelationshipBands.For(hero.Id, state) >= RelationshipBand.Regular); // test premise
        Assert.Equal(1, state.Day); // 2 + 1 = 3, odd — the fleece arm's own premise

        var trueWillingness = WillingnessModel.TrueWillingness(
            100, hero.Gold, hero.ClassId, interestPermille: 0, moodPermille: hero.MoodPermille,
            traitPermille: TraitEffects.PriceSensitivityPermille(hero));
        var (_, ceiling) = WillingnessModel.Band(trueWillingness, round: 1);

        var actions = ForgeCounterPlayer.ActionsFor(state);

        var haggle = Assert.IsType<HaggleResponseAction>(Assert.Single(actions));
        Assert.Equal(HaggleResponseKind.Counter, haggle.Kind);
        Assert.True(haggle.Price > ceiling, $"fleece price {haggle.Price}g did not clear the ceiling {ceiling}g");
        Assert.True(haggle.Price <= hero.Gold, $"fleece price {haggle.Price}g exceeds what hero 2 can afford ({hero.Gold}g)");
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

    [Fact]
    public void DrivenAcrossASeedSweep_ClosesAtLeastOneFleecedSale()
    {
        // P2-HONEST-35 (§11.14): §11.14 measured 551 closed sales — 270 pinned, ZERO fleeced —
        // across the whole 20-seed x 100-day corpus. This is the property that closes that gap:
        // over a real sweep, driven through the SAME production kernel the batch farm uses, the
        // fleece arm actually fires at least once.
        var kernel = GameSim.GameComposition.BuildKernel();
        var fleeced = 0;

        foreach (var seed in Enumerable.Range(1, 10).Select(i => (ulong)i))
        {
            var state = GameSim.GameComposition.NewCampaign(seed);
            while (state.Day <= 40)
            {
                var result = kernel.Tick(state, ForgeCounterPlayer.ActionsFor(state));
                state = result.NewState;
                fleeced += result.Events.OfType<CounterSaleClosed>().Count(s => s.Fleeced);
            }
        }

        Assert.True(fleeced > 0, "no fleeced counter sale closed across 10 seeds x 40 days");
    }

    [Fact]
    public void DrivenAcrossASeedSweep_AFleecedSale_ReachesMoodGossipAndBoycott()
    {
        // P2-HONEST-35 (§11.14): closing the sale is only half the gap — §11.14 also found
        // WillingnessModel.FleeceMoodPenalty, the TavernPack fleece gossip line, and
        // NeedsSystem's boycott bias had NEVER been reached by any harness, because nothing had
        // ever stamped a Fleeced sale for them to react to. This drives the same fleece arm across
        // a wider sweep and confirms all three surfaces are now live — mood actually drops on a
        // fleece, the tavern actually cites one, and the roster actually reaches a boycott
        // somewhere in the same runs (boycotting is driven by unmet-demand streaks, not the fleece
        // itself — CounterSaleClosed never resets ItemSold's streak — so this does not claim the
        // fleece CAUSES the boycott, only that both surfaces are reached together, honestly, in
        // the runs this unit adds).
        var kernel = GameSim.GameComposition.BuildKernel();
        var sawFleeceMoodPenalty = false;
        var sawFleeceGossipLine = false;
        var sawBoycott = false;

        foreach (var seed in Enumerable.Range(1, 10).Select(i => (ulong)i))
        {
            var state = GameSim.GameComposition.NewCampaign(seed);
            var fleecedEventIds = new HashSet<int>();

            while (state.Day <= 60)
            {
                var before = state;
                var result = kernel.Tick(before, ForgeCounterPlayer.ActionsFor(before));
                state = result.NewState;

                foreach (var sale in result.Events.OfType<CounterSaleClosed>().Where(s => s.Fleeced))
                {
                    fleecedEventIds.Add(sale.Id.Value);
                    if (before.Heroes.TryGetValue(sale.Hero.Value, out var heroBefore)
                        && state.Heroes.TryGetValue(sale.Hero.Value, out var heroAfter)
                        && heroAfter.MoodPermille < heroBefore.MoodPermille)
                    {
                        sawFleeceMoodPenalty = true;
                    }
                }

                foreach (var gossip in result.Events.OfType<GossipEmitted>())
                {
                    if (fleecedEventIds.Contains(gossip.Source.Value))
                    {
                        sawFleeceGossipLine = true;
                    }
                }

                if (!sawBoycott && state.Heroes.Values.Any(h => h.Alive && NeedsSystem.IsBoycotting(h.Id, state)))
                {
                    sawBoycott = true;
                }
            }
        }

        Assert.True(sawFleeceMoodPenalty, "a fleeced sale never dropped the hero's MoodPermille across the sweep");
        Assert.True(sawFleeceGossipLine, "no gossip line was ever emitted citing a fleeced counter sale");
        Assert.True(sawBoycott, "no hero ever reached a boycott across the sweep");
    }
}
