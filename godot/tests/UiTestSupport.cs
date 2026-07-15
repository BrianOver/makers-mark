#if GDUNIT_TESTS
using System;
using System.Text;
using Godot;

namespace GodotClient.Tests;

/// <summary>Helpers for driving the U11 panels through their real Controls.</summary>
public static class UiTestSupport
{
    /// <summary>Instantiate main_ui.tscn into the live scene tree with the auto-clock paused.</summary>
    public static MainUi MountMainUi()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var ui = GD.Load<PackedScene>("res://scenes/panels/main_ui.tscn").Instantiate<MainUi>();
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
