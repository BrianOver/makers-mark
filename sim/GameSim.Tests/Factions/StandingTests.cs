using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Factions;
using GameSim.Kernel;

namespace GameSim.Tests.Factions;

/// <summary>
/// P5 U2 standing state + drivers (R4/R5/KTD5/KTD6/KTD8): buying a faction's ore RAISES its
/// standing (clamped to +cap), and each Morning <see cref="FactionDriftSystem"/> steps every
/// non-neutral standing back toward neutral. Both are pure integer and draw NO RNG. This core is
/// discount-only, so standing only ever rises (on purchase) then drifts down — never below 0.
/// </summary>
public class StandingTests
{
    private static readonly FactionDefinition Deepvein = FactionRegistry.Deepvein;

    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, int gold = 40) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 30, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>Evening state with one hero (H1, 40g), player 1000g, and the given open offers.</summary>
    private static GameState EveningState(params OreOffered[] offers) =>
        GameFactory.NewGame(seed: 42) with
        {
            Phase = DayPhase.Evening,
            Player = PlayerState.NewGame(1000),
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, MakeHero(1)),
            OpenOreOffers = offers.ToImmutableList(),
        };

    private static OreOffered Offer(int heroId, string material, int quantity, int unitPrice) =>
        new(new HeroId(heroId), material, quantity, unitPrice);

    private static GameState Buy(GameState state, string material, int quantity)
    {
        var (next, rejected) = new OreMarketHandlers()
            .Apply(state, new BuyOreAction(new HeroId(1), material, quantity), new Pcg32(state.Rng), new TestSink());
        Assert.Null(rejected);
        return next;
    }

    // ---- Standing rises on a successful ore purchase (R5/KTD6) ------------------------

    [Fact]
    public void BuyingDeepveinOre_RaisesStanding_ByExactlyRiseStep()
    {
        var state = EveningState(Offer(1, "iron", quantity: 20, unitPrice: 4));

        var after = Buy(state, "iron", 1);

        Assert.Equal(Deepvein.RiseStep, after.Player.StandingFor(FactionRegistry.DeepveinId));
    }

    [Fact]
    public void SecondBuy_RaisesStandingAgain()
    {
        var state = EveningState(Offer(1, "iron", quantity: 20, unitPrice: 4));

        var after = Buy(Buy(state, "iron", 1), "iron", 1);

        Assert.Equal(2 * Deepvein.RiseStep, after.Player.StandingFor(FactionRegistry.DeepveinId));
    }

    [Fact]
    public void Rise_ClampsAt_PlusStandingCap()
    {
        // Seed standing one step short of the cap, then buy: the rise clamps to exactly +cap.
        var state = EveningState(Offer(1, "iron", quantity: 20, unitPrice: 4)) with { };
        state = state with
        {
            Player = state.Player.WithStanding(FactionRegistry.DeepveinId, Deepvein.StandingCap - Deepvein.RiseStep + 1),
        };

        var after = Buy(state, "iron", 1);

        Assert.Equal(Deepvein.StandingCap, after.Player.StandingFor(FactionRegistry.DeepveinId));
    }

    [Fact]
    public void BuyingOreNoFactionSupplies_LeavesStandingUnchanged()
    {
        // "obsidian" is supplied by no registered faction — ByOreKey returns null, no rise.
        Assert.Null(FactionRegistry.ByOreKey("obsidian"));
        var state = EveningState(Offer(1, "obsidian", quantity: 20, unitPrice: 4));

        var after = Buy(state, "obsidian", 1);

        Assert.Equal(0, after.Player.StandingFor(FactionRegistry.DeepveinId));
        Assert.True(after.Player.Standing is null || after.Player.Standing.Count == 0);
    }

    // ---- Morning drift toward neutral (R5/KTD5) --------------------------------------

    private static (GameState State, RngState RngAfter) Drift(GameState state)
    {
        var rng = new Pcg32(state.Rng);
        var after = new FactionDriftSystem().Process(state, rng, new TestSink());
        return (after, rng.Snapshot());
    }

    private static GameState WithStanding(int value) =>
        GameFactory.NewGame(seed: 7) with
        {
            Player = PlayerState.NewGame(100).WithStanding(FactionRegistry.DeepveinId, value),
        };

    [Fact]
    public void SystemContract_MorningPhase_StableName()
    {
        var system = new FactionDriftSystem();
        Assert.Equal(DayPhase.Morning, system.Phase);
        Assert.Equal("faction-drift", system.Name);
    }

    [Fact]
    public void PositiveStanding_StepsDownByDriftStep_TowardZero()
    {
        var (after, _) = Drift(WithStanding(Deepvein.StandingCap));

        Assert.Equal(Deepvein.StandingCap - Deepvein.DriftStep, after.Player.StandingFor(FactionRegistry.DeepveinId));
    }

    [Fact]
    public void NeutralStanding_IsUnchanged()
    {
        // Explicit-zero entry: still neutral, drift leaves it alone.
        var (after, _) = Drift(WithStanding(0));
        Assert.Equal(0, after.Player.StandingFor(FactionRegistry.DeepveinId));

        // Null map (fresh game): drift is a no-op, map stays null.
        var fresh = GameFactory.NewGame(seed: 7);
        var (afterFresh, _) = Drift(fresh);
        Assert.Null(afterFresh.Player.Standing);
    }

    [Fact]
    public void Drift_NeverOvershootsZero_ValueBelowStepSnapsToZero()
    {
        // A magnitude smaller than one drift step collapses straight to 0 (never negative).
        Assert.True(Deepvein.DriftStep > 1, "test assumes a drift step > 1 so a value of 1 is below it");
        var (after, _) = Drift(WithStanding(1));

        Assert.Equal(0, after.Player.StandingFor(FactionRegistry.DeepveinId));
    }

    [Fact]
    public void RepeatedDrift_DecaysToNeutral_AndStopsAtZero()
    {
        var state = WithStanding(Deepvein.StandingCap);
        for (var i = 0; i < 200; i++) // far past the decay horizon
        {
            (state, _) = Drift(state);
        }

        Assert.Equal(0, state.Player.StandingFor(FactionRegistry.DeepveinId));
    }

    [Fact]
    public void Drift_DrawsNoRng_KernelStreamUntouched()
    {
        var state = WithStanding(Deepvein.StandingCap);

        var (_, rngAfter) = Drift(state);

        Assert.Equal(state.Rng, rngAfter); // stream unchanged — drift rolls no dice (KTD5)
    }
}
