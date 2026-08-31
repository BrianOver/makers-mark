#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-05: "a disabled input node leaves no stale field that reads as live." Measured in a
/// rendered frame: the vigil Camp modal opened with "E · Forge" still floating over it. The census
/// was not primarily a z-order bug — <see cref="Town2d.WorldInput2D._PhysicsProcess"/> early-
/// returned on <c>!Enabled</c> ABOVE its own <c>SetTarget</c> call, and <c>PromptText</c> is
/// written only inside <c>SetTarget</c>, so the instant <c>MainUi.SetWorldInputEnabled(false)</c>
/// disabled the node, <c>PromptText</c> froze at whatever it last said. Both of its readers — the
/// HUD chip (<see cref="MainUi.UpdateInteractPrompt"/>) and the playtest bridge
/// (<c>AgentPlaytest.BuildDigest</c>'s <c>InteractPrompt</c> column) — took the frozen string as
/// live.
///
/// <para>This suite asserts on the FIELD (<see cref="Town2d.WorldInput2D.PromptText"/>), not the
/// chip: the field has two readers, and a test that only checked chip visibility would leave the
/// playtest-bridge reader still lying. <see cref="InteractPromptTests"/> already covers the chip
/// mirroring the field correctly once it changes; this suite is what makes the field itself change
/// on the disable edge in the first place. <see cref="OpeningTheCampModal_HidesTheChip_AndClosingRestoresItAtTheSameStation"/>
/// closes the loop through the exact modal the census photographed.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WorldInput2DDisabledPromptTests
{
    [TestCase]
    public async Task DisablingWorldInput_ClearsPromptTextField_WithinOnePhysicsFrame()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4); // let the forge-at-spawn overlap resolve, same as InteractPromptTests

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage(
                    "Setup check: nothing active at spawn — this test would prove nothing about " +
                    "clearing a live prompt.")
                .IsNotNull();
            AssertThat(ui.Town.WorldInputNode.PromptText)
                .OverrideFailureMessage("Setup check: PromptText is empty before disabling — nothing to clear.")
                .IsNotEmpty();

            ui.Town.WorldInputNode.Enabled = false;
            await SettlePhysics(ui); // one (well, a few) physics tick — the disable edge this unit fixes

            AssertThat(ui.Town.WorldInputNode.PromptText)
                .OverrideFailureMessage(
                    "WorldInput2D.PromptText is still non-empty after Enabled=false — a disabled " +
                    "input node is leaving a stale field that reads as live (P2-SCREEN-05 regressed).")
                .IsEmpty();
            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("ActiveTarget is still set after Enabled=false.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task ReenablingWorldInput_RestoresPromptText_AtTheSameStation()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4);

            var expected = ui.Town.WorldInputNode.PromptText;
            AssertThat(expected)
                .OverrideFailureMessage("Setup check: PromptText is empty before disabling — nothing to restore.")
                .IsNotEmpty();

            ui.Town.WorldInputNode.Enabled = false;
            await SettlePhysics(ui);
            AssertThat(ui.Town.WorldInputNode.PromptText)
                .OverrideFailureMessage("Setup check: disabling never cleared the field — see the other test in this suite.")
                .IsEmpty();

            ui.Town.WorldInputNode.Enabled = true;
            await SettlePhysics(ui); // re-enable at the same station: no player movement happened

            AssertThat(ui.Town.WorldInputNode.PromptText)
                .OverrideFailureMessage(
                    $"PromptText did not come back to '{expected}' after re-enabling at the same station.")
                .IsEqual(expected);
            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("ActiveTarget did not come back after re-enabling at the same station.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The exact scene the census photographed, driven for real: <see cref="MainUi.Camp"/>'s own
    /// <c>ShowModal</c>/<c>CloseModal</c> (the same calls the real vigil-camp flow and
    /// <c>tools/shot_harness.gd</c>'s <c>SHOT_STATE=Camp</c> branch both use) synchronously fire
    /// <c>MainUi.OnCampVisibilityChanged</c> -&gt; <c>UpdateEngaged</c> -&gt;
    /// <c>Town.SetWorldInputEnabled</c>, so this proves the field fix all the way through the chip
    /// a player actually sees, not just the isolated node.
    /// </summary>
    [TestCase]
    public async Task OpeningTheCampModal_HidesTheChip_AndClosingRestoresItAtTheSameStation()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui); // let MainUi._Process mirror PromptText into the chip

            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("Setup check: the chip is not visible before opening the Camp modal — nothing to hide.")
                .IsTrue();
            var expected = ui.InteractPromptLabel.Text;

            ui.Camp.ShowModal();
            await PumpWorldFrames(ui, 2);
            await SettleLayout(ui);

            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The 'E · Forge' chip is still visible over the open Camp modal — this is the exact census frame.")
                .IsFalse();
            AssertThat(ui.Town.WorldInputNode.PromptText)
                .OverrideFailureMessage("WorldInput2D.PromptText is still non-empty with the Camp modal open.")
                .IsEmpty();

            ui.Camp.CloseModal();
            await PumpWorldFrames(ui, 2);
            await SettleLayout(ui);

            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The chip did not come back after closing the Camp modal at the same station.")
                .IsTrue();
            AssertThat(ui.InteractPromptLabel.Text)
                .OverrideFailureMessage($"Chip text '{ui.InteractPromptLabel.Text}' does not match the pre-modal '{expected}' at the same station.")
                .IsEqual(expected);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
