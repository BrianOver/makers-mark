using GameSim;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Harness;
using GameSim.Kernel;

namespace GameSim.Tests.Balance;

/// <summary>
/// Phase D U-D2 (plan 2026-07-21-008): the Guild Assessment heartbeat's Confidence gauge over a full
/// 100-day <see cref="BaselinePlayer"/> campaign — stays in band (never pins at either rail for the
/// whole run, never goes solvent-breaking), the dues cadence actually fires repeatedly (the heartbeat
/// is not inert), and the campaign stays deterministic. Companion to <see cref="BalanceSimTests"/>
/// (same seed/day budget), kept separate so a re-tune of THIS gauge's constants doesn't touch the
/// unrelated core-progression bands.
/// </summary>
public class GuildAssessmentBalanceTests
{
    private const int Days = 100;
    private const ulong MainSeed = 2026; // matches BalanceSimTests' main seed

    private sealed record ConfidenceStats(
        string FinalJson,
        int MinConfidencePermille,
        int MaxConfidencePermille,
        int AssessmentsPassed,
        int AssessmentsMissed,
        int FinalPlayerGold);

    private static ConfidenceStats Run(ulong seed)
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(seed);

        var minConfidence = state.Rent.ConfidencePermille;
        var maxConfidence = state.Rent.ConfidencePermille;
        var passed = 0;
        var missed = 0;

        for (var tick = 0; tick < Days * 5; tick++) // 5-phase day (staged resolution)
        {
            var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
            state = result.NewState;

            minConfidence = Math.Min(minConfidence, state.Rent.ConfidencePermille);
            maxConfidence = Math.Max(maxConfidence, state.Rent.ConfidencePermille);

            foreach (var gameEvent in result.Events)
            {
                switch (gameEvent)
                {
                    case GuildAssessmentPassed:
                        passed++;
                        break;
                    case GuildAssessmentMissed:
                        missed++;
                        break;
                }
            }
        }

        return new ConfidenceStats(
            SaveCodec.Serialize(state), minConfidence, maxConfidence, passed, missed, state.Player.Gold);
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_Confidence_StaysInBand_OnMainSeed()
    {
        var stats = Run(MainSeed);

        // Always a legal 0-1000 permille value — the clamp never overshoots either rail.
        Assert.InRange(stats.MinConfidencePermille, 0, 1000);
        Assert.InRange(stats.MaxConfidencePermille, 0, 1000);

        // A sane band: BaselinePlayer plays reasonably well over 100 days, so Confidence should
        // never be driven all the way to the soft-fail floor on the main seed (a total collapse here
        // would mean the heartbeat's constants are miscalibrated, not a legitimate hard finding).
        Assert.True(stats.MinConfidencePermille > 0,
            $"Confidence bottomed out at 0 on the main seed baseline run (min {stats.MinConfidencePermille}) — re-tune the decay/bonus constants");

        // The heartbeat is not inert: over 100 days (≈14 assessment cycles) at least a few assessments
        // actually resolve (paid or missed).
        Assert.True(stats.AssessmentsPassed + stats.AssessmentsMissed >= 10,
            $"too few Guild Assessments resolved in 100 days: {stats.AssessmentsPassed} passed + {stats.AssessmentsMissed} missed");

        Assert.True(stats.FinalPlayerGold >= 0, $"player went insolvent (final gold {stats.FinalPlayerGold})");
    }

    [Theory]
    [Trait("Category", "Balance")]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    [InlineData(1234UL)]
    public void HundredDay_Confidence_StaysInBand_SeedSweep(ulong seed)
    {
        var stats = Run(seed);

        Assert.InRange(stats.MinConfidencePermille, 0, 1000);
        Assert.InRange(stats.MaxConfidencePermille, 0, 1000);
        Assert.True(stats.FinalPlayerGold >= 0, $"seed {seed}: insolvent (final gold {stats.FinalPlayerGold})");
    }

    [Fact]
    [Trait("Category", "Balance")]
    public void HundredDay_WithGuildAssessment_IsDeterministic()
    {
        Assert.Equal(Run(MainSeed).FinalJson, Run(MainSeed).FinalJson);
    }
}
