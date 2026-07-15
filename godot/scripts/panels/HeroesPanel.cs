using System.Collections.Generic;
using GameSim.Contracts;
using GameSim.Drama;
using Godot;
using GodotClient.Town;

namespace GodotClient.Panels;

/// <summary>
/// Hero roster + detail (R12 display half): the roster list
/// (name/role/level/gold/deepest/alive) and a detail pane showing worn gear with
/// item names and lifetime maker's-mark tallies (<see cref="LedgerQuery.MarkTally"/>)
/// plus the hero's item memories. Read-only — heroes are autonomous (A2).
/// </summary>
public partial class HeroesPanel : SimPanel
{
    private ItemList? _roster;
    private VBoxContainer? _detail;
    private readonly List<int> _rosterHeroIds = [];
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
        _roster!.Clear();
        _rosterHeroIds.Clear();
        foreach (var hero in state.Heroes.Values)
        {
            var status = hero.Alive
                ? $"L{hero.Level} {hero.Gold}g deepest {hero.DeepestFloorReached}"
                : $"DIED day {hero.DiedOnDay}";
            _roster.AddItem($"{hero.Id} {hero.Name} ({hero.Role}) — {status}");
            _rosterHeroIds.Add(hero.Id.Value);
        }

        if (_rosterHeroIds.Count == 0)
        {
            Clear(_detail!);
            AddLabel(_detail!, "no heroes in town");
            return;
        }

        var index = _rosterHeroIds.IndexOf(_selectedHeroId);
        if (index < 0)
        {
            index = 0;
        }

        _roster.Select(index);
        RenderDetail(_rosterHeroIds[index]);
    }

    /// <summary>
    /// Bind a specific hero into the detail pane (U12 town click routing, R20).
    /// Selects the roster row when the hero is listed and renders their detail.
    /// </summary>
    public void SelectHero(int heroValue)
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var index = _rosterHeroIds.IndexOf(heroValue);
        if (index >= 0)
        {
            _roster!.Select(index);
        }

        RenderDetail(heroValue);
    }

    private void RenderDetail(int heroValue)
    {
        _selectedHeroId = heroValue;
        var state = Adapter!.CurrentState;
        Clear(_detail!);
        if (!state.Heroes.TryGetValue(heroValue, out var hero))
        {
            return;
        }

        AddHeader(_detail!, $"{hero.Name} — {hero.Role}");
        AddLabel(_detail!, hero.Alive
            ? $"Level {hero.Level} | HP {hero.MaxHp} | {hero.Gold}g | deepest floor {hero.DeepestFloorReached}"
            : $"DIED day {hero.DiedOnDay} on floor record {hero.DeepestFloorReached}");

        AddLabel(_detail!, "GEAR:");
        var roleColor = HeroSprite.RoleColor(hero.Role);
        foreach (var (slot, itemId) in new (ItemSlot, ItemId?)[]
                 {
                     (ItemSlot.Weapon, hero.Gear.Weapon),
                     (ItemSlot.Shield, hero.Gear.Shield),
                     (ItemSlot.Armor, hero.Gear.Armor),
                 })
        {
            var row = AddRow(_detail!);
            AddIcon(row, IconRegistry.Slot(slot));
            // Role-tinted marker chip: whose-role-wears-this at a glance.
            row.AddChild(new ColorRect
            {
                Color = roleColor,
                CustomMinimumSize = new Vector2(10, 10),
                MouseFilter = MouseFilterEnum.Ignore,
            });

            if (itemId is not { } id || !state.Items.TryGetValue(id.Value, out var item))
            {
                AddLabel(row, $"  {slot}: —");
                continue;
            }

            var (kills, saves) = LedgerQuery.MarkTally(state, id);
            var mark = item.Mark is null ? "no mark" : $"mark of {item.Mark.CrafterName}: {kills} kills, {saves} saves";
            AddLabel(row, $"  {slot}: {item.Name} [{item.Quality}] atk {item.Stats.Attack} def {item.Stats.Defense} — {mark}");
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

    private void EnsureBuilt()
    {
        if (_roster is not null)
        {
            return;
        }

        var split = new HBoxContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(split);

        _roster = new ItemList
        {
            Name = "Roster",
            CustomMinimumSize = new Vector2(320, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _roster.ItemSelected += index => RenderDetail(_rosterHeroIds[(int)index]);
        split.AddChild(_roster);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
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
