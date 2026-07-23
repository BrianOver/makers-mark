using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel; // GameFactory

namespace GameSim.Tests.Heroes;

/// <summary>
/// Game-Feel Plan G4: <see cref="RaidForecast"/> is the pre-sleep triage board. These pin that it
/// (a) never disagrees with <see cref="MusterPlan.Compute"/>'s party/floor projection — the same
/// prediction the Expedition tick makes real — and (b) correctly enriches it with per-floor threats
/// and empty-gear-slot gaps. Pure, deterministic, RNG-free.
/// </summary>
public class RaidForecastTests
{
    [Fact]
    public void ForTomorrow_RosterAndFloor_MatchMusterPlan()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 4242));

        var plans = MusterPlan.Compute(state.Heroes, state.Bounties);
        var forecast = RaidForecast.ForTomorrow(state);

        Assert.Equal(plans.Count, forecast.Count);
        for (var i = 0; i < plans.Count; i++)
        {
            Assert.Equal(plans[i].TargetFloor, forecast[i].TargetFloor);
            Assert.Equal(plans[i].VenueId, forecast[i].VenueId);
            var expectedNames = plans[i].Roster.Select(id => state.Heroes[id.Value].Name).ToList();
            Assert.Equal(expectedNames, forecast[i].HeroNames);
        }
    }

    [Fact]
    public void Threats_CoverFloorsOneThroughTarget_InOrder()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 7));

        var forecast = RaidForecast.ForTomorrow(state);
        Assert.NotEmpty(forecast);

        foreach (var party in forecast)
        {
            Assert.Equal(party.TargetFloor, party.Threats.Count);
            for (var i = 0; i < party.Threats.Count; i++)
            {
                Assert.Equal(i + 1, party.Threats[i].Floor); // floors 1..TargetFloor, in order
                Assert.False(string.IsNullOrWhiteSpace(party.Threats[i].MonsterKind));
            }
        }
    }

    [Fact]
    public void GearGaps_NameOnlyHeroesWithEmptySlots_AndListEachMissingSlot()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 3));

        // Pin two heroes explicitly (don't assume the starting roster's default gear): one fully
        // equipped, one bare — so the assertions hold regardless of starting kit.
        var heroes = state.Heroes.Values.ToList();
        var full = heroes[0] with { Gear = new GearSet(new ItemId(1), new ItemId(2), new ItemId(3)) };
        var bare = heroes[1] with { Gear = GearSet.Empty };
        state = state with { Heroes = state.Heroes.SetItem(full.Id.Value, full).SetItem(bare.Id.Value, bare) };

        var allGaps = RaidForecast.ForTomorrow(state).SelectMany(p => p.GearGaps).ToList();

        Assert.DoesNotContain(allGaps, g => g.StartsWith($"{full.Name}:")); // fully geared => no gap line
        Assert.Contains($"{bare.Name}: no weapon, no shield, no armor", allGaps);
    }

    [Fact]
    public void ZeroHeroes_ProducesEmptyForecast()
    {
        var state = GameFactory.NewGame(seed: 11); // no roster installed
        Assert.Empty(RaidForecast.ForTomorrow(state));
    }

    [Fact]
    public void ForTomorrow_IsDeterministic()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 99));

        // ForecastParty is a record but its members are ImmutableLists (reference equality), so
        // compare a stable flattened render rather than the objects themselves.
        static string Render(System.Collections.Immutable.ImmutableList<ForecastParty> f) =>
            string.Join("|", f.Select(p =>
                $"{string.Join(",", p.HeroNames)};{p.TargetFloor};{p.VenueId};" +
                $"{string.Join(",", p.Threats.Select(t => $"{t.Floor}:{t.MonsterKind}"))};" +
                $"{string.Join(",", p.GearGaps)}"));

        Assert.Equal(Render(RaidForecast.ForTomorrow(state)), Render(RaidForecast.ForTomorrow(state)));
    }
}
