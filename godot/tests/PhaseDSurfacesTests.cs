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
        // content, not of the gate itself (SurfaceUnlocksTests owns that). P2-HONEST-01: the gate
        // reads the EventLog for a BountyPaid event now (Bounty.Paid itself is never set true by
        // the real sim), so the fixture emits that event, not just the flag.
        var paidBounty = new Bounty(new BountyId(1), TargetFloor: 1, RewardGold: 10, PostedOnDay: 1, AcceptedBy: null, Paid: true);
        var ui = MountMainUi(new SimAdapter(GameFactory.NewGame(2026) with
        {
            Bounties = ImmutableList.Create(paidBounty),
            EventLog = ImmutableList.Create<GameEvent>(new BountyPaid(paidBounty.Id, new HeroId(1), paidBounty.RewardGold)),
        }));
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

    /// <summary>P2-HONEST-01: the harness's own route to Progress must go through the real gate
    /// (<c>MainUi.OpenGatedSurface</c>, wired to the "OpenProgress" tray button's Pressed signal)
    /// -- never a bypass. A fresh campaign's button reads Disabled, and a forced press (<c>Press</c>,
    /// not <c>PressEnabled</c> -- the same "a real click can't reach a Disabled button, but this
    /// suite's Press deliberately bypasses that" precedent <c>ConfirmProfessions_ThreeSelected_...</c>
    /// already established) must refuse without opening the drawer, proving the closed path is a
    /// real refusal rather than a lucky no-op. Once a real <see cref="BountyPaid"/> fact exists, the
    /// identical button reads Enabled and the identical press opens the panel for real.</summary>
    [TestCase]
    public void OpenProgressButton_RefusesWhileGateClosed_OpensForRealOnceABountyIsPaid()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(Find<Button>(ui, "OpenProgress").Disabled)
                .OverrideFailureMessage("OpenProgress must read Disabled on a fresh campaign -- no bounty has ever been paid.")
                .IsTrue();

            Press(ui, "OpenProgress");
            AssertThat(ui.Drawer.CurrentPanelId)
                .OverrideFailureMessage("Progress opened while its own gate reads closed -- OpenGatedSurface didn't refuse it.")
                .IsNotEqual("Progress");
        }
        finally
        {
            Unmount(ui);
        }

        var paidBounty = new Bounty(new BountyId(1), TargetFloor: 1, RewardGold: 10, PostedOnDay: 1, AcceptedBy: null, Paid: true);
        var paid = MountMainUi(new SimAdapter(GameFactory.NewGame(2026) with
        {
            Bounties = ImmutableList.Create(paidBounty),
            EventLog = ImmutableList.Create<GameEvent>(new BountyPaid(paidBounty.Id, new HeroId(1), paidBounty.RewardGold)),
        }));
        try
        {
            AssertThat(Find<Button>(paid, "OpenProgress").Disabled)
                .OverrideFailureMessage("OpenProgress still reads Disabled after a real BountyPaid fact exists.")
                .IsFalse();

            Press(paid, "OpenProgress");
            AssertThat(paid.Drawer.CurrentPanelId)
                .OverrideFailureMessage("The identical button press didn't open Progress once its gate was genuinely open.")
                .IsEqual("Progress");
        }
        finally
        {
            Unmount(paid);
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
