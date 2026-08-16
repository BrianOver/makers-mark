#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U5 (loop-legibility plan, KTD-E): pins <see cref="TutorialFlow.Registry"/> as the SET
/// (constraint 6 — "tests pin the SET... never a hand-written list a new member can silently
/// miss": every <see cref="TutorialStep"/> value must have exactly one row, enumerated from the
/// registry itself, never a hand-counted total) and proves the two house rules this unit exists
/// to satisfy:
///
/// <list type="bullet">
/// <item><b>Never a dead click, never a silent fallback.</b> Every registry row's <see
/// cref="TutorialAnchor"/> resolves to a REAL on-screen target — a Building anchor against <see
/// cref="TownLayout2D.Venues"/>, a Hud anchor against the live mounted <c>MainUi</c> scene. A step
/// pointing at nothing is a RED test here, never a shrug.</item>
/// <item><b>The overlay and checklist track the chain exactly.</b> Exactly one anchor pulses at a
/// time (never a stale building left lit after the step moves to a Hud control); the checklist's
/// "Arrived" sub-tick fires on a REAL building click (through <c>MainUi.OnTownBuildingClicked</c>,
/// not a direct method call); a gated step's checklist row carries the SHORT gating note (never
/// the old, confusing "press Next/Advance"); mid-tutorial progress survives a reload.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialRegistryConformanceTests
{
    [TestCase]
    public void Registry_HasExactlyOneRowPerTutorialStep_NoDuplicatesNoGaps()
    {
        var steps = Enum.GetValues<TutorialStep>();
        AssertThat(TutorialFlow.Registry.Count)
            .OverrideFailureMessage(
                $"TutorialFlow.Registry has {TutorialFlow.Registry.Count} rows but there are " +
                $"{steps.Length} TutorialStep values — a step was added without a registry row, " +
                "or a row was left behind for a removed one.")
            .IsEqual(steps.Length);

        AssertThat(TutorialFlow.Registry.Select(d => d.Step).Distinct().Count())
            .OverrideFailureMessage("TutorialFlow.Registry has a duplicate Step row.")
            .IsEqual(steps.Length);

        foreach (var step in steps)
        {
            AssertThat(TutorialFlow.Registry.Any(d => d.Step == step))
                .OverrideFailureMessage($"{step} has no row in TutorialFlow.Registry.")
                .IsTrue();
        }
    }

    [TestCase]
    public void Registry_DisplayIndicesSpanOneToTotalSteps_Contiguously()
    {
        var indices = TutorialFlow.Registry.Select(d => d.DisplayIndex).Distinct().OrderBy(i => i).ToList();
        AssertThat(indices.First())
            .OverrideFailureMessage("The lowest DisplayIndex in the registry is not 1.")
            .IsEqual(1);
        AssertThat(indices.Last())
            .OverrideFailureMessage("The highest DisplayIndex does not match TutorialFlow.TotalSteps.")
            .IsEqual(TutorialFlow.TotalSteps);

        for (var i = 0; i < indices.Count; i++)
        {
            AssertThat(indices[i])
                .OverrideFailureMessage($"DisplayIndex {i + 1} is missing — the checklist would show a gap.")
                .IsEqual(i + 1);
        }
    }

    [TestCase]
    public void Registry_EveryRow_HasNonEmptyLabelsAndNoteAndADurablePredicateAndAValidMinDayAndSelfInclusiveAdvanceFrom()
    {
        foreach (var def in TutorialFlow.Registry)
        {
            AssertThat(string.IsNullOrWhiteSpace(def.ShortLabel))
                .OverrideFailureMessage($"{def.Step}: ShortLabel is empty — the checklist row would render blank.")
                .IsFalse();
            AssertThat(string.IsNullOrWhiteSpace(def.TeachNote))
                .OverrideFailureMessage($"{def.Step}: TeachNote is empty.")
                .IsFalse();
            AssertThat(def.IsDone)
                .OverrideFailureMessage($"{def.Step}: IsDone predicate is null — Advance() could never complete this step.")
                .IsNotNull();
            AssertThat(def.MinDay)
                .OverrideFailureMessage($"{def.Step}: MinDay must be at least 1 (the campaign's first day).")
                .IsGreaterEqual(1);
            AssertThat(def.AdvanceFrom)
                .OverrideFailureMessage($"{def.Step}: AdvanceFrom is empty — this row could never fire.")
                .IsNotEmpty();
            AssertThat(def.AdvanceFrom.Contains(def.Step))
                .OverrideFailureMessage(
                    $"{def.Step}: AdvanceFrom does not include the row's own Step — Advance()'s single forward " +
                    "pass could never reach this row from its own step (only from an earlier shared-anchor one).")
                .IsTrue();
        }
    }

    [TestCase]
    public void Registry_EveryBuildingAnchor_ResolvesAgainstTownLayout2D()
    {
        var venueKeys = TownLayout2D.Venues.Select(v => v.Key).ToHashSet();
        foreach (var def in TutorialFlow.Registry.Where(d => d.Anchor.Kind == TutorialAnchorKind.Building))
        {
            AssertThat(venueKeys.Contains(def.Anchor.Key!))
                .OverrideFailureMessage(
                    $"{def.Step}'s Building anchor \"{def.Anchor.Key}\" is not a real TownLayout2D venue key — " +
                    "this step would point at nothing (house rule: never a silent fallback).")
                .IsTrue();
        }
    }

    /// <summary>U2 (tutorial-revamp plan, §11.13): BuyMaterial/Craft's static registry default
    /// (blacksmith's "shelf"/"anvil") must resolve against a real mounted room — the conformance
    /// half of "never a silent fallback" for the new Station anchor kind.</summary>
    [TestCase]
    public void Registry_EveryStationAnchor_ResolvesAgainstARealRoomStation()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var def in TutorialFlow.Registry.Where(d => d.Anchor.Kind == TutorialAnchorKind.Station))
            {
                Building2D? station = null;
                Exception? thrown = null;
                try
                {
                    station = ui.Town.FindStation(def.Anchor.Key!, def.Anchor.StationId!);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }

                AssertThat(thrown)
                    .OverrideFailureMessage(
                        $"{def.Step}'s Station anchor ({def.Anchor.Key}, {def.Anchor.StationId}) does not resolve " +
                        $"against a real room station — this step would point at nothing: {thrown}")
                    .IsNull();
                AssertThat(station).IsNotNull();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U2: <see cref="TutorialFlow.CurrentAnchor"/>'s dynamic per-profession substitution
    /// (<see cref="WorkshopVocab.MaterialsStationIdFor"/>/<see
    /// cref="WorkshopVocab.CraftStationIdFor"/>) must resolve against the REAL room for every
    /// starting profession, not just blacksmith's own static default — an alchemist/tanner/engineer
    /// start must never point BuyMaterial/Craft at blacksmith's "shelf"/"anvil" by mistake.</summary>
    [TestCase]
    public void CurrentAnchor_ResolvesTheLiveProfessionsOwnStation_ForEveryStartingProfession()
    {
        foreach (var professionId in new[]
                 {
                     ProfessionRegistry.BlacksmithId, AlchemyProfession.Id, TanningProfession.Id, EngineeringProfession.Id,
                 })
        {
            var ui = MountMainUi(new SimAdapter(GameComposition.NewCampaign(ScriptedSession.Seed, professionId)));
            try
            {
                AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
                var anchor = ui.Tutorial.CurrentAnchor;
                AssertThat(anchor.Kind).IsEqual(TutorialAnchorKind.Station);

                Building2D? station = null;
                Exception? thrown = null;
                try
                {
                    station = ui.Town.FindStation(anchor.Key!, anchor.StationId!);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }

                AssertThat(thrown)
                    .OverrideFailureMessage(
                        $"{professionId}'s BuyMaterial anchor ({anchor.Key}, {anchor.StationId}) does not resolve: {thrown}")
                    .IsNull();
                AssertThat(station).IsNotNull();
            }
            finally
            {
                Unmount(ui);
            }
        }
    }

    [TestCase]
    public void Registry_EveryHudAnchor_ResolvesToALiveControlInTheMountedScene()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var def in TutorialFlow.Registry.Where(d => d.Anchor.Kind == TutorialAnchorKind.Hud))
            {
                var control = ui.FindChild(def.Anchor.Key!, recursive: true, owned: false) as Control;
                AssertThat(control)
                    .OverrideFailureMessage(
                        $"{def.Step}'s Hud anchor \"{def.Anchor.Key}\" does not resolve to a Control in the live " +
                        "MainUi scene — this step would point at nothing (house rule: never a silent fallback).")
                    .IsNotNull();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Registry_NoTwoHudAnchors_ShareAControlName()
    {
        // A weaker pin than full anchor uniqueness — Building anchors legitimately repeat (Shelve
        // and OpenCounter both point at "market", the same physical Shop) — but two DIFFERENT Hud
        // control names should never collide, since each names one specific on-screen control.
        var hudNames = TutorialFlow.Registry
            .Where(d => d.Anchor.Kind == TutorialAnchorKind.Hud)
            .Select(d => d.Anchor.Key)
            .ToList();
        AssertThat(hudNames.Distinct().Count())
            .OverrideFailureMessage("Two different tutorial steps declared the SAME Hud control name.")
            .IsEqual(hudNames.Count);
    }

    [TestCase]
    public void Overlay_PulsesExactlyTheActiveSteps_AnchorAndNothingElse()
    {
        var ui = MountMainUi();
        try
        {
            // Day 1, BuyMaterial: U2 (tutorial-revamp plan, §11.13) re-pointed this at
            // Station("forge", "shelf") — blacksmith's own materials station — rather than the
            // whole Forge building, so the overlay pulses the shelf itself.
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            AssertThat(ui.Overlay.PulsingBuildingKey)
                .OverrideFailureMessage("BuyMaterial's own Station anchor (the shelf) is not pulsing on a fresh mount.")
                .IsEqual("shelf");
            AssertThat(ui.Overlay.PulsingHudControlName).IsNull();

            // Drive to Shelve: Building("market") — the pulse must move OFF the shelf onto market.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve);
            AssertThat(ui.Overlay.PulsingBuildingKey)
                .OverrideFailureMessage("The overlay did not move its pulse from forge to market when Shelve became current.")
                .IsEqual("market");

            // Drive to LookIn: Hud("WatchButton") — a Building pulse must not linger once the
            // anchor becomes a Hud control.
            var craftedItem = ScriptedSession.CraftedItem(ui.Adapter.CurrentState);
            ui.Adapter.Queue(new StockAction(craftedItem, 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: party departs -> LookIn
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.LookIn);
            AssertThat(ui.Overlay.PulsingBuildingKey)
                .OverrideFailureMessage("A Building pulse is still lit after the anchor moved to a Hud control.")
                .IsNull();
            AssertThat(ui.Overlay.PulsingHudControlName)
                .OverrideFailureMessage("LookIn's own anchor (WatchButton) is not the thing carrying the outline.")
                .IsEqual("WatchButton");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void EnteringTheAnchoredBuilding_TicksTheChecklistsArrivedSubBox()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
            var before = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.Current);
            AssertThat(before.VisitedAnchor)
                .OverrideFailureMessage("The checklist reports 'Arrived' before the player ever clicked the forge.")
                .IsFalse();

            // Real click routing (Building2D.RaisePick — the same seam a real click/E-interact
            // drives, per Building2DInteractionTests' own precedent), not a direct Notify call —
            // proves the wiring through MainUi.OnTownBuildingClicked's WALKABLE-INTERIOR route
            // (the forge has one), the exact route that never touches Drawer.CurrentPanelId at
            // all and is why "the tutorial isn't updating despite entering the forge" survived a
            // drawer-only check before this unit.
            ui.Town.FindBuilding("forge").RaisePick();

            var after = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.Current);
            AssertThat(after.VisitedAnchor)
                .OverrideFailureMessage("Entering the forge (the current step's own anchor) did not tick the checklist's sub-box.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AGatedStep_ShowsItsGatingNote_OutsideItsOwnWindow_NeverThePressNextAdvanceCopy()
    {
        var ui = MountMainUi();
        try
        {
            // Drive to OpenCounter (display slot 6, Day-2-gated) the same way
            // TutorialFlowTests.DriveDay1ToLookIn does, then open the Mirror to advance past LookIn.
            var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(craftedItemId, 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: party departs -> LookIn
            ui.Mirror.ShowMirror(); // LookIn -> OpenCounter
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);

            // Still day 1 — OpenCounter's own MinDay is 2, so its checklist row must carry a
            // gating note naming the day, and it must NEVER be the old "press Next/Advance" copy
            // the owner explicitly flagged ("Tutorial 6 says press 'next/advance' assuming this
            // should be 'close the vigil'").
            var row = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.Current);
            AssertThat(row.GatingNote)
                .OverrideFailureMessage($"OpenCounter's checklist row carried no gating note on day {ui.Adapter.CurrentState.Day}.")
                .IsNotNull();
            AssertThat(row.GatingNote!)
                .OverrideFailureMessage($"Gating note still tells the player to press a button: \"{row.GatingNote}\"")
                .NotContains("press Next");
            AssertThat(row.GatingNote!).Contains("Day 2");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void MidTutorialProgress_PersistsAcrossAReload_WithNoDismissOrComplete()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Shelve);
            AssertThat(ui.Tutorial.Active)
                .OverrideFailureMessage("This test's whole premise is an ACTIVE, un-dismissed, un-completed chain.")
                .IsTrue();

            // A second, independent instance reading the SAME user:// file — mirrors
            // TutorialFlowTests.Dismiss_MidChain_PersistsAndNeverReprompts_AfterRemount's own
            // "remount == restart" idiom, but for Step specifically rather than Dismissed.
            var reloaded = new TutorialFlow();
            try
            {
                reloaded.Build();
                reloaded.Load();
                AssertThat(reloaded.Step)
                    .OverrideFailureMessage(
                        "A save made mid-chain (no Dismiss, no Complete) did not persist Step — a reload " +
                        "silently rewound the chain to BuyMaterial.")
                    .IsEqual(TutorialStep.Shelve);
                AssertThat(reloaded.Active)
                    .OverrideFailureMessage("A mid-chain reload should still read as an active, un-dismissed chain.")
                    .IsTrue();
            }
            finally
            {
                reloaded.Free();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U3 (tutorial-revamp plan, §11.13): the trap this unit could create — a gate that hides the
    /// very tray button a live tutorial step is pointing at. Neither HeroCards' own gate (first
    /// sale to a hero) nor Commissions' (first commission posted by the sim) is guaranteed true by
    /// the day the tutorial reaches those two steps — a player can legally reach day 3 having sold
    /// nothing and having no commission posted. <c>MainUi</c>'s own effective-open override must
    /// still keep both buttons pressable regardless of what <see cref="SurfaceUnlocks.IsOpen"/>
    /// alone would say.
    /// </summary>
    [TestCase]
    public void EveryTutorialStepAnchor_PointsAtASurfaceThatIsOpenByThatStepsMinDay()
    {
        var ui = MountMainUi();
        try
        {
            var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(craftedItemId, 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: party departs -> LookIn
            ui.Mirror.ShowMirror(); // LookIn -> OpenCounter

            // Fast-forward past OpenCounter/Vigil/EveningClose without ever selling to a hero or a
            // commission ever posting — the SAME "hand a modified state to Advance" idiom
            // TutorialFlowTests uses throughout, so this claim never depends on simulating a real
            // haggle/camp/commission just to reach day 3.
            var day2 = ui.Adapter.CurrentState with
            {
                Day = 2,
                EventLog = ui.Adapter.CurrentState.EventLog.Add(new CustomerApproached(new HeroId(1))),
                ActionLog = ui.Adapter.CurrentState.ActionLog.Add(
                    new LoggedBatch(2, ui.Adapter.CurrentState.Phase, ImmutableList.Create<PlayerAction>(new CloseCounterAction()))),
            };
            ui.Tutorial.Advance(day2);
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Vigil);

            ui.Tutorial.NotifyCampCardShown();
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.EveningClose);

            ui.Tutorial.Advance(ui.Adapter.CurrentState with { Day = 3 });
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.MeetHeroes);

            ui.RefreshAll(); // re-greys/un-greys the tray from the now-day-3 live state

            AssertThat(SurfaceUnlocks.IsOpen(ui.Adapter.CurrentState, "HeroCards"))
                .OverrideFailureMessage("Test setup drifted — HeroCards' own gate should still read closed here.")
                .IsFalse();
            AssertThat(Find<Button>(ui, "OpenHeroCards").Disabled)
                .OverrideFailureMessage("HeroCards is gated closed while MeetHeroes points straight at it — the player would be stranded.")
                .IsFalse();

            ui.OpenPanel("HeroCards"); // MeetHeroes -> Commission
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.Commission);
            ui.RefreshAll();

            AssertThat(SurfaceUnlocks.IsOpen(ui.Adapter.CurrentState, "Commissions"))
                .OverrideFailureMessage("Test setup drifted — Commissions' own gate should still read closed here.")
                .IsFalse();
            AssertThat(Find<Button>(ui, "OpenCommissions").Disabled)
                .OverrideFailureMessage(
                    "Commissions is gated closed while the Commission step points straight at it — the player would be stranded.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
