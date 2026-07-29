#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U6 (plan <c>2026-07-28-002</c>): the bounty board's post form rebuilt around a mine
/// cross-section (<see cref="MineCrossSection"/>) for the floor pick, a <see cref="CoinStack"/>
/// for the reward, and a drag-to-board poster (<see cref="PosterComposer"/>) alongside the
/// existing button/Enter path. Every scenario still drives the SAME action queue
/// (<see cref="PostBountyAction"/>) the pre-U6 two-SpinBox form used — only the controls behind
/// the pinned <c>Name</c>s (<c>BountyFloor</c>/<c>BountyReward</c>/<c>PostBounty</c>) changed type,
/// from <c>SpinBox</c> to the new widgets above.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BountyPanelTests
{
    private const int PostFloor = 4;
    private const int PostReward = 30;

    [TestCase]
    public void OpenBounty_RendersCard_WithFloorRewardAndPostForm()
    {
        var ui = MountMainUi(new SimAdapter(WorldWithOpenBounty()));
        try
        {
            var bountyText = RenderedText(ui.Bounties);
            AssertThat(bountyText).Contains("OPEN BOUNTIES");
            AssertThat(bountyText).Contains($"clear floor {PostFloor} for {PostReward}g");

            // The post form's controls survive the U6 redesign under their pinned Names — now the
            // new widget types, not SpinBoxes.
            Find<MineCrossSection>(ui.Bounties, "BountyFloor");
            Find<CoinStack>(ui.Bounties, "BountyReward");
            Find<PosterComposer>(ui.Bounties, "BountyPoster");
            Find<Button>(ui.Bounties, "PostBounty");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void FreshCampaign_NoBountiesPosted_RendersThemedEmptyState_NotBlankPanel()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Bounties.IsEmpty).IsTrue();

            var bountyText = RenderedText(ui.Bounties);
            AssertThat(bountyText).Contains("OPEN BOUNTIES");
            AssertThat(bountyText).Contains("none posted");

            AssertThat(ui.Bounties.FindChildren("*", "PanelContainer", recursive: true, owned: false).Count > 0)
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SelectingFloorAndReward_PressingPostButton_QueuesPostBountyAction_InPendingActions()
    {
        var ui = MountMainUi();
        try
        {
            Find<MineCrossSection>(ui.Bounties, "BountyFloor").SelectFloor(PostFloor);
            Find<CoinStack>(ui.Bounties, "BountyReward").SetValue(PostReward);
            PressEnabled(ui.Bounties, "PostBounty");

            var pending = ui.Adapter.PendingActions.OfType<PostBountyAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].TargetFloor).IsEqual(PostFloor);
            AssertThat(pending[0].RewardGold).IsEqual(PostReward);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StratumClick_ComposeCoins_DragPosterOntoBoard_QueuesSameAction_AsTheOldForm()
    {
        // U6's own scenario: drive the ACTUAL gestures (a real click on the cross-section, a real
        // drag-release on the poster) rather than calling the seam methods directly, proving the
        // recognizers wired via the GuiInput event actually reach PostBountyAction.
        var ui = MountMainUi();
        try
        {
            var cross = Find<MineCrossSection>(ui.Bounties, "BountyFloor");
            var coins = Find<CoinStack>(ui.Bounties, "BountyReward");
            var poster = Find<PosterComposer>(ui.Bounties, "BountyPoster");

            var clickPos = new Vector2(10f, 26f * (PostFloor - 1) + 13f); // mid-band for PostFloor
            cross.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = clickPos });
            AssertThat(cross.SelectedFloor).IsEqual(PostFloor);

            coins.SetValue(PostReward); // CoinStack's own click zones are U1's concern, not ours

            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(20f, 20f) });
            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = new Vector2(180f, 20f) });
            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(180f, 20f) });

            var pending = ui.Adapter.PendingActions.OfType<PostBountyAction>().ToList();
            AssertThat(pending.Count).IsEqual(1);
            AssertThat(pending[0].TargetFloor).IsEqual(PostFloor);
            AssertThat(pending[0].RewardGold).IsEqual(PostReward);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void OpenBounty_HasNoGeneratedArt_RendersArtRectFallback()
    {
        // KTD3 fallback path: a posted bounty has no per-post art concept, so ArtRect always
        // misses the manifest and renders the themed placeholder — never a blank hole.
        var ui = MountMainUi(new SimAdapter(WorldWithOpenBounty()));
        try
        {
            var placeholders =
                ui.Bounties.FindChildren("ArtRectFallback", "PanelContainer", recursive: true, owned: false);
            AssertThat(placeholders.Count > 0).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U6: MineCrossSection ──

    [TestCase]
    public void FloorAt_ReturnsTheBandFloor_AndZeroOutsideTheStrip()
    {
        var cross = new MineCrossSection();
        try
        {
            for (var floor = 1; floor <= MonsterTable.FloorCount; floor++)
            {
                var midY = 26f * (floor - 1) + 13f;
                AssertThat(cross.FloorAt(new Vector2(10f, midY))).IsEqual(floor);
            }

            AssertThat(cross.FloorAt(new Vector2(-5f, 10f))).IsEqual(0);     // left of the strip
            AssertThat(cross.FloorAt(new Vector2(200f, 10f))).IsEqual(0);    // right of the strip
            AssertThat(cross.FloorAt(new Vector2(10f, -5f))).IsEqual(0);     // above the shallowest band
            AssertThat(cross.FloorAt(new Vector2(10f, 26f * MonsterTable.FloorCount + 5f))).IsEqual(0); // below the deepest
        }
        finally
        {
            cross.Free();
        }
    }

    [TestCase]
    public void SelectFloor_ClampsToTheLegalRange()
    {
        var cross = new MineCrossSection();
        try
        {
            cross.SelectFloor(0);
            AssertThat(cross.SelectedFloor).IsEqual(1);

            cross.SelectFloor(MonsterTable.FloorCount + 50);
            AssertThat(cross.SelectedFloor).IsEqual(MonsterTable.FloorCount);

            cross.SelectFloor(-100);
            AssertThat(cross.SelectedFloor).IsEqual(1);
        }
        finally
        {
            cross.Free();
        }
    }

    [TestCase]
    public void Click_OnABand_SelectsThatFloor_ViaTheRealGuiInputSignal()
    {
        var cross = new MineCrossSection();
        try
        {
            var selected = -1;
            cross.FloorSelected += f => selected = f;

            cross.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(10f, 26f * 2 + 13f),
                });

            AssertThat(cross.SelectedFloor).IsEqual(3);
            AssertThat(selected).IsEqual(3);
        }
        finally
        {
            cross.Free();
        }
    }

    [TestCase]
    public void ArrowKeys_StepTheSelection_ClampedAtTheEdges()
    {
        var cross = new MineCrossSection();
        try
        {
            cross.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = Key.Down, Pressed = true });
            AssertThat(cross.SelectedFloor).IsEqual(2);

            cross.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = Key.Up, Pressed = true });
            AssertThat(cross.SelectedFloor).IsEqual(1);

            cross.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = Key.Up, Pressed = true });
            AssertThat(cross.SelectedFloor).IsEqual(1); // clamped — never below floor 1
        }
        finally
        {
            cross.Free();
        }
    }

    // ── U6: PosterComposer ──

    [TestCase]
    public void DragRelease_OverTheBoard_RaisesPostRequested()
    {
        var poster = new PosterComposer();
        try
        {
            var raised = false;
            poster.PostRequested += () => raised = true;
            poster.SetPreview(PostFloor, PostReward);

            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(20f, 20f) });
            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = new Vector2(180f, 20f) });
            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(180f, 20f) });

            AssertThat(raised).IsTrue();
        }
        finally
        {
            poster.Free();
        }
    }

    [TestCase]
    public void DragRelease_OffTheBoard_DoesNotRaisePostRequested()
    {
        var poster = new PosterComposer();
        try
        {
            var raised = false;
            poster.PostRequested += () => raised = true;

            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(20f, 20f) });
            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = Vector2.Zero });

            AssertThat(raised).IsFalse();
        }
        finally
        {
            poster.Free();
        }
    }

    [TestCase]
    public void PressDown_OffThePoster_StartsNoDrag_ReleaseOverBoardRaisesNothing()
    {
        var poster = new PosterComposer();
        try
        {
            var raised = false;
            poster.PostRequested += () => raised = true;

            // Press somewhere that is neither the poster nor the board — no drag armed.
            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(60f, 60f) });
            poster.EmitSignal(
                Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(180f, 20f) });

            AssertThat(raised).IsFalse();
        }
        finally
        {
            poster.Free();
        }
    }

    private static GameState WorldWithOpenBounty()
    {
        var baseState = GameFactory.NewGame(9200);
        var bounty = new Bounty(new BountyId(1), PostFloor, PostReward, PostedOnDay: 1, AcceptedBy: null, Paid: false);
        return baseState with { Bounties = ImmutableList.Create(bounty) };
    }
}
#endif
