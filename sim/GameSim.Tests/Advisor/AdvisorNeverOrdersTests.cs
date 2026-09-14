using System.Text.RegularExpressions;
using GameSim.Advisor;
using GameSim.Harness;
using GameSim.Kernel;
using GameSim.Tests.Kernel;

namespace GameSim.Tests.Advisor;

// LAW:influence-never-orders

/// <summary>
/// CLAUDE.md rule 12's first law, second half. <see cref="HeroSovereigntyCensusTests"/> proves no
/// player VERB writes hero state outside an honest channel — the mechanism side of "influence never
/// orders." This file proves the other side: the advisor's own WORDS never order the player either.
///
/// <para><b>P2-HONEST-24.</b> Before this unit, <see cref="ObjectiveAdvisor"/> wrote imperatives —
/// "craft 'X' now", "Raise the forge to Tier N", "Accept {hero}'s commission" — and
/// <c>MentorIdleVoice</c> put them verbatim in Bryn's mouth, the game's most trusted character
/// issuing orders the design says she must never give (<c>THE-GAME.md</c> §4.8). The existing
/// <c>LAW:influence-never-orders</c> tripwire never caught it: it measures whether a VERB moves hero
/// state, and nothing here touches a hero at all. A law can bend in its register while every existing
/// gauge of it stays green — this file is the gauge for the register.</para>
///
/// <para><b>Why a property, not the three fixed instances.</b> Hand-checking the three cited lines
/// would pass forever the moment a fourth imperative got written next month by someone who never
/// read this unit. Instead this drives real campaigns across multiple seeds and reads every distinct
/// <see cref="Suggestion.Reason"/> <see cref="ObjectiveAdvisor.Suggest"/> ever produces — the full
/// ranked list, not just the top pick, since a rewrite could fix rank 1 and leave rank 2 untouched —
/// and checks each one against the SHAPE of an order, never a literal string.</para>
/// </summary>
public class AdvisorNeverOrdersTests
{
    /// <summary>
    /// Every base-form command verb this module's own action vocabulary can be phrased with, plus
    /// the generic imperatives a rewrite could reach for instead of the exact verb being suggested.
    /// An order dressed in a different verb is still an order — this list exists so the check does
    /// not depend on which verb this month's rewrite happened to pick.
    /// </summary>
    private static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept", "Buy", "Craft", "Honor", "Shelve", "Stock", "Sell", "Post", "Send", "Unlock",
        "Upgrade", "Raise", "Go", "Take", "Use", "Equip", "Wear", "Pay", "Trade", "Visit", "Talk",
        "Recall", "Retreat", "Flee", "Attack", "Defend", "Check", "Look", "Consider", "Try", "Make",
        "Get", "Bring", "Keep", "Choose", "Pick", "Grab", "Move", "Walk", "Press", "Click",
    };

    /// <summary>A clause boundary within one <see cref="Suggestion.Reason"/> — advisor lines splice
    /// several independent facts with an em-dash or a full stop, and an imperative planted mid-line
    /// after either is exactly as much of an order as one that opens the whole string (this is the
    /// shape "craft 'X' now" used to take: it never led the sentence, it followed an em-dash).</summary>
    private static readonly Regex ClauseSplit = new(@"(?:\. |; | — |—)", RegexOptions.Compiled);

    /// <summary>A command verb paired with a directive "now" anywhere in the same clause — the
    /// specific shape this unit's own cited defects took ("craft 'X' now"), generalized past those
    /// two literal verbs to every verb in <see cref="ImperativeVerbs"/>.</summary>
    private static readonly Regex NowAsCommandTail = new(
        @"\b(accept|buy|craft|honor|shelve|stock|sell|post|send|unlock|upgrade|raise)\b[^.—;]*\bnow\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The second-person directive shape — tells the player what they should/must do
    /// rather than what is true.</summary>
    private static readonly Regex SecondPersonDirective = new(
        @"\byou (should|must|need to)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Does this advisor line ORDER the player rather than inform them? Three independent shapes,
    /// each one a future rewrite could slip into without tripping the other two.
    /// </summary>
    private static bool IsImperative(string reason, out string why)
    {
        if (SecondPersonDirective.IsMatch(reason))
        {
            why = "second-person directive (\"you should/must/need to\")";
            return true;
        }

        if (NowAsCommandTail.IsMatch(reason))
        {
            why = "a command verb paired with a directive \"now\"";
            return true;
        }

        foreach (var clause in ClauseSplit.Split(reason))
        {
            var trimmed = clause.TrimStart('*', ' ', '\'', '"');
            var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstWord is not null && ImperativeVerbs.Contains(firstWord.TrimEnd('.', ',', ':', '\'')))
            {
                why = $"a clause opens on a bare command verb (\"{firstWord}\")";
                return true;
            }
        }

        why = string.Empty;
        return false;
    }

    /// <summary>
    /// Drives several real campaigns (distinct seeds, distinct trajectories) and asserts every
    /// distinct <see cref="Suggestion.Reason"/> the advisor EVER surfaced — top pick and every
    /// ranked-list entry below it — is fact- or stake-phrased, never an order.
    /// </summary>
    [Fact]
    public void NoAdvisorLine_AcrossAnyDrivenState_EverOrdersThePlayer()
    {
        var kernel = GameComposition.BuildKernel();
        var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var distinctReasons = new HashSet<string>(StringComparer.Ordinal);

        foreach (var seed in new ulong[] { 1, 3, 9, 2026, 4242 })
        {
            var state = GameComposition.NewCampaign(seed);
            for (var tick = 0; tick < 40 * 5; tick++)
            {
                foreach (var suggestion in ObjectiveAdvisor.Suggest(state))
                {
                    distinctReasons.Add(suggestion.Reason);
                    if (IsImperative(suggestion.Reason, out var why) && !offenders.ContainsKey(suggestion.Reason))
                    {
                        offenders.Add(suggestion.Reason, why);
                    }
                }

                state = kernel.Tick(state, BaselinePlayer.ActionsFor(state)).NewState;
            }
        }

        // Denominator guard (the green-54 lesson): a scan that drove nothing, or only ever saw one
        // frozen line, would pass this vacuously.
        Assert.True(distinctReasons.Count >= 10,
            $"Only {distinctReasons.Count} distinct advisor line(s) were ever observed across 5 " +
            "seeds x 40 days — too few for a green run here to mean anything; check the drive loop, " +
            "not this test.");

        Assert.True(offenders.Count == 0,
            "Influence-never-orders (CLAUDE.md rule 12, the game's first law) is broken in the " +
            "advisor's OWN voice. The fix is to restate the line as a fact or a stake, never to " +
            "delete the information it carried:\n  " +
            string.Join("\n  ", offenders.Select(kv => $"\"{kv.Key}\" — {kv.Value}")));
    }

    /// <summary>
    /// Sanity check on the checker itself: a hand-written line in each of the three banned shapes
    /// must trip it, and a hand-written fact/stake line in the advisor's own voice must not. Without
    /// this, a typo in the regexes above could make <see cref="IsImperative"/> vacuously false and
    /// the property test above would pass for the wrong reason.
    /// </summary>
    [Theory]
    [InlineData("Craft 'Iron Sword' now.", true)]
    [InlineData("— craft 'Iron Sword' now, you already have enough copper.", true)]
    [InlineData("You should raise the forge to Tier 2.", true)]
    [InlineData("You must accept this commission.", true)]
    [InlineData("Buy 2 copper (7g) — the cheapest path to your next craft.", true)]
    [InlineData("Torvald's commission is open — Weapon at Common+ quality for a 15g premium (due day 12).", false)]
    [InlineData("'Iron Sword' is ready: you already have enough copper.", false)]
    [InlineData("Forge Tier 2 (40g, 6 ore) opens the way to 'Steel Sword'. Torvald carries Weapon gear below floor 3's Fine+ bar (currently Common); 'Tempering' opens that recipe once the forge can hold it.", false)]
    public void IsImperative_MatchesTheThreeNamedShapes_AndNothingElse(string reason, bool expected)
    {
        Assert.Equal(expected, IsImperative(reason, out _));
    }
}
