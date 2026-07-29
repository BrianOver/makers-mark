#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Phase B: the alchemist's reagent-puzzle overlay — the single-action contract (one queued
/// <c>CraftAction</c> on Submit, zero on Cancel, PKD8), the puzzle payload riding
/// <c>CraftAction.Puzzle</c> with a null <c>PerformanceGrade</c> (the sim scores it), pour-cap
/// and undo behavior, and same-script determinism. Most scenarios call the discrete input methods
/// directly on an unmounted node — no frame pump, no SubViewport, per the 3D-headless-hang rule
/// (the overlay is turn-based and has no _Process at all, so there is nothing to pump).
///
/// <para><b>U4 additions</b> (plan 2026-07-28-002) drive the drag-to-pour recogniser through REAL
/// synthesized input: <c>BrewCanvas</c> wires its handler via the <c>GuiInput</c> C# event rather
/// than overriding <c>_GuiInput</c> — the same idiom <c>DrawerHost</c>'s dim veil uses — so a test
/// can fire it with <c>EmitSignal(Control.SignalName.GuiInput, ...)</c> exactly like
/// <c>UiTestSupport.Click</c> does elsewhere, with no mouse and no SubViewport needed. The canvas
/// is found by name via <c>UiTestSupport.Find</c> on the (still unmounted) node tree the puzzle
/// already builds in <c>EnsureBuilt</c>.</para>
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

    // ── U4: drag-to-pour ──
    //
    // BrewCanvas seeds a deterministic 600x340 Size in EnsureBuilt (the drawer's real footprint)
    // precisely so these hit-tests and shelf coordinates are meaningful on an unmounted node with
    // no live container layout pass. The shelf/book coordinates below mirror BrewCanvas's own
    // private layout constants (ShelfStep=20, ShelfY=12, shelf x0 = Size.X - ShelfStep*6 - 10 =
    // 470; the recipe-book rect is fixed at (8, Size.Y-34, 26, 26)) — a deliberate layout change
    // there is expected to require updating these numbers too.

    [TestCase]
    public void IsOverCauldron_TrueOverThePot_FalseOverTheShelfOrEdges()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);

            AssertThat(brew.IsOverCauldron(new Vector2(300, 200))).IsTrue();  // inside the pot/mouth
            AssertThat(brew.IsOverCauldron(new Vector2(480, 20))).IsFalse(); // over the bottle shelf
            AssertThat(brew.IsOverCauldron(Vector2.Zero)).IsFalse();          // top-left edge
            AssertThat(brew.IsOverCauldron(new Vector2(599, 0))).IsFalse();  // top-right edge
            AssertThat(brew.IsOverCauldron(new Vector2(20, 320))).IsFalse(); // over the recipe book
        }
        finally
        {
            brew.Free();
        }
    }

    [TestCase]
    public void DragRelease_OverCauldron_ProducesTheSamePouredState_AsCallingPourReagentDirectly()
    {
        var direct = new GodotClient.Minigames.AlchemyBrewPuzzle();
        var dragged = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            direct.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            dragged.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);

            var ideal = AlchemyPuzzleScorer.IdealSequenceFor(ElixirRecipe);
            var reagentId = ideal[0];
            direct.PourReagent(reagentId); // the existing seam, called the way the palette button does

            // The SAME gesture via the real GuiInput-signal seam: pick up reagentId's shelf
            // bottle, carry it, release over the cauldron.
            var canvas = UiTestSupport.Find<Control>(dragged, "BrewCanvas");
            var shelfPos = new Vector2(470f + 20f * (reagentId + 0.5f), 20f);
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = shelfPos });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = new Vector2(300f, 200f) });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(300f, 200f) });

            AssertThat(dragged.Poured.SequenceEqual(direct.Poured)).IsTrue();
            AssertThat(dragged.Poured.Count).IsEqual(1);
        }
        finally
        {
            direct.Free();
            dragged.Free();
        }
    }

    [TestCase]
    public void DragRelease_NotOverCauldron_ShelvesHarmlessly_NoStateChange()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            var ideal = AlchemyPuzzleScorer.IdealSequenceFor(ElixirRecipe);
            var reagentId = ideal[0];

            var canvas = UiTestSupport.Find<Control>(brew, "BrewCanvas");
            var shelfPos = new Vector2(470f + 20f * (reagentId + 0.5f), 20f);
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = shelfPos });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = Vector2.Zero }); // off the pot

            AssertThat(brew.Poured.IsEmpty).IsTrue();
            AssertThat(brew.Completed).IsFalse();
            AssertThat(brew.WasCancelled).IsFalse(); // a miss is not an error state either
        }
        finally
        {
            brew.Free();
        }
    }

    [TestCase]
    public void Reconfigure_MidDrag_ClearsTheCarriedBottle_SoTheStaleReleaseDoesNothing()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            var canvas = UiTestSupport.Find<Control>(brew, "BrewCanvas");
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(480f, 20f) });

            brew.Configure(ElixirRecipe, "iron", Alchemy, ImmutableSortedSet<string>.Empty); // reopens mid-drag

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(300f, 200f) });

            AssertThat(brew.Poured.IsEmpty).IsTrue(); // the stale pick-up was cleared, not re-armed
        }
        finally
        {
            brew.Free();
        }
    }

    // ── U4: memory depth ──

    [TestCase]
    public void IsOverRecipeBook_TrueOverTheIcon_FalseElsewhere()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);

            AssertThat(brew.IsOverRecipeBook(new Vector2(20, 320))).IsTrue();  // inside the book icon
            AssertThat(brew.IsOverRecipeBook(new Vector2(300, 200))).IsFalse(); // over the pot instead
        }
        finally
        {
            brew.Free();
        }
    }

    [TestCase]
    public void NotesFading_NeverChangesTheEmittedAction_AcrossRepeatBrewsOfTheSameRecipe()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            AssertThat(brew.NotesFamiliar).IsFalse(); // first brew this session — notes show in full
            AssertThat(brew.NotesVisible).IsTrue();

            var ideal = AlchemyPuzzleScorer.IdealSequenceFor(ElixirRecipe);
            foreach (var reagent in ideal)
            {
                brew.PourReagent(reagent);
            }

            brew.Submit();
            var first = brew.EmittedAction!;
            AssertThat(brew.NotesFamiliar).IsTrue(); // now brewed once this session

            // Reopen for the SAME recipe: notes are faded by default, but the hover fallback is a
            // zero-cost, no-timing way back to full detail — and touching it changes NOTHING about
            // what gets queued.
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            AssertThat(brew.NotesFamiliar).IsTrue();  // persists across Configure — same session
            AssertThat(brew.NotesVisible).IsFalse();  // faded — no hover yet

            brew.SetRecipeBookHovered(true);
            AssertThat(brew.NotesVisible).IsTrue();
            brew.SetRecipeBookHovered(false);
            AssertThat(brew.NotesVisible).IsFalse();

            foreach (var reagent in ideal)
            {
                brew.PourReagent(reagent);
            }

            brew.Submit();
            var second = brew.EmittedAction!;

            AssertThat(((AlchemyReagentPuzzle)second.Puzzle!).Reagents
                .SequenceEqual(((AlchemyReagentPuzzle)first.Puzzle!).Reagents)).IsTrue();
            AssertThat(second.SubScores!).ContainsExactly(first.SubScores!);
            AssertThat(second.PerformanceGrade is null).IsTrue();
        }
        finally
        {
            brew.Free();
        }
    }

    [TestCase]
    public void HoveringTheRecipeBook_ShowsNotes_LeavingHidesThemAgain()
    {
        var brew = new GodotClient.Minigames.AlchemyBrewPuzzle();
        try
        {
            brew.Configure(ElixirRecipe, "copper", Alchemy, ImmutableSortedSet<string>.Empty);
            var ideal = AlchemyPuzzleScorer.IdealSequenceFor(ElixirRecipe);
            foreach (var reagent in ideal)
            {
                brew.PourReagent(reagent);
            }

            brew.Submit(); // this recipe is now familiar

            brew.Configure(ElixirRecipe, "iron", Alchemy, ImmutableSortedSet<string>.Empty);
            AssertThat(brew.NotesVisible).IsFalse();

            // Real hover via the GuiInput-signal seam (no click, motion only).
            var canvas = UiTestSupport.Find<Control>(brew, "BrewCanvas");
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = new Vector2(20f, 320f) }); // hovering the book
            AssertThat(brew.NotesVisible).IsTrue();

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = new Vector2(300f, 200f) }); // moved away
            AssertThat(brew.NotesVisible).IsFalse();
        }
        finally
        {
            brew.Free();
        }
    }
}
#endif
