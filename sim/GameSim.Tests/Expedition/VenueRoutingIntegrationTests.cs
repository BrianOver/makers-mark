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
    public void RealCampaign_RoutesPartiesAcrossTheLadder_Over100Days_NeverStrandsOrInvents()
    {
        // The distribution guard for the RANK router. Every hero starts at rank 0, so BOTH rank-0
        // peers (Mine, Sunken Crypt) must see traffic — that queue-split is still real, still tested.
        //
        // RE-MEASURED (forward-ladder plan 2026-08-10-003 L3): this test used to pin that Gloomwood
        // NEVER saw traffic and no hero ever graduated on this exact seed, because the Mine/Crypt
        // floor-5 gate (100) was a WALL against the measured 63-73 power plateau. L3 re-gated floor
        // 5 to 70 (with floor-5 monster stats dialed down for a fair, survivable fight — see
        // VenueRegistry.BuildMine's comment) and Gloomwood's own boss gate to 73 (see
        // GloomwoodVenue.Build's comment): on THIS seed, characterization now shows graduation at
        // day 13 and the Gloomwood boss falling at day 16 — three heroes reach rank 2 by day 100.
        // Gloomwood traffic and non-zero ranks are now the MEASURED reality, not an aspiration.
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

        // Gloomwood now ALSO sees traffic on this seed — the ladder's second rung is reachable,
        // not just registered (the whole point of L3's re-gate).
        Assert.Contains("gloomwood", seenVenues);

        // Every venue id that ever appeared is a member of the live rotation — routing never invents
        // or strands a party at an unregistered/non-live venue.
        foreach (var venueId in seenVenues)
        {
            Assert.Contains(venueId, VenueRegistry.LiveRotation);
        }

        // At least one hero graduated past rank 0 — the ladder actually moves on this seed now.
        // Every rank is a legal value (0/1/2 — Emberfall stays dormant, L4's job) and monotonic by
        // construction (ExpeditionRevealSystem is the only write site); no test here claims a
        // SPECIFIC count, since that is exactly the kind of economy-pace number this plan's own
        // L1 finding warns against over-pinning.
        Assert.Contains(state.Heroes.Values, hero => hero.LadderRank > 0);
        Assert.All(state.Heroes.Values, hero => Assert.InRange(hero.LadderRank, 0, 2));
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
