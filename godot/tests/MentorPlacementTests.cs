#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U37 (R27, R25): Bryn is somewhere, and she remembers. Her tile is a pure function of the sim's
/// own phase (never independent knowledge), the never-spoken-to goodbye carries no reproach, and
/// "was she ever spoken to" survives a quit.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MentorPlacementTests
{
    private static readonly HeroId FallenHeroId = new(9701);

    private static GameState AtPhase(ulong seed, DayPhase phase) =>
        GameFactory.NewGame(seed) with { Phase = phase };

    [TestCase]
    public void TileFor_IsDeterministic_AndKeysOffTheSimPhaseAlone()
    {
        var morning = MentorVoice.TileFor(AtPhase(9701, DayPhase.Morning));
        var sendOff = MentorVoice.TileFor(AtPhase(9702, DayPhase.Expedition));
        var quietEvening = MentorVoice.TileFor(AtPhase(9703, DayPhase.Evening));

        // Same phase, different seed -- the seed is not an input.
        AssertThat(MentorVoice.TileFor(AtPhase(9704, DayPhase.Morning))).IsEqual(morning);
        AssertThat(MentorVoice.TileFor(AtPhase(9705, DayPhase.Expedition))).IsEqual(sendOff);

        // Morning she is at her bench; while a party is away she is at the gate.
        AssertThat(morning).IsEqual(MentorVoice.Station.Tile);
        AssertThat(sendOff).IsNotEqual(morning);

        // A quiet evening is the bench again -- the wall is only for a loss.
        AssertThat(quietEvening).IsEqual(morning);
    }

    [TestCase]
    public void TileFor_OnAnEveningThatLostAHero_IsTheWall_NotTheBenchOrTheGate()
    {
        var lostToday = new HeroDied(FallenHeroId, Floor: 3, Cause: "test", WornGear: GearSet.Empty) { Day = 1 };
        var lostLongAgo = lostToday with { Day = 0 };

        var evening = AtPhase(9706, DayPhase.Evening) with { Day = 1 };
        var wall = MentorVoice.TileFor(evening with { EventLog = ImmutableList.Create<GameEvent>(lostToday) });
        var bench = MentorVoice.TileFor(evening);
        var gate = MentorVoice.TileFor(AtPhase(9707, DayPhase.Expedition));

        AssertThat(wall).IsNotEqual(bench);
        AssertThat(wall).IsNotEqual(gate);

        // Yesterday's loss does not keep her at the wall: the sim's day is the clock, not memory.
        AssertThat(MentorVoice.TileFor(evening with { EventLog = ImmutableList.Create<GameEvent>(lostLongAgo) }))
            .IsEqual(bench);
    }

    [TestCase]
    public void NeverSpokenGoodbye_FiresOnlyWhenNeverAddressed_AndCarriesNoReproach()
    {
        var neverSpoken = TutorialFlow.GraduationBeatTextFor(ruleWasRevised: false, everSpokenTo: false);
        var spokenHeld = TutorialFlow.GraduationBeatTextFor(ruleWasRevised: false, everSpokenTo: true);
        var spokenRevised = TutorialFlow.GraduationBeatTextFor(ruleWasRevised: true, everSpokenTo: true);

        AssertThat(neverSpoken).IsNotEqual(spokenHeld);
        AssertThat(neverSpoken).IsNotEqual(spokenRevised);
        // Once spoken to, the never-spoken variant is unreachable whatever the rule did.
        AssertThat(TutorialFlow.GraduationBeatTextFor(ruleWasRevised: true, everSpokenTo: false)).IsEqual(neverSpoken);

        // The register: she may say it plainly, never with guilt. Phrased as a deny-list so a
        // rewrite of the line stays covered.
        foreach (var reproach in new[] { "should", "could have", "never bothered", "shame", "pity", "ignored", "wish you" })
        {
            AssertThat(neverSpoken.Contains(reproach, System.StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"the never-spoken goodbye reproaches the player (\"{reproach}\"): {neverSpoken}")
                .IsFalse();
        }

        // It still names where the lessons live, like every other goodbye.
        AssertThat(neverSpoken).Contains("Lessons");
    }

    [TestCase]
    public void EverSpokenToMentor_SurvivesAQuit()
    {
        TutorialFlow.DeleteForTests();
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.EverSpokenToMentor).IsFalse();
            ui.Tutorial.NotifyMentorSpokenTo();
            AssertThat(ui.Tutorial.EverSpokenToMentor).IsTrue();

            var ui2 = MountMainUi(); // "quit and relaunch" -- no Unmount(ui) first
            try
            {
                AssertThat(ui2.Tutorial.EverSpokenToMentor)
                    .OverrideFailureMessage("She forgot overnight that she was ever spoken to.")
                    .IsTrue();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui);
            TutorialFlow.DeleteForTests();
        }
    }
}
#endif
