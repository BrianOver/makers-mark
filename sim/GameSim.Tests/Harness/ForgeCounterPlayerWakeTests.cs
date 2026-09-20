using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Harness;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-HONEST-38 (docs/design/MAKERS-MARK.md §11.15, "The harness sits the wake"): link 5 — the
/// outcome becomes the town's memory, with your name in it — had never once been exercised by a
/// policy. §11.15 measured all four wake verbs constructed by ZERO of the eleven policies in
/// <c>sim/GameSim/Harness/</c>: <c>HonorMemorialAction</c> legal at 23,777 Evening decision points
/// and chosen 0, <c>ReforgeHeirloomAction</c> legal at 43,815 and chosen 0, and
/// <c>MemorialHonored</c> / <c>GraveMarkerPlaced</c> / <c>RemembranceChosen</c> /
/// <c>HeirloomReforged</c> emitted 0 times across 40 campaigns.
///
/// <para>These are properties of the wake arm, not of one campaign: every assertion below is
/// phrased against the RULE the arm follows (honour what is unhonoured, lay the best legal piece,
/// take the wake's own default remembrance, carry the legend forward when the question is open)
/// rather than against a named hero, item or seed, so growing the roster, the recipe table or the
/// event vocabulary cannot quietly stop them covering it.</para>
/// </summary>
public class ForgeCounterPlayerWakeTests
{
    /// <summary>Seeds 1..N driven end to end through the production kernel — the same sweep shape
    /// <see cref="ForgeCounterPlayerTests"/>' fleece census uses.</summary>
    private static (int Honored, int Markers, int Remembrances, int Heirlooms, int Memorials) SweepWake(int seeds, int days)
    {
        var kernel = GameSim.GameComposition.BuildKernel();
        var honored = 0;
        var markers = 0;
        var remembrances = 0;
        var heirlooms = 0;
        var memorials = 0;

        foreach (var seed in Enumerable.Range(1, seeds).Select(i => (ulong)i))
        {
            var state = GameSim.GameComposition.NewCampaign(seed);
            while (state.Day <= days)
            {
                var result = kernel.Tick(state, ForgeCounterPlayer.ActionsFor(state));
                state = result.NewState;
                honored += result.Events.OfType<MemorialHonored>().Count();
                markers += result.Events.OfType<GraveMarkerPlaced>().Count();
                remembrances += result.Events.OfType<RemembranceChosen>().Count();
                heirlooms += result.Events.OfType<HeirloomReforged>().Count();
            }

            memorials += state.Drama.Memorials.Count;
        }

        return (honored, markers, remembrances, heirlooms, memorials);
    }

    [Fact]
    public void DrivenAcrossASeedSweep_EveryWakeVerbGetsItsFirstMeasuredOccurrence()
    {
        // The property this unit exists to measure. Not "seed 24 honours a memorial" — every one of
        // link 5's four verbs must actually fire somewhere in a real sweep, because §11.15's finding
        // was that the harness never reached ANY of them, on any seed, ever.
        var wake = SweepWake(seeds: 10, days: 60);

        Assert.True(wake.Memorials > 0, "no hero died across the sweep — the wake's own premise is missing");
        Assert.True(wake.Honored > 0, $"no memorial was ever honoured across the sweep ({wake})");
        Assert.True(wake.Remembrances > 0, $"no remembrance was ever chosen across the sweep ({wake})");
        Assert.True(wake.Markers > 0, $"no grave marker was ever placed across the sweep ({wake})");
        Assert.True(wake.Heirlooms > 0, $"no heirloom was ever reforged across the sweep ({wake})");
    }

    [Fact]
    public void EveryMemorialTheCampaignRaises_IsHonouredByTheEnd()
    {
        // The arm's own rule, stated as a property: it honours what is unhonoured, every Evening,
        // for every memorial in the roster's order. A campaign that ends with an unhonoured grave
        // means the arm skipped one — a budget starved, a legality mis-read, or an ordering bug.
        var kernel = GameSim.GameComposition.BuildKernel();

        foreach (var seed in Enumerable.Range(1, 6).Select(i => (ulong)i))
        {
            var state = GameSim.GameComposition.NewCampaign(seed);
            while (state.Day <= 60)
            {
                state = kernel.Tick(state, ForgeCounterPlayer.ActionsFor(state)).NewState;
            }

            // A hero who falls on the run's last day has no Evening left for their rite, so the
            // property is about every grave the campaign had a night to answer — measured: seed 3
            // ends with exactly one such same-day memorial (Liv) and every earlier grave honoured.
            var unhonoured = state.Drama.Memorials
                .Where(m => !m.Honored && m.Day < state.Day - 1)
                .Select(m => $"{m.HeroName} (day {m.Day}, run ended day {state.Day})")
                .ToList();
            Assert.True(
                unhonoured.Count == 0,
                $"seed {seed} ended with {unhonoured.Count} unhonoured memorial(s) it had nights for: {string.Join(", ", unhonoured)}");
            Assert.True(
                state.Drama.Memorials.Any(m => m.Honored),
                $"seed {seed} honoured nothing at all — the property above would pass vacuously");
        }
    }

    [Fact]
    public void TheWakeArmIsDeterministic_SameSeedSameRites()
    {
        // No RNG, no clock: the arm reads memorials in hero-id order and asks WakeQuery for its
        // choices, so two runs of one seed must lay the same markers in the same order.
        Assert.Equal(SweepWake(seeds: 3, days: 40), SweepWake(seeds: 3, days: 40));
    }
}
