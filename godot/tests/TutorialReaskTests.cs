#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U20 (§11.14.14, "the two absences" #2): "there is no way to ask where to go." The shortcut
/// legend covered movement, interact, the minigame verbs, escape, the docket, fullscreen, and
/// quick-travel — nothing let a player say "remind me what I'm doing." This suite pins the "remind
/// me" re-ask (<c>MainUi.ReaskTutorial</c>) end to end: the key and the on-screen button both reach
/// the SAME handler, it restates the current step's own on-screen line and nothing else, it is
/// inert once the course completes, and its camera peek (<see cref="Town2D.FocusOn"/>) moves the
/// camera only on the press itself — never on a tick, matching law 1 (influence never orders: an
/// automatic nudge would be illegal, but a player-requested peek is not).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialReaskTests
{
    private static readonly InputEventKey ReaskKeyPress = new() { PhysicalKeycode = Key.R, Pressed = true };

    private static string BannerText(MainUi ui) =>
        ui.Mentor.FindChild("MentorBannerText", true, false) is Label label ? label.Text : string.Empty;

    [TestCase]
    public void ReaskKey_RestatesTheCurrentStepsOwnOnScreenLine()
    {
        var ui = MountMainUi();
        try
        {
            var expected = ObjectiveTracker.Plain(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!);

            ui._UnhandledKeyInput(ReaskKeyPress);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Pressing the re-ask key did not show anything.")
                .IsTrue();
            AssertThat(BannerText(ui))
                .OverrideFailureMessage(
                    $"The re-ask banner does not restate the objective card's own line:\n  \"{BannerText(ui)}\"")
                .Contains(expected);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The matching on-screen control (<c>ObjectiveTutorialReask</c>) reaches the IDENTICAL
    /// handler the key does — same restated line, not a second, drifting copy of the feature.</summary>
    [TestCase]
    public void ReaskButton_ReachesTheSameHandlerAsTheKey()
    {
        var ui = MountMainUi();
        try
        {
            var expected = ObjectiveTracker.Plain(ui.Tutorial.TopSlotText(ui.Adapter.CurrentState)!);

            Press(ui.Objective, "ObjectiveTutorialReask");

            AssertThat(ui.Mentor.Visible).IsTrue();
            AssertThat(BannerText(ui)).Contains(expected);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Idempotent and side-effect-free on the SIM: no PlayerAction submitted, no step
    /// advanced, no first-touch id consumed — a re-ask is not a lesson, it is the same line every
    /// time. Pressed three times in a row on purpose (not once) — the whole point being pinned is
    /// that repeating it changes nothing FURTHER, not just that a single press is harmless.</summary>
    [TestCase]
    public void Reask_ChangesNothingElse_NoActionSubmitted_NoStepAdvanced_NoFirstTouchConsumed()
    {
        var ui = MountMainUi();
        try
        {
            var stepBefore = ui.Tutorial.Step;
            var actionLogCountBefore = ui.Adapter.CurrentState.ActionLog.Count;
            var firedCountBefore = ui.Tutorial.FirstTouch.Fired.Count;

            ui._UnhandledKeyInput(ReaskKeyPress);
            ui._UnhandledKeyInput(ReaskKeyPress);
            ui._UnhandledKeyInput(ReaskKeyPress);

            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Re-asking must never advance the chain.")
                .IsEqual(stepBefore);
            AssertThat(ui.Adapter.CurrentState.ActionLog.Count)
                .OverrideFailureMessage("Re-asking submitted a PlayerAction.")
                .IsEqual(actionLogCountBefore);
            AssertThat(ui.Tutorial.FirstTouch.Fired.Count)
                .OverrideFailureMessage(
                    "Re-asking consumed a first-touch lesson id — it must never be a first-touch itself.")
                .IsEqual(firedCountBefore);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Once the course actually ends (the real dismiss flow — Press then confirm, the same
    /// two presses a player makes, per <c>TutorialFlowTests.DismissButton_...</c>'s own precedent),
    /// both halves of the re-ask go silent: the on-screen control hides, and the key does nothing.</summary>
    [TestCase]
    public void Reask_IsInert_OnceTheCourseCompletes()
    {
        var ui = MountMainUi();
        try
        {
            Press(ui.Objective, "ObjectiveTutorialDismiss");
            Press(ui.Objective, "ObjectiveTutorialDismissConfirmYes");
            AssertThat(ui.Tutorial.Active)
                .OverrideFailureMessage("Fixture guard: the dismiss flow did not end the chain.")
                .IsFalse();

            AssertThat(Find<Button>(ui.Objective, "ObjectiveTutorialReask").Visible)
                .OverrideFailureMessage("The on-screen control must hide once the course is no longer active.")
                .IsFalse();

            ui._UnhandledKeyInput(ReaskKeyPress);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The re-ask key fired a banner after the course was dismissed.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Law 1's own tripwire: the camera must sit still on an idle tick with nothing
    /// pressed, and must move toward the anchor only once the key is actually pressed. Teleports the
    /// player away from BuyMaterial's own "forge" anchor first (<c>CameraFocusBeatTests</c>'s own
    /// "far enough to mean something" fixture-guard idiom) — a fresh spawn sits close to the forge
    /// by design, which would make "moved toward the anchor" true by accident.</summary>
    [TestCase]
    public async System.Threading.Tasks.Task Reask_MovesTheCameraTowardTheAnchor_OnlyOnThePress_NeverOnATick()
    {
        var ui = MountMainUi();
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var target = ui.Town.FindBuilding("forge").GlobalPosition; // BuyMaterial's aimed anchor, from outside

            ui.Town.Player.SpawnAt(ui.Town.FindBuilding("minegate").GlobalPosition);
            for (var i = 0; i < 3; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            var player = ui.Town.Player.GlobalPosition;
            AssertThat(player.DistanceTo(target))
                .OverrideFailureMessage("The teleport did not put the player far enough from the anchor for this test to mean anything.")
                .IsGreater(60f);

            var before = ui.Town.Cam.GlobalPosition;
            for (var i = 0; i < 5; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            AssertThat(ui.Town.Cam.GlobalPosition.DistanceTo(before))
                .OverrideFailureMessage("The camera moved with nothing pressed at all — a nudge is happening on its own.")
                .IsLess(5f);

            ui._UnhandledKeyInput(ReaskKeyPress);
            for (var i = 0; i < 3; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            var duringPeek = ui.Town.Cam.GlobalPosition.DistanceTo(target);
            var toPlayerDuring = ui.Town.Cam.GlobalPosition.DistanceTo(player);
            AssertThat(duringPeek < toPlayerDuring)
                .OverrideFailureMessage(
                    $"The re-ask press did not move the camera toward the anchor: {duringPeek:0.#}px from it vs " +
                    $"{toPlayerDuring:0.#}px from the player.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
