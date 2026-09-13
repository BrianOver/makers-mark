using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Economy;

/// <summary>
/// P2-LONG-18 ("the pledge"): <see cref="PledgeDuesHandlers"/> processes
/// <see cref="PledgeDuesAction"/>. Covers the ownership/provenance/appraisal/cycle guard chain (each
/// with its own typed rejection), the happy path (item removed from the world, zero gold moved,
/// <see cref="DuesPledged"/> emitted with the right numbers), the free-action ruling (no slot spent),
/// and — the property this whole unit exists to guarantee — that a pledged piece is TRULY gone: it can
/// never again be stocked or sent to a hero once it has left the world this way.
/// </summary>
public class PledgeDuesHandlersTests
{
    private static readonly GameKernel Kernel = new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new PledgeDuesHandlers()));

    /// <summary>A day-1 world with one player-marked Shortsword (item 10, Attack 15 -> appraises at
    /// 30g under <see cref="Advisor.SuggestedPrice"/>) and the Guild Assessment's dues set to
    /// <paramref name="duesGold"/> (20g by default, comfortably under the sword's 30g appraisal).</summary>
    private static GameState PledgeableWorld(ulong seed = 42, int duesGold = 20)
    {
        var state = GameFactory.NewGame(seed);
        var sword = new Item(
            new ItemId(10), "shortsword", "Shortsword", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(15, 0, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        return state with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(10, sword),
            Assessment = state.Assessment with { DuesGold = duesGold },
        };
    }

    // ---- Happy path ---------------------------------------------------------------------

    [Fact]
    public void Pledge_ValidPledge_RemovesItemPermanently_EmitsDuesPledged_NoGoldMoves()
    {
        var state = PledgeableWorld();
        var beforeGold = state.Player.Gold;

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));

        Assert.Empty(result.Rejected);
        Assert.Equal(beforeGold, result.NewState.Player.Gold); // the piece paid, not the till
        Assert.False(result.NewState.Items.ContainsKey(10)); // gone from the world

        var pledged = Assert.Single(result.Events.OfType<DuesPledged>());
        Assert.Equal(new ItemId(10), pledged.Item);
        Assert.Equal("Shortsword", pledged.ItemName);
        Assert.Equal(30, pledged.AppraisedGold);
        Assert.Equal(20, pledged.DuesCoveredGold);
    }

    [Fact]
    public void Pledge_ConsumesNoActionSlot()
    {
        var state = PledgeableWorld();
        var before = state.ActionSlotsRemaining;

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));

        Assert.Empty(result.Rejected);
        Assert.Equal(before, result.NewState.ActionSlotsRemaining);
    }

    [Fact]
    public void Pledge_ShelvedItem_RemovesTheShelfEntryToo()
    {
        var world = PledgeableWorld();
        var state = world with { Player = world.Player with { Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(10), 30)) } };

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));

        Assert.Empty(result.Rejected);
        Assert.Empty(result.NewState.Player.Shelf);
    }

    // ---- Rejections: existence / provenance ----------------------------------------------

    [Fact]
    public void Pledge_UnknownItem_TypedRejection()
    {
        var state = PledgeableWorld();

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(999))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("No such item", rejection.Reason);
    }

    [Fact]
    public void Pledge_NotPlayerCrafted_TypedRejection_ItemUntouched()
    {
        var world = PledgeableWorld();
        var rivalBlade = new Item(
            new ItemId(11), "shortsword", "Rival Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(15, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
        var state = world with { Items = world.Items.Add(11, rivalBlade) };

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(11))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("carries no MakersMark", rejection.Reason);
        Assert.True(result.NewState.Items.ContainsKey(11)); // link 1: bought/rival stock never hangs on the wall
    }

    [Fact]
    public void Pledge_WornByAHero_TypedRejection()
    {
        var world = PledgeableWorld();
        var hero = new Hero(
            new HeroId(1), "Sera", "vanguard", Level: 1, MaxHp: 20, Gold: 0,
            new GearSet(new ItemId(10), null, null), ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var state = world with { Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero) };

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("is equipped by Sera", rejection.Reason);
    }

    [Fact]
    public void Pledge_AlreadySold_TypedRejection()
    {
        var world = PledgeableWorld();
        var state = world with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new ItemSold(new ItemId(10), new HeroId(1), 30, true) { Id = new EventId(1), Day = 1 }),
        };

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("already sold", rejection.Reason);
    }

    [Fact]
    public void Pledge_AlreadyInAHerosPack_TypedRejection()
    {
        var world = PledgeableWorld();
        var hero = new Hero(
            new HeroId(1), "Sera", "vanguard", Level: 1, MaxHp: 20, Gold: 0,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null)
        {
            Pack = ImmutableList.Create(new ItemId(10)),
        };
        var state = world with { Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero) };

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("already in a hero's pack", rejection.Reason);
    }

    // ---- Rejections: appraisal / cycle -----------------------------------------------------

    [Fact]
    public void Pledge_AppraisalBelowDues_TypedRejection_ItemUntouched()
    {
        var state = PledgeableWorld(duesGold: 1000); // far above the 30g sword

        var result = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));

        var rejection = Assert.Single(result.Rejected);
        Assert.Contains("appraises at 30g", rejection.Reason);
        Assert.Contains("dues are 1000g", rejection.Reason);
        Assert.True(result.NewState.Items.ContainsKey(10));
    }

    [Fact]
    public void Pledge_SecondPledgeSameCycle_TypedRejection_SecondPieceUntouched()
    {
        var world = PledgeableWorld();
        var secondBlade = new Item(
            new ItemId(11), "shortsword", "Second Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(15, 0, 3), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var state = world with { Items = world.Items.Add(11, secondBlade) };

        var first = Kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(10))));
        Assert.Empty(first.Rejected);

        var second = Kernel.Tick(first.NewState, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(new ItemId(11))));

        var rejection = Assert.Single(second.Rejected);
        Assert.Contains("one pledge per assessment", rejection.Reason);
        Assert.True(second.NewState.Items.ContainsKey(11)); // the SECOND piece was never touched
    }

    // ---- The trade itself: a pledged piece can never reach a hero by another channel ------

    /// <summary>
    /// The property this whole unit exists to guarantee (see <see cref="PledgeDuesAction"/>'s own doc
    /// comment): once a piece is pledged, EVERY other honest channel to a hero (the shelf, a vigil
    /// delivery) must refuse it exactly as it would refuse an item that never existed — because after
    /// this handler runs, as far as every other handler is concerned, it never did. Drives the REAL
    /// composed kernel (<see cref="GameComposition.BuildKernel"/>), not the isolated fixture above, so
    /// this proves the guard chain every OTHER handler already has (not a new one this unit added) is
    /// what does the enforcing.
    /// </summary>
    [Fact]
    public void Pledge_ThenAttemptToStockOrSendTheSamePiece_BothRejected_ItIsTrulyGone()
    {
        var kernel = GameComposition.BuildKernel();
        var fresh = GameComposition.NewCampaign(seed: 7);
        var itemId = new ItemId(fresh.NextItemId);
        var sword = new Item(
            itemId, "shortsword", "Fixture Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(15, 0, 3), new MakersMark("You", fresh.Day), ImmutableList<ItemHistoryEntry>.Empty);

        var state = fresh with
        {
            Items = fresh.Items.Add(itemId.Value, sword),
            NextItemId = fresh.NextItemId + 1,
            Assessment = fresh.Assessment with { DuesGold = 20 },
        };

        var pledgeResult = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PledgeDuesAction(itemId)));
        Assert.Empty(pledgeResult.Rejected);
        Assert.False(pledgeResult.NewState.Items.ContainsKey(itemId.Value));

        // Channel 1: the shelf.
        var stockResult = kernel.Tick(pledgeResult.NewState, ImmutableList.Create<PlayerAction>(new StockAction(itemId, 30)));
        var stockRejection = Assert.Single(stockResult.Rejected);
        Assert.Contains("No such item", stockRejection.Reason);

        // Channel 2: the vigil runner (SendSupplyAction), Camp phase.
        var hero = pledgeResult.NewState.Heroes.Values.First(h => h.Alive);
        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(hero.Id),
            TargetFloor: 2,
            CheckpointFloor: 1,
            VenueId: VenueRegistry.Mine.Id,
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(hero.Id.Value, hero.MaxHp),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty,
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: 1);

        var campState = pledgeResult.NewState with
        {
            Phase = DayPhase.Camp,
            InFlight = ImmutableList.Create(inFlight),
            Player = pledgeResult.NewState.Player with { Gold = 1000 },
        };

        var sendResult = kernel.Tick(campState, ImmutableList.Create<PlayerAction>(new SendSupplyAction(hero.Id, itemId)));
        var sendRejection = Assert.Single(sendResult.Rejected);
        Assert.Contains("No such item", sendRejection.Reason);
    }
}
