using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Venues;
using GameSim.Venues.Emberfall;
using GameSim.Venues.Gloomwood;
using GameSim.Venues.SunkenCrypt;
using Xunit;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-MEMORY-27: the director's vocabulary is no longer Mine-only. Every live venue owns a rumor and a
/// notable, and a venue's incidents enter the day's weight table only once a party has mustered for it
/// (<see cref="DirectorSystem.RaidedVenues"/> over the recorded <see cref="PartiesFormed"/> plans).
/// </summary>
public sealed class VenueIncidentTests
{
    [Fact]
    public void EveryLiveVenue_OwnsARumorAndANotable()
    {
        foreach (var venueId in VenueRegistry.LiveRotation)
        {
            var own = DirectorSystem.Catalog.Where(i => i.VenueId == venueId).ToList();
            Assert.Contains(own, i => i.Category == IncidentCategory.Rumor && i.Magnitude == IncidentMagnitude.Minor);
            Assert.Contains(own, i => i.Magnitude == IncidentMagnitude.Notable);
        }
    }

    [Fact]
    public void EveryCatalogVenue_IsRegistered_AndIdsAreUnique()
    {
        foreach (var inc in DirectorSystem.Catalog)
        {
            Assert.True(VenueRegistry.All.ContainsKey(inc.VenueId), $"{inc.Id} names unregistered venue '{inc.VenueId}'");
        }

        Assert.Equal(DirectorSystem.Catalog.Length, DirectorSystem.Catalog.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void BeforeAnyMuster_OnlyTheMineIsEligible()
    {
        var state = NewWorld();
        Assert.Empty(state.EventLog.OfType<PartiesFormed>());

        var raided = DirectorSystem.RaidedVenues(state);
        Assert.Equal(new[] { VenueRegistry.MineId }, raided.OrderBy(v => v));

        var eligible = DirectorSystem.EligibleIds(state);
        Assert.NotEmpty(eligible);
        Assert.All(eligible, id => Assert.Equal(VenueRegistry.MineId, DirectorSystem.Catalog.Single(i => i.Id == id).VenueId));
    }

    [Fact]
    public void AMusterForAVenue_UnlocksThatVenuesRumor_AndNoOthers()
    {
        var state = NewWorld();
        var hero = state.Heroes.Keys.First();
        var plan = new PartyPlan(ImmutableList.Create(new HeroId(hero)), TargetFloor: 1, GloomwoodVenue.Id);
        var formed = new PartiesFormed(ImmutableList.Create(plan)) { Day = state.Day };
        var raidedGloomwood = state with { EventLog = state.EventLog.Add(formed) };

        var eligible = DirectorSystem.EligibleIds(raidedGloomwood);
        Assert.Contains("lanterns_in_the_gloomwood", eligible);
        Assert.DoesNotContain("the_causeway_sings", eligible);
        Assert.DoesNotContain("smoke_over_emberfall", eligible);
        // The notable still waits on survivors (MinSurvived 2) — a fresh town has none.
        Assert.DoesNotContain("bramble_chokes_the_paths", eligible);
    }

    [Fact]
    public void PickByCumulativeWeight_NeverReturnsAnUnraidedVenue()
    {
        var raided = ImmutableHashSet.Create(VenueRegistry.MineId, SunkenCryptVenue.Id);
        var total = DirectorSystem.Catalog.Where(i => DirectorSystem.IsEligible(i, 9, 9, raided)).Sum(i => i.Weight);
        var picks = Enumerable.Range(0, total).Select(r => DirectorSystem.PickByCumulativeWeight(9, 9, raided, r)).ToList();
        Assert.All(picks, p => Assert.Contains(p.VenueId, raided));
        Assert.Contains(picks, p => p.Id == "wights_walk_the_causeway");
        Assert.DoesNotContain(picks, p => p.Id == "smoke_over_emberfall");
    }
}
