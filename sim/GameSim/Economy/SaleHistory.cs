using System.Collections.Generic;
using GameSim.Contracts;

namespace GameSim.Economy;

/// <summary>
/// P2-HONEST-34 ("a sold piece does not come back for free"): has a hero ever paid for this item?
/// Read from the recorded log — <see cref="ItemSold"/> (shelf sales and shelf-filled commissions,
/// both stamped by the sim) and <see cref="CounterSaleClosed"/> (the stepped counter, which closes
/// its own sale event) — never from new state. Before this unit rule 3b of
/// <see cref="ShopHandlers"/> closed only consumables; a gear piece a hero bought and later dropped for
/// an upgrade counted as an "unshelved craft" again and sold a second time at full price, unrecorded
/// (41% of baseline shelf revenue rode on it). Pure; no RNG, no clock.
/// </summary>
public static class SaleHistory
{
    /// <summary>True when any recorded sale names <paramref name="item"/>.</summary>
    public static bool EverSold(GameState state, ItemId item)
    {
        foreach (var gameEvent in state.EventLog)
        {
            switch (gameEvent)
            {
                case ItemSold sold when sold.Item == item:
                case CounterSaleClosed closed when closed.Item == item:
                    return true;
            }
        }

        return false;
    }

    /// <summary>Every item id a recorded sale names — the set form for the harness stock filters,
    /// which walk the whole item table once a Morning.</summary>
    public static HashSet<int> SoldItemIds(GameState state)
    {
        var sold = new HashSet<int>();
        foreach (var gameEvent in state.EventLog)
        {
            switch (gameEvent)
            {
                case ItemSold s:
                    sold.Add(s.Item.Value);
                    break;
                case CounterSaleClosed c:
                    sold.Add(c.Item.Value);
                    break;
            }
        }

        return sold;
    }
}
