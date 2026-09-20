using System.Collections.Immutable;
using GameSim;
using GameSim.Advisor;
using GameSim.Cli;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Economy;
using GameSim.Harness;
using Xunit.Abstractions;

namespace GameSim.Tests.Cli;

/// <summary>
/// P2-HONEST-40 (docs/design/MAKERS-MARK.md §11.15 measurement 4): the masterwork chain joins the
/// sweep, and this census is the reading it produces.
///
/// <para><b>What the §11.15 census could not see.</b> <see cref="MasterworkSeekingPlayer"/> is the
/// only policy in <c>Harness/</c> that constructs <see cref="MasterworkAttemptAction"/>,
/// <see cref="BuyForgeSupplyAction"/> or <see cref="UpgradeForgeAction"/>, and it was not on
/// <see cref="BatchRunner"/>'s <c>--policy</c> list — so every campaign the census swept read zero
/// Masterwork crafts, which is indistinguishable from "the top grade is unreachable". This file
/// measures the difference: the same seeds under the new policy against the baseline one.</para>
///
/// <para><b>Why the forge-supply verb read "never legal" at ten thousand baseline decision
/// points.</b> Not a game gate. <see cref="ActionLegality.IsLegal"/> has a real case for the whole
/// forge chain, but <see cref="ActionLegality.LegalActions"/> — the candidate enumerator every
/// decision-point census walks — never CONSTRUCTS a candidate for any of those four verbs, so no
/// census built on it can report them as legal whatever the state says. <c>BuyForgeSupplyLegal</c>
/// itself asks only for the Morning phase, a stocked key (coal or flux), gold for the line, and a
/// free action slot: <see cref="ForgeSupplyVerbs_AreLegalOnRealMornings_ButTheEnumeratorNeverOffersThem"/>
/// shows both halves of that on one trajectory. <c>BalanceCorpusCoverageCensusTests</c> already
/// names the same enumerator gap for <c>CommissionLegendaryWorkAction</c>; this pins it as a
/// measurement rather than a comment, and changes no sim rule to do it.</para>
///
/// Fast lane: short campaigns, kernel driven directly (no file IO — <c>BatchRunnerTests</c> covers
/// the CLI surface), so this stays a measurement and never becomes a balance gate.
/// </summary>
public class MasterworkPolicySweepCensusTests
{
    private const int SeedCount = 8;
    private const int Days = 30;

    private readonly ITestOutputHelper _output;

    public MasterworkPolicySweepCensusTests(ITestOutputHelper output) => _output = output;

    /// <summary>The forge chain: every verb whose only harness author is
    /// <see cref="MasterworkSeekingPlayer"/>. Named as the family, not as one id, so a fifth
    /// forge-chain verb joins this census by being added here rather than by being forgotten.</summary>
    private static ImmutableList<PlayerAction> ForgeChainCandidates() =>
        ImmutableList.Create<PlayerAction>(
            new UpgradeForgeAction(),
            new BuyForgeSupplyAction(ForgeSupplyHandlers.Coal, 1),
            new BuyForgeSupplyAction(ForgeSupplyHandlers.Flux, 1),
            new MasterworkAttemptAction(RecipeTable.All.Values.First().RecipeId, RecipeTable.All.Values.First().MaterialKey));

    [Fact]
    public void MasterworkPolicy_ReachesTheTopCraftGrade_WhereBaselineNeverDoes()
    {
        var masterwork = Sweep(BatchRunner.Policy.Masterwork);
        var baseline = Sweep(BatchRunner.Policy.Baseline);

        _output.WriteLine($"P2-HONEST-40 craft-grade census — {SeedCount} seeds x {Days} days");
        _output.WriteLine("policy     | crafts | Poor | Common | Fine | Superior | Masterwork | attempts");
        _output.WriteLine(Row("masterwork", masterwork));
        _output.WriteLine(Row("baseline", baseline));

        // The property: the axis exists so the top grade is REACHED, not so a new filename appears.
        // A policy that runs and never produces the grade it was written to produce is an
        // instrument reading zero for the same reason the census already read zero.
        Assert.True(
            masterwork.Grades[QualityGrade.Masterwork] > 0,
            $"the masterwork policy produced no Masterwork craft in {SeedCount}x{Days}: {Row("masterwork", masterwork)}");

        // And the contrast that makes the reading mean something: the same seeds under the policy
        // the census actually swept never reach it, so this is the axis's own contribution.
        Assert.Equal(0, baseline.Grades[QualityGrade.Masterwork]);
        Assert.Equal(0, baseline.MasterworkAttempts);
    }

    [Fact]
    public void ForgeSupplyVerbs_AreLegalOnRealMornings_ButTheEnumeratorNeverOffersThem()
    {
        var legalMornings = 0;
        var offeredMornings = 0;

        foreach (var seed in Seeds())
        {
            var kernel = GameComposition.BuildKernel();
            var state = GameComposition.NewCampaign(seed);
            while (state.Day <= Days)
            {
                if (state.Phase == DayPhase.Morning)
                {
                    var supply = new BuyForgeSupplyAction(ForgeSupplyHandlers.Coal, 1);
                    if (ActionLegality.IsLegal(state, supply, state.Phase))
                    {
                        legalMornings++;
                    }

                    var offered = ActionLegality.LegalActions(state, state.Phase);
                    if (ForgeChainCandidates().Any(c => offered.Any(o => o.GetType() == c.GetType())))
                    {
                        offeredMornings++;
                    }
                }

                state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            }
        }

        _output.WriteLine(
            $"BuyForgeSupply under baseline, {SeedCount} seeds x {Days} days: "
            + $"legal at {legalMornings} Mornings, offered by LegalActions at {offeredMornings}");

        // Half one: the verb's own precondition (Morning + a stocked key + gold + a free slot) is
        // met all the time on an ordinary baseline trajectory. Nothing in the game gates it shut.
        Assert.True(legalMornings > 0, "BuyForgeSupply was never legal on any baseline Morning");

        // Half two: and yet no forge-chain verb is ever handed back as a candidate — which is the
        // whole reason a decision-point census reads "never legal". The enumerator, not the rule.
        Assert.Equal(0, offeredMornings);
    }

    private static IEnumerable<ulong> Seeds() =>
        Enumerable.Range(1, SeedCount).Select(i => (ulong)i);

    private static Census Sweep(BatchRunner.Policy policy)
    {
        var policyFn = BatchRunner.PolicyFn(policy, CraftHand.Average);
        var startingProfession = BatchRunner.PolicyStartingProfession(policy);
        var grades = Enum.GetValues<QualityGrade>().ToDictionary(g => g, _ => 0);
        var attempts = 0;

        foreach (var seed in Seeds())
        {
            var kernel = GameComposition.BuildKernel();
            var state = startingProfession is null
                ? GameComposition.NewCampaign(seed)
                : GameComposition.NewCampaign(seed, startingProfession);

            while (state.Day <= Days)
            {
                var submitted = policyFn(state);
                attempts += submitted.Count(a => a is MasterworkAttemptAction);
                var result = kernel.Tick(state, submitted);

                // A rejected attempt crafts nothing, so only the accepted ones can move a grade —
                // count the submissions separately from what the forge actually produced.
                attempts -= result.Rejected.Count(r => r.Action is MasterworkAttemptAction);
                state = result.NewState;
            }

            foreach (var crafted in state.EventLog.OfType<ItemCrafted>())
            {
                grades[crafted.Quality]++;
            }
        }

        return new Census(grades, attempts);
    }

    private static string Row(string name, Census census) =>
        $"{name,-10} | {census.Grades.Values.Sum(),6} | {census.Grades[QualityGrade.Poor],4} | "
        + $"{census.Grades[QualityGrade.Common],6} | {census.Grades[QualityGrade.Fine],4} | "
        + $"{census.Grades[QualityGrade.Superior],8} | {census.Grades[QualityGrade.Masterwork],10} | {census.MasterworkAttempts,8}";

    private sealed record Census(Dictionary<QualityGrade, int> Grades, int MasterworkAttempts);
}
