using System;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Base for the U11 management panels (KTD10 — these ARE the real UI skeleton).
/// A panel binds the ONE <see cref="SimAdapter"/> (KTD2), renders
/// <c>Adapter.CurrentState</c>, and queues <see cref="PlayerAction"/>s from its
/// buttons. Adapter-only: no game rules in any panel. Content is rebuilt
/// synchronously on <see cref="Refresh"/> so tests can assert rendered text
/// immediately after a tick.
///
/// <para>P007 U2 (KTD2): the themed widget kit (<see cref="UiKit"/>) is exposed below as
/// protected passthroughs alongside the original <see cref="AddLabel"/>/<see cref="AddHeader"/>/
/// <see cref="AddButton"/>/<see cref="AddRow"/>/<see cref="AddIcon"/> — those keep their exact
/// behavior (still test-load-bearing) while screens rebuilt on the kit compose
/// <see cref="Card"/>/<see cref="Section"/>/<see cref="StatChip"/>/<see cref="PortraitFrame"/>/
/// <see cref="ArtRect"/> instead of bare rows. No panel is required to switch — the "placeholder
/// look by design" era is over, but the lifecycle (Bind/Refresh/Clear) and the HeroName/ItemName
/// lookups are unchanged.</para>
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
        label.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        // P007 polish: opt this header into the display-font theme-type variation (never the
        // base "Label" type) — see GameTheme's HeaderFont remarks.
        label.ThemeTypeVariation = GameTheme.HeaderThemeType;
        return label;
    }

    protected static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    /// <summary>
    /// U6 (R6) prevention half: reflect the kernel's own legality verdict on a button —
    /// Disabled with a player-phrased tooltip when the queued action would provably be
    /// refused. MIRROR, never replace: <paramref name="legal"/> must be read off the same
    /// sim-exposed facts/predicates the action's handler checks, and the kernel remains
    /// the authority on apply — a stale enable is still honestly rejected (and toasted
    /// by MainUi), never silently dropped.
    /// </summary>
    protected static Button GateButton(Button button, bool legal, string whyNot)
    {
        button.Disabled = !legal;
        button.TooltipText = legal ? string.Empty : whyNot;
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

    // ── P007 U2: themed widget kit passthroughs (GodotClient.Ui.UiKit) ───────────────────────

    /// <summary>A plain themed card container — see <see cref="UiKit.Card"/>.</summary>
    protected static PanelContainer Card(string? name = null) => UiKit.Card(name);

    /// <summary>A titled section (header + body VBox) — see <see cref="UiKit.Section"/>.</summary>
    protected static UiKit.SectionView Section(string title) => UiKit.Section(title);

    /// <summary>A small themed label/value pill — see <see cref="UiKit.StatChip"/>.</summary>
    protected static Control StatChip(string label, string value, UiKit.ChipTone tone = UiKit.ChipTone.Neutral) =>
        UiKit.StatChip(label, value, tone);

    /// <summary>A bordered hero-portrait frame — see <see cref="UiKit.PortraitFrame"/>.</summary>
    protected static Control PortraitFrame(
        string artKey, float size = UiKit.PortraitSize, Texture2D? fallbackIcon = null, string? caption = null) =>
        UiKit.PortraitFrame(artKey, size, fallbackIcon, caption);

    /// <summary>The fallback-safe art-loader bridge — see <see cref="UiKit.ArtRect"/>.</summary>
    protected static Control ArtRect(
        string artKey, Vector2 size, Texture2D? fallbackIcon = null, string? caption = null) =>
        UiKit.ArtRect(artKey, size, fallbackIcon, caption);
}
