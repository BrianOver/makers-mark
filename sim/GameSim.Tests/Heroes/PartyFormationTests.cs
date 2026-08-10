using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Tests.Heroes;

/// <summary>Covers R7's party half: deterministic role-composition grouping for U6's resolver.</summary>
public class PartyFormationTests
{
    private static Hero MakeHero(int id, string classId, bool alive = true, int ladderRank = 0) => new Hero(
        new HeroId(id), $"Hero{id}", classId, Level: 1, MaxHp: 25, Gold: 40,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: null) with { LadderRank = ladderRank };

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    private static ImmutableSortedDictionary<int, Hero> StandardSix(bool hero3Alive = true, bool hero6Alive = true) => Roster(
        MakeHero(1, "vanguard"),
        MakeHero(2, "vanguard"),
        MakeHero(3, "striker", hero3Alive),
        MakeHero(4, "striker"),
        MakeHero(5, "mystic"),
        MakeHero(6, "mystic", hero6Alive));

    [Fact]
    public void SixAlive_TwoPartiesOfThree_EachWithAVanguard()
    {
        var roster = StandardSix();

        var parties = PartyFormation.FormParties(roster);

        Assert.Equal(2, parties.Count);
        Assert.All(parties, p => Assert.Equal(3, p.Count));
        Assert.All(parties, p => Assert.Contains(p, id => roster[id.Value].ClassId == "vanguard"));

        // Every alive hero parties exactly once.
        var all = parties.SelectMany(p => p).Select(id => id.Value).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, all);
    }

    [Fact]
    public void FormParties_IsDeterministic_SameRosterSameParties()
    {
        var a = PartyFormation.FormParties(StandardSix());
        var b = PartyFormation.FormParties(StandardSix());

        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i], b[i]);
        }
    }

    [Fact]
    public void OneAlive_FormsSoloParty()
    {
        var parties = PartyFormation.FormParties(Roster(MakeHero(4, "mystic")));

        var solo = Assert.Single(parties);
        Assert.Equal(new HeroId(4), Assert.Single(solo));
    }

    [Fact]
    public void ZeroAlive_FormsNoParties()
    {
        var parties = PartyFormation.FormParties(Roster(
            MakeHero(1, "vanguard", alive: false),
            MakeHero(2, "striker", alive: false)));

        Assert.Empty(parties);
    }

    [Fact]
    public void DeadHeroes_NeverParty_LeftoverFormsSmallerParty()
    {
        // 4 alive (heroes 3 and 6 dead) -> one party of 3 + one solo leftover.
        var roster = StandardSix(hero3Alive: false, hero6Alive: false);

        var parties = PartyFormation.FormParties(roster);

        Assert.Equal(2, parties.Count);
        Assert.Equal(3, parties[0].Count);
        Assert.Single(parties[1]);
        Assert.Contains(parties[0], id => roster[id.Value].ClassId == "vanguard");

        var all = parties.SelectMany(p => p).Select(id => id.Value).ToArray();
        Assert.DoesNotContain(3, all);
        Assert.DoesNotContain(6, all);
        Assert.Equal(4, all.Length);
    }

    [Fact]
    public void NoVanguardsAlive_PartiesStillForm()
    {
        // "At least 1 Vanguard" is a preference, not a hard gate — heroes still go.
        var parties = PartyFormation.FormParties(Roster(
            MakeHero(1, "striker"),
            MakeHero(2, "mystic"),
            MakeHero(3, "mystic")));

        var party = Assert.Single(parties);
        Assert.Equal(3, party.Count);
    }

    // ---- Forward ladder L2 (plan 2026-08-10-003): cohort by rank before anchor/id rules ----

    /// <summary>Ranks {0,0,0,1,1,2}: the plan's own worked example. Hero4 (rank 1) is an
    /// anchor-class hero too — deliberately, to prove a same-class anchor in ANOTHER cohort
    /// never leaks into the rank-0 party the way a single global anchor queue would allow.</summary>
    private static ImmutableSortedDictionary<int, Hero> MixedRankSix() => Roster(
        MakeHero(1, "vanguard", ladderRank: 0),
        MakeHero(2, "striker", ladderRank: 0),
        MakeHero(3, "mystic", ladderRank: 0),
        MakeHero(4, "vanguard", ladderRank: 1),
        MakeHero(5, "striker", ladderRank: 1),
        MakeHero(6, "mystic", ladderRank: 2));

    [Fact]
    public void MixedRankRoster_CohortsByRank_BeforeAnchorAndIdRules()
    {
        var roster = MixedRankSix();

        var parties = PartyFormation.FormParties(roster);

        Assert.Equal(3, parties.Count);

        // Every formed party's members share exactly one rank — the postcondition routing
        // (VenueRouter's MIN-of-members rule) now relies on being exact rather than interim.
        foreach (var party in parties)
        {
            Assert.Single(party.Select(id => roster[id.Value].LadderRank).Distinct());
        }

        var rank0Party = Assert.Single(parties, p => roster[p[0].Value].LadderRank == 0);
        Assert.Equal(new[] { 1, 2, 3 }, rank0Party.Select(id => id.Value).OrderBy(v => v));
        Assert.Contains(rank0Party, id => roster[id.Value].ClassId == "vanguard"); // anchor rule holds within the cohort
        Assert.DoesNotContain(rank0Party, id => id.Value == 4); // rank-1's anchor must not leak in

        var rank1Party = Assert.Single(parties, p => roster[p[0].Value].LadderRank == 1);
        Assert.Equal(new[] { 4, 5 }, rank1Party.Select(id => id.Value).OrderBy(v => v));

        var rank2Party = Assert.Single(parties, p => roster[p[0].Value].LadderRank == 2);
        Assert.Equal(new HeroId(6), Assert.Single(rank2Party)); // a solo veteran run is honest drama, not a bug
    }

    [Fact]
    public void MixedRankRoster_FormationIsDeterministic_SameRosterSameParties()
    {
        var a = PartyFormation.FormParties(MixedRankSix());
        var b = PartyFormation.FormParties(MixedRankSix());

        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i], b[i]);
        }
    }
}
