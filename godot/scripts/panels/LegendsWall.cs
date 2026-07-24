using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Wave 4 (U21, plan 2026-07-24-003): a single monument to the spine — "your craft writes the
/// legends" made literal in one place. Renders <see cref="DramaState.Memorials"/> (the fallen,
/// name/day/gear), the Depths Progress board (deepest floor per hero), and per-item legend
/// entries — items with <see cref="LegendQuery.FamousBeatThreshold"/>+ proven
/// <see cref="AttributionBeatEvent"/>s OR a Wave-4a Signed Work (<see cref="Item.IsSigned"/>) —
/// each opening that item's <see cref="ProvenanceCard"/>. Same code-built-modal idiom as
/// <see cref="RaidForecastBoard"/>/<see cref="BestiaryPanel"/>: dim backdrop, centered themed
/// card, a Close button. Property-only/headless-test safe: no frame pump, no render scheduled by
/// building or showing it.
///
/// <para>Wave 4c (U18/U20): unlike the read-only Wave 4 wall, this one now submits player
/// actions from the memorial rows — an "Honor" button per un-honored <see cref="Memorial"/>
/// (queues <see cref="HonorMemorialAction"/>) and a "Reforge" button per still-reforgeable piece
/// of a fallen hero's worn gear (queues <see cref="ReforgeHeirloomAction"/>, reusing the source
/// item's own recipe + its baseline material key — a one-click default, not a full recipe
/// picker; scope-controlled per the unit spec). Carries its own settable <see cref="Adapter"/>
/// (the <see cref="CommissionBoard"/> precedent) rather than a <c>SimAdapter</c>-bound
/// <see cref="SimPanel"/> base — <see cref="ShowWall"/> still takes the live
/// <see cref="GameState"/> explicitly, so rendering never depends on <see cref="Adapter"/> being
/// set; only the new buttons do (null-safe: disabled when unset).</para>
/// </summary>
public partial class LegendsWall : Control
{
    private Label? _title;
    private VBoxContainer? _body;
    private ProvenanceCard? _provenance;

    /// <summary>Set by <c>MainUi</c> after construction so Honor/Reforge can queue actions.
    /// Null-safe: a wall shown before this is wired simply renders with disabled buttons
    /// (headless/test safe, <see cref="CommissionBoard.Adapter"/> precedent).</summary>
    public SimAdapter? Adapter { get; set; }

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

        var reforgedSourceIds = state.EventLog.OfType<HeirloomReforged>()
            .Select(e => e.SourceItem.Value)
            .ToHashSet();

        // Recent first — the newest loss is the one the player is most likely here to see.
        foreach (var memorial in state.Drama.Memorials.OrderByDescending(m => m.Day))
        {
            var row = AddRow(_body!);
            var text = $"  Day {memorial.Day} — {memorial.HeroName}, carrying {memorial.GearNamed}"
                + (memorial.Honored ? " — honored" : string.Empty);
            var label = AddLabel(row, text);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            if (!memorial.Honored)
            {
                var hero = memorial.Hero;
                var honor = new Button { Name = $"Honor_{hero.Value}", Text = "Honor" };
                honor.Pressed += () => Adapter?.Queue(new HonorMemorialAction(hero));
                honor.Disabled = Adapter is null;
                row.AddChild(honor);
            }

            RenderReforgeOptions(state, memorial.Hero, reforgedSourceIds);
        }
    }

    /// <summary>Wave 4c (U20): one "Reforge" row per still-eligible piece of
    /// <paramref name="hero"/>'s worn-at-death gear — a real item, recorded on that hero's
    /// <see cref="HeroDied"/> event, not already reforged. Reuses the item's OWN recipe id and
    /// that recipe's baseline material key as the one-click default (a full recipe/material
    /// picker is out of scope for this minimal surface — the sim handler is what matters).</summary>
    private void RenderReforgeOptions(GameState state, HeroId hero, HashSet<int> reforgedSourceIds)
    {
        var died = state.EventLog.OfType<HeroDied>().FirstOrDefault(d => d.Hero == hero);
        if (died is null)
        {
            return;
        }

        foreach (var slotItem in new[] { died.WornGear.Weapon, died.WornGear.Shield, died.WornGear.Armor, died.WornGear.Trinket })
        {
            if (slotItem is not { } itemId
                || reforgedSourceIds.Contains(itemId.Value)
                || !state.Items.TryGetValue(itemId.Value, out var item)
                || !ProfessionRegistry.TryGetRecipe(item.RecipeId, out var recipe))
            {
                continue;
            }

            var row = AddRow(_body!);
            var label = AddLabel(row, $"    reforge {item.Name}?");
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            var recipeId = recipe!.RecipeId;
            var materialKey = recipe.MaterialKey;
            var button = new Button { Name = $"Reforge_{itemId.Value}", Text = "Reforge" };
            button.Pressed += () => Adapter?.Queue(new ReforgeHeirloomAction(itemId, recipeId, materialKey));
            button.Disabled = Adapter is null;
            row.AddChild(button);
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
