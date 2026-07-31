#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Can a person actually finish a craft, and is the result fair?
///
/// <para><b>The question no other suite asks.</b> <c>ForgeMinigameTests</c> proves the heat/shape
/// arithmetic is right. <c>MinigameKeyboardWorksTests</c> proves the keys reach it. Neither can tell you
/// whether the thing is <i>playable</i>, and that is what actually broke: "also doesn't seem possible to
/// complete? the shape keeps resetting to zero", then after the controls were fixed, "i am incapable of
/// creating anything - something is wrong lol".</para>
///
/// <para>So this runs <see cref="ForgePlayer"/> — a policy that acts only through real key events — across
/// several seeds at two skill levels, and asserts on the OUTCOME distribution. The beginner runs are the
/// ones that matter: "completable by an expert" is not a design goal, it is a warning.</para>
///
/// <para><b>What is asserted vs. reported.</b> Completion is a hard invariant — a craft the player cannot
/// finish is a broken game, full stop. Grades are reported as telemetry in the failure message but only
/// loosely bounded, because a tight grade assertion would go red on every balance tweak and get weakened
/// until it caught nothing. The one grade claim worth failing on is that a competent player is not pinned
/// at the floor, since that is indistinguishable from the game ignoring their input.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeWinnabilityTests
{
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    /// <summary>Seeds to sweep. Enough to expose a policy that only wins on a lucky rhythm, few enough to
    /// stay well inside the suite's time budget (CI runs this roughly 9x slower than a local machine).</summary>
    private static readonly int[] Seeds = [1, 7, 19, 42, 101];

    [TestCase]
    public async Task ABeginnerCanFinishACraft_OnEverySeed()
    {
        var runs = await PlayEach(ForgePlayer.Skill.Beginner);

        var failed = runs.Where(r => !r.Run.Completed).ToList();

        AssertThat(failed)
            .OverrideFailureMessage(
                "A beginner could not finish the craft on these seeds:\n  " +
                string.Join("\n  ", failed.Select(f =>
                    $"seed {f.Seed}: gave up after {f.Run.ElapsedSeconds:0.#}s with shape short of done, " +
                    $"{f.Run.Strikes} strikes, heat bottomed out at {f.Run.LowestHeatPermille}\n    {f.Run.Story}")) +
                "\n\nThis is \"it doesn't seem possible to complete\". If the heat floor is near zero the " +
                "billet is in the death spiral: strike advance is proportional to heat, striking costs heat, " +
                "and the bellows cannot outpace the drain — so the shape stops moving and drifts back.")
            .IsEmpty();
    }

    /// <summary>
    /// A player who has learned the rhythm must be REWARDED for it. If a veteran scores the same as a
    /// beginner, the tempo mechanic is decoration and the minigame is a slot machine wearing a skill
    /// costume — which is the honest reading of "grades land Poor" no matter how you play.
    /// </summary>
    [TestCase]
    public async Task LearningTheRhythm_ActuallyPaysOff()
    {
        var beginner = await PlayEach(ForgePlayer.Skill.Beginner);
        var veteran = await PlayEach(ForgePlayer.Skill.Veteran);

        var beginnerGrade = MeanGrade(beginner);
        var veteranGrade = MeanGrade(veteran);
        var beginnerTime = beginner.Average(r => r.Run.ElapsedSeconds);
        var veteranTime = veteran.Average(r => r.Run.ElapsedSeconds);

        var report =
            $"beginner: mean grade {beginnerGrade:0} permille over {beginnerTime:0.#}s " +
            $"({string.Join(", ", beginner.Select(r => r.Run.GradePermille?.ToString() ?? "DNF"))})\n" +
            $"veteran:  mean grade {veteranGrade:0} permille over {veteranTime:0.#}s " +
            $"({string.Join(", ", veteran.Select(r => r.Run.GradePermille?.ToString() ?? "DNF"))})";

        // Printed, not asserted: the grade distribution is tuning telemetry, and the value of having it in
        // the log is that a balance change shows up as a visible shift rather than a silent one.
        GD.Print($"[forge-telemetry]\n{report}");

        AssertThat(veteranGrade > beginnerGrade)
            .OverrideFailureMessage(
                "Playing well scores no better than playing adequately, so the tempo bonus is not " +
                $"reaching the outcome:\n{report}\n\nStrikeOnTempoBonusMultiplier is " +
                $"{ForgeMinigame.StrikeOnTempoBonusMultiplier}x inside a " +
                $"{ForgeMinigame.TempoOnBeatWindowPermille}-permille window; if that never shows up in the " +
                "grade, the skill the game asks the player to learn does not pay.")
            .IsTrue();

        // The floor claim: a competent player must not be pinned at the bottom of the scale. Deliberately
        // loose — this is here to catch "every craft is Poor whatever you do", not to pin balance.
        AssertThat(veteranGrade)
            .OverrideFailureMessage(
                $"A veteran averages {veteranGrade:0} permille, which is the bottom of the scale:\n{report}\n" +
                "Being unable to score above the floor is indistinguishable, to a player, from the game " +
                "ignoring their input — which is exactly what was reported.")
            .IsGreater(300);
    }

    private static async Task<List<(int Seed, ForgePlayer.Run Run)>> PlayEach(ForgePlayer.Skill skill)
    {
        var results = new List<(int, ForgePlayer.Run)>();

        foreach (var seed in Seeds)
        {
            var forge = new ForgeMinigame { Name = "ForgeMinigame" };
            try
            {
                ((SceneTree)Engine.GetMainLoop()).Root.AddChild(forge);
                forge.Configure(
                    DaggerRecipe,
                    ScriptedSession.CraftMaterial,
                    ProfessionRegistry.Blacksmith,
                    ImmutableSortedSet<string>.Empty,
                    day: 0);

                var player = new HumanPlayer(forge);
                await player.Frames(2); // focus is claimed deferred — see UiKit.ClaimKeyboard

                results.Add((seed, await new ForgePlayer(forge, player, skill, seed).Play()));
            }
            finally
            {
                forge.Free();
            }
        }

        return results;
    }

    /// <summary>Mean grade, counting a run that never finished as 0 rather than skipping it — otherwise a
    /// policy that only completes its best attempts would report a flattering average.</summary>
    private static double MeanGrade(List<(int Seed, ForgePlayer.Run Run)> runs) =>
        runs.Average(r => (double)(r.Run.GradePermille ?? 0));
}
#endif
