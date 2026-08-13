#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Audio;
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

    // ── Hero-facing-day H1 (docs/design/2026-08-04-hero-facing-day.md §3.3): the vigil chain ──
    //
    // A single frail hero, seed and stats found by an offline search over GameComposition's own
    // REAL production kernel (system registration order is the determinism contract — see
    // GameComposition's class doc), NOT a hand-picked subset: seed 3 + this exact hero/gear
    // reliably parks at floor-1 checkpoint, then dies in stage 2 UNLESS the front-inserted item
    // is a healing consumable — reproducing, through the real forge and real Send button (never
    // an injected fixture), the exact chain CampHandlersTests' own marquee test proves at the
    // sim layer: a camp-delivered player-marked item earns a PotionLifesave beat at Evening.

    private const ulong VigilChainSeed = 3;

    private static Hero Kess() => new(
        new HeroId(1), "Kess", "vanguard", Level: 3, MaxHp: 18, Gold: 30,
        new GearSet(new ItemId(90), null, new ItemId(91)), ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 1, DiedOnDay: null);

    /// <summary>Day-1 world at Expedition, one frail hero, pre-stocked with exactly the copper a
    /// real field-salve craft needs (Tier 1, 2x copper — same requirement the dagger fixture
    /// already relies on) so the vigil-chain test can forge the ANSWER for real, not place it.</summary>
    private static GameState VigilChainWorld() => GameFactory.NewGame(VigilChainSeed) with
    {
        Phase = DayPhase.Expedition,
        Heroes = new[] { Kess() }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 6), Armor(91, 6) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
        Player = GameFactory.NewGame(VigilChainSeed).Player with
        {
            Materials = ImmutableSortedDictionary<string, int>.Empty.Add("copper", 2),
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

    // ── U2 (loud-failures-and-quiet-channels plan): a real send makes a sound, a refusal none ──
    // Before this unit CampPanel had no cue on Send at all. Mirrors
    // ImmediateActionsDoNotReplayThePhaseTests' own technique (a real button press, then read
    // AudioDirector.RecentCues).

    [TestCase]
    public void Send_RealDelivery_PlaysCoinCue()
    {
        var ui = MountAtCamp();
        try
        {
            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            Press(ui.Camp, "CampSend_1"); // the held salve is the sole option — a real delivery

            var delivered = ui.Adapter.CurrentState.EventLog.OfType<SupplyDelivered>().Any(e => e.To.Value == 1);
            AssertThat(delivered).OverrideFailureMessage("Precondition: the send must actually land.").IsTrue();

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"A real supply delivery played [{string.Join(", ", audio.RecentCues)}] — Coin was " +
                    "never among them. The runner's fee is a real gold transaction and must be audible.")
                .Contains(Cue.Coin);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The half of the send cue that matters: a refusal must never SOUND LIKE A SALE. It does still
    /// make a noise, and that is correct — <c>MainUi</c> plays <see cref="Cue.Rejected"/> for every
    /// rejected action the adapter reports (MainUi.cs, the LastRejections branch), so refusal
    /// feedback is app-wide and predates this unit. An earlier draft of this test asserted total
    /// silence and failed against that existing, intended behaviour; asserting "no Coin" is the
    /// claim this unit is actually entitled to make.
    /// </summary>
    [TestCase]
    public void Send_RefusedSecondDelivery_DoesNotSoundLikeASale()
    {
        var ui = MountAtCamp();
        try
        {
            Press(ui.Camp, "CampSend_1"); // the real delivery — lands, plays Coin

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();
            audio!.ClearRecentCues();

            Press(ui.Camp, "CampSend_1"); // one runner per party per day — this one is refused

            var rejected = ui.Adapter.LastRejections.Single(r => r.Action is SendSupplyAction);
            AssertThat(rejected.Reason).Contains("One runner per party per day");

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"A refused send played [{string.Join(", ", audio.RecentCues)}] — Coin was among " +
                    "them. The runner's fee cue marks gold actually changing hands; a refusal that " +
                    "sounds identical to a delivery tells the player something happened when nothing did.")
                .NotContains(Cue.Coin);

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"A refused send played [{string.Join(", ", audio.RecentCues)}] — the app-wide " +
                    "rejection cue never sounded, so the refusal was silent. MainUi plays Rejected " +
                    "for every rejected action; losing that is a regression in the whole UI, not just here.")
                .Contains(Cue.Rejected);
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

    // ── 8. The vigil chain, end to end (hero-facing-day H1): the acceptance test for the whole
    // feature — a real craft during a held vigil, sent for real, reaching the player's own
    // Night ledger with the mark named. ──────────────────────────────────────────────────────

    /// <summary>
    /// docs/design/2026-08-04-hero-facing-day.md §3.3/§9 DoD-2, verbatim: "A player who forges a
    /// salve at the vigil stop and sends it can, that same Night, point to the beat that names it
    /// — the full chain witnessed in one day." This drives every link for real (never an injected
    /// fixture): the slate states the stakes in hero terms, the player discovers and takes the
    /// forge-and-send verb through the new "Forge something for them" affordance, crafts a REAL
    /// field-salve, sends it, answers the vigil, and the raid plays out for real (real RNG, the
    /// production kernel) into a Night ledger line naming that exact item and that it saved Kess's
    /// life. Seed/hero fixture per <see cref="VigilChainWorld"/>'s own doc.
    /// </summary>
    [TestCase]
    public void VigilChain_CraftDuringHeldStop_SendIt_AttributedOutcomeReachesTheLedger()
    {
        var ui = MountMainUi(new SimAdapter(VigilChainWorld()));
        try
        {
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: Kess parks alone, hurt, no heals yet
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
            AssertThat(ui.Conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);
            AssertThat(ui.Camp.Visible).IsTrue();

            // H1 §3.3 V-1/V-2: the slate states the stakes in hero terms (what's still down
            // there, read off the same venue data the resolver rolls against) and names the
            // craft-and-send verb as something the player can take RIGHT NOW.
            var stakesText = RenderedText(ui.Camp);
            AssertThat(stakesText).Contains("Tunnel Spider"); // floor 2's monster — the real threat ahead
            AssertThat(stakesText).Contains("Forge something for them");
            AssertThat(stakesText)
                .OverrideFailureMessage("The slate must say the stop survives a trip to the forge.")
                .Contains("the vigil holds until you answer it");
            AssertThat(stakesText).Contains("of which yours: 0"); // nothing sent yet

            // Discover and take the verb: leave the stop, forge the answer, come back.
            Press(ui.Camp, "CampForge");
            AssertThat(ui.Camp.Visible).IsFalse();
            AssertThat(ui.Conductor.Current)
                .OverrideFailureMessage("Opening the forge from the slate must never end the vigil stop.")
                .IsEqual(RaidConductor.Beat.VigilStop);

            PressEnabled(ui.Forge, "Craft_field-salve");
            var crafted = ui.Adapter.LastEvents.OfType<ItemCrafted>().SingleOrDefault();
            AssertThat(crafted)
                .OverrideFailureMessage("Setup failed: the field-salve never crafted — nothing to send down.")
                .IsNotNull();

            // "Comes back" on its own — the same mechanism VigilRoundTrip proves.
            AssertThat(ui.Camp.Visible)
                .OverrideFailureMessage("The slate should reopen on its own once the craft lands.")
                .IsTrue();

            var pick = Find<OptionButton>(ui.Camp, "CampPick_1");
            var index = -1;
            for (var i = 0; i < pick.ItemCount; i++)
            {
                if (pick.GetItemMetadata(i).AsInt32() == crafted!.Item.Value)
                {
                    index = i;
                    break;
                }
            }

            AssertThat(index)
                .OverrideFailureMessage("The freshly-forged salve never appeared in the Send picker.")
                .IsGreaterEqual(0);
            pick.Select(index);

            Press(ui.Camp, "CampSend_1");
            var send = ui.Adapter.AppliedThisPhase.OfType<SendSupplyAction>().Single();
            AssertThat(send.Item.Value).IsEqual(crafted!.Item.Value);

            // H1 §3.3 V-1: the payoff-preview updates the instant the delivery lands — "yours"
            // now counts the player's own send, not just whatever the hero happened to buy.
            AssertThat(RenderedText(ui.Camp)).Contains("of which yours: 1");

            Press(ui.Camp, "CampDeeper"); // answer the vigil for real
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.ExpeditionDeep);

            // The raid span plays itself from here (U1) — forced through deterministically
            // (HumanPlaytestTests/RingBellHudTests precedent), never by a real-time wait.
            for (var guard = 0; guard < 8 && ui.Conductor.Current != RaidConductor.Beat.Idle; guard++)
            {
                ui.Conductor.Hurry();
            }

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("The raid span never handed control back at Evening.")
                .IsEqual(DayPhase.Evening);

            PressEnabled(ui, "AdvancePhase"); // the real Evening bell — fires the reveal
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // Return Ritual elapses -> Ledger opens

            AssertThat(ui.Ledger.Visible).IsTrue();
            var ledgerText = RenderedText(ui.Ledger);
            // The acceptance test for the whole feature: the Night ledger line that is different
            // because the player forged and sent that exact item during the held vigil.
            AssertThat(ledgerText)
                .OverrideFailureMessage("The Night ledger never named the delivered salve's own mark.")
                .Contains("Field Salve saved Kess's life");
            AssertThat(ui.Adapter.CurrentState.Heroes[1].Alive)
                .OverrideFailureMessage("Kess should have survived stage 2 thanks to the delivered salve.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
