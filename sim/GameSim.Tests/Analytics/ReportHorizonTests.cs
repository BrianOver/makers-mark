using System.Collections.Immutable;
using Analytics;
using GameSim.Chronicle;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Tests.Analytics;

/// <summary>
/// P2-HONEST-46: a batch export keeps simulating to <c>--days N</c> long after
/// <see cref="CampaignEnded"/> fires — median day 27 under <c>BaselinePlayer</c>, 31 under
/// <c>forgecounter</c>, against a 100-day sweep — so 64–84% of a driven policy's own derived
/// counts land in days no player ever reaches. §11.17 measurement 1 recorded what that costs: a
/// read of whole-file totals concluded a shipped unit had regressed when, split at the Ending, it
/// had done exactly what it was booked to do.
///
/// <para>These guards are phrased against the rule — an event is counted in the column its own day
/// belongs to, and a run that never ends is counted whole and said so — rather than against one
/// fixture's numbers, which the report is free to re-word.</para>
/// </summary>
public class ReportHorizonTests
{
    private static Hero Hero(int id) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: 20, Gold: 5,
        new GearSet(null, null, null), ImmutableList<ItemMemory>.Empty,
        Alive: false, DeepestFloorReached: 1, DiedOnDay: null);

    private static GameEvent Death(int id, int day) =>
        new HeroDied(new HeroId(1), Floor: 1, "a Cave Rat", new GearSet(null, null, null))
        { Id = new EventId(id), Day = day };

    /// <summary>One death before the Ending, one after. The split is the point: a single sum would
    /// report two, and the player only ever lived through one of them.</summary>
    [Fact]
    public void Build_SplitsEveryCountAtTheCampaignsOwnEnding()
    {
        var ended = new CampaignEnded(
            DeepestFloorReached: 3, MemorialCount: 1, HonoredMemorialCount: 1,
            AttributionBeatCount: 0, GossipHighlightCount: 0, LegendaryHeroCount: 0)
        { Id = new EventId(2), Day = 10 };

        var run = ChronicleFor(
            seed: 1, day: 31,
            [Death(1, day: 5), ended, Death(3, day: 20)],
            endedOnDay: 10);

        var report = Report.Build([run]);
        var preIndex = report.IndexOf("## Pre-Ending", StringComparison.Ordinal);
        var postIndex = report.IndexOf("## Post-Ending", StringComparison.Ordinal);

        Assert.True(preIndex >= 0 && postIndex > preIndex, "the report must carry both columns, pre first");

        var pre = report[preIndex..postIndex];
        var post = report[postIndex..];

        // One death in each column — the numbers, not the prose, are what the split is for.
        Assert.Contains("| 1 | 1 |", pre, StringComparison.Ordinal);
        Assert.Contains("| 1 | 1 |", post, StringComparison.Ordinal);
    }

    /// <summary>
    /// A seed that never reaches its Ending is counted WHOLE and named, never silently folded into
    /// the post-Ending column as if its whole life were an afterlife. §11.16 measured six of twenty
    /// `forgecounter` campaigns in exactly this state; a report that hides them hides the finding.
    /// </summary>
    [Fact]
    public void Build_ARunThatNeverEnds_IsCountedWholeAndSaidSo()
    {
        var run = ChronicleFor(seed: 7, day: 31, [Death(1, day: 5), Death(2, day: 20)], endedOnDay: null);

        var report = Report.Build([run]);
        var postIndex = report.IndexOf("## Post-Ending", StringComparison.Ordinal);
        var pre = report[..postIndex];

        Assert.Contains("never ended", report, StringComparison.Ordinal);
        Assert.Contains("7", report, StringComparison.Ordinal); // the seed is named, not just tallied
        Assert.Contains("| 1 | 2 |", pre, StringComparison.Ordinal); // both deaths, in the pre column
    }

    /// <summary>
    /// The stamped field is an accelerator, not a new source of truth: a chronicle written before
    /// <see cref="ChronicleData.EndedOnDay"/> existed still splits correctly off its own
    /// <see cref="CampaignEnded"/> event. Every chronicle already on disk is of that shape, so a
    /// report that needed the field would silently mis-read the whole existing corpus.
    /// </summary>
    [Fact]
    public void Build_AChronicleWrittenBeforeTheStampedField_StillSplitsAtItsOwnEnding()
    {
        var ended = new CampaignEnded(
            DeepestFloorReached: 3, MemorialCount: 1, HonoredMemorialCount: 1,
            AttributionBeatCount: 0, GossipHighlightCount: 0, LegendaryHeroCount: 0)
        { Id = new EventId(2), Day = 10 };

        var stamped = ChronicleFor(1, 31, [Death(1, 5), ended, Death(3, 20)], endedOnDay: 10);
        var legacy = ChronicleFor(1, 31, [Death(1, 5), ended, Death(3, 20)], endedOnDay: null);

        Assert.Equal(Report.Build([stamped]), Report.Build([legacy]));
    }

    /// <summary>The codec stamps the day the sim itself recorded — never a second derivation of
    /// "when did this campaign end", which could drift from the event the ledger reads.</summary>
    [Fact]
    public void FromState_StampsTheEndingDayTheEventLogAlreadyCarries()
    {
        var ended = new CampaignEnded(
            DeepestFloorReached: 5, MemorialCount: 2, HonoredMemorialCount: 2,
            AttributionBeatCount: 9, GossipHighlightCount: 3, LegendaryHeroCount: 1)
        { Id = new EventId(1), Day = 27 };

        var state = GameFactory.NewGame(4242);
        var withEnding = state with { EventLog = state.EventLog.Add(ended) };

        Assert.Equal(27, ChronicleCodec.FromState(4242, withEnding).EndedOnDay);
        Assert.Null(ChronicleCodec.FromState(4242, state).EndedOnDay);
    }

    private static ChronicleData ChronicleFor(ulong seed, int day, GameEvent[] events, int? endedOnDay) =>
        new(seed, day, DayPhase.Morning, ImmutableList.Create(Hero(1)), [.. events], endedOnDay);
}
