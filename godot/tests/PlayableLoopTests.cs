#if GDUNIT_TESTS
using System.Linq;
using System.Text;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
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
    /// <summary>Default shelf price: AddSpinBox's initial value on the Stock row (ShopPanel).</summary>
    private const int DefaultStockPrice = 10;

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

    [TestCase]
    public void GatedClock_ProcessInert_AdvanceTicksOnce_AutoToggleDrives()
    {
        var ui = MountFreshCampaign();
        try
        {
            // Auto is OFF by default (U2/R1): arbitrarily large frame deltas through the
            // REAL _Process path leave the sim untouched.
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

            // Opt into auto mode through its real Controls. MountMainUi paused the clock,
            // so PlayPause resumes it — both presses drive the same buttons a player sees.
            PressEnabled(ui, "AutoAdvance");
            AssertThat(ui.Clock.AutoAdvance).IsTrue();
            PressEnabled(ui, "PlayPause");
            AssertThat(ui.Clock.Playing).IsTrue();

            // One frame ≥ the phase duration → _Process drives exactly ONE tick (Update
            // caps one advance per call — a huge delta can never skip phases). Bounded,
            // no wall-clock sleeping anywhere.
            ui._Process(PhaseClock.MorningSeconds);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);

            // Toggle back to gated: accrued time and huge deltas are harmless again.
            PressEnabled(ui, "AutoAdvance");
            AssertThat(ui.Clock.AutoAdvance).IsFalse();
            for (var frame = 0; frame < 5; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
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
    /// The driven loop: buy 2x copper (Morning tick) → craft the dagger (Expedition tick)
    /// → stock it at the default price (Camp tick) → ride the gated Advance to the day-2
    /// Morning tick, where HeroShoppingSystem (a MORNING system) has every alive hero
    /// browse the shelf — the sale opportunity. Returns a step-by-step transcript of all
    /// rendered text (the determinism test compares two runs byte-for-byte). Every step
    /// re-asserts the two loop-level invariants: zero kernel rejections so far, and the
    /// raw "REJECTED:" string absent from ALL rendered text (R6).
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
        // then the gated Advance lands them on the Morning tick.
        PressEnabled(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");
        PressEnabled(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");
        PressEnabled(ui, "AdvancePhase");
        Step("Morning tick: vendor buys landed");
        AssertThat(ui.Adapter.LastEvents.OfType<MaterialPurchased>().Count()).IsEqual(2);
        // U21: RefreshAll is visibility-gated — open Forge for a fresh read/Disabled state.
        ui.OpenPanel("Forge");
        AssertThat(RenderedText(ui.Forge))
            .Contains($"{ScriptedSession.CraftMaterial} x{ScriptedSession.CopperNeeded}");
        AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

        // Day-1 Expedition: the copper is HELD now, so the U6 craft gate is open
        // (crafting is legal in all phases — the forge never closes).
        PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
        PressEnabled(ui, "AdvancePhase");
        Step("Expedition tick: craft landed");
        AssertThat(ui.Adapter.LastEvents.OfType<ItemCrafted>().Count()).IsEqual(1);
        var crafted = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
        ui.OpenPanel("Shop"); // U21: open Shop so the fresh unshelved craft actually renders
        AssertThat(RenderedText(ui.Shop)).Contains("Dagger");

        // Day-1 Camp: shelve the dagger from the Shop tab (StockAction is all-phases).
        PressEnabled(ui.Shop, $"Stock_{crafted.Value}");
        PressEnabled(ui, "AdvancePhase");
        Step("Camp tick: stock landed");
        var shelf = ui.Adapter.CurrentState.Player.Shelf;
        AssertThat(shelf.Count).IsEqual(1);
        AssertThat(shelf[0].Item).IsEqual(crafted);
        AssertThat(shelf[0].Price).IsEqual(DefaultStockPrice);
        var shopText = RenderedText(ui.Shop);
        AssertThat(shopText).Contains("Dagger");
        // P007 U3: price moved from an inline "— Ng" suffix into its own StatChip value label.
        AssertThat(shopText).Contains($"{DefaultStockPrice}g");

        // Ride the gated clock to day-2 Morning: ExpeditionDeep, then Evening (day rolls).
        PressEnabled(ui, "AdvancePhase");
        Step("ExpeditionDeep tick");
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
