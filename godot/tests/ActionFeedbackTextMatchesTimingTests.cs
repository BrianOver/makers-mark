#if GDUNIT_TESTS
using System;
using System.Reflection;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Pressing a button whose action resolves IMMEDIATELY must say so happened — never a future
/// promise about a change that has already landed.
///
/// <para><b>Why this exists.</b> Brian's playtest, 2026-08-02: "Open counter does nothing -
/// tutorial stuck at 6", "opening the counter queues", "you have a TON of past 'queued' actions
/// which don't interact with our game well lol". The kernel side was already fixed —
/// <see cref="ActionTiming.ResolvesImmediately"/> returns true for <see cref="OpenCounterAction"/>
/// and 20 other verbs, and <c>SimAdapter.Queue</c> really does apply them on the spot. What was
/// NOT fixed was the SENTENCE: <see cref="CounterPanel"/> and <see cref="ForgePanel"/> hardcoded
/// the old deferred wording at every call site regardless of what actually happened. The counter
/// really opened; the label still told the player to press Advance and wait for it — so they
/// pressed Advance believing nothing had happened yet, and burned the phase.</para>
///
/// <para>The fix (<see cref="SimPanel.Confirm"/>) derives the sentence from
/// <see cref="ActionTiming.ResolvesImmediately"/> instead of a hand-written string per button.
/// This suite pins BOTH directions so a lazy "just delete the word Queued everywhere" patch
/// cannot pass: an immediate press must drop the future-tense promise entirely, and a genuinely
/// deferred action must KEEP it — the three remaining bell-riders (forge upgrade, profession
/// change, Guild commission) really do still cost a beat, and that promise is correct for them.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ActionFeedbackTextMatchesTimingTests
{
    /// <summary>P2-HONEST-04 (family C — "a guard must be phrased against the property, never the
    /// instance"): this suite used to hardcode <c>"resolves when"</c> and <c>"Press Advance"</c>,
    /// the literal words the deferred branch happened to use in 2026-08. Both were rewritten when
    /// the branch stopped naming a control that does not exist ("Advance" is the advance button's
    /// NODE name; its label reads "Skip" or a bell verb) and stopped printing the raw
    /// <c>DayPhase</c> enum. A literal guard would have gone green on the rewrite while asserting
    /// a sentence the game no longer says.
    ///
    /// <para>So the forbidden fragment is now READ FROM <see cref="SimPanel"/> at runtime: whatever
    /// <c>Confirm</c> appends for a genuinely deferred action IS the promise clause, by definition.
    /// The immediate-branch tests assert the immediate sentence does not carry it. That is the real
    /// rule, and no copy rewrite can make it tautological
    /// (<see cref="UiTestSupport.DeferredPromiseClauseOf"/>).</para>
    ///
    /// <para>This constant is the other half: the advance control's NODE name, which no player ever
    /// sees — its rendered label is "Skip" (clock engaged) or <c>PhaseVocab.BellVerb</c>. Copy
    /// naming this string would be telling the player to press something that is not on screen,
    /// which is exactly what P2-HONEST-04 found it doing.</para></summary>
    private const string AdvanceNodeNameNeverShown = "Advance";

    /// <summary>
    /// The reported bug, pinned directly on the exact button the owner complained about.
    /// Fresh <see cref="MountMainUi()"/> starts at day-1 Morning with no live counter session, so
    /// "Open Counter" is live with zero setup.
    /// </summary>
    [TestCase]
    public void CounterPanel_OpenCounter_ConfirmsImmediately_NeverPromisesTheBell()
    {
        var ui = MountMainUi();
        try
        {
            // MainUi owns a Town2D with a live SubViewport; awaiting frames while one renders is
            // the documented gdUnit headless hang. This test never awaits a frame, but every test
            // that mounts MainUi disables it up front regardless, per house convention.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            ui.OpenPanel("Shop"); // CounterPanel is nested inside ShopPanel (PA7)
            PressEnabled(ui.Shop, "OpenCounter");

            var counterPanel = Find<CounterPanel>(ui.Shop, "CounterPanel");
            var feedback = Find<Label>(ui.Shop, "CounterFeedback").Text;
            AssertThat(feedback)
                .OverrideFailureMessage(
                    $"CounterFeedback read '{feedback}' after Open Counter. OpenCounterAction " +
                    "resolves immediately (ActionTiming) — the counter really did open — so this " +
                    "must say so happened, not promise a future resolution. This is the exact " +
                    "owner-reported bug: \"opening the counter queues\", tutorial stuck waiting on " +
                    "an event that already fired.")
                .Contains("Opened the counter");
            AssertThat(feedback).NotContains(DeferredPromiseClauseOf(counterPanel));
            AssertThat(feedback).NotContains(AdvanceNodeNameNeverShown);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Same contract, the other panel, a different immediate verb (talent unlock rather than the
    /// vendor buy MainUiTests already covers) — proves the fix is the shared helper, not a
    /// one-button patch. "keen-eye" has no prerequisites, so it is unlockable from a fresh save.
    /// </summary>
    [TestCase]
    public void ForgePanel_UnlockTalent_ConfirmsImmediately_NeverPromisesTheBell()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            PressEnabled(ui.Forge, "Unlock_keen-eye");

            var feedback = Find<Label>(ui.Forge, "ForgeFeedback").Text;
            AssertThat(feedback)
                .OverrideFailureMessage(
                    $"ForgeFeedback read '{feedback}' after Unlock. UnlockTalentAction resolves " +
                    "immediately (ActionTiming) — the talent is already unlocked — so this must " +
                    "say so happened, not promise a future resolution.")
                .Contains("Unlocked keen-eye");
            AssertThat(feedback).NotContains(DeferredPromiseClauseOf(ui.Forge));
            AssertThat(feedback).NotContains(AdvanceNodeNameNeverShown);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The other half of the contract: a genuinely deferred action must KEEP the reviewed bell
    /// wording. Neither panel currently exposes a button for one of the three remaining
    /// bell-riders (forge upgrade / profession change / Guild commission are reached through other
    /// UI, out of this unit's scope) — so this drives the shared <see cref="SimPanel.Confirm"/>
    /// seam directly, via reflection, on the SAME live panel instances the buttons above just
    /// proved work. That is deliberate: it is the one path that can fail a "delete the word Queued
    /// everywhere" patch, which the two tests above cannot (they never exercise the deferred
    /// branch at all).
    /// </summary>
    [TestCase]
    public void Confirm_GenuinelyDeferredAction_StillPromisesTheBell_OnBothPanels()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            ui.OpenPanel("Shop");

            PlayerAction deferred = new UpgradeForgeAction();
            AssertThat(ActionTiming.ResolvesImmediately(deferred))
                .OverrideFailureMessage(
                    "This test's premise broke: UpgradeForgeAction is no longer a deferred " +
                    "bell-rider per ActionTiming. Pick another still-deferred action so this test " +
                    "keeps proving what it claims.")
                .IsFalse();

            var counter = Find<CounterPanel>(ui.Shop, "CounterPanel");

            // The promise must SURVIVE for a real bell-rider — a patch that strips the future
            // tense for every action (not just the immediate ones) would pass the immediate tests
            // above and must fail here. Phrased against the property rather than the 2026-08 words:
            // the deferred sentence must differ from the immediate one, and must name the moment it
            // resolves in the player's own phase vocabulary.
            var phaseWord = PhaseVocab.Display(ui.Adapter.CurrentState);

            var forgeText = InvokeConfirm(ui.Forge, deferred, "Upgraded the forge");
            AssertThat(forgeText)
                .OverrideFailureMessage(
                    $"ForgePanel.Confirm for a DEFERRED action returned '{forgeText}' — it must not "
                    + "read the same as an immediate confirmation; the world really does have to act "
                    + "first, and the sentence has to say so.")
                .IsNotEqual(InvokeConfirm(ui.Forge, new OpenCounterAction(), "Upgraded the forge"));
            AssertThat(forgeText)
                .OverrideFailureMessage(
                    $"ForgePanel.Confirm for a DEFERRED action returned '{forgeText}' — it must name "
                    + $"the moment it resolves, in the HUD's own word for it ('{phaseWord}'), never the "
                    + "raw DayPhase enum (P2-HONEST-04; PhaseVocab is the one table).")
                .Contains(phaseWord);
            AssertThat(forgeText).NotContains(AdvanceNodeNameNeverShown);

            var counterText = InvokeConfirm(counter, deferred, "Upgraded the forge");
            AssertThat(counterText).Contains(phaseWord);
            AssertThat(counterText).NotContains(AdvanceNodeNameNeverShown);
            AssertThat(counterText)
                .OverrideFailureMessage("Both panels must route through the SAME SimPanel.Confirm.")
                .IsEqual(forgeText);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Reflection seam for <see cref="SimPanel"/>'s protected <c>Confirm</c> — the same
    /// idiom <c>BellTrayTests</c>/<c>ActionTimingConformanceTests</c> already use in this codebase
    /// for asserting behaviour that has no public surface of its own.</summary>
    private static string InvokeConfirm(SimPanel panel, PlayerAction action, string whatHappened)
    {
        var method = typeof(SimPanel).GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "SimPanel.Confirm not found by reflection — was it renamed? Update this test alongside it.");
        return (string)method.Invoke(panel, new object[] { action, whatHappened })!;
    }
}
#endif
