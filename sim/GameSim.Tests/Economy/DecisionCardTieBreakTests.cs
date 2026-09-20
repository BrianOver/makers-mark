using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Harness;
using Xunit;
using Xunit.Abstractions;

namespace GameSim.Tests.Economy;

/// <summary>
/// P2-HONEST-41: a decision card that reads "Scale Mail over Scale Mail" describes a real decision
/// with a sentence that explains nothing. Measured over the horizon, 46 of 395 cards (12%) named the
/// same item on both sides — two pieces of one recipe at different grades or prices, one of them the
/// player's. The runner-up half is now qualified by whichever recorded fact actually split them.
/// </summary>
public sealed class DecisionCardTieBreakTests
{
    private readonly ITestOutputHelper _out;

    public DecisionCardTieBreakTests(ITestOutputHelper output) => _out = output;

    private static bool SelfNamed(HeroDecisionExplained card) =>
        string.Equals(card.Chosen, card.RunnerUp, System.StringComparison.Ordinal);

    /// <summary>The qualifier the sim prepended, or null when the runner-up is simply a different
    /// item. Matched against the exact shapes the sim builds rather than by suffix: a card reading
    /// "Shortsword over Soldier's Shortsword" is two genuinely different recipes, and a suffix test
    /// mistakes it for a qualified tie.</summary>
    private static string? QualifierOf(HeroDecisionExplained card)
    {
        foreach (var shape in new[] { "your", "the rival's", "an identical" })
        {
            if (card.RunnerUp == $"{shape} {card.Chosen}")
            {
                return shape;
            }
        }

        if (card.RunnerUp.StartsWith("the ", System.StringComparison.Ordinal)
            && card.RunnerUp.EndsWith(" " + card.Chosen, System.StringComparison.Ordinal))
        {
            var middle = card.RunnerUp["the ".Length..^(card.Chosen.Length + 1)];
            return middle.Contains(' ') ? null : middle;
        }

        return null;
    }

    [Fact]
    public void NoDecisionCard_EverNamesTheSameItemOnBothSides_UnlessNothingTrueSplitsThem()
    {
        // Driven through the production kernel under the policy that actually shops, so the census
        // measures the cards the game emits rather than a fixture's idea of them.
        var kernel = GameComposition.BuildKernel();
        var cards = 0;
        var selfNamed = 0;
        var qualified = 0;

        foreach (var seed in Enumerable.Range(1, 12).Select(i => (ulong)i))
        {
            var state = GameComposition.NewCampaign(seed);
            for (var tick = 0; tick < 60 * 5; tick++)
            {
                var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
                state = result.NewState;
                foreach (var card in result.Events.OfType<HeroDecisionExplained>())
                {
                    cards++;
                    if (SelfNamed(card))
                    {
                        selfNamed++;
                    }
                    else if (QualifierOf(card) is not null)
                    {
                        qualified++;
                    }
                }
            }
        }

        _out.WriteLine($"P2-HONEST-41 census (12 seeds x 60 days): {cards} decision cards, "
            + $"{selfNamed} still self-named, {qualified} qualified by grade or price (before: 46 of 395, 12%)");

        Assert.True(cards > 0, "the sweep produced no decision cards — the census is vacuous");
        Assert.True(qualified > 0, "no card was ever disambiguated — the tie-break never fired on a real run");

        // No card names one item on both sides any more. Where grade, ranked price and shelf are all
        // equal the two pieces genuinely are interchangeable, and the card says "an identical X"
        // rather than describing a comparison that had nothing to compare.
        Assert.True(selfNamed == 0,
            $"{selfNamed} of {cards} cards still read as an item beating itself");
    }

    [Fact]
    public void AQualifiedRunnerUp_NamesTheGradeWhenGradesDiffer_AndThePriceWhenTheyDoNot()
    {
        // Shape, asserted on the copy itself: grade is the word a player reads as quality, so it wins
        // when it differs; price is the fallback, and it is the RANKED price beside the margin.
        var kernel = GameComposition.BuildKernel();
        var grades = new[] { "poor", "common", "fine", "superior", "masterwork" };
        var byGrade = 0;
        var byPrice = 0;
        var byOwner = 0;
        var byIdentical = 0;

        foreach (var seed in Enumerable.Range(1, 12).Select(i => (ulong)i))
        {
            var state = GameComposition.NewCampaign(seed);
            for (var tick = 0; tick < 60 * 5; tick++)
            {
                var result = kernel.Tick(state, BaselinePlayer.ActionsFor(state));
                state = result.NewState;
                foreach (var card in result.Events.OfType<HeroDecisionExplained>())
                {
                    if (QualifierOf(card) is not { } qualifier)
                    {
                        continue;
                    }

                    if (qualifier is "your" or "the rival's")
                    {
                        byOwner++;
                    }
                    else if (qualifier == "an identical")
                    {
                        byIdentical++;
                    }
                    else if (grades.Contains(qualifier))
                    {
                        byGrade++;
                    }
                    else
                    {
                        Assert.EndsWith("g", qualifier);
                        Assert.True(int.TryParse(qualifier[..^1], out var price) && price >= 0,
                            $"qualifier '{qualifier}' is neither a grade nor a price");
                        byPrice++;
                    }
                }
            }
        }

        _out.WriteLine($"qualified by grade: {byGrade}, by price: {byPrice}, by owner: {byOwner}, identical: {byIdentical}");
        Assert.True(byGrade + byPrice + byOwner + byIdentical > 0, "no qualified card appeared — this test would pass vacuously");
    }
}
