using System.Collections.Immutable;
using System.Text.RegularExpressions;
using GameSim.Contracts;
using GameSim.Kernel;

// Namespace segment deliberately NOT "Contracts": a GameSim.Tests.Contracts namespace would
// shadow the `Contracts.` shorthand other test files use for GameSim.Contracts (CS0234).
namespace GameSim.Tests.CounterContracts;

/// <summary>
/// PA1 contracts micro-PR pins (plan 2026-07-21-002): the counter actions, the dual-mode craft
/// seam (<see cref="CraftAction.Puzzle"/>/<see cref="CraftAction.SubScores"/>), <see cref="CounterState"/>,
/// <see cref="Hero.MoodPermille"/>, and <see cref="Item.CraftSubScores"/> all serialize polymorphically
/// and stay SAVE-COMPAT: a pre-Phase-A save (no property) loads to the documented default, and the
/// additions are behavior-neutral until a handler lands (PA3). No handler exists yet, so every new
/// action is safely rejected by the kernel's "No handler accepts" path with zero RNG drawn.
/// </summary>
public class CounterContractTests
{
    private static GameKernel HandlerlessKernel() =>
        new(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);

    [Fact]
    public void NewCounterActions_RoundTripPolymorphically()
    {
        // Handler-less kernel logs every batch even when rejected — enough to pin the five new
        // polymorphic discriminators through a real SaveCodec round-trip.
        var state = GameFactory.NewGame(seed: 501);
        state = HandlerlessKernel().Tick(state, ImmutableList.Create<PlayerAction>(
            new OpenCounterAction(),
            new PresentItemAction(new ItemId(7)),
            new SuggestItemAction(new ItemId(8)),
            new HaggleResponseAction(HaggleResponseKind.Counter, 42),
            new CloseCounterAction())).NewState;

        var roundTripped = SaveCodec.Deserialize(SaveCodec.Serialize(state));

        var actions = Assert.Single(roundTripped.ActionLog).Actions;
        Assert.IsType<OpenCounterAction>(actions[0]);
        Assert.Equal(new ItemId(7), Assert.IsType<PresentItemAction>(actions[1]).Item);
        Assert.Equal(new ItemId(8), Assert.IsType<SuggestItemAction>(actions[2]).Item);
        var haggle = Assert.IsType<HaggleResponseAction>(actions[3]);
        Assert.Equal(HaggleResponseKind.Counter, haggle.Kind);
        Assert.Equal(42, haggle.Price);
        Assert.IsType<CloseCounterAction>(actions[4]);
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(roundTripped));
    }

    [Fact]
    public void CounterActionWithNoHandler_RejectedTyped_ZeroRng_StateUnchanged()
    {
        // Kernel safety (PA1): a counter action today has no handler → typed RejectedAction,
        // no state mutation, and NOT ONE RNG draw (the roll stream is byte-identical after the tick).
        var state = GameFactory.NewGame(seed: 502);
        var before = state;

        var result = HandlerlessKernel().Tick(
            state, ImmutableList.Create<PlayerAction>(new PresentItemAction(new ItemId(1))));

        var rejected = Assert.Single(result.Rejected);
        Assert.IsType<PresentItemAction>(rejected.Action);
        Assert.Contains("No handler accepts", rejected.Reason);
        Assert.Equal(before.Rng, result.NewState.Rng);        // zero RNG advanced
        Assert.Null(result.NewState.Counter);                  // no session opened
        Assert.Empty(result.NewState.Items);                   // nothing minted
        Assert.Empty(result.Events);                           // nothing emitted
    }

    [Fact]
    public void PrePhaseASave_WithoutCounter_LoadsNull()
    {
        // Save-compat (InFlight/Venues precedent): GameState.Counter is a non-positional init member.
        // A fresh save writes "Counter":null; a pre-Phase-A save has no property — both load to null.
        var state = GameFactory.NewGame(seed: 503);
        var json = SaveCodec.Serialize(state);

        var pre = Regex.Replace(json, ",?\\s*\"Counter\"\\s*:\\s*null", string.Empty);
        Assert.DoesNotContain("\"Counter\"", pre);
        Assert.Null(SaveCodec.Deserialize(pre).Counter);

        // Populated case survives byte-identical.
        var populated = state with { Counter = CounterState.Empty with { PatienceRounds = 3, Round = 1 } };
        var reloaded = SaveCodec.Deserialize(SaveCodec.Serialize(populated));
        Assert.Equal(3, reloaded.Counter!.PatienceRounds);
        Assert.Equal(1, reloaded.Counter!.Round);
        Assert.Equal(SaveCodec.Serialize(populated), SaveCodec.Serialize(reloaded));
    }

    [Fact]
    public void PrePhaseASave_WithoutHeroMood_LoadsZero()
    {
        // Save-compat (Hero.Pack precedent): Hero.MoodPermille is a non-positional init member.
        // NewGame seeds zero heroes (they arrive via recruit trickle/muster), so inject one.
        var hero = new Hero(
            new HeroId(1), "Torvald", "vanguard", Level: 1, MaxHp: 20, Gold: 50,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true,
            DeepestFloorReached: 0, DiedOnDay: null);
        var state = GameFactory.NewGame(seed: 504) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
        };
        var json = SaveCodec.Serialize(state);

        var pre = Regex.Replace(json, ",?\\s*\"MoodPermille\"\\s*:\\s*0", string.Empty);
        Assert.DoesNotContain("MoodPermille", pre);

        var loaded = SaveCodec.Deserialize(pre);
        Assert.All(loaded.Heroes.Values, h => Assert.Equal(0, h.MoodPermille));

        // Populated case survives byte-identical.
        var moody = state with
        {
            Heroes = state.Heroes.SetItem(1, hero with { MoodPermille = -120 }),
        };
        var reloaded = SaveCodec.Deserialize(SaveCodec.Serialize(moody));
        Assert.Equal(-120, reloaded.Heroes[1].MoodPermille);
        Assert.Equal(SaveCodec.Serialize(moody), SaveCodec.Serialize(reloaded));
    }

    [Fact]
    public void PrePhaseASave_WithoutItemSubScores_LoadsEmpty()
    {
        // Save-compat (non-positional init member): Item.CraftSubScores defaults to empty.
        var item = new Item(
            new ItemId(90), "dagger", "Test Dagger", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(3, 0, 2), Mark: null, History: ImmutableList<ItemHistoryEntry>.Empty);
        var state = GameFactory.NewGame(seed: 505) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(90, item),
        };
        var json = SaveCodec.Serialize(state);

        var pre = Regex.Replace(json, ",?\\s*\"CraftSubScores\"\\s*:\\s*\\[\\]", string.Empty);
        Assert.DoesNotContain("CraftSubScores", pre);
        Assert.Empty(SaveCodec.Deserialize(pre).Items[90].CraftSubScores);

        // Populated case survives byte-identical.
        var scored = state with
        {
            Items = state.Items.SetItem(90, item with { CraftSubScores = ImmutableList.Create(812, 640, 905) }),
        };
        var reloaded = SaveCodec.Deserialize(SaveCodec.Serialize(scored));
        Assert.Equal(new[] { 812, 640, 905 }, reloaded.Items[90].CraftSubScores);
        Assert.Equal(SaveCodec.Serialize(scored), SaveCodec.Serialize(reloaded));
    }

    [Fact]
    public void CraftAction_WithoutPuzzleAndSubScores_LoadsNull_AndNullEverythingRoundTrips()
    {
        // Dual-mode seam save-compat (PKD1): CraftAction gained TWO trailing optionals. A pre-seam
        // logged craft has neither property and must deserialize to null (VenueId/M3 precedent);
        // an all-null craft round-trips byte-identical.
        var state = GameFactory.NewGame(seed: 506);
        state = HandlerlessKernel().Tick(
            state, ImmutableList.Create<PlayerAction>(new CraftAction("dagger", "copper"))).NewState;

        var json = SaveCodec.Serialize(state);
        var pre = Regex.Replace(json, ",?\\s*\"Puzzle\"\\s*:\\s*null", string.Empty);
        pre = Regex.Replace(pre, ",?\\s*\"SubScores\"\\s*:\\s*null", string.Empty);
        Assert.DoesNotContain("Puzzle", pre);
        Assert.DoesNotContain("SubScores", pre);

        var craft = Assert.IsType<CraftAction>(Assert.Single(SaveCodec.Deserialize(pre).ActionLog).Actions[0]);
        Assert.Null(craft.Puzzle);
        Assert.Null(craft.SubScores);

        // All-null craft is byte-identical across a round-trip.
        var reloaded = SaveCodec.Deserialize(json);
        Assert.Equal(json, SaveCodec.Serialize(reloaded));
    }

    [Fact]
    public void CraftAction_WithSubScores_RoundTripsVerbatim()
    {
        // The three beat sub-scores ride the ActionLog verbatim (PKD1 data-not-rules).
        var state = GameFactory.NewGame(seed: 507);
        state = HandlerlessKernel().Tick(state, ImmutableList.Create<PlayerAction>(
            new CraftAction("dagger", "copper", 750, Puzzle: null, SubScores: ImmutableList.Create(812, 640, 905)))).NewState;

        var reloaded = SaveCodec.Deserialize(SaveCodec.Serialize(state));
        var craft = Assert.IsType<CraftAction>(Assert.Single(reloaded.ActionLog).Actions[0]);
        Assert.Equal(750, craft.PerformanceGrade);
        Assert.Null(craft.Puzzle);
        Assert.Equal(new[] { 812, 640, 905 }, craft.SubScores);
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(reloaded));
    }
}
