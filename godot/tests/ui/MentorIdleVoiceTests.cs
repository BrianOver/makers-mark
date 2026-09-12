#if GDUNIT_TESTS
using System.Linq;
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
}
#endif
