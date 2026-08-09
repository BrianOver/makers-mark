#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U1 (plan 2026-08-03-001, KTD-A "the two-bell day"): <see cref="RaidConductor"/> is plain C#
/// (PhaseClock's own testability idiom) — every scenario here drives a bare <see cref="SimAdapter"/>/
/// <see cref="PhaseClock"/> pair with fake, test-controlled completion predicates, no Godot runtime
/// needed. Covers the plan's own enumerated test scenarios for U1.
/// </summary>
[TestSuite]
public class RaidConductorTests
{
    // ── Fixtures ───────────────────────────────────────────────────────────────────────────────

    // A fresh Day-1 campaign is GUARANTEED unstaged: every hero's first-ever trip targets floor 1,
    // and ExpeditionSystem.CheckpointFor(1) = min(1, 0) = 0 < 1, so the whole run resolves at the
    // Expedition tick and InFlight never populates (see ExpeditionSystem's own class doc). This is
    // the common case this unit exists to fix, not a contrived edge case.
    private const ulong UnstagedSeed = 2026;

    // Mirrors CampPanelTests' own precedent exactly (duplicated per BellTrayTests' own documented
    // reasoning for why fixture duplication across test files is fine here): DeepestFloorReached: 1
    // pushes the target floor to 2, so CheckpointFor(2) = min(1, 1) = 1 — staged, and two strong
    // vanguards reliably clear floor 1 clean rather than wipe/gate/lose it, so they PARK rather than
    // finalizing badly.
    private const ulong StagedSeed = 6;

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

    /// <summary>A Day-1 Morning world with two heroes guaranteed to clear stage 1 clean and park.
    /// NextHeroId is bumped past the two hand-assigned ids — left at its GameFactory default (1),
    /// RecruitSystem's Morning trickle (fires on ANY fresh roster short of six, unconditionally,
    /// per its own "an idle gate rests at zero" doc) mints its recruit at id 1 and collides with
    /// Strong(1) already in the dictionary, throwing out of ImmutableSortedDictionary.Add instead
    /// of a readable test failure.</summary>
    private static GameState StagedWorld() => GameFactory.NewGame(StagedSeed) with
    {
        Phase = DayPhase.Morning,
        NextHeroId = 3,
        Heroes = new[] { Strong(1), Strong(2) }.ToImmutableSortedDictionary(h => h.Id.Value, h => h),
        Items = new[] { Weapon(90, 30), Armor(91, 20) }.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
    };

    private static (SimAdapter Adapter, PhaseClock Clock, RaidConductor Conductor) Build(
        GameState world, bool departureDone = true, bool homecomingDone = true, Func<bool>? showHeld = null)
    {
        var adapter = new SimAdapter(world);
        var clock = new PhaseClock(adapter);
        var conductor = new RaidConductor(
            adapter, clock, () => departureDone, () => homecomingDone, showHeld ?? (() => false));
        return (adapter, clock, conductor);
    }

    /// <summary>A <see cref="StagedWorld"/> driven all the way to a REAL parked party at Camp, then
    /// handed back as bare state — the shape <c>CampaignSave</c> restores and <c>MainUi._Ready</c>
    /// builds a brand-new adapter/clock/conductor over when the player picks Continue.</summary>
    private static GameState ParkedAtCampWorld()
    {
        var driver = new SimAdapter(StagedWorld());
        driver.AdvancePhase(); // Morning -> Expedition
        driver.AdvancePhase(); // Expedition -> Camp: the party parks
        AssertThat(driver.CurrentState.Phase)
            .OverrideFailureMessage("Fixture premise failed: the staged drive did not land at Camp.")
            .IsEqual(DayPhase.Camp);
        AssertThat(driver.CurrentState.InFlight.IsEmpty)
            .OverrideFailureMessage("Fixture premise failed: the staged party did not park.")
            .IsFalse();
        return driver.CurrentState;
    }

    /// <summary>One frame of <c>MainUi._Process</c>'s exact routing — the Idle gate that decides
    /// which of the two timers is allowed to run this frame. Duplicated here (rather than mounting
    /// the whole scene) because these tests are deliberately engine-free; the routing is three lines
    /// and pinning it against the real thing is <c>PlayableLoopTests</c>'s job, not this suite's.</summary>
    private static void Frame(PhaseClock clock, RaidConductor conductor, double delta)
    {
        if (conductor.Current == RaidConductor.Beat.Idle)
        {
            clock.Update(delta);
        }
        else
        {
            conductor.Update(delta);
        }
    }

    // ── 1. A parked party stops exactly once, and stops indefinitely ─────────────────────────────

    [TestCase]
    public void StagedParty_Parks_ConductorStopsExactlyOnceAtVigil_AndHoldsIndefinitely()
    {
        var (adapter, clock, conductor) = Build(StagedWorld());

        clock.AdvanceNow(); // the Morning bell: Morning -> Expedition
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

        conductor.Update(RaidConductor.SendOffMaxSeconds); // departure show done (fake predicate true) -> stage-1 tick
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
        AssertThat(adapter.CurrentState.InFlight.IsEmpty)
            .OverrideFailureMessage("Fixture premise failed: the staged party did not park.")
            .IsFalse();
        AssertThat(conductor.Current)
            .OverrideFailureMessage("A parked party must stop the conductor at VigilStop.")
            .IsEqual(RaidConductor.Beat.VigilStop);

        // Indefinite: drive WAY past every pinned max, several times over — the phase must never
        // move and Current must never leave VigilStop until the player answers.
        for (var i = 0; i < 20; i++)
        {
            conductor.Update(RaidConductor.HomecomingMaxSeconds * 10);
        }

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("VigilStop ticked the phase forward on its own — it must hold with no timer.")
            .IsEqual(DayPhase.Camp);
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);

        // The modal answers (the camp slate's third verb, "Send them deeper") — this is the ONLY
        // thing that ends the stop.
        conductor.ResolveVigil();
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.ExpeditionDeep);
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.DeepTick);
    }

    // ── 2. No parked party reaches Evening with zero further player input, never shows the stop ──

    [TestCase]
    public void UnstagedDay_ReachesEvening_WithZeroFurtherInput_NeverEntersVigilStop()
    {
        var (adapter, clock, conductor) = Build(GameComposition.NewCampaign(UnstagedSeed));

        clock.AdvanceNow(); // the ONE player input this whole test performs: the Morning bell
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

        var everWasVigilStop = false;
        for (var i = 0; i < 50 && adapter.CurrentState.Phase != DayPhase.Evening; i++)
        {
            conductor.Update(1.0);
            everWasVigilStop |= conductor.Current == RaidConductor.Beat.VigilStop;
        }

        AssertThat(everWasVigilStop)
            .OverrideFailureMessage("An unstaged day (nobody parked) must never show the vigil stop.")
            .IsFalse();
        AssertThat(adapter.CurrentState.InFlight.IsEmpty)
            .OverrideFailureMessage("Fixture premise failed: someone parked on a fresh day 1.")
            .IsTrue();
        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("The conductor never reached Evening on its own.")
            .IsEqual(DayPhase.Evening);

        conductor.Update(RaidConductor.HomecomingMaxSeconds); // let the (trivial, nobody-away) homecoming beat clear
        AssertThat(conductor.Current)
            .OverrideFailureMessage("Evening arrived but the conductor never handed control back (Idle).")
            .IsEqual(RaidConductor.Beat.Idle);
    }

    // ── 3. Hurry lands at the next stop from every beat, never skipping the vigil stop ──────────

    [TestCase]
    public void Hurry_FromSendOff_OnAStagedDay_LandsExactlyAtVigilStop()
    {
        var (adapter, clock, conductor) = Build(StagedWorld(), departureDone: false, homecomingDone: false);

        clock.AdvanceNow(); // Morning -> Expedition
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

        conductor.Hurry();

        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
        AssertThat(conductor.Current)
            .OverrideFailureMessage("Hurry must land at the vigil stop, not sail past it.")
            .IsEqual(RaidConductor.Beat.VigilStop);
    }

    [TestCase]
    public void Hurry_FromSendOff_OnAnUnstagedDay_LandsAtEvening_WithNoStopInBetween()
    {
        var (adapter, clock, conductor) = Build(GameComposition.NewCampaign(UnstagedSeed), departureDone: false, homecomingDone: false);

        clock.AdvanceNow(); // Morning -> Expedition
        conductor.Hurry();

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("Hurry on an unstaged day should reach Evening in one press — nothing stands between SendOff and Idle when nobody parks.")
            .IsEqual(DayPhase.Evening);
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.Idle);
    }

    [TestCase]
    public void Hurry_WhileAtVigilStop_IsANoOp_NeverSkipsThePlayersDecision()
    {
        var (adapter, clock, conductor) = Build(StagedWorld());
        clock.AdvanceNow();
        conductor.Update(RaidConductor.SendOffMaxSeconds); // -> Camp, VigilStop
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);

        conductor.Hurry();

        AssertThat(conductor.Current)
            .OverrideFailureMessage("Hurry must never itself resolve the vigil stop.")
            .IsEqual(RaidConductor.Beat.VigilStop);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
    }

    // ── 4. Recall during the stop still enters ExpeditionDeep ────────────────────────────────────

    [TestCase]
    public void RecallDuringTheStop_DoesNotEndIt_ButStillEntersExpeditionDeepOnceResolved()
    {
        var (adapter, clock, conductor) = Build(StagedWorld());
        clock.AdvanceNow();
        conductor.Update(RaidConductor.SendOffMaxSeconds);
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);

        var lead = adapter.CurrentState.InFlight[0].Party[0];
        adapter.Queue(new RecallPartyAction(lead)); // immediate action — must not end the stop by itself

        AssertThat(conductor.Current)
            .OverrideFailureMessage("An immediate camp action (Recall) perturbed the vigil stop's own beat.")
            .IsEqual(RaidConductor.Beat.VigilStop);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);

        conductor.ResolveVigil(); // the modal's third verb — the only real way out

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("Recalling a party must still enter ExpeditionDeep once the vigil is answered.")
            .IsEqual(DayPhase.ExpeditionDeep);
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.DeepTick);
    }

    // ── 5. Pending bell-rider actions flush on a conductor tick ──────────────────────────────────

    [TestCase]
    public void PendingDeferredAction_FlushesOnTheConductorsOwnTick_QueueEmpties()
    {
        var (adapter, clock, conductor) = Build(GameComposition.NewCampaign(UnstagedSeed));
        clock.AdvanceNow(); // Morning -> Expedition
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

        adapter.Queue(new UpgradeForgeAction()); // a genuine bell-rider — deferred, not immediate
        AssertThat(adapter.PendingActions.Count).IsEqual(1);

        conductor.Update(RaidConductor.SendOffMaxSeconds); // the conductor's OWN tick, not a bell press

        AssertThat(adapter.PendingActions.Count)
            .OverrideFailureMessage("A conductor-driven tick must flush pending bell-riders exactly like the bell does.")
            .IsEqual(0);
    }

    // ── 6. Morning counter-hold: the conductor never starts while Morning holds ──────────────────

    [TestCase]
    public void OpenCounterSession_HoldsMorning_ConductorNeverStarts()
    {
        var (adapter, clock, conductor) = Build(GameComposition.NewCampaign(UnstagedSeed));

        adapter.Queue(new OpenCounterAction()); // immediate — opens the session
        clock.AdvanceNow(); // GameKernel holds the day at Morning while Counter is { Closed: false }

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("Fixture premise failed: the counter-hold did not fire.")
            .IsEqual(DayPhase.Morning);
        AssertThat(conductor.Current)
            .OverrideFailureMessage("The conductor started even though Morning never actually completed.")
            .IsEqual(RaidConductor.Beat.Idle);
    }

    // ── 7. The retired bell labels appear nowhere, across all five phases ───────────────────────

    [TestCase]
    public void BellVerb_NeverRendersAnyOfTheThreeRetiredLabels_AcrossEveryPhase()
    {
        string[] retired =
        [
            "Lower them into the mine",
            "Let them press deeper",
            "Ring the return bell",
            "Close the vigil",
        ];

        foreach (var phase in System.Enum.GetValues<DayPhase>())
        {
            var state = GameComposition.NewCampaign(1) with { Phase = phase };
            var verb = Ui.PhaseVocab.BellVerb(state);
            foreach (var label in retired)
            {
                AssertThat(verb)
                    .OverrideFailureMessage($"{phase}'s bell verb (\"{verb}\") still renders the retired label \"{label}\".")
                    .IsNotEqual(label);
            }
        }
    }

    // ── 8. Scope ruling point 3: an unstaged (no-park) day still shows real SendOff/Homecoming ──
    //      content — "two bells is a floor, not the goal" — the middle is not compressed to nothing.

    [TestCase]
    public void UnstagedDay_SendOff_StillWaitsOnItsOwnConditionAndMax_NeverInstant()
    {
        var (adapter, clock, conductor) = Build(GameComposition.NewCampaign(UnstagedSeed), departureDone: false);

        clock.AdvanceNow(); // Morning -> Expedition
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

        // departureDone is false and stays false — SendOff must hold on its OWN terms (the pinned
        // max), never skip ahead just because the party underneath it will turn out unstaged.
        conductor.Update(RaidConductor.SendOffMaxSeconds - 0.5);
        AssertThat(conductor.Current)
            .OverrideFailureMessage("SendOff ended before its own condition or max — the departure is not yet worth calling seen.")
            .IsEqual(RaidConductor.Beat.SendOff);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

        conductor.Update(1.0); // crosses SendOffMaxSeconds
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
    }

    [TestCase]
    public void UnstagedDay_Homecoming_StillWaitsOnItsOwnConditionAndMax_NeverSkippedForBeingEmpty()
    {
        var (adapter, clock, conductor) = Build(GameComposition.NewCampaign(UnstagedSeed), homecomingDone: false);

        clock.AdvanceNow(); // Morning -> Expedition
        conductor.Update(RaidConductor.SendOffMaxSeconds); // -> Camp (empty InFlight -> DeepTick)
        conductor.Update(RaidConductor.EmptyBeatSeconds);  // -> ExpeditionDeep (still DeepTick)
        conductor.Update(RaidConductor.EmptyBeatSeconds);  // -> Evening (Homecoming)

        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Evening);
        AssertThat(conductor.Current)
            .OverrideFailureMessage("The empty Camp/Deep beats must not swallow Homecoming — nobody parking does not mean nobody is worth watching come home.")
            .IsEqual(RaidConductor.Beat.Homecoming);

        // homecomingDone is false and stays false — only the pinned max may release it, exactly
        // like a staged day's homecoming would be held. A no-park day gets the SAME return content,
        // not a shortcut past it.
        conductor.Update(RaidConductor.HomecomingMaxSeconds - 0.5);
        AssertThat(conductor.Current)
            .OverrideFailureMessage("Homecoming ended before its own condition or max on an unstaged day — the return was compressed to nothing, which the scope ruling forbids.")
            .IsEqual(RaidConductor.Beat.Homecoming);

        conductor.Update(1.0); // crosses HomecomingMaxSeconds
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.Idle);
    }

    // ── 9. The hold: a timer may never answer for the player (2026-08-09 owner report) ──────────
    //
    //  "i clicked send them off and it auto jumped to night???? yet this is still on tutorial 5???
    //   this is a critical bug as it skipped most the game and prevented me from playing more"
    //
    // Measured on a fresh day 1 before the fix: 4.77 real seconds from the Morning bell to Evening
    // with zero further input, and — because the apprenticeship chain's Watch step is printed on the
    // Expedition->Camp tick while the Watch control itself only exists during the raid span —
    // exactly 2.00 seconds to answer an instruction the game had only just given. What the cases
    // below pin, together, is the law (§11.7.8: no timers on decisions; skipping stays legal and its
    // cost is named in copy, never engineered): the timer stops while an answer is owed, the held
    // seconds are never banked, and the player's own press always goes through anyway.

    [TestCase]
    public void HeldShow_StopsTheTimerDead_TheUnstagedDayNeverReachesEveningOnItsOwn()
    {
        var held = true;
        var (adapter, clock, conductor) = Build(
            GameComposition.NewCampaign(UnstagedSeed), departureDone: false, showHeld: () => held);

        clock.AdvanceNow(); // the Morning bell — "Send them off"
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.SendOff);
        AssertThat(conductor.ShowHeld)
            .OverrideFailureMessage("A running show with an unanswered ask must report itself held.")
            .IsTrue();

        // Two full minutes of frames — twenty times every pinned max in this class combined.
        for (var i = 0; i < 120; i++)
        {
            conductor.Update(1.0);
        }

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage(
                "The show timer ran while the player still owed an answer — this is the reported bug: " +
                "one press of Send them off carried the whole day to Night on its own.")
            .IsEqual(DayPhase.Expedition);

        // Answered: the show resumes on its own terms, and only then.
        held = false;
        AssertThat(conductor.ShowHeld).IsFalse();
        conductor.Update(RaidConductor.SendOffMaxSeconds);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
    }

    [TestCase]
    public void HeldShow_NeverBanksTheHeldTime_TheShowResumesWhereItPaused()
    {
        // The failure this pins is the reported bug wearing a different hat: if the hold merely
        // DEFERRED the tick (PhaseClock.Update's own engaged branch accrues to the cap on purpose),
        // then a minute spent inside the forge would fire every remaining beat the frame the player
        // walked out — a skipped day, just delayed. A held show is PAUSED, not deferred.
        var held = true;
        var (adapter, clock, conductor) = Build(
            GameComposition.NewCampaign(UnstagedSeed), departureDone: false, showHeld: () => held);

        clock.AdvanceNow(); // Morning -> Expedition
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.SendOff);

        for (var i = 0; i < 60; i++)
        {
            conductor.Update(1.0); // 60 seconds held — ten times SendOffMaxSeconds
        }

        held = false;
        conductor.Update(1.0 / 60.0); // the very first frame after the hold lifts

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage(
                "The held seconds were banked: the show fired the instant the hold lifted instead of " +
                "resuming where it paused.")
            .IsEqual(DayPhase.Expedition);

        conductor.Update(RaidConductor.SendOffMaxSeconds); // now it earns its own max, from zero
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
    }

    [TestCase]
    public void Hurry_WalksStraightThroughTheHold_SkippingStaysLegal()
    {
        // §11.7.8: skipping stays legal and its cost is named in copy, never engineered. The hold
        // binds the TIMER; the bell-row control is the player, and the player always wins.
        var (adapter, clock, conductor) = Build(
            GameComposition.NewCampaign(UnstagedSeed), departureDone: false, showHeld: () => true);

        clock.AdvanceNow();
        AssertThat(conductor.ShowHeld).IsTrue();

        conductor.Hurry();

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("The hold trapped the player — Hurry must always be allowed through it.")
            .IsEqual(DayPhase.Evening);
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.Idle);
    }

    [TestCase]
    public void EngagedSurface_HoldsTheShow_TheSameLatchThatAlreadyHeldThePhaseClock()
    {
        // PhaseClock.Engaged has meant "a drawer/interior/modal owns the screen, hold at the
        // boundary" since U15 — and the conductor was simply never wired to it, so the day rolled to
        // Night underneath a player who was standing in the forge crafting during the march (exactly
        // what §11.7.4 asks the Quest phase to be for).
        var (adapter, clock, conductor) = Build(GameComposition.NewCampaign(UnstagedSeed), departureDone: false);

        clock.AdvanceNow();
        clock.Engaged = true;
        AssertThat(conductor.ShowHeld).IsTrue();

        for (var i = 0; i < 60; i++)
        {
            conductor.Update(1.0);
        }

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("An engaged surface must hold the raid span exactly as it holds the phase clock.")
            .IsEqual(DayPhase.Expedition);

        clock.Engaged = false;
        AssertThat(conductor.ShowHeld).IsFalse();
    }

    [TestCase]
    public void ShowHeld_IsFalseWhereThereIsNoTimerToHold_IdleAndVigilStop()
    {
        var (_, clock, conductor) = Build(StagedWorld(), showHeld: () => true);

        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.Idle);
        AssertThat(conductor.ShowHeld)
            .OverrideFailureMessage("Idle has no show timer — reporting it held would put a false caption on the HUD.")
            .IsFalse();

        clock.AdvanceNow();
        conductor.Update(RaidConductor.SendOffMaxSeconds); // -> Camp, party parked
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);
        AssertThat(conductor.ShowHeld)
            .OverrideFailureMessage("VigilStop is already timer-free — it is a stop, not a held show.")
            .IsFalse();
    }

    // ── 10. A resumed campaign arms the vigil at construction, not on the next transition ────────

    [TestCase]
    public void ResumedMidCamp_ArmsTheVigilStopAtConstruction_NeverStartsIdle()
    {
        var parked = ParkedAtCampWorld();
        var (_, _, conductor) = Build(parked);

        AssertThat(conductor.Current)
            .OverrideFailureMessage(
                "A campaign resumed with a party already parked started at Idle. Idle routes " +
                "MainUi._Process into Clock.Update and the bell press into Clock.AdvanceNow(), so a " +
                "timer or one stray bell would end an unanswered vigil — the law break this derivation closes.")
            .IsEqual(RaidConductor.Beat.VigilStop);
    }

    [TestCase]
    public void ResumedMidCamp_TheOptInInnkeepersClock_CannotTimeTheVigilAway()
    {
        var (adapter, clock, conductor) = Build(ParkedAtCampWorld());

        clock.SetAutoAdvance(true); // the persisted escape hatch, restored at boot before frame one
        clock.Play();

        for (var i = 0; i < 200; i++)
        {
            Frame(clock, conductor, 1.0); // 200 seconds — four times PhaseClock's own borrowed duration
        }

        AssertThat(adapter.CurrentState.Phase)
            .OverrideFailureMessage("A wall-clock timer ended the vigil of a party that is still parked.")
            .IsEqual(DayPhase.Camp);
        AssertThat(adapter.CurrentState.InFlight.IsEmpty).IsFalse();
        AssertThat(conductor.Current).IsEqual(RaidConductor.Beat.VigilStop);

        conductor.ResolveVigil(); // still the only way out
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.ExpeditionDeep);
    }
}
#endif
