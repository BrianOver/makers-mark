#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U3 (tutorial-revamp plan, §11.13): <see cref="SurfaceUnlocks"/> is a pure function of <see
/// cref="GameState"/> — no Godot scene needed to test it (mirrors <see
/// cref="ActionReachabilityCensusTests"/>'s own no-runtime shape). The ONE test that needs a live
/// mounted scene — the "no gate may ever hide a tutorial anchor" pin — lives in <see
/// cref="TutorialRegistryConformanceTests"/> instead, alongside that suite's own established
/// drive-the-real-chain helpers.
/// </summary>
[TestSuite]
public class SurfaceUnlocksTests
{
    /// <summary>Deny-by-default census, mirroring <see cref="ActionReachabilityCensusTests"/>'
    /// own idiom: the SET of gated surfaces must be exactly the seven the plan names — never fewer
    /// (a book that should be gated but was forgotten) and never more (a book gated that should
    /// stay always-open). "Lessons" is the one tray book explicitly, permanently ungated.</summary>
    [TestCase]
    public void EverySurfaceInTheTray_HasAGateOrAnExplicitAlwaysOpen()
    {
        var expectedGated = new[] { "Ledger", "Forecast", "HeroCards", "Commissions", "Demand", "Legends", "Progress" };

        AssertThat(SurfaceUnlocks.Gates.Select(g => g.SurfaceId).ToHashSet().SetEquals(expectedGated))
            .OverrideFailureMessage(
                $"SurfaceUnlocks.Gates names [{string.Join(", ", SurfaceUnlocks.Gates.Select(g => g.SurfaceId))}], " +
                $"expected exactly [{string.Join(", ", expectedGated)}].")
            .IsTrue();

        AssertThat(SurfaceUnlocks.GateFor("Lessons"))
            .OverrideFailureMessage("Lessons must never be gated — dismissing/finishing the tutorial must not take it away.")
            .IsNull();
    }

    [TestCase]
    public void Ledger_IsClosed_BeforeTheFirstDeparture_AndOpen_After()
    {
        var fresh = GameFactory.NewGame(2026);
        AssertThat(SurfaceUnlocks.IsOpen(fresh, "Ledger"))
            .OverrideFailureMessage("Ledger reads open before any party has ever departed.")
            .IsFalse();

        var departed = fresh with
        {
            EventLog = ImmutableList.Create<GameEvent>(new PartyDeparted(ImmutableList.Create(new HeroId(1)), TargetFloor: 1)),
        };
        AssertThat(SurfaceUnlocks.IsOpen(departed, "Ledger")).IsTrue();
    }

    [TestCase]
    public void Legends_IsClosed_UntilTheFirstAttributionBeat()
    {
        var fresh = GameFactory.NewGame(2026);
        AssertThat(SurfaceUnlocks.IsOpen(fresh, "Legends")).IsFalse();

        var beat = fresh with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), new HeroId(1), Floor: 1, Detail: "test")),
        };
        AssertThat(SurfaceUnlocks.IsOpen(beat, "Legends")).IsTrue();
    }

    /// <summary>§11.13 amendment (U6, test scenario 7): an unattributed first death — no
    /// player-marked item on the fallen — must still open the wall on the exact night the
    /// tutorial's dormant loss act points there. LegendsWall itself renders memorials regardless of
    /// attribution, so this gate widening is honest, not generous.</summary>
    [TestCase]
    public void LegendsGate_OpensOnFirstHeroDied_EvenWithNoAttributionBeat()
    {
        var fresh = GameFactory.NewGame(2026);
        AssertThat(SurfaceUnlocks.IsOpen(fresh, "Legends")).IsFalse();

        var died = fresh with
        {
            EventLog = ImmutableList.Create<GameEvent>(new HeroDied(
                new HeroId(1), Floor: 1, Cause: "slain by a Crypt Crab", WornGear: GearSet.Empty)),
        };
        AssertThat(SurfaceUnlocks.IsOpen(died, "Legends"))
            .OverrideFailureMessage("An unattributed first death must still open Legends — the wall has someone to remember.")
            .IsTrue();
    }

    /// <summary>
    /// P2-HONEST-01: the defect this unit exists to fix. Before the fix, Progress's own gate read
    /// <c>state.Bounties.Any(b => b.Paid)</c> — permanently false, since <see cref="Bounty.Paid"/>
    /// is never set true by the real sim (<c>BountyPayoutSystem</c> removes a paid bounty from the
    /// board instead of ever flipping the flag). Drives a REAL campaign through the full kernel
    /// until an actual bounty pays out — the same day-loop
    /// <c>GameSim.Tests.Bounties.BountyTests.CompletedBounty_PaysAcceptingHero_ExactlyOnce</c> uses
    /// — so this proves the gate opens from a genuine scripted payout, never a hand-fabricated
    /// event standing in for one.
    /// </summary>
    [TestCase]
    public void Progress_IsClosed_OnAFreshCampaign_AndOpensAfterAScriptedBountyPayout()
    {
        var kernel = GameComposition.BuildKernel();
        var state = GameComposition.NewCampaign(2026);

        AssertThat(SurfaceUnlocks.IsOpen(state, "Progress"))
            .OverrideFailureMessage("Progress reads open on a fresh campaign before any bounty has ever been paid.")
            .IsFalse();

        var posted = kernel.Tick(state, ImmutableList.Create<PlayerAction>(new PostBountyAction(TargetFloor: 1, RewardGold: 40)));
        state = posted.NewState;

        BountyPaid? paid = null;
        for (var day = 0; day < 15 && paid is null; day++)
        {
            var result = kernel.Tick(state, ImmutableList<PlayerAction>.Empty);
            state = result.NewState;
            paid = result.Events.OfType<BountyPaid>().FirstOrDefault();
        }

        AssertThat(paid)
            .OverrideFailureMessage("No bounty paid out within 15 days -- the fixture is broken, not the gate.")
            .IsNotNull();
        AssertThat(SurfaceUnlocks.IsOpen(state, "Progress"))
            .OverrideFailureMessage("A real BountyPaid event landed in the log, but Progress still reads closed.")
            .IsTrue();
    }

    /// <summary>No <c>user://</c> persistence exists for any gate (KTD2) — a "reload" is just a
    /// FRESH <see cref="GameState"/> instance carrying the identical campaign history, and it must
    /// re-derive the identical verdict rather than depending on any flag written by an earlier
    /// call.</summary>
    [TestCase]
    public void AGatedSurface_ReDerivesItsGate_AfterAReload()
    {
        var history = ImmutableList.Create<GameEvent>(new PartyDeparted(ImmutableList.Create(new HeroId(1)), TargetFloor: 1));
        var first = GameFactory.NewGame(2026) with { EventLog = history };
        AssertThat(SurfaceUnlocks.IsOpen(first, "Ledger")).IsTrue();

        var reloaded = GameFactory.NewGame(2026) with { EventLog = history }; // a SEPARATE instance, same facts
        AssertThat(SurfaceUnlocks.IsOpen(reloaded, "Ledger"))
            .OverrideFailureMessage("A freshly-constructed GameState carrying the identical history read the gate differently.")
            .IsTrue();
    }

    /// <summary>Forecast's own gate is the one that reads <see cref="GameState.Day"/>/<see
    /// cref="GameState.Phase"/> directly rather than an EventLog entry — the case worth checking
    /// explicitly for monotonicity, since an EventLog-backed gate is monotonic by construction (a
    /// real campaign's own log only ever grows).</summary>
    [TestCase]
    public void NoGate_EverClosesASurfaceItPreviouslyOpened()
    {
        var evening = GameFactory.NewGame(2026) with { Phase = DayPhase.Evening };
        AssertThat(SurfaceUnlocks.IsOpen(evening, "Forecast")).IsTrue();

        var laterMorning = evening with { Day = evening.Day + 5, Phase = DayPhase.Morning };
        AssertThat(SurfaceUnlocks.IsOpen(laterMorning, "Forecast"))
            .OverrideFailureMessage("Forecast re-closed once Day advanced and Phase left Evening — not monotonic.")
            .IsTrue();
    }

    /// <summary>Every gate names a real, non-empty reason — the tooltip/toast text a closed
    /// surface's button shows, and the string <see cref="ActionReachabilityCensusTests"/>' own
    /// gated Surfaces entries quote from (its own doc: "each gated entry must carry its gate in
    /// the surface string").</summary>
    [TestCase]
    public void EveryGate_NamesANonEmptyReason()
    {
        foreach (var gate in SurfaceUnlocks.Gates)
        {
            AssertThat(string.IsNullOrWhiteSpace(gate.Reason))
                .OverrideFailureMessage($"{gate.SurfaceId}'s gate carries no reason — a closed tray button would grey out with a blank tooltip.")
                .IsFalse();
        }
    }

    /// <summary>
    /// U13 (§11.14.14): <see cref="SurfaceUnlocks.ForcedOpenByAnchor"/> generalizes the "never hide
    /// a tutorial anchor" protection past the two Hud-anchored rows (HeroCards/Commissions) it was
    /// hardcoded to before this unit. Proves it for "Demand" — one of the seven gates neither
    /// existing tutorial row ever points at — via BOTH naming conventions a future row could use,
    /// with no live UI or GameState needed: this is a pure function of an anchor and a surface id.
    /// </summary>
    [TestCase]
    public void ForcedOpenByAnchor_ProtectsAGatedSurface_OtherThanTheTwoHardcodedRows()
    {
        // A Hud anchor named by MainUi's own "Open{surfaceId}" tray-button convention.
        AssertThat(SurfaceUnlocks.ForcedOpenByAnchor(TutorialAnchor.ForHud("OpenDemand"), "Demand"))
            .OverrideFailureMessage(
                "A Hud anchor named \"OpenDemand\" must force the Demand surface open — this is the " +
                "exact protection HeroCards/Commissions already get, generalized to a THIRD surface " +
                "with no new case in SurfaceUnlocks or MainUi.")
            .IsTrue();

        // A PanelControl anchor scoped directly to the panel by its own registered id — the shape
        // Wave A shipped with no registry row using it yet (TutorialAnchorKind.PanelControl's own
        // doc). This is the gap the OLD Hud-only check could not have closed at all.
        AssertThat(SurfaceUnlocks.ForcedOpenByAnchor(TutorialAnchor.ForPanelControl("Demand", "SomeButton"), "Demand"))
            .OverrideFailureMessage(
                "A PanelControl anchor scoped to the \"Demand\" panel must force the Demand surface " +
                "open too — a future beat pointing a control INSIDE a gated panel needs the identical " +
                "protection a Hud-anchored beat already gets.")
            .IsTrue();

        // Neither convention should force open a DIFFERENT surface than the one it names.
        AssertThat(SurfaceUnlocks.ForcedOpenByAnchor(TutorialAnchor.ForHud("OpenDemand"), "Legends")).IsFalse();
        AssertThat(SurfaceUnlocks.ForcedOpenByAnchor(TutorialAnchor.ForPanelControl("Demand", "SomeButton"), "Legends")).IsFalse();

        // A Building/Station/None anchor never names a tray surface at all — honestly false, never
        // a guess (the class doc's "disjoint vocabularies" reasoning).
        AssertThat(SurfaceUnlocks.ForcedOpenByAnchor(TutorialAnchor.ForBuilding("market"), "Demand")).IsFalse();
        AssertThat(SurfaceUnlocks.ForcedOpenByAnchor(TutorialAnchor.None, "Demand")).IsFalse();
    }
}
#endif
