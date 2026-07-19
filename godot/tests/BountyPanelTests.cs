#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 polish (R18/AE7/KTD2/KTD3): the bounty board rebuilt around <c>UiKit.Section</c>s —
/// Post Bounty / Open Bounties — with each open bounty a themed <c>Card</c>. Every scenario
/// drives the SAME sim read (<c>state.Bounties</c>) and action queue
/// (<see cref="PostBountyAction"/>) the pre-polish panel used, through the real Controls
/// (<see cref="Press"/>/<see cref="PressEnabled"/>) under their pinned <c>Name</c>s
/// (<c>BountyFloor</c>/<c>BountyReward</c>/<c>PostBounty</c>), proving only the visual
/// composition changed.
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

            // The post form's controls survive the polish under their pinned Names.
            Find<SpinBox>(ui.Bounties, "BountyFloor");
            Find<SpinBox>(ui.Bounties, "BountyReward");
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
    public void PressingPostButton_QueuesPostBountyAction_InPendingActions()
    {
        var ui = MountMainUi();
        try
        {
            Find<SpinBox>(ui.Bounties, "BountyFloor").Value = PostFloor;
            Find<SpinBox>(ui.Bounties, "BountyReward").Value = PostReward;
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

    private static GameState WorldWithOpenBounty()
    {
        var baseState = GameFactory.NewGame(9200);
        var bounty = new Bounty(new BountyId(1), PostFloor, PostReward, PostedOnDay: 1, AcceptedBy: null, Paid: false);
        return baseState with { Bounties = ImmutableList.Create(bounty) };
    }
}
#endif
