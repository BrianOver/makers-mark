#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U2 (playtest F1, "menus off-screen" — 2026-07-21 gate-b findings): the missing assertion the
/// postmortem called out. <c>MenuSizingTests</c> only checked the objective chip's WIDTH resolved
/// to <see cref="GodotClient.Ui.ObjectiveTracker.DockWidth"/> — never that the chip, or the HUD's
/// own Skip/Auto/Pause/Ledger controls, actually sit INSIDE the viewport. Two concrete defects this
/// hunts: (a) once the Gold/Heroes stat chips mount after the first tick the HUD header row's total
/// width can exceed the window, pushing the rightmost controls off-screen; (b) the objective chip
/// is a fixed-height overlay docked over the drawer/modal region, so it can visually sit on top of
/// the very panel buttons (or the Ledger) the tutorial points the player at.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HudBoundsTests
{
    [TestCase]
    public async Task AfterFirstTick_CoreHudControls_StayInsideViewport()
    {
        var ui = MountMainUi();
        try
        {
            // Mount is day 1 Morning (2 stat chips: Day/Phase). Advance once so Gold/Heroes
            // chips mount too (RefreshStatus rebuilds all 4 — see MainUi.RefreshStatus) — the
            // exact "once the stat chips mount" trigger the finding names.
            PressEnabled(ui, "AdvancePhase");
            await SettleLayout(ui);

            var viewport = ui.GetViewportRect().Size;
            AssertThat(viewport.X).IsGreater(0f);
            AssertThat(viewport.Y).IsGreater(0f);

            foreach (var name in new[] { "AdvancePhase", "AutoAdvance", "PlayPause", "Speed", "OpenLedger" })
            {
                var control = Find<Control>(ui, name);
                if (!control.Visible)
                {
                    continue; // PlayPause/Speed are Auto sub-controls (U2), hidden while gated
                }

                var rect = control.GetGlobalRect();
                AssertThat(rect.Position.X)
                    .OverrideFailureMessage($"{name} left edge {rect.Position.X} < 0 (off-screen)")
                    .IsGreaterEqual(0f);
                AssertThat(rect.Position.Y)
                    .OverrideFailureMessage($"{name} top edge {rect.Position.Y} < 0 (off-screen)")
                    .IsGreaterEqual(0f);
                AssertThat(rect.End.X)
                    .OverrideFailureMessage($"{name} right edge {rect.End.X} > viewport width {viewport.X}")
                    .IsLessEqual(viewport.X);
                AssertThat(rect.End.Y)
                    .OverrideFailureMessage($"{name} bottom edge {rect.End.Y} > viewport height {viewport.Y}")
                    .IsLessEqual(viewport.Y);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task DrawerOpen_ObjectiveChip_NeverCoversDrawerButtons()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge"); // Morning phase — the vendor's "Buy 1" rows are live
            await SettleLayout(ui);

            var buyCopper = Find<Control>(ui.Forge, "BuyMat_copper");
            AssertThat(buyCopper.Visible).IsTrue();
            var buyRect = buyCopper.GetGlobalRect();

            AssertThat(!ui.Objective.Visible || !ui.Objective.GetGlobalRect().Intersects(buyRect))
                .OverrideFailureMessage(
                    "Objective chip overlaps the Forge drawer's Buy-copper button — "
                    + $"chip={ui.Objective.GetGlobalRect()} button={buyRect}")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task LedgerOpen_ObjectiveChip_NeverCoversLedger()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);                                     // day 1 -> Evening arms the Return Ritual gate
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // gate elapses -> Ledger opens
            AssertThat(ui.Ledger.Visible).IsTrue();
            await SettleLayout(ui);

            AssertThat(!ui.Objective.Visible
                    || !ui.Objective.GetGlobalRect().Intersects(ui.Ledger.GetGlobalRect()))
                .OverrideFailureMessage("Objective chip overlaps the open Evening Ledger modal")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            // A fresh Morning-1 mount has exactly one objective step — the un-expanded chip must be
            // sized to its own content, NOT padded to the old fixed 260px dock. Assert against the
            // content min-size (robust to objective-text length — e.g. U4's location-aware step copy
            // is longer than the original) rather than a magic pixel threshold; and prove it shrank
            // below the retired fixed dock.
            var chip = ui.Objective;
            AssertThat(chip.Size.Y)
                .OverrideFailureMessage($"objective chip height {chip.Size.Y} still reserves the fixed 260px empty dock")
                .IsLess(260f);
            AssertThat(chip.Size.Y)
                .OverrideFailureMessage($"objective chip height {chip.Size.Y} exceeds its content min-size {chip.GetCombinedMinimumSize().Y} — not content-tracked")
                .IsLessEqual(chip.GetCombinedMinimumSize().Y + 2f);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
