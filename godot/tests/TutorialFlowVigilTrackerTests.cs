#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Kernel;
using GameSim.Venues;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// P2-PEOPLE-24 ("the tracker knows the vigil is the moment, not a shut vendor") — serves decision 6
/// (send the runner, or trust their judgment). Before this unit, <see cref="TutorialFlow"/>'s WaitText
/// answered strictly for whichever numbered step the chain happened to be parked on: a chain still on
/// BuyMaterial during <see cref="DayPhase.Camp"/> printed the material vendor's Morning-only excuse
/// over the one screen the whole day exists for, with a real party camped below the checkpoint and
/// nothing in the tracker naming it. Pins the fix as a PROPERTY over a table of camped-party SHAPES
/// (party size, checkpoint floor, heals-of-yours) — the same table idiom <c>PartyVoiceTests</c>
/// established for this exact underlying data — crossed with every phase-gated step whose own WaitText
/// branch could otherwise leak its excuse onto a live vigil, never one hand-picked fixture.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialFlowVigilTrackerTests
{
    private const string MineId = VenueRegistry.MineId;

    /// <summary>Every step whose own WaitText branch checks <see cref="GameState.Phase"/> — the
    /// property must hold no matter which of these is the chain's current step, since the reported
    /// defect was current on BuyMaterial specifically, not on Vigil.</summary>
    private static readonly TutorialStep[] PhaseGatedSteps =
    {
        TutorialStep.BuyMaterial, TutorialStep.PostBounty, TutorialStep.OpenCounter,
    };

    // ── A table of camped-party SHAPES — every positive test below iterates the whole table ───────

    private sealed record Scenario(int CheckpointFloor, int TargetFloor, string[] Names, int[] YoursHeals);

    private static IEnumerable<Scenario> Scenarios()
    {
        // Solo camper, no heals of the player's own at all.
        yield return new Scenario(1, 2, new[] { "Torvald" }, new[] { 0 });
        // Two heroes, one player-crafted heal between them.
        yield return new Scenario(1, 3, new[] { "Torvald", "Kael" }, new[] { 1, 0 });
        // Three heroes, deeper checkpoint, none of the heals are the player's own.
        yield return new Scenario(2, 5, new[] { "Torvald", "Kael", "Sable" }, new[] { 0, 0, 0 });
        // Three heroes, several player-crafted heals spread unevenly.
        yield return new Scenario(3, 5, new[] { "Torvald", "Kael", "Sable" }, new[] { 2, 1, 0 });
    }

    // ── 1. A live vigil outranks whatever the CURRENT step's own excuse would have said ────────────

    [TestCase]
    public void CopyFor_EveryPhaseGatedStep_NamesTheVigil_WhenAPartyIsActuallyCamped()
    {
        var tutorial = new TutorialFlow();
        try
        {
            foreach (var scenario in Scenarios())
            {
                var (state, _) = Build(scenario);

                foreach (var step in PhaseGatedSteps)
                {
                    var copy = tutorial.CopyFor(step, state);
                    var context = $"step={step} checkpoint={scenario.CheckpointFloor} party=[{string.Join(",", scenario.Names)}]";

                    AssertThat(copy.Contains("vigil", StringComparison.OrdinalIgnoreCase))
                        .OverrideFailureMessage($"[{context}] tracker did not name the vigil over a real camp. Line: \"{copy}\"")
                        .IsTrue();
                    AssertThat(copy.Contains("only trades in the Morning", StringComparison.Ordinal))
                        .OverrideFailureMessage($"[{context}] the shut-vendor excuse leaked onto a live vigil. Line: \"{copy}\"")
                        .IsFalse();

                    foreach (var name in scenario.Names)
                    {
                        AssertThat(copy.Contains(name, StringComparison.Ordinal))
                            .OverrideFailureMessage($"[{context}] camped party member \"{name}\" is not named. Line: \"{copy}\"")
                            .IsTrue();
                    }
                }
            }
        }
        finally
        {
            tutorial.Free();
        }
    }

    // ── 2. Honesty direction: Camp phase with nobody actually camped never claims a vigil ──────────

    [TestCase]
    public void CopyFor_CampPhaseWithNoPartyCamped_NeverClaimsAVigil()
    {
        // RaidConductor.cs: DayPhase.Camp can run with an EMPTY InFlight (Beat.DeepTick) — Camp
        // reached, nobody there. This is the shape that must not be mistaken for a live vigil, or
        // the fix trades one dishonest line ("the vendor's shut") for another ("the vigil holds")
        // over an empty checkpoint.
        var state = GameFactory.NewGame(1) with
        {
            Phase = DayPhase.Camp,
            InFlight = ImmutableList<InFlightExpedition>.Empty,
        };

        AssertThat(TutorialFlow.VigilInProgressTextForTests(state))
            .OverrideFailureMessage("Camp with an empty InFlight must not produce a vigil line at all.")
            .IsNull();

        var tutorial = new TutorialFlow();
        try
        {
            foreach (var step in PhaseGatedSteps)
            {
                var copy = tutorial.CopyFor(step, state);
                AssertThat(copy.Contains("vigil", StringComparison.OrdinalIgnoreCase))
                    .OverrideFailureMessage($"[step={step}] Camp with nobody camped falsely claimed a vigil. Line: \"{copy}\"")
                    .IsFalse();
            }
        }
        finally
        {
            tutorial.Free();
        }
    }

    // ── 3. Negative control: the vendor excuse still shows when it is genuinely the reason ─────────

    [TestCase]
    public void CopyFor_BuyMaterial_StillNamesTheShutVendor_OutsideOfAnyVigil()
    {
        var state = GameFactory.NewGame(1) with
        {
            Phase = DayPhase.Evening,
            InFlight = ImmutableList<InFlightExpedition>.Empty,
        };
        var tutorial = new TutorialFlow();
        try
        {
            var copy = tutorial.CopyFor(TutorialStep.BuyMaterial, state);
            AssertThat(copy.Contains("only trades in the Morning", StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"BuyMaterial's own Morning-only excuse must still show when a shut vendor really is the reason " +
                    $"(no vigil in play here at all). Line: \"{copy}\"")
                .IsTrue();
        }
        finally
        {
            tutorial.Free();
        }
    }

    // ── 4. Influence never orders (Law 1): no clause opens on a bare command verb ───────────────────

    /// <summary>Same deny-by-property idiom <c>AdvisorNeverOrdersTests.ImperativeVerbs</c> and
    /// <c>PartyVoiceTests.ImperativeSentenceStarts</c> already check their own generated lines
    /// against — every verb the vigil's own three buttons could be phrased with, plus the generic
    /// imperatives a rewrite could reach for instead.</summary>
    private static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Send", "Recall", "Bring", "Give", "Go", "Use", "Buy", "Sell", "Forge", "Craft", "Pay",
        "Wait", "Hurry", "Keep", "Take", "Leave", "Hold", "Ring", "Answer", "Spend", "Trust",
    };

    /// <summary>Same clause boundary as <c>AdvisorNeverOrdersTests.ClauseSplit</c> — an imperative
    /// planted after an em dash or a full stop is exactly as much of an order as one that opens the
    /// whole line.</summary>
    private static readonly Regex ClauseSplit = new(@"(?:\. |; | — |—)", RegexOptions.Compiled);

    [TestCase]
    public void VigilInProgressText_EveryScenario_NeverPhrasesAnOrder()
    {
        foreach (var scenario in Scenarios())
        {
            var (state, _) = Build(scenario);
            var line = TutorialFlow.VigilInProgressTextForTests(state);

            AssertThat(line).OverrideFailureMessage("A camped scenario must always produce a vigil line.").IsNotNull();

            AssertThat(line!.Contains("you must", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("you should", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("you need to", StringComparison.OrdinalIgnoreCase))
                .OverrideFailureMessage($"Line phrases a second-person directive: \"{line}\"")
                .IsFalse();

            foreach (var clause in ClauseSplit.Split(line))
            {
                var trimmed = clause.TrimStart('*', ' ', '\'', '"');
                var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    ?.TrimEnd('.', ',', ':', '\'');
                if (firstWord is null)
                {
                    continue;
                }

                AssertThat(ImperativeVerbs.Contains(firstWord))
                    .OverrideFailureMessage(
                        $"Clause \"{trimmed}\" opens on a bare command verb (\"{firstWord}\") — influence never " +
                        $"orders (Law 1). Full line: \"{line}\"")
                    .IsFalse();
            }

            // "it waits on you" is the register (no timer implied) — a regression that swaps in
            // urgency language would still pass every check above, so pin the phrase directly too.
            AssertThat(line.Contains("waits on you", StringComparison.Ordinal))
                .OverrideFailureMessage($"Vigil line dropped the no-timer register (\"it waits on you\"). Line: \"{line}\"")
                .IsTrue();
        }
    }

    // ── Fixture builder — one camped-party SHAPE in, GameState (Camp phase, that party InFlight) out ─

    private static (GameState State, InFlightExpedition Party) Build(Scenario scenario)
    {
        var partyIds = Enumerable.Range(1, scenario.Names.Length).Select(id => new HeroId(id)).ToImmutableList();

        var heroes = ImmutableSortedDictionary<int, Hero>.Empty;
        var hp = ImmutableSortedDictionary<int, int>.Empty;
        var items = ImmutableSortedDictionary<int, Item>.Empty;
        var packs = ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty;
        var nextItemId = 1000;

        for (var i = 0; i < scenario.Names.Length; i++)
        {
            var memberId = i + 1;
            heroes = heroes.Add(memberId, MakeHero(memberId, scenario.Names[i]));
            hp = hp.Add(memberId, 10);

            var pack = ImmutableList<ItemId>.Empty;
            for (var y = 0; y < scenario.YoursHeals[i]; y++)
            {
                var id = nextItemId++;
                items = items.Add(id, MakeHeal(id));
                pack = pack.Add(new ItemId(id));
            }

            packs = packs.Add(memberId, pack);
        }

        var party = new InFlightExpedition(
            Party: partyIds,
            TargetFloor: scenario.TargetFloor,
            CheckpointFloor: scenario.CheckpointFloor,
            VenueId: MineId,
            Hp: hp,
            Packs: packs,
            Gold: ImmutableSortedDictionary<int, int>.Empty,
            Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Loot: ImmutableList<OreLoot>.Empty,
            DeepestFloorCleared: scenario.CheckpointFloor);

        var state = GameFactory.NewGame(1) with
        {
            Heroes = heroes,
            Items = items,
            Phase = DayPhase.Camp,
            InFlight = ImmutableList.Create(party),
        };

        return (state, party);
    }

    private static Hero MakeHero(int id, string name) => new(
        new HeroId(id), name, ClassRegistry.VanguardId, Level: 3, MaxHp: 20, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    private static Item MakeHeal(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));
}
#endif
