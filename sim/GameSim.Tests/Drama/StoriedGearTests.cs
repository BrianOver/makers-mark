using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;
using Xunit;

namespace GameSim.Tests.Drama;

/// <summary>
/// M2b: the storied-gear promotion, which <see cref="ShoppingAi"/> has been making silently for
/// months. These tests pin the QUERY that finally lets a screen say it — that it agrees with the
/// gate exactly, that it respects each hero's OWN trait-shifted threshold rather than the bare
/// constant, that it writes nothing, and that its copy reads as recorded facts rather than credit.
///
/// <para>Traits are derived from (<see cref="HeroId"/>, name) and never stored
/// (<see cref="TraitRegistry.TraitsFor"/>), so Sentimental/Practical fixtures are FOUND by scanning
/// ids against the production hash — the <c>TraitDivergenceTests</c> idiom, not a second
/// mechanism.</para>
/// </summary>
public class StoriedGearTests
{
    private const string FixtureNamePrefix = "Sto";

    // ── the threshold: below is ordinary, at is storied ────────────────────────────────────────

    [Fact]
    public void OneDeedBelowTheBearersThreshold_IsNotStoried()
    {
        var (state, item) = StateWith(NeutralHero(deeds: BaseThreshold - 1, out var hero));

        Assert.Equal(ShoppingAi.SentimentalDeedThreshold, StoriedGear.ThresholdFor(hero));
        Assert.Null(StoriedGear.For(state, item));
        Assert.Empty(StoriedGear.All(state));
    }

    [Fact]
    public void AtTheBearersThreshold_IsStoried_AndNamesTheRecordedFacts()
    {
        var (state, item) = StateWith(NeutralHero(deeds: BaseThreshold, out var hero));

        var storied = StoriedGear.For(state, item);

        Assert.NotNull(storied);
        Assert.Equal(item, storied!.Item);
        Assert.Equal(hero.Id, storied.Bearer);
        Assert.Equal(hero.Name, storied.BearerName);
        Assert.Equal(BaseThreshold, storied.Deeds);
        Assert.Equal(BaseThreshold, storied.Threshold);
        Assert.Single(StoriedGear.All(state));
    }

    // ── the threshold is the HERO'S, never the bare constant ───────────────────────────────────

    [Fact]
    public void TheThresholdRespectedIsTheHerosOwn_SentimentalAndPractical_DisagreeAboutTheSameItem()
    {
        // One deed: below the base constant (3), at/above Sentimental's shifted threshold
        // (max(1, 3-2) = 1), and unreachably below Practical's (3 + 1000).
        const int deeds = 1;
        var (sentimentalState, item) = StateWith(TraitHero(TraitId.Sentimental, deeds, out var sentimental));
        var (practicalState, _) = StateWith(TraitHero(TraitId.Practical, deeds, out var practical));

        Assert.NotEqual(ShoppingAi.SentimentalDeedThreshold, StoriedGear.ThresholdFor(sentimental));
        Assert.NotEqual(ShoppingAi.SentimentalDeedThreshold, StoriedGear.ThresholdFor(practical));
        Assert.True(StoriedGear.ThresholdFor(sentimental) < StoriedGear.ThresholdFor(practical));

        // The whole point: same object, same deed count, two heroes who disagree about it.
        Assert.NotNull(StoriedGear.For(sentimentalState, item));
        Assert.Null(StoriedGear.For(practicalState, item));

        // And the render agrees with the gate it is rendering — the Sentimental hero really does
        // refuse the marginal upgrade the Practical one takes.
        var upgrade = MakeWeapon(WornItemId + 1, attack: WornAttack + 2, name: "Better Blade");
        var catalog = Catalog(MakeWorn(), upgrade);
        var sentimentalVerdict = ShoppingAi.EvaluateItem(sentimental, upgrade, price: 5, catalog);
        var practicalVerdict = ShoppingAi.EvaluateItem(practical, upgrade, price: 5, catalog);

        Assert.Equal(PassReasonKind.Sentimental, sentimentalVerdict.PassReason);
        Assert.Equal(ShoppingVerdictKind.Buy, practicalVerdict.Kind);
    }

    [Fact]
    public void ADeedCountTheBareConstantWouldCall_Storied_IsNotStoriedForAPracticalBearer()
    {
        // Guards the specific mistake this unit exists to avoid: reading
        // ShoppingAi.SentimentalDeedThreshold directly instead of the hero's shifted value. Four
        // deeds clears the raw constant comfortably — and still means nothing to a Practical hero.
        var (state, item) = StateWith(TraitHero(TraitId.Practical, deeds: BaseThreshold + 1, out var practical));

        Assert.True(BaseThreshold + 1 >= ShoppingAi.SentimentalDeedThreshold);
        Assert.True(BaseThreshold + 1 < StoriedGear.ThresholdFor(practical));
        Assert.Null(StoriedGear.For(state, item));
    }

    // ── what is NOT storied ────────────────────────────────────────────────────────────────────

    [Fact]
    public void GearNobodyIsWearing_IsNotStoried_EvenWithDeedsRecorded()
    {
        var hero = NeutralHero(deeds: BaseThreshold + 5, out _) with { Gear = GearSet.Empty };
        var state = StateWith(hero).State;

        Assert.Null(StoriedGear.For(state, new ItemId(WornItemId)));
        Assert.Empty(StoriedGear.All(state));
    }

    [Fact]
    public void AFallenBearersGear_IsNotStoried_TheGateItRendersCanNeverFireAgain()
    {
        var hero = NeutralHero(deeds: BaseThreshold, out _) with { Alive = false, DiedOnDay = 4 };
        var state = StateWith(hero).State;

        Assert.Null(StoriedGear.For(state, new ItemId(WornItemId)));
        Assert.Empty(StoriedGear.All(state));
    }

    // ── the query writes nothing ───────────────────────────────────────────────────────────────

    [Fact]
    public void NoSimStateIsWritten_WholeStateFingerprintIsUnchanged()
    {
        // The WHOLE state through the save codec, not a hand-listed field set: a hand-listed
        // fingerprint silently stops covering new contract fields the day one lands, and the
        // failure looks like a game bug (ConsequenceProbe.Fingerprint's own account of it).
        var (state, item) = StateWith(NeutralHero(deeds: BaseThreshold, out var hero));
        var before = SaveCodec.Serialize(state);

        StoriedGear.ThresholdFor(hero);
        StoriedGear.DeedsFor(hero, item);
        var info = StoriedGear.For(state, item);
        StoriedGear.All(state);
        StoriedGear.Clause(info);
        StoriedGear.FightsWord(3);

        Assert.Equal(before, SaveCodec.Serialize(state));
    }

    // ── the copy: recorded facts, never credit ─────────────────────────────────────────────────

    [Fact]
    public void Clause_StatesTheRecordedFacts_AndCarriesNoScoreRatioOrRanking()
    {
        var (state, item) = StateWith(NeutralHero(deeds: 4, out var hero));

        var clause = StoriedGear.Clause(StoriedGear.For(state, item));

        Assert.Contains("Storied", clause);
        Assert.Contains(hero.Name, clause);
        Assert.Contains("4 fights", clause);

        // Law 4 tripwire (§2 "no participation credit"): the moment this line acquires a total, a
        // ratio, a medal, a percentage of party contribution, a ranking or a score, it has stopped
        // being a recorded fact. The register is P2-PROOF's memorial: "The blow read 15."
        foreach (var banned in new[]
                 {
                     "%", "/", "total", "score", "rank", "best", "top ", "points", "credit",
                     "contribution", "rating", "tier", "×", "out of", "MVP", "award",
                 })
        {
            Assert.DoesNotContain(banned, clause, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Clause_ForOrdinaryGear_IsEmpty_AnHonestEmptyStateNotAFallbackLine()
    {
        var (state, item) = StateWith(NeutralHero(deeds: 0, out _));

        Assert.Equal(string.Empty, StoriedGear.Clause(StoriedGear.For(state, item)));
    }

    [Fact]
    public void FightsWord_IsSingularAtOneDeed_WhichASentimentalBearerCanActuallyReach()
    {
        TraitHero(TraitId.Sentimental, deeds: 1, out var sentimental);

        Assert.Equal(1, StoriedGear.ThresholdFor(sentimental));
        Assert.Equal("fight", StoriedGear.FightsWord(1));
        Assert.Equal("fights", StoriedGear.FightsWord(2));
    }

    // ── determinism of the listing ─────────────────────────────────────────────────────────────

    [Fact]
    public void All_ListsByHeroIdThenFixedSlotOrder_SoTwoRunsAgree()
    {
        var weapon = MakeWorn();
        var armor = new Item(
            new ItemId(WornItemId + 5), "worn-mail", "Worn Mail", ItemSlot.Armor, QualityGrade.Common,
            new ItemStats(0, 4, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var memories = ImmutableList.Create(
            new ItemMemory(weapon.Id, Kills: BaseThreshold, Saves: 0),
            new ItemMemory(armor.Id, Kills: 0, Saves: BaseThreshold));

        var first = NeutralHero(deeds: 0, out _) with
        {
            Gear = GearSet.Empty.WithSlot(ItemSlot.Weapon, weapon.Id).WithSlot(ItemSlot.Armor, armor.Id),
            Memories = memories,
        };
        var second = first with { Id = new HeroId(first.Id.Value + 500), Name = first.Name + "-b" };

        var state = GameFactory.NewGame(seed: 7301) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(first.Id.Value, first)
                .Add(second.Id.Value, second),
            Items = Catalog(weapon, armor),
        };

        var listed = StoriedGear.All(state);

        Assert.Equal(
            new[] { (first.Id, weapon.Id), (first.Id, armor.Id), (second.Id, weapon.Id), (second.Id, armor.Id) },
            listed.Select(s => (s.Bearer, s.Item)).ToArray());
        Assert.Equal(listed, StoriedGear.All(state)); // same state in, same list out
    }

    // ── fixtures ───────────────────────────────────────────────────────────────────────────────

    private const int WornItemId = 601;
    private const int WornAttack = 6;
    private static int BaseThreshold => ShoppingAi.SentimentalDeedThreshold;

    private static Item MakeWorn() => MakeWeapon(WornItemId, WornAttack, "Emberfang");

    private static Item MakeWeapon(int id, int attack, string name) => new(
        new ItemId(id), "worn-blade", name, ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.Aggregate(ImmutableSortedDictionary<int, Item>.Empty, (map, item) => map.Add(item.Id.Value, item));

    /// <summary>A hero holding NEITHER Sentiment trait, so <see cref="StoriedGear.ThresholdFor"/>
    /// returns the base constant — the only fixture where the two can legitimately be equal.</summary>
    private static Hero NeutralHero(int deeds, out Hero hero)
    {
        var (id, name) = FindNeutralSentimentHero();
        hero = MakeHero(id, name, deeds);
        return hero;
    }

    private static Hero TraitHero(TraitId wanted, int deeds, out Hero hero)
    {
        var (id, name) = FindHero(wanted);
        hero = MakeHero(id, name, deeds);
        return hero;
    }

    private static Hero MakeHero(HeroId id, string name, int deeds) => new(
        id, name, "vanguard", Level: 1, MaxHp: 25, Gold: 1000,
        GearSet.Empty.WithSlot(ItemSlot.Weapon, new ItemId(WornItemId)),
        ImmutableList.Create(new ItemMemory(new ItemId(WornItemId), Kills: deeds, Saves: 0)),
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static (GameState State, ItemId Item) StateWith(Hero hero)
    {
        var worn = MakeWorn();
        var state = GameFactory.NewGame(seed: 7300) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = Catalog(worn),
        };
        return (state, worn.Id);
    }

    private static (HeroId Id, string Name) FindHero(TraitId wanted, int maxId = 2000)
    {
        for (var id = 1; id <= maxId; id++)
        {
            var heroId = new HeroId(id);
            var name = $"{FixtureNamePrefix}{id}";
            if (TraitRegistry.TraitsFor(heroId, name).Contains(wanted))
            {
                return (heroId, name);
            }
        }

        throw new Xunit.Sdk.XunitException($"No hero id in 1..{maxId} derives trait {wanted}.");
    }

    private static (HeroId Id, string Name) FindNeutralSentimentHero(int maxId = 2000)
    {
        for (var id = 1; id <= maxId; id++)
        {
            var heroId = new HeroId(id);
            var name = $"{FixtureNamePrefix}{id}";
            var traits = TraitRegistry.TraitsFor(heroId, name);
            if (!traits.Contains(TraitId.Sentimental) && !traits.Contains(TraitId.Practical))
            {
                return (heroId, name);
            }
        }

        throw new Xunit.Sdk.XunitException($"No hero id in 1..{maxId} is neutral on the Sentiment axis.");
    }
}
