#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
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
}
#endif
