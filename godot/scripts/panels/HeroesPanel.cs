using System.Collections.Generic;
using System.Linq;
using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Drama;
using Godot;
using GodotClient.Town;
using GodotClient.Ui;

namespace GodotClient.Panels;

/// <summary>
/// Hero roster + detail (R12 display half): a scrollable portrait-card roster
/// (class-tinted portrait, level/gold/deepest chips or DIED state) and a detail
/// pane showing worn gear with item art and lifetime maker's-mark tallies
/// (<see cref="LedgerQuery.MarkTally"/>) plus the hero's item memories.
/// Read-only — heroes are autonomous (A2).
///
/// <para>P007 U4 (R12/KTD2/KTD3): the roster is a <see cref="GridContainer"/> of
/// <see cref="PortraitFrame"/> cards (art key <c>"hero-"+classId</c> via
/// <see cref="AssetCatalog.HeroPortraitId"/>, falling back to the hand-authored
/// <see cref="IconRegistry.Sprite"/> SVG then the kit's own placeholder), each a themed
/// <see cref="Button"/> so a click drives the exact same <see cref="RenderDetail"/> path the
/// town-click routing (<see cref="SelectHero"/>) and the old <c>ItemList</c> selection did.
/// The class tint (<see cref="HeroSprite.RoleColor"/>) is applied to the portrait's own
/// icon layer only, never the card's text, so name/chip legibility is unaffected.</para>
///
/// <para>Rebuilt (world-rework U4): the card is now a content-honest
/// <see cref="PanelContainer"/> — its predecessor, a bare <see cref="Button"/>, is not a
/// <see cref="Container"/>, so its VBox children never sized or clipped it (3
/// <see cref="UiKit.StatChipCompact"/> chips needed ~270px in a 140px card; captions clipped
/// mid-word). A transparent overlay <see cref="Button"/> now drives the click, the roster name
/// ellipsizes instead of wrapping mid-word, and <see cref="TintPortraitFrame"/> tints the
/// frame/underlay only — a painted portrait's own texture stays untinted (white Modulate).</para>
/// </summary>
public partial class HeroesPanel : SimPanel
{
    /// <summary>Roster grid column count — two wide fits the panel's left column comfortably.</summary>
    private const int RosterColumns = 2;

    /// <summary>Roster column width (px) — two <see cref="RosterCardSize"/> cards plus gutters.</summary>
    private const float RosterColumnWidth = 300f;

    /// <summary>One roster card's footprint (px): portrait + name + a chip row/DIED line. A
    /// FLOOR only — the card's real <see cref="PanelContainer"/> sizing grows past this when its
    /// content (the chip row) needs more.</summary>
    private static readonly Vector2 RosterCardSize = new(140f, 190f);

    /// <summary>Gear-row item-art tile edge length (px).</summary>
    private const float GearArtSize = 48f;

    /// <summary>Sane minimum width (px) for the gear row's info column (R7-class guard, mirrors
    /// <see cref="RosterCardSize"/>'s fixed-width technique) — a long item name (e.g. "Soldier's
    /// Longsword") must keep enough room to wrap at word boundaries, not mid-word.</summary>
    private const float GearInfoColumnMinWidth = 180f;

    private GridContainer? _rosterGrid;
    private VBoxContainer? _detail;
    private readonly Dictionary<int, Button> _heroCardButtons = [];
    private int _selectedHeroId = -1;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        Clear(_rosterGrid!);
        _heroCardButtons.Clear();

        foreach (var hero in state.Heroes.Values)
        {
            var (frame, button) = BuildHeroCard(hero);
            _rosterGrid!.AddChild(frame);
            _heroCardButtons[hero.Id.Value] = button;
        }

        if (state.Heroes.IsEmpty)
        {
            Clear(_detail!);
            AddLabel(_detail!, "no heroes in town");
            return;
        }

        var selected = state.Heroes.ContainsKey(_selectedHeroId) ? _selectedHeroId : state.Heroes.Values.First().Id.Value;
        RenderDetail(selected);
    }

    /// <summary>
    /// Bind a specific hero into the detail pane (U12 town click routing, R20).
    /// Highlights the matching roster card (if any) and renders their detail.
    /// </summary>
    public void SelectHero(int heroValue)
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        RenderDetail(heroValue);
    }

    private void RenderDetail(int heroValue)
    {
        _selectedHeroId = heroValue;
        foreach (var (id, button) in _heroCardButtons)
        {
            // NoSignal: this is a programmatic sync, never a real click — using the plain
            // setter would re-emit `toggled`/risk re-entrancy through the card's own
            // Pressed handler.
            button.SetPressedNoSignal(id == heroValue);
        }

        var state = Adapter!.CurrentState;
        Clear(_detail!);
        if (!state.Heroes.TryGetValue(heroValue, out var hero))
        {
            return;
        }

        AddHeader(_detail!, $"{hero.Name} — {ClassRegistry.Require(hero.ClassId).DisplayName}");
        AddLabel(_detail!, hero.Alive
            ? $"Level {hero.Level} | HP {hero.MaxHp} | {hero.Gold}g | deepest floor {hero.DeepestFloorReached}"
            : $"DIED day {hero.DiedOnDay} on floor record {hero.DeepestFloorReached}");

        AddHeader(_detail!, "GEAR:");
        var roleColor = HeroSprite.RoleColor(hero.ClassId);
        foreach (var (slot, itemId) in new (ItemSlot, ItemId?)[]
                 {
                     (ItemSlot.Weapon, hero.Gear.Weapon),
                     (ItemSlot.Shield, hero.Gear.Shield),
                     (ItemSlot.Armor, hero.Gear.Armor),
                 })
        {
            var row = AddRow(_detail!);
            // Role-tinted marker chip: whose-role-wears-this at a glance (U12 pinned).
            row.AddChild(new ColorRect
            {
                Color = roleColor,
                CustomMinimumSize = new Vector2(10, 10),
                MouseFilter = MouseFilterEnum.Ignore,
            });

            if (itemId is not { } id || !state.Items.TryGetValue(id.Value, out var item))
            {
                AddIcon(row, IconRegistry.Slot(slot));
                AddLabel(row, $"  {slot}: —");
                continue;
            }

            row.AddChild(ArtRect(
                AssetCatalog.ItemIconId(item.RecipeId), new Vector2(GearArtSize, GearArtSize),
                // Caption restored (item.Name): on a manifest MISS this is the ONLY place the
                // placeholder's caption comes from — dropping it would show the raw asset key
                // instead of the item name. On a HIT it also renders under the icon now,
                // alongside the fuller infoCol line below — redundant, never wrong.
                IconRegistry.Slot(slot), item.Name));

            var (kills, saves) = LedgerQuery.MarkTally(state, id);
            var mark = item.Mark is null ? "no mark" : $"mark of {item.Mark.CrafterName}: {kills} kills, {saves} saves";
            var infoCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(GearInfoColumnMinWidth, 0),
            };
            row.AddChild(infoCol);
            AddLabel(infoCol, $"  {slot}: {item.Name} [{item.Quality}] — {mark}");
            var chipRow = AddRow(infoCol);
            chipRow.AddChild(StatChip("Atk", $"{item.Stats.Attack}"));
            chipRow.AddChild(StatChip("Def", $"{item.Stats.Defense}"));
        }

        AddLabel(_detail!, "ITEM MEMORIES:");
        if (hero.Memories.IsEmpty)
        {
            AddLabel(_detail!, "  (none yet)");
        }

        foreach (var memory in hero.Memories)
        {
            AddLabel(_detail!, $"  {ItemName(memory.Item)}: {memory.Kills} kills, {memory.Saves} saves");
        }
    }

    /// <summary>One roster card: a content-honest <see cref="PanelContainer"/> (R7-class fix,
    /// U4 — its predecessor, a bare <see cref="Button"/>, is not a <see cref="Container"/>, so its
    /// VBox children never sized or clipped it; 3 <see cref="UiKit.StatChipCompact"/> chips need
    /// more room than a fixed 140px card gave them) wrapping a class-tinted portrait, the hero's
    /// name (ellipsized, never mid-word-wrapped), and either level/gold/deepest chips (alive) or a
    /// DIED line — plus a transparent, borderless <see cref="Button"/> stacked on top by
    /// <see cref="PanelContainer"/>'s every-child-fills-the-content-rect layout, so a click still
    /// drives the same <see cref="RenderDetail"/> path <see cref="SelectHero"/> does and selection
    /// state stays provable via <see cref="Button.ButtonPressed"/>.</summary>
    private (Control Frame, Button Overlay) BuildHeroCard(Hero hero)
    {
        var frame = new PanelContainer
        {
            Name = $"HeroCardFrame_{hero.Id.Value}",
            CustomMinimumSize = RosterCardSize,
        };

        var body = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        frame.AddChild(body);

        var portrait = PortraitFrame(
            AssetCatalog.HeroPortraitId(hero.ClassId), UiKit.PortraitSize, IconRegistry.Sprite(hero.ClassId),
            hero.Name, ellipsizeCaption: true);
        TintPortraitFrame(portrait, HeroSprite.RoleColor(hero.ClassId));
        body.AddChild(portrait);

        if (hero.Alive)
        {
            var chipRow = AddRow(body);
            chipRow.Alignment = BoxContainer.AlignmentMode.Center;
            chipRow.AddChild(UiKit.StatChipCompact("Lv", $"{hero.Level}"));
            chipRow.AddChild(UiKit.StatChipCompact("Gold", $"{hero.Gold}g", UiKit.ChipTone.Accent));
            chipRow.AddChild(UiKit.StatChipCompact("Deepest", $"{hero.DeepestFloorReached}"));
        }
        else
        {
            var died = AddLabel(body, $"DIED day {hero.DiedOnDay}");
            died.HorizontalAlignment = HorizontalAlignment.Center;
            died.AddThemeColorOverride("font_color", GameTheme.BloodColor);
        }

        // Decoration only: every descendant must pass mouse input through so the click always
        // resolves to the overlay Button, never a nested PanelContainer (portrait/chips default
        // to Stop) swallowing it first (mirrors SimPanel.AddIcon/HeroSprite's sprite+marker
        // convention, generalized recursively).
        MakeDecorative(body);

        // Transparent overlay: PanelContainer stacks every direct child to fill the same content
        // rect, and a later-added sibling receives input first, so this Button — added after
        // `body` — sits visually on top and alone answers the click without drawing its own
        // themed background/border over the card's real content.
        var overlay = new Button
        {
            Name = $"HeroCard_{hero.Id.Value}",
            ToggleMode = true,
            Flat = true,
        };
        overlay.Pressed += () => RenderDetail(hero.Id.Value);
        frame.AddChild(overlay);

        return (frame, overlay);
    }

    /// <summary>Tint the portrait's frame/underlay only via <see cref="CanvasItem.SelfModulate"/>
    /// (which colors a node's own drawn stylebox but does not cascade to children, unlike
    /// <see cref="CanvasItem.Modulate"/>) — a painted portrait's own texture keeps its default
    /// white <see cref="CanvasItem.Modulate"/> untouched, so generated art is never discolored.
    /// A glyph fallback icon (no generated art for this class) still gets the role tint, since
    /// it is a placeholder, not painted art.</summary>
    private static void TintPortraitFrame(Control frame, Color tint)
    {
        if (frame is CanvasItem item)
        {
            item.SelfModulate = tint;
        }

        var fallbackIcon = frame.FindChildren("FallbackIcon", nameof(TextureRect), recursive: true, owned: false)
            .Cast<TextureRect>()
            .FirstOrDefault();
        if (fallbackIcon is not null)
        {
            fallbackIcon.Modulate = tint;
        }
    }

    /// <summary>Recursively set every descendant Control to ignore mouse input, so a rich themed
    /// subtree can sit inside a clickable <see cref="Button"/> without any nested
    /// <c>PanelContainer</c>/<c>Container</c> (default filter Stop) swallowing the click.</summary>
    private static void MakeDecorative(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = MouseFilterEnum.Ignore;
        }

        foreach (var child in node.GetChildren())
        {
            MakeDecorative(child);
        }
    }

    private void EnsureBuilt()
    {
        if (_rosterGrid is not null)
        {
            return;
        }

        var split = new HBoxContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(split);

        // Roster: a vertically-scrolling grid of portrait cards (horizontal scroll disabled,
        // U7/R7 convention — the grid follows the column width instead of collapsing).
        var rosterScroll = new ScrollContainer
        {
            Name = "RosterScroll",
            CustomMinimumSize = new Vector2(RosterColumnWidth, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        split.AddChild(rosterScroll);

        _rosterGrid = new GridContainer
        {
            Name = "RosterGrid",
            Columns = RosterColumns,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        rosterScroll.AddChild(_rosterGrid);

        // Horizontal scroll disabled (U7/R7): the detail column follows the pane width so
        // autowrap labels wrap on real width instead of collapsing to 1 char per line.
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        split.AddChild(scroll);
        _detail = new VBoxContainer
        {
            Name = "HeroDetail",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_detail);
    }
}
