#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U10 (world-and-interiors plan, KTD-5): "why did the heroes come back to the town visually?" —
/// the return is now a ceremony, not a teleport.
///
/// <para><b>Root cause (verified before this plan was written).</b> An unstaged (target floor 1)
/// expedition resolves and lands its result in <c>GameState.PendingExpeditions</c> on the SAME
/// <c>AdvancePhase</c> tick that ends the Expedition phase (<c>ExpeditionSystem.cs:84-106</c>), and
/// <c>Town2D.OnPhaseCompleted(Expedition)</c> used to call <c>ReturnSurvivors</c> — which snapped a
/// survivor straight home — in that same synchronous call. The bell press that "lowers them into
/// the mine" was the same click that walked them home; the departure animation the PREVIOUS tick
/// scheduled had not even finished.</para>
///
/// <para><b>The fix (KTD-5).</b> <c>ReturnSurvivors</c> now only QUEUES a survivor group
/// (<c>Town2D.AnyReturnPending</c>); <c>Town2D._Process</c> holds it until EVERY member has stood
/// at <c>HeroActor2D.HeroTownState.Away</c> for at least <c>Town2D.MinDelveShowSeconds</c> (~8s),
/// measured against each actor's OWN accumulated Away time — not against when the tick fired. A
/// staged (multi-day Camp) return has already accrued far more than that by the time it resolves,
/// so the floor is invisible there by construction; only the same-day unstaged path is held up.</para>
///
/// <para><b>The hazard this suite exists to prove impossible</b> (this unit's stated central risk):
/// a hero stuck invisible forever. <see cref="ReloadMidHold_NeverLeavesAHeroStuckInvisible"/> is
/// that test, written first per the task brief.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeroReturnCeremonyTests
{
    /// <summary>
    /// The exact original bug, reproduced: press the bell that ends Morning (departs the party),
    /// then IMMEDIATELY press it again to end Expedition — ZERO frames in between, the same
    /// synchronous-click shape the owner actually hit. Then prove the whole ceremony: invisible
    /// until BOTH floor conditions clear, emergence from the gate, the narrator line, arrival home.
    /// </summary>
    [TestCase]
    public async Task UnstagedDay_HeroesStayInvisibleUntilTheShowFloorClears_ThenEmergeFromTheGate()
    {
        var ui = MountMainUi(); // fresh seed-2026 campaign: 6 starting heroes, all DeepestFloorReached
                                 // 0, so day 1's target floor is always 1 — always unstaged.
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            ui.Adapter.AdvancePhase(); // Morning -> Expedition: DepartWanderingHeroes schedules march-out
            ui.Adapter.AdvancePhase(); // Expedition -> Camp: the unstaged run finalizes; ReturnSurvivors
                                       // must QUEUE here, not snap — this is the exact reproduction.

            var survivorIds = ui.Adapter.CurrentState.PendingExpeditions
                .SelectMany(r => r.Survivors)
                .Select(id => id.Value)
                .ToList();

            AssertThat(survivorIds.Count)
                .OverrideFailureMessage(
                    "Precondition: day 1's unstaged run produced zero survivors (a total party " +
                    "wipe?) — this test needs at least one survivor to prove anything about the " +
                    "return ceremony.")
                .IsGreater(0);

            AssertThat(ui.Town.AnyReturnPending)
                .OverrideFailureMessage(
                    "ReturnSurvivors must QUEUE the group (Town2D.AnyReturnPending) rather than " +
                    "snap it home on the SAME tick that resolved the expedition — that immediate " +
                    "snap is the exact bug behind \"why did the heroes come back to the town " +
                    "visually?\".")
                .IsTrue();

            foreach (var id in survivorIds)
            {
                AssertThat(ui.Town.FindHeroActor(id)?.State)
                    .OverrideFailureMessage(
                        $"Hero {id} must not already read as home (Wandering) the instant the run " +
                        "resolves — nothing has been visibly gone yet at this point.")
                    .IsNotEqual(HeroActor2D.HeroTownState.Wandering);
            }

            // Let the REAL march-out animation actually finish — condition-waited, never a frame
            // count (HumanPlayer.WaitUntil's own contract).
            var player = new HumanPlayer(ui);
            var marchedOut = await player.WaitUntil(
                () => survivorIds.All(id => ui.Town.FindHeroActor(id)?.State == HeroActor2D.HeroTownState.Away),
                maxFrames: 900);

            AssertThat(marchedOut)
                .OverrideFailureMessage(
                    "Survivors never finished marching out to Away within 900 real frames — " +
                    "DepartWanderingHeroes/_pendingMarchOut regressed.")
                .IsTrue();

            foreach (var id in survivorIds)
            {
                AssertThat(ui.Town.FindHeroActor(id)!.Visible)
                    .OverrideFailureMessage(
                        $"Hero {id} is Away but still Visible — R6 requires nobody visible in town " +
                        "while the phase vocabulary says the party is below.")
                    .IsFalse();
            }

            // Marching out only took a few real seconds — nowhere near MinDelveShowSeconds yet, so
            // the hold must still be open.
            AssertThat(ui.Town.AnyReturnPending)
                .OverrideFailureMessage(
                    "The show floor cleared far too early — survivors reached Away only moments ago.")
                .IsTrue();

            // Fast-forward the remainder WITHOUT waiting the real ~8s: one manual tick past
            // MinDelveShowSeconds, the same big-delta idiom this suite already uses for every other
            // real-seconds gate (e.g. MainUi.ReturnRitualDelaySeconds elsewhere in this project).
            ui.Town._Process(Town2D.MinDelveShowSeconds + 1.0);

            AssertThat(ui.Town.AnyReturnPending)
                .OverrideFailureMessage(
                    "The show floor elapsed but the group is still queued — TickPendingReturns regressed.")
                .IsFalse();

            // The group left the queue — its staggered file-out (mirrors departure's own
            // FileExitStaggerSeconds stagger) is now in flight. Only the FIRST hero begins
            // WalkingIn on this exact tick; the rest peel off staggered, same as departure — so
            // this waits on the CONDITION ("nobody is still frozen at Away") rather than asserting
            // every survivor is WalkingIn simultaneously (which the initial version of this test
            // got away with only because its +1.0s nudge happened to swamp the stagger too).
            // Bounded well under a second MinDelveShowSeconds wait, so a regression that gated the
            // rest of the party behind another floor would still be caught here.
            var emergenceStarted = await player.WaitUntil(
                () => survivorIds.All(id => ui.Town.FindHeroActor(id)?.State != HeroActor2D.HeroTownState.Away),
                maxFrames: 120);

            AssertThat(emergenceStarted)
                .OverrideFailureMessage("Survivors never started their staggered walk-in once the show floor cleared.")
                .IsTrue();

            foreach (var id in survivorIds)
            {
                AssertThat(ui.Town.FindHeroActor(id)!.Visible)
                    .OverrideFailureMessage($"Hero {id} must be visible again once the emergence begins.")
                    .IsTrue();
            }

            AssertThat(player.Sees("returns from"))
                .OverrideFailureMessage(
                    $"No narrator line rendered on emergence (MainUi.OnPartyEmerging). " +
                    $"Screen:\n{player.Screen()}")
                .IsTrue();

            // Let the walk-in actually complete — proves the ceremony finishes, not just begins.
            var arrivedHome = await player.WaitUntil(
                () => survivorIds.All(id => ui.Town.FindHeroActor(id)?.State == HeroActor2D.HeroTownState.Wandering),
                maxFrames: 900);

            AssertThat(arrivedHome)
                .OverrideFailureMessage("Survivors never completed their walk-in back to Wandering.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// THE central hazard this unit must prove impossible (task brief: "make that the test you
    /// write first"). A save/load taken while a KTD-5 hold was live — the sim's own state already
    /// fully resolved the day (Phase past Expedition, survivors sitting in PendingExpeditions,
    /// exactly what CampaignSave/Continue hands back) — must NEVER produce a hero stuck at
    /// <see cref="HeroActor2D.HeroTownState.Away"/> forever. <see cref="Town2D._pendingReturns"/>
    /// (Town2D's in-memory hold queue) is presentation-only and never persisted (KTD-5: "the floor
    /// only stretches presentation inside a live session, never state") — a freshly-built Town2D
    /// for this exact GameState has no memory of any hold and reconciles every alive hero to
    /// Wandering via the ordinary <see cref="Town2D.ReconcileHeroes"/> default, so the failure mode
    /// this test targets cannot occur by construction. Pinned anyway, so a FUTURE attempt to make
    /// reload "smarter" (e.g. reconstructing Away state from PendingExpeditions on Build without
    /// also re-queuing the return) cannot reintroduce it silently.
    /// </summary>
    [TestCase]
    public void ReloadMidHold_NeverLeavesAHeroStuckInvisible()
    {
        var partyIds = ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3));
        var state = GameFactory.NewGame(31415) with
        {
            Heroes = ThreeHeroes(),
            Phase = DayPhase.Camp,
            PendingExpeditions = ImmutableList.Create(SurvivedRun(partyIds, "mine")),
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            foreach (var id in partyIds)
            {
                var actor = ui.Town.FindHeroActor(id.Value);
                AssertThat(actor)
                    .OverrideFailureMessage(
                        $"Hero {id.Value} has no actor at all after a fresh build — ReconcileHeroes regressed.")
                    .IsNotNull();

                AssertThat(actor!.State)
                    .OverrideFailureMessage(
                        $"Hero {id.Value} loaded STUCK at {actor.State} instead of Wandering — this " +
                        "IS the stuck-invisible failure mode KTD-5 requires be impossible. A fresh " +
                        "Town2D must never inherit an in-flight return hold from a previous session.")
                    .IsEqual(HeroActor2D.HeroTownState.Wandering);

                AssertThat(actor.Visible)
                    .OverrideFailureMessage(
                        $"Hero {id.Value} loaded invisible — a reload must never leave a hero " +
                        "permanently unseen.")
                    .IsTrue();
            }

            AssertThat(ui.Town.AnyReturnPending)
                .OverrideFailureMessage(
                    "A freshly-built Town2D must never start with a pending return already queued " +
                    "— ReturnSurvivors only ever runs from a live OnPhaseCompleted call, never from " +
                    "Build/ReconcileHeroes.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// "staged (Camp) runs unchanged" (task brief's explicit scenario). KTD-5's floor is measured
    /// against each actor's OWN accumulated Away time, never against when <c>ReturnSurvivors</c>
    /// happens to be called — so a party that has genuinely been Away for a long time (the ordinary
    /// shape of a staged, multi-day Camp return) must emerge on the SAME tick it is queued, with no
    /// extra artificial delay layered on top.
    ///
    /// <para>Driven on a standalone <see cref="Town2D"/> rather than trusting a seed's combat RNG to
    /// actually stage cleanly: <see cref="Town2D.OnPhaseCompleted"/> is public and phase-decoupled
    /// from the adapter's own phase (the same seam <c>Town2DSceneTests</c>/<c>HeroActor2DTests</c>
    /// already rely on), and <see cref="Town2D._Process"/> accepts a manual delta — the established
    /// big-delta idiom this whole suite uses to fast-forward real-seconds gates without waiting on
    /// the wall clock.</para>
    /// </summary>
    [TestCase]
    public async Task StagedReturn_AlreadyPastTheShowFloor_EmergesWithNoExtraDelay()
    {
        var partyIds = ImmutableList.Create(new HeroId(1), new HeroId(2), new HeroId(3));
        var state = GameFactory.NewGame(24601) with
        {
            Heroes = ThreeHeroes(),
            PendingExpeditions = ImmutableList.Create(SurvivedRun(partyIds, "mine")),
        };

        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new SimAdapter(state));
        town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

        try
        {
            town.OnPhaseCompleted(DayPhase.Morning); // schedules the real march-out for all three

            var player = new HumanPlayer(town);
            var marchedOut = await player.WaitUntil(
                () => partyIds.All(id => town.FindHeroActor(id.Value)?.State == HeroActor2D.HeroTownState.Away),
                maxFrames: 900);

            AssertThat(marchedOut)
                .OverrideFailureMessage("Setup: the party never finished marching out to Away.")
                .IsTrue();

            // Stands in for the real days a staged Camp run would spend below — comfortably clears
            // MinDelveShowSeconds before ReturnSurvivors is ever called, unlike the unstaged fast path.
            town._Process(50.0);

            // Precondition, asserted explicitly and separately from the consequence below (review
            // finding): if this fails, the FIXTURE is broken (the away-time accumulator never saw
            // the big-delta tick, or an actor isn't Away yet) — not the design under test. Without
            // this split, a fixture regression and a real design regression report the identical
            // failure below and cannot be told apart.
            foreach (var id in partyIds)
            {
                AssertThat(town.AwaySecondsFor(id.Value))
                    .OverrideFailureMessage(
                        $"Setup: hero {id.Value} is not past the show floor yet " +
                        $"({town.AwaySecondsFor(id.Value)}s < {Town2D.MinDelveShowSeconds}s) — the " +
                        "fixture, not Town2D, is broken.")
                    .IsGreaterEqual(Town2D.MinDelveShowSeconds);
            }

            town.OnPhaseCompleted(DayPhase.ExpeditionDeep); // queues the group (ReturnSurvivors)

            // Wait on the CONDITION, not a guessed duration (review finding — HumanPlayer.WaitUntil
            // is exactly this): a staged return whose actors are already past the floor must clear
            // Town2D.AnyReturnPending within a handful of real frames, nowhere close to another
            // MinDelveShowSeconds-scale wait (~480 frames at 60fps). Bounding this at 60 is the
            // actual assertion — if a regression re-added a second floor-wait here, this would time
            // out and fail loudly instead of silently passing on an over-generous cap.
            var clearedWithoutExtraDelay = await player.WaitUntil(() => !town.AnyReturnPending, maxFrames: 60);

            AssertThat(clearedWithoutExtraDelay)
                .OverrideFailureMessage(
                    "A staged return whose actors already cleared the show floor must emerge " +
                    "within a handful of frames, not wait an extra MinDelveShowSeconds — " +
                    "\"staged (Camp) runs unchanged\" regressed.")
                .IsTrue();

            // The group left the queue — now let the staggered file-out (mirrors departure's own
            // FileExitStaggerSeconds stagger) actually finish. Only the FIRST hero begins walking
            // in on the exact tick the group clears; the rest peel off staggered, same as
            // departure — asserting "all WalkingIn on this one tick" (the first version of this
            // test) was wrong about the design, not a regression in it. Bounded well under a
            // second show-floor wait, so a regression that gated the REST behind another
            // MinDelveShowSeconds would still be caught here.
            var allHome = await player.WaitUntil(
                () => partyIds.All(id => town.FindHeroActor(id.Value)?.State == HeroActor2D.HeroTownState.Wandering),
                maxFrames: 300);

            AssertThat(allHome)
                .OverrideFailureMessage("Survivors never completed their staggered walk-in back to Wandering.")
                .IsTrue();
        }
        finally
        {
            town.Free();
        }
    }

    // ── fixtures ──────────────────────────────────────────────────────────────────────────────

    private static Hero Delver(int id, string name, string classId) => new(
        new HeroId(id), name, classId, Level: 3, MaxHp: 30, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static ImmutableSortedDictionary<int, Hero> ThreeHeroes() =>
        ImmutableSortedDictionary<int, Hero>.Empty
            .Add(1, Delver(1, "V1", "vanguard"))
            .Add(2, Delver(2, "S1", "striker"))
            .Add(3, Delver(3, "M1", "mystic"));

    /// <summary>A finalized, all-survived <c>ExpeditionResult</c> for <paramref name="party"/> —
    /// enough shape for <c>Town2D.ReturnSurvivors</c> to read (Survivors/VenueId); the combat detail
    /// fields this unit's presentation-only code never reads are left empty.</summary>
    private static ExpeditionResult SurvivedRun(ImmutableList<HeroId> party, string venueId) => new(
        Party: party,
        TargetFloor: 1,
        DeepestFloorCleared: 1,
        Floors: ImmutableList<FloorOutcome>.Empty,
        Survivors: party,
        Deaths: ImmutableList<HeroId>.Empty,
        Beats: ImmutableList<AttributionBeat>.Empty,
        Loot: ImmutableList<OreLoot>.Empty,
        GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty,
        VenueId: venueId);
}
#endif
