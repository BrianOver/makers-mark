#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U7 (2026-08-04-001 "verify by playing" plan): the two-act forge chain — <see cref="ForgeMinigame"/>
/// (Act 1, bellows+anvil worked together) handing off via <see cref="ForgeMinigame.ShapingDone"/> to
/// <see cref="QuenchMinigame"/> (Act 2, the single timed plunge). This is the owner's third repeated
/// complaint about forge pacing ("Dude the forge mini game is identical - still takes too long") and
/// his own description of the fix ("anvil + bellows work together then you squelch the item") — this
/// suite is the receipt that both landed together, not just the length knob a third time.
///
/// <para>Every scenario drives the REAL <see cref="ForgeMinigame"/>/<see cref="QuenchMinigame"/>
/// instances through their public <c>Advance(double)</c>/input seams — no wall-clock, no engine RNG.
/// House style follows <c>ForgeMinigameTests</c>/<c>BuyUpdatesTheCountImmediatelyTests</c>:
/// <c>MountMainUi</c> disables rendering by default (no viewport line needed).</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ForgeTwoActTests
{
    private const int TestDay = 0;
    private static readonly Recipe DaggerRecipe = ProfessionRegistry.AllRecipes[ScriptedSession.CraftRecipeId];

    /// <summary>
    /// A skilled run finishes fast; a beginner run finishes slower but still finishes.
    ///
    /// <para>"Skilled" here is NOT <c>ForgePlayer</c>'s one-swing-per-beat cadence (that throttle is
    /// <c>ForgeWinnabilityTests</c>' own choice to look human-plausible for a DIFFERENT invariant —
    /// tempo-accuracy scoring — and keeping it here would conflate "how fast can this be played" with
    /// "does the tempo bonus pay", two separate questions). A practiced player mashes the instant heat
    /// allows rather than idling between beats; this is what a real skilled player's hands actually do.
    /// Measured via a standalone harness against the REAL <c>ForgePath</c>/<c>ForgeScorer</c> before
    /// this pin landed: skilled Act 1 ~8.6s (19 strikes) + a decisive Act 2 plunge ~1.2s = ~9.7s
    /// combined; beginner Act 1 ~15.5s (38 strikes) + Act 2's ~4.0s auto-timeout = ~19.5s combined.</para>
    /// </summary>
    [TestCase]
    public void SkilledRun_FinishesBothActsUnderTenSeconds_BeginnerRun_TakesLongerButFinishes()
    {
        var skilled = PlayBothActs(demonstratedAccuracyPermille: 1000, pumpUntilPermille: 900, strikeAbovePermille: 500, decisivePlunge: true);
        var beginner = PlayBothActs(demonstratedAccuracyPermille: 0, pumpUntilPermille: 700, strikeAbovePermille: 320, decisivePlunge: false);

        AssertThat(skilled.Act1Completed).IsTrue();
        AssertThat(skilled.Act2Completed).IsTrue();
        AssertThat(beginner.Act1Completed).IsTrue();
        AssertThat(beginner.Act2Completed).IsTrue();

        AssertThat(skilled.CombinedSeconds)
            .OverrideFailureMessage(
                $"A skilled scripted run took {skilled.CombinedSeconds:0.0}s combined (Act 1 " +
                $"{skilled.Act1Seconds:0.0}s/{skilled.Act1Strikes}st + Act 2 {skilled.Act2Seconds:0.0}s), " +
                "over the plan's ~10s bar. This is the owner's third repeated complaint about forge " +
                "pacing — a fourth 'still too long' is not acceptable.")
            .IsLess(10.5);

        AssertThat(beginner.CombinedSeconds)
            .OverrideFailureMessage(
                $"A beginner ({beginner.CombinedSeconds:0.0}s combined) did not take longer than a " +
                $"skilled player ({skilled.CombinedSeconds:0.0}s) — the skill curve (R6, required " +
                "strikes falling with demonstrated accuracy) is not reaching wall-clock time.")
            .IsGreater(skilled.CombinedSeconds);
    }

    /// <summary>
    /// The BEGINNER run also has a ceiling. The owner's complaint was 19.2s; the two-act split cut a
    /// skilled run to ~9.7s but left a beginner at ~19.5s — within a hair of the number he called too
    /// long, and a beginner is exactly who complains. <c>ForgeMinigame.AssistPerOverrunStrike</c> is
    /// the lever: once a run has spent its strike budget, each further blow pays more.
    ///
    /// <para>The bar here is deliberately looser than the skilled bar. A beginner SHOULD be slower —
    /// the test above pins that ordering, and this one must not fight it. What this pins is that
    /// "slower" cannot mean "back where we started": a badly-heated billet has to close out in
    /// clearly less than the 19.2s that drew the complaint, with headroom against the ordering pin.</para>
    ///
    /// <para>Change this number only against a measured run, never by reasoning about the constants —
    /// the last attempt to shorten a run by intuition (cutting the tempo period) made the skilled
    /// case 20% WORSE via beat/mash aliasing, and only the measurement caught it.</para>
    ///
    /// <para>Measured with the assist in place: beginner Act 1 11.6s/28 strikes (was 15.5s/38) plus
    /// Act 2's unchanged 4.0s auto-timeout = 15.6s combined, against 19.5s before. Act 2's fixed
    /// window is now the single largest slice of a beginner run — shortening it would cut more
    /// wall-clock than any further assist, but it would also shrink the plunge window for everyone,
    /// so it is a difficulty decision for the owner rather than a pacing tweak.</para>
    /// </summary>
    [TestCase]
    public void BeginnerRun_StaysUnderTheComplaintThreshold()
    {
        var beginner = PlayBothActs(demonstratedAccuracyPermille: 0, pumpUntilPermille: 700, strikeAbovePermille: 320, decisivePlunge: false);

        AssertThat(beginner.Act1Completed).IsTrue();
        AssertThat(beginner.Act2Completed).IsTrue();

        AssertThat(beginner.CombinedSeconds)
            .OverrideFailureMessage(
                $"A beginner run took {beginner.CombinedSeconds:0.0}s combined (Act 1 " +
                $"{beginner.Act1Seconds:0.0}s/{beginner.Act1Strikes}st + Act 2 " +
                $"{beginner.Act2Seconds:0.0}s). The owner called 19.2s too long. A beginner is who " +
                "complains, so the overrun assist must keep a poorly-heated billet well under that.")
            .IsLess(17.0);
    }

    /// <summary>
    /// The assist must not be a stealth difficulty cut for someone who is already good. A skilled run
    /// finishes at or within a strike of its own (reduced) budget, so its assist multiplier stays at
    /// or near 1.0 — the beginner is the only one it meaningfully pays.
    /// </summary>
    [TestCase]
    public void Assist_BarelyTouchesASkilledRun()
    {
        var act1 = new ForgeMinigame();
        act1.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith,
            ImmutableSortedSet<string>.Empty, TestDay, demonstratedAccuracyPermille: 1000);

        AssertThat(act1.AssistEngaged)
            .OverrideFailureMessage("The assist was already engaged before a single strike landed.")
            .IsFalse();
        AssertThat(act1.AssistMultiplier).IsEqual(1.0);

        DriveAct1ToCompletion(act1, pumpUntilPermille: 900, strikeAbovePermille: 500);

        AssertThat(act1.Completed).IsTrue();
        AssertThat(act1.AssistMultiplier)
            .OverrideFailureMessage(
                $"A skilled run finished with assist {act1.AssistMultiplier:0.00}x after " +
                $"{act1.StrikesLanded} strikes against a budget of {act1.RequiredStrikes}. The assist " +
                "is meant for a struggling player; paying a skilled one this much makes the skill " +
                "curve meaningless.")
            .IsLessEqual(1.0 + ForgeMinigame.AssistPerOverrunStrike);

        act1.QueueFree();
    }

    /// <summary>R6, "high metals are more precise": <see cref="QuenchMinigame"/>'s acceptable-plunge
    /// band narrows as recipe tier rises — a pure function, so this needs no overlay at all.</summary>
    [TestCase]
    public void QuenchBand_NarrowsForAHigherTierMetal()
    {
        var tier1 = QuenchMinigame.BandHalfWidthPermilleForTier(1);
        var tier3 = QuenchMinigame.BandHalfWidthPermilleForTier(3);

        AssertThat(tier3)
            .OverrideFailureMessage(
                $"Tier 3's acceptable-plunge band ({tier3}‰) is not narrower than tier 1's ({tier1}‰) — " +
                "\"high metals are more precise\" is not implemented.")
            .IsLess(tier1);
    }

    /// <summary>R6, "you get faster as you get better": <see cref="ForgeMinigame.RequiredStrikes"/>
    /// falls as the caller's demonstrated accuracy rises — a later craft needs fewer strikes than a
    /// first one, without needing to drive a whole run to observe it.</summary>
    [TestCase]
    public void DemonstratedAccuracy_LowersTheRequiredStrikeCount_OnALaterCraft()
    {
        var mg = new ForgeMinigame();
        try
        {
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay);
            var firstCraftRequired = mg.RequiredStrikes; // no accuracy argument — a first craft
            AssertThat(firstCraftRequired).IsEqual(ForgeMinigame.BaseRequiredStrikes);

            // The SAME recipe, a later craft this session, with a proven track record fed in —
            // exactly how ForgePanel carries _demonstratedAccuracyPermille across crafts.
            mg.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay,
                demonstratedAccuracyPermille: 1000);
            var laterCraftRequired = mg.RequiredStrikes;
            AssertThat(laterCraftRequired).IsEqual(ForgeMinigame.MinRequiredStrikes);

            AssertThat(laterCraftRequired)
                .OverrideFailureMessage(
                    $"A later craft with a proven track record still needs {laterCraftRequired} strikes, " +
                    $"same as a first craft's {firstCraftRequired} — demonstrated accuracy is not lowering " +
                    "the required strike count.")
                .IsLess(firstCraftRequired);
        }
        finally
        {
            mg.Free();
        }
    }

    /// <summary>Act 2 is pre-built (like every other craft overlay) but stays HIDDEN until Act 1's own
    /// <see cref="ForgeMinigame.ShapingDone"/> fires — there is no button, key, or state that opens it
    /// early.</summary>
    [TestCase]
    public void Act1_CannotBeSkippedIntoAct2()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");

            var quench = Find<QuenchMinigame>(ui.Forge, "QuenchMinigame");
            AssertThat(quench.Visible)
                .OverrideFailureMessage("Act 2 is visible the instant Act 1 opens — there is a skip path.")
                .IsFalse();

            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            act1.Advance(0.5);
            act1.ForgeStrike(); // one strike — nowhere near Act 1's own finish line

            AssertThat(act1.Completed).IsFalse();
            AssertThat(quench.Visible)
                .OverrideFailureMessage("Act 2 became visible before Act 1 reached its finish line — a skip path exists.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Cancelling Act 1 leaves no partial item and no spent material — Act 1 never builds a
    /// <see cref="CraftAction"/> at all (only Act 2 does, on its own Plunge), so there is nothing for a
    /// cancel to un-do.</summary>
    [TestCase]
    public void CancellingAct1_LeavesNoPartialItemAndNoSpentMaterial()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            var materialBefore = ui.Adapter.CurrentState.Player.Materials.GetValueOrDefault(ScriptedSession.CraftMaterial);

            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            act1.Advance(0.5);
            act1.ForgeStrike(); // real progress, so the cancel is not just abandoning an untouched run

            PressEnabled(ui.Forge, "ForgeMinigameCancel");

            AssertThat(act1.WasCancelled).IsTrue();
            AssertThat(ui.Adapter.AppliedThisPhase.OfType<CraftAction>().Count())
                .OverrideFailureMessage("Cancelling Act 1 queued a CraftAction — a partial item was produced.")
                .IsEqual(0);
            AssertThat(ui.Adapter.CurrentState.Player.Materials.GetValueOrDefault(ScriptedSession.CraftMaterial))
                .OverrideFailureMessage("Cancelling Act 1 spent material that was never turned into anything.")
                .IsEqual(materialBefore);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// PT1 was a dead-keyboard bug from a missing <c>GrabFocus</c> equivalent — the overlay claimed
    /// focus-ABLE but never actually FOCUSED. The two-act split adds a SECOND overlay swap (Act 1
    /// hides, Act 2 shows) where the same mistake could silently reappear. This proves the keyboard
    /// genuinely reaches Act 2 the instant it opens: Space plunges immediately with no prior click.
    /// </summary>
    [TestCase]
    public async Task KeyboardFocus_LandsOnAct2_WhenAct1Ends()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");

            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            DriveAct1ToCompletion(act1, pumpUntilPermille: 900, strikeAbovePermille: 500);
            AssertThat(act1.Completed).IsTrue();

            var quench = Find<QuenchMinigame>(ui.Forge, "QuenchMinigame");
            AssertThat(quench.Visible).IsTrue();

            var player = new HumanPlayer(ui);
            await player.Frames(2); // ClaimKeyboard's focus grab is deferred — see UiKit.ClaimKeyboard

            AssertThat(quench.HasFocus())
                .OverrideFailureMessage("Act 2 opened but never actually took focus (focus-ABLE is not focused).")
                .IsTrue();

            player.Tap(Key.Space);
            await player.Frames(1);

            AssertThat(quench.Completed)
                .OverrideFailureMessage(
                    "Space did not reach Act 2 the instant it opened — the exact PT1 shape (an overlay " +
                    "that LOOKS open but the keyboard still points somewhere else, e.g. the 'Work the " +
                    "forge' button behind it).")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"Forge another like it" (loop-structure plan KTD-C): once a recipe+material has a
    /// proven trace, a repeat craft re-queues it at one click and skips BOTH acts entirely.</summary>
    [TestCase]
    public void RepeatCraft_SkipsTheMeterEntirely()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded * 3));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            var buttonName = $"ForgeAnother_{ScriptedSession.CraftRecipeId}";
            AssertThat(ui.Forge.FindChild(buttonName, recursive: true, owned: false))
                .OverrideFailureMessage("The repeat-craft button exists before any minigame craft has ever completed.")
                .IsNull();

            PressEnabled(ui.Forge, $"WorkForge_{ScriptedSession.CraftRecipeId}");
            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            DriveAct1ToCompletion(act1, pumpUntilPermille: 900, strikeAbovePermille: 500);
            var quench = Find<QuenchMinigame>(ui.Forge, "QuenchMinigame");
            quench.Plunge();

            var craftsAfterFirst = ui.Adapter.AppliedThisPhase.OfType<CraftAction>().Count();
            AssertThat(craftsAfterFirst).IsEqual(1);

            var repeatButton = Find<Button>(ui.Forge, buttonName);
            AssertThat(repeatButton.Disabled)
                .OverrideFailureMessage("The repeat-craft button is disabled even though the material was bought 3x over.")
                .IsFalse();

            repeatButton.EmitSignal(BaseButton.SignalName.Pressed);

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<CraftAction>().Count())
                .OverrideFailureMessage("Pressing 'Forge another like it' did not queue a second CraftAction.")
                .IsEqual(craftsAfterFirst + 1);
            AssertThat(act1.Visible)
                .OverrideFailureMessage("Repeat-craft opened Act 1 — it must skip the meter entirely, not replay it.")
                .IsFalse();
            AssertThat(quench.Visible)
                .OverrideFailureMessage("Repeat-craft opened Act 2 — it must skip the meter entirely, not replay it.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// PR #382 CI receipt (<c>HumanPlaytestTests.EveryVisibleButton_ActuallyRespondsToARealClick</c>):
    /// finishing a craft opens <c>ForgePanel</c>'s G1 result ceremony over a real 2-second WALL-CLOCK
    /// timer (<c>ShowCeremony</c>/<c>CeremonySeconds</c>, decremented by real <c>_Process</c> delta) —
    /// but the overlay's hit-test area is the panel's whole <c>FullRect</c>, not just the small centered
    /// card it actually draws. Any click elsewhere in the Forge panel that lands before that real time
    /// elapses (or before the player hits Skip) is silently swallowed, even though nothing is visibly
    /// covering it. CI hit this reliably (its per-frame wall-clock time is smaller, so the same handful
    /// of pumped test frames covers far less real time than locally — see
    /// docs/frame-count-is-not-a-duration in project memory); this test reproduces it deterministically
    /// on ANY machine, no wall-clock race needed, because zero real seconds can possibly have elapsed
    /// between the synchronous <see cref="QuenchMinigame.Plunge"/> call and the very next click.
    ///
    /// <para>Re-clicks "Work the forge" itself (bought 3x the needed copper, so material remains after
    /// one craft) rather than a Talent "Unlock" button: completing a craft ALSO makes the recipe row
    /// grow a new "Forge another like it" button (<see cref="ForgePanel.OnQuenchFinished"/> records the
    /// trace before <c>Refresh()</c> runs). At the time this test was written that was a SECOND,
    /// independent layout defect this test deliberately did not chase — the row's growth widened the
    /// whole scroll body by exactly the new button's width (measured then: 592px to 746px), shifting
    /// every later-laid-out control (a Talent "Unlock" button among them) sideways by the same amount.
    /// "Work the forge" sits earlier in its own row and stayed on screen either way, which is why this
    /// test isolated the ceremony behavior from that other bug rather than depending on a fix for it.
    /// That defect is now fixed (repo task #100: <c>controlsRow</c> wraps instead of growing, see
    /// <c>ForgePanel</c>'s recipe-row comment) and covered by its own dedicated regression test,
    /// <c>HumanPlaytestTests.NoPanel_DemandsMoreWidthThanTheDrawerGivesIt_AfterACompletedCraft</c> — this
    /// test's own re-click of "Work the forge" (rather than "Unlock") stays unchanged since that isolation
    /// was never the bug, just a deliberate scope boundary.</para>
    /// </summary>
    [TestCase]
    public async Task ForgeCeremony_DoesNotSwallowAClickOutsideItsOwnCard()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded * 3));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            var player = new HumanPlayer(ui);
            await player.WaitForLayout(ui.Drawer.CurrentContent!); // let the drawer's open-slide settle first

            var workForgeName = $"WorkForge_{ScriptedSession.CraftRecipeId}";
            var workForgeBefore = Find<Button>(ui.Forge, workForgeName);
            AssertThat(workForgeBefore.Disabled)
                .OverrideFailureMessage("WorkForge is disabled even after buying 3x the needed material — the test's setup is wrong, not the game.")
                .IsFalse();

            // The Vendor section (every priced material, one row each) sits above Recipes, so
            // "Work the forge" starts below the fold on a fresh campaign — scroll it into view by
            // computed offset rather than a wheel-notch count (which depends on how tall the page
            // happens to be, not what this test is about).
            var scroller = ScrollContainerAncestorOf(workForgeBefore);
            var scrollMargin = 40f;
            var scrollTarget = Mathf.Max(0f,
                workForgeBefore.GetGlobalRect().Position.Y - scroller.GetGlobalRect().Position.Y - scrollMargin);
            scroller.ScrollVertical = (int)scrollTarget;
            await player.WaitForLayout(workForgeBefore);

            await player.ClickControl(workForgeBefore, "Work the forge (before the craft)"); // sanity: reachable before the ceremony ever exists

            var act1 = Find<ForgeMinigame>(ui.Forge, "ForgeMinigame");
            DriveAct1ToCompletion(act1, pumpUntilPermille: 900, strikeAbovePermille: 500);
            var quench = Find<QuenchMinigame>(ui.Forge, "QuenchMinigame");
            quench.Plunge(); // -> OnQuenchFinished -> ShowCeremony, synchronously, zero frames pumped since

            var ceremony = Find<Control>(ui.Forge, "ForgeCeremonyOverlay");
            AssertThat(ceremony.Visible)
                .OverrideFailureMessage("The ceremony never showed — this test is not exercising the overlay it means to.")
                .IsTrue();

            // Refresh() rebuilt the recipe row (same recipe id, a NEW Button instance, still enabled —
            // 2x the needed copper remains) — re-resolve by name and settle the queue_sort() the rebuild
            // just queued (a few frames: real-world milliseconds, nowhere near the 2 real seconds
            // CeremonySeconds needs).
            var workForgeAfter = Find<Button>(ui.Forge, workForgeName);
            AssertThat(workForgeAfter.Disabled)
                .OverrideFailureMessage("WorkForge became disabled after one craft even though 2x the needed material remains.")
                .IsFalse();
            await player.WaitForLayout(workForgeAfter);
            await player.ClickControl(workForgeAfter, "Work the forge (right after the craft)");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── C2 (input substrate plan): plunge is an InputMap action, not a raw key ──────────────────

    [TestCase]
    public void PlungeAction_FollowsARebind_TheOldPhysicalKeyStopsWorking()
    {
        WithTemporaryBinding("plunge", new InputEventKey { PhysicalKeycode = Key.F }, () =>
        {
            var quench = new QuenchMinigame();
            try
            {
                var handoff = new ForgeMinigame.ShapingResult(
                    ImmutableList<int>.Empty, ImmutableList<int>.Empty, PathSeed: 0, HeatYPermille: 500, StrikesLanded: 0);
                quench.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, handoff);

                // The keys this overlay used to hard-match (Space/Enter/KpEnter) must now be a no-op...
                quench._GuiInput(new InputEventKey { PhysicalKeycode = Key.Space, Pressed = true, Echo = false });
                AssertThat(quench.Completed).IsFalse();

                // ...and the NEWLY bound physical key fires the exact same Plunge behaviour.
                quench._GuiInput(new InputEventKey { PhysicalKeycode = Key.F, Pressed = true, Echo = false });
                AssertThat(quench.Completed).IsTrue();
            }
            finally
            {
                quench.Free();
            }
        });
    }

    [TestCase]
    public void PlungeButtonLabel_ReadsTheLiveInputMapBinding_NotAFrozenLiteral()
    {
        WithTemporaryBinding("plunge", new InputEventKey { PhysicalKeycode = Key.F }, () =>
        {
            var quench = new QuenchMinigame();
            try
            {
                var handoff = new ForgeMinigame.ShapingResult(
                    ImmutableList<int>.Empty, ImmutableList<int>.Empty, PathSeed: 0, HeatYPermille: 500, StrikesLanded: 0);
                quench.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, handoff);

                // A prompt that hardcodes "(Space)" would lie the instant a rebind screen moves this
                // key — this is the exact defect C2 exists to close before any rebind UI ships.
                var plungeButton = Find<Button>(quench, "QuenchPlunge");
                AssertThat(plungeButton.Text).IsEqual("Plunge! (F)");
            }
            finally
            {
                quench.Free();
            }
        });
    }

    // ── U6 (buttons-learn-phases wave): the phantom auto-plunge ─────────────────────────────────

    /// <summary>
    /// U6 (campaign finding): before <see cref="QuenchMinigame.Configure"/> ever runs, this node is
    /// still ticking — <c>ForgePanel.EnsureBuilt</c> pre-builds Act 2 hidden at its own
    /// <c>_Ready</c>, and <c>_Process</c> calls <see cref="QuenchMinigame.Advance"/> unconditionally
    /// from tree entry. With <see cref="QuenchMinigame.RecipeId"/>/<see
    /// cref="QuenchMinigame.MaterialKey"/> both defaulting to empty, the fixed <see
    /// cref="QuenchMinigame.QuenchDurationSeconds"/> (4.0s) timeout fired anyway and auto-plunged a
    /// phantom <c>CraftAction("", "")</c> through <see cref="QuenchMinigame.Finished"/> — rejected
    /// <c>Unknown recipe ''.</c> in 34/34 campaign runs, every one, before the player had ever opened
    /// the forge. This pins the fix (the <c>_configured</c> gate): an unconfigured instance advanced
    /// well past the timeout raises nothing and submits nothing.
    /// </summary>
    [TestCase]
    public void UnconfiguredQuench_AdvancedPastTheTimeout_FiresNoFinishedEvent_SubmitsNothing()
    {
        var quench = new QuenchMinigame();
        try
        {
            CraftAction? emitted = null;
            var finishedFired = false;
            quench.Finished += action => { finishedFired = true; emitted = action; };

            // 10 simulated seconds, well past QuenchDurationSeconds (4.0s) — the exact phantom-
            // plunge window the campaign hit before the player ever opened the forge.
            const double stepSeconds = 0.5;
            for (var i = 0; i < 20; i++)
            {
                quench.Advance(stepSeconds);
            }

            AssertThat(finishedFired)
                .OverrideFailureMessage("An unconfigured QuenchMinigame fired Finished — the phantom-plunge bug is back.")
                .IsFalse();
            AssertThat(emitted).IsNull();
            AssertThat(quench.Completed).IsFalse();
            AssertThat(quench.EmittedAction).IsNull();
        }
        finally
        {
            quench.Free();
        }
    }

    /// <summary>Nearest <see cref="ScrollContainer"/> ancestor, or throws.</summary>
    private static ScrollContainer ScrollContainerAncestorOf(Control control)
    {
        for (var parent = control.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer scroller)
            {
                return scroller;
            }
        }

        throw new InvalidOperationException($"{control.Name} has no ScrollContainer ancestor.");
    }

    // ── Scripted-run drivers — pure Advance(delta)/input-seam calls, no wall-clock, no RNG ────

    /// <summary>One full craft, both acts, off-panel (no <c>MainUi</c> needed) — for the timing scenario,
    /// which needs precise control over the simulated clock and does not touch the sim's action queue at
    /// all. Rapid-fire: strikes the instant heat allows rather than waiting for a tempo beat, which is
    /// what a practiced player's hands actually do (see the test's own remarks on why this differs from
    /// <c>ForgePlayer</c>'s one-swing-per-beat cadence).</summary>
    private static PlayResult PlayBothActs(
        int demonstratedAccuracyPermille, int pumpUntilPermille, int strikeAbovePermille, bool decisivePlunge)
    {
        const double stepSeconds = 0.02;
        const double patienceSeconds = 60.0;

        var act1 = new ForgeMinigame();
        QuenchMinigame? act2 = null;
        try
        {
            ForgeMinigame.ShapingResult? handoff = null;
            act1.ShapingDone += r => handoff = r;
            act1.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, TestDay,
                demonstratedAccuracyPermille);

            var act1Seconds = 0.0;
            var pumping = false;
            while (!act1.Completed && act1Seconds < patienceSeconds)
            {
                if (pumping)
                {
                    if (act1.HeatYPermille >= pumpUntilPermille)
                    {
                        act1.BellowsStop();
                        pumping = false;
                    }
                }
                else if (act1.HeatYPermille < strikeAbovePermille)
                {
                    act1.BellowsStart();
                    pumping = true;
                }
                else
                {
                    act1.ForgeStrike();
                }

                if (act1.Completed)
                {
                    break;
                }

                act1.Advance(stepSeconds);
                act1Seconds += stepSeconds;
            }

            act2 = new QuenchMinigame();
            act2.Configure(DaggerRecipe, ScriptedSession.CraftMaterial, ProfessionRegistry.Blacksmith, ImmutableSortedSet<string>.Empty, handoff!.Value);

            var act2Seconds = 0.0;
            while (!act2.Completed && act2Seconds < patienceSeconds)
            {
                act2.Advance(stepSeconds);
                act2Seconds += stepSeconds;
                if (decisivePlunge && Math.Abs(act2.HeatYPermille - act2.TargetTroughPermille) <= act2.BandHalfWidthPermille)
                {
                    act2.Plunge();
                }
            }

            return new PlayResult(act1.Completed, act1Seconds, act1.StrikesLanded, act2.Completed, act2Seconds);
        }
        finally
        {
            act1.Free();
            act2?.Free();
        }
    }

    /// <summary>Drives a REAL, panel-mounted <see cref="ForgeMinigame"/> (Act 1) to completion —
    /// same rapid-fire shape as <see cref="PlayBothActs"/>, for the scenarios that need the real
    /// <c>MainUi</c>/<c>ForgePanel</c> wiring (the handoff to Act 2) rather than a standalone pair.
    /// Internal (not private): <see cref="HumanPlaytestTests"/> reuses this exact driver rather than
    /// hand-rolling a second copy of the same rapid-fire loop for its own post-craft width guard.</summary>
    internal static void DriveAct1ToCompletion(ForgeMinigame act1, int pumpUntilPermille, int strikeAbovePermille)
    {
        const double stepSeconds = 0.02;
        var guardSeconds = 0.0;
        var pumping = false;
        while (!act1.Completed && guardSeconds < 60.0)
        {
            if (pumping)
            {
                if (act1.HeatYPermille >= pumpUntilPermille)
                {
                    act1.BellowsStop();
                    pumping = false;
                }
            }
            else if (act1.HeatYPermille < strikeAbovePermille)
            {
                act1.BellowsStart();
                pumping = true;
            }
            else
            {
                act1.ForgeStrike();
            }

            if (act1.Completed)
            {
                break;
            }

            act1.Advance(stepSeconds);
            guardSeconds += stepSeconds;
        }

        if (!act1.Completed)
        {
            throw new InvalidOperationException("Act 1 never reached its finish line within the patience budget.");
        }
    }

    private readonly record struct PlayResult(
        bool Act1Completed, double Act1Seconds, int Act1Strikes, bool Act2Completed, double Act2Seconds)
    {
        public double CombinedSeconds => Act1Seconds + Act2Seconds;
    }
}
#endif
