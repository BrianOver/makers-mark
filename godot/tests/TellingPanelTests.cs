#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;
using GameSim.Venues;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-PROOF-03..07 (§11.15): <see cref="TellingPanel"/> — link 4's counterfactual proof staged
/// instead of printed as one ledger line. Every "expected number" scenario here derives its ground
/// truth from <see cref="TellingQuery.Build"/> itself (the SAME pattern <c>TellingQueryTests</c>
/// uses against <c>CombatMath</c>) rather than a hand-typed constant — this suite's job is proving
/// the panel is a FAITHFUL RENDERER of whatever the query already proved, never re-verifying the
/// query's own arithmetic (out of scope here; <c>sim/</c> is untouched).
///
/// <para>Standalone <see cref="TellingPanel"/> instances (never added to a scene tree) follow
/// <c>SettingsPanelTests</c>/<c>MineWatchTests</c>'s own "construct, try/finally Free()" idiom —
/// never a bare unfreed node, per this repo's own orphan-node-leak lesson.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TellingPanelTests
{
    private const int Day = 1;
    private const int Floor = 3; // Mine floor 3: MonsterAttack 23, MonsterHp 42 (VenueRegistry.BuildMine)
    private static readonly HeroId Hero = new(9001);
    private static readonly ItemId ArmorId = new(9101);

    // ── Fixture: the flagship LethalSave night (three recorded rounds) ──────────────────────────

    private static (GameState State, ExpeditionResult Result, AttributionBeatEvent BeatEvent) LethalSaveNight()
    {
        var item = new Item(
            ArmorId, "recipe-test-armor", "Emberbite", ItemSlot.Armor, QualityGrade.Fine,
            new ItemStats(0, 6, 5), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var departure = new HeroAtDeparture(Hero, "Torvald", "vanguard", Level: 3, MaxHp: 24, Weapon: null, Shield: null, Armor: ArmorId);

        // Round 1: ordinary exchange, nothing lethal -- genuine mid-play (two more rounds follow).
        var round1 = new CombatEvent(
            Floor, Hero, "Deep Ghoul", ImmutableList.Create(4, 2), DamageDealt: 4, DamageTaken: 3, MonsterKilled: false, KillingItem: null);
        // Round 2: the lethal-save round. Real Mine floor-3 numbers: MonsterAttack 23, Torvald's
        // own Defense 3 (Level 3, no shield) without Emberbite, 9 with its +6 Defense. Roll 1:
        // without the armor the blow reads 23+1-3=21 -- exactly the 21 hp Torvald carries into this
        // round, so he falls (<=0). WITH it, the recorded 9 taken leaves him standing at 12.
        var round2 = new CombatEvent(
            Floor, Hero, "Deep Ghoul", ImmutableList.Create(3, 1), DamageDealt: 3, DamageTaken: 9, MonsterKilled: false, KillingItem: null);
        // Round 3: Torvald finishes the fight -- one recorded roll (a kill round is never padded).
        var round3 = new CombatEvent(
            Floor, Hero, "Deep Ghoul", ImmutableList.Create(5), DamageDealt: 35, DamageTaken: 0, MonsterKilled: true, KillingItem: null);

        var floorOutcome = new FloorOutcome(Floor, Cleared: true, ImmutableList.Create(round1, round2, round3));
        var beat = new AttributionBeat(BeatType.LethalSave, ArmorId, Hero, Floor, "Emberbite turned the killing blow");
        var result = new ExpeditionResult(
            ImmutableList.Create(Hero), Floor, Floor, ImmutableList.Create(floorOutcome),
            ImmutableList.Create(Hero), ImmutableList<HeroId>.Empty, ImmutableList.Create(beat),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(departure),
        };

        var beatEvent = new AttributionBeatEvent(beat.Beat, beat.Item, beat.Hero, beat.Floor, beat.Detail)
            with { Id = new EventId(80001), Day = Day };
        var returned = new PartyReturned(ImmutableList.Create(Hero)) with { Id = new EventId(80002), Day = Day };
        var departed = new PartyDeparted(ImmutableList.Create(Hero), TargetFloor: Floor) with { Id = new EventId(80003), Day = Day };

        var baseState = GameFactory.NewGame(9001);
        var state = baseState with
        {
            Items = baseState.Items.SetItem(ArmorId.Value, item),
            EventLog = baseState.EventLog.AddRange([beatEvent, returned, departed]),
            LastNightExpeditions = ImmutableList.Create(result),
        };

        return (state, result, beatEvent);
    }

    // ── Full journey: Framing -> Factual x3 -> Fork -> Fall -> Verdict ──────────────────────────

    [TestCase]
    public void LethalSave_FactualStage_ShowsRecordedRollsAndSnappedHp_MidPlay()
    {
        var (state, result, beatEvent) = LethalSaveNight();
        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Framing);

            panel.Dev_Advance(1); // Framing -> Factual, round index 0 (round 1 of 3)
            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Factual);

            var text = RenderedText(panel);
            AssertThat(text).Contains("Round 1 of 3");
            // Round 1's own recorded facts: hero roll 4, monster roll 2, dealt 4, taken 3, hp 24-3=21.
            AssertThat(Find<Label>(panel, "TellingHeroHp").Text).IsEqual("21 HP");
            AssertThat(Find<Label>(panel, "TellingMonsterHp").Text).IsEqual("38 HP"); // 42 - 4

            // A NORMAL (non-kill) round carries a monster roll and a "taken" chip -- absence is
            // only for kill rounds (asserted separately below).
            AssertThat(text).Contains("Monster roll");
            AssertThat(text).Contains("Taken");
        }
        finally
        {
            panel.Free();
        }
    }

    [TestCase]
    public void LethalSave_KillRound_RendersOneRollAndNoMonsterFlinch()
    {
        var (state, result, beatEvent) = LethalSaveNight();
        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            panel.Dev_Advance(3); // Framing -> round0 -> round1 -> round2 (the kill round, index 2)

            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Factual);
            var text = RenderedText(panel);
            AssertThat(text).Contains("Round 3 of 3");
            AssertThat(Find<Label>(panel, "TellingMonsterHp").Text).IsEqual("Defeated");
            // Rule: "kill rounds have one roll, not two ... render that absence as absence -- no
            // chip, no flinch." The kill round's own recorded rolls list has exactly one entry, so
            // neither a second dice chip nor a "Taken" chip renders for it.
            AssertThat(text).NotContains("Monster roll");
            AssertThat(text).NotContains("Taken");
        }
        finally
        {
            panel.Free();
        }
    }

    [TestCase]
    public void LethalSave_ForkStage_HoldsLastFrameAndDesaturates()
    {
        var (state, result, beatEvent) = LethalSaveNight();
        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            panel.Dev_Advance(4); // Framing -> 3 factual rounds -> Fork

            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Fork);
            var duelRow = Find<HBoxContainer>(panel, "TellingDuelRow");
            AssertThat(duelRow.Modulate.R).IsLess(1f); // desaturated tint, not full white
            var text = RenderedText(panel);
            AssertThat(text).Contains("Same roll. No armor.");
            // The fork still shows the LAST FACTUAL round (round 3) held, not a new one.
            AssertThat(text).Contains("Round 3 of 3");
        }
        finally
        {
            panel.Free();
        }
    }

    [TestCase]
    public void LethalSave_FallStage_SnapsHpToTheCounterfactualDivergenceRound_NeverPastIt()
    {
        var (state, result, beatEvent) = LethalSaveNight();
        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            panel.Dev_Advance(5); // ... -> Fork -> Fall

            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Fall);
            AssertThat(Find<Label>(panel, "TellingHeroHp").Text).IsEqual("Fallen"); // HeroHpAfterWithoutItem == 0
            AssertThat(Find<Label>(panel, "TellingRoundLabel").Text).IsEqual("Round 2 -- without it");
            AssertThat(RenderedText(panel)).Contains("Torvald falls. The rest of that night never happens.");

            // The event feed this panel actually drew: exactly one counterfactual entry, at the
            // divergence round (2) -- never a synthesized round past it.
            var counterfactualEntries = panel.RenderLog.Where(e => e.Counterfactual).ToList();
            AssertThat(counterfactualEntries.Count).IsEqual(1);
            AssertThat(counterfactualEntries[0].Round).IsEqual(2);

            var logCountAtFall = panel.RenderLog.Count;
            panel.Dev_Advance(1); // Fall -> Verdict
            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Verdict);
            // Verdict draws no duel frame at all -- the render log is unchanged.
            AssertThat(panel.RenderLog.Count).IsEqual(logCountAtFall);
        }
        finally
        {
            panel.Free();
        }
    }

    [TestCase]
    public void LethalSave_VerdictStage_StampsAndPrintsTheMarginNumbers()
    {
        var (state, result, beatEvent) = LethalSaveNight();
        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            panel.Dev_Advance(6); // ... -> Verdict

            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Verdict);
            var text = RenderedText(panel);
            AssertThat(text).Contains("MAKER'S MARK");
            AssertThat(text).Contains("Emberbite turned the killing blow on floor 3. Torvald lives.");
            // The payload's own named margin numbers (RawBlow 24, ItemDefenseStat 6, HeroHpAfterWithItem 12).
            AssertThat(text).Contains("The blow read 24");
            AssertThat(text).Contains("Emberbite drank 6 of it");
            AssertThat(text).Contains("Torvald stood at 12");
            AssertThat(Find<Button>(panel, "TellingAdvance").Visible).IsFalse(); // terminal -- Close only
        }
        finally
        {
            panel.Free();
        }
    }

    // ── No credit / honest downgrade shapes ──────────────────────────────────────────────────────

    [TestCase]
    public void Provisioned_NoCounterfactualPass_SaysSoOutLoud_NoParticipationCredit()
    {
        var itemId = new ItemId(9102);
        var item = new Item(
            itemId, "recipe-test-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
            new ItemStats(0, 0, 0), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty,
            new ConsumableEffect(ConsumableKind.Heal, 5));
        var departure = new HeroAtDeparture(Hero, "Elowen", "vanguard", Level: 2, MaxHp: 30, Weapon: null, Shield: null, Armor: null);
        var use = new ConsumableUse(itemId, Round: 1, HpBefore: 20, HpAfter: 25);
        var round1 = new CombatEvent(Floor, Hero, "Deep Ghoul", ImmutableList.Create(3, 2), DamageDealt: 3, DamageTaken: 2, MonsterKilled: false, KillingItem: null)
        {
            Uses = ImmutableList.Create(use),
        };
        var round2 = new CombatEvent(Floor, Hero, "Deep Ghoul", ImmutableList.Create(6), DamageDealt: 20, DamageTaken: 0, MonsterKilled: true, KillingItem: null);
        var floorOutcome = new FloorOutcome(Floor, Cleared: true, ImmutableList.Create(round1, round2));
        var beat = new AttributionBeat(BeatType.Provisioned, itemId, Hero, Floor, "Field Salve kept her fighting");
        var result = new ExpeditionResult(
            ImmutableList.Create(Hero), Floor, Floor, ImmutableList.Create(floorOutcome),
            ImmutableList.Create(Hero), ImmutableList<HeroId>.Empty, ImmutableList.Create(beat),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(departure),
        };
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(itemId.Value, item);
        var script = TellingQuery.Build(result, beat, items, VenueRegistry.Mine);
        var payload = (ProvisionedPayload)script.Payload;

        var beatEvent = new AttributionBeatEvent(beat.Beat, beat.Item, beat.Hero, beat.Floor, beat.Detail) with { Id = new EventId(80101), Day = Day };
        var baseState = GameFactory.NewGame(9002);
        var state = baseState with
        {
            Items = items,
            EventLog = baseState.EventLog.Add(beatEvent),
            LastNightExpeditions = ImmutableList.Create(result),
        };

        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            panel.Dev_Advance(3); // Framing -> round0 -> round1(last) -> Verdict (no counterfactual pass)

            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Verdict);
            var text = RenderedText(panel);
            AssertThat(text).Contains("but it would have run the same without it");
            AssertThat(text).Contains("No credit taken");
            AssertThat(text).Contains($"{payload.NaiveHpWithoutHeal}");
            // No stamp ceremony for the no-credit case.
            AssertThat(text).NotContains("MAKER'S MARK");
        }
        finally
        {
            panel.Free();
        }
    }

    [TestCase]
    public void KillingBlow_HonestEpilogue_NoDishonestSecondPass()
    {
        var itemId = new ItemId(9103);
        var item = new Item(
            itemId, "recipe-test-sword", "Fine Shortsword", ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(40, 0, 4), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var departure = new HeroAtDeparture(Hero, "Brannis", "vanguard", Level: 3, MaxHp: 20, Weapon: itemId, Shield: null, Armor: null);
        var round1 = new CombatEvent(Floor, Hero, "Deep Ghoul", ImmutableList.Create(4), DamageDealt: 50, DamageTaken: 0, MonsterKilled: true, KillingItem: itemId);
        var floorOutcome = new FloorOutcome(Floor, Cleared: true, ImmutableList.Create(round1));
        var beat = new AttributionBeat(BeatType.KillingBlow, itemId, Hero, Floor, "Fine Shortsword turned the killing blow");
        var result = new ExpeditionResult(
            ImmutableList.Create(Hero), Floor, Floor, ImmutableList.Create(floorOutcome),
            ImmutableList.Create(Hero), ImmutableList<HeroId>.Empty, ImmutableList.Create(beat),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(departure),
        };
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(itemId.Value, item);
        var script = TellingQuery.Build(result, beat, items, VenueRegistry.Mine);
        var payload = (KillingBlowPayload)script.Payload;
        AssertThat(script.CounterfactualTail.IsEmpty).IsTrue(); // no second pass for this shape

        var beatEvent = new AttributionBeatEvent(beat.Beat, beat.Item, beat.Hero, beat.Floor, beat.Detail) with { Id = new EventId(80201), Day = Day };
        var baseState = GameFactory.NewGame(9003);
        var state = baseState with
        {
            Items = items,
            EventLog = baseState.EventLog.Add(beatEvent),
            LastNightExpeditions = ImmutableList.Create(result),
        };

        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            panel.Dev_Advance(2); // Framing -> round0(last, one round) -> Verdict directly (no Fork/Fall)

            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Verdict);
            var text = RenderedText(panel);
            AssertThat(text).Contains("There the record ends. No one rolled what comes next.");
            AssertThat(text).Contains($"the beast still stands at {payload.MonsterHpWithoutItem}");
            AssertThat(text).Contains("MAKER'S MARK"); // a recorded kill still earns the stamp
        }
        finally
        {
            panel.Free();
        }
    }

    [TestCase]
    public void MarginOnly_HonestDowngrade_NeverStagesADeathTheReplayDisproves()
    {
        var itemId = new ItemId(9104);
        var laterItemId = new ItemId(9105);
        var item = new Item(
            itemId, "recipe-test-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
            new ItemStats(0, 0, 0), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty,
            new ConsumableEffect(ConsumableKind.Heal, 8));
        var laterItem = item with { Id = laterItemId };
        var departure = new HeroAtDeparture(Hero, "Selwyn", "vanguard", Level: 2, MaxHp: 20, Weapon: null, Shield: null, Armor: null);

        var use1 = new ConsumableUse(itemId, Round: 1, HpBefore: 10, HpAfter: 18);
        var round1 = new CombatEvent(Floor, Hero, "Deep Ghoul", ImmutableList.Create(2, 5), DamageDealt: 2, DamageTaken: 10, MonsterKilled: false, KillingItem: null)
        {
            Uses = ImmutableList.Create(use1),
        };
        // Round 2: a LATER, independent quaff keeps Selwyn alive even with round 1's salve removed
        // -- the strict replay never crosses zero, so this downgrades rather than stages a death
        // the replay itself disproves (finding 5).
        var use2 = new ConsumableUse(laterItemId, Round: 2, HpBefore: 4, HpAfter: 16);
        var round2 = new CombatEvent(Floor, Hero, "Deep Ghoul", ImmutableList.Create(3, 6), DamageDealt: 3, DamageTaken: 12, MonsterKilled: false, KillingItem: null)
        {
            Uses = ImmutableList.Create(use2),
        };
        var round3 = new CombatEvent(Floor, Hero, "Deep Ghoul", ImmutableList.Create(7), DamageDealt: 20, DamageTaken: 0, MonsterKilled: true, KillingItem: null);
        var floorOutcome = new FloorOutcome(Floor, Cleared: true, ImmutableList.Create(round1, round2, round3));
        var beat = new AttributionBeat(BeatType.PotionLifesave, itemId, Hero, Floor, "Field Salve kept her alive");
        var result = new ExpeditionResult(
            ImmutableList.Create(Hero), Floor, Floor, ImmutableList.Create(floorOutcome),
            ImmutableList.Create(Hero), ImmutableList<HeroId>.Empty, ImmutableList.Create(beat),
            ImmutableList<OreLoot>.Empty, ImmutableSortedDictionary<int, int>.Empty)
        {
            PartyAtDeparture = ImmutableList.Create(departure),
        };
        var items = ImmutableSortedDictionary<int, Item>.Empty.Add(itemId.Value, item).Add(laterItemId.Value, laterItem);
        var script = TellingQuery.Build(result, beat, items, VenueRegistry.Mine);
        AssertThat(script.Shape).IsEqual(TellingShape.MarginOnly); // the fixture actually hits the downgrade
        var payload = (MarginOnlyPayload)script.Payload;

        var beatEvent = new AttributionBeatEvent(beat.Beat, beat.Item, beat.Hero, beat.Floor, beat.Detail) with { Id = new EventId(80301), Day = Day };
        var baseState = GameFactory.NewGame(9004);
        var state = baseState with
        {
            Items = items,
            EventLog = baseState.EventLog.Add(beatEvent),
            LastNightExpeditions = ImmutableList.Create(result),
        };

        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(state, result, beatEvent);
            panel.Dev_Advance(4); // Framing -> 3 rounds -> Verdict (MarginOnly has no counterfactual pass)

            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Verdict);
            var text = RenderedText(panel);
            AssertThat(text).Contains("the strict replay says otherwise");
            AssertThat(text).Contains("No credit taken");
            AssertThat(text).Contains($"{payload.MinHpReached}");
            AssertThat(text).NotContains("MAKER'S MARK");
        }
        finally
        {
            panel.Free();
        }
    }

    // ── Framing: the wiped-party teller ──────────────────────────────────────────────────────────

    [TestCase]
    public void WipedParty_FramingLine_IsTheWinchKeeper_NotASurvivor()
    {
        var (state, result, beatEvent) = LethalSaveNight();
        var wiped = result with { Survivors = ImmutableList<HeroId>.Empty, Deaths = ImmutableList.Create(Hero) };
        var wipedState = state with { LastNightExpeditions = ImmutableList.Create(wiped) };

        var panel = new TellingPanel();
        try
        {
            panel.ShowFor(wipedState, wiped, beatEvent);

            var text = RenderedText(panel);
            AssertThat(text).Contains("Nobody came up to tell it. The winch-keeper reads the ledger the way the ledger wrote it.");
        }
        finally
        {
            panel.Free();
        }
    }

    // ── Availability: no button, no telling, when the query can't stage it ─────────────────────

    [TestCase]
    public void FindResult_ReturnsNull_WhenTheNightHasRolledOutOfRetention()
    {
        var (state, _, beatEvent) = LethalSaveNight();
        var emptyNightState = state with { LastNightExpeditions = ImmutableList<ExpeditionResult>.Empty };

        AssertThat(TellingPanel.FindResult(emptyNightState, beatEvent)).IsNull();
    }

    [TestCase]
    public void IsAvailable_FalseForToolAssist_TheOneBeatTypeWithNoEmitterYet()
    {
        AssertThat(TellingPanel.IsAvailable(BeatType.ToolAssist)).IsFalse();
        AssertThat(TellingPanel.IsAvailable(BeatType.KillingBlow)).IsTrue();
        AssertThat(TellingPanel.IsAvailable(BeatType.LethalSave)).IsTrue();
        AssertThat(TellingPanel.IsAvailable(BeatType.BreakpointClear)).IsTrue();
        AssertThat(TellingPanel.IsAvailable(BeatType.Provisioned)).IsTrue();
        AssertThat(TellingPanel.IsAvailable(BeatType.PotionLifesave)).IsTrue();
    }

    // ── Wired through the real ledger button, in the real mounted tree ──────────────────────────

    [TestCase]
    public void LedgerBeatRow_AskHowItHappened_OpensTheTellingPanel_ThroughTheRealClick()
    {
        var (state, _, beatEvent) = LethalSaveNight();
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Ledger.ShowFor(Day);
            PressEnabled(ui.Ledger, $"AskHowItHappened_{beatEvent.Id.Value}");

            var panel = Find<TellingPanel>(ui.Ledger, "TellingPanel");
            AssertThat(panel.Visible).IsTrue();
            AssertThat(panel.CurrentStage).IsEqual(TellingPanel.TellingStage.Framing);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The plan's own proof requirement reads "exactly one SubViewport in the whole mounted tree" —
    /// measured here to be WRONG about the baseline: <c>MainUi</c> already mounts more than one
    /// before this panel exists at all (<c>UiTestSupport.DisableAllRendering</c>'s own doc names
    /// Town's WorldViewport plus a second one <c>MineWatch</c>'s constructor builds, "MineViewport").
    /// The honest form of the same requirement — the one this test actually proves — is that
    /// <see cref="TellingPanel"/> adds NONE of its own: the count taken before this panel ever opens
    /// must equal the count with it open and walked all the way to <see cref="TellingPanel.TellingStage.Verdict"/>.
    /// </summary>
    [TestCase]
    public void TellingPanel_AddsNoSubViewportOfItsOwn_ToWhateverTheMountedTreeAlreadyCarries()
    {
        var (state, _, beatEvent) = LethalSaveNight();
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            var baselineSubViewports = CountSubViewports(ui);

            ui.Ledger.ShowFor(Day);
            PressEnabled(ui.Ledger, $"AskHowItHappened_{beatEvent.Id.Value}");
            var panel = Find<TellingPanel>(ui.Ledger, "TellingPanel");
            panel.Dev_Advance(6); // walk it all the way to Verdict

            var withTellingOpen = CountSubViewports(ui);
            AssertThat(withTellingOpen)
                .OverrideFailureMessage(
                    $"Baseline mounted tree carried {baselineSubViewports} SubViewport(s); with the Telling " +
                    $"open and walked to Verdict it carries {withTellingOpen}. TellingPanel must stay a plain " +
                    "Control tree (headless-hang hazard) -- it must add zero, regardless of what MineWatch/Town already mount.")
                .IsEqual(baselineSubViewports);
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static int CountSubViewports(Node root)
    {
        var count = root is SubViewport ? 1 : 0;
        foreach (var child in root.GetChildren())
        {
            count += CountSubViewports(child);
        }

        return count;
    }

    // ── Source census: one creation site, no Tween, MainUi never initiates ─────────────────────

    private static readonly Lazy<string> AllGodotScriptSource = new(ReadAllGodotScriptSource);

    /// <summary>Same fixture/guard idiom as <c>TeachingCoverageCensusTests</c>/<c>FireOnOpenRetiredTests</c>
    /// — a broken <see cref="ProjectSettings.GlobalizePath"/> would silently scan zero files and make
    /// every check below pass by finding nothing to contradict it.</summary>
    private static string ReadAllGodotScriptSource()
    {
        var scriptsDir = ProjectSettings.GlobalizePath("res://scripts");
        var files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);
        if (files.Length < 100)
        {
            throw new InvalidOperationException(
                $"Only found {files.Length} .cs files under {scriptsDir} -- too few to trust a source scan against.");
        }

        return string.Join("\n---FILE---\n", files.Select(File.ReadAllText));
    }

    /// <summary>
    /// The button's REAL creation site — an <c>AddButton</c> call whose verb argument is the exact
    /// label, immediately followed by the shared no-gate <c>Verdict.Ok</c> every such call passes —
    /// not a bare substring count, which also matches this class's OWN doc comments quoting the
    /// label for readers (two of them, verified present) and would false-positive on legitimate
    /// documentation. <c>", Verdict.Ok"</c> right after the closing quote is a call-site shape no
    /// prose sentence produces.
    /// </summary>
    [TestCase]
    public void AskHowItHappened_HasExactlyOneCreationSite()
    {
        var count = CountOccurrences(AllGodotScriptSource.Value, "\"Ask how it happened.\", Verdict.Ok");
        AssertThat(count)
            .OverrideFailureMessage(
                $"Found {count} creation sites for the \"Ask how it happened.\" button -- the plan requires " +
                "exactly one (LedgerModal's beat row) so the game never initiates the Telling itself.")
            .IsEqual(1);
    }

    /// <summary>Denominator guard: the label is genuinely quoted more than once in source (two doc
    /// comments plus the one real call) — proves the call-site-shaped check above is doing real
    /// narrowing, not passing merely because there is only one mention to find.</summary>
    [TestCase]
    public void AskHowItHappened_LabelAppearsInDocsToo_TheCallSiteCheckStillNarrowsToOne()
    {
        var bareMentions = CountOccurrences(AllGodotScriptSource.Value, "Ask how it happened.");
        AssertThat(bareMentions).IsGreaterEqual(2);
    }

    [TestCase]
    public void MainUi_NeverReferencesTellingPanel_NeverOpensItUnprompted()
    {
        var mainUiPath = ProjectSettings.GlobalizePath("res://scripts/MainUi.cs");
        var mainUiSource = File.ReadAllText(mainUiPath);
        AssertThat(mainUiSource).NotContains("TellingPanel");
    }

    [TestCase]
    public void TellingPanel_Source_NeverCreatesAnEngineTween()
    {
        var panelPath = ProjectSettings.GlobalizePath("res://scripts/panels/TellingPanel.cs");
        var source = File.ReadAllText(panelPath);
        AssertThat(source).NotContains("CreateTween");
        AssertThat(source).NotContains("new Tween");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
#endif
