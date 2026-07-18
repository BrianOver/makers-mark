using System;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// Base for the U11 management panels (KTD10 — these ARE the real UI skeleton).
/// A panel binds the ONE <see cref="SimAdapter"/> (KTD2), renders
/// <c>Adapter.CurrentState</c>, and queues <see cref="PlayerAction"/>s from its
/// buttons. Adapter-only: no game rules in any panel. Content is rebuilt
/// synchronously on <see cref="Refresh"/> so tests can assert rendered text
/// immediately after a tick — plain Controls, placeholder look by design.
/// </summary>
public abstract partial class SimPanel : Control
{
    protected SimAdapter? Adapter { get; private set; }

    public void Bind(SimAdapter adapter)
    {
        Adapter = adapter;
        Refresh();
    }

    /// <summary>Rebuild rendered content from <c>Adapter.CurrentState</c>.</summary>
    public abstract void Refresh();

    /// <summary>
    /// Remove and free children immediately (not QueueFree) so a refresh leaves no
    /// stale text in the tree. Only ever called from Refresh — never from a signal
    /// handler of a node being cleared.
    /// </summary>
    protected static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.Free();
        }
    }

    protected static Label AddLabel(Node parent, string text)
    {
        // ExpandFill (U7/R7): an autowrap label's minimum width is ~1px, so inside an HBox row
        // (which hands non-expand children their minimum) it collapses to one character per
        // line. Expanding claims the row's leftover width; in a VBox it is a no-op (cross-axis
        // already fills).
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        parent.AddChild(label);
        return label;
    }

    protected static Label AddHeader(Node parent, string text)
    {
        var label = AddLabel(parent, text);
        label.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
        return label;
    }

    protected static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    protected static SpinBox AddSpinBox(Node parent, string name, double min, double max, double value)
    {
        var spin = new SpinBox { Name = name, MinValue = min, MaxValue = max, Rounded = true, Value = value };
        parent.AddChild(spin);
        return spin;
    }

    protected static HBoxContainer AddRow(Node parent)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        return row;
    }

    /// <summary>
    /// Add a small themed icon (U16) next to text. Decoration only — clicks pass
    /// through, and a null texture yields a blank spacer so callers need no guard.
    /// </summary>
    protected static TextureRect AddIcon(Node parent, Texture2D? texture, int size = 22)
    {
        var rect = new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = new Vector2(size, size),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        parent.AddChild(rect);
        return rect;
    }

    /// <summary>Full-rect ScrollContainer wrapping a VBox — the standard panel body.</summary>
    protected VBoxContainer BuildScrollBody()
    {
        // Horizontal scroll disabled (U7/R7): with it enabled the child gets unbounded
        // horizontal space, so autowrap labels lose their real wrap width. Vertical-only.
        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scroll);
        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(body);
        return body;
    }

    protected string HeroName(HeroId id) =>
        Adapter is not null && Adapter.CurrentState.Heroes.TryGetValue(id.Value, out var hero)
            ? hero.Name
            : id.ToString();

    protected string ItemName(ItemId id) =>
        Adapter is not null && Adapter.CurrentState.Items.TryGetValue(id.Value, out var item)
            ? item.Name
            : id.ToString();
}
