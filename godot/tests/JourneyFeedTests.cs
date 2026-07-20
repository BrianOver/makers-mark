#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using GodotClient;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U16 (KTD11): <see cref="JourneyPlayhead"/> (per-party time-stretch) and <see cref="JourneyFeed"/>
/// (the per-tick cache every spectate surface composes) — pure C#, no Godot runtime. Covers the
/// accumulated-delta reveal pacing, the stream-exhaustion idle loop, the skip-collapses-to-summary
/// rule, and the "clouds on reload" behavior (a fresh cache mid-expedition reveals nothing until
/// the first <c>Advance</c>).
/// </summary>
[TestSuite]
public class JourneyFeedTests
{
    [TestCase]
    public void Playhead_RevealsBeatsGradually_OverThePhaseDuration()
    {
        var head = new JourneyPlayhead();
        head.Bind(partyKey: 1, beatCount: 4, phaseDurationSeconds: 4.0); // 1 beat/sec

        head.Advance(0.9, paused: false);
        AssertThat(head.Revealed).IsEqual(1);

        head.Advance(1.2, paused: false); // elapsed 2.1s -> beat 3 (1-based ceil)
        AssertThat(head.Revealed).IsEqual(3);

        head.Advance(10.0, paused: false); // way past the phase — clamps at the beat count
        AssertThat(head.Revealed).IsEqual(4);
        AssertThat(head.Idle).IsTrue();
    }

    [TestCase]
    public void Playhead_Paused_NeverAdvances()
    {
        var head = new JourneyPlayhead();
        head.Bind(1, 4, 4.0);

        head.Advance(10.0, paused: true);

        AssertThat(head.Revealed).IsEqual(0);
        AssertThat(head.Idle).IsFalse();
    }

    [TestCase]
    public void Playhead_Collapse_JumpsStraightToTheEnd()
    {
        var head = new JourneyPlayhead();
        head.Bind(1, 5, 100.0); // long phase, barely started

        head.Advance(0.01, paused: false);
        AssertThat(head.Revealed).IsLess(5);

        head.Collapse();

        AssertThat(head.Revealed).IsEqual(5);
        AssertThat(head.Idle).IsTrue();
    }

    [TestCase]
    public void Playhead_NewPartyKey_ResetsRevealToZero()
    {
        var head = new JourneyPlayhead();
        head.Bind(1, 4, 4.0);
        head.Advance(4.0, paused: false);
        AssertThat(head.Revealed).IsEqual(4);

        head.Bind(2, 3, 3.0); // a different party (or the same party rebuilt fresh after reload)

        AssertThat(head.Revealed).IsEqual(0);
    }

    [TestCase]
    public void Playhead_GrowingBeatCount_NeverResetsAnInProgressReveal()
    {
        // Staged party: stage-1 beats at Camp, stage-2 appended once resolved — the reveal must
        // continue from where it was, not restart.
        var head = new JourneyPlayhead();
        head.Bind(1, 2, 2.0);
        head.Advance(2.0, paused: false);
        AssertThat(head.Revealed).IsEqual(2);

        head.Bind(1, 5, 3.0); // same party key, more beats now, new phase duration
        AssertThat(head.Revealed).IsEqual(2); // unchanged — no reset

        head.Advance(3.0, paused: false);
        AssertThat(head.Revealed).IsEqual(5);
    }

    [TestCase]
    public void Feed_Refresh_ClouedOnReload_RevealedEmptyUntilFirstAdvance()
    {
        // A fresh JourneyFeed (a reloaded save rebuilds the whole scene tree, so every adapter
        // cache starts empty) mid-expedition: Refresh alone reveals nothing yet — the documented
        // "clouds on reload" behavior (KTD11) — and does not throw.
        var feed = new JourneyFeed();
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))));
        var inFlight = new InFlightExpedition(
            Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 2, CheckpointFloor: 1, VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 40),
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty, Dead: ImmutableSortedSet<int>.Empty,
            Floors: floors, Loot: ImmutableList<OreLoot>.Empty, DeepestFloorCleared: 1);
        var state = GameFactory.NewGame(77) with { Phase = DayPhase.Camp, InFlight = ImmutableList.Create(inFlight) };

        feed.Refresh(state, ImmutableList<GameEvent>.Empty);

        AssertThat(feed.Cards.Count).IsEqual(1);
        AssertThat(feed.Revealed(feed.Cards[0]).IsEmpty).IsTrue();
    }

    [TestCase]
    public void Feed_SkipMidStream_NextRefreshCollapsesThePreviousCardFirst_NoCrash()
    {
        var feed = new JourneyFeed();
        var floors = ImmutableList.Create(
            new FloorOutcome(1, true, ImmutableList.Create(
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null),
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null),
                new CombatEvent(1, new HeroId(1), "cave-rat", ImmutableList.Create(3), 5, 0, true, null))));
        var result = new ExpeditionResult(
            Party: ImmutableList.Create(new HeroId(1)), TargetFloor: 1, DeepestFloorCleared: 1, Floors: floors,
            Survivors: ImmutableList.Create(new HeroId(1)), Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList<AttributionBeat>.Empty, Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);

        var stateA = GameFactory.NewGame(78) with { Phase = DayPhase.Camp, PendingExpeditions = ImmutableList.Create(result) };
        feed.Refresh(stateA, ImmutableList<GameEvent>.Empty);
        feed.Advance(0.001, paused: false); // barely started — nowhere near fully revealed

        AssertThat(feed.Revealed(feed.Cards[0]).Count).IsLess(feed.Cards[0].Beats.Count);

        // The player skips — a new tick arrives before the reveal finished. The very next Refresh
        // must not throw and must leave the feed in a consistent (collapsed-then-rebound) state.
        var stateB = stateA with { Phase = DayPhase.ExpeditionDeep };
        feed.Refresh(stateB, ImmutableList<GameEvent>.Empty);

        AssertThat(feed.Cards.Count).IsEqual(1);
        AssertThat(feed.Revealed(feed.Cards[0]).Count).IsEqual(feed.Cards[0].Beats.Count); // caught up, not dangling
    }

    [TestCase]
    public void Feed_StreamExhaustion_IdleLine_NeverEmpty_CyclesOverTime()
    {
        var feed = new JourneyFeed();
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 1, VenueId: "mine");
        var state = GameFactory.NewGame(79) with
        {
            Phase = DayPhase.Camp,
            InFlight = ImmutableList.Create(new InFlightExpedition(
                Party: plan.Roster, TargetFloor: 2, CheckpointFloor: 1, VenueId: "mine",
                Hp: ImmutableSortedDictionary<int, int>.Empty, Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
                Gold: ImmutableSortedDictionary<int, int>.Empty, Dead: ImmutableSortedSet<int>.Empty,
                Floors: ImmutableList<FloorOutcome>.Empty, Loot: ImmutableList<OreLoot>.Empty, DeepestFloorCleared: 0)),
        };

        feed.Refresh(state, ImmutableList<GameEvent>.Empty);
        var card = feed.Cards[0];

        AssertThat(feed.IsIdle(card)).IsTrue(); // zero beats — nothing to reveal, ever
        var line1 = feed.IdleLine(card.PartyKey);
        AssertThat(string.IsNullOrEmpty(line1)).IsFalse();

        feed.Advance(10.0, paused: false); // well past one idle cycle
        var line2 = feed.IdleLine(card.PartyKey);
        AssertThat(string.IsNullOrEmpty(line2)).IsFalse();
    }
}
#endif
