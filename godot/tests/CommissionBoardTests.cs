#if GDUNIT_TESTS
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Wave 3 "Commissions" (plan 2026-07-24-003, U15): <see cref="GodotClient.Panels.CommissionBoard"/>
/// lists <see cref="GameState.Commissions"/> (mirroring <see cref="GodotClient.Panels.RaidForecastBoard"/>'s
/// code-built-modal idiom), Accept/Decline buttons queue the matching action through
/// <see cref="SimAdapter"/>, an empty board renders the explicit "nobody's asking" line, and the
/// modal engages the clock latch exactly like Forecast/Bestiary.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CommissionBoardTests
{
    private static SimAdapter AdapterWithCommission(Commission commission)
    {
        var state = GameComposition.NewCampaign(seed: 5150) with
        {
            Commissions = System.Collections.Immutable.ImmutableList.Create(commission),
        };
        return new SimAdapter(state);
    }

    [TestCase]
    public void ShowOpen_ListsCommission_AcceptButtonQueuesAcceptAction()
    {
        var commission = new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 12, PremiumGold: 30);
        var ui = MountMainUi(AdapterWithCommission(commission));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            AssertThat(ui.Commissions.Visible).IsTrue();
            AssertThat(ui.Commissions.CommissionCount).IsEqual(1);

            PressEnabled(ui.Commissions, "CommissionAccept_1");

            var accepted = ui.Adapter.PendingActions.OfType<AcceptCommissionAction>().Single();
            AssertThat(accepted.Hero).IsEqual(new HeroId(1));
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void DeclineButton_QueuesDeclineAction()
    {
        var commission = new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 12, PremiumGold: 30);
        var ui = MountMainUi(AdapterWithCommission(commission));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            PressEnabled(ui.Commissions, "CommissionDecline_1");

            var declined = ui.Adapter.PendingActions.OfType<DeclineCommissionAction>().Single();
            AssertThat(declined.Hero).IsEqual(new HeroId(1));
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AcceptedCommission_HasNoAcceptDeclineButtons_ShowsStatusInstead()
    {
        var commission = new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Fine, DeadlineDay: 12, PremiumGold: 30)
        {
            Accepted = true,
        };
        var ui = MountMainUi(AdapterWithCommission(commission));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            AssertThat(ui.Commissions.FindChild("CommissionAccept_1", recursive: true, owned: false)).IsNull();
            AssertThat(ui.Commissions.FindChild("CommissionDecline_1", recursive: true, owned: false)).IsNull();
            AssertThat(RenderedText(ui.Commissions)).Contains("Accepted");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void EmptyBoard_RendersExplanatoryPlaceholder()
    {
        var state = GameComposition.NewCampaign(seed: 5151); // no commissions
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);

            AssertThat(ui.Commissions.CommissionCount).IsEqual(0);
            AssertThat(RenderedText(ui.Commissions)).Contains("No one's asking for anything right now.");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void Open_EngagesTheLatch_ClosingDisengages()
    {
        var state = GameComposition.NewCampaign(seed: 5152);
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            AssertThat(ui.Clock.Engaged).IsFalse();

            ui.Commissions.ShowOpen(ui.Adapter.CurrentState);
            AssertThat(ui.Commissions.Visible).IsTrue();
            AssertThat(ui.Clock.Engaged).IsTrue();

            PressEnabled(ui.Commissions, "CommissionClose");
            AssertThat(ui.Commissions.Visible).IsFalse();
            AssertThat(ui.Clock.Engaged).IsFalse();
        }
        finally { Unmount(ui); }
    }
}
#endif
