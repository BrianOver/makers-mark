using System;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Wave 4 (U21, plan 2026-07-24-003): a single monument to the spine — "your craft writes the
/// legends" made literal in one place. Renders <see cref="DramaState.Memorials"/> (the fallen,
/// name/day/gear), the Depths Progress board (deepest floor per hero), and per-item legend
/// entries — items with <see cref="LegendQuery.FamousBeatThreshold"/>+ proven
/// <see cref="AttributionBeatEvent"/>s OR a Wave-4a Signed Work (<see cref="Item.IsSigned"/>) —
/// each opening that item's <see cref="ProvenanceCard"/>. Zero sim change — a pure projection of
/// existing <c>Contracts</c> data (KTD2), same code-built-modal idiom as
/// <see cref="RaidForecastBoard"/>/<see cref="BestiaryPanel"/>: dim backdrop, centered themed
/// card, a Close button; no <c>SimAdapter</c> binding — the caller hands in the already-live
/// <see cref="GameState"/> through <see cref="ShowWall"/>. Property-only/headless-test safe: no
/// frame pump, no render scheduled by building or showing it.
/// </summary>
public partial class LegendsWall : Control
{
    private Label? _title;
    private VBoxContainer? _body;
    private ProvenanceCard? _provenance;

    /// <summary>True iff the last <see cref="ShowWall"/> call rendered the invitational empty
    /// state (no memorials, no depths records, no legend items) — test hook.</summary>
    public bool ShowedEmptyState { get; private set; }

    /// <summary>Count of per-item legend rows rendered by the last <see cref="ShowWall"/> call —
    /// test hook.</summary>
    public int LegendItemCount { get; private set; }

    public override void _Ready() => EnsureBuilt();

    /// <summary>Populate from <paramref name="state"/> and open the overlay.</summary>
    public void ShowWall(GameState state)
    {
        EnsureBuilt();
        Clear(_body!);

        var legendItems = LegendItems(state);
        LegendItemCount = legendItems.Count;
        ShowedEmptyState = state.Drama.Memorials.IsEmpty && state.Drama.DepthsBoard.IsEmpty && legendItems.Count == 0;

        if (ShowedEmptyState)
        {
            AddLabel(_body!, "No legends yet — the Mine hasn't claimed anyone; your work is about to change that.");
            Visible = true;
            return;
        }

        RenderMemorials(state);
        RenderDepthsRecords(state);
        RenderLegendItems(state, legendItems);

        Visible = true;
    }

    public void Close() => Visible = false;

    private void RenderMemorials(GameState state)
    {
        AddHeader(_body!, "THE FALLEN");
        if (state.Drama.Memorials.IsEmpty)
        {
            AddLabel(_body!, "  Nobody has fallen yet.");
            return;
        }

        // Recent first — the newest loss is the one the player is most likely here to see.
        foreach (var memorial in state.Drama.Memorials.OrderByDescending(m => m.Day))
        {
            AddLabel(_body!, $"  Day {memorial.Day} — {memorial.HeroName}, carrying {memorial.GearNamed}");
        }
    }

    private void RenderDepthsRecords(GameState state)
    {
        AddHeader(_body!, "DEPTHS RECORDS");
        if (state.Drama.DepthsBoard.IsEmpty)
        {
            AddLabel(_body!, "  No depth records yet — the Mine awaits.");
            return;
        }

        var standings = state.Drama.DepthsBoard
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key);
        foreach (var (heroValue, floor) in standings)
        {
            AddLabel(_body!, $"  floor {floor} — {HeroName(state, new HeroId(heroValue))}");
        }
    }

    private void RenderLegendItems(GameState state, System.Collections.Generic.List<Item> legendItems)
    {
        AddHeader(_body!, "LEGENDARY GEAR");
        if (legendItems.Count == 0)
        {
            AddLabel(_body!, "  No legendary gear yet — a Signed Work or a proven hero of steel is still to come.");
            return;
        }

        foreach (var item in legendItems)
        {
            var row = AddRow(_body!);
            var label = item.IsSigned
                ? $"✦ {item.Name} — \"{item.SignedName}\""
                : $"★ {item.Name} — {AttributionBeatCount(state, item.Id)} proven beats";
            var button = AddButton(row, $"Legend_{item.Id.Value}", label, () => OnShowProvenance(state, item.Id));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Alignment = HorizontalAlignment.Left;
        }
    }

    /// <summary>Items that earn a legend row: a Signed Work (U19), or at least
    /// <see cref="LegendQuery.FamousBeatThreshold"/> proven attribution beats. Signed Works first,
    /// then by beat count descending, tie-broken by item id for determinism.</summary>
    private static System.Collections.Generic.List<Item> LegendItems(GameState state)
    {
        var beatCounts = state.EventLog.OfType<AttributionBeatEvent>()
            .GroupBy(b => b.Item.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return state.Items.Values
            .Where(item => item.IsSigned
                || (beatCounts.TryGetValue(item.Id.Value, out var count) && count >= LegendQuery.FamousBeatThreshold))
            .OrderByDescending(item => item.IsSigned)
            .ThenByDescending(item => beatCounts.TryGetValue(item.Id.Value, out var count) ? count : 0)
            .ThenBy(item => item.Id.Value)
            .ToList();
    }

    private static int AttributionBeatCount(GameState state, ItemId item) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count(b => b.Item == item);

    private void OnShowProvenance(GameState state, ItemId itemId)
    {
        EnsureBuilt();
        _provenance!.ShowFor(state, itemId);
    }

    private void EnsureBuilt()
    {
        if (_body is not null)
        {
            return;
        }

        Name = "LegendsWall";
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // swallow input like every other modal overlay here

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.Card("LegendsWallPanel");
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 400) };
        panel.AddChild(box);

        _title = AddLabel(box, "THE LEGENDS WALL");
        _title.Name = "LegendsWallTitle";
        _title.ThemeTypeVariation = GameTheme.HeaderThemeType;
        _title.AddThemeColorOverride("font_color", GameTheme.HeaderColor);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        _body = new VBoxContainer { Name = "LegendsWallBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_body);

        AddButton(box, "LegendsWallClose", "Close", Close);

        // Added LAST (after the panel body) so it draws over the wall, self-contained
        // (ScryingMirror precedent), hidden until a legend-item row opens it.
        _provenance = new ProvenanceCard { Visible = false };
        AddChild(_provenance);
    }

    // ── minimal self-contained widget helpers (mirrors ProvenanceCard/RaidForecastBoard) ──

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

    private static string HeroName(GameState state, HeroId id) =>
        state.Heroes.TryGetValue(id.Value, out var hero) ? hero.Name : $"Hero #{id.Value}";
}
