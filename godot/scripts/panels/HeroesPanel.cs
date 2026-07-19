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
/// </summary>
public partial class HeroesPanel : SimPanel
{
    /// <summary>Roster grid column count — two wide fits the panel's left column comfortably.</summary>
    private const int RosterColumns = 2;

    /// <summary>Roster column width (px) — two <see cref="RosterCardSize"/> cards plus gutters.</summary>
    private const float RosterColumnWidth = 300f;

    /// <summary>One roster card's footprint (px): portrait + name + a chip row/DIED line.</summary>
    private static readonly Vector2 RosterCardSize = new(140f, 190f);

    /// <summary>Gear-row item-art tile edge length (px).</summary>
    private const float GearArtSize = 48f;

    private GridContainer? _rosterGrid;
    private VBoxContainer? _detail;
    private readonly Dictionary<int, Button> _heroCards = [];
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
        _heroCards.Clear();

        foreach (var hero in state.Heroes.Values)
        {
            var card = BuildHeroCard(hero);
            _rosterGrid!.AddChild(card);
            _heroCards[hero.Id.Value] = card;
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
        foreach (var (id, card) in _heroCards)
        {
            // NoSignal: this is a programmatic sync, never a real click — using the plain
            // setter would re-emit `toggled`/risk re-entrancy through the card's own
            // Pressed handler.
            card.SetPressedNoSignal(id == heroValue);
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
                IconRegistry.Slot(slot), item.Name));

            var (kills, saves) = LedgerQuery.MarkTally(state, id);
            var mark = item.Mark is null ? "no mark" : $"mark of {item.Mark.CrafterName}: {kills} kills, {saves} saves";
            var infoCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
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

    /// <summary>One roster card: a themed toggle <see cref="Button"/> (so a click drives the same
    /// <see cref="RenderDetail"/> path as <see cref="SelectHero"/>, and selection state is provable
    /// via <see cref="Button.ButtonPressed"/>) wrapping a class-tinted portrait, the hero's name,
    /// and either level/gold/deepest chips (alive) or a DIED line.</summary>
    private Button BuildHeroCard(Hero hero)
    {
        var card = new Button
        {
            Name = $"HeroCard_{hero.Id.Value}",
            ToggleMode = true,
            ClipText = true,
            CustomMinimumSize = RosterCardSize,
        };
        card.Pressed += () => RenderDetail(hero.Id.Value);

        var body = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        body.SetAnchorsPreset(LayoutPreset.FullRect);
        card.AddChild(body);

        var frame = PortraitFrame(
            AssetCatalog.HeroPortraitId(hero.ClassId), UiKit.PortraitSize, IconRegistry.Sprite(hero.ClassId), hero.Name);
        TintPortraitIcon(frame, HeroSprite.RoleColor(hero.ClassId));
        body.AddChild(frame);

        var nameLabel = AddLabel(body, hero.Name);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;

        if (hero.Alive)
        {
            var chipRow = AddRow(body);
            chipRow.Alignment = BoxContainer.AlignmentMode.Center;
            chipRow.AddChild(StatChip("Lv", $"{hero.Level}"));
            chipRow.AddChild(StatChip("Gold", $"{hero.Gold}g", UiKit.ChipTone.Accent));
            chipRow.AddChild(StatChip("Deepest", $"{hero.DeepestFloorReached}"));
        }
        else
        {
            var died = AddLabel(body, $"DIED day {hero.DiedOnDay}");
            died.HorizontalAlignment = HorizontalAlignment.Center;
            died.AddThemeColorOverride("font_color", GameTheme.BloodColor);
        }

        // Decoration only: every descendant must pass mouse input through so the click always
        // resolves to the card Button itself (mirrors SimPanel.AddIcon/HeroSprite's sprite+marker
        // convention, generalized recursively — PortraitFrame/StatChip nest PanelContainers that
        // default to Stop and would otherwise swallow the click before it reaches the Button).
        MakeDecorative(body);
        return card;
    }

    /// <summary>Tint the portrait's own icon layer (hit texture or fallback glyph) to
    /// <paramref name="tint"/> — never the card's text, so name/chip legibility is unaffected.
    /// Mirrors <see cref="HeroSprite"/>'s own neutral-body-tinted-via-Modulate convention.</summary>
    private static void TintPortraitIcon(Control frame, Color tint)
    {
        var icon = frame.FindChildren("*", nameof(TextureRect), recursive: true, owned: false)
            .Cast<TextureRect>()
            .FirstOrDefault();
        if (icon is not null)
        {
            icon.Modulate = tint;
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
