#if GDUNIT_TESTS
using System;
using System.Text;
using GameSim.Contracts;
using Godot;

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
    /// <summary>Instantiate main_ui.tscn into the live scene tree with the auto-clock paused.</summary>
    public static MainUi MountMainUi() => MountMainUi(adapterOverride: null);

    /// <summary>
    /// Mount with an injected adapter (U12 scenario tests — e.g., a crafted wipe-day
    /// campaign). Pass null for the default fresh seed-2026 campaign.
    /// </summary>
    public static MainUi MountMainUi(SimAdapter? adapterOverride)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var ui = GD.Load<PackedScene>("res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
        MainUi.AdapterOverride = adapterOverride; // static handoff (U4) — _Ready consumes it
        tree.Root.AddChild(ui); // triggers _Ready: adapter + panels + bindings
        ui.Clock.Pause();       // tests drive phases explicitly
        return ui;
    }

    public static void Unmount(MainUi ui)
    {
        ui.GetParent()?.RemoveChild(ui);
        ui.Free();
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
}
#endif
