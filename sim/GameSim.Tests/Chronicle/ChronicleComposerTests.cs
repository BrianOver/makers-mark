using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GameSim.Chronicle;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Chronicle;

/// <summary>
/// P2-MEMORY-13. These guards are phrased against the PROPERTY, not against fifteen literal
/// strings: <see cref="EveryRegisteredPredicate_FiresOnItsOwnFixture"/> iterates
/// <see cref="ChronicleComposer.Registry"/> itself, so a sixteenth predicate added later with no
/// matching fixture here is a red build rather than silent dead copy — the repo has shipped an
/// unreachable threshold before (P2-HONEST-19) and this is the same shape of guard.
/// </summary>
public sealed class ChronicleComposerTests
{
    private static GameState Base(ulong seed = 1) => GameFactory.NewGame(seed);

    private static GameState WithEvents(GameState state, params GameEvent[] events) =>
        state with { EventLog = state.EventLog.AddRange(events) };

    private static GameState WithHeroes(GameState state, params Hero[] heroes) =>
        state with { Heroes = heroes.Aggregate(state.Heroes, (acc, h) => acc.SetItem(h.Id.Value, h)) };

    private static GameState WithItems(GameState state, params Item[] items) =>
        state with { Items = items.Aggregate(state.Items, (acc, i) => acc.SetItem(i.Id.Value, i)) };

    private static GameState WithMemorials(GameState state, params Memorial[] memorials) =>
        state with { Drama = state.Drama with { Memorials = memorials.ToImmutableList() } };

    private static Hero MakeHero(int id, string name) => new(
        new HeroId(id), name, "vanguard", Level: 1, MaxHp: 20, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeItem(int id, string name) => new(
        new ItemId(id), $"recipe-{id}", name, ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(3, 0, 2), new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>One state built to satisfy each registered predicate, keyed by its <c>Id</c>.
    /// Kept in sync with <see cref="ChronicleComposer.Registry"/> BY THE TEST ITSELF — see
    /// <see cref="EveryRegisteredPredicate_FiresOnItsOwnFixture"/>.</summary>
    private static readonly Dictionary<string, Func<GameState>> SatisfyingStates = new()
    {
        ["no-work-turned-a-fight"] = () => WithEvents(Base(),
            new PartyReturned(ImmutableList.Create(new HeroId(1))) { Id = new EventId(1), Day = 1 }),

        ["short-chapter"] = () => WithEvents(Base(),
            new PartyReturned(ImmutableList.Create(new HeroId(1))) { Id = new EventId(1), Day = 1 }),

        ["priced-for-people"] = () => WithEvents(Base(),
            new CounterSaleClosed(new HeroId(1), new ItemId(1), 20, Pinned: true) { Id = new EventId(1), Day = 1 }),

        ["took-the-coin-every-time"] = () => WithEvents(Base(),
            new CounterSaleClosed(new HeroId(1), new ItemId(1), 20, Pinned: false) { Id = new EventId(1), Day = 1 }),

        ["shelf-did-the-talking"] = () => WithEvents(Base(),
            new ItemSold(new ItemId(1), new HeroId(1), 15, FromPlayerShop: true) { Id = new EventId(1), Day = 1 }),

        ["never-reached-into-dark"] = () => WithEvents(Base(),
            new PartyCampReport(
                ImmutableList.Create(new HeroId(1)), CampedBelowFloor: 2, TargetFloor: 3,
                ImmutableSortedDictionary<int, int>.Empty.Add(1, 10),
                ImmutableSortedDictionary<int, int>.Empty.Add(1, 1)) { Id = new EventId(1), Day = 1 }),

        ["held-item-for-hero"] = () => WithEvents(
            WithHeroes(WithItems(Base(), MakeItem(1, "Iron Blade")), MakeHero(1, "Torvald")),
            new ItemCrafted(new ItemId(1), QualityGrade.Fine) { Id = new EventId(1), Day = 1 },
            new ItemSold(new ItemId(1), new HeroId(1), 20, FromPlayerShop: true) { Id = new EventId(2), Day = 6 },
            new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), 3, "x") { Id = new EventId(3), Day = 7 }),

        ["said-goodbye-to-every-name"] = () => WithMemorials(Base(),
            new Memorial(new HeroId(1), "Torvald", 1, "Iron Blade", Honored: true)),

        ["wall-keeps-names-unspoken"] = () => WithMemorials(Base(),
            new Memorial(new HeroId(1), "Torvald", 1, "Iron Blade", Honored: false)),

        ["wall-stayed-bare"] = () => WithEvents(Base(),
            new PartyReturned(ImmutableList.Create(new HeroId(1))) { Id = new EventId(1), Day = 1 }),

        ["gold-pointed-at-floor"] = () => WithEvents(
            WithHeroes(Base(), MakeHero(1, "Brunhilde")),
            new BountyPosted(new BountyId(1), TargetFloor: 4, RewardGold: 50) { Id = new EventId(1), Day = 1 },
            new BountyJudged(new BountyId(1), new HeroId(1), Accepted: true, Reason: "worth it") { Id = new EventId(2), Day = 2 },
            new FloorRecordSet(new HeroId(1), 4) { Id = new EventId(3), Day = 3 }),

        ["sent-supply-to-every-camped-party"] = () => WithEvents(Base(),
            new PartyCampReport(
                ImmutableList.Create(new HeroId(1)), CampedBelowFloor: 2, TargetFloor: 3,
                ImmutableSortedDictionary<int, int>.Empty.Add(1, 10),
                ImmutableSortedDictionary<int, int>.Empty.Add(1, 1)) { Id = new EventId(1), Day = 1 },
            new SupplyDelivered(new HeroId(1), new ItemId(1), Fee: 5) { Id = new EventId(2), Day = 2 }),

        ["finished-every-commission"] = () => WithEvents(Base(),
            new CommissionFulfilled(new HeroId(1), new ItemId(1), Premium: 10) { Id = new EventId(1), Day = 1 }),

        ["proven-kills-carry-your-mark"] = () => WithEvents(
            WithHeroes(Base(), MakeHero(1, "Torvald")),
            new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), 2, "x") { Id = new EventId(1), Day = 1 }),

        ["roster-set-floor-records-unasked"] = () => WithEvents(Base(),
            new FloorRecordSet(new HeroId(1), 3) { Id = new EventId(1), Day = 1 }),
    };

    [Fact]
    public void Registry_HasExactlyFifteenPredicates()
    {
        Assert.Equal(15, ChronicleComposer.Registry.Count);
    }

    [Fact]
    public void EveryRegisteredPredicate_FiresOnItsOwnFixture()
    {
        foreach (var entry in ChronicleComposer.Registry)
        {
            Assert.True(
                SatisfyingStates.TryGetValue(entry.Id, out var build),
                $"Predicate '{entry.Id}' has no satisfying-state fixture in this test — " +
                "a predicate nothing can ever prove is silent dead copy.");

            var result = entry.Evaluate(build!());

            Assert.False(
                string.IsNullOrEmpty(result),
                $"Predicate '{entry.Id}' did not fire on the state built to satisfy it.");
        }
    }

    [Fact]
    public void EveryPredicate_RendersWithNoUnsubstitutedPlaceholder()
    {
        var placeholder = new Regex(@"\{[^{}]*\}");

        foreach (var entry in ChronicleComposer.Registry)
        {
            var text = entry.Evaluate(SatisfyingStates[entry.Id]());
            Assert.NotNull(text);
            Assert.DoesNotMatch(placeholder, text!);
        }

        Assert.DoesNotMatch(placeholder, ChronicleComposer.Closer);
    }

    private static GameState RichState()
    {
        var state = WithHeroes(
            WithItems(Base(), MakeItem(1, "Iron Blade")),
            MakeHero(1, "Torvald"), MakeHero(2, "Brunhilde"));

        return WithMemorials(
            WithEvents(state,
                new ItemCrafted(new ItemId(1), QualityGrade.Fine) { Id = new EventId(1), Day = 1 },
                new ItemSold(new ItemId(1), new HeroId(1), 20, FromPlayerShop: true) { Id = new EventId(2), Day = 6 },
                new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), 3, "x") { Id = new EventId(3), Day = 7 },
                new BountyPosted(new BountyId(1), TargetFloor: 5, RewardGold: 50) { Id = new EventId(4), Day = 1 },
                new BountyJudged(new BountyId(1), new HeroId(2), Accepted: true, Reason: "worth it") { Id = new EventId(5), Day = 2 },
                new FloorRecordSet(new HeroId(2), 5) { Id = new EventId(6), Day = 3 }),
            new Memorial(new HeroId(3), "Sable", 1, "Old Shield", Honored: false));
    }

    [Fact]
    public void Compose_PicksTheThreeMostSpecific_TiesByDeclaredOrder()
    {
        var result = ChronicleComposer.Compose(RichState());

        // Six predicates fire on this state; three of them (held-item-for-hero,
        // gold-pointed-at-floor, proven-kills-carry-your-mark) name a hero AND a number — the top
        // tier — so all three fill every scored slot, in their DECLARED order (registry indices
        // 6, 10, 13), ahead of the number-only and neither-tier predicates that also fired
        // (wall-keeps-names-unspoken, roster-set-floor-records-unasked, shelf-did-the-talking).
        Assert.Equal(
            new[]
            {
                "You held Iron Blade 5 days for Torvald. It mattered when it left.",
                "Your gold pointed at floor 5. Brunhilde followed it down and stayed.",
                "1 proven kills carry your mark and Torvald's name.",
                ChronicleComposer.Closer,
            },
            result);
    }

    [Fact]
    public void Compose_NumberOnlyBeatsNeither_WhenNoHeroAndNumberPredicateFires()
    {
        var state = WithMemorials(
            WithEvents(Base(),
                new ItemSold(new ItemId(1), new HeroId(1), 15, FromPlayerShop: true) { Id = new EventId(1), Day = 1 }),
            new Memorial(new HeroId(2), "Sable", 1, "Old Shield", Honored: false));

        var result = ChronicleComposer.Compose(state);

        Assert.Equal(
            new[]
            {
                "The wall keeps 1 names. You never said one of them aloud.",
                "You let the shelf do your talking.",
                ChronicleComposer.Closer,
            },
            result);
    }

    [Fact]
    public void Compose_IsDeterministic_AcrossRepeatedCalls()
    {
        var first = ChronicleComposer.Compose(RichState());
        var second = ChronicleComposer.Compose(RichState());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compose_CloserAlwaysPrintsLast_AndIsNeverOneOfTheScoredThree()
    {
        foreach (var state in new[] { Base(), RichState() })
        {
            var result = ChronicleComposer.Compose(state);

            Assert.Equal(ChronicleComposer.Closer, result[^1]);
            Assert.DoesNotContain(ChronicleComposer.Closer, result.Take(result.Count - 1));
            Assert.True(result.Count <= 4);
        }
    }

    [Fact]
    public void Compose_ZeroBeatState_PrintsEpithetAndShortChapter_ByOrdinaryScoring()
    {
        var state = WithEvents(Base(),
            new PartyReturned(ImmutableList.Create(new HeroId(1))) { Id = new EventId(1), Day = 1 });

        var result = ChronicleComposer.Compose(state);

        Assert.Contains(
            "No work of yours turned a fight this season. The Mine neither knows nor cares. The town does.",
            result);
        Assert.Contains("This chapter is short. The next campaign's doesn't have to be.", result);
        Assert.Equal(ChronicleComposer.Closer, result[^1]);
    }

    [Fact]
    public void Compose_FreshState_OnlyPrintsTheCloser()
    {
        var result = ChronicleComposer.Compose(Base());

        Assert.Equal(ImmutableList.Create(ChronicleComposer.Closer), result);
    }
}
