#if GDUNIT_TESTS
using System;
using System.Text;
using System.Threading.Tasks;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Tests;

/// <summary>Helpers for driving the U11 panels through their real Controls.</summary>
public static class UiTestSupport
{
    /// <summary>Hard cap on ticks per day: a stuck phase machine fails the test instead of
    /// hanging. Above any real day length (3 today, 5 after staged-plan U2).</summary>
    public const int MaxPhasesPerDay = 8;

    /// <summary>
    /// Advance whole days WITHOUT hardcoding a day's phase count (loop-until-Morning): tick
    /// AdvancePhase() until the sim reports Morning again. Day-length agnostic by design —
    /// green on the 3-tick day (today) and the 5-tick staged-resolution kernel (staged-plan
    /// U2) alike. The DayPhase enum grew two values (contracts PR #34) one PR BEFORE the day
    /// grows two ticks (U2), so counting phases — <c>Enum.GetValues&lt;DayPhase&gt;().Length</c>
    /// or a literal 3 — would break in exactly that window. Called at a fresh Morning it
    /// advances one full day; called mid-day it finishes the current day.
    /// </summary>
    public static void AdvanceDay(MainUi ui, int days = 1)
    {
        for (var day = 0; day < days; day++)
        {
            var ticks = 0;
            do
            {
                ui.Adapter.AdvancePhase();
                if (++ticks > MaxPhasesPerDay)
                {
                    throw new InvalidOperationException(
                        $"AdvanceDay exceeded {MaxPhasesPerDay} ticks without returning to Morning.");
                }
            }
            while (ui.Adapter.CurrentState.Phase != DayPhase.Morning);
        }
    }

    /// <summary>
    /// Tick AdvancePhase() until the sim sits AT <paramref name="phase"/> (no-op if already
    /// there), capped like <see cref="AdvanceDay"/>. Day-length agnostic — use when a test
    /// needs a specific phase within a day (e.g. day-N Evening) instead of a literal tick count.
    /// </summary>
    public static void AdvanceToPhase(MainUi ui, DayPhase phase)
    {
        var ticks = 0;
        while (ui.Adapter.CurrentState.Phase != phase)
        {
            ui.Adapter.AdvancePhase();
            if (++ticks > MaxPhasesPerDay)
            {
                throw new InvalidOperationException(
                    $"AdvanceToPhase did not reach {phase} within {MaxPhasesPerDay} ticks.");
            }
        }
    }
    /// <summary>Instantiate main_ui.tscn into the live scene tree with the auto-clock gated
    /// and paused.</summary>
    public static MainUi MountMainUi() => MountMainUi(adapterOverride: null);

    /// <summary>
    /// Mount with an injected adapter (U12 scenario tests — e.g., a crafted wipe-day
    /// campaign). Pass null for the default fresh seed-2026 campaign.
    /// </summary>
    /// <param name="adapterOverride">Injected campaign, or null for a fresh seed-2026 one.</param>
    /// <param name="forceGated">
    /// U15: the living clock now defaults to auto-advance ON, and a leftover
    /// <c>ClockSettings</c> file from an earlier test/run could otherwise leak a preference
    /// into this mount. Defaults true — every existing suite drives phases explicitly via
    /// <see cref="AdvanceDay"/>/<see cref="AdvanceToPhase"/>/direct <c>Adapter.AdvancePhase</c>
    /// calls and must see a fully inert clock regardless of persisted state. Pass false only
    /// to observe what <c>MainUi._Ready</c> itself applied (e.g. asserting the persisted
    /// setting/AE1 default landed) before this override would mask it.
    /// </param>
    public static MainUi MountMainUi(SimAdapter? adapterOverride, bool forceGated = true)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var ui = GD.Load<PackedScene>("res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        MainUi.AdapterOverride = adapterOverride; // static handoff (U4) — _Ready consumes it
        tree.Root.AddChild(ui); // triggers _Ready: adapter + panels + bindings
        if (forceGated)
        {
            ui.Clock.Pause();          // tests drive phases explicitly
            ui.Clock.SetAutoAdvance(false); // U15: never let a persisted/ON default fire a tick
        }

        return ui;
    }

    public static void Unmount(MainUi ui)
    {
        ui.GetParent()?.RemoveChild(ui);
        ui.Free();

        // U15: every MountMainUi pairs with Unmount (one shared teardown, every call site) —
        // wipe any ClockSettings file a pressed Auto toggle may have written mid-test so a
        // local test run can never leak a persisted "manual mode" preference into the
        // developer's own real Godot user:// data, and no suite can leak one into another.
        MainUi.ClockSettings.DeleteForTests();

        // U23: same leak guard for the tutorial-flow chain's own user:// file — a completed/
        // dismissed flag from one test must never suppress the next suite's tutorial chip.
        TutorialFlow.DeleteForTests();
    }

    /// <summary>Find a (code-built, unowned) control by name anywhere under root.</summary>
    public static T Find<T>(Node root, string name) where T : Node =>
        root.FindChild(name, recursive: true, owned: false) as T
        ?? throw new InvalidOperationException($"No {typeof(T).Name} named '{name}' under {root.Name}.");

    /// <summary>Press a button the way a user would — through its pressed signal.</summary>
    public static void Press(Node root, string buttonName) =>
        Find<Button>(root, buttonName).EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>
    /// Press like <see cref="Press"/> but fail loudly when the control renders Disabled —
    /// the U8 loop's proof that the driven path never leans on a gated-off button (a real
    /// player could not have clicked it, so the test must not either).
    /// </summary>
    public static void PressEnabled(Node root, string buttonName)
    {
        var button = Find<Button>(root, buttonName);
        if (button.Disabled)
        {
            throw new InvalidOperationException($"Button '{buttonName}' was Disabled at press time.");
        }

        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    /// <summary>Left-click a plain Control (U12 town markers) — through its gui_input signal.</summary>
    public static void Click(Control control) =>
        control.EmitSignal(
            Control.SignalName.GuiInput,
            new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });

    /// <summary>
    /// World-rework U8 (gate G1) — RED, kept for the record, NOT on the passing path.
    /// Attempted: left-click a WORLD position inside a SubViewport by pushing a
    /// mouse-motion then a mouse-button <see cref="InputEventMouseButton"/> through
    /// <see cref="SubViewport.PushInput"/> (positions transformed world→viewport-local via
    /// <see cref="Viewport.CanvasTransform"/> so it stays correct under a scrolled
    /// Camera2D), with physics frames settled both before and after the push.
    /// Verdict (observed on a real Godot_v4.6.3-stable_mono_win64 binary via GODOT_BIN,
    /// not just "no runner found"): the click never reaches Area2D physics picking in
    /// gdUnit4Net's headless run — see <c>Area2dPickingSpikeTests</c> for the red output.
    /// Do not build tests on this path. Use <see cref="TryClickArea"/> instead.
    /// </summary>
    public static async Task ClickWorld(SubViewport viewport, Vector2 worldPos)
    {
        var screenPos = viewport.CanvasTransform * worldPos;

        viewport.PushInput(
            new InputEventMouseMotion { Position = screenPos, GlobalPosition = screenPos },
            inLocalCoords: true);
        viewport.PushInput(
            new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screenPos, GlobalPosition = screenPos },
            inLocalCoords: true);
        viewport.PushInput(
            new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screenPos, GlobalPosition = screenPos },
            inLocalCoords: true);

        await SettlePhysics(viewport);
    }

    /// <summary>
    /// World-rework U8 (gate G1) FALLBACK — the seam tests must actually use. Since headless
    /// physics picking is unproven (see <see cref="ClickWorld"/>), world-clickable Area2D
    /// nodes are driven directly: this reimplements the same rectangle hit-test the engine's
    /// picking pass would do (centered on <paramref name="area"/>'s global position, against
    /// its first <see cref="RectangleShape2D"/> child), and on a hit emits the SAME
    /// <see cref="Area2D.SignalName.InputEvent"/> signal real picking would emit — so
    /// production click-handling code does not need to know it is under test. Mirrors the
    /// existing <see cref="Click"/> seam for Controls (drives GuiInput directly rather than
    /// real OS input). Camera-agnostic by construction: it hit-tests in world space, so a
    /// scrolled Camera2D cannot desync it the way a screen-coordinate helper could.
    /// Returns whether the point hit (so miss-case tests can assert on the return value too).
    /// </summary>
    public static bool TryClickArea(Area2D area, Vector2 worldPos)
    {
        foreach (var child in area.GetChildren())
        {
            if (child is not CollisionShape2D { Disabled: false, Shape: RectangleShape2D rect } shape)
            {
                continue;
            }

            var local = worldPos - (area.GlobalPosition + shape.Position);
            if (Mathf.Abs(local.X) > rect.Size.X / 2f || Mathf.Abs(local.Y) > rect.Size.Y / 2f)
            {
                continue;
            }

            area.EmitSignal(
                Area2D.SignalName.InputEvent,
                area.GetViewport(),
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true },
                0);
            return true;
        }

        return false;
    }

    /// <summary>
    /// World-rework U8 (gate G1) MANUAL-SMOKE RECIPE — not automated, never runs in CI.
    /// <see cref="TryClickArea"/> intentionally bypasses the engine's own picking pass, so it
    /// cannot catch a regression IN that pass. Run this by hand once per milestone that
    /// touches world-click wiring (U14 TownWorld promotion, any later Area2D interactable):
    /// launch the real game (`dotnet run` / the Godot editor's Play), hover and left-click a
    /// world-clickable Area2D on screen, and confirm the expected in-game reaction fires.
    /// If it does not, physics picking itself is broken — a defect this suite cannot see.
    /// </summary>
    public const string ManualSmokeRecipe =
        "Launch the real game, left-click a world-clickable Area2D, confirm its in-game " +
        "reaction fires. TryClickArea cannot catch a regression in real picking.";

    /// <summary>All user-visible text rendered under a control (labels, buttons, item lists).</summary>
    public static string RenderedText(Node root)
    {
        var text = new StringBuilder();
        Collect(root, text);
        return text.ToString();
    }

    private static void Collect(Node node, StringBuilder text)
    {
        switch (node)
        {
            case Label label:
                text.AppendLine(label.Text);
                break;
            case Button button:
                text.AppendLine(button.Text);
                break;
            case ItemList list:
                for (var i = 0; i < list.ItemCount; i++)
                {
                    text.AppendLine(list.GetItemText(i));
                }

                break;
        }

        foreach (var child in node.GetChildren())
        {
            Collect(child, text);
        }
    }

    /// <summary>
    /// P007 U8 cross-screen smoke guard: let container layout settle over a few real process
    /// frames before reading geometry. A container's <c>queue_sort()</c> is deferred, and nested
    /// containers can cascade across several frames, so <see cref="Control.Size"/>/
    /// <see cref="Control.GetCombinedMinimumSize"/> read immediately after a mutation (a mount, a
    /// tab switch, a Refresh) can still show stale — often zero — values without this pump.
    /// Mirrors <c>LayoutTests</c>' private helper of the same shape, promoted here so any suite
    /// asserting layout geometry can reuse it.
    /// </summary>
    public static async Task SettleLayout(Node node)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < 3; i++)
        {
            await node.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>
    /// World-rework U8: pump a few physics frames so 2D physics-space mutations (a freshly
    /// added CollisionShape2D, a camera scroll, a queued picking event) are committed before
    /// the test reads results. The physics twin of <see cref="SettleLayout"/>.
    /// </summary>
    public static async Task SettlePhysics(Node node)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < 3; i++)
        {
            await node.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// U20: pump both process AND physics frames for real, letting the engine's own
    /// <c>_Process</c>/<c>_PhysicsProcess</c> run at their normal cadence — the avatar/world-input
    /// twin of <see cref="SettlePhysics"/>, just for longer stretches (walking a real distance, or
    /// letting a click-to-move path settle, needs many ticks). Deliberately real engine frames
    /// rather than manual method calls, so a WASD/collision test exercises the exact same code
    /// path production does.
    /// </summary>
    public static async Task PumpWorldFrames(Node node, int frames)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < frames; i++)
        {
            await node.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await node.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// P007 U8 cross-screen smoke guard: true iff the U1 Theme cascade (<see cref="GameTheme.
    /// Build"/>, assigned ONLY at the <c>MainUi</c> root) reaches <paramref name="panel"/> at a
    /// legible size. <see cref="Control.GetThemeDefaultFontSize"/> walks the ancestor chain for
    /// the nearest assigned <see cref="Theme"/> and returns THAT theme's <c>DefaultFontSize</c> —
    /// so this reads the cascade source itself rather than probing an arbitrary descendant
    /// Label. Deliberately NOT a Label-text probe: several panels carry Labels with intentional
    /// LOCAL per-node overrides (Town's building/gate markers, <c>UiKit.StatChip</c>/Section-
    /// header pills) smaller than the legibility floor by design, which would make a Label-probe
    /// a false negative even though the cascade itself reaches the panel fine.
    /// </summary>
    public static bool ThemeReachesPanel(Control panel) =>
        panel.GetThemeDefaultFontSize() >= GameTheme.LegibilityFloor;

    /// <summary>
    /// P007 U8 cross-screen smoke guard: true iff <paramref name="control"/> laid out to a real,
    /// non-zero footprint — the general panel-level guard against the "one-character-per-line"
    /// layout-collapse *class* (R7/R15) that <c>LayoutTests</c> hunts at the individual-autowrap-
    /// label instance level. Checks the settled <see cref="Control.Size"/> OR
    /// <see cref="Control.GetCombinedMinimumSize"/>, whichever is non-zero, since a fixed-
    /// <c>CustomMinimumSize</c> widget can report a healthy minimum a frame before its parent's
    /// Size catches up (call after <see cref="SettleLayout"/>, on a control whose tab is current
    /// — a hidden <c>TabContainer</c> page is never laid out).
    /// </summary>
    public static bool HasNonDegenerateLayout(Control control) =>
        (control.Size.X > 1f && control.Size.Y > 1f) ||
        (control.GetCombinedMinimumSize().X > 1f && control.GetCombinedMinimumSize().Y > 1f);

    /// <summary>
    /// T3: the 3D-town twin of <see cref="WalkUntilArrived"/> — pumps real physics frames until
    /// <paramref name="body"/> settles within 1.2 units of <paramref name="target"/> (matches the
    /// arrival radius later click-to-move code uses, T6), rather than hand-computing a frame
    /// count from speed/distance. Capped and throws on exhaustion, same as every other tick-loop
    /// helper in this file, so a genuinely stuck body fails the test instead of hanging it.
    /// </summary>
    public static async Task WalkUntilArrived3D(Node ctx, Node3D body, Vector3 target, int maxFrames = 600)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            if (body.GlobalPosition.DistanceTo(target) < 1.2f)
            {
                return;
            }

            await ctx.ToSignal(ctx.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        throw new System.Exception($"body did not arrive within {maxFrames} frames (at {body.GlobalPosition}, target {target})");
    }

}
#endif
