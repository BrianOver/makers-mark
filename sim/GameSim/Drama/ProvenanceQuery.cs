using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>The four honest channels an item can reach a hero through (link 2), plus the
/// pinned-counter case: a <see cref="CounterSaleClosed"/> sale where the player named the price
/// and the hero paid it without a back-and-forth (<see cref="CounterSaleClosed.Pinned"/>).</summary>
public enum ItemChannel
{
    Shelf,
    Counter,
    CounterPinned,
    Commission,
    Runner,
}

/// <summary>How an item reached the hand that held it (link 2), and when — the most recent
/// qualifying event naming the item (P2-MEMORY-03).</summary>
public sealed record ItemChannelInfo(ItemChannel Channel, int Day, HeroId Hero);

/// <summary>
/// Pure read model over <see cref="GameState.EventLog"/> (mirrors <see cref="LedgerQuery"/>/<see
/// cref="DemandBoard"/>): no state changes, no RNG draw, no wall clock, callable any number of
/// times by the Evening Ledger's beat rows and the item's provenance card. Today the attribution
/// beat says a craft mattered but not how it got there — this closes that gap without a new event
/// or a Contracts edit: <see cref="CounterSaleClosed"/> already carries hero/item/price/Pinned,
/// <see cref="ItemSold"/> already flags <c>FromPlayerShop</c>, and <see cref="CommissionFulfilled"/>/
/// <see cref="SupplyDelivered"/> already name the item.
/// </summary>
public static class ProvenanceQuery
{
    /// <summary>
    /// The channel that delivered <paramref name="item"/> to a hero, or null when no qualifying
    /// event exists yet (an auto-crafted/rival item that was never sold through the player's own
    /// four channels, or one still sitting unshelved) — an honest empty state; callers render
    /// nothing for a null result, never a generic fallback line. The log is stamped in
    /// nondecreasing <see cref="GameEvent.Day"/> order (<see cref="DayLog"/>'s own invariant), so
    /// a single forward walk that keeps overwriting on every match lands on the most recent one.
    /// </summary>
    public static ItemChannelInfo? Channel(GameState state, ItemId item)
    {
        ItemChannelInfo? found = null;
        foreach (var gameEvent in state.EventLog)
        {
            switch (gameEvent)
            {
                case CounterSaleClosed sale when sale.Item == item:
                    found = new ItemChannelInfo(
                        sale.Pinned ? ItemChannel.CounterPinned : ItemChannel.Counter, gameEvent.Day, sale.Hero);
                    break;
                case ItemSold sold when sold.Item == item && sold.FromPlayerShop:
                    found = new ItemChannelInfo(ItemChannel.Shelf, gameEvent.Day, sold.Buyer);
                    break;
                case CommissionFulfilled commission when commission.Item == item:
                    found = new ItemChannelInfo(ItemChannel.Commission, gameEvent.Day, commission.Hero);
                    break;
                case SupplyDelivered supply when supply.Item == item:
                    found = new ItemChannelInfo(ItemChannel.Runner, gameEvent.Day, supply.To);
                    break;
            }
        }

        return found;
    }

    /// <summary>
    /// The channel clause — the sentence's second line. <paramref name="asOf"/> is the day the
    /// gap is measured against: the Evening Ledger anchors it to the night being retold (a fixed
    /// historical fact that reads the same however much later the card is reopened), while the
    /// provenance card anchors it to the live day (a browsed-now view). Empty string when
    /// <paramref name="info"/> is null — the honest-empty-state contract; render nothing, not a
    /// fallback.
    /// </summary>
    public static string Clause(ItemChannelInfo? info, int asOf)
    {
        if (info is null)
        {
            return string.Empty;
        }

        var ago = DaysAgo(asOf - info.Day);
        return info.Channel switch
        {
            ItemChannel.Shelf => $"It left your shelf {ago} — you never met over it.",
            ItemChannel.Counter => $"Haggled off your counter {ago}.",
            ItemChannel.CounterPinned => $"You named the fair price at the counter, and it was paid — {ago}.",
            ItemChannel.Commission => $"Commissioned, and delivered {ago}.",
            ItemChannel.Runner => "You put it in the runner's hands yourself, at the vigil.",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The heirloom clause, reading the fact the forge already stamped (<see
    /// cref="Item.HeirloomLineage"/>: "forged from the {item} of {fallen hero}") as a sentence —
    /// or null for ordinary stock. No re-derivation: the lineage string is the recorded fact, not
    /// a template filled in twice.
    /// </summary>
    public static string? HeirloomClause(Item item) =>
        item.HeirloomLineage is { Length: > 0 } lineage ? Capitalize(lineage) + "." : null;

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string DaysAgo(int gap) => gap switch
    {
        <= 0 => "today",
        1 => "a day ago",
        _ => $"{gap} days ago",
    };
}
