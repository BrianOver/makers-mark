using System;
using System.Linq;
using GameSim.Contracts;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// U5 (world-rework plan, "your craft writes the legends" made touchable): a small READ-ONLY
/// popup card showing one item's whole life story — <see cref="Item.History"/> rendered as
/// ordered prose (Day-ascending), the <see cref="Item.Mark"/> maker's mark, and the three
/// forge-beat sub-scores (<see cref="Item.CraftSubScores"/>) when the item carries them. Zero sim
/// change — a pure projection of existing <c>Contracts</c> data (KTD2).
///
/// <para>Self-contained by design (this unit's scope guard keeps <c>MainUi</c> untouched): every
/// surface that lists a crafted item (<c>ShopPanel</c>'s shelf/unshelved sections,
/// <c>HeroesPanel</c>'s gear rows, <c>ScryingMirror</c>'s ★ attribution lines) instantiates and
/// adds ONE of these as its own child, then calls <see cref="ShowFor"/> with its live
/// <see cref="GameState"/> and the clicked <see cref="ItemId"/> — mirroring the code-built modal
/// idiom every other overlay in this codebase already uses (<c>LedgerModal</c>/<c>CampPanel</c>/
/// <c>ScryingMirror</c>: dim backdrop, centered themed panel, a Close button).</para>
/// </summary>
public partial class ProvenanceCard : Control
{
    private Label? _title;
    private VBoxContainer? _body;

    /// <summary>The item currently shown, or null before the first <see cref="ShowFor"/> call —
    /// test hook (mirrors <c>LedgerModal.ShownDay</c>).</summary>
    public ItemId? ShownItemId { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>
    /// Populate with <paramref name="itemId"/>'s current history/mark/sub-scores from
    /// <paramref name="state"/> and open the overlay. A missing item id (defensive — the caller's
    /// own list should never offer a dangling one) closes the card instead of throwing.
    /// </summary>
    public void ShowFor(GameState state, ItemId itemId)
    {
        EnsureBuilt();
        if (!state.Items.TryGetValue(itemId.Value, out var item))
        {
            Visible = false;
            return;
        }

        ShownItemId = itemId;
        Render(item);
        Visible = true;
    }

    public void CloseCard() => Visible = false;

    private void Render(Item item)
    {
        Clear(_body!);
        _title!.Text = $"{item.Name} [{item.Quality}] — {item.Slot}";

        var markRow = AddRow(_body!);
        markRow.AddChild(ItemIcon(item));
        AddLabel(markRow, item.Mark is { } mark
            ? $"Forged by {mark.CrafterName} on day {mark.CraftedOnDay}."
            : "No maker's mark — not player-crafted.");

        // Forge-beat sub-scores (per-mille, smelt/forge/quench order) — only when the item
        // actually carries them (empty for auto-crafted/rival/pre-Phase-A items, per the
        // contract's own doc); a missing record renders no section, never an error.
        if (item.CraftSubScores.Count == 3)
        {
            AddHeader(_body!, "FORGE-BEAT SCORES:");
            var scoreRow = AddRow(_body!);
            scoreRow.AddChild(StatChip("Smelt", $"{item.CraftSubScores[0]}‰"));
            scoreRow.AddChild(StatChip("Forge", $"{item.CraftSubScores[1]}‰"));
            scoreRow.AddChild(StatChip("Quench", $"{item.CraftSubScores[2]}‰"));
        }

        AddHeader(_body!, "HISTORY:");
        if (item.History.IsEmpty)
        {
            AddLabel(_body!, "Fresh off the forge — no history yet.");
        }
        else
        {
            // Day-ascending prose, one line per entry (OrderBy is a stable sort — same-day
            // entries keep their originally-appended relative order).
            foreach (var entry in item.History.OrderBy(h => h.Day))
            {
                AddLabel(_body!, $"Day {entry.Day} — {entry.Kind}: {entry.Detail}");
            }
        }
    }

    private static Control ItemIcon(Item item)
    {
        var rect = new TextureRect
        {
            Texture = AssetCatalog.ItemIcon(item.RecipeId) ?? IconRegistry.Slot(item.Slot),
            CustomMinimumSize = new Vector2(40, 40),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        return rect;
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "ProvenanceCard";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("ProvenanceCardPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(420, 320) };
        panel.AddChild(box);

        _title = AddLabel(box, string.Empty);
        _title.Name = "ProvenanceTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "ProvenanceBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "ProvenanceClose", "Close", CloseCard);
    }

    // ── minimal self-contained widget helpers (mirrors SimPanel's — this class deliberately
    // does not derive from SimPanel: it needs no SimAdapter binding, only the caller's already-
    // live GameState handed in through ShowFor) ─────────────────────────────────────────────────

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.Free();
        }
    }

    private static Label AddLabel(Node parent, string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        parent.AddChild(label);
        return label;
    }

    private static Label AddHeader(Node parent, string text)
    {
        var label = AddLabel(parent, text);
        label.AddThemeColorOverride("font_color", GameTheme.HeaderColor);
        label.ThemeTypeVariation = GameTheme.HeaderThemeType;
        return label;
    }

    private static Button AddButton(Node parent, string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        parent.AddChild(button);
        return button;
    }

    private static HBoxContainer AddRow(Node parent)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);
        return row;
    }

    private static Control StatChip(string label, string value) => UiKit.StatChip(label, value);
}
