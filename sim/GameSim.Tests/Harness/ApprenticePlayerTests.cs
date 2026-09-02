using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-ONBOARD-03 (docs/design/MAKERS-MARK.md §11.15): <see cref="ApprenticePlayer"/> — purity
/// (same state, same actions, every call), grep-level proof that every action type the course
/// needs is actually submitted under the right conditions (not merely assumed — CLAUDE.md names
/// exactly this kind of unverified claim as a prior planning defect), the one-held-consumable
/// reserve for the camp runner, and the day-2 "close fair" (Accept, never Counter) behavior that
/// distinguishes this policy from <see cref="CounterPlayer"/>.
/// </summary>
public class ApprenticePlayerTests
{
    private static Hero MakeHero(int id, string classId, int gold, bool alive = true) => new(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeGearItem(int id, int attack, int defense, bool playerCrafted = true) => new(
        new ItemId(id), "dagger", "Dagger", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, defense, Weight: 2), playerCrafted ? new MakersMark("Apprentice", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty);

    private static Item MakeConsumableItem(int id, int magnitude, bool playerCrafted = true) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, Weight: 0), playerCrafted ? new MakersMark("Apprentice", 1) : null,
        ImmutableList<ItemHistoryEntry>.Empty, Effect: new ConsumableEffect(ConsumableKind.Heal, magnitude));

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    private static GameState BaseState(ImmutableSortedDictionary<int, Hero>? heroes = null, params Item[] items) =>
        GameFactory.NewGame(seed: 7007) with
        {
            Heroes = heroes ?? ImmutableSortedDictionary<int, Hero>.Empty,
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        };

    private static GameState WithPlayer(
        GameState state, int gold = 100, int copper = 0, params ShelfEntry[] shelf) =>
        state with
        {
            Player = PlayerState.NewGame(gold) with
            {
                Materials = ImmutableSortedDictionary<string, int>.Empty.SetItem("copper", copper),
                Shelf = shelf.ToImmutableList(),
            },
        };

    // ---------------------------------------------------------------------------------------
    // Purity (no IO/RNG/clock)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ActionsFor_SameStateTwice_ProducesTheSameActions_EveryCall()
    {
        var gear = MakeGearItem(1, attack: 8, defense: 0);
        var state = WithPlayer(BaseState(Roster(MakeHero(1, "striker", 100)), gear), copper: 10);

        var first = ApprenticePlayer.ActionsFor(state);
        var second = ApprenticePlayer.ActionsFor(state);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void ActionsFor_ExpeditionDeep_ReturnsEmpty_NoPlayerVerbExistsThere()
    {
        var state = BaseState() with { Phase = DayPhase.ExpeditionDeep };

        Assert.Empty(ApprenticePlayer.ActionsFor(state));
    }

    // ---------------------------------------------------------------------------------------
    // Morning: bootstrap buy, commission accept, shelving + the one held reserve
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ActionsFor_FreshCampaignDay1Morning_BuysExactlyOneCraftsWorthOfCopper()
    {
        // GameComposition.NewCampaign's real fresh-campaign fixture (BaselinePlayerPinTests'
        // own precedent): 100 gold, zero materials, zero talents, zero shelf. Day 1 is not the
        // counter day, so this is the ordinary routine with nothing yet to accept or shelve.
        var state = GameComposition.NewCampaign(seed: 2026);

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new BuyMaterialAction("copper", 2)), actions);
    }

    [Fact]
    public void ActionsFor_Morning_CopperAlreadyBanked_NeverRebuys()
    {
        var state = WithPlayer(BaseState(), copper: 2);

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.DoesNotContain(actions, a => a is BuyMaterialAction);
    }

    [Fact]
    public void ActionsFor_Morning_AcceptsOpenGearCommission_ButExcludesConsumableSlot()
    {
        var hero1 = MakeHero(1, "striker", 100);
        var hero2 = MakeHero(2, "striker", 100);
        var state = BaseState(Roster(hero1, hero2)) with
        {
            Commissions = ImmutableList.Create(
                new Commission(hero1.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 20),
                new Commission(hero2.Id, ItemSlot.Consumable, QualityGrade.Common, DeadlineDay: 10, PremiumGold: 5)),
        };
        state = WithPlayer(state, copper: 2);

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Contains(actions, a => a is AcceptCommissionAction accept && accept.Hero == hero1.Id);
        Assert.DoesNotContain(actions, a => a is AcceptCommissionAction accept && accept.Hero == hero2.Id);
    }

    [Fact]
    public void ActionsFor_Morning_ShelvesUnsoldGear_AtDoubleStatValue_ButReservesTheHeldConsumable()
    {
        var gear = MakeGearItem(1, attack: 6, defense: 4); // value 10 -> fair price 20
        var salve = MakeConsumableItem(2, magnitude: 6);
        var state = WithPlayer(BaseState(items: [gear, salve]), copper: 10);

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Contains(actions, a => a is StockAction s && s.Item == gear.Id && s.Price == 20);
        Assert.DoesNotContain(actions, a => a is StockAction s && s.Item == salve.Id);
    }

    [Fact]
    public void ActionsFor_Morning_NeverReshelvesAnAlreadySoldConsumable()
    {
        var salve = MakeConsumableItem(1, magnitude: 6);
        var state = WithPlayer(BaseState(items: [salve]), copper: 10) with
        {
            EventLog = ImmutableList.Create<GameEvent>(new ItemSold(salve.Id, new HeroId(9), 12, FromPlayerShop: true)),
        };

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.DoesNotContain(actions, a => a is StockAction s && s.Item == salve.Id);
    }

    [Fact]
    public void ActionsFor_Camp_NeverSendsAnAlreadySoldConsumable_EvenIfNoLongerPacked()
    {
        // A consumable that has ever sold is gone for good once drunk (ShopHandlers 3b) — it can
        // drop out of every hero's pack afterward, so the reserve check must key off the sale
        // event, not merely "not currently held anywhere," or a long-consumed id could be handed
        // to the camp runner as if it were still real stock.
        var hero = MakeHero(1, "striker", 100);
        var salve = MakeConsumableItem(1, magnitude: 6);
        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(hero.Id),
            TargetFloor: 2,
            CheckpointFloor: 1,
            VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 20),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty.Add(1, ImmutableList<ItemId>.Empty),
            Gold: ImmutableSortedDictionary<int, int>.Empty.Add(1, 0),
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);
        var state = WithPlayer(BaseState(Roster(hero), salve), gold: 100) with
        {
            Phase = DayPhase.Camp,
            InFlight = ImmutableList.Create(inFlight),
            EventLog = ImmutableList.Create<GameEvent>(new ItemSold(salve.Id, new HeroId(9), 12, FromPlayerShop: true)),
        };

        Assert.Empty(ApprenticePlayer.ActionsFor(state));
    }

    // ---------------------------------------------------------------------------------------
    // Expedition: craft gear until the shelf holds one, then bank/top up the reserve
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ActionsFor_Expedition_CraftsGear_WhenShelfHasNoUnsoldGear()
    {
        var state = WithPlayer(BaseState(), copper: 2) with { Phase = DayPhase.Expedition };

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper")), actions);
    }

    [Fact]
    public void ActionsFor_Expedition_CraftsTheConsumable_OnceGearIsAlreadyShelvedUnsold()
    {
        var gear = MakeGearItem(1, attack: 8, defense: 0);
        var state = WithPlayer(BaseState(items: [gear]), gold: 100, copper: 2, new ShelfEntry(gear.Id, 16)) with
        {
            Phase = DayPhase.Expedition,
        };

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new CraftAction("field-salve", "copper")), actions);
    }

    [Fact]
    public void ActionsFor_Expedition_ReturnsEmpty_WhenTheDaysActionSlotsAreSpent()
    {
        var state = WithPlayer(BaseState(), copper: 10) with
        {
            Phase = DayPhase.Expedition,
            ActionSlotsRemaining = 0,
        };

        Assert.Empty(ApprenticePlayer.ActionsFor(state));
    }

    // ---------------------------------------------------------------------------------------
    // Camp: the vigil runner — one reserved consumable to the first camped party
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ActionsFor_Camp_SendsTheReservedConsumable_ToTheCampedParty_WhenOneFires()
    {
        var hero = MakeHero(1, "striker", 100);
        var salve = MakeConsumableItem(1, magnitude: 6);
        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(hero.Id),
            TargetFloor: 2,
            CheckpointFloor: 1, // every InFlight party is deep-bound by construction (checkpoint < target)
            VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 20),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty.Add(1, ImmutableList<ItemId>.Empty),
            Gold: ImmutableSortedDictionary<int, int>.Empty.Add(1, 0),
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);
        var state = WithPlayer(BaseState(Roster(hero), salve), gold: 100) with
        {
            Phase = DayPhase.Camp,
            InFlight = ImmutableList.Create(inFlight),
        };

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new SendSupplyAction(hero.Id, salve.Id)), actions);
    }

    [Fact]
    public void ActionsFor_Camp_ReturnsEmpty_WhenNoConsumableIsHeldInReserve()
    {
        var hero = MakeHero(1, "striker", 100);
        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(hero.Id),
            TargetFloor: 2,
            CheckpointFloor: 1,
            VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 20),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty.Add(1, ImmutableList<ItemId>.Empty),
            Gold: ImmutableSortedDictionary<int, int>.Empty.Add(1, 0),
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);
        var state = WithPlayer(BaseState(Roster(hero)), gold: 100) with
        {
            Phase = DayPhase.Camp,
            InFlight = ImmutableList.Create(inFlight),
        };

        Assert.Empty(ApprenticePlayer.ActionsFor(state));
    }

    // ---------------------------------------------------------------------------------------
    // Evening: buy every affordable ore offer, in order, within the day's action budget
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ActionsFor_Evening_BuysAffordableOreOffers_InOrder_SkippingWhatItCannotAfford()
    {
        var hero1 = MakeHero(1, "striker", 10);
        var hero2 = MakeHero(2, "striker", 10);
        var state = WithPlayer(BaseState(Roster(hero1, hero2)), gold: 30) with
        {
            Phase = DayPhase.Evening,
            OpenOreOffers = ImmutableList.Create(
                new OreOffered(hero1.Id, "iron", Quantity: 5, UnitPrice: 5),   // costs 25 — affordable first
                new OreOffered(hero2.Id, "steel", Quantity: 10, UnitPrice: 10)), // costs 100 — never affordable after
        };

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new BuyOreAction(hero1.Id, "iron", 5)), actions);
    }

    // ---------------------------------------------------------------------------------------
    // Day 2: open the counter, close FAIR (Accept — never Counter, unlike CounterPlayer)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ActionsFor_Day2Morning_CounterNull_OpensTheCounter_AlongsideTheOrdinaryRoutine()
    {
        var hero = MakeHero(1, "striker", 100);
        var gear = MakeGearItem(1, attack: 8, defense: 0);
        var state = WithPlayer(BaseState(Roster(hero), gear), copper: 0) with { Day = 2 };

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Contains(actions, a => a is OpenCounterAction);
        Assert.Contains(actions, a => a is BuyMaterialAction); // ordinary routine still runs this tick
        Assert.Contains(actions, a => a is StockAction s && s.Item == gear.Id);
    }

    [Fact]
    public void ActionsFor_Day2Morning_StandingOffer_RespondsAccept_NeverCounter()
    {
        var hero = MakeHero(1, "striker", 500);
        var gear = MakeGearItem(1, attack: 8, defense: 0);
        var state = BaseState(Roster(hero), gear) with
        {
            Day = 2,
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(gear.Id, 20)) },
            Counter = CounterState.Empty with
            {
                Queue = ImmutableList.Create(hero.Id),
                Active = hero.Id,
                Round = 1,
                Presented = gear.Id,
                StandingOfferGold = 15,
            },
        };

        var actions = ApprenticePlayer.ActionsFor(state);

        var haggle = Assert.IsType<HaggleResponseAction>(Assert.Single(actions));
        Assert.Equal(HaggleResponseKind.Accept, haggle.Kind);
    }

    [Fact]
    public void ActionsFor_Day2Morning_PresentsTheBestShelfItem_WhenNoStandingOfferYet()
    {
        var hero = MakeHero(1, "striker", 500);
        var gear = MakeGearItem(1, attack: 8, defense: 0);
        var state = BaseState(Roster(hero), gear) with
        {
            Day = 2,
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(gear.Id, 20)) },
            Counter = CounterState.Empty with { Queue = ImmutableList.Create(hero.Id), Active = hero.Id },
        };

        var actions = ApprenticePlayer.ActionsFor(state);

        var present = Assert.IsType<PresentItemAction>(Assert.Single(actions));
        Assert.Equal(gear.Id, present.Item);
    }

    [Fact]
    public void ActionsFor_Day2Morning_NoActiveCustomer_Closes()
    {
        var state = BaseState() with { Day = 2, Counter = CounterState.Empty };

        var actions = ApprenticePlayer.ActionsFor(state);

        Assert.Equal(ImmutableList.Create<PlayerAction>(new CloseCounterAction()), actions);
    }

    [Fact]
    public void ActionsFor_Day2Morning_ClosingTick_ReturnsEmpty()
    {
        var state = BaseState() with { Day = 2, Counter = CounterState.Empty with { Closed = true } };

        Assert.Empty(ApprenticePlayer.ActionsFor(state));
    }

    [Fact]
    public void ActionsFor_Day2_ThroughTheKernel_ClosesAFairSale_ByAcceptingTheHerosOwnOffer()
    {
        // The full script this policy produces, one tick at a time, through the SAME kernel
        // CounterPlayerTests drives — proves this is an actually-playable stepped morning, and
        // that "close fair" really does close a sale rather than merely emit the right verb once.
        var hero = MakeHero(1, "striker", 1000);
        var gear = MakeGearItem(1, attack: 6, defense: 0);
        var state = BaseState(Roster(hero), gear) with
        {
            Day = 2,
            Player = PlayerState.NewGame(0) with
            {
                Materials = ImmutableSortedDictionary<string, int>.Empty.SetItem("copper", 2),
                Shelf = ImmutableList.Create(new ShelfEntry(gear.Id, 30)),
            },
        };
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new GameSim.Counter.CounterQueueSystem()),
            ImmutableList.Create<IActionHandler>(new GameSim.Counter.CounterHandlers()));

        GameSim.Contracts.CounterSaleClosed? sale = null;
        for (var i = 0; i < 10 && state.Counter is not { Closed: true } && sale is null; i++)
        {
            var actions = ApprenticePlayer.ActionsFor(state);
            var result = kernel.Tick(state, actions);
            Assert.Empty(result.Rejected);
            sale ??= result.Events.OfType<GameSim.Contracts.CounterSaleClosed>().FirstOrDefault();
            state = result.NewState;
        }

        Assert.NotNull(sale);
        Assert.Equal(hero.Id, sale!.Hero);
        Assert.Equal(gear.Id, sale.Item);
    }
}
