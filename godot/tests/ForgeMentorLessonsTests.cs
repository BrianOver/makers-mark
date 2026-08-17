#if GDUNIT_TESTS
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave B (§11.14.4, Act I): Bryn's own first-touch teaching, wired into <c>ForgePanel</c> —
/// "the forge's two acts, taught inside the forge," "material sets the ceiling and your hands set
/// the band," and "the mark, read." Every scenario drives the REAL <see cref="ForgePanel"/> through
/// a real <see cref="Ui.MainUi"/> mount, the same house idiom <c>ForgeTwoActTests</c> uses (real
/// button presses via <see cref="PressEnabled"/>, real Act 1 completion via
/// <see cref="ForgeTwoActTests.DriveAct1ToCompletion"/>) — never a stand-in for the panel this
/// feature actually lives in.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeMentorLessonsTests
{
    /// <summary>Buys 3x the recipe's material need so a campaign can craft the same recipe twice in
    /// this suite (once to observe the material-ceiling lesson, again to observe Act 1's own lesson
    /// once the ceiling one has already fired) without a second shopping trip mid-test.</summary>
    private static void BuyPlentyOfMaterialAndOpenForge(GodotClient.MainUi ui)
    {
        ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded * 3));
        ui.Adapter.AdvancePhase();
        ui.OpenPanel("Forge");
    }

    /// <summary>
    /// The player's first-EVER reach into a craft overlay teaches "material sets the ceiling and
    /// your hands set the band" (link, §11.14.4) — not Act 1's own lesson, which is exactly what
    /// <see cref="ShowMentorFirstTouch"/>'s one-lesson-per-action priority order says should happen
    /// (the foundational lesson goes first). The spotlight anchor reuses Wave A's own
    /// <see cref="TutorialAnchorKind.PanelControl"/> mechanism, and <c>MainUi</c>'s own
    /// <c>MentorSpotlightChanged</c> wiring must reach the tutorial overlay WITHOUT any phase tick or
    /// panel reopen happening in between (opening a craft overlay queues nothing).
    /// </summary>
    [TestCase]
    public async Task WorkingTheForgeForTheFirstTimeEver_TeachesMaterialCeiling_AndSpotlightsMaterialSelect()
    {
        var ui = MountMainUi();
        try
        {
            BuyPlentyOfMaterialAndOpenForge(ui);
            await SettleLayout(ui);

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");

            var banner = Find<PanelContainer>(ui.Forge, "ForgeMentorBanner");
            var label = Find<Label>(ui.Forge, "ForgeMentorText");
            AssertThat(banner.Visible)
                .OverrideFailureMessage("The material-ceiling lesson never showed on the very first craft overlay this campaign ever opened.")
                .IsTrue();
            AssertThat(label.Text).Contains(MentorVoice.Name); // attributed to Bryn, never an anonymous tooltip
            AssertThat(label.Text).Contains("ceiling");

            // Law: "show only what the sim decided" — this lesson describes the MECHANISM
            // qualitatively; it must never smuggle in a client-invented number for the sim's own
            // grade math.
            AssertThat(label.Text.Any(char.IsDigit))
                .OverrideFailureMessage($"The material-ceiling lesson contains a digit — a client-authored quantity has leaked into copy that must only describe the mechanism: \"{label.Text}\"")
                .IsFalse();

            AssertThat(ui.Forge.MentorSpotlight)
                .OverrideFailureMessage("The material-ceiling lesson did not spotlight the material dropdown it is teaching about.")
                .IsEqual(TutorialAnchor.ForPanelControl("Forge", "MaterialSelect"));

            // The wiring test: MentorSpotlightChanged must have already reached MainUi's own
            // RefreshObjectiveLine -> Overlay.RefreshAnchor with ZERO phase tick or panel reopen in
            // between (opening Act 1 queues no CraftAction) -- only Overlay.Tick's own real-frame
            // settle is needed to read the result, same discipline PanelControlAnchorTests uses.
            await SettleLayout(ui);
            ui.Overlay.Tick(0.016);
            AssertThat(Find<ColorRect>(ui, "TutorialOverlayTop").Visible)
                .OverrideFailureMessage("MentorSpotlightChanged fired but the tutorial overlay never actually pulsed the spotlighted control.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Dismiss is the player's OWN press, never a timer (law: no timers on decisions) — and it must
    /// hand the spotlight back rather than leave it stuck on a control the player already moved past.
    /// </summary>
    [TestCase]
    public void DismissingTheBanner_ClearsTheSpotlight()
    {
        var ui = MountMainUi();
        try
        {
            BuyPlentyOfMaterialAndOpenForge(ui);
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            AssertThat(ui.Forge.MentorSpotlight).IsNotNull();

            PressEnabled(ui.Forge, "ForgeMentorDismiss");

            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("Pressing \"Got it\" did not hide the mentor banner.")
                .IsFalse();
            AssertThat(ui.Forge.MentorSpotlight)
                .OverrideFailureMessage("Dismissing the banner left the tutorial overlay pointed at a control the player already moved past.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Once the material-ceiling lesson has already had its turn (a prior craft-open action), the
    /// NEXT craft-open action reaches Act 1's own lesson — "the forge's two acts, taught inside the
    /// forge" — proving both the priority order AND that Wave A's once-ever first-touch engine, not
    /// a second mechanism, is what gates each lesson independently.
    /// </summary>
    [TestCase]
    public void ASecondForgeOpen_TeachesAct1sOwnLesson_NotMaterialCeilingAgain()
    {
        var ui = MountMainUi();
        try
        {
            BuyPlentyOfMaterialAndOpenForge(ui);
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}"); // consumes material-ceiling
            PressEnabled(ui.Forge, "ForgeMentorDismiss");
            PressEnabled(ui.Forge, "ForgeMinigameCancel"); // back to a pressable Forge panel, nothing queued

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}"); // second-ever open

            var label = Find<Label>(ui.Forge, "ForgeMentorText");
            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible).IsTrue();
            AssertThat(label.Text)
                .OverrideFailureMessage($"The second forge-open should teach Act 1's own lesson (material-ceiling already fired once) but showed: \"{label.Text}\"")
                .Contains("hammer");
            AssertThat(ui.Forge.MentorSpotlight)
                .OverrideFailureMessage("Act 1's own lesson spotlights nothing — it is taught with the player's hands already on the forge, not by pointing elsewhere.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Law: no timers on decisions. Unlike <c>ForgePanel</c>'s own result ceremony (a real,
    /// owner-approved auto-dismissing toast on a fixed <c>CeremonySeconds</c>), the mentor banner
    /// carries NO countdown of its own — it must still be up after many real, ticked engine frames
    /// with nobody pressing anything.
    /// </summary>
    [TestCase]
    public async Task MentorBanner_NeverAutoDismisses_RegardlessOfHowManyFramesPass()
    {
        var ui = MountMainUi();
        var player = new HumanPlayer(ui);
        try
        {
            BuyPlentyOfMaterialAndOpenForge(ui);
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible).IsTrue();

            await player.Frames(90); // real, ticked _Process frames -- comfortably past any plausible toast timer

            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("The mentor banner disappeared on its own after real frames passed with no player input -- this is a timer on a decision, which is against the law.")
                .IsTrue();
        }
        finally
        {
            player.ReleaseAll();
            Unmount(ui);
        }
    }

    /// <summary>
    /// The banner is a toast, never a gate (law: influence never orders, skip stays legal) -- the
    /// player must be free to keep working the SAME craft overlay while the banner is still up
    /// un-dismissed, exactly as <see cref="ForgePanel.ShowMentorFirstTouch"/>'s own remarks describe.
    /// </summary>
    [TestCase]
    public void MentorBanner_NeverBlocksTheCraftOverlayBehindIt()
    {
        var ui = MountMainUi();
        try
        {
            BuyPlentyOfMaterialAndOpenForge(ui);
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible).IsTrue();

            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            AssertThat(act1.Visible)
                .OverrideFailureMessage("Act 1's own overlay is not visible while the mentor banner is up -- the banner is acting as a gate, not a toast.")
                .IsTrue();

            act1.Advance(0.5);
            act1.ForgeStrike(); // real progress WHILE the banner is still up and un-dismissed

            AssertThat(act1.StrikesLanded)
                .OverrideFailureMessage("A strike thrown while the mentor banner is up did not register -- the banner is blocking play underneath it.")
                .IsGreater(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Link 1 of the five-link spine, "the mark, read": the FIRST time the player ever finishes a
    /// craft, the mentor banner shows the sim's own <see cref="GameSim.Contracts.MakersMark"/> —
    /// never an invented crafter name or day. Reads <see cref="GameSim.Contracts.ItemCrafted"/> off
    /// the adapter's OWN event log the same way <c>ForgePanel</c> itself does, so this test's
    /// expectation and the panel's own source of truth can never silently drift apart.
    /// </summary>
    [TestCase]
    public void FinishingAForgeCraft_ShowsTheMarkReadLesson_WithTheSimsOwnCrafterNameAndDay()
    {
        var ui = MountMainUi();
        try
        {
            BuyPlentyOfMaterialAndOpenForge(ui);
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}"); // consumes material-ceiling
            PressEnabled(ui.Forge, "ForgeMentorDismiss"); // free the banner slot for the mark-read lesson

            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            ForgeTwoActTests.DriveAct1ToCompletion(act1, pumpUntilPermille: 900, strikeAbovePermille: 500);
            var quench = Find<QuenchMinigame>(ui.Forge, "QuenchMinigame");
            quench.Plunge(); // -> OnQuenchFinished -> ShowCeremony -> ShowMarkReadLesson, synchronously

            var crafted = ui.Adapter.CurrentState.EventLog.OfType<ItemCrafted>().Last();
            var mark = ui.Adapter.CurrentState.Items[crafted.Item.Value].Mark;
            AssertThat(mark)
                .OverrideFailureMessage("Test setup: the just-completed craft carries no MakersMark at all.")
                .IsNotNull();

            var label = Find<Label>(ui.Forge, "ForgeMentorText");
            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("The mark-read lesson never showed after the player's first-ever completed craft.")
                .IsTrue();
            AssertThat(label.Text)
                .OverrideFailureMessage($"The mark-read banner does not name the sim's own crafter (\"{mark!.CrafterName}\"): \"{label.Text}\"")
                .Contains(mark!.CrafterName);
            AssertThat(label.Text)
                .OverrideFailureMessage($"The mark-read banner does not name the sim's own craft day ({mark.CraftedOnDay}): \"{label.Text}\"")
                .Contains(mark.CraftedOnDay.ToString());
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
