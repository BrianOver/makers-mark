#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Crafting;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Minigames;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U3 (plan 2026-07-28-004): the engineer's assembly bench — the anti-forge with NO clock anywhere.
/// Covers the rendered-schematic-matches-scorer contract, the single-action contract (PKD8), the
/// reseat-is-free bookkeeping (the scorer only honours a socket's FIRST flattened entry, so the
/// overlay must fold a correction back to ONE entry rather than literally append every seat event —
/// see <see cref="EngineeringBench"/>'s own class doc), real synthesized mouse drag-drop AND crank-drag
/// gestures via the <c>GuiInput</c> C# event (same headless-testable idiom as
/// <c>AlchemyBrewPuzzle.BrewCanvas</c>), and full keyboard parity to the identical seams. Every
/// scenario either drives the bench unmounted via its public seam methods, or through the real
/// <c>ForgePanel</c> to prove the overlay is LIVE and reachable — <c>EngineeringProfession
/// .Definition.ActiveCraft</c> flipped true in U3b, so selecting engineering now routes through
/// the real Assemble button, not the plain auto-craft fallback. PROPERTY-ONLY: a plain 2D
/// <c>Control</c> canvas, never a 3D <c>SubViewport</c> — the known gdUnit headless-hang trap never
/// applies here.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class EngineeringBenchTests
{
    private static readonly Recipe Tier1Recipe = ProfessionRegistry.AllRecipes["engineering-bolt-thrower"];
    private static readonly Recipe Tier2Recipe = ProfessionRegistry.AllRecipes["engineering-clockwork-glaive"];
    private static readonly Recipe Tier3Recipe = ProfessionRegistry.AllRecipes["engineering-exo-frame"];
    private static readonly ProfessionDefinition Engineering = EngineeringProfession.Definition;

    [TestCase]
    public void RenderedSchematic_MatchesTheScorer_ForEveryTierSocketCount()
    {
        foreach (var recipe in new[] { Tier1Recipe, Tier2Recipe, Tier3Recipe })
        {
            var bench = new EngineeringBench();
            try
            {
                bench.Configure(recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);

                AssertThat(bench.SocketCount).IsEqual(EngineeringAssemblyScorer.SocketCountFor(recipe));
                AssertThat(bench.Schematic.SequenceEqual(EngineeringAssemblyScorer.SchematicFor(recipe))).IsTrue();
            }
            finally
            {
                bench.Free();
            }
        }

        // Tiers 1/2/3+ land on genuinely different socket counts (3/4/5) — the coverage this
        // suite claims to have, not just three configurations that all happen to be tier 1.
        AssertThat(EngineeringAssemblyScorer.SocketCountFor(Tier1Recipe)).IsEqual(3);
        AssertThat(EngineeringAssemblyScorer.SocketCountFor(Tier2Recipe)).IsEqual(4);
        AssertThat(EngineeringAssemblyScorer.SocketCountFor(Tier3Recipe)).IsEqual(5);
    }

    [TestCase]
    public void PerfectAssembly_EmitsExactlyOneAction_WithNullGrade_AndTopScores()
    {
        var bench = new EngineeringBench();
        try
        {
            var finishedCount = 0;
            CraftAction? emitted = null;
            bench.Finished += a => { finishedCount++; emitted = a; };

            bench.Configure(Tier2Recipe, "iron", Engineering, ImmutableSortedSet<string>.Empty);
            var schematic = bench.Schematic;
            for (var socket = 0; socket < schematic.Count; socket++)
            {
                bench.Place(socket, schematic[socket]);
            }

            for (var i = 0; i < EngineeringBench.CrankStrokesRequired; i++)
            {
                bench.CrankStroke();
            }

            bench.CrankStroke(); // a stroke past completion must not double-fire

            AssertThat(bench.Completed).IsTrue();
            AssertThat(finishedCount).IsEqual(1);
            AssertThat(emitted!.RecipeId).IsEqual("engineering-clockwork-glaive");
            AssertThat(emitted.MaterialKey).IsEqual("iron");
            AssertThat(emitted.PerformanceGrade is null).IsTrue(); // the puzzle is the source; sim scores it

            var puzzle = emitted.Puzzle as EngineeringAssemblyInput;
            AssertThat(puzzle is not null).IsTrue();
            var expectedFlat = ImmutableList.CreateBuilder<int>();
            for (var socket = 0; socket < schematic.Count; socket++)
            {
                expectedFlat.Add(socket);
                expectedFlat.Add(schematic[socket]);
            }

            AssertThat(puzzle!.Placements.SequenceEqual(expectedFlat.ToImmutable())).IsTrue();
            AssertThat(emitted.SubScores!).ContainsExactly(1000, 1000, 1000);
        }
        finally
        {
            bench.Free();
        }
    }

    [TestCase]
    public void ReseatingWrongToRight_ScoresIdenticallyToSeatingRightTheFirstTime()
    {
        var direct = RunSeatingScript(Tier1Recipe, bench =>
        {
            var s = bench.Schematic;
            bench.Place(0, s[0]);
            bench.Place(1, s[1]);
            bench.Place(2, s[2]);
        });

        var reseated = RunSeatingScript(Tier1Recipe, bench =>
        {
            var s = bench.Schematic;
            var wrong = (s[0] + 1) % EngineeringAssemblyScorer.PartCount;
            bench.Place(0, wrong);          // wrong first attempt
            bench.Place(1, s[1]);
            bench.Place(2, s[2]);
            bench.RemoveFromSocket(0);      // pull it back out — free
            bench.Place(0, s[0]);           // reseat correctly before winding the crank
        });

        var directPuzzle = (EngineeringAssemblyInput)direct.Puzzle!;
        var reseatedPuzzle = (EngineeringAssemblyInput)reseated.Puzzle!;
        AssertThat(reseatedPuzzle.Placements.SequenceEqual(directPuzzle.Placements)).IsTrue();
        AssertThat(reseated.SubScores!).ContainsExactly(direct.SubScores!);
        AssertThat(reseated.PerformanceGrade is null).IsTrue();
    }

    [TestCase]
    public void PulledOutAndNeverRefilled_ContributesNoPairAtSubmit()
    {
        var result = RunSeatingScript(Tier1Recipe, bench =>
        {
            var s = bench.Schematic;
            bench.Place(0, s[0]);
            bench.Place(1, (s[1] + 1) % EngineeringAssemblyScorer.PartCount);
            bench.RemoveFromSocket(1); // left empty on purpose
            bench.Place(2, s[2]);
        });

        var puzzle = (EngineeringAssemblyInput)result.Puzzle!;
        // Only sockets 0 and 2 ever emit — socket 1 was touched then emptied, so it contributes
        // nothing (flattened list has 2 pairs = 4 ints, never a stray/placeholder entry for socket 1).
        AssertThat(puzzle.Placements.Count).IsEqual(4);
        AssertThat(puzzle.Placements.Contains(1)).IsFalse();
    }

    [TestCase]
    public void Cancel_MidRun_QueuesNoActionAndRaisesCancelledExactlyOnce()
    {
        var bench = new EngineeringBench();
        try
        {
            bench.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            var cancelledCount = 0;
            bench.Cancelled += () => cancelledCount++;

            bench.Place(0, bench.Schematic[0]);
            bench.Cancel();
            bench.Cancel(); // double-cancel must not double-fire
            bench.Place(1, bench.Schematic[1]); // dead input after cancel
            bench.CrankStroke();

            AssertThat(bench.WasCancelled).IsTrue();
            AssertThat(bench.Completed).IsFalse();
            AssertThat(cancelledCount).IsEqual(1);
            AssertThat(bench.EmittedAction is null).IsTrue();
            AssertThat(bench.Seated.ContainsKey(1)).IsFalse(); // the post-cancel Place never landed
        }
        finally
        {
            bench.Free();
        }
    }

    // ── real synthesized mouse: drag-drop + crank-drag via the GuiInput C# event ────────────────

    [TestCase]
    public void DragFromTray_OntoASocket_SeatsTheSamePartAsCallingPlaceDirectly()
    {
        var direct = new EngineeringBench();
        var dragged = new EngineeringBench();
        try
        {
            direct.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            dragged.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);

            const int partId = 0;
            direct.Place(1, partId);

            var canvas = Find<Control>(dragged, "BenchCanvas");
            var trayPos = dragged.TrayAnchor(partId);
            var socketPos = dragged.SocketAnchor(1);
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = trayPos });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = socketPos });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = socketPos });

            AssertThat(dragged.Seated[1]).IsEqual(direct.Seated[1]);
            AssertThat(dragged.Seated.Count).IsEqual(1);
        }
        finally
        {
            direct.Free();
            dragged.Free();
        }
    }

    [TestCase]
    public void DragFromTray_DroppedOffAnySocket_ShelvesHarmlessly_NoStateChange()
    {
        var bench = new EngineeringBench();
        try
        {
            bench.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            var canvas = Find<Control>(bench, "BenchCanvas");
            var trayPos = bench.TrayAnchor(0);

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = trayPos });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = Vector2.Zero }); // off every socket

            AssertThat(bench.Seated.Count).IsEqual(0);
            AssertThat(bench.Completed).IsFalse();
            AssertThat(bench.WasCancelled).IsFalse(); // a miss is not an error state either
        }
        finally
        {
            bench.Free();
        }
    }

    [TestCase]
    public void DragAnExistingSocketPart_ToADifferentSocket_RelocatesIt_NeverDuplicatesIt()
    {
        var bench = new EngineeringBench();
        try
        {
            bench.Configure(Tier2Recipe, "iron", Engineering, ImmutableSortedSet<string>.Empty);
            var partId = bench.Schematic[0];
            bench.Place(0, partId);

            var canvas = Find<Control>(bench, "BenchCanvas");
            var from = bench.SocketAnchor(0);
            var to = bench.SocketAnchor(2);
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseMotion { Position = to });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });

            AssertThat(bench.Seated.ContainsKey(0)).IsFalse(); // moved away, not copied
            AssertThat(bench.Seated[2]).IsEqual(partId);
            AssertThat(bench.Seated.Count).IsEqual(1);
        }
        finally
        {
            bench.Free();
        }
    }

    [TestCase]
    public void DroppingAPartBackOnItsOwnSocket_IsANoOp()
    {
        var bench = new EngineeringBench();
        try
        {
            bench.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            var partId = bench.Schematic[0];
            bench.Place(0, partId);

            var canvas = Find<Control>(bench, "BenchCanvas");
            var pos = bench.SocketAnchor(0);
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pos });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pos });

            AssertThat(bench.Seated[0]).IsEqual(partId);
            AssertThat(bench.Seated.Count).IsEqual(1);
        }
        finally
        {
            bench.Free();
        }
    }

    [TestCase]
    public void RightDragOnTheCrank_QuantizesIntoCrankStrokes_ByFixedPixelThreshold()
    {
        var bench = new EngineeringBench();
        try
        {
            bench.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            var canvas = Find<Control>(bench, "BenchCanvas");
            var hub = bench.CrankAnchor;

            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = hub });
            // Exactly 3 strokes' worth of drag distance (40px each), split across two motion events —
            // the accumulator must persist across events, not reset each call.
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Relative = new Vector2(80, 0) });
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Relative = new Vector2(40, 0) });

            AssertThat(bench.CrankProgressPermille).IsEqual(3 * (1000 / EngineeringBench.CrankStrokesRequired));

            // A leftover fractional remainder must not fire a fourth stroke yet.
            var afterThree = bench.CrankProgressPermille;
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Relative = new Vector2(39, 0) });
            AssertThat(bench.CrankProgressPermille).IsEqual(afterThree);

            // Releasing resets the accumulator — the leftover 39px is discarded, not carried forward.
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
            canvas.EmitSignal(Control.SignalName.GuiInput,
                new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = hub });
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Relative = new Vector2(39, 0) });
            AssertThat(bench.CrankProgressPermille).IsEqual(afterThree);
        }
        finally
        {
            bench.Free();
        }
    }

    // ── keyboard parity ────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void KeyboardCycleAndSeatAndPull_ReachTheSameSeamsAsDirectCalls()
    {
        var bench = new EngineeringBench();
        try
        {
            bench.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            var canvas = Find<Control>(bench, "BenchCanvas");

            // Cycle the socket cursor right twice: 0 -> 1 -> 2 (same as CycleSelectedSocket(1) x2).
            Key(canvas, Godot.Key.Right);
            Key(canvas, Godot.Key.Right);
            AssertThat(bench.SelectedSocketId).IsEqual(2);

            // Cycle the part cursor up (wraps to the last part): 0 -> 5.
            Key(canvas, Godot.Key.Up);
            AssertThat(bench.SelectedPartId).IsEqual(EngineeringAssemblyScorer.PartCount - 1);

            // Enter seats the cursor-selected part into the cursor-selected socket.
            Key(canvas, Godot.Key.Enter);
            AssertThat(bench.Seated[2]).IsEqual(EngineeringAssemblyScorer.PartCount - 1);

            // Backspace pulls it back out — the same free removal RemoveFromSocket gives directly.
            Key(canvas, Godot.Key.Backspace);
            AssertThat(bench.Seated.ContainsKey(2)).IsFalse();
        }
        finally
        {
            bench.Free();
        }
    }

    // ── C2 (input substrate plan): crank_stroke/confirm/pull_part are InputMap actions ─────────

    [TestCase]
    public void CrankStrokeAction_FollowsARebind_TheOldPhysicalKeyStopsWorking()
    {
        WithTemporaryBinding("crank_stroke", new InputEventKey { PhysicalKeycode = Godot.Key.F }, () =>
        {
            var bench = new EngineeringBench();
            try
            {
                bench.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
                var canvas = Find<Control>(bench, "BenchCanvas");

                // The key this canvas used to hard-match (Space) must now be a no-op...
                Key(canvas, Godot.Key.Space);
                AssertThat(bench.CrankProgressPermille).IsEqual(0);

                // ...and the NEWLY bound physical key fires the exact same CrankStroke behaviour.
                Key(canvas, Godot.Key.F);
                AssertThat(bench.CrankProgressPermille).IsEqual(1000 / EngineeringBench.CrankStrokesRequired);
            }
            finally
            {
                bench.Free();
            }
        });
    }

    [TestCase]
    public void SeatButtonLabel_ReadsTheLiveInputMapBinding_NotAFrozenLiteral()
    {
        WithTemporaryBinding("confirm", new InputEventKey { PhysicalKeycode = Godot.Key.F }, () =>
        {
            var bench = new EngineeringBench();
            try
            {
                bench.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);

                // A prompt that hardcodes "(Enter)" would lie the instant a rebind screen moves this
                // key — this is the exact defect C2 exists to close before any rebind UI ships.
                var seatButton = Find<Button>(bench, "EngineeringBenchSeat");
                AssertThat(seatButton.Text).IsEqual("Seat (F)");
            }
            finally
            {
                bench.Free();
            }
        });
    }

    [TestCase]
    public void EntirelyKeyboardDrivenAssembly_ProducesTheIdenticalPayload_AsDirectPlaceCalls()
    {
        var direct = RunSeatingScript(Tier1Recipe, bench =>
        {
            var s = bench.Schematic;
            bench.Place(0, s[0]);
            bench.Place(1, s[1]);
            bench.Place(2, s[2]);
        });

        var bench2 = new EngineeringBench();
        try
        {
            bench2.Configure(Tier1Recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            var canvas = Find<Control>(bench2, "BenchCanvas");
            var s = bench2.Schematic;

            for (var socket = 0; socket < s.Count; socket++)
            {
                // Cursor starts at socket 0/part 0 and is already there for socket 0 on the first
                // pass; for later sockets, cycle right the remaining distance from wherever it sits.
                while (bench2.SelectedSocketId != socket)
                {
                    Key(canvas, Godot.Key.Right);
                }

                while (bench2.SelectedPartId != s[socket])
                {
                    Key(canvas, Godot.Key.Down);
                }

                Key(canvas, Godot.Key.Enter); // SeatSelected -> Place(socket, s[socket])
            }

            for (var i = 0; i < EngineeringBench.CrankStrokesRequired; i++)
            {
                Key(canvas, Godot.Key.Space); // CrankStroke — the last one finishes the run
            }

            AssertThat(bench2.Completed).IsTrue();
            var keyboardPuzzle = (EngineeringAssemblyInput)bench2.EmittedAction!.Puzzle!;
            var directPuzzle = (EngineeringAssemblyInput)direct.Puzzle!;
            AssertThat(keyboardPuzzle.Placements.SequenceEqual(directPuzzle.Placements)).IsTrue();
            AssertThat(bench2.EmittedAction.SubScores!).ContainsExactly(direct.SubScores!);
        }
        finally
        {
            bench2.Free();
        }
    }

    // ── live: the flip (U3b) opened the route to this overlay ─────────────────────────────────

    /// <summary>
    /// <para>Inverted by U3b. This case previously asserted <c>ActiveCraft</c> was FALSE and that
    /// <c>ForgePanel</c> rendered no Assemble button — the correct pin while the overlay shipped
    /// deliberately dormant (#272), before there was any way for a player to earn the grade.</para>
    ///
    /// <para>U3b is precisely the change that flips it, so the assertion has to flip with it. Kept
    /// as the same scenario rather than deleted, because the wiring it exercises — a profession
    /// selection reaching the panel and producing the active-craft entry point instead of the plain
    /// Craft button — is exactly what the flip is claiming to have done, and a deleted test claims
    /// nothing.</para>
    /// </summary>
    [TestCase]
    public void EngineeringActiveCraftIsTrue_SoForgePanelRoutesThroughTheAssembleButton()
    {
        AssertThat(Engineering.ActiveCraft).IsTrue(); // flipped by U3b, with the talent remap

        var state = GameComposition.NewCampaign(2026) with
        {
            Player = GameComposition.NewCampaign(2026).Player with
            {
                SelectedProfessions = ImmutableSortedSet.Create(EngineeringProfession.Id),
            },
        };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.OpenPanel("Forge");

            var assemble = ui.Forge.FindChild("Assemble_engineering-bolt-thrower", recursive: true, owned: false);
            AssertThat(assemble is not null)
                .OverrideFailureMessage(
                    "ActiveCraft is true but ForgePanel rendered no Assemble button — the overlay is " +
                    "unreachable, which means every engineering craft silently falls back to auto-craft.")
                .IsTrue();

            // The auto-craft path is still offered — deliberately, per ForgePanel's PA6/PKD4 note:
            // an active profession keeps its instant Craft as an explicit, honestly-labelled
            // fallback beside the minigame rather than being forced through the overlay. What the
            // flip changes is the LABEL, and that relabel is the player-visible consequence worth
            // pinning: "Craft" would read as the only way to make the thing.
            var craft = ui.Forge.FindChild("Craft_engineering-bolt-thrower", recursive: true, owned: false) as Button;
            AssertThat(craft is not null).IsTrue();
            // P2-SCREEN-09: a refused verb now carries its blocker right in the label too (this
            // campaign has zero materials), so the button's Text is the verb PLUS that reason —
            // Contains, not IsEqual, is the honest check for the verb half of that string.
            AssertThat(craft!.Text)
                .OverrideFailureMessage(
                    "An active profession's instant Craft must read as the fallback, not the default.")
                .Contains("Auto-craft (competent)");
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    // PhysicalKeycode (not Keycode) — C2 standardised BenchCanvas.HandleKey on InputMap actions
    // registered by physical key, so a synthetic test event must set the same field a real keypress
    // populates alongside Keycode.
    private static void Key(Control canvas, Key keycode) =>
        canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { PhysicalKeycode = keycode, Pressed = true, Echo = false });

    private static CraftAction RunSeatingScript(Recipe recipe, System.Action<EngineeringBench> script)
    {
        var bench = new EngineeringBench();
        try
        {
            bench.Configure(recipe, "copper", Engineering, ImmutableSortedSet<string>.Empty);
            script(bench);
            for (var i = 0; i < EngineeringBench.CrankStrokesRequired; i++)
            {
                bench.CrankStroke();
            }

            return bench.EmittedAction!;
        }
        finally
        {
            bench.Free();
        }
    }
}
#endif
