#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Phase C/D "actually in the 3D game" coverage: the systems that shipped to the sim (Guild
/// Assessment + Confidence, the campaign arc, the U-D4 progression spine) must be visible in the
/// Godot client, not just the CLI. These assert the HUD chips and the progression panel render.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PhaseDSurfacesTests
{
    [TestCase]
    public void Hud_Surfaces_Confidence_Assessment_And_Act_Chips()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui); // force a HUD refresh tick

            AssertThat(Find<Control>(ui, "ConfidenceChip")).IsNotNull();
            AssertThat(Find<Control>(ui, "AssessmentChip")).IsNotNull();
            AssertThat(Find<Control>(ui, "ActChip")).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ProgressionPanel_Opens_AndRendersAllFiveLadders_WithNextRungs()
    {
        // U3 (tutorial-revamp plan, §11.13): Progress is now a gated tray book (opens on the first
        // BountyPaid) — mounted with one already paid so this stays a test of the panel's OWN
        // content, not of the gate itself (SurfaceUnlocksTests owns that).
        var paidBounty = new Bounty(new BountyId(1), TargetFloor: 1, RewardGold: 10, PostedOnDay: 1, AcceptedBy: null, Paid: true);
        var ui = MountMainUi(new SimAdapter(GameFactory.NewGame(2026) with { Bounties = ImmutableList.Create(paidBounty) }));
        try
        {
            Press(ui, "OpenProgress");
            var text = RenderedText(ui.Progress);

            AssertThat(text).Contains("Forge");
            AssertThat(text).Contains("Depth");
            AssertThat(text).Contains("Roster");
            AssertThat(text).Contains("Wealth");
            AssertThat(text).Contains("Chronicle");
            AssertThat(text).Contains("next:");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void DepthsPanel_SurfacesBothLiveVenues_AndDenThreat()
    {
        var ui = MountMainUi();
        try
        {
            ui.Depths.Refresh();
            var text = RenderedText(ui.Depths);

            // U-C4: the second venue (Gloomwood) is now a real tile beside the Mine.
            AssertThat(text).Contains("Mine");
            AssertThat(text).Contains("Gloomwood");
            // U-C3: den escalation is legible per venue.
            AssertThat(text.ToLowerInvariant()).Contains("den");
            // The tiles exist as named nodes for both live venues.
            AssertThat(Find<Control>(ui.Depths, "VenueTile_mine")).IsNotNull();
            AssertThat(Find<Control>(ui.Depths, "VenueTile_gloomwood")).IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
