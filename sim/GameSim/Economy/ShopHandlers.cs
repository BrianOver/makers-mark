using GameSim.Contracts;

namespace GameSim.Economy;

/// <summary>
/// Player storefront management (R16): <see cref="StockAction"/>,
/// <see cref="SetPriceAction"/>, <see cref="UnstockAction"/> — each with typed
/// rejections, never a silent drop.
///
/// Phase legality: ALL THREE phases. The plan's crafting window is the Expedition
/// phase and the forge never closes (see CraftingHandlers); the shop counter follows
/// the same rule — craft-then-shelve during Expedition is the intended play pattern,
/// and repricing at Evening (after reading the Ledger) is the R16 feedback lever.
/// Heroes only BUY during Morning (HeroShoppingSystem), so phase timing of shelf
/// management never races a sale within a tick: the kernel applies actions before
/// systems.
///
/// No events: stocking, repricing, and unstocking are not sales — the only shop event
/// is <see cref="ItemSold"/>, emitted by the shopping system at purchase time.
/// Determinism: no RNG draws anywhere in these handlers; every rejection happens
/// before any state change.
/// </summary>
public sealed class ShopHandlers : IActionHandler
{
    public bool CanHandle(PlayerAction action, DayPhase phase) =>
        action is StockAction or SetPriceAction or UnstockAction; // all phases legal

    public (GameState State, RejectedAction? Rejected) Apply(
        GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events) =>
        action switch
        {
            StockAction stock => ApplyStock(state, stock),
            SetPriceAction setPrice => ApplySetPrice(state, setPrice),
            UnstockAction unstock => ApplyUnstock(state, unstock),
            _ => (state, new RejectedAction(action, $"ShopHandlers cannot apply {action.GetType().Name}.")),
        };

    /// <summary>
    /// Check order is fixed (existence, provenance, bearer, duplication, price) so
    /// rejection reasons are stable across runs.
    /// </summary>
    private static (GameState, RejectedAction?) ApplyStock(GameState state, StockAction action)
    {
        // 1. The item must exist in the world.
        if (!state.Items.TryGetValue(action.Item.Value, out var item))
        {
            return (state, new RejectedAction(action, $"No such item {action.Item}."));
        }

        // 2. Only the player's own craft goes on the player shelf (rival goods and
        //    anything else unmarked are not the player's to sell).
        if (!item.PlayerCrafted)
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is not player-crafted — only marked craft can be shelved."));
        }

        // 3. An item on a hero's back is not in the player's hands. Checked against
        //    every hero, alive or dead — dead heroes keep their worn gear (R13).
        foreach (var hero in state.Heroes.Values)
        {
            if (hero.Gear.Weapon == action.Item || hero.Gear.Shield == action.Item || hero.Gear.Armor == action.Item)
            {
                return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is equipped by {hero.Name} — it cannot be shelved."));
            }
        }

        // 4. One shelf slot per item.
        if (state.Player.Shelf.Any(e => e.Item == action.Item))
        {
            return (state, new RejectedAction(action, $"{item.Name} ({action.Item}) is already on the shelf."));
        }

        // 5. Price must be positive.
        if (action.Price <= 0)
        {
            return (state, new RejectedAction(action, $"Price must be positive; got {action.Price}."));
        }

        var newState = state with
        {
            Player = state.Player with
            {
                Shelf = state.Player.Shelf.Add(new ShelfEntry(action.Item, action.Price)),
            },
        };
        return (newState, null);
    }

    private static (GameState, RejectedAction?) ApplySetPrice(GameState state, SetPriceAction action)
    {
        var index = state.Player.Shelf.FindIndex(e => e.Item == action.Item);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"Item {action.Item} is not on the shelf."));
        }

        if (action.Price <= 0)
        {
            return (state, new RejectedAction(action, $"Price must be positive; got {action.Price}."));
        }

        var newState = state with
        {
            Player = state.Player with
            {
                Shelf = state.Player.Shelf.SetItem(index, new ShelfEntry(action.Item, action.Price)),
            },
        };
        return (newState, null);
    }

    private static (GameState, RejectedAction?) ApplyUnstock(GameState state, UnstockAction action)
    {
        var index = state.Player.Shelf.FindIndex(e => e.Item == action.Item);
        if (index < 0)
        {
            return (state, new RejectedAction(action, $"Item {action.Item} is not on the shelf."));
        }

        // The shelf entry goes; the item itself stays in GameState.Items — its
        // maker's-mark history is permanent (R5).
        var newState = state with
        {
            Player = state.Player with
            {
                Shelf = state.Player.Shelf.RemoveAt(index),
            },
        };
        return (newState, null);
    }
}
