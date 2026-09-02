using GameSim.Contracts;
using GameSim.Venues;

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

    /// <summary>The floor gold decided (P2-MEMORY-17): present only when an accepted bounty is the
    /// reason an expedition departed for <see cref="Floor"/> rather than the ordinary depth-based
    /// default it would otherwise have earned.</summary>
    public sealed record PresenceInfo(int Floor);

    /// <summary>
    /// Whether the bounty board — not the party's own earned depth — decided where <paramref
    /// name="hero"/>'s expedition on <paramref name="day"/> went. The difference between two
    /// logged numbers, never an inference from <c>BountyPosted</c>/<c>BountyJudged</c>: a bounty
    /// can be posted, even accepted, and still leave the floor unchanged (coincidence with the
    /// natural default) — only a floor that actually moved counts.
    ///
    /// Number one: <see cref="PartyDeparted.TargetFloor"/> — the floor the party actually departed
    /// for, the last thing <c>ExpeditionSystem.Process</c> logs per party. Number two: the
    /// depth-based default <c>ExpeditionSystem.TargetFloorFor</c> (its non-bounty arm) would have
    /// picked — the same one-line formula (<c>clamp(deepest + 1, 1, venue.FloorCount)</c>, mirrored
    /// here rather than reaching into Expedition/ for it, exactly as
    /// <c>MusterSystem.StampTargetFloorDecision</c> already does for its own ticker line) fed each
    /// roster member's deepest floor AS IT STOOD BEFORE THAT DAY.
    ///
    /// "Before that day" is load-bearing and is why this never reads a hero's live
    /// <c>DeepestFloorReached</c>: by the time anything queries current state, the Evening reveal
    /// for THIS SAME expedition has typically already bumped it (<c>ExpeditionRevealSystem</c>'s
    /// depth-records step) — comparing the actual floor against a default inflated by the very trip
    /// being explained would hide exactly the bounty-pushed-them-deeper case this exists to catch.
    /// Each member's prior depth is rebuilt instead from <see cref="FloorRecordSet"/> — one
    /// already-logged event per personal-best floor, strictly before <paramref name="day"/> —
    /// another read over already-recorded facts, never a snapshot invented for this query.
    ///
    /// The venue's floor count for the clamp is read from that day's own <see cref="PartiesFormed"/>
    /// plan (Morning, before any hero mutation) rather than assumed, because a bounty-free party can
    /// route to a shallower venue than the Mine (the Gloomwood's 4 floors) and get capped there —
    /// assuming the Mine's 5 would manufacture a false difference on an ordinary non-bounty night.
    /// Falls back to the Mine when no matching plan is logged (an older/synthetic log): a
    /// bounty-driven departure is always Mine-routed by construction, so the fallback only ever
    /// widens the venue this query assumes — which can only make it MORE conservative (silence,
    /// never a false claim), never less.
    /// </summary>
    public static PresenceInfo? Presence(GameState state, HeroId hero, int day)
    {
        PartyDeparted? departure = null;
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent.Day == day && gameEvent is PartyDeparted pd && pd.Party.Contains(hero))
            {
                departure = pd;
            }
        }

        if (departure is null)
        {
            return null; // no logged departure this day for this hero — honest silence
        }

        var venueId = VenueRegistry.MineId;
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent.Day == day && gameEvent is PartiesFormed formed)
            {
                var plan = formed.Parties.FirstOrDefault(p => p.Roster.Contains(hero));
                if (plan is not null)
                {
                    venueId = plan.VenueId;
                    break;
                }
            }
        }

        var deepestBefore = 0;
        foreach (var member in departure.Party)
        {
            var best = 0;
            foreach (var gameEvent in state.EventLog)
            {
                if (gameEvent.Day < day && gameEvent is FloorRecordSet record && record.Hero == member)
                {
                    best = Math.Max(best, record.Floor);
                }
            }

            deepestBefore = Math.Max(deepestBefore, best);
        }

        var defaultFloor = Math.Clamp(deepestBefore + 1, 1, VenueRegistry.Require(venueId).FloorCount);
        return departure.TargetFloor == defaultFloor ? null : new PresenceInfo(departure.TargetFloor);
    }

    /// <summary>
    /// <see cref="Presence"/> for whichever hero and day the item's own most recent <see
    /// cref="AttributionBeatEvent"/> names — the provenance card's anchor, since (unlike the
    /// Evening Ledger's beat row) it has no beat of its own handed to it. Null when the item never
    /// earned a beat.
    /// </summary>
    public static PresenceInfo? ItemPresence(GameState state, ItemId item)
    {
        AttributionBeatEvent? found = null;
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent is AttributionBeatEvent beat && beat.Item == item)
            {
                found = beat;
            }
        }

        return found is null ? null : Presence(state, found.Hero, found.Day);
    }

    /// <summary>
    /// The presence clause — the sentence naming gold, not depth earned, as the reason the fight
    /// happened where it did. Empty string when <paramref name="presence"/> is null: the same
    /// honest-empty-state contract this file already keeps for <see cref="Clause"/> — no bounty
    /// moved the floor renders as nothing, never a generic line. Pronoun-free, matching P2-MEMORY-03
    /// (no gender field exists on <c>Hero</c> to draw one from): the floor is named, not the hero.
    /// </summary>
    public static string PresenceClause(PresenceInfo? presence) =>
        presence is null ? string.Empty : $"Floor {presence.Floor} — your gold said floor {presence.Floor}.";

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string DaysAgo(int gap) => gap switch
    {
        <= 0 => "today",
        1 => "a day ago",
        _ => $"{gap} days ago",
    };
}
