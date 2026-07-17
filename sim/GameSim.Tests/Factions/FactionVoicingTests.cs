using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Economy;
using GameSim.Factions;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Factions;

/// <summary>
/// P5 U4 voicing of standing shifts (R9/KTD7): a standing change that crosses a named threshold emits
/// a stamped <see cref="FactionStandingShifted"/> the flavor engine renders into gossip. The ore-buy
/// rise crosses the favored ENTER boundary (warms); the Morning <see cref="FactionDriftSystem"/>
/// crosses the EXIT boundary (cools); the ENTER/EXIT deadband is the HYSTERESIS that stops a same-day
/// drift-then-buy oscillation from emitting a contradictory pair. Faction lines compete with hero
/// lines for the per-day cap by log order. Zero RNG anywhere in the path; the faction name rides in on
/// the event, never a registry lookup inside <c>Generate</c>.
/// </summary>
public class FactionVoicingTests
{
    private static readonly FactionDefinition Deepvein = FactionRegistry.Deepvein;
    private static int Enter => FactionStandingThresholds.FavoredEnter(Deepvein);
    private static int Exit => FactionStandingThresholds.FavoredExit(Deepvein);

    private const ulong Campaign = 0xC0FFEEUL;

    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, int gold = 40) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 30, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    /// <summary>Evening state: one hero, deep player purse, the given seeded standing and offers.</summary>
    private static GameState EveningState(int standing, params OreOffered[] offers)
    {
        var player = PlayerState.NewGame(100000);
        if (standing != 0)
        {
            player = player.WithStanding(FactionRegistry.DeepveinId, standing);
        }

        return GameFactory.NewGame(seed: 42) with
        {
            Phase = DayPhase.Evening,
            Player = player,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, MakeHero(1)),
            OpenOreOffers = offers.ToImmutableList(),
        };
    }

    private static (GameState State, List<GameEvent> Events) Buy(GameState state, string material, int quantity)
    {
        var sink = new TestSink();
        var (next, rejected) = new OreMarketHandlers()
            .Apply(state, new BuyOreAction(new HeroId(1), material, quantity), new Pcg32(state.Rng), sink);
        Assert.Null(rejected);
        return (next, sink.Events);
    }

    private static (GameState State, List<GameEvent> Events, RngState RngAfter) Drift(GameState state)
    {
        var sink = new TestSink();
        var rng = new Pcg32(state.Rng);
        var after = new FactionDriftSystem().Process(state, rng, sink);
        return (after, sink.Events, rng.Snapshot());
    }

    private static GameState DriftState(int standing) =>
        GameFactory.NewGame(seed: 7) with
        {
            Phase = DayPhase.Morning,
            Player = PlayerState.NewGame(100).WithStanding(FactionRegistry.DeepveinId, standing),
        };

    // ---- Threshold cross emits exactly one stamped-able event citing the real faction ----

    [Fact]
    public void BuyCrossingFavoredEnter_EmitsExactlyOneFavoredShift_CitingRealFactionAndName()
    {
        // Seed one short of ENTER (in the deadband); one buy carries standing across it.
        var state = EveningState(standing: Enter - 1, Offer(1, "iron", quantity: 50, unitPrice: 1));

        var (_, events) = Buy(state, "iron", 1);

        var shift = Assert.Single(events.OfType<FactionStandingShifted>());
        Assert.Equal(FactionRegistry.DeepveinId, shift.FactionId);
        Assert.Equal(Deepvein.DisplayName, shift.FactionName); // display name rides in on the event (KTD7)
        Assert.Equal(StandingShiftDirection.Favored, shift.Direction);
    }

    [Fact]
    public void BuyNotCrossingAThreshold_EmitsNoStandingShift()
    {
        // Fresh neutral standing → one buy lands well inside the deadband's floor; no crossing.
        var state = EveningState(standing: 0, Offer(1, "iron", quantity: 50, unitPrice: 1));

        var (after, events) = Buy(state, "iron", 1);

        Assert.Empty(events.OfType<FactionStandingShifted>());
        Assert.True(after.Player.StandingFor(FactionRegistry.DeepveinId) < Enter);
    }

    [Fact]
    public void DriftCrossingFavoredExit_EmitsExactlyOneCooledShift()
    {
        // Seed at EXIT so one Morning drift falls through it.
        var state = DriftState(Exit);

        var (_, events, _) = Drift(state);

        var shift = Assert.Single(events.OfType<FactionStandingShifted>());
        Assert.Equal(FactionRegistry.DeepveinId, shift.FactionId);
        Assert.Equal(Deepvein.DisplayName, shift.FactionName);
        Assert.Equal(StandingShiftDirection.Cooled, shift.Direction);
    }

    [Fact]
    public void DriftNotCrossingAThreshold_EmitsNoStandingShift()
    {
        // Standing at the cap: one drift step lowers it, but nowhere near the EXIT boundary.
        var (_, events, _) = Drift(DriftState(Deepvein.StandingCap));

        Assert.Empty(events.OfType<FactionStandingShifted>());
    }

    [Fact]
    public void Voicing_DrawsNoRng_DriftStreamUntouched()
    {
        var state = DriftState(Exit);

        var (_, _, rngAfter) = Drift(state);

        Assert.Equal(state.Rng, rngAfter); // emission is pure integer — no dice (KTD7)
    }

    // ---- Hysteresis: same-day drift-down + buy-up cannot emit a contradictory pair ----

    [Fact]
    public void Hysteresis_HoveringAtBoundary_DriftThenBuy_EmitsAtMostOnePerDirection_NoContradictoryPair()
    {
        // Standing parked AT the ENTER boundary. A naive single-threshold design would fire "cooled"
        // on the Morning drift (50→48 crosses 50 downward) AND "favored" on the Evening rebuy
        // (48→53 crosses 50 upward) — a contradictory same-batch pair. The ENTER/EXIT deadband makes
        // the drift step land inside the band (no cooled), so only the rebuy speaks.
        var morning = DriftState(Enter);
        var (afterDrift, driftEvents, _) = Drift(morning);

        var evening = afterDrift with
        {
            Phase = DayPhase.Evening,
            Player = afterDrift.Player, // carry the drifted standing into the Evening
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, MakeHero(1)),
            OpenOreOffers = ImmutableList.Create(Offer(1, "iron", quantity: 50, unitPrice: 1)),
        };
        var (_, buyEvents) = Buy(evening, "iron", 1);

        var shifts = driftEvents.Concat(buyEvents).OfType<FactionStandingShifted>().ToList();

        // At most one per direction, and NEVER both directions in the same day-cycle.
        Assert.True(shifts.Count(s => s.Direction == StandingShiftDirection.Favored) <= 1);
        Assert.True(shifts.Count(s => s.Direction == StandingShiftDirection.Cooled) <= 1);
        Assert.False(
            shifts.Any(s => s.Direction == StandingShiftDirection.Favored)
            && shifts.Any(s => s.Direction == StandingShiftDirection.Cooled),
            "hysteresis must prevent a contradictory favored+cooled pair in one day-cycle");

        // And concretely: the Morning drift stayed silent (inside the deadband), the rebuy warmed.
        Assert.Empty(driftEvents.OfType<FactionStandingShifted>());
        Assert.Equal(StandingShiftDirection.Favored, Assert.Single(shifts).Direction);
    }

    // ---- Faction lines compete with hero lines for the per-day cap by log order ----

    [Fact]
    public void FactionAndHeroLines_CompeteForMaxLinesPerDay_ByLogOrder()
    {
        var state = DramaWorld();
        GameEvent[] sources =
        [
            new FactionStandingShifted("deepvein", Deepvein.DisplayName, StandingShiftDirection.Favored) { Id = new EventId(1), Day = 1 },
            new HeroDied(new HeroId(1), 2, "slain by a Tunnel Spider", GearSet.Empty) { Id = new EventId(2), Day = 1 },
            new RecruitArrived(new HeroId(2)) { Id = new EventId(3), Day = 1 },
            new FloorRecordSet(new HeroId(3), 4) { Id = new EventId(4), Day = 1 }, // 4th — drops off the cap
        ];

        var lines = GossipGenerator.Generate(sources, state.Heroes, state.Items, Campaign, GossipGenerator.MaxLinesPerDay);

        Assert.Equal(GossipGenerator.MaxLinesPerDay, lines.Count);
        Assert.Equal(new[] { 1, 2, 3 }, lines.Select(l => l.Source.Value)); // first three in log order
        Assert.Contains(Deepvein.DisplayName, lines[0].Line, StringComparison.Ordinal); // the faction line took its slot
    }

    // ---- Save round-trip stays byte-identical with a faction-rendered gossip line ----

    [Fact]
    public void SaveRoundTrip_WithFactionGossipInTheLog_StaysByteIdentical()
    {
        var state = DramaWorld() with
        {
            Day = 2,
            Phase = DayPhase.Morning,
            NextEventId = 2,
            EventLog = ImmutableList.Create<GameEvent>(
                new FactionStandingShifted("deepvein", Deepvein.DisplayName, StandingShiftDirection.Favored)
                {
                    Id = new EventId(1),
                    Day = 1,
                }),
        };

        var tick = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new GossipSystem()),
            ImmutableList<IActionHandler>.Empty).Tick(state, ImmutableList<PlayerAction>.Empty);

        var gossip = Assert.Single(tick.NewState.EventLog.OfType<GossipEmitted>());
        Assert.Contains(Deepvein.DisplayName, gossip.Line, StringComparison.Ordinal);

        var json = SaveCodec.Serialize(tick.NewState);
        Assert.Equal(json, SaveCodec.Serialize(SaveCodec.Deserialize(json)));
    }

    private static OreOffered Offer(int heroId, string material, int quantity, int unitPrice) =>
        new(new HeroId(heroId), material, quantity, unitPrice);

    /// <summary>A seeded world with the starting roster — heroes present for hero-line rendering.</summary>
    private static GameState DramaWorld() =>
        HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 2026));
}
