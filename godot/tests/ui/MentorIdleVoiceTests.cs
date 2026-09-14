#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Materials;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-MEMORY-05 ("the idle line varies"): <see cref="MentorIdleVoice"/> is a plain, engine-free
/// pure function of <see cref="GameState"/> (same idiom as <see cref="MentorVoiceTests"/>'s own
/// header doc — none of these need <c>[RequireGodotRuntime]</c>). Guards the PROPERTY (does not
/// repeat across distinct states; same state twice renders identically; falls back honestly when
/// nothing is happening), never a hand-picked literal line.
/// </summary>
[TestSuite]
public class MentorIdleVoiceTests
{
    /// <summary>Evening, no heroes, no materials, no commissions, no memorials — every one of
    /// <see cref="GameSim.Advisor.ObjectiveAdvisor.Suggest"/>'s own branches is closed (the buy
    /// branch is Morning-only; the craft-now branch needs stock this state has none of), so it
    /// returns an empty list — the one state honestly producing nothing to say.</summary>
    private static GameState NothingHappeningState() =>
        GameFactory.NewGame(1) with { Phase = DayPhase.Evening };

    /// <summary>Fresh Morning, no materials yet — the advisor's cheapest-productive-path fallback
    /// answers "buy".</summary>
    private static GameState FreshMorningState() => GameFactory.NewGame(1);

    /// <summary>Fresh Morning with every priced material already well-stocked — the SAME fallback
    /// answers "craft" instead, a different sentence for a different live fact.</summary>
    private static GameState StockedMorningState()
    {
        var state = GameFactory.NewGame(1);
        var materials = state.Player.Materials;
        foreach (var key in MaterialRegistry.PricedPool)
        {
            materials = materials.SetItem(key, 999);
        }

        return state with { Player = state.Player with { Materials = materials } };
    }

    [TestCase]
    public void Line_DoesNotRepeat_AcrossDistinctStates()
    {
        var lines = new[]
        {
            MentorIdleVoice.Line(FreshMorningState()),
            MentorIdleVoice.Line(StockedMorningState()),
            MentorIdleVoice.Line(NothingHappeningState()),
        };

        AssertThat(lines.ToHashSet().Count)
            .OverrideFailureMessage(
                $"Bryn spoke the identical line for three different live situations: \"{lines[0]}\" — "
                + "the whole point of this unit is that a real change in what's happening changes what she says.")
            .IsGreater(1);
    }

    [TestCase]
    public void Line_IsDeterministic_SameStateTwice()
    {
        // Two INDEPENDENTLY built states, not the same instance — proves the function reads the
        // state's own facts, not object identity or anything mutable.
        AssertThat(MentorIdleVoice.Line(FreshMorningState()))
            .IsEqual(MentorIdleVoice.Line(FreshMorningState()));
        AssertThat(MentorIdleVoice.Line(StockedMorningState()))
            .IsEqual(MentorIdleVoice.Line(StockedMorningState()));
    }

    [TestCase]
    public void Line_FallsBackToRestingLine_WhenNothingIsActuallyHappening()
    {
        AssertThat(MentorIdleVoice.Line(NothingHappeningState()))
            .IsEqual(MentorVoice.CurrentLesson(null));
    }

    [TestCase]
    public void Line_WhenSomethingIsHappening_SpeaksItInHerOwnVoice()
    {
        var spoken = MentorIdleVoice.Line(FreshMorningState());

        AssertThat(spoken.StartsWith($"{MentorVoice.Name}:"))
            .OverrideFailureMessage($"\"{spoken}\" is not attributed to {MentorVoice.Name}.")
            .IsTrue();
        AssertThat(spoken)
            .OverrideFailureMessage($"\"{spoken}\" still reads the fixed RestingLine even though there is a live objective to speak.")
            .IsNotEqual(MentorVoice.CurrentLesson(null));
    }

    /// <summary>Fresh Morning with an open commission on the board — exercises the branch
    /// P2-HONEST-24 rewrote from an imperative ("Accept {hero}'s commission") to a fact
    /// ("{hero}'s commission is open").</summary>
    private static GameState OpenCommissionState()
    {
        // The roster is EMPTY on a fresh game -- heroes arrive by recruitment, not at creation -- so
        // reading .First() off it threw "Sequence contains no elements" and took this whole test with
        // it. The fixture has to seed its own hero, the same way every other client test that needs
        // one does (AdventureTickerTests.Delver, CampPanelTests.Strong, ArcScenesTests.TorvaldHero).
        var state = GameFactory.NewGame(1);
        var hero = new Hero(
            new HeroId(1), "Kael", "vanguard", Level: 3, MaxHp: 40, Gold: 10,
            GearSet.Empty, ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

        return state with
        {
            Heroes = state.Heroes.SetItem(hero.Id.Value, hero),
            Commissions = ImmutableList.Create(new Commission(
                hero.Id, ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: state.Day + 5, PremiumGold: 15)),
        };
    }

    /// <summary>The same three command shapes <c>AdvisorNeverOrdersTests</c> (sim-side) checks for,
    /// mirrored here rather than shared across assemblies — this project has no reference to
    /// <c>GameSim.Tests</c>. Deliberately smaller (this file only needs to catch a regression in the
    /// handful of states it drives, not stand as the law's own tripwire — that is the sim-side
    /// test's job).</summary>
    private static bool IsImperative(string line) =>
        Regex.IsMatch(line, @"\byou (should|must|need to)\b", RegexOptions.IgnoreCase)
        || Regex.IsMatch(line, @"\b(accept|buy|craft|honor|shelve|stock|sell|post|send|unlock|upgrade|raise)\b[^.—;]*\bnow\b", RegexOptions.IgnoreCase)
        || Regex.Split(line, @"(?:\. |; | — |—)")
            .Select(clause => clause.TrimStart('*', ' ', '\'', '"').Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Any(firstWord => firstWord is not null && ImperativeVerbs.Contains(firstWord.TrimEnd('.', ',', ':', '\'')));

    private static readonly HashSet<string> ImperativeVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Accept", "Buy", "Craft", "Honor", "Shelve", "Stock", "Sell", "Post", "Send", "Unlock",
            "Upgrade", "Raise", "Go", "Take", "Use", "Equip", "Wear", "Pay", "Trade", "Visit", "Talk",
        };

    /// <summary>
    /// P2-HONEST-24: Bryn's idle voice is a verbatim pass-through of the advisor's own <see
    /// cref="GameSim.Advisor.Suggestion.Reason"/> (see <see cref="MentorIdleVoice"/>'s own class
    /// doc) — proven here by equality against <see cref="MentorVoice.Speak"/> applied directly to
    /// the same Reason, across states that exercise branches this unit rewrote (a fresh-game buy
    /// fallback and an open commission). The direct imperative check is a local backstop only; the
    /// law's own tripwire is the sim-side property test, which this file cannot reach.
    /// </summary>
    [TestCase]
    public void Line_SpeaksTheAdvisorsReasonVerbatim_AndNeverOrdersThePlayer()
    {
        foreach (var state in new[] { FreshMorningState(), StockedMorningState(), OpenCommissionState() })
        {
            var top = ObjectiveAdvisor.Suggest(state).FirstOrDefault();
            if (top is null)
            {
                continue;
            }

            var spoken = MentorIdleVoice.Line(state);
            AssertThat(spoken).IsEqual(MentorVoice.Speak(top.Reason));

            // Checked against the RAW reason, not the wrapped "Bryn: "..."" string — the wrapper's
            // own "Bryn:" prefix would mask a leading command verb in the line it wraps.
            AssertThat(IsImperative(top.Reason))
                .OverrideFailureMessage($"Bryn's line reads as an order: \"{spoken}\"")
                .IsFalse();
        }
    }
}
#endif
