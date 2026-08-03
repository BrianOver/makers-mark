using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// End-to-end proof (Phase C U-C4, extended for the T1 four-venue flip): over a real, seeded,
/// <see cref="BaselinePlayer"/>-driven campaign, parties actually depart to EVERY live venue
/// (<see cref="VenueRegistry.LiveRotation"/>) — not just the Mine — and the Morning prediction
/// (<c>MusterSystem</c>/<c>MusterPlan.Compute</c>) never disagrees with which venue the Expedition
/// tick actually used. This complements the pure <see cref="GameSim.Tests.Venues.VenueRouterTests"/>
/// unit suite with the real kernel wiring.
/// </summary>
public class VenueRoutingIntegrationTests
{
    /// <summary>Every <see cref="ExpeditionResult.VenueId"/>/<see cref="InFlightExpedition.VenueId"/>
    /// a real campaign produces — parked (staged, camping) and finalised (unstaged/immediate)
    /// results both carry the field, so the union covers every party that departed that day.</summary>
    private static IEnumerable<string> VenueIdsThisTick(GameState state) =>
        state.PendingExpeditions.Select(r => r.VenueId)
            .Concat(state.InFlight.Select(f => f.VenueId));

    [Fact]
    public void RealCampaign_RoutesPartiesToEveryLiveVenue_Over100Days()
    {
        // THE distribution guard for the banded router: on this measured seed, a 100-day campaign
        // sends parties to every live venue (early band mine+crypt in the opening weeks, Gloomwood
        // once parties cross 72, Emberfall once they cross 79). If a future tuning change breaks
        // this, the venue distribution moved: re-run the batch farm and re-place the EntryPower
        // bands consciously, don't just swap the seed.
        //
        // Seed reassigned 3 -> 1 (2026-08-02, P3/task #45 go-live re-tune, EntryPower 72 -> 79):
        // seed 3 was itself a reassignment FROM seed 1, taken while Gloomwood and Emberfall were
        // tied at band 72 (seed 1 landed on zero Gloomwood visits once Emberfall's ordinal
        // tie-break won every close call). Un-tying the bands (Emberfall now strictly later at
        // 79) removes that tie-break entirely, and a fresh 20-seed x 100-day batch-farm sweep on
        // THIS branch confirms seed 1 reaches all four live venues again (gloomwood/mine/emberfall
        // /sunken-crypt all present in its chronicle) — reverting to the original reference seed
        // rather than carrying the tie-era workaround forward.
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 1);

        var seenVenues = new HashSet<string>(StringComparer.Ordinal);

        for (var day = 0; day < 100; day++)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // Morning
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // Expedition

            foreach (var venueId in VenueIdsThisTick(state))
            {
                seenVenues.Add(venueId);
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // Evening
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // Camp
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // ExpeditionDeep
        }

        // Every live venue actually saw traffic — routing is real, and no venue is starved to
        // zero (the exact failure mode the banded router replaced tightest-fit to fix).
        foreach (var venueId in VenueRegistry.LiveRotation)
        {
            Assert.Contains(venueId, seenVenues);
        }

        // Every venue id that ever appeared is a member of the live rotation — routing never invents
        // or strands a party at an unregistered/non-live venue.
        foreach (var venueId in seenVenues)
        {
            Assert.Contains(venueId, VenueRegistry.LiveRotation);
        }
    }

    [Fact]
    public void PredictedVenue_ByteMatches_ExpeditionSystem_Over40Days()
    {
        // The Morning prediction's PartyPlan.VenueId must equal what the Expedition tick actually
        // used for the SAME roster, every day — the same no-drift property
        // MusterSystemTests.PredictedRoster_ByteMatches_ExpeditionSystem_Over100Days already pins for
        // roster/floor, extended here to venue.
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 4242);

        for (var day = 0; day < 40; day++)
        {
            var morning = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = morning.NewState;
            var predicted = Assert.Single(morning.Events.OfType<PartiesFormed>());

            var expedition = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = expedition.NewState;

            // Reconstruct which venue each departed party actually raided, in the SAME order
            // PartyFormation produces (parties are processed in order, so PendingExpeditions/InFlight
            // append in the same order the Morning prediction enumerated them).
            var actualVenueByRoster = new Dictionary<string, string>();
            foreach (var result in state.PendingExpeditions)
            {
                actualVenueByRoster[string.Join(",", result.Party.Select(h => h.Value))] = result.VenueId;
            }

            foreach (var inFlight in state.InFlight)
            {
                actualVenueByRoster[string.Join(",", inFlight.Party.Select(h => h.Value))] = inFlight.VenueId;
            }

            foreach (var plan in predicted.Parties)
            {
                var key = string.Join(",", plan.Roster.Select(h => h.Value));
                if (actualVenueByRoster.TryGetValue(key, out var actualVenueId))
                {
                    Assert.Equal(actualVenueId, plan.VenueId);
                }
            }

            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // Evening
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // Camp
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState; // ExpeditionDeep
        }
    }
}
