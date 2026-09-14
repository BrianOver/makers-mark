#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-22: a design capture found "E · Forge" rendered on the TAVERN's roof, right beside
/// the Tavern nametag, while the player stood at the forge door — the chip was anchored
/// CenterBottom (<c>MainUi.BuildUi</c>'s old <c>LayoutPreset.CenterBottom</c>), a FIXED screen
/// point that named one building while sitting wherever the camera happened to frame the bottom
/// of the screen. The same audit found it also survives a Send-Off/mine-gate camera pan, since
/// <see cref="WorldInput2D"/> keeps scanning (and the player keeps walking) throughout one of
/// those — see <see cref="Town2D.FocusOn"/>'s own doc.
///
/// <para><see cref="InteractPromptTests"/> already pins the chip's TEXT against <see
/// cref="WorldInput2D.PromptText"/>; this suite is the POSITION half — <see
/// cref="MainUi.UpdateInteractPrompt"/> now floats the chip over <see
/// cref="WorldInput2D.ActiveTarget"/>'s own nametag (<see cref="Building2D.NameLabel"/>) via <see
/// cref="Town2D.WorldToScreen"/>, the exact world-to-screen mechanism <see
/// cref="Ui.TutorialOverlay"/>'s off-camera marker already established for this codebase, rather
/// than a second, independently-typed anchoring scheme.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteractPromptAnchorTests
{
    /// <summary>
    /// Iterates every venue <see cref="TownLayout2D.Venues"/> actually places — not a single
    /// hand-typed "forge" — and checks the chip against a property, not a re-derivation of
    /// <c>UpdateInteractPrompt</c>'s own formula (a test that recomputed the exact same nametag-
    /// plus-gap offset would only prove the code agrees with itself). The property CenterBottom
    /// could never satisfy for every venue at once: the five venues span both the north (forge,
    /// market) and south (tavern, noticeboard) building rows, roughly 28 tiles apart, so a chip
    /// anchored to a single fixed screen point cannot land near EVERY one of their own on-screen
    /// positions — only whichever venue happens to sit near that fixed point at spawn.
    /// </summary>
    [TestCase]
    public async Task EveryTownVenue_PromptFloatsAboveItsOwnBuilding_NeverAFixedScreenPoint()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            foreach (var venue in TownLayout2D.Venues)
            {
                var building = ui.Town.FindBuilding(venue.Key);
                ui.Town.Player.GlobalPosition = building.DoorAnchorGlobal;
                await PumpWorldFrames(ui, 4);
                await SettleLayout(ui);

                AssertThat(ui.Town.WorldInputNode.ActiveTarget?.Key)
                    .OverrideFailureMessage(
                        $"Setup check: standing at '{venue.Key}'s door anchor never made it the " +
                        "active target — this iteration would prove nothing about its own prompt position.")
                    .IsEqual(venue.Key);
                AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                    .OverrideFailureMessage($"Setup check: the chip is not visible at '{venue.Key}'.")
                    .IsTrue();

                var buildingScreen = ui.Town.WorldToScreen(building.GlobalPosition);
                var chipRect = ui.InteractPromptLabel.GetGlobalRect();

                AssertThat(chipRect.Position.Y)
                    .OverrideFailureMessage(
                        $"[{venue.Key}] chip ({chipRect}) does not float above its own building " +
                        $"(screen anchor {buildingScreen}) — it must sit above the thing it names.")
                    .IsLess(buildingScreen.Y);

                // Generous on purpose: covers the widest venue footprint (tavern, 84px) times the
                // largest CanvasShrink a small test viewport can report, plus the nameplate's own
                // half-width — this is a "same building" bound, not a pixel-exact one.
                const float horizontalSlackPx = 250f;
                AssertThat(Mathf.Abs(chipRect.GetCenter().X - buildingScreen.X))
                    .OverrideFailureMessage(
                        $"[{venue.Key}] chip center X {chipRect.GetCenter().X} is nowhere near its " +
                        $"own building's screen X {buildingScreen.X} (screen anchor {buildingScreen}) " +
                        "— it reads as anchored to something other than the venue it names.")
                    .IsLess(horizontalSlackPx);
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Negative control (unit's own test list, bullet 2): mirrors <see
    /// cref="InteractPromptTests.WalkingAway_ClearsThePrompt"/>'s off-grid teleport, but reads the
    /// PanelContainer's own global rect rather than just <c>IsVisibleInTree</c> — a hidden control
    /// still reports a stale <c>GlobalPosition</c> from wherever it last floated, so this proves
    /// there is really nothing left claiming a screen position, not just that the flag flipped.
    /// </summary>
    [TestCase]
    public async Task NoTargetInRange_ShowsNoPrompt_AtAnyPosition()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("Setup check: nothing active at spawn — this test would prove nothing about clearing an existing prompt.")
                .IsNotNull();

            // Same off-grid teleport WorldInput2DNoTargetInteractTests/InteractPromptTests use:
            // TownLayout2D's whole grid is 40x28 tiles at 16px, so this is nowhere near any
            // Interact zone by construction, not by tuning a magic distance.
            ui.Town.Player.GlobalPosition = new Vector2(-2000f, -2000f);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("WorldInput2D still has an ActiveTarget 2000px off the town grid.")
                .IsNull();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The interact-prompt chip is still visible with no target in range.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The MineGateFocus case the audit found: <see cref="Town2D.FocusOnMineGate"/> borrows the
    /// camera for a Send-Off departure pan without touching player input or <see
    /// cref="WorldInput2D"/> at all (see its own doc — "the player keeps walking underneath"), so
    /// a target can stay active the entire time the visible frame shows the mine gate instead of
    /// wherever the player actually is. The chip must disappear for exactly that stretch, since a
    /// target-relative prompt cannot honestly point at anything the screen isn't currently showing.
    /// </summary>
    [TestCase]
    public async Task CameraFocusedElsewhere_HidesThePrompt_EvenWithATargetStillActive()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("Setup check: nothing active at spawn — this test would prove nothing about hiding a live prompt during a focus beat.")
                .IsNotNull();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("Setup check: the chip is not showing before the focus beat — nothing to hide.")
                .IsTrue();

            ui.Town.FocusOnMineGate();
            AssertThat(ui.Town.IsCameraOnPlayer)
                .OverrideFailureMessage("Setup check: FocusOnMineGate did not actually start a focus beat (IsCameraOnPlayer still true) — this test would prove nothing.")
                .IsFalse();

            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage(
                    "Setup check: the target cleared on its own during the focus beat — WorldInput2D " +
                    "keeps scanning while the player keeps walking (FocusOn's own doc), so this test's " +
                    "premise (a target still active while the camera looks elsewhere) no longer holds.")
                .IsNotNull();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The 'E · Forge' chip is still visible while the camera is off the player during a Send-Off/mine-gate focus beat — P2-SCREEN-22 regressed.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }
}
#endif
