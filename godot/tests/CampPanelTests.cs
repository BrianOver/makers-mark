#if GDUNIT_TESTS
using System;
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
    /// strong vanguards → one party, plus a single held marked salve. 100g start covers the fee.
    /// Also pre-stocks the dagger's copper (mirrors <see cref="ScriptedSession.StartState"/>'s own
    /// technique) so a test can drive a REAL craft — never a synthetic item — while the vigil stop
    /// is armed (U1 scope-ruling: "closing the modal must leave the stop armed... and the player
    /// must be able to move, enter the forge, and craft while it is held").</summary>
    private static GameState ExpeditionWorld() => GameFactory.NewGame(CampSeed) with
    {
        Phase = DayPhase.Expedition,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20), Salve(SalveId) }
            .ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        Player = GameFactory.NewGame(CampSeed).Player with
        {
            Materials = GameFactory.NewGame(CampSeed).Player.Materials.SetItem(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded),
        },
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

            // U1: SendSupply is an immediate verb now — read where it actually lands.
            var send = ui.Adapter.AppliedThisPhase.OfType<SendSupplyAction>().Single();
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

            // U1: RecallParty is an immediate verb now — read where it actually lands.
            var recall = ui.Adapter.AppliedThisPhase.OfType<RecallPartyAction>().Single();
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
            // Two deliveries to the same party: the first lands, the second is refused (one runner
            // per party per day) — a real U4 handler string, rendered on the slate.
            //
            // U1 (loop-legibility) moved SendSupply to the immediate lane, so the refusal happens on
            // the SECOND PRESS, not at the Camp tick. Advancing the phase here would step past the
            // moment under test — and the point of the change is precisely that the player is told
            // no while their hand is still on the button, instead of at a bell they may not connect
            // to the click.
            Press(ui.Camp, "CampSend_1");
            Press(ui.Camp, "CampSend_1");

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

    // ── U16 (Wave 4, KTD3-b): other already-resolved parties pace out during the Vigil too ─────

    /// <summary>Drive a throwaway adapter to Camp (mirrors <see cref="MountAtCamp"/>), then apply
    /// <paramref name="customize"/> to its parked state BEFORE mounting the real UI — the only way
    /// to hand <c>MainUi</c> an already-customized Camp world, since <see cref="SimPanel.Adapter"/>
    /// has no public setter to rebind mid-test.</summary>
    private static MainUi MountAtCampWith(Func<GameState, GameState> customize)
    {
        var seed = new SimAdapter(ExpeditionWorld());
        seed.AdvancePhase(); // Expedition -> Camp: the party parks
        var ui = MountMainUi(new SimAdapter(customize(seed.CurrentState)));
        AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
        AssertThat(ui.Camp.Visible).IsTrue(); // SyncCampModal adopts the injected mid-day park
        return ui;
    }

    private static GameState WithPendingExpedition(GameState state, params HeroId[] party) => state with
    {
        PendingExpeditions = state.PendingExpeditions.Add(new ExpeditionResult(
            party.ToImmutableList(),
            TargetFloor: 2,
            DeepestFloorCleared: 2,
            ImmutableList<FloorOutcome>.Empty,
            Survivors: party.ToImmutableList(),
            Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList<AttributionBeat>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty)),
    };

    [TestCase]
    public void OtherResolvedParty_RendersSummary_WithoutRevealingOutcome()
    {
        var ui = MountAtCampWith(state => WithPendingExpedition(state, new HeroId(99)));
        try
        {
            var text = RenderedText(ui.Camp);
            AssertThat(text).Contains("ALREADY BACK TODAY");
            AssertThat(text).Contains("back from the mine");
            // Self-censored like JourneyStream/ScryingMirror: no floor-cleared number, no death word.
            AssertThat(text).NotContains("floor 2 cleared");
            AssertThat(text).NotContains("died");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NoPendingExpeditions_NoAlreadyBackSection()
    {
        var ui = MountAtCamp();
        try
        {
            var text = RenderedText(ui.Camp);
            AssertThat(text).NotContains("ALREADY BACK TODAY");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U17 (Wave 4): the "signal retreat" interrupt reframes the EXISTING Recall verb ─────────

    [TestCase]
    public void FleeThreshold_HpBelow40Percent_RecallButtonBecomesSignalRetreat()
    {
        var ui = MountAtCampWith(state => state with
        {
            InFlight = ImmutableList.Create(state.InFlight[0] with
            {
                Hp = ImmutableSortedDictionary<int, int>.Empty.Add(1, 5).Add(2, 60), // hero 1: 5/60 hp, well under 40%
            }),
        });
        try
        {
            var button = Find<Button>(ui.Camp, "CampRecall_1");
            AssertThat(button.Text).Contains("Signal Retreat");
            AssertThat(RenderedText(ui.Camp)).Contains("fading");

            // Still the SAME action, unchanged — U17 is UI framing only.
            Press(ui.Camp, "CampRecall_1");
            // U1: RecallParty is an immediate verb now — read where it actually lands.
            var recall = ui.Adapter.AppliedThisPhase.OfType<RecallPartyAction>().Single();
            AssertThat(recall.Member.Value).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AboveFleeThreshold_RecallButtonStaysPlain()
    {
        var ui = MountAtCamp();
        try
        {
            var button = Find<Button>(ui.Camp, "CampRecall_1");
            AssertThat(button.Text).IsEqual("Recall");
            AssertThat(RenderedText(ui.Camp)).NotContains("fading");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 6. Send them deeper: closes the slate AND ticks Camp forward (U1, KTD-A) ──────────────

    /// <summary>
    /// U1 (plan 2026-08-03-001): "CampHold"/"Hold (close)" is retired — there is no longer a
    /// separate phase bell to press afterward, so the modal's own third verb both closes the slate
    /// and ends the vigil stop (<c>RaidConductor.ResolveVigil</c>), ticking Camp → ExpeditionDeep
    /// directly. Nothing is queued because this is a direct tick, not a deferred action.
    /// </summary>
    [TestCase]
    public void SendThemDeeper_ClosesSlate_AndTicksCampForward_NothingQueued()
    {
        var ui = MountAtCamp();
        try
        {
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);

            Press(ui.Camp, "CampDeeper");

            AssertThat(ui.Camp.Visible).IsFalse();
            AssertThat(ui.Adapter.PendingActions.Count).IsEqual(0);
            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Send them deeper must tick Camp -> ExpeditionDeep directly.")
                .IsEqual(DayPhase.ExpeditionDeep);
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.DeepTick);
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── 7. The vigil round trip (U1 scope-ruling, load-bearing): close, craft, come back, send ──

    /// <summary>
    /// The design's core verb, verbatim from the scope ruling: "while the party is camped, the
    /// player leaves the stop, walks to the forge, crafts a potion, comes back, and sends it down."
    /// Proves the mechanism through real Controls: closing the slate never ends the stop, world
    /// input comes back so the player could walk anywhere, a real craft lands while the slate is
    /// closed, that craft's own StateChanged reopens the slate on its own (no player navigation
    /// needed to "come back"), and a held consumable can then be sent and the vigil answered for
    /// real. The sim-side attribution chain (CampHandlers' front-insert, AttributionEngine proving
    /// <c>PotionLifesave</c> for a specific craft) is untouched and out of scope here (zero sim
    /// diff) — this unit's job is that the STOP survives the round trip, not the sim's payoff for it.
    /// </summary>
    [TestCase]
    public void VigilRoundTrip_CloseCraftComeBackSend_ThePlayerCanActWhileTheStopIsArmed()
    {
        var ui = MountAtCamp();
        try
        {
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);
            AssertThat(ui.Camp.Visible).IsTrue();
            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("The slate owns the screen — world input should be blocked while it is up.")
                .IsFalse();

            // Leave the stop: close the slate (the real Escape path, not a bare property write).
            ui.Camp._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });

            AssertThat(ui.Camp.Visible).IsFalse();
            AssertThat(ui.Conductor.Current)
                .OverrideFailureMessage("Closing the slate must never end the vigil stop.")
                .IsEqual(RaidConductor.Beat.VigilStop);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
            AssertThat(ui.Town.WorldInputNode.Enabled)
                .OverrideFailureMessage("World input must come back once the slate closes — the player must be able to walk to the forge.")
                .IsTrue();

            // Walk to the forge and craft — a real, all-phases immediate action, landing while the
            // slate is closed and the stop is still armed.
            ui.OpenPanel("Forge");
            PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");
            var freshCraft = ui.Adapter.LastEvents.OfType<ItemCrafted>().SingleOrDefault();
            AssertThat(freshCraft)
                .OverrideFailureMessage("Setup failed: the craft never landed — nothing to send down.")
                .IsNotNull();

            // "Comes back": the craft's own immediate-action StateChanged re-triggers SyncCampModal
            // (Phase is still Camp, InFlight still non-empty) — the slate reopens on its own, no
            // extra navigation needed to reach it again.
            AssertThat(ui.Camp.Visible)
                .OverrideFailureMessage("The slate should reopen on its own once a real action lands — the player should not have to hunt for a way back.")
                .IsTrue();
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);

            // Send a held consumable down. The dagger just crafted above is a WEAPON (no
            // ConsumableEffect), so it never enters the Send picker at all — that filter
            // (CampPanel.HeldConsumables) is unrelated to this unit and untouched; the crafting
            // half of the round trip is what this test proves with the dagger, and the fixture's
            // own pre-placed Salve (a real held consumable) is what proves the send-then-resolve
            // half. Wiring a full Alchemy-profession potion craft is a separate, heavier fixture
            // this test does not need to stand up to prove the mechanism.
            var pick = Find<OptionButton>(ui.Camp, "CampPick_1");
            var salveIndex = -1;
            for (var i = 0; i < pick.ItemCount; i++)
            {
                if (pick.GetItemMetadata(i).AsInt32() == SalveId)
                {
                    salveIndex = i;
                    break;
                }
            }

            AssertThat(salveIndex)
                .OverrideFailureMessage("The held salve never appeared in the Send picker (setup regression).")
                .IsGreaterEqual(0);
            pick.Select(salveIndex);

            Press(ui.Camp, "CampSend_1");
            var send = ui.Adapter.AppliedThisPhase.OfType<SendSupplyAction>().Single();
            AssertThat(send.Item.Value).IsEqual(SalveId);

            // Finally answer the vigil for real — the round trip ends where it must: a real tick.
            Press(ui.Camp, "CampDeeper");
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.ExpeditionDeep);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// No-softlock guard: if the player closed the slate and comes back to the HUD's own bell-row
    /// control instead of walking back to it, that control must reopen the slate rather than no-op
    /// — CampPanel has a history of exactly this shape of unrecoverable stop (BuildFittedModalCard's
    /// own remarks). Proves there are TWO independent ways back (the automatic reopen-on-action
    /// proven above, and this deliberate one), never zero.
    /// </summary>
    [TestCase]
    public void NoSoftlock_TheBellRowReopensTheSlate_WhenClosedWhileTheStopIsArmed()
    {
        var ui = MountAtCamp();
        try
        {
            ui.Camp._Input(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
            AssertThat(ui.Camp.Visible).IsFalse();
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);

            var bell = Find<Button>(ui, "AdvancePhase");
            AssertThat(bell.Text)
                .OverrideFailureMessage("The bell-row control should offer a deliberate way back to a closed, armed vigil.")
                .IsEqual("Return to the vigil");

            PressEnabled(ui, "AdvancePhase");

            AssertThat(ui.Camp.Visible)
                .OverrideFailureMessage("Pressing the reopen control must actually reopen the slate.")
                .IsTrue();
            AssertThat(ui.Conductor.Current)
                .OverrideFailureMessage("Reopening the slate must never itself resolve the vigil.")
                .IsEqual(RaidConductor.Beat.VigilStop);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
