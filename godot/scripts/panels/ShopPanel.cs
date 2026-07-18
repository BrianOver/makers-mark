using System.Collections.Generic;
using System.Linq;
using GameSim.Contracts;
using Godot;

namespace GodotClient.Panels;

/// <summary>
/// The storefront (R16 display + R8/AE4 render half): the player shelf with
/// reprice/unstock controls and the day's <see cref="HeroPassedOnItem"/> reasons
/// rendered under each shelved item; unshelved player crafts with stock+price
/// controls; the rival shelf read-only. The unshelved filter mirrors (never
/// replaces) the sim's own StockAction validation — an invalid stock is still
/// the kernel's rejection to make.
/// </summary>
public partial class ShopPanel : SimPanel
{
    private Label? _feedback;
    private VBoxContainer? _content;

    public override void _Ready() => EnsureBuilt();

    public override void Refresh()
    {
        EnsureBuilt();
        if (Adapter is null)
        {
            return;
        }

        var state = Adapter.CurrentState;
        Clear(_content!);

        // The day's pass-reasons, grouped per item (R8/AE4 — the legible half).
        var passesToday = new Dictionary<int, List<HeroPassedOnItem>>();
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent is HeroPassedOnItem pass && gameEvent.Day == state.Day)
            {
                if (!passesToday.TryGetValue(pass.Item.Value, out var list))
                {
                    passesToday[pass.Item.Value] = list = [];
                }

                list.Add(pass);
            }
        }

        AddHeader(_content!, "YOUR SHELF");
        if (state.Player.Shelf.IsEmpty)
        {
            AddLabel(_content!, "  (empty — craft at the forge, then stock it here)");
        }

        foreach (var entry in state.Player.Shelf)
        {
            var item = state.Items[entry.Item.Value];
            var row = AddRow(_content!);
            AddIcon(row, IconRegistry.Slot(item.Slot));
            AddLabel(row, $"{entry.Item} {item.Name} [{item.Quality}] — {entry.Price}g");
            var priceSpin = AddSpinBox(row, $"Price_{entry.Item.Value}", 1, 99999, entry.Price);
            var itemId = entry.Item;
            AddButton(row, $"Reprice_{entry.Item.Value}", "Reprice", () =>
            {
                Adapter!.Queue(new SetPriceAction(itemId, (int)priceSpin.Value));
                _feedback!.Text = $"queued: reprice {itemId} to {(int)priceSpin.Value}g";
            });
            AddButton(row, $"Unstock_{entry.Item.Value}", "Unstock", () =>
            {
                Adapter!.Queue(new UnstockAction(itemId));
                _feedback!.Text = $"queued: unstock {itemId}";
            });

            if (passesToday.TryGetValue(entry.Item.Value, out var passes))
            {
                foreach (var pass in passes)
                {
                    var reason = AddLabel(_content!, $"    {HeroName(pass.Hero)} passed: {pass.Reason}");
                    reason.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.4f));
                }
            }
        }

        AddHeader(_content!, "UNSHELVED CRAFTS");
        var unshelved = UnshelvedPlayerCrafts(state).ToList();
        if (unshelved.Count == 0)
        {
            AddLabel(_content!, "  (none)");
        }

        foreach (var item in unshelved)
        {
            var row = AddRow(_content!);
            AddIcon(row, IconRegistry.Slot(item.Slot));
            AddLabel(row, $"{item.Id} {item.Name} [{item.Quality}] atk {item.Stats.Attack} def {item.Stats.Defense}");
            var priceSpin = AddSpinBox(row, $"StockPrice_{item.Id.Value}", 1, 99999, 10);
            var itemId = item.Id;
            var stock = AddButton(row, $"Stock_{item.Id.Value}", "Stock", () =>
            {
                Adapter!.Queue(new StockAction(itemId, (int)priceSpin.Value));
                _feedback!.Text = $"queued: stock {itemId} at {(int)priceSpin.Value}g";
            });
            // U6 gate, mirroring ShopHandlers.ApplyStock check 3b (the only refusal this
            // list can still reach: existence/provenance/equipped are already filtered by
            // UnshelvedPlayerCrafts, and the SpinBox floor of 1 keeps prices positive): a
            // consumable that has ever been sold never returns to the shelf.
            var soldConsumable = item.Effect is not null
                && state.EventLog.Any(e => e is ItemSold sold && sold.Item == itemId);
            GateButton(stock, !soldConsumable, "Sold consumables don't come back.");
        }

        AddHeader(_content!, "RIVAL SHELF (read-only)");
        if (state.RivalShelf.IsEmpty)
        {
            AddLabel(_content!, "  (empty)");
        }

        foreach (var entry in state.RivalShelf)
        {
            var item = state.Items[entry.Item.Value];
            var row = AddRow(_content!);
            AddIcon(row, IconRegistry.Slot(item.Slot));
            AddLabel(row, $"  {entry.Item} {item.Name} [{item.Quality}] — {entry.Price}g");
        }
    }

    /// <summary>Player crafts that could go on the shelf: marked, not shelved, not on a hero's back.</summary>
    private static IEnumerable<Item> UnshelvedPlayerCrafts(GameState state)
    {
        var shelved = state.Player.Shelf.Select(e => e.Item.Value).ToHashSet();
        var equipped = new HashSet<int>();
        foreach (var hero in state.Heroes.Values)
        {
            foreach (var slot in new[] { hero.Gear.Weapon, hero.Gear.Shield, hero.Gear.Armor })
            {
                if (slot is { } id)
                {
                    equipped.Add(id.Value);
                }
            }
        }

        return state.Items.Values.Where(i =>
            i.PlayerCrafted && !shelved.Contains(i.Id.Value) && !equipped.Contains(i.Id.Value));
    }

    private void EnsureBuilt()
    {
        if (_content is not null)
        {
            return;
        }

        var body = BuildScrollBody();
        _feedback = AddLabel(body, string.Empty);
        _feedback.Name = "ShopFeedback";
        _content = new VBoxContainer { Name = "ShopContent" };
        body.AddChild(_content);
    }
}
