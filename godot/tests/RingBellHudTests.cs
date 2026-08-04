#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Wave 1 "Ring the Bell" (plan 2026-07-24-003): player-decided pacing. U1 (plan 2026-08-03-001,
/// KTD-A "the two-bell day") narrowed this further — the advance button (node name kept as
/// "AdvancePhase") is a real bell ONLY at Morning/Evening now; at Expedition/Camp/ExpeditionDeep it
/// is <see cref="RaidConductor"/>'s "Hurry" control instead (same Control, same node name — see
/// <c>MainUi</c>'s Pressed handler branch on <c>Conductor.Current</c>). The clock label still names
/// the player phase Dawn/Prepare/Quest/Vigil/Deep Vigil/Night, and ringing the Morning bell while a
/// counter session is open still closes it first so the day never silently fails to advance (U5).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RingBellHudTests
{
    private static Button Bell(MainUi ui) => Find<Button>(ui, "AdvancePhase");
    private static Label ClockLabel(MainUi ui) => Find<Label>(ui, "ClockLabel");

    // Mirrors CampPanelTests'/RaidConductorTests' own precedent (duplicated per BellTrayTests' own
    // documented reasoning): DeepestFloorReached: 1 stages the trip (checkpoint 1), and two strong
    // vanguards reliably clear floor 1 clean and park rather than wipe/gate/lose it.
    private static Hero Strong(int id) => new(
        new HeroId(id), $"Strong{id}", "vanguard", Level: 5, MaxHp: 60, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    private static Item Weapon(int id, int attack) => new(
        new ItemId(id), "sword", "Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(attack, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Armor(int id, int defense) => new(
        new ItemId(id), "plate", "Plate", ItemSlot.Armor, QualityGrade.Common,
        new ItemStats(0, defense, 8), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState StagedWorld() => GameFactory.NewGame(6) with
    {
        Phase = DayPhase.Morning,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
    };

    [TestCase]
    public void BellVerb_And_PhaseBanner_TrackTheKernelPhase()
    {
        var ui = MountMainUi();
        try
        {
            // Day 1 Morning, no counter open → Dawn / "Send them off" — the real bell.
            AssertThat(Bell(ui).Text).IsEqual("Send them off");
            AssertThat(ClockLabel(ui).Text).Contains("Dawn");

            PressEnabled(ui, "AdvancePhase"); // Morning → Expedition
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            // U1: no per-phase bell verb here any more — the conductor owns the span, and this
            // SAME control is now its Hurry caption, unconditionally, regardless of InFlight.
            AssertThat(Bell(ui).Text).IsEqual("Hurry the day along");
            AssertThat(ClockLabel(ui).Text).Contains("Quest");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// U1 retired the three per-phase bell verbs entirely — this is the direct descendant of the
    /// old <c>CampBell_TellsTheTruth_AboutWhetherAnyoneIsActuallyBelow</c> test, which pinned the
    /// per-InFlight labels this file no longer emits. Proves the SAME control reads "Hurry the day
    /// along" at Camp even with a party genuinely parked (InFlight non-empty) — the case that used
    /// to render "Let them press deeper".
    /// </summary>
    [TestCase]
    public void CampBell_ReadsHurry_EvenWithAPartyGenuinelyParkedBelow()
    {
        var ui = MountMainUi(new SimAdapter(StagedWorld()));
        try
        {
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: the staged party parks
            ui.RefreshAll();
            AssertThat(ui.Adapter.CurrentState.InFlight.IsEmpty)
                .OverrideFailureMessage("Fixture premise failed: the staged party did not park.")
                .IsFalse();

            AssertThat(Bell(ui).Text)
                .OverrideFailureMessage("The bell must read \"Hurry the day along\" at Camp even with a party parked, never the retired \"Let them press deeper\".")
                .IsEqual("Hurry the day along");
        }
        finally { Unmount(ui); }
    }

    /// <summary>The sweep the plan's own U1 test scenario asks for: none of the three retired
    /// labels renders anywhere across a full day, whichever path (staged or not) that day takes.</summary>
    [TestCase]
    public void RetiredBellLabels_AppearNowhere_AcrossAFullDay()
    {
        string[] retired =
        [
            "Lower them into the mine", "Let them press deeper", "Ring the return bell", "Close the vigil",
        ];

        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "AdvancePhase"); // the one real bell press for Morning: Send them off

            for (var i = 0; i < UiTestSupport.MaxPhasesPerDay && ui.Adapter.CurrentState.Day == 1; i++)
            {
                if (ui.Conductor.Current == RaidConductor.Beat.VigilStop)
                {
                    Press(ui.Camp, "CampDeeper"); // answer the one real stop through its real Control
                }
                else if (ui.Conductor.Current == RaidConductor.Beat.Idle && ui.Adapter.CurrentState.Phase == DayPhase.Evening)
                {
                    PressEnabled(ui, "AdvancePhase"); // the real Evening bell: Snuff the lanterns
                }
                else
                {
                    ui.Conductor.Hurry(); // force every show beat straight through — no frame pump needed
                }

                ui.RefreshAll();
                var rendered = RenderedText(ui);
                foreach (var label in retired)
                {
                    AssertThat(rendered.Contains(label))
                        .OverrideFailureMessage($"Retired bell label \"{label}\" rendered somewhere on screen at phase {ui.Adapter.CurrentState.Phase}.")
                        .IsFalse();
                }
            }

            AssertThat(ui.Adapter.CurrentState.Day)
                .OverrideFailureMessage("The day never actually completed within the iteration budget.")
                .IsGreaterEqual(2);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void OpenCounter_ShowsPrepareBanner()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new OpenCounterAction());
            ui.Adapter.AdvancePhase(); // applies OpenCounter; day HOLDS at Morning (session open)
            ui.RefreshAll();

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Adapter.CurrentState.Counter).IsNotNull();
            AssertThat(ClockLabel(ui).Text).Contains("Prepare");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void RingingBell_WithOpenCounter_ClosesSessionAndAdvances_NeverSilentlyHolds()
    {
        var ui = MountMainUi();
        try
        {
            // Open a counter session (day holds at Morning while Closed==false).
            ui.Adapter.Queue(new OpenCounterAction());
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Adapter.CurrentState.Counter is { Closed: false }).IsTrue();

            // Ring the bell: U5 must close the session first so the day actually moves — a naive
            // AdvanceNow here would tick with Counter{Closed:false} and silently stay at Morning.
            PressEnabled(ui, "AdvancePhase");

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(ui.Adapter.CurrentState.Counter).IsNull(); // session torn down on the day boundary
        }
        finally { Unmount(ui); }
    }
}
#endif
