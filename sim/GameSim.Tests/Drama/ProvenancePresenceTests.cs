using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Venues;
using GameSim.Venues.Gloomwood;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-MEMORY-17: the presence clause. <see cref="ProvenanceQuery.Presence"/> is a pure read model
/// over the event log — the difference between the party's actual departure floor
/// (<see cref="PartyDeparted"/>) and the depth-based default it would have earned with no bounty in
/// play (rebuilt from <see cref="FloorRecordSet"/> history strictly before the day, never a hero's
/// live field). No new event, no Contracts edit, and no reading of BountyPosted/BountyJudged —
/// the clause is the difference between two logged numbers, not an inference from the bounty event.
/// </summary>
public class ProvenancePresenceTests
{
    private static GameState WithLog(params GameEvent[] events) =>
        NewWorld() with { EventLog = events.ToImmutableList() };

    private static PartyPlan Plan(HeroId hero, int targetFloor, string venueId) =>
        new(ImmutableList.Create(hero), targetFloor, venueId);

    [Fact]
    public void Presence_BountyDrivenExpedition_RendersTheClause()
    {
        var hero = new HeroId(1);
        var state = WithLog(
            new FloorRecordSet(hero, 2) with { Id = new EventId(1), Day = 3 },
            new PartiesFormed(ImmutableList.Create(Plan(hero, 5, VenueRegistry.MineId))) with { Id = new EventId(2), Day = 5 },
            new PartyDeparted(ImmutableList.Create(hero), TargetFloor: 5) with { Id = new EventId(3), Day = 5 });

        var presence = ProvenanceQuery.Presence(state, hero, day: 5);

        Assert.Equal(new ProvenanceQuery.PresenceInfo(5), presence);
        Assert.Equal("Floor 5 — your gold said floor 5.", ProvenanceQuery.PresenceClause(presence));
    }

    [Fact]
    public void Presence_NormalNight_NoOverride_RendersNothing()
    {
        var hero = new HeroId(2);
        var state = WithLog(
            new FloorRecordSet(hero, 2) with { Id = new EventId(1), Day = 3 },
            // 2 + 1 = 3, the natural default — no bounty, no override.
            new PartyDeparted(ImmutableList.Create(hero), TargetFloor: 3) with { Id = new EventId(2), Day = 5 });

        var presence = ProvenanceQuery.Presence(state, hero, day: 5);

        Assert.Null(presence);
        Assert.Equal(string.Empty, ProvenanceQuery.PresenceClause(presence));
    }

    [Fact]
    public void Presence_BountyPostedButNotAccepted_RendersNothing()
    {
        // A bounty sitting on the board unaccepted has no logged effect on the departure floor —
        // TargetFloorFor's override arm never fires without an AcceptedBy, so the party still
        // departs for its natural default. Presence never reads BountyPosted/BountyJudged at all;
        // this proves an unaccepted bounty sitting in the log cannot manufacture a false claim.
        var hero = new HeroId(3);
        var state = WithLog(
            new BountyPosted(new BountyId(1), TargetFloor: 5, RewardGold: 20) with { Id = new EventId(1), Day = 4 },
            new FloorRecordSet(hero, 2) with { Id = new EventId(2), Day = 3 },
            new PartyDeparted(ImmutableList.Create(hero), TargetFloor: 3) with { Id = new EventId(3), Day = 5 });

        Assert.Null(ProvenanceQuery.Presence(state, hero, day: 5));
    }

    [Fact]
    public void Presence_NoLoggedDeparture_RendersNothing()
    {
        Assert.Null(ProvenanceQuery.Presence(WithLog(), new HeroId(9), day: 5));
    }

    [Fact]
    public void Presence_IgnoresSameDayFloorRecordSet_FromThisSameExpeditionsOwnReveal()
    {
        // This same trip pushed the hero to floor 5 and stamped FloorRecordSet same-day. Reading
        // that record instead of "strictly before the day" would inflate the recomputed default to
        // 6 (clamped to 5) and hide the exact case this query exists to catch — a bounty pushing a
        // party deeper than they had earned. Strictly-before uses the true prior depth (2) instead.
        var hero = new HeroId(4);
        var state = WithLog(
            new FloorRecordSet(hero, 2) with { Id = new EventId(1), Day = 3 },
            new PartiesFormed(ImmutableList.Create(Plan(hero, 5, VenueRegistry.MineId))) with { Id = new EventId(2), Day = 5 },
            new PartyDeparted(ImmutableList.Create(hero), TargetFloor: 5) with { Id = new EventId(3), Day = 5 },
            new FloorRecordSet(hero, 5) with { Id = new EventId(4), Day = 5 }); // this same trip's own reveal

        var presence = ProvenanceQuery.Presence(state, hero, day: 5);

        Assert.Equal(new ProvenanceQuery.PresenceInfo(5), presence);
    }

    [Fact]
    public void Presence_NoBountyGloomwoodCeiling_UsesLoggedVenueFloorCount_NoFalsePositive()
    {
        // A bounty-free party already at the Gloomwood's ceiling (4 floors) departs for floor 4 —
        // its own natural default, clamped to ITS venue, not the Mine's larger one. Assuming the
        // Mine's 5-floor cap here would recompute a default of 5 and wrongly claim gold moved it.
        var hero = new HeroId(5);
        var state = WithLog(
            new FloorRecordSet(hero, 4) with { Id = new EventId(1), Day = 3 },
            new PartiesFormed(ImmutableList.Create(Plan(hero, 4, GloomwoodVenue.Id))) with { Id = new EventId(2), Day = 5 },
            new PartyDeparted(ImmutableList.Create(hero), TargetFloor: 4) with { Id = new EventId(3), Day = 5 });

        Assert.Null(ProvenanceQuery.Presence(state, hero, day: 5));
    }

    [Fact]
    public void ItemPresence_ReadsHeroAndDayFromTheItemsOwnAttributionBeat()
    {
        var hero = new HeroId(6);
        var item = new ItemId(30);
        var state = WithLog(
            new FloorRecordSet(hero, 2) with { Id = new EventId(1), Day = 3 },
            new PartiesFormed(ImmutableList.Create(Plan(hero, 5, VenueRegistry.MineId))) with { Id = new EventId(2), Day = 5 },
            new PartyDeparted(ImmutableList.Create(hero), TargetFloor: 5) with { Id = new EventId(3), Day = 5 },
            new AttributionBeatEvent(BeatType.LethalSave, item, hero, Floor: 5, Detail: "held") with { Id = new EventId(4), Day = 5 });

        var presence = ProvenanceQuery.ItemPresence(state, item);

        Assert.Equal(new ProvenanceQuery.PresenceInfo(5), presence);
    }

    [Fact]
    public void ItemPresence_NoBeat_RendersNothing()
    {
        Assert.Null(ProvenanceQuery.ItemPresence(WithLog(), new ItemId(99)));
    }
}
