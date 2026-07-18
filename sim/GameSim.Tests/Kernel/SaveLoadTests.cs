using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Factions;
using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// Snapshot saves (KTD4): serialize → deserialize → continue must equal an uninterrupted run.
/// </summary>
public class SaveLoadTests
{
    private sealed class RngProbeSystem : IPhaseSystem
    {
        public DayPhase Phase => DayPhase.Evening;
        public string Name => "rng-probe";

        public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
        {
            return state with { Player = state.Player with { Gold = state.Player.Gold + rng.Roll100() } };
        }
    }

    [Fact]
    public void SaveAtDayN_LoadAndContinue_EqualsUninterruptedRun()
    {
        var kernel = new GameKernel(
            ImmutableList.Create<IPhaseSystem>(new RngProbeSystem()),
            ImmutableList<IActionHandler>.Empty);

        // Uninterrupted: 10 full days (5-phase day = 50 ticks).
        var uninterrupted = GameFactory.NewGame(seed: 1234);
        for (var i = 0; i < 50; i++)
        {
            uninterrupted = kernel.Tick(uninterrupted, ImmutableList<PlayerAction>.Empty).NewState;
        }

        // Interrupted: 5 days, save, load, 5 more days (25 + 25 ticks).
        var first = GameFactory.NewGame(seed: 1234);
        for (var i = 0; i < 25; i++)
        {
            first = kernel.Tick(first, ImmutableList<PlayerAction>.Empty).NewState;
        }

        var loaded = SaveCodec.Deserialize(SaveCodec.Serialize(first));
        for (var i = 0; i < 25; i++)
        {
            loaded = kernel.Tick(loaded, ImmutableList<PlayerAction>.Empty).NewState;
        }

        Assert.Equal(SaveCodec.Serialize(uninterrupted), SaveCodec.Serialize(loaded));
    }

    [Fact]
    public void PreP4Save_WithoutVenueId_LoadsAsMine()
    {
        // Backward-compat contract (P4): a pre-P4 ExpeditionResult serialized without the
        // trailing VenueId must deserialize to the Mine. Pinned in-repo rather than trusting
        // System.Text.Json default-when-absent semantics by reasoning alone.
        var result = new ExpeditionResult(
            ImmutableList<HeroId>.Empty, TargetFloor: 1, DeepestFloorCleared: 0,
            ImmutableList<FloorOutcome>.Empty, ImmutableList<HeroId>.Empty, ImmutableList<HeroId>.Empty,
            ImmutableList<AttributionBeat>.Empty, ImmutableList<OreLoot>.Empty,
            ImmutableSortedDictionary<int, int>.Empty);
        var state = GameFactory.NewGame(seed: 7) with { PendingExpeditions = ImmutableList.Create(result) };

        var json = SaveCodec.Serialize(state);
        // Strip the VenueId property to mimic a save written before P4 added it (case/position-agnostic).
        var preP4 = System.Text.RegularExpressions.Regex.Replace(
            json, ",?\\s*\"[Vv]enueId\"\\s*:\\s*\"mine\"", string.Empty);
        Assert.DoesNotContain("enueId", preP4);

        var loaded = SaveCodec.Deserialize(preP4);
        Assert.Equal("mine", loaded.PendingExpeditions[0].VenueId);
    }

    [Fact]
    public void PreP5Save_WithoutStanding_LoadsAsNeutral()
    {
        // Backward-compat contract (P5 U2/KTD2): PlayerState.Standing is a trailing-optional member
        // defaulting to null. A pre-core save has no Standing property; it must load as neutral
        // standing everywhere (StandingFor → 0), not crash. This asserts LOAD behavior — not
        // byte-identical re-save, since a fresh save now carries the member.
        var state = GameFactory.NewGame(seed: 11);
        var json = SaveCodec.Serialize(state);

        // Strip the Standing property to mimic a save written before the member existed
        // (fresh saves write "Standing":null; an explicitly-empty map would write "Standing":{}).
        var preP5 = System.Text.RegularExpressions.Regex.Replace(
            json, ",?\\s*\"Standing\"\\s*:\\s*(?:null|\\{\\})", string.Empty);
        Assert.DoesNotContain("Standing", preP5);

        var loaded = SaveCodec.Deserialize(preP5);
        Assert.Equal(0, loaded.Player.StandingFor(FactionRegistry.DeepveinId));
    }

    [Fact]
    public void SaveWithPopulatedStanding_RoundTripsValues()
    {
        // A save that HAS standing preserves the exact values across a round-trip (byte-identical).
        var state = GameFactory.NewGame(seed: 12);
        state = state with { Player = state.Player.WithStanding(FactionRegistry.DeepveinId, 35) };

        var loaded = SaveCodec.Deserialize(SaveCodec.Serialize(state));

        Assert.Equal(35, loaded.Player.StandingFor(FactionRegistry.DeepveinId));
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(loaded));
    }

    [Fact]
    public void RoundTrip_PreservesPolymorphicActionsAndEvents()
    {
        var state = GameFactory.NewGame(seed: 5);
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(3, 50))).NewState;

        var roundTripped = SaveCodec.Deserialize(SaveCodec.Serialize(state));

        var logged = Assert.Single(roundTripped.ActionLog);
        var bounty = Assert.IsType<PostBountyAction>(logged.Actions[0]);
        Assert.Equal(3, bounty.TargetFloor);
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(roundTripped));
    }

    [Fact]
    public void PreStagedSave_WithoutInFlight_LoadsEmpty()
    {
        // Backward-compat contract (staged resolution U1): GameState.InFlight is a non-positional
        // init member; a pre-staging save has no InFlight property and must load as empty.
        var state = GameFactory.NewGame(seed: 21);
        var json = SaveCodec.Serialize(state);

        var preStaged = System.Text.RegularExpressions.Regex.Replace(
            json, ",?\\s*\"InFlight\"\\s*:\\s*\\[\\]", string.Empty);
        Assert.DoesNotContain("InFlight", preStaged);

        var loaded = SaveCodec.Deserialize(preStaged);
        Assert.Empty(loaded.InFlight);

        // Populated case: values survive a byte-identical round-trip.
        var inFlight = new InFlightExpedition(
            ImmutableList.Create(new HeroId(1), new HeroId(2)),
            TargetFloor: 3, CheckpointFloor: 1, VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 14).Add(2, 9),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty
                .Add(1, ImmutableList.Create(new ItemId(41))).Add(2, ImmutableList<ItemId>.Empty),
            Gold: ImmutableSortedDictionary<int, int>.Empty.Add(1, 6).Add(2, 0),
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList.Create(new FloorOutcome(1, Cleared: true, ImmutableList<CombatEvent>.Empty)),
            Loot: ImmutableList.Create(new OreLoot(new HeroId(1), "iron", 2)),
            DeepestFloorCleared: 1)
        { SupplySent = true, Recalled = false };
        var populated = state with { InFlight = ImmutableList.Create(inFlight) };

        var reloaded = SaveCodec.Deserialize(SaveCodec.Serialize(populated));
        Assert.Equal(SaveCodec.Serialize(populated), SaveCodec.Serialize(reloaded));
        var round = Assert.Single(reloaded.InFlight);
        Assert.True(round.SupplySent);
        Assert.Equal(1, round.CheckpointFloor);
        Assert.Equal(14, round.Hp[1]);
    }

    [Fact]
    public void CampPhase_RoundTripsAsInt()
    {
        // DayPhase serializes as an int (no string-enum converter): Camp = 3 must survive a
        // byte-identical round-trip so a mid-day save parked at the Camp tick is safe.
        var state = GameFactory.NewGame(seed: 22) with { Phase = DayPhase.Camp };

        var json = SaveCodec.Serialize(state);
        var loaded = SaveCodec.Deserialize(json);

        Assert.Equal(DayPhase.Camp, loaded.Phase);
        Assert.Equal(json, SaveCodec.Serialize(loaded));
    }

    [Fact]
    public void PreP6Result_WithoutHalt_LoadsTargetReached()
    {
        // Backward-compat contract (P6 save-shape): ExpeditionHalt is a trailing member with a
        // TargetReached default — a pre-staging ExpeditionResult without the property must
        // deserialize to TargetReached (the old implicit meaning), VenueId-precedent style.
        var result = new ExpeditionResult(
            ImmutableList<HeroId>.Empty, TargetFloor: 1, DeepestFloorCleared: 1,
            ImmutableList<FloorOutcome>.Empty, ImmutableList<HeroId>.Empty, ImmutableList<HeroId>.Empty,
            ImmutableList<AttributionBeat>.Empty, ImmutableList<OreLoot>.Empty,
            ImmutableSortedDictionary<int, int>.Empty);
        var state = GameFactory.NewGame(seed: 23) with { PendingExpeditions = ImmutableList.Create(result) };

        var json = SaveCodec.Serialize(state);
        var preP6 = System.Text.RegularExpressions.Regex.Replace(
            json, ",?\\s*\"Halt\"\\s*:\\s*0", string.Empty);
        Assert.DoesNotContain("\"Halt\"", preP6);

        var loaded = SaveCodec.Deserialize(preP6);
        Assert.Equal(ExpeditionHalt.TargetReached, loaded.PendingExpeditions[0].Halt);
    }

    [Fact]
    public void RoundTrip_PreservesCampActions()
    {
        // Handler-less kernel: the actions are rejected but still logged (GameKernel logs every
        // batch) — enough to pin their polymorphic discriminators before any handler exists.
        var state = GameFactory.NewGame(seed: 24);
        var kernel = new GameKernel(ImmutableList<IPhaseSystem>.Empty, ImmutableList<IActionHandler>.Empty);
        state = kernel.Tick(state, ImmutableList.Create<PlayerAction>(
            new SendSupplyAction(new HeroId(1), new ItemId(7)),
            new RecallPartyAction(new HeroId(2)))).NewState;

        var roundTripped = SaveCodec.Deserialize(SaveCodec.Serialize(state));

        var logged = Assert.Single(roundTripped.ActionLog);
        var supply = Assert.IsType<SendSupplyAction>(logged.Actions[0]);
        Assert.Equal(new ItemId(7), supply.Item);
        var recall = Assert.IsType<RecallPartyAction>(logged.Actions[1]);
        Assert.Equal(new HeroId(2), recall.Member);
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(roundTripped));
    }

    [Fact]
    public void RoundTrip_PreservesCampEvents()
    {
        // Pins the three staged-resolution event discriminators polymorphically.
        var state = GameFactory.NewGame(seed: 25);
        state = state with
        {
            EventLog = state.EventLog.AddRange(new GameEvent[]
            {
                new PartyCampReport(
                    ImmutableList.Create(new HeroId(1)),
                    CampedBelowFloor: 2, TargetFloor: 4,
                    HpByHero: ImmutableSortedDictionary<int, int>.Empty.Add(1, 11),
                    HealsLeftByHero: ImmutableSortedDictionary<int, int>.Empty.Add(1, 1))
                { Id = new EventId(9001), Day = 3 },
                new SupplyDelivered(new HeroId(1), new ItemId(7), Fee: 9) { Id = new EventId(9002), Day = 3 },
                new PartyRecalled(ImmutableList.Create(new HeroId(1))) { Id = new EventId(9003), Day = 3 },
            }),
        };

        var roundTripped = SaveCodec.Deserialize(SaveCodec.Serialize(state));

        var events = roundTripped.EventLog;
        var report = Assert.IsType<PartyCampReport>(events[^3]);
        Assert.Equal(2, report.CampedBelowFloor);
        Assert.Equal(1, report.HealsLeftByHero[1]);
        var delivered = Assert.IsType<SupplyDelivered>(events[^2]);
        Assert.Equal(9, delivered.Fee);
        Assert.IsType<PartyRecalled>(events[^1]);
        Assert.Equal(SaveCodec.Serialize(state), SaveCodec.Serialize(roundTripped));
    }
}
