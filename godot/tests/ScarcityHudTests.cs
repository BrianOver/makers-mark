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

    /// <summary>
    /// P2-MEMORY-20 ("the forecast gets a face"): the board renders the party anchor's own line —
    /// <see cref="GodotClient.Ui.MusterVoice.AnchorLine"/> — for EVERY mustering party, not just the
    /// first. A fresh starting roster mustering for the first time carries a real gear gap on every
    /// hero (nobody has crafted anything yet), so this exercises the "going without" branch rather
    /// than the quieter full-kit one — <see cref="ForecastBoard_QuietDay_RendersNoRaidsLine_NotEmpty"/>
    /// below covers the no-parties-at-all case, and <c>MusterVoiceTests</c> covers the full-kit
    /// branch as a pure-logic property. This is the wiring check: the exact string the read-model
    /// produces actually reaches the screen, verbatim, for whichever party ordinal it is.
    /// </summary>
    [TestCase]
    public void ForecastBoard_RendersTheAnchorLine_ForEveryMusteringParty()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 9105));
        var expected = RaidForecast.ForTomorrow(state);
        AssertThat(expected.IsEmpty)
            .OverrideFailureMessage("setup check: a fresh starting roster must muster at least one party.")
            .IsFalse();

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Forecast.ShowForTomorrow(state);
            var text = RenderedText(ui.Forecast);

            foreach (var party in expected)
            {
                AssertThat(text)
                    .OverrideFailureMessage(
                        $"the board must speak the SAME line MusterVoice derives for this party, verbatim: \"{text}\"")
                    .Contains(GodotClient.Ui.MusterVoice.AnchorLine(party));
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Negative control (this unit's own "fires at the muster and nowhere else"): the anchor's voice
    /// is wired into <c>RaidForecastBoard.RenderParty</c> alone, so it must never leak into the
    /// board's OTHER sections — "TOMORROW AT THE COUNTER" and "THE LIST" render from entirely
    /// different read-models (<see cref="CounterForecast"/>, <see cref="DemandBoard"/>) and must
    /// never coincidentally echo a muster-voice phrase.
    /// </summary>
    [TestCase]
    public void MusterVoicePhrasing_NeverAppearsOutsideThePartySections()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 9106));
        var expected = RaidForecast.ForTomorrow(state);
        AssertThat(expected.IsEmpty)
            .OverrideFailureMessage("setup check: a fresh starting roster must muster at least one party.")
            .IsFalse();

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Forecast.ShowForTomorrow(state);
            var text = RenderedText(ui.Forecast);
            // TOMORROW AT THE COUNTER + THE LIST render before the first party section (see
            // RaidForecastBoard.ShowForTomorrow — RenderCounterSection runs before the party loop).
            var scopedBlock = ExtractBlock(text, "TOMORROW AT THE COUNTER", "Party 1:");

            AssertThat(scopedBlock.Contains("of us for floor", StringComparison.Ordinal))
                .OverrideFailureMessage($"the muster voice leaked into the counter/todo sections: \"{scopedBlock}\"")
                .IsFalse();
            AssertThat(scopedBlock.Contains("Just me, for floor", StringComparison.Ordinal))
                .OverrideFailureMessage($"the muster voice leaked into the counter/todo sections: \"{scopedBlock}\"")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Negative control: nowhere else in the client speaks this way before the forecast board is
    /// ever opened — the anchor's line is a projection of tomorrow's muster, not a standing UI
    /// fixture, so a client that has never opened the board must never render it by accident.
    /// </summary>
    [TestCase]
    public void MusterVoicePhrasing_NeverAppearsAnywhereInTheClient_BeforeTheForecastBoardIsOpened()
    {
        var ui = MountMainUi(new SimAdapter(HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 9107))));
        try
        {
            var text = RenderedText(ui);
            AssertThat(text.Contains("of us for floor", StringComparison.Ordinal))
                .OverrideFailureMessage($"the muster voice rendered somewhere without the board ever being opened: \"{text}\"")
                .IsFalse();
            AssertThat(text.Contains("Just me, for floor", StringComparison.Ordinal))
                .OverrideFailureMessage($"the muster voice rendered somewhere without the board ever being opened: \"{text}\"")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// P2-SCREEN-18 (decision 3, "fill the empty slot, or upgrade the full one"): the board's OTHER
    /// arm. <see cref="ForecastButton_OpensBoard_ContentMatchesSimQuery"/> above only ever pins the
    /// party/floor/threat rendering — the starting roster carries no gear at all, so <see
    /// cref="ForecastParty.WornGear"/> stays empty there and never gets exercised. This fixture
    /// equips one hero with a real, player-marked item so the "Gear worn:" line has something to
    /// prove: link 1 ("you make a thing, and it is provably yours") reaching the one screen the
    /// player reads before every decision to fill or upgrade.
    /// </summary>
    [TestCase]
    public void ForecastBoard_NamesWornGear_ForAPlayerCraftedSlot()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(2026));
        var hero = state.Heroes.Values.First();
        var item = new Item(
            new ItemId(9001), "recipe", "Copper Dagger", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(1, 1, 1), new MakersMark("You", 2), ImmutableList<ItemHistoryEntry>.Empty);
        var geared = hero with { Gear = new GearSet(item.Id, null, null) };
        state = state with
        {
            Items = state.Items.Add(item.Id.Value, item),
            Heroes = state.Heroes.SetItem(geared.Id.Value, geared),
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Forecast.ShowForTomorrow(state);

            var text = RenderedText(ui.Forecast);
            AssertThat(text).Contains("Gear worn:");
            AssertThat(text).Contains(geared.Name);
            AssertThat(text).Contains("Copper Dagger");
            AssertThat(text)
                .OverrideFailureMessage(
                    $"link 1 (\"it is provably yours\") never rendered for the player's own MakersMark: \"{text}\"")
                .Contains("(yours, day 2)");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The law this unit is closest to breaking: "the forecast does not tell you who will
    /// survive." Guarded as a PATTERN over the actual rendered worn-gear block, never a single
    /// literal string — scoped to that block specifically (not the whole board) because the board
    /// legitimately narrates "it does not tell you who will survive" as first-touch teaching
    /// elsewhere on the same screen (<see cref="RaidForecastBoard"/>'s own gear-gap lesson), which
    /// is meta-commentary ABOUT the rule, not a violation of it.
    /// </summary>
    [TestCase]
    public void ForecastBoard_WornGearBlock_NeverNamesASurvivalEstimateOrPowerScore()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(2027));
        var hero = state.Heroes.Values.First();
        var item = new Item(
            new ItemId(9002), "recipe", "Iron Shield", ItemSlot.Shield, QualityGrade.Superior,
            new ItemStats(1, 1, 1), new MakersMark("You", 5), ImmutableList<ItemHistoryEntry>.Empty);
        var geared = hero with { Gear = new GearSet(null, item.Id, null) };
        state = state with
        {
            Items = state.Items.Add(item.Id.Value, item),
            Heroes = state.Heroes.SetItem(geared.Id.Value, geared),
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Forecast.ShowForTomorrow(state);

            var text = RenderedText(ui.Forecast);
            var wornBlock = ExtractBlock(text, "Gear worn:", "Gear gaps:");
            AssertThat(wornBlock.Length > 0)
                .OverrideFailureMessage("setup check: the worn-gear block never rendered at all.")
                .IsTrue();

            var suspect = new System.Text.RegularExpressions.Regex(
                @"\d+%|\bsurvive[sd]?\b|\bchance\b|\bpower\b|\bwill (win|lose|die)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            AssertThat(suspect.IsMatch(wornBlock))
                .OverrideFailureMessage(
                    $"the worn-gear block must state facts only, never a survival estimate or power score: \"{wornBlock}\"")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>Slices the text between the first <paramref name="startMarker"/> and the following
    /// <paramref name="endMarker"/> (or to the end, if the end marker never appears) — scopes a
    /// property check to one section of the board's rendered text rather than the whole screen.</summary>
    private static string ExtractBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += startMarker.Length;
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
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
            var text = RenderedText(ui.Forecast);
            AssertThat(text).Contains("No parties muster tomorrow");

            // P2-MEMORY-20 negative control: no party means no anchor — the voice never renders
            // for a muster that never happens.
            AssertThat(text.Contains("of us for floor", StringComparison.Ordinal))
                .OverrideFailureMessage($"a quiet day must never speak a party's anchor line: \"{text}\"")
                .IsFalse();
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
