#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// V7a engine-lane scenarios: the winch-house camp slate binds real sim state through the ONE
/// adapter and every verb goes through real Controls. It auto-opens when a party parks at Camp,
/// lists each camped hero's hp/heals off the live <see cref="InFlightExpedition"/>, queues the
/// exact <see cref="SendSupplyAction"/>/<see cref="RecallPartyAction"/> the kernel accepts, and
/// renders <c>TickResult.Rejected</c> reasons verbatim (AE4). The panel never enforces a rule.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CampPanelTests
{
    // Seed 6 parks a strong vanguard party at the floor-1 checkpoint (CampHandlersTests precedent).
    private const ulong CampSeed = 6;
    private const int SalveId = 50;
    private const int Floor1Fee = 9; // SupplyFeeBase 6 + SupplyFeePerFloor 3 × checkpoint 1 (CampHandlers)

    // ── Fixtures (mirror CampHandlersTests) ─────────────────────────────────────────────────

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

    /// <summary>A held, player-crafted heal consumable — not shelved, not carried (send-legal).</summary>
    private static Item Salve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    /// <summary>A day-1 world already at Expedition (skips Morning's shopping/recruit noise): two
    /// strong vanguards → one party, plus a single held marked salve. 100g start covers the fee.</summary>
    private static GameState ExpeditionWorld() => GameFactory.NewGame(CampSeed) with
    {
        Phase = DayPhase.Expedition,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20), Salve(SalveId) }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i),
    };

    /// <summary>Mount at Expedition, then tick into Camp so the real phase hook raises the slate.</summary>
    private static MainUi MountAtCamp()
    {
        var ui = MountMainUi(new SimAdapter(ExpeditionWorld()));
        AssertThat(ui.Camp.Visible).IsFalse();      // not parked yet
        ui.Adapter.AdvancePhase();                  // Expedition → Camp: the party parks, the hook opens the slate
        AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
        AssertThat(ui.Adapter.CurrentState.InFlight.IsEmpty).IsFalse();
        return ui;
    }

    // ── 1. Camp + non-empty InFlight → slate visible, every hero listed with hp/heals ────────

    [TestCase]
    public void CampPhaseWithInFlight_OpensSlate_ListsEveryHeroWithHpAndHeals_AndFee()
    {
        var ui = MountAtCamp();
        try
        {
            AssertThat(ui.Camp.Visible).IsTrue();

            var party = ui.Adapter.CurrentState.InFlight.Single();
            var text = RenderedText(ui.Camp);

            foreach (var member in party.Party)
            {
                var hero = ui.Adapter.CurrentState.Heroes[member.Value];
                AssertThat(text).Contains(hero.Name);
                AssertThat(text).Contains($"hp {party.Hp[member.Value]}/{hero.MaxHp}");
            }

            AssertThat(text).Contains("heals left");
            AssertThat(text).Contains($"Runner: {Floor1Fee}g"); // fee read from the checkpoint-1 formula
            AssertThat(party.CheckpointFloor).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 2. Not Camp / empty InFlight → slate hidden ──────────────────────────────────────────

    [TestCase]
    public void NotCampOrEmptyInFlight_SlateHidden()
    {
        // Fresh default campaign at Morning: no parked party.
        var fresh = MountMainUi();
        try
        {
            AssertThat(fresh.Camp.Visible).IsFalse();
        }
        finally
        {
            Unmount(fresh);
        }

        // Injected mid-game at Expedition, InFlight still empty (nobody parked yet).
        var expedition = MountMainUi(new SimAdapter(ExpeditionWorld()));
        try
        {
            AssertThat(expedition.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(expedition.Adapter.CurrentState.InFlight.IsEmpty).IsTrue();
            AssertThat(expedition.Camp.Visible).IsFalse();
        }
        finally
        {
            Unmount(expedition);
        }
    }

    // ── 3. Send: pick item + hero → exact SendSupplyAction queued ─────────────────────────────

    [TestCase]
    public void Send_QueuesSendSupplyAction_WithExactIds()
    {
        var ui = MountAtCamp();
        try
        {
            // The held salve is the sole option in the party's picker; Send targets hero 1.
            var pick = Find<OptionButton>(ui.Camp, "CampPick_1");
            AssertThat(pick.ItemCount).IsEqual(1);

            Press(ui.Camp, "CampSend_1");

            var send = ui.Adapter.PendingActions.OfType<SendSupplyAction>().Single();
            AssertThat(send.To.Value).IsEqual(1);
            AssertThat(send.Item.Value).IsEqual(SalveId);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 4. Recall: button → exact RecallPartyAction queued ────────────────────────────────────

    [TestCase]
    public void Recall_QueuesRecallPartyAction_WithAPartyMember()
    {
        var ui = MountAtCamp();
        try
        {
            Press(ui.Camp, "CampRecall_1");

            var recall = ui.Adapter.PendingActions.OfType<RecallPartyAction>().Single();
            AssertThat(ui.Adapter.CurrentState.InFlight.Single().Party.Select(h => h.Value))
                .Contains(recall.Member.Value);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 5. Rejected camp action → reason rendered verbatim ────────────────────────────────────

    [TestCase]
    public void RejectedSend_RendersKernelReasonVerbatim()
    {
        var ui = MountAtCamp();
        try
        {
            // Two deliveries to the same party in one batch: the first lands, the second is refused
            // (one runner per party per day) — a real U4 handler string, rendered on the slate.
            Press(ui.Camp, "CampSend_1");
            Press(ui.Camp, "CampSend_1");
            ui.Adapter.AdvancePhase(); // Camp tick applies the batch; rejection surfaces in LastRejections

            var rejected = ui.Adapter.LastRejections
                .Single(r => r.Action is SendSupplyAction);
            AssertThat(rejected.Reason).Contains("One runner per party per day");

            AssertThat(ui.Camp.Visible).IsTrue(); // slate held through the Deep phase to stay legible
            AssertThat(RenderedText(ui.Camp)).Contains(rejected.Reason);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 6. Hold: close → nothing queued; the day advances normally ───────────────────────────

    [TestCase]
    public void Hold_ClosesSlate_QueuesNothing_DayAdvances()
    {
        var ui = MountAtCamp();
        try
        {
            Press(ui.Camp, "CampHold");
            AssertThat(ui.Camp.Visible).IsFalse();
            AssertThat(ui.Adapter.PendingActions.Count).IsEqual(0);

            AdvanceDay(ui); // day 1 Camp → day 2 Morning, no camp action in flight
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(2);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
