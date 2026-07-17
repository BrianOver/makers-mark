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

        // Uninterrupted: 10 full days.
        var uninterrupted = GameFactory.NewGame(seed: 1234);
        for (var i = 0; i < 30; i++)
        {
            uninterrupted = kernel.Tick(uninterrupted, ImmutableList<PlayerAction>.Empty).NewState;
        }

        // Interrupted: 5 days, save, load, 5 more days.
        var first = GameFactory.NewGame(seed: 1234);
        for (var i = 0; i < 15; i++)
        {
            first = kernel.Tick(first, ImmutableList<PlayerAction>.Empty).NewState;
        }

        var loaded = SaveCodec.Deserialize(SaveCodec.Serialize(first));
        for (var i = 0; i < 15; i++)
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
}
