#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U16 (KTD13 HUD layout spec): the bottom-right PiP dock, mounted on the real
/// <c>MainUi</c> so its visibility is driven by the real phase clock, not a hand-built card.
/// Covers the one explicitly test-pinned rule (PiP absent Morning/Evening) plus the click-to-
/// expand wire into <see cref="MainUi.Mirror"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PipDockTests
{
    [TestCase]
    public void Morning_PipAbsent()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            ui.Pip._Process(0.0); // Docked is computed in _Process (the slide-out driver) — force one frame
            AssertThat(ui.Pip.Docked).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Expedition_Camp_Deep_PipDocked_Evening_PipAbsentAgain()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Expedition);
            ui.Pip._Process(0.0); // force the slide-state recompute (no real frame pump in this suite)
            AssertThat(ui.Pip.Docked).IsTrue();

            AdvanceToPhase(ui, DayPhase.Evening);
            // Camp/ExpeditionDeep may or may not exist for today's parties depending on staging —
            // either way, the moment the sim reports Evening the dock's intent flips back off.
            ui.Pip._Process(0.0);
            AssertThat(ui.Pip.Docked).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Clock_Paused_FeedHoldsStill_Played_ItAdvances()
    {
        // U25 follow-up (a): the PiP feed pauses with the clock (paused != engaged — an engaged
        // surface, e.g. a drawer open over the world, keeps the feed flowing per KTD3; PipDock's
        // own visibility never forces a pause the way ScryingMirror's does, so gating on
        // PhaseClock.Playing here is safe — see ScryingMirror.cs for why that surface differs).
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Camp);
            ui.Clock.Pause();

            ui.Pip._Process(100.0); // would force a full reveal if the feed were still advancing
            AssertThat(ui.Pip.CurrentBeats.IsEmpty).IsTrue();

            ui.Clock.Play();
            ui.Pip._Process(100.0);
            AssertThat(ui.Pip.CurrentBeats.IsEmpty).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ExpandButton_OpensTheScryingMirror()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Expedition);
            AssertThat(ui.Mirror.Visible).IsFalse();

            Press(ui.Pip, "PipExpand");

            AssertThat(ui.Mirror.Visible).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U9 (world-and-interiors plan, R5) — "too small and unsure what its even supposed to
    // show" ──────────────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void Dock_NamesItself_AndStatesTheExpandAffordance()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(Find<Label>(ui.Pip, "PipTitle").Text)
                .OverrideFailureMessage("The dock still doesn't say what it is.")
                .IsEqual("SCRYING MIRROR");
            AssertThat(Find<Button>(ui.Pip, "PipExpand").Text)
                .OverrideFailureMessage("The expand button copy is still the unlabelled \"Mirror ⤢\".")
                .Contains("Watch the delve");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Refresh_StagedPartyHp_OnePipPerPartyMember_ColorReflectsFraction()
    {
        // Standalone PipDock + handcrafted GameState (MineWatchTests' own fixture idiom) rather
        // than a real ticked campaign: a fresh default campaign's heroes resolve UNSTAGED straight
        // into PendingExpeditions (see ScryingMirrorTests' own comment), never staging into
        // InFlight -- so live per-hero hp (the ONE thing a pip can honestly show) is only ever
        // available for a genuinely staged/camped party, which this builds directly.
        var pip = new PipDock();
        try
        {
            pip.Build();
            var heroes = ImmutableSortedDictionary<int, Hero>.Empty
                .Add(1, Delver(1, "V1", "vanguard"))
                .Add(2, Delver(2, "S1", "striker"))
                .Add(3, Delver(3, "M1", "mystic"));
            var camp = new InFlightExpedition(
                Party: ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3)),
                TargetFloor: 2, CheckpointFloor: 1, VenueId: "mine",
                Hp: ImmutableSortedDictionary<int, int>.Empty.Add(1, 40).Add(2, 30).Add(3, 5),
                Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
                Gold: ImmutableSortedDictionary<int, int>.Empty,
                Dead: ImmutableSortedSet<int>.Empty,
                Floors: ImmutableList<FloorOutcome>.Empty,
                Loot: ImmutableList<OreLoot>.Empty,
                DeepestFloorCleared: 1);
            var state = GameFactory.NewGame(9098) with
            {
                Heroes = heroes, Phase = DayPhase.Camp, InFlight = ImmutableList.Create(camp),
            };

            pip.Refresh(state, ImmutableList<GameEvent>.Empty);

            var pips = Find<HBoxContainer>(pip, "PipHpPips");
            AssertThat(pips.GetChildCount())
                .OverrideFailureMessage("Expected one pip per party member.")
                .IsEqual(3);

            // Hero 3 (5/40 hp — well under the danger line) reads distinctly from Hero 1 (full hp).
            var lowHp = Find<ColorRect>(pip, "HpPip_3");
            var fullHp = Find<ColorRect>(pip, "HpPip_1");
            AssertThat(lowHp.Color)
                .OverrideFailureMessage("A near-dead party member's pip is the same color as a full-hp one.")
                .IsNotEqual(fullHp.Color);
        }
        finally
        {
            pip.Free();
        }
    }

    [TestCase]
    public void Refresh_NoStagedParty_HpPipsRowStaysEmpty()
    {
        // R5's own honesty rule: never fabricate a pip for hp nobody knows yet (Expedition/Rumored
        // -- no InFlight entry to read).
        var pip = new PipDock();
        try
        {
            pip.Build();
            var plan = new PartyPlan(ImmutableList.Create(new HeroId(1)), TargetFloor: 1, VenueId: "mine");
            var state = GameFactory.NewGame(9098) with { Phase = DayPhase.Expedition };
            var events = ImmutableList.Create<GameEvent>(new PartiesFormed(ImmutableList.Create(plan)));

            pip.Refresh(state, events);

            AssertThat(Find<HBoxContainer>(pip, "PipHpPips").GetChildCount())
                .OverrideFailureMessage("A Rumored (not-yet-staged) party must not show fabricated hp pips.")
                .IsEqual(0);
        }
        finally
        {
            pip.Free();
        }
    }

    private static Hero Delver(int id, string name, string classId) => new(
        new HeroId(id), name, classId, Level: 3, MaxHp: 40, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);
}
#endif
