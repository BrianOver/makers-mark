#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U2 (plan <c>2026-07-28-004</c>): the tanning frame overlay — the shared-patch-layout contract
/// (this overlay renders EXACTLY the <c>TanningScrapeScorer.CellKind</c> grid the scorer
/// regenerates sim-side from the SAME <c>PatchSeed</c>), the single-action contract (PKD8), the
/// two distinct gestures (a quantized scrape-drag inside the grid vs. a discrete drag-the-hide-
/// off-the-frame commit), keyboard parity for both, and same-script determinism.
///
/// <para>Most scenarios call the discrete seam methods directly on an unmounted node — no frame
/// pump, no SubViewport, per the 3D-headless-hang rule (this overlay has no clock at all, unlike
/// the forge). The drag scenarios drive <c>HideCanvas</c> through REAL synthesized input: the
/// canvas wires its recognizer via the <c>GuiInput</c> C# event rather than overriding
/// <c>_GuiInput</c> — the same idiom <c>AlchemyBrewPuzzle.BrewCanvas</c>/<c>DrawerHost</c>'s dim
/// veil use — so a test can fire it with <c>EmitSignal(Control.SignalName.GuiInput, ...)</c>
/// exactly like <c>UiTestSupport.Click</c> does elsewhere, with no mouse and no SubViewport
/// needed. The canvas is found by name via <see cref="Find{T}"/> on the (still unmounted) node
/// tree the frame already builds in <c>EnsureBuilt</c>. Keyboard scenarios call
/// <c>TanningFrame._GuiInput</c> directly with constructed <c>InputEventKey</c>s — the SAME idiom
/// <c>ForgeMinigameTests</c> already uses for its own keyboard/aimed-input coverage.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TanningFrameTests
{
    private const int TestDay = 0;
    private static readonly ProfessionDefinition Tanning = TanningProfession.Definition;
    private static readonly Recipe HideRecipe = ProfessionRegistry.AllRecipes["tanning-leather-cap"];

    [TestCase]
    public void Configure_RendersTheExactSamePatches_TheScorerRegeneratesForTheSameSeed()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);

            var regenerated = TanningScrapeScorer.PatchesFor(frame.PatchSeed);
            AssertThat(frame.Patches.SequenceEqual(regenerated)).IsTrue();
            AssertThat(frame.Patches.Length).IsEqual(GodotClient.Minigames.TanningFrame.CellCount);
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void DifferentDay_RegeneratesADifferentPatchSeed_ButStaysAgreeableWithTheScorer()
    {
        var day0 = new GodotClient.Minigames.TanningFrame();
        var day1 = new GodotClient.Minigames.TanningFrame();
        try
        {
            day0.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, 0);
            day1.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, 1);

            AssertThat(day1.PatchSeed).IsNotEqual(day0.PatchSeed);
            AssertThat(day0.Patches.SequenceEqual(TanningScrapeScorer.PatchesFor(day0.PatchSeed))).IsTrue();
            AssertThat(day1.Patches.SequenceEqual(TanningScrapeScorer.PatchesFor(day1.PatchSeed))).IsTrue();
        }
        finally
        {
            day0.Free();
            day1.Free();
        }
    }

    [TestCase]
    public void ScrapeCell_IncrementsExactlyThatCell_OutOfRangeIndicesAreIgnored()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);

            frame.ScrapeCell(-1);   // ignored
            frame.ScrapeCell(9999); // ignored
            AssertThat(frame.CellPasses.All(c => c == 0)).IsTrue();

            frame.ScrapeCell(5);
            frame.ScrapeCell(5);
            frame.ScrapeCell(12);

            AssertThat(frame.CellPasses[5]).IsEqual(2);
            AssertThat(frame.CellPasses[12]).IsEqual(1);
            AssertThat(frame.CellPasses.Where((_, i) => i != 5 && i != 12).All(c => c == 0)).IsTrue();
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void Submit_EmitsExactlyOneAction_WithThePuzzlePayload_AndNullGrade()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            var finishedCount = 0;
            CraftAction? emitted = null;
            frame.Finished += a => { finishedCount++; emitted = a; };

            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            frame.ScrapeCell(0);
            frame.ScrapeCell(1);
            frame.ScrapeCell(1);

            frame.Submit();
            frame.Submit(); // double-submit must not double-fire (single-action contract)

            AssertThat(frame.Completed).IsTrue();
            AssertThat(finishedCount).IsEqual(1);
            AssertThat(emitted!.RecipeId).IsEqual(HideRecipe.RecipeId);
            AssertThat(emitted.MaterialKey).IsEqual("copper");
            AssertThat(emitted.PerformanceGrade is null).IsTrue(); // the cell-pass list is the source; sim scores it

            var puzzle = emitted.Puzzle as TanningScrapeInput;
            AssertThat(puzzle is not null).IsTrue();
            AssertThat(puzzle!.PatchSeed).IsEqual(frame.PatchSeed);
            AssertThat(puzzle.CellPasses.SequenceEqual(frame.CellPasses)).IsTrue();

            var preview = TanningScrapeScorer.Score(HideRecipe, puzzle, ImmutableSortedSet<string>.Empty, Tanning);
            AssertThat(emitted.SubScores!).ContainsExactly(preview.CoveragePermille, preview.RuinPermille, preview.GradePermille);
            AssertThat(frame.PreviewGradePermille!.Value).IsEqual(preview.GradePermille);
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void Cancel_QueuesNothing_AndFurtherInputIsIgnored()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            var finishedCount = 0;
            var cancelledCount = 0;
            frame.Finished += _ => finishedCount++;
            frame.Cancelled += () => cancelledCount++;

            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            frame.ScrapeCell(3);
            frame.Cancel();
            frame.Cancel(); // double-cancel must not double-fire
            frame.ScrapeCell(4); // dead input after cancel
            frame.Submit();      // dead input after cancel

            AssertThat(frame.WasCancelled).IsTrue();
            AssertThat(frame.Completed).IsFalse();
            AssertThat(cancelledCount).IsEqual(1);
            AssertThat(finishedCount).IsEqual(0);
            AssertThat(frame.EmittedAction is null).IsTrue();
            AssertThat(frame.CellPasses[4]).IsEqual(0); // the post-cancel scrape never landed
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void SameScriptTwice_ProducesIdenticalPayload_NoHiddenRandomness()
    {
        var first = RunScript();
        var second = RunScript();

        AssertThat(((TanningScrapeInput)second.Puzzle!).CellPasses
            .SequenceEqual(((TanningScrapeInput)first.Puzzle!).CellPasses)).IsTrue();
        AssertThat(((TanningScrapeInput)second.Puzzle!).PatchSeed).IsEqual(((TanningScrapeInput)first.Puzzle!).PatchSeed);
        AssertThat(second.SubScores!).ContainsExactly(first.SubScores!);

        static CraftAction RunScript()
        {
            var frame = new GodotClient.Minigames.TanningFrame();
            try
            {
                frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
                frame.ScrapeCell(0);
                frame.ScrapeCell(7);
                frame.ScrapeCell(7);
                frame.ScrapeCell(7);
                frame.ScrapeCell(39); // deliberately over-scrape once for a non-trivial ruin term
                frame.ScrapeCell(39);
                frame.Submit();
                return frame.EmittedAction!;
            }
            finally
            {
                frame.Free();
            }
        }
    }

    [TestCase]
    public void Reconfigure_ResetsToACleanRun()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            frame.ScrapeCell(0);
            frame.Submit();
            AssertThat(frame.Completed).IsTrue();

            frame.Configure(HideRecipe, "iron", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            AssertThat(frame.Completed).IsFalse();
            AssertThat(frame.CellPasses.All(c => c == 0)).IsTrue();
            AssertThat(frame.MaterialKey).IsEqual("iron");
            AssertThat(frame.EmittedAction is null).IsTrue();
            AssertThat(frame.CursorIndex).IsEqual(0);
        }
        finally
        {
            frame.Free();
        }
    }

    // ── Cursor + hit-test geometry ──────────────────────────────────────────────────────────────

    [TestCase]
    public void MoveCursor_ClampsAtGridEdges_NoWraparound()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            AssertThat(frame.CursorIndex).IsEqual(0);

            frame.MoveCursor(-1, 0); // already at the left edge
            AssertThat(frame.CursorIndex).IsEqual(0);
            frame.MoveCursor(0, -1); // already at the top edge
            AssertThat(frame.CursorIndex).IsEqual(0);

            frame.MoveCursor(1, 0);
            AssertThat(frame.CursorIndex).IsEqual(1); // (col 1, row 0)
            frame.MoveCursor(0, 1);
            AssertThat(frame.CursorIndex).IsEqual(1 + GodotClient.Minigames.TanningFrame.Columns); // (col 1, row 1)

            // Drive to the bottom-right corner and confirm it clamps rather than wraps.
            for (var i = 0; i < 20; i++)
            {
                frame.MoveCursor(1, 1);
            }

            AssertThat(frame.CursorIndex).IsEqual(GodotClient.Minigames.TanningFrame.CellCount - 1);
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void CellAt_TrueOverEachCell_NullOutsideTheGrid()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);

            for (var i = 0; i < GodotClient.Minigames.TanningFrame.CellCount; i++)
            {
                AssertThat(frame.CellAt(frame.CellCenterFor(i))!.Value).IsEqual(i);
            }

            AssertThat(frame.CellAt(Vector2.Zero)).IsNull();               // frame margin, above the grid
            AssertThat(frame.CellAt(new Vector2(300, 320))).IsNull();      // drop-zone tray, below the grid
            AssertThat(frame.IsOverReleaseClip(frame.ReleaseClipAnchor)).IsTrue();
            AssertThat(frame.IsOverReleaseClip(frame.CellCenterFor(0))).IsFalse();
            AssertThat(frame.IsOverDropZone(frame.DropZoneAnchor)).IsTrue();
            AssertThat(frame.IsOverDropZone(frame.CellCenterFor(0))).IsFalse();
        }
        finally
        {
            frame.Free();
        }
    }

    // ── U2: scrape-drag quantization (KTD-B) ────────────────────────────────────────────────────

    [TestCase]
    public void PressThenDrag_WithinOneCell_QuantizesIntoTheExpectedPassCount()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            var canvas = Find<Control>(frame, "HideCanvas");
            var start = frame.CellCenterFor(0);
            var threshold = GodotClient.Minigames.TanningFrame.ScrapePixelThreshold;

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start });
            AssertThat(frame.CellPasses[0]).IsEqual(1); // a plain press scrapes immediately, no drag needed

            // Two whole thresholds' worth of travel, staying inside cell 0 (its half-width is well
            // over 2*threshold), split across two motion events — the accumulator must persist
            // across events, not reset each call (same idiom ForgeMinigame.PumpStroke already pins).
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = start + new Vector2(threshold, 0) });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = start + new Vector2(threshold * 2, 0) });
            AssertThat(frame.CellPasses[0]).IsEqual(3); // 1 press + 2 quantized drag scrapes

            // A leftover fractional remainder (< one threshold) must NOT fire an extra scrape yet.
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = start + new Vector2(threshold * 2 + threshold - 1, 0) });
            AssertThat(frame.CellPasses[0]).IsEqual(3);

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = start });

            // Every OTHER cell stayed untouched.
            AssertThat(frame.CellPasses.Where((_, i) => i != 0).All(c => c == 0)).IsTrue();
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void Scrub_BackAndForth_AccumulatesTotalDistanceTravelled_NotNetDisplacement()
    {
        // Deliberately different from ForgeMinigame's directional-only bellows accumulator: a real
        // scrape is a back-and-forth scrub over the SAME spot, so distance in either direction must
        // count, even though net displacement ends at zero.
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            var canvas = Find<Control>(frame, "HideCanvas");
            var start = frame.CellCenterFor(0);
            var threshold = GodotClient.Minigames.TanningFrame.ScrapePixelThreshold;

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start });
            AssertThat(frame.CellPasses[0]).IsEqual(1);

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = start + new Vector2(threshold, 0) }); // forward: +14
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = start }); // back to start: another +14 travelled
            AssertThat(frame.CellPasses[0]).IsEqual(3); // 1 press + 2 scrub-distance scrapes, net position unchanged
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void Drag_AcrossACellBoundary_AttributesScrapesToTheCellUnderThePointer()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            var canvas = Find<Control>(frame, "HideCanvas");
            var cell0 = frame.CellCenterFor(0);
            var cell1 = frame.CellCenterFor(1);

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = cell0 });
            AssertThat(frame.CellPasses[0]).IsEqual(1);

            // One motion event carries the pointer straight into the neighbouring cell — every
            // threshold quantized out of that event's travel lands on cell 1 (the cell under the
            // pointer AT that emission point), never retroactively credited to cell 0.
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Position = cell1 });

            AssertThat(frame.CellPasses[0]).IsEqual(1); // untouched by the cross-boundary travel
            AssertThat(frame.CellPasses[1]).IsGreater(0);
        }
        finally
        {
            frame.Free();
        }
    }

    // ── U2: drag-the-hide-off-the-frame commit gesture ──────────────────────────────────────────

    [TestCase]
    public void DragReleaseClip_IntoTheDropZone_CommitsTheHide_ThroughTheOwnerSubmitSeam()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            var finishedCount = 0;
            frame.Finished += _ => finishedCount++;

            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            frame.ScrapeCell(0);
            frame.ScrapeCell(1);

            var canvas = Find<Control>(frame, "HideCanvas");
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = frame.ReleaseClipAnchor });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = frame.DropZoneAnchor });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = frame.DropZoneAnchor });

            AssertThat(frame.Completed).IsTrue();
            AssertThat(finishedCount).IsEqual(1);
            var puzzle = frame.EmittedAction!.Puzzle as TanningScrapeInput;
            AssertThat(puzzle!.CellPasses[0]).IsEqual(1);
            AssertThat(puzzle.CellPasses[1]).IsEqual(1);
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void DragReleaseClip_ReleasedOutsideTheDropZone_ShelvesHarmlessly_NoSubmit()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);

            var canvas = Find<Control>(frame, "HideCanvas");
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = frame.ReleaseClipAnchor });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = Vector2.Zero }); // off the tray

            AssertThat(frame.Completed).IsFalse();
            AssertThat(frame.WasCancelled).IsFalse(); // a miss is not an error state either
            AssertThat(frame.EmittedAction is null).IsTrue();
        }
        finally
        {
            frame.Free();
        }
    }

    [TestCase]
    public void Reconfigure_MidClipDrag_ClearsTheCarriedHide_SoTheStaleReleaseDoesNothing()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            var canvas = Find<Control>(frame, "HideCanvas");
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = frame.ReleaseClipAnchor });

            frame.Configure(HideRecipe, "iron", Tanning, ImmutableSortedSet<string>.Empty, TestDay); // reopens mid-drag

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = frame.DropZoneAnchor });

            AssertThat(frame.Completed).IsFalse(); // the stale pick-up was cleared, not re-armed
        }
        finally
        {
            frame.Free();
        }
    }

    // ── U2: keyboard parity (KTD-C) ──────────────────────────────────────────────────────────────

    [TestCase]
    public void ArrowKeysAndSpace_MoveTheCursorAndScrapeIt_ReachingTheSameSeamsAsDirectCalls()
    {
        var viaKeyboard = new GodotClient.Minigames.TanningFrame();
        var viaDirectCalls = new GodotClient.Minigames.TanningFrame();
        try
        {
            viaKeyboard.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            viaDirectCalls.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);

            // Right, Right, Down, Space — via real keyboard events.
            viaKeyboard._GuiInput(new InputEventKey { Keycode = Key.Right, Pressed = true, Echo = false });
            viaKeyboard._GuiInput(new InputEventKey { Keycode = Key.Right, Pressed = true, Echo = false });
            viaKeyboard._GuiInput(new InputEventKey { Keycode = Key.Down, Pressed = true, Echo = false });
            viaKeyboard._GuiInput(new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = false });
            viaKeyboard._GuiInput(new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = false });

            // The SAME script via the direct seams the button row/a scripted test would call.
            viaDirectCalls.MoveCursor(1, 0);
            viaDirectCalls.MoveCursor(1, 0);
            viaDirectCalls.MoveCursor(0, 1);
            viaDirectCalls.ScrapeFocusedCell();
            viaDirectCalls.ScrapeFocusedCell();

            AssertThat(viaKeyboard.CursorIndex).IsEqual(viaDirectCalls.CursorIndex);
            AssertThat(viaKeyboard.CellPasses.SequenceEqual(viaDirectCalls.CellPasses)).IsTrue();
            AssertThat(viaKeyboard.CellPasses[viaKeyboard.CursorIndex]).IsEqual(2);
        }
        finally
        {
            viaKeyboard.Free();
            viaDirectCalls.Free();
        }
    }

    [TestCase]
    public void EnterKey_SubmitsTheSameWayTheDragOffTheFrameGestureDoes()
    {
        var viaKeyboard = new GodotClient.Minigames.TanningFrame();
        var viaDrag = new GodotClient.Minigames.TanningFrame();
        try
        {
            viaKeyboard.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);
            viaDrag.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);

            viaKeyboard.ScrapeCell(2);
            viaDrag.ScrapeCell(2);

            viaKeyboard._GuiInput(new InputEventKey { Keycode = Key.Enter, Pressed = true, Echo = false });

            var canvas = Find<Control>(viaDrag, "HideCanvas");
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = viaDrag.ReleaseClipAnchor });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = viaDrag.DropZoneAnchor });

            AssertThat(viaKeyboard.Completed).IsTrue();
            AssertThat(viaDrag.Completed).IsTrue();
            var kbPuzzle = (TanningScrapeInput)viaKeyboard.EmittedAction!.Puzzle!;
            var dragPuzzle = (TanningScrapeInput)viaDrag.EmittedAction!.Puzzle!;
            AssertThat(kbPuzzle.CellPasses.SequenceEqual(dragPuzzle.CellPasses)).IsTrue();
        }
        finally
        {
            viaKeyboard.Free();
            viaDrag.Free();
        }
    }

    [TestCase]
    public void ArrowKeys_ClampAtGridEdges_ThroughTheRealGuiInputSeam()
    {
        var frame = new GodotClient.Minigames.TanningFrame();
        try
        {
            frame.Configure(HideRecipe, "copper", Tanning, ImmutableSortedSet<string>.Empty, TestDay);

            frame._GuiInput(new InputEventKey { Keycode = Key.Left, Pressed = true, Echo = false });
            frame._GuiInput(new InputEventKey { Keycode = Key.Up, Pressed = true, Echo = false });
            AssertThat(frame.CursorIndex).IsEqual(0); // already at the top-left corner
        }
        finally
        {
            frame.Free();
        }
    }
}
#endif
