#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Phase B: the alchemist's reagent-puzzle overlay — the single-action contract (one queued
/// <c>CraftAction</c> on Submit, zero on Cancel, PKD8), the puzzle payload riding
/// <c>CraftAction.Puzzle</c> with a null <c>PerformanceGrade</c> (the sim scores it), pour-cap
/// and undo behavior, and same-script determinism. PROPERTY-ONLY driving throughout: every
/// scenario calls the discrete input methods directly on an unmounted node — no frame pump, no
/// SubViewport, per the 3D-headless-hang rule (the overlay is turn-based and has no _Process
/// at all, so there is nothing to pump).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AlchemyBrewPuzzleTests
{
    private static readonly ProfessionDefinition Alchemy = AlchemyProfession.Definition;
    private static readonly Recipe ElixirRecipe = ProfessionRegistry.AllRecipes["alchemy-minor-elixir"];

    [TestCase]
    public void PerfectPour_EmitsExactlyOneAction_WithPuzzlePayload_AndNullGrade()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            var finishedCount = 0;
            CraftAction? emitted = null;
            brew.Finished += a => { finishedCount++; emitted = a; };

            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            var ideal = AlchemyPuzzleScorer.IdealSequenceFor(ElixirRecipe);
            foreach (var reagent in ideal)
            {
                brew.PourReagent(reagent);
            }

            brew.Submit();
            brew.Submit(); // double-submit must not double-fire (single-action contract)

            AssertThat(brew.Completed).IsTrue();
            AssertThat(finishedCount).IsEqual(1);
            AssertThat(emitted!.RecipeId).IsEqual("alchemy-minor-elixir");
            AssertThat(emitted.MaterialKey).IsEqual("copper");
            AssertThat(emitted.PerformanceGrade is null).IsTrue(); // the puzzle is the source; sim scores it
            var puzzle = emitted.Puzzle as AlchemyReagentPuzzle;
            AssertThat(puzzle is not null).IsTrue();
            AssertThat(puzzle!.Reagents.SequenceEqual(ideal)).IsTrue();
            AssertThat(emitted.SubScores!).ContainsExactly(1000, 1000, 1000); // scorer preview triple
        }
        finally
        {
            brew.Free(); // never parented — free directly, no leaked orphan
        }
    }

    [TestCase]
    public void Cancel_QueuesNothing_AndFurtherInputIsIgnored()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            var finishedCount = 0;
            var cancelledCount = 0;
            brew.Finished += _ => finishedCount++;
            brew.Cancelled += () => cancelledCount++;

            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            brew.PourReagent(AlchemyReagents.Sunpetal);
            brew.Cancel();
            brew.PourReagent(AlchemyReagents.Dewroot); // dead input after cancel
            brew.Submit();

            AssertThat(brew.WasCancelled).IsTrue();
            AssertThat(brew.Completed).IsFalse();
            AssertThat(cancelledCount).IsEqual(1);
            AssertThat(finishedCount).IsEqual(0);
            AssertThat(brew.EmittedAction is null).IsTrue();
        }
        finally
        {
            brew.Free();
        }
    }

    [TestCase]
    public void PourCap_UndoAndInvalidIds_BehaveDiscretely()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            AssertThat(brew.RequiredPours).IsEqual(3); // tier 1 → 3 pours

            brew.PourReagent(-1);  // invalid: ignored
            brew.PourReagent(99);  // invalid: ignored
            AssertThat(brew.Poured.Count).IsEqual(0);

            brew.PourReagent(AlchemyReagents.Sunpetal);
            brew.PourReagent(AlchemyReagents.Dewroot);
            brew.PourReagent(AlchemyReagents.Glimmercap);
            brew.PourReagent(AlchemyReagents.Voidsalt); // past the cap: ignored
            AssertThat(brew.Poured.Count).IsEqual(3);

            brew.UndoPour();
            AssertThat(brew.Poured.SequenceEqual(ImmutableList.Create(AlchemyReagents.Sunpetal, AlchemyReagents.Dewroot))).IsTrue();
        }
        finally
        {
            brew.Free();
        }
    }

    [TestCase]
    public void SameScriptTwice_ProducesIdenticalPayload_NoHiddenRandomness()
    {
        var first = RunScript();
        var second = RunScript();

        AssertThat(((AlchemyReagentPuzzle)second.Puzzle!).Reagents
            .SequenceEqual(((AlchemyReagentPuzzle)first.Puzzle!).Reagents)).IsTrue();
        AssertThat(second.SubScores!).ContainsExactly(first.SubScores!);

        static CraftAction RunScript()
        {
            var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
            try
            {
                brew.Configure(ElixirRecipe, "copper", Alchemy,
                    ImmutableSortedSet.Create(AlchemyProfession.MeasuredPour));
                brew.PourReagent(AlchemyReagents.Glimmercap); // deliberately sloppy
                brew.PourReagent(AlchemyReagents.Dewroot);
                brew.PourReagent(AlchemyReagents.Voidsalt);
                brew.Submit();
                return brew.EmittedAction!;
            }
            finally
            {
                brew.Free();
            }
        }
    }

    [TestCase]
    public void Reconfigure_ResetsToACleanRun()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            brew.PourReagent(AlchemyReagents.Sunpetal);
            brew.Submit();
            AssertThat(brew.Completed).IsTrue();

            brew.Configure(ElixirRecipe, "iron", Alchemy, ImmutableSortedSet<string>.Empty);
            AssertThat(brew.Completed).IsFalse();
            AssertThat(brew.Poured.Count).IsEqual(0);
            AssertThat(brew.MaterialKey).IsEqual("iron");
            AssertThat(brew.EmittedAction is null).IsTrue();
        }
        finally
        {
            brew.Free();
        }
    }
}
#endif
