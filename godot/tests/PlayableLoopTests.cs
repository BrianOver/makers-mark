#if GDUNIT_TESTS
using System.Linq;
using System.Text;
using GameSim;
using GameSim.Advisor;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U8 (Playable Core R15): the headline regression suite that locks the playable loop
/// (R1–R7) and the gated clock behind engine tests, so neither playtest blocker — a
/// dead-end loop step or a clock that moves on its own — can ever ship green again.
/// Everything is driven through the REAL Controls (never Adapter.Queue directly), every
/// driven button must render Enabled at press time (<see cref="UiTestSupport.PressEnabled"/>),
/// and the whole sequence must produce ZERO kernel rejections and never render the raw
/// "REJECTED:" string (R6, loop-level complement to RejectionUxTests).
///
/// Batch-order note (verified against GameKernel.Tick): queued actions apply
/// SEQUENTIALLY in submission order, each successful handler's state feeding the next —
/// so buy+buy+craft in ONE Morning batch would succeed sim-side. The loop below still
/// splits them (buy → Advance → craft → Advance) because the U6 craft gate mirrors
/// CURRENTLY-HELD materials: Craft_dagger renders Disabled until the copper lands, and
/// this suite refuses to press a disabled button.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlayableLoopTests
{
    /// <summary>
    /// Fresh campaign through the U4 static handoff — the same
    /// <see cref="GameComposition.NewCampaign(ulong)"/> world the new-game flow seeds.
    /// </summary>
    private static MainUi MountFreshCampaign() =>
        MountMainUi(new SimAdapter(GameComposition.NewCampaign(ScriptedSession.Seed)));

    // ── 1. THE headline test: the full loop through real Controls ────────────────────────

    [TestCase]
    public void PlayableLoop_BuyCraftStockSell_ThroughControls_ZeroRejections()
    {
        var ui = MountFreshCampaign();
        try
        {
            var transcript = DriveLoop(ui);
            AssertThat(transcript.Length > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 2. Gated clock integration over the mounted shell ────────────────────────────────

    /// <summary>
    /// U1 (plan 2026-08-03-001, KTD-A) rewrite: <c>Clock.AutoAdvance</c> ("the Innkeeper's Clock")
    /// now only ever gates Morning and Evening — the two phases with a real bell. The raid span
    /// (Expedition/Camp/ExpeditionDeep) is <see cref="RaidConductor"/>'s alone, driven from
    /// <c>MainUi._Process</c> UNCONDITIONALLY (see <c>MainUi._Process</c>'s own remarks on why
    /// <c>Clock.Update</c> is gated to <c>Conductor.Current == Idle</c> to prevent a double-tick) —
    /// so "gated" no longer means "huge deltas are harmless" once the bell has left Morning. The old
    /// version of this test proved the opposite (AutoAdvance OFF held Camp inert too) and would
    /// silently pass for the wrong reason under the new architecture; this version pins what is
    /// actually true now.
    /// </summary>
    [TestCase]
    public void GatedClock_HoldsMorningAndEveningInert_ButTheRaidSpanAlwaysAdvancesViaProcess()
    {
        var ui = MountFreshCampaign();
        try
        {
            // Auto is OFF by default (U2/R1): arbitrarily large frame deltas through the
            // REAL _Process path leave Morning untouched.
            AssertThat(ui.Clock.AutoAdvance).IsFalse();
            for (var frame = 0; frame < 5; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            // The AdvancePhase button — the explicit player Advance — ticks EXACTLY once.
            PressEnabled(ui, "AdvancePhase");
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

            // 2026-08-09: the apprenticeship chain now HOLDS the raid span open at its Watch step
            // (RaidConductor's own hold doc — the fix for "i clicked send them off and it auto
            // jumped to night???? yet this is still on tutorial 5"), and a fresh mount is always
            // mid-chain. This test is about the Clock-vs-Conductor ROUTING, not about the tutorial,
            // so it dismisses the chain and asserts the routing on its own terms; the hold itself is
            // pinned by TutorialWatchStep_HoldsTheRaidSpanOpen_... below and by RaidConductorTests.
            ui.Tutorial.Dismiss();

            // The raid span is NEVER gated by the Innkeeper's Clock: RaidConductor.Update runs from
            // _Process independent of Clock.AutoAdvance (still OFF here, unchanged from the
            // assertion above), so huge deltas keep walking it forward on their own. A fresh Day-1
            // campaign is guaranteed unstaged — every hero's first-ever trip targets floor 1,
            // structurally below the staging checkpoint (ExpeditionSystem.CheckpointFor) — so nobody
            // parks and, with nothing owed, this reaches Evening with zero further player input.
            for (var frame = 0; frame < 8 && ui.Conductor.Current != RaidConductor.Beat.Idle; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("The raid span must advance via _Process regardless of Clock.AutoAdvance being off.")
                .IsEqual(DayPhase.Evening);
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.Idle);

            // Evening is a real bell phase again — gated (AutoAdvance still off throughout) is
            // inert here too, same as Morning was.
            for (var frame = 0; frame < 5; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The owner's 2026-08-09 report, end to end through the real mounted shell: "i clicked send
    /// them off and it auto jumped to night???? yet this is still on tutorial 5??? this is a
    /// critical bug as it skipped most the game and prevented me from playing more."
    ///
    /// <para>The mechanism, measured before the fix: the chain's Watch step is printed on the
    /// Expedition→Camp tick (its predecessor's completion fact is <c>PartyDeparted</c>, which fires
    /// on that tick), while the Watch control itself is only on the bell row during
    /// Expedition/Camp/ExpeditionDeep — and the two empty beats between those two facts are
    /// <see cref="RaidConductor.EmptyBeatSeconds"/> each. Two seconds to answer an instruction the
    /// game had only just given, after which the button was gone and the step could not be completed
    /// at all. This drives it with a delta per frame so large that EVERY pinned max in the conductor
    /// is crossed on every single frame — if any timer is still allowed to run, the day is at Night
    /// by frame two.</para>
    /// </summary>
    [TestCase]
    public void TutorialWatchStep_HoldsTheRaidSpanOpen_TheWatchButtonIsStillThereWhenThePlayerLooks()
    {
        var ui = MountFreshCampaign();
        try
        {
            PressEnabled(ui, "AdvancePhase"); // "Send them off" — the one press the owner made
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            for (var frame = 0; frame < 40; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Fixture premise failed: the party never departed, so the Watch step is not current.")
                .IsEqual(Ui.TutorialStep.LookIn);
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage(
                    "The day reached Night on its own while the tutorial was still asking the player to " +
                    "press Watch — the reported bug, exactly.")
                .IsNotEqual(DayPhase.Evening);
            AssertThat(Find<Button>(ui, "WatchButton").Visible)
                .OverrideFailureMessage("The step names a control that is no longer on screen.")
                .IsTrue();
            AssertThat(ui.Conductor.ShowHeld)
                .OverrideFailureMessage("The span stopped, but nothing on the HUD would say why.")
                .IsTrue();
            AssertThat(RenderedText(ui)).Contains("the day waits on you");

            // Answering it releases the hold through the REAL hook (the Watch button's own press).
            PressEnabled(ui, "WatchButton");
            AssertThat(ui.Tutorial.Step).IsEqual(Ui.TutorialStep.OpenCounter);
            ui.Mirror.CloseMirror(); // the open Mirror engages the clock latch — close it to disengage
            AssertThat(ui.Conductor.ShowHeld).IsFalse();

            for (var frame = 0; frame < 8 && ui.Conductor.Current != RaidConductor.Beat.Idle; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Once answered, the span must run on to Evening exactly as it always did.")
                .IsEqual(DayPhase.Evening);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Skipping stays legal (§11.7.8): the held day is never a trap — the same bell-row
    /// control, reading "Hurry the day along", walks straight through the hold in one press.</summary>
    [TestCase]
    public void HeldRaidSpan_IsNeverATrap_TheBellRowStillHurriesStraightThroughIt()
    {
        var ui = MountFreshCampaign();
        try
        {
            PressEnabled(ui, "AdvancePhase"); // Send them off
            for (var frame = 0; frame < 40; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Conductor.ShowHeld).IsTrue();

            PressEnabled(ui, "AdvancePhase"); // Hurry the day along — the player's own choice

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("The hold refused the player's own press — that is a softlock, not a stop.")
                .IsEqual(DayPhase.Evening);
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage(
                    "A step the player deliberately hurried past must move on, not keep naming a Watch " +
                    "control that Night does not have.")
                .IsEqual(Ui.TutorialStep.OpenCounter);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 3. Determinism: same seed, same driven loop, identical rendered outcome ──────────

    [TestCase]
    public void PlayableLoop_SameSeed_RendersIdenticalTranscript()
    {
        var first = CaptureLoopTranscript();
        var second = CaptureLoopTranscript();
        AssertThat(first.Length > 0).IsTrue();
        AssertThat(first).IsEqual(second);
    }

    private static string CaptureLoopTranscript()
    {
        var ui = MountFreshCampaign();
        try
        {
            return DriveLoop(ui);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The driven loop: buy 2x copper (Morning tick) → craft the dagger → stock it at the default
    /// price (both immediate, all-phases verbs — U1's two-bell day no longer offers a separate
    /// Camp/ExpeditionDeep bell to do them "in") → ride Hurry through the decision-free raid span
    /// (a fresh Day-1 campaign is always unstaged — every hero's first trip targets floor 1, which
    /// is structurally below the staging checkpoint, so nobody parks and one Hurry press reaches
    /// Evening directly) → the real Evening bell rolls the day → day-2 Morning tick, where
    /// HeroShoppingSystem (a MORNING system) has every alive hero browse the shelf — the sale
    /// opportunity. Returns a step-by-step transcript of all rendered text (the determinism test
    /// compares two runs byte-for-byte). Every step re-asserts the two loop-level invariants: zero
    /// kernel rejections so far, and the raw "REJECTED:" string absent from ALL rendered text (R6).
    /// </summary>
    private static string DriveLoop(MainUi ui)
    {
        var transcript = new StringBuilder();

        void Step(string name)
        {
            var rendered = RenderedText(ui);
            AssertThat(rendered.Contains("REJECTED:")).IsFalse();
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);
            transcript.AppendLine($"== {name} ==");
            transcript.Append(rendered);
        }

        AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
        AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
        Step("fresh campaign, day-1 Morning");

        // Day-1 Morning: two vendor buys (dagger = 2x copper; BuyMat buys 1 per press),
        // then the real Morning bell lands them on the Morning tick.
        PressEnabled(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");
        PressEnabled(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");
        PressEnabled(ui, "AdvancePhase"); // Send them off: Morning -> Expedition
        Step("Morning tick: vendor buys landed");
        AssertThat(ui.Adapter.LastEvents.OfType<MaterialPurchased>().Count()).IsEqual(2);
        // U21: RefreshAll is visibility-gated — open Forge for a fresh read/Disabled state.
        ui.OpenPanel("Forge");
        // U-T7-1 (register #149): a bare open lands on the craft section now. The two buys above go
        // through the craft section's own needs row (that is the point of it — the tutorial's day-1
        // instruction has to be answerable where the tutorial sends you), but the assertion below
        // wants the VENDOR row's owned column, whose "x2" is a running stock total; the needs row's
        // own column deliberately reads "have/need" instead. So press the tab a player would.
        PressEnabled(ui.Forge, "ForgeTab_materials");
        // UI-5: the copper count now reads off the vendor ListRow's "owned" column
        // (BuyMat_copper's row), not a standalone "MATERIALS: copper x2" prose line.
        var copperRow = Find<Godot.Button>(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");
        var copperOwned = copperRow.GetParent()?.FindChild("Owned", recursive: false, owned: false) as Godot.Label;
        AssertThat(copperOwned?.Text).IsEqual($"×{ScriptedSession.CopperNeeded}");
        AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        AssertThat(ui.Conductor.Current)
            .OverrideFailureMessage("The conductor should now own the span — the bell just left Morning for real.")
            .IsEqual(RaidConductor.Beat.SendOff);

        // The copper is HELD now, so the U6 craft gate is open — crafting is legal in all phases,
        // and there is no longer a separate Expedition/Camp bell to press "in between": both this
        // and the stock below are immediate, all-phases verbs pressed back to back while the raid
        // conductor holds the span.
        PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
        Step("craft landed");
        AssertThat(ui.Adapter.LastEvents.OfType<ItemCrafted>().Count()).IsEqual(1);
        var crafted = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
        ui.OpenPanel("Shop"); // U21: open Shop so the fresh unshelved craft actually renders
        AssertThat(RenderedText(ui.Shop)).Contains("Dagger");

        // U6 (auto pricing): the StockPrice_ SpinBox is pre-filled with SuggestedPrice.For — the
        // player never has to touch it — so the expected shelf price IS the suggestion, not a
        // hand-picked constant.
        var suggestedPrice = SuggestedPrice.For(ui.Adapter.CurrentState.Items[crafted.Value]);

        // Shelve the dagger from the Shop tab (StockAction is all-phases), with ZERO price
        // interaction — Stock is pressed as-is, no SpinBox edit.
        PressEnabled(ui.Shop, $"Stock_{crafted.Value}");
        Step("stock landed");
        var shelf = ui.Adapter.CurrentState.Player.Shelf;
        AssertThat(shelf.Count).IsEqual(1);
        AssertThat(shelf[0].Item).IsEqual(crafted);
        AssertThat(shelf[0].Price).IsEqual(suggestedPrice);
        var shopText = RenderedText(ui.Shop);
        AssertThat(shopText).Contains("Dagger");
        // P007 U3: price moved from an inline "— Ng" suffix into its own StatChip value label.
        AssertThat(shopText).Contains($"{suggestedPrice}g");
        // U6: the auto price must never look like a silent guess — its origin is on screen too.
        AssertThat(shopText).Contains("suggested");

        // Ride Hurry through the decision-free raid span. One press is enough: nobody parks on a
        // fresh day 1 (see method doc), so there is no vigil stop between here and Evening.
        PressEnabled(ui, "AdvancePhase"); // Hurry, not a bell — Expedition -> Camp -> ExpeditionDeep -> Evening
        Step("raid span: reached Evening");
        AssertThat(ui.Adapter.CurrentState.Phase)
            .OverrideFailureMessage("One Hurry press should reach Evening directly on an unstaged day.")
            .IsEqual(DayPhase.Evening);
        AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.Idle);

        // The real Evening bell rolls the day.
        PressEnabled(ui, "AdvancePhase");
        Step("Evening tick: day rolled");
        AssertThat(ui.Adapter.CurrentState.Day).IsEqual(2);
        AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

        // Day-2 Morning tick: heroes shop. Hero-visible is provable either way — every
        // alive hero either bought the dagger (ItemSold) or passed with a reason
        // (HeroPassedOnItem); a shelved item nobody judged would fail here.
        PressEnabled(ui, "AdvancePhase");
        Step("day-2 Morning tick: heroes shopped");
        var verdicts = ui.Adapter.LastEvents
            .Where(e => (e is ItemSold sold && sold.Item == crafted)
                     || (e is HeroPassedOnItem pass && pass.Item == crafted))
            .ToList();
        AssertThat(verdicts.Count > 0).IsTrue();

        var sale = ui.Adapter.LastEvents.OfType<ItemSold>().FirstOrDefault(s => s.Item == crafted);
        if (sale is not null)
        {
            // A sale from OUR shelf: the shelf slot cleared and the forge got paid.
            AssertThat(sale.FromPlayerShop).IsTrue();
            AssertThat(ui.Adapter.CurrentState.Player.Shelf.IsEmpty).IsTrue();
        }

        transcript.AppendLine(sale is null ? "outcome: every hero passed" : "outcome: sold");
        return transcript.ToString();
    }
}
#endif
