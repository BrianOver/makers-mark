using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;

namespace GameSim.Tests.Drama;

/// <summary>
/// U3 (C3, R3): pins the pure "what to print" half of the Camp reframe — extracted from
/// Program.cs's <c>Advance()</c>/<c>PrintLedger</c> into <see cref="CampNarration"/> the same way
/// <see cref="GameSim.Cli.EventNarration"/> is (its own doc: "so the mapping is unit-testable").
/// Exercises both members directly against constructed state/event data, mirroring
/// <c>EventNarrationTests</c>'s style rather than parsing Program.cs's stdout.
/// </summary>
public class CampNarrationTests
{
    private static readonly HeroId Hero = new(1);

    private static InFlightExpedition MinimalParty(bool recalled = false, bool supplySent = false) => new(
        Party: ImmutableList.Create(Hero),
        TargetFloor: 4,
        CheckpointFloor: 1,
        VenueId: "mine",
        Hp: ImmutableSortedDictionary<int, int>.Empty,
        Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
        Gold: ImmutableSortedDictionary<int, int>.Empty,
        Dead: ImmutableSortedSet<int>.Empty,
        Floors: ImmutableList<FloorOutcome>.Empty,
        Loot: ImmutableList<OreLoot>.Empty,
        DeepestFloorCleared: 1)
    {
        Recalled = recalled,
        SupplySent = supplySent,
    };

    private static ImmutableList<GameEvent> CampReportOnly() =>
        ImmutableList.Create<GameEvent>(new PartyCampReport(
            ImmutableList.Create(Hero), CampedBelowFloor: 1, TargetFloor: 4,
            ImmutableSortedDictionary<int, int>.Empty, ImmutableSortedDictionary<int, int>.Empty));

    [Fact]
    public void WindowClosedUntouched_IsTrue_WhenNeitherSendNorRecallLanded()
    {
        Assert.True(CampNarration.WindowClosedUntouched(MinimalParty()));
    }

    [Fact]
    public void WindowClosedUntouched_IsFalse_WhenRecalled()
    {
        Assert.False(CampNarration.WindowClosedUntouched(MinimalParty(recalled: true)));
    }

    [Fact]
    public void WindowClosedUntouched_IsFalse_WhenSupplySent()
    {
        Assert.False(CampNarration.WindowClosedUntouched(MinimalParty(supplySent: true)));
    }

    [Fact]
    public void Attribution_IsNull_WhenHeroNeverCarriedACampSlateToday()
    {
        // No PartyCampReport for this hero today — the party resolved in one stage-1 pass, so no
        // camp story is fabricated (MF-3: never a case where the game invents a choice that wasn't
        // actually offered).
        var line = CampNarration.Attribution(ImmutableList<GameEvent>.Empty, Hero, survived: true);
        Assert.Null(line);
    }

    [Fact]
    public void Attribution_NamesRecallInTime_WhenRecalledAndSurvived()
    {
        var events = CampReportOnly().Add(new PartyRecalled(ImmutableList.Create(Hero)));
        var line = CampNarration.Attribution(events, Hero, survived: true);
        Assert.NotNull(line);
        Assert.Contains("recall", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("too late", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attribution_NamesRecallTooLate_WhenRecalledButDied()
    {
        var events = CampReportOnly().Add(new PartyRecalled(ImmutableList.Create(Hero)));
        var line = CampNarration.Attribution(events, Hero, survived: false);
        Assert.NotNull(line);
        Assert.Contains("too late", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attribution_NamesSupplyCarriedThrough_WhenSuppliedAndSurvived()
    {
        var events = CampReportOnly().Add(new SupplyDelivered(Hero, new ItemId(1), Fee: 5));
        var line = CampNarration.Attribution(events, Hero, survived: true);
        Assert.NotNull(line);
        Assert.Contains("runner", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wasn't enough", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attribution_NamesSupplyWasNotEnough_WhenSuppliedButDied()
    {
        var events = CampReportOnly().Add(new SupplyDelivered(Hero, new ItemId(1), Fee: 5));
        var line = CampNarration.Attribution(events, Hero, survived: false);
        Assert.NotNull(line);
        Assert.Contains("wasn't enough", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attribution_NamesHeldAndMadeIt_WhenNeitherActionAndSurvived()
    {
        var line = CampNarration.Attribution(CampReportOnly(), Hero, survived: true);
        Assert.NotNull(line);
        Assert.Contains("held", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("made it", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attribution_NamesHeldAndLost_WhenNeitherActionAndDied()
    {
        var line = CampNarration.Attribution(CampReportOnly(), Hero, survived: false);
        Assert.NotNull(line);
        Assert.Contains("held", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anyway", line, StringComparison.OrdinalIgnoreCase);
    }
}
