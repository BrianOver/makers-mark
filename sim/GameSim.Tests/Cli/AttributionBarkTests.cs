using System.Collections.Immutable;
using GameSim.Cli;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Cli;

/// <summary>
/// Phase B (B5, Gate B attribution barks): the 10 shop-teeth traits (<see cref="TraitDivergenceTests"/>)
/// change WHAT a hero decides, but before this unit nothing on the transcript said WHY — a reader
/// could not tell a Thrifty hero from a Spendthrift one without opening the trait card (fable
/// Gate-B finding). These pin that each of the 5 trait axes now has at least one CLI line whose TEXT
/// itself differs by trait, on events the sim already emits — no new event field, no Contracts
/// change, presentation-only (<see cref="EventNarration"/>/<see cref="DemandNarration"/>).
/// Fixtures mirror <see cref="TraitDivergenceTests"/>' FindHero-by-scan pattern: traits are derived
/// (<see cref="TraitRegistry.TraitsFor"/>), never stored, so a fixture is found by scanning HeroIds
/// for the wanted trait rather than constructed by hand.
/// </summary>
public class AttributionBarkTests
{
    private const string FixtureNamePrefix = "Bark";

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

    private static Hero MakeHero(TraitId trait, int gold = 1000, int deepestFloor = 0) =>
        new(
            FindHero(trait).Id, FindHero(trait).Name, ClassRegistry.StrikerId, Level: 1, MaxHp: 25, Gold: gold,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: deepestFloor, DiedOnDay: null);

    private static GameState BaseState(params Hero[] heroes) =>
        GameComposition.NewCampaign(seed: 1) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        };

    private static readonly DemandSnapshot EmptySnapshot = new(
        ImmutableList<PassReasonRollup>.Empty,
        ImmutableList<OpenCommissionEntry>.Empty,
        ImmutableList<DepthStallEntry>.Empty,
        ImmutableList<BountyFloorMinimum>.Empty,
        ImmutableList<OpenBountyEntry>.Empty);

    // ---- Axis 1: Price Sensitivity (Thrifty / Spendthrift) — CounterSaleClosed buyer-name flavor -

    [Fact]
    public void CounterSaleClosed_FlavorsSpendthriftAndThrifty_DifferentlyOnTheIdenticalLine()
    {
        var spendthrift = MakeHero(TraitId.Spendthrift);
        var thrifty = MakeHero(TraitId.Thrifty);
        var state = BaseState(spendthrift, thrifty);
        var item = new ItemId(1);

        var spendthriftLine = EventNarration.Line(new CounterSaleClosed(spendthrift.Id, item, 105, Pinned: true), state);
        var thriftyLine = EventNarration.Line(new CounterSaleClosed(thrifty.Id, item, 89, Pinned: false), state);

        Assert.NotNull(spendthriftLine);
        Assert.Contains($"Spendthrift {spendthrift.Name}", spendthriftLine, StringComparison.Ordinal);

        Assert.NotNull(thriftyLine);
        Assert.Contains($"Thrifty {thrifty.Name}", thriftyLine, StringComparison.Ordinal);

        Assert.NotEqual(spendthriftLine, thriftyLine);
    }

    // ---- Axis 2: Quality Demand (Discerning / Unfussy) — HeroPassedOnItem pass-reason flavor -----

    [Fact]
    public void HeroPassedOnItem_FlavorsDiscerning_OnTheVeteranQualityGateReason()
    {
        var discerning = MakeHero(TraitId.Discerning, deepestFloor: ShoppingAi.VeteranFloorThreshold);
        var state = BaseState(discerning);

        var line = EventNarration.Line(
            new HeroPassedOnItem(discerning.Id, new ItemId(1), "a floor-3 veteran won't trust common work — bring fine or better"),
            state);

        Assert.NotNull(line);
        Assert.Contains($"Discerning {discerning.Name}", line, StringComparison.Ordinal);
    }

    // ---- Axis 3: Sentiment (Sentimental / Practical) — HeroPassedOnItem pass-reason flavor -------

    [Fact]
    public void HeroPassedOnItem_FlavorsSentimental_OnTheStoriedGearReason()
    {
        var sentimental = MakeHero(TraitId.Sentimental);
        var state = BaseState(sentimental);

        var line = EventNarration.Line(
            new HeroPassedOnItem(sentimental.Id, new ItemId(1), "won't part with Worn Blade — it's carried them through 3 fights"),
            state);

        Assert.NotNull(line);
        Assert.Contains($"Sentimental {sentimental.Name}", line, StringComparison.Ordinal);
    }

    // ---- Axis 4: Haggle Patience (Patient / Stubborn) — CustomerCountered / CustomerWalked flavor -

    [Fact]
    public void CustomerCountered_FlavorsPatient_OnlyOnceHagglingReachesTheRoundCap()
    {
        var patient = MakeHero(TraitId.Patient);
        var stubborn = MakeHero(TraitId.Stubborn);
        var state = BaseState(patient, stubborn) with
        {
            Counter = CounterState.Empty with { Round = WillingnessModel.MaxRounds, Active = patient.Id },
        };

        var patientLine = EventNarration.Line(new CustomerCountered(patient.Id, 50), state);
        Assert.NotNull(patientLine);
        Assert.Contains($"Patient {patient.Name}", patientLine, StringComparison.Ordinal);

        // Same round cap, but this hero holds Stubborn (mutually exclusive with Patient on the same
        // axis) — the flavor must not bleed onto a hero whose trait didn't cause it.
        var stubbornLine = EventNarration.Line(new CustomerCountered(stubborn.Id, 50), state);
        Assert.NotNull(stubbornLine);
        Assert.DoesNotContain("Patient", stubbornLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerWalked_FlavorsStubborn_OnThePatienceExhaustedReason()
    {
        var stubborn = MakeHero(TraitId.Stubborn);
        var state = BaseState(stubborn);

        var line = EventNarration.Line(new CustomerWalked(stubborn.Id, new ItemId(1), "the customer's patience ran out"), state);

        Assert.NotNull(line);
        Assert.Contains($"Stubborn {stubborn.Name}", line, StringComparison.Ordinal);
    }

    /// <summary>End-to-end (mirrors <c>TraitDivergenceTests.HagglePatience_Patient_SurvivesTwoHoldFirms_ThatStubborn_WalksOn</c>):
    /// runs the REAL counter kernel through 2 HoldFirms and asserts the CLI line itself — not just
    /// the raw event — differs: Patient's still-open round gets the "Patient" bark, Stubborn's walk
    /// gets the "Stubborn" bark, on the identical script.</summary>
    [Fact]
    public void TwoHoldFirms_ProduceDifferentCliLines_ForPatientVersusStubborn()
    {
        var sword = new Item(new ItemId(1), "test-recipe", "Iron Sword", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(6, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

        GameState Setup(Hero hero) => GameFactory.NewGame(seed: 900) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(hero.Id.Value, hero),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(sword.Id.Value, sword),
            Player = PlayerState.NewGame(50) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 100)) },
        };

        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
            ImmutableList.Create<IActionHandler>(new CounterHandlers()));

        (GameState State, ImmutableList<GameEvent> Events) RunTwoHoldFirms(Hero hero)
        {
            var state = kernel.Tick(Setup(hero), ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
            state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm))).NewState;
            var second = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm)));
            return (second.NewState, second.Events);
        }

        var patient = MakeHero(TraitId.Patient, gold: 1000);
        var (patientState, patientEvents) = RunTwoHoldFirms(patient);
        var patientCountered = Assert.Single(patientEvents.OfType<CustomerCountered>());
        var patientLine = EventNarration.Line(patientCountered, patientState);
        Assert.NotNull(patientLine);
        Assert.Contains($"Patient {patient.Name}", patientLine, StringComparison.Ordinal);

        var stubborn = MakeHero(TraitId.Stubborn, gold: 1000);
        var (stubbornState, stubbornEvents) = RunTwoHoldFirms(stubborn);
        var stubbornWalked = Assert.Single(stubbornEvents.OfType<CustomerWalked>());
        var stubbornLine = EventNarration.Line(stubbornWalked, stubbornState);
        Assert.NotNull(stubbornLine);
        Assert.Contains($"Stubborn {stubborn.Name}", stubbornLine, StringComparison.Ordinal);
    }

    // ---- Axis 5: Consumable Stocking (Prepared / Reckless) — Morning muster-line flavor -----------

    [Fact]
    public void MusterLine_FlavorsReckless_WithANearEmptyPack_AndPrepared_WithADeepStock()
    {
        var reckless = MakeHero(TraitId.Reckless); // default Pack is empty — RecklessStockTarget (0) never restocks
        var prepared = MakeHero(TraitId.Prepared) with { Pack = ImmutableList.Create(new ItemId(101), new ItemId(102)) };
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(reckless.Id.Value, reckless)
            .Add(prepared.Id.Value, prepared);
        var parties = ImmutableList.Create(new PartyPlan(ImmutableList.Create(reckless.Id, prepared.Id), TargetFloor: 1, VenueId: "mine"));

        var line = DemandNarration.MusterLine(parties, EmptySnapshot, heroes);

        Assert.Contains($"{reckless.Name} marches down with a near-empty pack", line, StringComparison.Ordinal);
        Assert.Contains($"{prepared.Name} stocked deep on salves", line, StringComparison.Ordinal);
    }

    [Fact]
    public void MusterLine_StaysSilentOnConsumableStocking_WhenNoMarchingHeroTripsTheTell()
    {
        // A Prepared hero who hasn't finished restocking yet (below PreparedStockTarget) and a
        // Reckless hero who somehow still carries a Heal (e.g. a commission reward) are BOTH
        // mid-state, not the tell-tale extreme — no clause should fire for either.
        var prepared = MakeHero(TraitId.Prepared) with { Pack = ImmutableList.Create(new ItemId(101)) }; // 1 < PreparedStockTarget (2)
        var reckless = MakeHero(TraitId.Reckless) with { Pack = ImmutableList.Create(new ItemId(102)) }; // not empty
        var heroes = ImmutableSortedDictionary<int, Hero>.Empty
            .Add(prepared.Id.Value, prepared)
            .Add(reckless.Id.Value, reckless);
        var parties = ImmutableList.Create(new PartyPlan(ImmutableList.Create(prepared.Id, reckless.Id), TargetFloor: 1, VenueId: "mine"));

        var line = DemandNarration.MusterLine(parties, EmptySnapshot, heroes);

        Assert.DoesNotContain("stocked deep", line, StringComparison.Ordinal);
        Assert.DoesNotContain("near-empty pack", line, StringComparison.Ordinal);
    }
}
