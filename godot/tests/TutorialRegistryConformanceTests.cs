#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Expedition;
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
            // Drive to OpenCounter (display slot 6) the same way TutorialFlowTests.DriveDay1ToLookIn
            // does, then open the Mirror to advance past LookIn.
            var craftedItemId = new ItemId(ui.Adapter.CurrentState.NextItemId);
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.Queue(new StockAction(craftedItemId, 50));
            ui.Adapter.Queue(new PostBountyAction(ScriptedSession.BountyFloor, ScriptedSession.BountyReward));
            ui.Adapter.AdvancePhase(); // Morning -> Expedition
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: party departs -> LookIn
            ui.Mirror.ShowMirror(); // LookIn -> OpenCounter
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.OpenCounter);

            // Still Day 1, Phase Camp — U-T2-16 dropped OpenCounter's MinDay from 2 to 1 (the real
            // gate was never the calendar, it was the counter's own Morning-only legality), so its
            // checklist row must now carry a MORNING gating note, never a day claim, and never the
            // old "press Next/Advance" copy the owner explicitly flagged ("Tutorial 6 says press
            // 'next/advance' assuming this should be 'close the vigil'").
            var row = ui.Tutorial.Checklist(ui.Adapter.CurrentState).Single(r => r.Current);
            AssertThat(row.GatingNote)
                .OverrideFailureMessage($"OpenCounter's checklist row carried no gating note on day {ui.Adapter.CurrentState.Day}.")
                .IsNotNull();
            AssertThat(row.GatingNote!)
                .OverrideFailureMessage($"Gating note still tells the player to press a button: \"{row.GatingNote}\"")
                .NotContains("press Next");
            AssertThat(row.GatingNote!.Contains("Day", System.StringComparison.Ordinal))
                .OverrideFailureMessage($"OpenCounter's gating note still claims a day gate that no longer exists: \"{row.GatingNote}\"")
                .IsFalse();
            AssertThat(row.GatingNote!).Contains("Morning");
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
    /// KTD-4 (house rule, U1, §11.13): "the fact must be caused by a PlayerAction in ActionLog or
    /// by a Notify-hook, never by a hero-decided event." This is the tripwire against the exact bug
    /// this unit fixes returning: step 6 used to require the CUSTOMER's own accept
    /// (<c>CounterSaleClosed</c>), and step 7's precondition was a stop <c>RaidConductor</c>'s own
    /// doc calls "the UNCOMMON case" — both unfollowable by construction.
    ///
    /// <para>For every row except the two declared UI-navigation-only steps (<see
    /// cref="TutorialStep.LookIn"/>/<see cref="TutorialStep.MeetHeroes"/> key off a real UI open
    /// with no durable predicate this test could construct at all — <c>TutorialFlow</c>'s own class
    /// doc), a state built ONLY from a real player action and its deterministic, state-gated kernel
    /// consequence — never an AI/hero decision (a customer's own accept, a party that actually
    /// managed to park) — must flip <see cref="TutorialStepDef.IsDone"/> true. The switch below is
    /// exhaustive over <see cref="TutorialStep"/>: a step added without an entry here is a compile
    /// error, not a silent gap.</para>
    /// </summary>
    [TestCase]
    public void EveryStepsCompletionFact_IsReachableByPlayerActionAlone()
    {
        var exempt = new[] { TutorialStep.LookIn, TutorialStep.MeetHeroes };
        var baseState = GameComposition.NewCampaign(9001);
        var hero = new HeroId(1);
        var item = new ItemId(1);

        foreach (var def in TutorialFlow.Registry)
        {
            if (exempt.Contains(def.Step))
            {
                continue;
            }

            GameState fixture = def.Step switch
            {
                TutorialStep.BuyMaterial => baseState with
                {
                    EventLog = baseState.EventLog.Add(new MaterialPurchased("copper", 2, 4)),
                },
                TutorialStep.Craft => baseState with
                {
                    EventLog = baseState.EventLog.Add(new ItemCrafted(item, QualityGrade.Common)),
                },
                TutorialStep.Shelve => baseState with
                {
                    EventLog = baseState.EventLog.Add(new ItemSold(item, hero, 10, FromPlayerShop: true)),
                },
                TutorialStep.PostBounty => baseState with
                {
                    EventLog = baseState.EventLog.Add(new BountyPosted(new BountyId(1), 1, 5)),
                },
                TutorialStep.WatchDeparture => baseState with
                {
                    EventLog = baseState.EventLog.Add(new PartyDeparted(ImmutableList.Create(hero), 1)),
                },
                // U1 fix: the customer's own decision (CounterSaleClosed) is deliberately ABSENT
                // here — only the player's own actions, proving the old "a sale must close" gate is
                // gone. U-T2-14 tightened CounterAnsweredAtLeastOnce further: Close alone no longer
                // counts, so this fixture needs a PresentItemAction too (still entirely player-
                // caused, never a hero's own decision) — the walk-away/holding-the-line ending, not
                // a closed sale.
                TutorialStep.OpenCounter => baseState with
                {
                    ActionLog = baseState.ActionLog.Add(new LoggedBatch(
                        2, DayPhase.Morning,
                        ImmutableList.Create<PlayerAction>(
                            new OpenCounterAction(), new PresentItemAction(item), new CloseCounterAction()))),
                },
                // U1 fix: SupplyDelivered/PartyRecalled are still legitimate here — deterministic
                // consequences of the player's own SendSupply/Recall actions, gated only by state
                // the player can see, never a hero's own choice. The OTHER completion path
                // (NotifyCampCardShown, a UI-only hook with no durable predicate) is covered
                // separately by TutorialFlowTests' Step7_Completes_OnSeeingTheCampCard_... — this
                // row's own IsDone never needs a party to have actually parked.
                TutorialStep.Vigil => baseState with
                {
                    EventLog = baseState.EventLog.Add(new PartyRecalled(ImmutableList.Create(hero))),
                },
                TutorialStep.EveningClose => baseState with { Day = 3 },
                TutorialStep.Commission => baseState with
                {
                    ActionLog = baseState.ActionLog.Add(new LoggedBatch(
                        3, DayPhase.Morning, ImmutableList.Create<PlayerAction>(new DeclineCommissionAction(hero)))),
                },
                _ => throw new NotSupportedException($"{def.Step}: no KTD-4 fixture wired for this step."),
            };

            AssertThat(def.IsDone(fixture))
                .OverrideFailureMessage(
                    $"{def.Step}'s IsDone did not flip true from a state built ONLY from player-caused facts " +
                    "— which means it is (still) gated on something a hero decides, or this test's own fixture " +
                    "is missing a fact the predicate actually needs.")
                .IsTrue();
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
        // This test's whole point (its own doc above) is a day-3 state where NEITHER HeroCards'
        // nor Commissions' gate has fired yet, so it can prove MainUi's override — not an earned
        // gate — is what keeps the two buttons pressable. A BARE MountMainUi() cannot reach that:
        // GameSim.Heroes.CommissionSystem posts a commission for ANY hero with an empty/sub-par
        // weapon/shield/armor slot or no carried Heal, with NO RNG — and every starting hero's
        // gear is GearSet.Empty (HeroRoster's own doc), so the very first real Morning tick below
        // (line "Morning -> Expedition") deterministically posts one, for every seed, every run.
        // That is not the scenario this test means to build. Fully kitting (and healing-supplying)
        // every starting hero closes every CommissionSystem.FindGapSlot gap up front so the sim
        // never has a reason to post one, leaving the override — not luck — to carry both steps.
        var kitted = GameComposition.NewCampaign(ScriptedSession.Seed);
        var proofWeapon = new Item(
            new ItemId(9800), "test-gap-proof", "Proof Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(1, 0, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
        var proofShield = new Item(
            new ItemId(9801), "test-gap-proof", "Proof Shield", ItemSlot.Shield, QualityGrade.Common,
            new ItemStats(0, 1, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
        var proofArmor = new Item(
            new ItemId(9802), "test-gap-proof", "Proof Armor", ItemSlot.Armor, QualityGrade.Common,
            new ItemStats(0, 1, 1), Mark: null, ImmutableList<ItemHistoryEntry>.Empty);
        var proofHeal = new Item(
            new ItemId(9803), "test-gap-proof", "Proof Salve", ItemSlot.Consumable, QualityGrade.Common,
            new ItemStats(0, 0, 0), Mark: null, ImmutableList<ItemHistoryEntry>.Empty,
            Effect: new ConsumableEffect(ConsumableKind.Heal, 10));
        kitted = kitted with
        {
            Items = kitted.Items
                .Add(proofWeapon.Id.Value, proofWeapon)
                .Add(proofShield.Id.Value, proofShield)
                .Add(proofArmor.Id.Value, proofArmor)
                .Add(proofHeal.Id.Value, proofHeal),
            Heroes = kitted.Heroes.Values
                .Select(h => h with
                {
                    Gear = new GearSet(proofWeapon.Id, proofShield.Id, proofArmor.Id),
                    Pack = h.Pack.Add(proofHeal.Id),
                })
                .ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        };

        var ui = MountMainUi(new SimAdapter(kitted));
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
                // Fixed alongside the merge's own kept predicate (TutorialFlow.CounterAnsweredAtLeastOnce,
                // U1's ActionLog-only version — see TutorialFlow.Registry's own comment on the
                // OpenCounter row): needs an OpenCounterAction in the same batch, not just Close.
                // U-T2-14 tightened the predicate further — Close alone no longer counts, so a
                // PresentItemAction (still entirely player-caused) has to sit between Open and Close.
                ActionLog = ui.Adapter.CurrentState.ActionLog.Add(
                    new LoggedBatch(2, ui.Adapter.CurrentState.Phase,
                        ImmutableList.Create<PlayerAction>(
                            new OpenCounterAction(), new PresentItemAction(craftedItemId), new CloseCounterAction()))),
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

    /// <summary>
    /// U-T2-2 (§11.13): <c>TutorialFlow.BackstopDay</c> used to be ONE constant meaning BOTH "the
    /// apprenticeship's warrant ends" and "the guided chain force-closes" — harmless while the
    /// pointed chain finished a day before that backstop, but the owner has since ruled the pointed
    /// chain now runs through day 7, and a single constant cannot serve both a fixed 3-day warrant
    /// AND a growing chain without either silently extending mortality's postponement or silently
    /// force-closing the chain mid-lesson. Split into <c>WarrantEndDay</c> (this test — unchanged
    /// meaning) and <c>ChainBackstopDay</c> (the next test — the chain's own, now-later close).
    /// Both constants are <c>private</c> — pinned here by the OBSERVABLE contract each one
    /// guarantees rather than reflection, so a future drift fails by BEHAVIOR, not a private-field
    /// peek.
    /// </summary>
    [TestCase]
    public void WarrantEndDay_EqualsWarrantLastGraceDayPlusOne()
    {
        var ui = MountMainUi(); // fresh campaign: no natural step-progression event has ever fired
        try
        {
            var stillWithinTheWarrant = ui.Adapter.CurrentState with { Day = ApprenticeWarrant.LastGraceDay };
            AssertThat(ui.Tutorial.ConsumeWarrantEndBeat(stillWithinTheWarrant))
                .OverrideFailureMessage(
                    "The warrant-end beat fired at Day == LastGraceDay, before the warrant's own dawn — WarrantEndDay drifted earlier.")
                .IsNull();

            var atWarrantEnd = ui.Adapter.CurrentState with { Day = ApprenticeWarrant.LastGraceDay + 1 };
            AssertThat(ui.Tutorial.ConsumeWarrantEndBeat(atWarrantEnd))
                .OverrideFailureMessage(
                    "The warrant-end beat did not fire at Day == LastGraceDay + 1 — WarrantEndDay has drifted from the warrant's own end.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U-T2-2: the chain's OWN unconditional close is now a SEPARATE, LATER fact than the warrant's
    /// own end — this pins both halves: it must sit strictly after <c>WarrantEndDay</c> (the chain
    /// must not force-complete the instant the warrant ends, which would silently re-collapse the
    /// two constants back into one), and it must still force-complete the chain once actually
    /// reached (day 8 — one day past the pointed chain's own day-7 close, the owner's ruling).
    /// </summary>
    [TestCase]
    public void ChainBackstopDay_IsAtLeastWarrantEndDay_AndClosesTheChainUnconditionally()
    {
        var ui = MountMainUi(); // fresh campaign: no natural step-progression event has ever fired
        try
        {
            var atWarrantEnd = ui.Adapter.CurrentState with { Day = ApprenticeWarrant.LastGraceDay + 1 };
            ui.Tutorial.Advance(atWarrantEnd);
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage(
                    "The chain force-closed the instant the warrant ended — ChainBackstopDay has collapsed back onto WarrantEndDay.")
                .IsFalse();

            var dayBeforeChainBackstop = ui.Adapter.CurrentState with { Day = 7 };
            ui.Tutorial.Advance(dayBeforeChainBackstop);
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("The chain force-closed before its own day-8 backstop.")
                .IsFalse();

            var atChainBackstop = ui.Adapter.CurrentState with { Day = 8 };
            ui.Tutorial.Advance(atChainBackstop);
            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("ChainBackstopDay did not force-complete the chain at Day 8.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U-T2-15 (#162 defect 2): the counter step's anchor is a real physical station now
    /// (<see cref="TutorialAnchor.ForStation"/>), not the market's own door — <see
    /// cref="Registry_EveryStationAnchor_ResolvesAgainstARealRoomStation"/> already covers every
    /// Station anchor resolving against a real room station; this pins the SPECIFIC join — the
    /// counter step names the counter, never the building's front door (<c>TutorialAnchorKind.Building</c>).
    /// </summary>
    [TestCase]
    public void TheCounterStep_PointsAtTheCounterStation_NotTheMarketDoor()
    {
        var def = TutorialFlow.Registry.Single(d => d.Step == TutorialStep.OpenCounter);
        AssertThat(def.Anchor.Kind)
            .OverrideFailureMessage("OpenCounter's anchor is still a Building (the market's door), not a Station.")
            .IsEqual(TutorialAnchorKind.Station);
        AssertThat(def.Anchor.Key).IsEqual("market");
        AssertThat(def.Anchor.StationId)
            .OverrideFailureMessage("OpenCounter's Station anchor does not name the counter station specifically.")
            .IsEqual("counter");

        var ui = MountMainUi();
        try
        {
            var station = ui.Town.FindStation(def.Anchor.Key!, def.Anchor.StationId!);
            AssertThat(station)
                .OverrideFailureMessage("OpenCounter's counter station does not resolve against the real Shop room.")
                .IsNotNull();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
