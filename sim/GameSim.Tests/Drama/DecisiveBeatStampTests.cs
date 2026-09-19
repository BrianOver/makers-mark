using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Venues;

namespace GameSim.Tests.Drama;

/// <summary>
/// P2-MEMORY-23: <see cref="AttributionBeatEvent.Decisive"/> is stamped at reveal from the recorded
/// fight, and it must agree with the live predicate the Telling modal and gossip already use — for
/// as long as that fight is still held. After that, the stamp is the only record.
/// </summary>
public class DecisiveBeatStampTests
{
    [Fact]
    public void EveryKillBeatsStamp_AgreesWithTheLivePredicate_WhileTheFightIsStillHeld()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed: 2026);
        var compared = 0;
        var decisive = 0;
        var nonKillBeats = 0;

        for (var tick = 0; tick < 40 * 5; tick++)
        {
            state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            if (state.Phase != DayPhase.Morning)
            {
                continue;
            }

            // Morning: LastNightExpeditions is the night just revealed; its beats are in the log.
            foreach (var result in state.LastNightExpeditions)
            {
                var venue = VenueRegistry.All.TryGetValue(result.VenueId, out var v) ? v : VenueRegistry.Mine;
                foreach (var beat in result.Beats)
                {
                    var logged = state.EventLog.OfType<AttributionBeatEvent>().Last(e =>
                        e.Beat == beat.Beat && e.Item == beat.Item && e.Hero == beat.Hero && e.Floor == beat.Floor && e.Detail == beat.Detail);
                    if (beat.Beat == BeatType.KillingBlow)
                    {
                        var live = TellingQuery.KillingBlowIsDecisive(result, beat, state.Items, venue);
                        Assert.True(live == logged.Decisive, $"day {state.Day}: {beat.Detail} stamped {logged.Decisive}, live {live}");
                        compared++;
                        decisive += logged.Decisive ? 1 : 0;
                    }
                    else
                    {
                        Assert.True(logged.Decisive, $"{beat.Beat} must always be decisive: {beat.Detail}");
                        nonKillBeats++;
                    }
                }
            }
        }

        Assert.True(compared >= 10, $"only {compared} kill beats compared — the fixture stopped exercising the stamp");
        Assert.True(decisive > 0 && decisive < compared,
            $"{decisive}/{compared} kills decisive — a stamp that is all one value is not distinguishing anything");
        Assert.True(nonKillBeats > 0, "no non-kill beats seen in 40 days — widen the run");
    }
}
