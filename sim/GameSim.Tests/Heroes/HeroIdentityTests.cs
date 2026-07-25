using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>
/// Phase B (B1d, R-B4): duplicate-name disambiguation is a DISPLAY-time derived view — it must
/// never touch <see cref="Hero.Name"/> (that would poison B2's <c>(HeroId, Name)</c> trait-hash
/// input), only the string this helper returns.
/// </summary>
public class HeroIdentityTests
{
    private static Hero MakeHero(int id, string name) => new(
        new HeroId(id), name, "vanguard", Level: 1, MaxHp: 25, Gold: 0,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static GameState StateWith(params Hero[] heroes) =>
        GameFactory.NewGame(seed: 1) with
        {
            Heroes = heroes.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        };

    [Fact]
    public void UniqueName_DisplaysBare_NoEpithet()
    {
        var state = StateWith(MakeHero(1, "Torvald"), MakeHero(2, "Brunhilde"));

        Assert.Equal("Torvald", HeroIdentity.DisplayName(new HeroId(1), state));
        Assert.Equal("Brunhilde", HeroIdentity.DisplayName(new HeroId(2), state));
    }

    [Fact]
    public void DuplicateName_FirstArrivalKeepsBareName_LaterOnesGetOrdinalEpithets()
    {
        var state = StateWith(
            MakeHero(1, "Torvald"),
            MakeHero(2, "Torvald"),
            MakeHero(3, "Torvald"));

        Assert.Equal("Torvald", HeroIdentity.DisplayName(new HeroId(1), state));
        Assert.Equal("Torvald the Younger", HeroIdentity.DisplayName(new HeroId(2), state));
        Assert.Equal("Torvald the Third", HeroIdentity.DisplayName(new HeroId(3), state));
    }

    [Fact]
    public void DisambiguationIsOrderedByHeroId_NotRosterInsertionOrder()
    {
        // The dictionary keys ascend by HeroId regardless of construction order — arrival order
        // IS HeroId order (recruits mint monotonically), so id 1 is "first" even if listed last.
        var state = StateWith(MakeHero(2, "Torvald"), MakeHero(1, "Torvald"));

        Assert.Equal("Torvald", HeroIdentity.DisplayName(new HeroId(1), state));
        Assert.Equal("Torvald the Younger", HeroIdentity.DisplayName(new HeroId(2), state));
    }

    [Fact]
    public void NeverMutatesTheStoredName()
    {
        var state = StateWith(MakeHero(1, "Torvald"), MakeHero(2, "Torvald"));

        HeroIdentity.DisplayName(new HeroId(2), state);

        Assert.Equal("Torvald", state.Heroes[2].Name); // the stored field is untouched
    }

    [Fact]
    public void UnknownHeroId_FallsBackToRawIdString_NeverThrows()
    {
        var state = StateWith(MakeHero(1, "Torvald"));

        var display = HeroIdentity.DisplayName(new HeroId(99), state);

        Assert.Equal(new HeroId(99).ToString(), display);
    }
}
