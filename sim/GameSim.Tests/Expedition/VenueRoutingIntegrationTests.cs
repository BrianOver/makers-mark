using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Expedition;

/// <summary>
/// End-to-end proof (Phase C U-C4, extended for the T1 four-venue flip; REWRITTEN for the forward
/// ladder — owner ruling 2026-08-10, plan 2026-08-10-003 L1, §11.8's fix): over a real, seeded,
/// <see cref="BaselinePlayer"/>-driven campaign, parties depart to a live venue, never an invented
/// or unregistered one, and the Morning prediction (<c>MusterSystem</c>/<c>MusterPlan.Compute</c>)
/// never disagrees with which venue the Expedition tick actually used. This complements the pure
/// <see cref="GameSim.Tests.Venues.VenueRouterTests"/> unit suite and the rank-controlled
/// <see cref="LadderRoutingTests"/> with the real kernel wiring under UNCONTROLLED (organic economy)
/// play.
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
    public void RealCampaign_RoutesPartiesToTheStarterTier_Over100Days_NeverStrandsOrInvents()
    {
        // The distribution guard for the RANK router. Every hero starts at rank 0, so BOTH rank-0
        // peers (Mine, Sunken Crypt) must see traffic — that queue-split is still real, still tested.
        //
        // Gloomwood (rank 1) is DELIBERATELY NOT asserted here anymore. Under the old EntryPower
        // router this test proved every live venue got traffic because power alone opened the door;
        // under the ladder, a venue only opens once a party GRADUATES (clears its current rung's
        // bottom floor), and that is an economy-pace question, not a routing one. Measured on this
        // exact seed (characterization run, L1 PR body): BaselinePlayer's party-average power
        // plateaus at 63-73 by day ~15 and never moves again in 100 days — short of the Mine/Crypt
        // floor-5 gate (100) — so nobody graduates and Gloomwood legitimately never sees a party.
        // That ceiling is independent of this router (it is a function of gear/level growth, which
        // this PR does not touch) and is already flagged as a finding for the plan's later units
        // (L3/L4 raise the craft-side ceiling with higher-tier rung recipes). Pinning "Gloomwood gets
        // traffic in 100 days" here would be pinning today's economy pace, not the router's contract.
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

        // Both rank-0 peers saw traffic — routing is real, and neither starter venue is starved to
        // zero (the exact failure mode the banded router, and now the ranked router, both guard).
        Assert.Contains(VenueRegistry.MineId, seenVenues);
        Assert.Contains("sunken-crypt", seenVenues);

        // Every venue id that ever appeared is a member of the live rotation — routing never invents
        // or strands a party at an unregistered/non-live venue.
        foreach (var venueId in seenVenues)
        {
            Assert.Contains(venueId, VenueRegistry.LiveRotation);
        }

        // No hero's LadderRank ever moved — the observed side effect of the same power ceiling: with
        // nobody clearing a bottom floor, graduation never fires on this seed, which is itself a
        // pin on today's measured pace (not a claim the mechanism is inert — see LadderRoutingTests
        // and ExpeditionRevealSystemTests for the directly-forced graduation proofs).
        Assert.All(state.Heroes.Values, hero => Assert.Equal(0, hero.LadderRank));
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
