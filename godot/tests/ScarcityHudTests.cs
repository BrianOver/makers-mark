#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Tools;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U10 (first-play/Legends-Visible plan, "surface scarcity in the Godot HUD"): the action-slot pip
/// row and the <see cref="RaidForecastBoard"/> are pure projections of existing sim state
/// (<c>GameState.ActionSlotsRemaining</c> and <see cref="RaidForecast.ForTomorrow"/>) — zero sim
/// change (KTD2). Property-only assertions; no frame pump (no 3D viewport in this chain, but the
/// no-pump idiom is kept house-wide).
///
/// <para>P2-LONG-17 ("Rent demoted; the assessor gets a face"): the rent-countdown chip this class
/// doc used to describe is gone — Rent no longer occupies a permanent HUD chip at all. The tests
/// below cover its replacement instead: a Morning-only line on the clock banner, stating the sim's
/// own recorded charge with no pay verb attached (a manual pay button would be "a deadline dressed
/// as a verb" — the plan's own ruling), plus the two guards this demotion is worth having: no
/// Rent/Assessment-named chip survives anywhere in the permanent stat-chip row, and no new
/// pressable verb appeared on the rent path.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScarcityHudTests
{
    /// <summary>Property-driven, not the default campaign's own numbers: a fixture whose Rent is
    /// set to values nothing else in this suite happens to produce, so a hardcoded string ("30g",
    /// the base rent) could never make this pass by accident.</summary>
    [TestCase]
    public void RentMorningLine_StatesTheRecordedCharge()
    {
        var custom = GameFactory.NewGame(2026) with
        {
            Rent = new RentState(DaysUntilDue: 4, AmountDueGold: 77, MissedPayments: 0, ConfidencePermille: 900),
        };
        var ui = MountMainUi(new SimAdapter(custom));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Setup check: this fixture must start in Morning for the rent line to render.")
                .IsEqual(DayPhase.Morning);

            var clockLabel = Find<Label>(ui, "ClockLabel").Text;
            AssertThat(clockLabel)
                .OverrideFailureMessage($"the Morning line never named the sim's own recorded rent (77g/4d): \"{clockLabel}\"")
                .Contains("77g");
            AssertThat(clockLabel).Contains("4d");
        }
        finally { Unmount(ui); }
    }

    /// <summary>A missed payment escalates the sim's own record (<see
    /// cref="RentState.MissedPayments"/>) — the Morning line must name it, still off the recorded
    /// fields alone, never a formula this client invents.</summary>
    [TestCase]
    public void RentMorningLine_NamesMissedPayments_WhenTheSimRecordedAny()
    {
        var custom = GameFactory.NewGame(2026) with
        {
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: 55, MissedPayments: 3, ConfidencePermille: 400),
        };
        var ui = MountMainUi(new SimAdapter(custom));
        try
        {
            var clockLabel = Find<Label>(ui, "ClockLabel").Text;
            AssertThat(clockLabel).Contains("55g");
            AssertThat(clockLabel)
                .OverrideFailureMessage($"the Morning line never named the 3 missed payments: \"{clockLabel}\"")
                .Contains("3 missed");
        }
        finally { Unmount(ui); }
    }

    /// <summary>Only Morning gets the line — outside it, the fact is still true but costs no
    /// screen space, which is the actual demotion (never present all day the way the old chip
    /// was).</summary>
    [TestCase]
    public void RentMorningLine_NeverRendersOutsideMorning()
    {
        var custom = GameFactory.NewGame(2026) with
        {
            Phase = DayPhase.Evening,
            Rent = new RentState(DaysUntilDue: 4, AmountDueGold: 77, MissedPayments: 0, ConfidencePermille: 900),
        };
        var ui = MountMainUi(new SimAdapter(custom));
        try
        {
            var clockLabel = Find<Label>(ui, "ClockLabel").Text;
            AssertThat(clockLabel)
                .OverrideFailureMessage($"the rent line leaked outside Morning: \"{clockLabel}\"")
                .NotContains("77g");
        }
        finally { Unmount(ui); }
    }

    /// <summary>Scans whatever chips actually live in the permanent stat-chip row, rather than
    /// asserting the two removed node NAMES are individually absent — so a differently-named
    /// future re-add of a demoted Rent/Assessment gauge is still caught.</summary>
    [TestCase]
    public void StatChips_CarryNoPermanentChipForRentOrGuildAssessment()
    {
        var ui = MountMainUi();
        try
        {
            var statChips = Find<HBoxContainer>(ui, "StatChips");
            var names = ScreenObservation.Descendants(statChips).Select(n => n.Name.ToString()).ToList();

            AssertThat(names.Any(n => n.Contains("Rent")))
                .OverrideFailureMessage($"a Rent-named node still lives in the permanent stat-chip row: {string.Join(", ", names)}")
                .IsFalse();
            AssertThat(names.Any(n => n.Contains("Assessment")))
                .OverrideFailureMessage($"an Assessment-named node still lives in the permanent stat-chip row: {string.Join(", ", names)}")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>The ruling's own guard, and the one most worth having: demoting Rent must never
    /// grow a manual pay verb (§11's own words — "a deadline dressed as a verb"). Scans every
    /// Button in the whole client rather than one known location.</summary>
    [TestCase]
    public void NoNewPressableVerbAppearsOnTheRentPath()
    {
        var ui = MountMainUi();
        try
        {
            var suspects = ScreenObservation.Descendants(ui).OfType<Button>()
                .Where(b => b.Name.ToString().Contains("Rent")
                    || (b.Text is { Length: > 0 } text && text.Contains("Rent")))
                .ToList();

            AssertThat(suspects.Count)
                .OverrideFailureMessage(
                    "a rent-related pressable verb exists — the plan's own ruling is that a pay " +
                    $"button would be a deadline dressed as a verb: {string.Join(", ", suspects.Select(b => b.Name))}")
                .IsEqual(0);
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
        // U3 (tutorial-revamp plan, §11.13): Forecast is now a gated tray book (opens once you
        // reach an Evening — it forecasts tomorrow) — mounted at Evening so this stays a test of
        // the WIRING, not of the gate itself (SurfaceUnlocksTests owns that).
        var ui = MountMainUi(new SimAdapter(GameFactory.NewGame(2026) with { Phase = DayPhase.Evening }));
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
        // U3 (tutorial-revamp plan, §11.13): see ForecastButton_OpensBoard_ContentMatchesSimQuery's
        // own remark — Forecast is gated on reaching an Evening.
        var ui = MountMainUi(new SimAdapter(GameFactory.NewGame(2026) with { Phase = DayPhase.Evening }));
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
