using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Harness;
using Xunit;
using Xunit.Abstractions;

namespace GameSim.Tests.Harness;

/// <summary>
/// P2-HONEST-39 (§11.15, "The harness sends the runner"): decision 6 — send the runner or trust their
/// judgment — had never been taken. §11.15 measured <c>SendSupply</c> legal at 1,858 decision points
/// and chosen 0, <c>SupplyDelivered</c> 0 across 40 campaigns, 1,301 of 1,319 forgecounter camps
/// (98.6%) carrying no heal, and 5 <c>Provisioned</c> / 1 <c>PotionLifesave</c> of 5,750 beats.
///
/// <para>These are properties of the arm's rule — send when the sim's own too-hurt bar says a camper
/// is under it, at most one runner per party per day — not of a named seed or hero.</para>
/// </summary>
public class ForgeCounterPlayerVigilTests
{
    private readonly ITestOutputHelper _out;

    public ForgeCounterPlayerVigilTests(ITestOutputHelper output) => _out = output;

    private sealed record VigilTally(int Deliveries, int Camps, int CampsWithNoHeal, int Provisioned, int PotionLifesaves);

    private static VigilTally Sweep(int seeds, int days)
    {
        var kernel = GameSim.GameComposition.BuildKernel();
        int deliveries = 0, camps = 0, campsNoHeal = 0, provisioned = 0, lifesaves = 0;

        foreach (var seed in Enumerable.Range(1, seeds).Select(i => (ulong)i))
        {
            var state = GameSim.GameComposition.NewCampaign(seed);
            while (state.Day <= days)
            {
                var result = kernel.Tick(state, ForgeCounterPlayer.ActionsFor(state));
                state = result.NewState;

                deliveries += result.Events.OfType<SupplyDelivered>().Count();
                foreach (var camp in result.Events.OfType<PartyCampReport>())
                {
                    camps++;
                    if (camp.HealsLeftByHero.Values.Sum() == 0)
                    {
                        campsNoHeal++;
                    }
                }

                foreach (var beat in result.Events.OfType<AttributionBeatEvent>())
                {
                    if (beat.Beat == BeatType.Provisioned)
                    {
                        provisioned++;
                    }
                    else if (beat.Beat == BeatType.PotionLifesave)
                    {
                        lifesaves++;
                    }
                }
            }
        }

        return new VigilTally(deliveries, camps, campsNoHeal, provisioned, lifesaves);
    }

    [Fact]
    public void DrivenAcrossASeedSweep_TheRunnerActuallyGoes()
    {
        // The property this unit exists to measure: the verb was legal for 1,858 decision points and
        // no policy had ever taken it, so a sweep that still delivers nothing means the arm is inert.
        var tally = Sweep(seeds: 10, days: 60);

        _out.WriteLine($"P2-HONEST-39 vigil census (10 seeds x 60 days): {tally}");
        Assert.True(tally.Camps > 0, "no party ever camped across the sweep — the arm's own premise is missing");
        Assert.True(tally.Deliveries > 0, $"the runner never went ({tally})");
    }

    [Fact]
    public void CampsCarryingNoHeal_FallBelowTheMeasuredNinetyNinePercent()
    {
        // §11.15: 1,301 of 1,319 forgecounter camps (98.6%) carried no heal at all. The arm cannot
        // reach every camp — a party that fell hurt with an empty forge gets nothing — so this is a
        // floor under the improvement, not a pin on the exact share.
        var tally = Sweep(seeds: 10, days: 60);
        var share = (double)tally.CampsWithNoHeal / tally.Camps;

        _out.WriteLine($"camps with no heal: {tally.CampsWithNoHeal} of {tally.Camps} ({share:P1}), before 98.6%");
        Assert.True(share < 0.95, $"{share:P1} of camps still carry no heal — the arm is barely reaching any of them ({tally})");
    }

    [Fact]
    public void TheVigilArmIsDeterministic_SameSeedSameRunners()
    {
        // No RNG, no clock: parties in InFlight order, the neediest camper by HP share then hero id,
        // the lowest-id sendable salve. Two runs of one seed must send the same runners.
        Assert.Equal(Sweep(seeds: 3, days: 40), Sweep(seeds: 3, days: 40));
    }
}
