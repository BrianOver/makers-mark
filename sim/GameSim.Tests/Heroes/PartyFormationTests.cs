using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Tests.Heroes;

/// <summary>Covers R7's party half: deterministic role-composition grouping for U6's resolver.</summary>
public class PartyFormationTests
{
    private static Hero MakeHero(int id, HeroRole role, bool alive = true) => new(
        new HeroId(id), $"Hero{id}", role, Level: 1, MaxHp: 25, Gold: 40,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: null);

    private static ImmutableSortedDictionary<int, Hero> Roster(params Hero[] heroes) =>
        heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h);

    private static ImmutableSortedDictionary<int, Hero> StandardSix(bool hero3Alive = true, bool hero6Alive = true) => Roster(
        MakeHero(1, HeroRole.Vanguard),
        MakeHero(2, HeroRole.Vanguard),
        MakeHero(3, HeroRole.Striker, hero3Alive),
        MakeHero(4, HeroRole.Striker),
        MakeHero(5, HeroRole.Mystic),
        MakeHero(6, HeroRole.Mystic, hero6Alive));

    [Fact]
    public void SixAlive_TwoPartiesOfThree_EachWithAVanguard()
    {
        var roster = StandardSix();

        var parties = PartyFormation.FormParties(roster);

        Assert.Equal(2, parties.Count);
        Assert.All(parties, p => Assert.Equal(3, p.Count));
        Assert.All(parties, p => Assert.Contains(p, id => roster[id.Value].Role == HeroRole.Vanguard));

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
        var parties = PartyFormation.FormParties(Roster(MakeHero(4, HeroRole.Mystic)));

        var solo = Assert.Single(parties);
        Assert.Equal(new HeroId(4), Assert.Single(solo));
    }

    [Fact]
    public void ZeroAlive_FormsNoParties()
    {
        var parties = PartyFormation.FormParties(Roster(
            MakeHero(1, HeroRole.Vanguard, alive: false),
            MakeHero(2, HeroRole.Striker, alive: false)));

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
        Assert.Contains(parties[0], id => roster[id.Value].Role == HeroRole.Vanguard);

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
            MakeHero(1, HeroRole.Striker),
            MakeHero(2, HeroRole.Mystic),
            MakeHero(3, HeroRole.Mystic)));

        var party = Assert.Single(parties);
        Assert.Equal(3, party.Count);
    }
}
