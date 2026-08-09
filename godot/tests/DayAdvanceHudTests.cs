#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U7 (R11/R12/KD1): the themed HUD header — the real home for the hybrid day
/// clock. Both the <c>AdvancePhase</c> button and the <c>AutoAdvance</c> toggle must
/// drive the SAME gated advance (<see cref="PhaseClock.AdvanceNow"/> /
/// <see cref="PhaseClock.Update"/> -> <see cref="SimAdapter.AdvancePhase"/>) — never a
/// second code path (KD1) — and the stat-chip row must stay live. The rejection banner's
/// full transience matrix (timeout AND clean-tick clearing, player-phrased text, raw
/// string never rendered) is already covered by <see cref="RejectionUxTests"/>; the one
/// scenario here drives the SAME rejection through the real HUD Advance button instead
/// of a direct Adapter call, proving the banner is wired to the control a player clicks.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DayAdvanceHudTests
{
    [TestCase]
    public void AdvanceButton_MorningBell_TicksExactlyOnePhase()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            // Morning keeps a real bell (U1, KTD-A) — one press, one tick, same as always.
            PressEnabled(ui, "AdvancePhase");
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U1 (plan 2026-08-03-001, KTD-A): once the conductor owns the span, the SAME "AdvancePhase"
    /// control is Hurry — a skip-to-the-next-stop, not a one-tick bell — and can legitimately tick
    /// MULTIPLE phases from one press. A fresh Day-1 campaign is guaranteed unstaged (every hero's
    /// first-ever trip targets floor 1, which is structurally below the staging checkpoint —
    /// <c>ExpeditionSystem.CheckpointFor</c>), so nobody parks and there is no vigil stop between
    /// Expedition and Evening: one Hurry press reaches Evening directly, the plan's own "an empty
    /// Camp/Deep costs ~a second of show, not a click."
    /// </summary>
    [TestCase]
    public void AdvanceButton_DuringTheRaidSpan_IsHurry_ReachesEveningInOnePress_WhenNobodyParks()
    {
        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "AdvancePhase"); // Morning -> Expedition (the real bell)
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            PressEnabled(ui, "AdvancePhase"); // Hurry now — not a bell
            AssertThat(ui.Adapter.CurrentState.InFlight.IsEmpty)
                .OverrideFailureMessage("Fixture premise failed: someone parked on a fresh day 1.")
                .IsTrue();
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Hurry on an unstaged day must reach Evening in one press — nothing between SendOff and Idle stops it.")
                .IsEqual(DayPhase.Evening);
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.Idle);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AutoToggle_FiresSameAdvanceOnTimer_DisablingStopsIt_ForTheMorningPhaseItGates()
    {
        var ui = MountMainUi();
        try
        {
            // Gated by default: MountMainUi pauses the clock and auto starts OFF, so an
            // arbitrarily large frame delta through the real _Process path is a no-op.
            AssertThat(ui.Clock.AutoAdvance).IsFalse();
            ui._Process(PhaseClock.MorningSeconds * 10);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            // Opt in through the real controls: Auto toggle, then Play (MountMainUi
            // paused the clock so the timer needs an explicit resume).
            PressEnabled(ui, "AutoAdvance");
            AssertThat(ui.Clock.AutoAdvance).IsTrue();
            PressEnabled(ui, "PlayPause");
            AssertThat(ui.Clock.Playing).IsTrue();

            // One frame >= the phase duration drives exactly one tick through PhaseClock.
            // Update -> the SAME SimAdapter.AdvancePhase the AdvancePhase button calls. U1
            // (KTD-A): MainUi._Process makes Clock.Update and Conductor.Update mutually
            // exclusive per frame (Conductor.Current == Idle gates which one runs) — exactly
            // ONE tick happens here, landing at Expedition, never cascading into the
            // conductor's own SendOff beat in the same frame.
            ui._Process(PhaseClock.MorningSeconds);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

            // Disabling auto stops the TIMER cold — but only for the phase it ever gated in the
            // first place. It never governed the raid span: RaidConductor.Update runs from
            // _Process independent of Clock.AutoAdvance (U1's own "why this is not the rejected
            // living clock" — the conductor is not a phase timer with an opt-out, the phases it
            // owns have no player verb to protect), so huge deltas keep walking the now-armed
            // SendOff beat forward regardless of this toggle. A fresh Day-1 campaign is
            // guaranteed unstaged (every hero's first trip targets floor 1, structurally below
            // the staging checkpoint), so nobody parks and this reaches Evening with zero further
            // player input.
            PressEnabled(ui, "AutoAdvance");
            AssertThat(ui.Clock.AutoAdvance).IsFalse();

            // 2026-08-09: the apprenticeship chain now HOLDS the raid span open at its Watch step
            // (RaidConductor's own hold doc), and a fresh mount is always mid-chain. This test is
            // about what the Auto TOGGLE does and does not gate, not about the tutorial — dismissing
            // the chain keeps that the only variable. PlayableLoopTests pins the hold itself.
            ui.Tutorial.Dismiss();

            for (var frame = 0; frame < 8 && ui.Conductor.Current != RaidConductor.Beat.Idle; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.InFlight.IsEmpty)
                .OverrideFailureMessage("Fixture premise failed: someone parked on a fresh day 1.")
                .IsTrue();
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("The raid span must keep advancing via _Process even with Clock.AutoAdvance disabled — it was never gated by that toggle.")
                .IsEqual(DayPhase.Evening);
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.Idle);

            // NOW Evening is a real bell phase again, and IT is gated by AutoAdvance being off:
            // further huge deltas are harmless here, same as Morning was at the top of this test.
            for (var frame = 0; frame < 5; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Advance_RejectedAction_ShowsPlayerPhrasedBanner_ClearsOnNextCleanAdvance()
    {
        var ui = MountMainUi();
        try
        {
            // Queue a doomed action (no handler accepts an ore buy at Morning), then drive
            // it through the SAME HUD control a player clicks — not a direct Adapter call.
            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), ScriptedSession.CraftMaterial, 1));
            PressEnabled(ui, "AdvancePhase");

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(1);
            var rendered = RenderedText(ui);
            AssertThat(rendered).Contains("Can't do that right now.");
            AssertThat(rendered.Contains("REJECTED:")).IsFalse();

            // The next clean advance — same Advance button — clears the banner early,
            // without waiting out the wall-clock toast timeout.
            PressEnabled(ui, "AdvancePhase");
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);
            AssertThat(RenderedText(ui).Contains("Can't do that right now.")).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StatChips_ReflectLiveDayPhaseGoldHeroes_AfterTick()
    {
        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "AdvancePhase");
            var state = ui.Adapter.CurrentState;
            var alive = state.Heroes.Values.Count(h => h.Alive);

            // U2 (playtest-three plan): the chip reads PhaseVocab now, not the raw enum.
            AssertThat(RenderedText(Find<Control>(ui, "DayChip"))).Contains($"{state.Day}");
            AssertThat(RenderedText(Find<Control>(ui, "PhaseChip"))).Contains(PhaseVocab.Display(state));
            AssertThat(RenderedText(Find<Control>(ui, "GoldChip"))).Contains($"{state.Player.Gold}g");
            AssertThat(RenderedText(Find<Control>(ui, "HeroesChip"))).Contains($"{alive}/{state.Heroes.Count}");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// U5 (world-and-interiors plan): migrated verbatim from the deleted <c>ShopStageTests</c> —
    /// this test exercises <c>MainUi</c>'s own gold-chip bounce-scale pop (LW3), which reads the
    /// SAME <c>Adapter.LastEvents</c> batch <c>ShopStage</c>/<c>MarketLife2D</c> stage their
    /// choreography from but has no dependency on either — retiring <c>ShopStage</c> does not
    /// touch this coverage, so it moves here rather than being deleted along with its old home.
    /// </summary>
    [TestCase]
    public void MorningSale_PopsTheGoldChip()
    {
        var ui = MountMainUi(new SimAdapter(GuaranteedSaleState()));
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            var goldBefore = ui.Adapter.CurrentState.Player.Gold;

            ui.Adapter.AdvancePhase(); // Morning: the sale lands (OnPhaseCompleted stages the run)

            var sale = ui.Adapter.LastEvents.OfType<ItemSold>().FirstOrDefault(s => s.FromPlayerShop);
            AssertThat(sale).IsNotNull();
            AssertThat(ui.Adapter.CurrentState.Player.Gold).IsGreater(goldBefore);

            // Gold-pop tween property assertions (accumulated-delta, no engine Tween): the
            // StatusBar's gold VALUE label bounces 1.0 -> ~1.25 -> 1.0 over 0.3s.
            var goldValue = Find<Label>(Find<HBoxContainer>(ui, "GoldChip"), "Value");
            AssertThat(goldValue.Scale).IsEqual(Vector2.One);

            ui._Process(0.15); // mid-pop
            AssertThat(goldValue.Scale.X).IsGreater(1.05f);

            ui._Process(0.15); // pop completes
            AssertThat(goldValue.Scale).IsEqual(Vector2.One);
            AssertThat(goldValue.Text).Contains($"{ui.Adapter.CurrentState.Player.Gold}g");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// A real starting campaign (full composition — faction drift, restock, recruit, gossip all
    /// run) with every starting hero's Gear cleared and Gold bumped so the first shopper in
    /// HeroId order provably buys the shelved item: its value ratio (gain/price = 5/8 = 0.625)
    /// strictly beats every RivalCatalog line's constant 0.5 ratio ((Atk+Def)*2 pricing against a
    /// zero starting gear score), so no rival-shelf item can win the "single best across both
    /// shelves" comparison regardless of which class shops first. Migrated verbatim from the
    /// deleted <c>ShopStageTests.GuaranteedSaleState</c>.
    /// </summary>
    private static GameState GuaranteedSaleState()
    {
        var baseState = GameComposition.NewCampaign(9002);
        var item = new Item(
            new ItemId(9001), "test-guaranteed-sale", "Test Blade", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(Attack: 5, Defense: 0, Weight: 2), new MakersMark("You", 1),
            ImmutableList<ItemHistoryEntry>.Empty);

        var heroes = baseState.Heroes.Values
            .Select(h => h with { Gold = 500, Gear = GearSet.Empty })
            .ToImmutableSortedDictionary(h => h.Id.Value, h => h);

        return baseState with
        {
            Heroes = heroes,
            RivalShelf = ImmutableList<ShelfEntry>.Empty,
            Items = baseState.Items.Add(item.Id.Value, item),
            Player = baseState.Player with { Shelf = ImmutableList.Create(new ShelfEntry(item.Id, 8)) },
        };
    }
}
#endif
