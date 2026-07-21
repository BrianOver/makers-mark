using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// PA5 (plan 2026-07-21-002): the plan's required determinism-suite extension — "golden-replay
/// covers a minigame craft and a counter sale" (spec success criterion 2). A ~20-day scripted run
/// with a graded <see cref="CraftAction"/> (explicit <c>PerformanceGrade</c> + <c>SubScores</c> —
/// the M3/PA2 minigame seam) AND a full haggled counter sale (PA3/PA4, driven start-to-finish by
/// <see cref="CounterPlayer"/>), run twice from the same seed, must serialize byte-identical
/// (KTD4/KTD5). Plus: <c>CounterState</c> round-trips through <see cref="SaveCodec"/> mid-round
/// and the haggle continues identically either side of the save/load.
///
/// <para>Scoped to exactly the handlers/systems this script exercises (the same convention
/// <c>Counter/HaggleEconomicsTests</c> and <c>Cli/CounterCliWiringTests</c> use) rather than the
/// full production composition: this suite's job is replay EXACTNESS, not economy balance (that
/// is <c>Balance/BalanceSimTests</c>' job) — the full composition's recruit-trickle/rival-restock
/// RNG draws would make whether the scripted sale even closes probabilistic, without adding
/// anything this test needs to prove.</para>
/// </summary>
public class SteppedMorningReplayTests
{
    private const ulong Seed = 8080;

    private static readonly Hero FixtureHero = new(
        new HeroId(1), "Torvald", ClassRegistry.VanguardId, Level: 1, MaxHp: 30, Gold: 200,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static GameKernel BuildScopedKernel() => new(
        ImmutableList.Create<IPhaseSystem>(new CounterQueueSystem(), new HeroShoppingSystem()),
        ImmutableList.Create<IActionHandler>(
            new CraftingHandlers(), new MaterialVendorHandlers(), new ShopHandlers(), new CounterHandlers()));

    private static ImmutableList<PlayerAction> ScriptedActionsFor(GameState state, ItemId? craftedItemId) =>
        (state.Day, state.Phase) switch
        {
            (1, DayPhase.Morning) => ImmutableList.Create<PlayerAction>(new BuyMaterialAction("copper", 4)),
            (1, DayPhase.Expedition) => ImmutableList.Create<PlayerAction>(
                new CraftAction("dagger", "copper", PerformanceGrade: 850, SubScores: ImmutableList.Create(820, 760, 900))),
            (1, DayPhase.Evening) when craftedItemId is { } id =>
                ImmutableList.Create<PlayerAction>(new StockAction(id, 20)),
            (>= 2, DayPhase.Morning) => CounterPlayer.ActionsFor(state),
            _ => ImmutableList<PlayerAction>.Empty,
        };

    private static GameState RunScript(int days = 20)
    {
        var kernel = BuildScopedKernel();
        var state = GameFactory.NewGame(Seed) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, FixtureHero),
            NextHeroId = 2,
        };
        ItemId? craftedItemId = null;

        while (state.Day <= days)
        {
            var actions = ScriptedActionsFor(state, craftedItemId);
            var result = kernel.Tick(state, actions);
            state = result.NewState;

            if (craftedItemId is null && result.Events.OfType<ItemCrafted>().FirstOrDefault() is { } crafted)
            {
                craftedItemId = crafted.Item;
            }
        }

        return state;
    }

    [Fact]
    public void SteppedMorning_GradedCraftAndHaggledSale_TwiceByteIdentical()
    {
        var first = RunScript();
        var second = RunScript();

        Assert.Equal(SaveCodec.Serialize(first), SaveCodec.Serialize(second));

        // Not vacuously identical — the two scenarios the plan names actually happened in the
        // script: the graded craft landed with its captured sub-scores, and a counter sale closed.
        var craftedItem = Assert.Single(first.Items.Values, i => i.CraftSubScores.Count > 0);
        Assert.Equal(ImmutableList.Create(820, 760, 900), craftedItem.CraftSubScores);
        Assert.Contains(first.EventLog.OfType<CounterSaleClosed>(), e => e.Hero == FixtureHero.Id);
    }

    [Fact]
    public void SaveLoad_MidHaggleRound_RoundTripsAndContinuesIdentically()
    {
        var kernel = BuildScopedKernel();
        var sword = new Item(
            new ItemId(1), "test-recipe", "Sword", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(6, 0, 3), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
        var state = GameFactory.NewGame(Seed) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, FixtureHero),
            NextHeroId = 2,
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(1, sword),
            Player = PlayerState.NewGame(0) with { Shelf = ImmutableList.Create(new ShelfEntry(sword.Id, 100)) },
        };

        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new OpenCounterAction())).NewState;
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PresentItemAction(sword.Id))).NewState;
        Assert.NotNull(state.Counter?.StandingOfferGold); // a round is genuinely open — "mid-haggle-round"

        var respond = ImmutableList.Create<PlayerAction>(new HaggleResponseAction(HaggleResponseKind.HoldFirm));

        var continuedDirect = kernel.Tick(state, respond).NewState;

        var roundTripped = SaveCodec.Deserialize(SaveCodec.Serialize(state));
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(roundTripped)); // the load itself is faithful
        var continuedAfterLoad = kernel.Tick(roundTripped, respond).NewState;

        Assert.Equal(SaveCodec.Serialize(continuedDirect), SaveCodec.Serialize(continuedAfterLoad));
        Assert.Equal(2, continuedAfterLoad.Counter!.Round); // HoldFirm actually advanced the round — proves the continuation is real
    }
}
