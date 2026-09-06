using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-END-01 option 4 (MAKERS-MARK.md §11.8.1): <see cref="GateHeldStreakQuery"/> is a pure count
/// over the <see cref="DecisionExplained"/> trail <c>ExpeditionRevealSystem</c> already persists
/// every Evening (§11.14.8) — never a fresh gate/power recomputation. These pin the streak count
/// itself, the "a gap breaks it" rule, the milestone gate that keeps the fact from nagging every
/// night, and that reading it writes nothing to the world.
/// </summary>
public class GateHeldStreakQueryTests
{
    private static DecisionExplained HaltEvent(string venueId, ExpeditionHalt halt, int day) =>
        new(GateHeldStreakQuery.ExpeditionHaltWhat(venueId), halt.ToString(), "test fixture")
        {
            Id = new EventId(day),
            Day = day,
        };

    private static GameState WithLog(params GameEvent[] events) =>
        NewWorld() with { EventLog = events.ToImmutableList() };

    [Fact]
    public void NoEventsAtAll_StreakIsZero()
    {
        var state = NewWorld();

        Assert.Equal(0, GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 5));
    }

    [Fact]
    public void SingleGateHeldNight_StreakIsOne()
    {
        var state = WithLog(HaltEvent("mine", ExpeditionHalt.GateHeld, 3));

        Assert.Equal(1, GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 3));
    }

    [Fact]
    public void ConsecutiveGateHeldNights_CountBackFromDayInclusive()
    {
        var state = WithLog(
            HaltEvent("mine", ExpeditionHalt.GateHeld, 1),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 2),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 3),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 4));

        Assert.Equal(4, GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 4));
        // Asking about an EARLIER day than the caller's "tonight" still counts only up through
        // that day — the query never looks ahead of the day it was asked about.
        Assert.Equal(2, GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 2));
    }

    [Fact]
    public void ATargetReachedNightBreaksTheStreak()
    {
        var state = WithLog(
            HaltEvent("mine", ExpeditionHalt.GateHeld, 1),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 2),
            HaltEvent("mine", ExpeditionHalt.TargetReached, 3),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 4));

        // Day 4 is held, but day 3 cleared it — the streak restarts at 1, it does not see through
        // the clear to the two nights held before it.
        Assert.Equal(1, GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 4));
    }

    [Fact]
    public void ADayWithNoExpeditionAtAllToThisVenue_AlsoBreaksTheStreak()
    {
        // Day 2 has no "expedition-halt:mine" event whatsoever (the party raided a different venue,
        // or nobody departed) — silence reads the same as a clear: not currently held.
        var state = WithLog(
            HaltEvent("mine", ExpeditionHalt.GateHeld, 1),
            HaltEvent("gloomwood", ExpeditionHalt.TargetReached, 2),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 3));

        Assert.Equal(1, GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 3));
    }

    [Fact]
    public void DifferentVenuesAreCountedIndependently()
    {
        // Events for the same day interleave in the array (real logs stamp everything the day's
        // reveal produced together) — DayLog.For's backward scan relies on Day being nondecreasing
        // across the log, not on same-day entries being grouped by venue.
        var state = WithLog(
            HaltEvent("mine", ExpeditionHalt.GateHeld, 1),
            HaltEvent("gloomwood", ExpeditionHalt.GateHeld, 1),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 2),
            HaltEvent("gloomwood", ExpeditionHalt.GateHeld, 2),
            HaltEvent("gloomwood", ExpeditionHalt.GateHeld, 3));

        Assert.Equal(2, GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 2));
        Assert.Equal(3, GateHeldStreakQuery.ConsecutiveNights(state, "gloomwood", day: 3));
    }

    // ---- the anti-nag rule (test requirement: repeated holds must not repeat identical nagging) ----

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(15, false)]
    [InlineData(16, true)]
    [InlineData(63, false)]
    [InlineData(64, true)]
    public void IsMilestoneNight_OnlyDoublingCounts(int streak, bool expected)
    {
        Assert.Equal(expected, GateHeldStreakQuery.IsMilestoneNight(streak));
    }

    [Fact]
    public void MilestoneNights_AreStrictlyIncreasingAndSparse()
    {
        // Across a full 100-day campaign the fact would surface at most 6 times (2,4,8,16,32,64) —
        // never once per held night — which is the whole point of the rule.
        var milestoneCount = 0;
        for (var streak = 1; streak <= 100; streak++)
        {
            if (GateHeldStreakQuery.IsMilestoneNight(streak))
            {
                milestoneCount++;
            }
        }

        Assert.Equal(6, milestoneCount);
    }

    // ---- no sim state written (whole-state fingerprint, not a hand-listed field list) ----

    [Fact]
    public void ReadingTheStreak_WritesNoSimState()
    {
        var state = WithLog(
            HaltEvent("mine", ExpeditionHalt.GateHeld, 1),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 2),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 3),
            HaltEvent("mine", ExpeditionHalt.GateHeld, 4));
        var before = SaveCodec.Serialize(state);

        var streak = GateHeldStreakQuery.ConsecutiveNights(state, "mine", day: 4);
        GateHeldStreakQuery.IsMilestoneNight(streak);

        var after = SaveCodec.Serialize(state);
        Assert.Equal(before, after);
    }
}
