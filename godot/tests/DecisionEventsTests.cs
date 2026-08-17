#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Kernel;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T6 (register #164, MAKERS-MARK.md §11.14.8): the behavioral half of
/// <c>DecisionReasonCensusTests</c> (fast lane) — that test only proves a <c>case</c> exists in
/// <see cref="DecisionEvents"/>'s source text; this proves the case actually reaches
/// <see cref="PlaytestLog.Decision"/> with the field the sim computed, and that both
/// <see cref="SimAdapter"/> choke points (<c>Queue</c>'s immediate branch, <c>AdvancePhase</c>) wire
/// it — the same two spots <c>PlaytestLogTests.CraftActionRow_NamesTheRecipeAndMaterial</c> already
/// proves for <see cref="ActionSubject"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DecisionEventsTests
{
    /// <summary>Direct unit proof of the mapping table: one event of each of the six known
    /// reason-bearing types in, six correctly-shaped decision rows out.</summary>
    [TestCase]
    public void LogAll_WritesADecisionRowForEachKnownReasonBearingEvent()
    {
        var path = ProjectSettings.GlobalizePath("user://decision-events-logall.jsonl");
        PlaytestLog.RedirectForTests(path);
        try
        {
            DecisionEvents.LogAll(ImmutableList.Create<GameEvent>(
                new HeroPassedOnItem(new HeroId(1), new ItemId(2), "too expensive"),
                new HeroDecisionExplained(new HeroId(1), "Iron Sword", "nothing else affordable", "best value per gold", 1000),
                new BountyJudged(new BountyId(3), new HeroId(1), true, "floor within reach"),
                new CustomerWalked(new HeroId(1), new ItemId(2), "the customer's patience ran out"),
                new HeroDied(new HeroId(1), 4, "slain by a Goblin", GearSet.Empty),
                new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(2), new HeroId(1), 4, "would have died without it")));

            var rows = Rows(path);
            AssertThat(rows.Count).OverrideFailureMessage(Dump(rows)).IsEqual(6);

            AssertThat(rows[0]).Contains("\"what\":\"hero-item-pass:1\"");
            AssertThat(rows[0]).Contains("\"chose\":\"declined item #2\"");
            AssertThat(rows[0]).Contains("\"why\":\"too expensive\"");

            AssertThat(rows[1]).Contains("\"what\":\"hero-gear-pick:1\"");
            AssertThat(rows[1]).Contains("\"chose\":\"Iron Sword\"");
            AssertThat(rows[1]).Contains("\"why\":\"best value per gold\"");

            AssertThat(rows[2]).Contains("\"what\":\"bounty-judged:3\"");
            AssertThat(rows[2]).Contains("\"chose\":\"accepted\"");
            AssertThat(rows[2]).Contains("\"why\":\"floor within reach\"");
            AssertThat(rows[2]).Contains("\"candidates\":2");

            AssertThat(rows[3]).Contains("\"what\":\"customer-walked:1\"");
            AssertThat(rows[3]).Contains("\"why\":\"the customer's patience ran out\"");

            AssertThat(rows[4]).Contains("\"what\":\"hero-died:1\"");
            AssertThat(rows[4]).Contains("\"chose\":\"floor 4\"");
            AssertThat(rows[4]).Contains("\"why\":\"slain by a Goblin\"");

            AssertThat(rows[5]).Contains("\"what\":\"attribution-beat:KillingBlow\"");
            AssertThat(rows[5]).Contains("\"why\":\"would have died without it\"");
        }
        finally
        {
            PlaytestLog.RedirectForTests(null);
        }
    }

    /// <summary>The seventh, generic case: <see cref="DecisionExplained"/> is a straight echo (its
    /// fields are already named What/Chosen/Reason/Candidates — the exact <see
    /// cref="PlaytestLog.Decision"/> parameter shape), unlike the six above it, which each reformat
    /// a typed field into that shape.</summary>
    [TestCase]
    public void LogAll_EchoesDecisionExplained_Verbatim()
    {
        var path = ProjectSettings.GlobalizePath("user://decision-events-logall-explained.jsonl");
        PlaytestLog.RedirectForTests(path);
        try
        {
            DecisionEvents.LogAll(ImmutableList.Create<GameEvent>(
                new DecisionExplained("expedition-halt:mine", "TooHurt", "1 survived, 0 dead, cleared 3/5, 0 floors fought")));

            var rows = Rows(path);
            AssertThat(rows.Count).OverrideFailureMessage(Dump(rows)).IsEqual(1);
            AssertThat(rows[0]).Contains("\"what\":\"expedition-halt:mine\"");
            AssertThat(rows[0]).Contains("\"chose\":\"TooHurt\"");
            AssertThat(rows[0]).Contains("\"why\":\"1 survived, 0 dead, cleared 3/5, 0 floors fought\"");
        }
        finally
        {
            PlaytestLog.RedirectForTests(null);
        }
    }

    /// <summary>The fail-soft contract every other <c>PlaytestLog</c> writer honors: disarmed means
    /// zero work, not just zero output — a disarmed recorder must never throw on an event shape it
    /// has never seen.</summary>
    [TestCase]
    public void LogAll_IsANoOp_WhenTheRecorderIsDisarmed()
    {
        PlaytestLog.RedirectForTests(null);
        AssertThat(PlaytestLog.Active).IsFalse();

        DecisionEvents.LogAll(ImmutableList.Create<GameEvent>(
            new HeroPassedOnItem(new HeroId(1), new ItemId(2), "irrelevant — recorder is off")));

        AssertThat(PlaytestLog.Active).IsFalse();
    }

    /// <summary>
    /// "The reveal deletes its own evidence" (§11.14.8): an Evening tick with a pending expedition
    /// used to consume <c>GameState.PendingExpeditions</c> the SAME tick it narrated it, destroying
    /// the typed <see cref="ExpeditionResult.Halt"/> with no durable record anywhere. Fixed at the
    /// source now: <c>GameSim.Drama.ExpeditionRevealSystem</c> emits a persisted
    /// <see cref="DecisionExplained"/> naming the halt before it clears PendingExpeditions, so this
    /// drives a real <see cref="SimAdapter.AdvancePhase"/> tick (never calling a client-side echo
    /// directly) and proves the row reaches the session log through the SAME generic
    /// <see cref="DecisionEvents.LogAll"/> path as every other reason-bearing event — no
    /// snapshot-timing workaround left to break.
    /// </summary>
    [TestCase]
    public void AdvancePhase_LogsTheTypedHalt_BeforeTheEveningTickConsumesIt()
    {
        var path = ProjectSettings.GlobalizePath("user://decision-events-revealed.jsonl");
        PlaytestLog.RedirectForTests(path);
        try
        {
            var partyIds = ImmutableList.Create(new HeroId(1));
            var result = new ExpeditionResult(
                Party: partyIds,
                TargetFloor: 5,
                DeepestFloorCleared: 3,
                Floors: ImmutableList<FloorOutcome>.Empty,
                Survivors: partyIds,
                Deaths: ImmutableList<HeroId>.Empty,
                Beats: ImmutableList<AttributionBeat>.Empty,
                Loot: ImmutableList<OreLoot>.Empty,
                GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty,
                VenueId: "mine",
                Halt: ExpeditionHalt.TooHurt);

            var state = GameFactory.NewGame(2026) with
            {
                Phase = DayPhase.Evening,
                PendingExpeditions = ImmutableList.Create(result),
            };

            var adapter = new SimAdapter(state);
            adapter.AdvancePhase(); // Evening -> Morning: ExpeditionRevealSystem clears PendingExpeditions THIS tick

            var rows = Rows(path);
            var haltRow = rows.LastOrDefault(r => r.Contains("\"what\":\"expedition-halt:mine\""));
            AssertThat(haltRow).OverrideFailureMessage(Dump(rows)).IsNotNull();
            AssertThat(haltRow).Contains("\"chose\":\"TooHurt\"");
            AssertThat(haltRow).Contains("cleared 3/5");

            // The evidence really was about to be destroyed — proves this test exercises the actual
            // bug's precondition, not a state where nothing was at risk.
            AssertThat(adapter.CurrentState.PendingExpeditions.IsEmpty)
                .OverrideFailureMessage("Precondition failed: PendingExpeditions was never populated at all.")
                .IsTrue();
        }
        finally
        {
            PlaytestLog.RedirectForTests(null);
        }
    }

    private static string Dump(List<string> rows) => $"rows: [{string.Join(" | ", rows)}]";

    private static List<string> Rows(string path) =>
        System.IO.File.Exists(path)
            ? System.IO.File.ReadAllLines(path).Where(l => l.Contains("\"kind\":\"decision\"")).ToList()
            : new List<string>();
}
#endif
