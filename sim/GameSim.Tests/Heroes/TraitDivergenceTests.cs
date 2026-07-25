using System.Collections.Immutable;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Phase B (B2, Gate B slice, R-B5): proves the 10 traits carry REAL shop teeth — for each of the
/// five axes, two heroes who differ (for this fixture's purposes) only in which side of that axis
/// they hold produce a DIFFERENT verdict or typed reason for the IDENTICAL shelf item on the
/// IDENTICAL state. Traits are derived (<see cref="TraitRegistry.TraitsFor"/>), never stored, so
/// each fixture is found by scanning HeroIds for the one wanted trait rather than constructed by
/// hand — <see cref="FindHeroId"/> is a pure, deterministic search over the SAME production hash,
/// not a second mechanism. Every fixture is a floor-0 rookie with empty gear (unless the fixture is
/// specifically about worn-gear sentiment) so the OTHER (unrelated) trait each search happens to
/// land on can never interfere: the veteran-quality gate is floor-gated regardless of trait
/// (KD3 no-softlock, untouched by B2), and an empty <see cref="GearSet"/> can never trip the
/// sentimental gate (no worn item to grow attached to).
/// </summary>
public class TraitDivergenceTests
{
    private const string FixtureNamePrefix = "Div";

    /// <summary>Scans HeroIds 1.. for the first one whose derived traits
    /// (<see cref="TraitRegistry.TraitsFor"/>, name fixed to <see cref="FixtureNamePrefix"/> + id)
    /// contain <paramref name="wanted"/>. Deterministic and total for any trait in the 10-trait
    /// catalogue (every axis/side pair recurs within a small id range).</summary>
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

        throw new InvalidOperationException($"No HeroId in 1..{maxId} derives {wanted} under the '{FixtureNamePrefix}' prefix.");
    }

    // Neutral (factor 1000) class by default — WillingnessModelTests' pinned table precedent —
    // so the Price Sensitivity test's band math is exactly (100, 82, 98) before any trait offset.
    private static Hero MakeRookie(TraitId trait, int gold, GearSet? gear = null, ImmutableList<ItemMemory>? memories = null, string classId = ClassRegistry.StrikerId)
    {
        var (id, name) = FindHero(trait);
        return new Hero(
            id, name, classId, Level: 1, MaxHp: 25, Gold: gold,
            gear ?? GearSet.Empty, memories ?? ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
    }

    private static Hero MakeVeteran(TraitId trait, int gold, int floor) =>
        MakeRookie(trait, gold) with { DeepestFloorReached = floor };

    private static Item MakeWeapon(int id, int attack, QualityGrade quality = QualityGrade.Common, string name = "Test Blade") => new(
        new ItemId(id), "test-recipe", name, ItemSlot.Weapon, quality,
        new ItemStats(attack, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static ImmutableSortedDictionary<int, Item> Catalog(params Item[] items) =>
        items.ToImmutableSortedDictionary(i => i.Id.Value, i => i);

    // ---- Axis 1: Quality Demand (Discerning / Unfussy) — ShoppingAi's veteran gate -------------

    [Fact]
    public void QualityDemand_Discerning_RefusesCommon_ThatUnfussyBuys_SameVeteranFloor_SameItem()
    {
        var discerning = MakeVeteran(TraitId.Discerning, gold: 1000, floor: ShoppingAi.VeteranFloorThreshold);
        var unfussy = MakeVeteran(TraitId.Unfussy, gold: 1000, floor: ShoppingAi.VeteranFloorThreshold);
        var item = MakeWeapon(1, attack: 9, quality: QualityGrade.Common);
        var catalog = Catalog(item);

        var discerningVerdict = ShoppingAi.EvaluateItem(discerning, item, price: 10, catalog);
        var unfussyVerdict = ShoppingAi.EvaluateItem(unfussy, item, price: 10, catalog);

        Assert.Equal(ShoppingVerdictKind.Pass, discerningVerdict.Kind);
        Assert.Equal(PassReasonKind.QualityTooLow, discerningVerdict.PassReason);

        Assert.Equal(ShoppingVerdictKind.Buy, unfussyVerdict.Kind);
    }

    // ---- Axis 2: Sentiment (Sentimental / Practical) — ShoppingAi's storied-gear gate ----------

    [Fact]
    public void Sentiment_Sentimental_ClingsToLightlyStoriedGear_ThatPractical_TradesAwayFreely()
    {
        var worn = MakeWeapon(10, attack: 6, name: "Worn Blade");
        var upgrade = MakeWeapon(11, attack: 8, name: "Better Blade"); // +2 gain: below SentimentalMinDisplacementGain (5)
        var deeds = ImmutableList.Create(new ItemMemory(worn.Id, Kills: 1, Saves: 0)); // 1 deed: below base
                                                                                        // threshold (3) but at/above Sentimental's shifted threshold (max(1, 3-2)=1)

        var sentimental = MakeRookie(TraitId.Sentimental, gold: 1000, gear: GearSet.Empty.WithSlot(ItemSlot.Weapon, worn.Id), memories: deeds);
        var practical = MakeRookie(TraitId.Practical, gold: 1000, gear: GearSet.Empty.WithSlot(ItemSlot.Weapon, worn.Id), memories: deeds);
        var catalog = Catalog(worn, upgrade);

        var sentimentalVerdict = ShoppingAi.EvaluateItem(sentimental, upgrade, price: 5, catalog);
        var practicalVerdict = ShoppingAi.EvaluateItem(practical, upgrade, price: 5, catalog);

        Assert.Equal(ShoppingVerdictKind.Pass, sentimentalVerdict.Kind);
        Assert.Equal(PassReasonKind.Sentimental, sentimentalVerdict.PassReason);

        Assert.Equal(ShoppingVerdictKind.Buy, practicalVerdict.Kind); // Practical's threshold is pushed far out of reach
    }

    // ---- Axis 3: Consumable Stocking (Prepared / Reckless) — HeroShoppingSystem's restock gate --

    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Item MakeSalve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), Mark: null, ImmutableList<ItemHistoryEntry>.Empty,
        new ConsumableEffect(ConsumableKind.Heal, 6));

    [Fact]
    public void ConsumableStocking_Prepared_RestocksWithAnEmptyPack_ThatReckless_Refuses()
    {
        var salve = MakeSalve(1);
        var prepared = MakeRookie(TraitId.Prepared, gold: 100);
        var reckless = MakeRookie(TraitId.Reckless, gold: 100);

        GameState Setup(Hero hero) => GameFactory.NewGame(seed: 500) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = Catalog(salve),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(salve.Id, 5)) },
        };

        var system = new HeroShoppingSystem();
        var rng = new Pcg32(GameFactory.NewGame(seed: 500).Rng);

        var preparedSink = new TestSink();
        var preparedAfter = system.Process(Setup(prepared), rng, preparedSink);
        var recklessSink = new TestSink();
        var recklessAfter = system.Process(Setup(reckless), rng, recklessSink);

        Assert.Single(preparedSink.Events.OfType<ItemSold>());
        Assert.Single(preparedAfter.Heroes[prepared.Id.Value].Pack);

        Assert.Empty(recklessSink.Events.OfType<ItemSold>());
        Assert.Empty(recklessAfter.Heroes[reckless.Id.Value].Pack);
    }

    // ---- Axis 4: Haggle Patience (Patient / Stubborn) — the live counter's patience budget ------

    private static GameKernel CounterKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(new CounterHandlers()));

    [Fact]
    public void HagglePatience_Patient_SurvivesTwoHoldFirms_ThatStubborn_WalksOn()
    {
        var sword = MakeWeapon(1, attack: 6, name: "Iron Sword");

        GameState Setup(Hero hero) => GameFactory.NewGame(seed: 900) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = Catalog(sword),
            Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 100)) },
        };

        GameState RunTwoHoldFirms(Hero hero, out ImmutableList<GameEvent> events)
        {
            var kernel = CounterKernel();
            var state = kernel.Tick(Setup(hero), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
            var second = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm)));
            events = second.Events;
            return second.NewState;
        }

        var patient = MakeRookie(TraitId.Patient, gold: 1000);
        var patientEnd = RunTwoHoldFirms(patient, out var patientEvents);
        Assert.Empty(patientEvents.OfType<CustomerWalked>()); // Patient still has a round left (4 - 2 = 2)
        Assert.False(patientEnd.Counter!.Closed);

        var stubborn = MakeRookie(TraitId.Stubborn, gold: 1000);
        var stubbornEnd = RunTwoHoldFirms(stubborn, out var stubbornEvents);
        Assert.Single(stubbornEvents.OfType<CustomerWalked>()); // Stubborn's patience (2) is exhausted after 2 HoldFirms
    }

    // ---- Axis 5: Price Sensitivity (Thrifty / Spendthrift) — WillingnessModel's counter ceiling --

    [Fact]
    public void PriceSensitivity_Spendthrift_AcceptsAsAPin_ThePriceThatFleecesThrifty()
    {
        var sword = MakeWeapon(1, attack: 6, name: "Iron Sword");

        GameState Setup(Hero hero) => GameFactory.NewGame(seed: 900) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = Catalog(sword),
            Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 100)) },
        };

        (CounterSaleClosed Sale, int Mood) RunCounterAt(Hero hero, int price)
        {
            var kernel = CounterKernel();
            var state = kernel.Tick(Setup(hero), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
            var result = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.Counter, price)));
            var sale = Assert.Single(result.Events.OfType<CounterSaleClosed>());
            return (sale, result.NewState.Heroes[hero.Id.Value].MoodPermille);
        }

        var spendthrift = MakeRookie(TraitId.Spendthrift, gold: 1000);
        var thrifty = MakeRookie(TraitId.Thrifty, gold: 1000);

        // Neutral "striker" class (factor 1000) at list price 100 has true willingness 100 BEFORE
        // any trait offset (WillingnessModelTests' pinned table: round-1 floor 82, ceiling 98).
        // Spendthrift's +90 permille bonus lifts the factor to 1090 -> true willingness 109 ->
        // round-1 ceiling 106, pin window [102,115]. Thrifty's -90 permille penalty drops the
        // factor to 910 -> true willingness 91 -> round-1 ceiling 89. A SINGLE counter price of 105
        // lands inside Spendthrift's pin window (a clean pin) while exceeding Thrifty's ceiling of
        // 89 (a fleece) — same item, same list price, same round, opposite outcomes.
        var spendthriftResult = RunCounterAt(spendthrift, 105);
        var thriftyResult = RunCounterAt(thrifty, 105);

        Assert.True(spendthriftResult.Sale.Pinned);
        Assert.True(spendthriftResult.Mood > 0);

        Assert.False(thriftyResult.Sale.Pinned);
        Assert.True(thriftyResult.Mood < 0); // fleeced — the counter penalty landed instead of a pin bonus
    }

    // ---- Trait-registry conformance (R-B5) ------------------------------------------------------

    [Fact]
    public void Registry_HasExactlyTenTraits_FiveOpposingAxes()
    {
        Assert.Equal(10, TraitRegistry.All.Length);

        var byAxis = TraitRegistry.All.GroupBy(t => t.Axis).ToList();
        Assert.Equal(5, byAxis.Count);
        Assert.All(byAxis, g => Assert.Equal(2, g.Count()));

        // Every trait id is distinct — no duplicate entries slipped into the catalogue.
        Assert.Equal(10, TraitRegistry.All.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void Registry_EveryTrait_HasATemplatedNonEmptyTooltipAndDisplayName()
    {
        foreach (var trait in TraitRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(trait.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(trait.Tooltip));
        }
    }

    [Fact]
    public void Registry_EveryTrait_HasANonZeroTooth()
    {
        // One representative hero per trait (rookie, empty gear — no confound); asserts each
        // trait's own knob moves AWAY from the neutral/baseline value TraitEffects would otherwise
        // return. Mirrors each axis's dedicated divergence test above, collected in one place as
        // the "all 10 traits carry teeth" conformance sweep (R-B5).
        foreach (var trait in TraitRegistry.All)
        {
            var (id, name) = FindHero(trait.Id);
            var hero = new Hero(
                id, name, "vanguard", Level: 1, MaxHp: 25, Gold: 1000,
                GearSet.Empty, ImmutableList<ItemMemory>.Empty,
                Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

            var hasTooth = trait.Axis switch
            {
                TraitAxis.PriceSensitivity => TraitEffects.PriceSensitivityPermille(hero) != 0,
                TraitAxis.QualityDemand => TraitEffects.VeteranMinQualityGradeFor(hero, QualityGrade.Common) != QualityGrade.Common,
                TraitAxis.Sentiment => TraitEffects.SentimentalDeedThresholdFor(hero, ShoppingAi.SentimentalDeedThreshold) != ShoppingAi.SentimentalDeedThreshold,
                TraitAxis.HagglePatience => TraitEffects.PatienceRoundsFor(hero, WillingnessModel.InitialPatienceRounds) != WillingnessModel.InitialPatienceRounds,
                TraitAxis.ConsumableStocking => TraitEffects.ConsumableStockTargetFor(hero) != TraitEffects.BaselineStockTarget,
                _ => false,
            };

            Assert.True(hasTooth, $"{trait.Id} ({trait.Axis}) produced no change from the neutral baseline.");
        }
    }

    [Fact]
    public void TraitsFor_NeverReturnsATraitAndItsOpposite_AcrossASweepOfHeroIds()
    {
        var opposites = new Dictionary<TraitId, TraitId>
        {
            [TraitId.Thrifty] = TraitId.Spendthrift,
            [TraitId.Spendthrift] = TraitId.Thrifty,
            [TraitId.Discerning] = TraitId.Unfussy,
            [TraitId.Unfussy] = TraitId.Discerning,
            [TraitId.Sentimental] = TraitId.Practical,
            [TraitId.Practical] = TraitId.Sentimental,
            [TraitId.Patient] = TraitId.Stubborn,
            [TraitId.Stubborn] = TraitId.Patient,
            [TraitId.Prepared] = TraitId.Reckless,
            [TraitId.Reckless] = TraitId.Prepared,
        };

        for (var id = 1; id <= 500; id++)
        {
            var traits = TraitRegistry.TraitsFor(new HeroId(id), $"Sweep{id}");
            Assert.Equal(2, traits.Length);
            Assert.NotEqual(traits[0], traits[1]);
            Assert.DoesNotContain(opposites[traits[0]], traits);
        }
    }
}
