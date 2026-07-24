#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U10 (first-play/Legends-Visible plan, "surface scarcity in the Godot HUD"): the rent-countdown
/// chip, the action-slot pip row, and the <see cref="RaidForecastBoard"/> are pure projections of
/// existing sim state (<c>GameState.Rent</c>/<c>ActionSlotsRemaining</c> and
/// <see cref="RaidForecast.ForTomorrow"/>) — zero sim change (KTD2). Property-only assertions; no
/// frame pump (no 3D viewport in this chain, but the no-pump idiom is kept house-wide).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScarcityHudTests
{
    [TestCase]
    public void RentChip_ShowsDaysUntilDueAndAmount()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState;
            var rentChip = Find<Control>(ui, "RentChip");
            var text = RenderedText(rentChip);

            AssertThat(text).Contains($"{state.Rent.DaysUntilDue}d");
            AssertThat(text).Contains($"{state.Rent.AmountDueGold}g");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SlotPips_OneDotPerSlot_FilledMatchesRemaining()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState;
            var pips = Find<HBoxContainer>(ui, "SlotPips");

            var total = pips.GetChildren().OfType<ColorRect>().Count();
            var filled = pips.GetChildren().OfType<ColorRect>()
                .Count(c => c.HasMeta("filled") && (bool)c.GetMeta("filled"));

            AssertThat(total).IsEqual(ActionBudget.SlotsPerDay);
            AssertThat(filled).IsEqual(state.ActionSlotsRemaining);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ForecastButton_OpensBoard_ContentMatchesSimQuery()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState;
            var expected = RaidForecast.ForTomorrow(state);

            PressEnabled(ui, "OpenForecast");

            var board = Find<RaidForecastBoard>(ui, "RaidForecastBoard");
            AssertThat(board.Visible).IsTrue();
            AssertThat(board.PartyCount).IsEqual(expected.Count);
            AssertThat(RenderedText(board)).Contains($"Tomorrow's Raids — Day {state.Day + 1}");

            // Opening a modal engages the latch (clock owned) — same contract as the Ledger.
            AssertThat(ui.Clock.Engaged).IsTrue();

            if (!expected.IsEmpty)
            {
                var first = expected[0];
                var text = RenderedText(board);
                AssertThat(text).Contains($"Target: floor {first.TargetFloor}");
                // Floor 1's threat is always present (Threats run 1..TargetFloor, TargetFloor >= 1).
                AssertThat(text).Contains($"F1: {first.Threats[0].MonsterKind}");
            }
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ForecastBoard_QuietDay_RendersNoRaidsLine_NotEmpty()
    {
        var ui = MountMainUi();
        try
        {
            // No heroes + no bounties => MusterPlan forms no parties => a quiet forecast.
            var quiet = GameFactory.NewGame(2026) with
            {
                Heroes = ImmutableSortedDictionary<int, Hero>.Empty,
                Bounties = ImmutableList<Bounty>.Empty,
            };

            ui.Forecast.ShowForTomorrow(quiet);

            AssertThat(ui.Forecast.PartyCount).IsEqual(0);
            AssertThat(RenderedText(ui.Forecast)).Contains("No parties muster tomorrow");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ForecastBoard_Close_HidesAndReleasesLatch()
    {
        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "OpenForecast");
            AssertThat(Find<RaidForecastBoard>(ui, "RaidForecastBoard").Visible).IsTrue();

            PressEnabled(ui, "ForecastClose");

            AssertThat(Find<RaidForecastBoard>(ui, "RaidForecastBoard").Visible).IsFalse();
            AssertThat(ui.Clock.Engaged).IsFalse();
        }
        finally { Unmount(ui); }
    }
}
#endif
