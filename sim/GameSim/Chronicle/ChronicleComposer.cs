using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GameSim.Chronicle;

/// <summary>
/// P2-MEMORY-13: link 5 of the game's chain, said about the PLAYER rather than a hero — the
/// campaign's closing spread. Fifteen predicates, each a fact the event log can prove with plain
/// integer arithmetic; <see cref="Compose"/> renders the three most specific that fired, then the
/// one fixed closer that is never scored. Composition only: reads <see cref="GameState"/>, never
/// writes it, draws no RNG, reads no clock, touches no transcendental <c>Math.*</c> (KTD2 /
/// determinism) — same state in, same lines out, always.
/// </summary>
public static class ChronicleComposer
{
    /// <summary>
    /// Rank of a fired predicate's own text: hero+number beats number-only beats neither (the
    /// plan's own ordering). No predicate here names a hero without a number, so that fourth
    /// combination is unused rather than ambiguous.
    /// </summary>
    public enum Specificity
    {
        Neither = 0,
        NumberOnly = 1,
        HeroAndNumber = 2,
    }

    /// <summary>One predicate: renders its own line, or null when <paramref name="state"/> does not satisfy it.</summary>
    public delegate string? Predicate(GameState state);

    /// <summary>
    /// One registry entry. <see cref="Order"/> is this entry's index in <see cref="Registry"/> and
    /// IS the tie-break: when two fired predicates share a <see cref="Specificity"/>, the one
    /// declared earlier wins — a plain int, never a hash of <see cref="Id"/>, never a dictionary's
    /// own enumeration order (both would let a re-run of the same save print a different three).
    /// </summary>
    public sealed record Entry(string Id, Specificity Specificity, Predicate Evaluate, int Order);

    /// <summary>Never scored, never one of the printed three, always the final line — even when
    /// nothing else in the registry fired, the book still closes.</summary>
    public const string Closer =
        "You never went down. Everything in this book happened anyway, because of you.";

    private static readonly (string Id, Specificity Specificity, Predicate Evaluate)[] Declared =
    {
        ("no-work-turned-a-fight", Specificity.Neither, NoWorkTurnedAFight),
        ("short-chapter", Specificity.Neither, ShortChapter),
        ("priced-for-people", Specificity.Neither, PricedForPeople),
        ("took-the-coin-every-time", Specificity.Neither, TookTheCoinEveryTime),
        ("shelf-did-the-talking", Specificity.Neither, ShelfDidTheTalking),
        ("never-reached-into-dark", Specificity.Neither, NeverReachedIntoDark),
        ("held-item-for-hero", Specificity.HeroAndNumber, HeldItemForHero),
        ("said-goodbye-to-every-name", Specificity.Neither, SaidGoodbyeToEveryName),
        ("wall-keeps-names-unspoken", Specificity.NumberOnly, WallKeepsNamesUnspoken),
        ("wall-stayed-bare", Specificity.Neither, WallStayedBare),
        ("gold-pointed-at-floor", Specificity.HeroAndNumber, GoldPointedAtFloor),
        ("sent-supply-to-every-camped-party", Specificity.NumberOnly, SentSupplyToEveryCampedParty),
        ("finished-every-commission", Specificity.Neither, FinishedEveryCommission),
        ("proven-kills-carry-your-mark", Specificity.HeroAndNumber, ProvenKillsCarryYourMark),
        ("roster-set-floor-records-unasked", Specificity.NumberOnly, RosterSetFloorRecordsUnasked),
    };

    /// <summary>
    /// The fifteen predicates, in DECLARED order (the tie-break, see <see cref="Entry.Order"/>).
    /// First ten are the plan's own text, verbatim; last five are authored for this unit.
    /// <see cref="NoWorkTurnedAFight"/> and <see cref="ShortChapter"/> couple by design — identical
    /// firing condition, declared first — so a bad ending earns "its epithet plus one line" by
    /// ORDINARY top-3 scoring, never a branch that bypasses it.
    /// </summary>
    public static readonly ImmutableList<Entry> Registry = Declared
        .Select((d, i) => new Entry(d.Id, d.Specificity, d.Evaluate, i))
        .ToImmutableList();

    /// <summary>
    /// The closing spread: the three most specific fired predicates (ties broken by declared
    /// order, see <see cref="Entry"/>), followed always by <see cref="Closer"/>. A pure function
    /// of <paramref name="state"/> — same state in, same lines out, always.
    /// </summary>
    public static ImmutableList<string> Compose(GameState state)
    {
        var top = Registry
            .Select(e => (Entry: e, Text: e.Evaluate(state)))
            .Where(f => f.Text is not null)
            .OrderByDescending(f => (int)f.Entry.Specificity)
            .ThenBy(f => f.Entry.Order)
            .Take(3)
            .Select(f => f.Text!);

        return top.Append(Closer).ToImmutableList();
    }

    // ---- shared reads --------------------------------------------------------------------

    private static bool CampaignHasRun(GameState state) => state.EventLog.OfType<PartyReturned>().Any();

    private static int TotalAttributionBeats(GameState state) =>
        state.EventLog.OfType<AttributionBeatEvent>().Count();

    // ---- the ten plan predicates, text verbatim -------------------------------------------

    private static string? PricedForPeople(GameState state)
    {
        var sales = state.EventLog.OfType<CounterSaleClosed>().ToList();
        return sales.Count > 0 && sales.All(s => s.Pinned)
            ? "You priced for people, not for coin."
            : null;
    }

    private static string? TookTheCoinEveryTime(GameState state)
    {
        var sales = state.EventLog.OfType<CounterSaleClosed>().ToList();
        return sales.Count > 0 && sales.All(s => !s.Pinned)
            ? "You took the coin that was offered. Every time."
            : null;
    }

    private static string? ShelfDidTheTalking(GameState state)
    {
        var shelfSales = state.EventLog.OfType<ItemSold>().Count(s => s.FromPlayerShop);
        var counterSales = state.EventLog.OfType<CounterSaleClosed>().Count();
        var commissions = state.EventLog.OfType<CommissionFulfilled>().Count();
        var deliveries = state.EventLog.OfType<SupplyDelivered>().Count();
        return shelfSales > 0 && counterSales == 0 && commissions == 0 && deliveries == 0
            ? "You let the shelf do your talking."
            : null;
    }

    private static string? NeverReachedIntoDark(GameState state)
    {
        var campReports = state.EventLog.OfType<PartyCampReport>().Count();
        var deliveries = state.EventLog.OfType<SupplyDelivered>().Count();
        return campReports > 0 && deliveries == 0
            ? "You never once reached into the dark."
            : null;
    }

    /// <summary>Longest gap between an item's craft day and the day it left the shop into a
    /// hero's hands, restricted to items that later earned that hero a proven
    /// <see cref="AttributionBeatEvent"/> — "it mattered" is the beat, not a guess.</summary>
    private static string? HeldItemForHero(GameState state)
    {
        var craftedDay = state.EventLog.OfType<ItemCrafted>().ToDictionary(c => c.Item, c => c.Day);
        var earnedBeat = state.EventLog.OfType<AttributionBeatEvent>()
            .Select(b => (b.Item, b.Hero)).ToHashSet();

        (ItemId Item, HeroId Hero, int Days)? best = null;
        foreach (var d in Departures(state, craftedDay.Keys))
        {
            if (!earnedBeat.Contains((d.Item, d.Hero)))
            {
                continue;
            }

            var days = d.Day - craftedDay[d.Item];
            if (days <= 0)
            {
                continue; // "held" means at least one full day passed
            }

            if (best is null || days > best.Value.Days
                || (days == best.Value.Days && d.Item.Value < best.Value.Item.Value))
            {
                best = (d.Item, d.Hero, days);
            }
        }

        if (best is not { } b
            || !state.Items.TryGetValue(b.Item.Value, out var item)
            || !state.Heroes.TryGetValue(b.Hero.Value, out var hero))
        {
            return null;
        }

        return $"You held {item.Name} {b.Days} days for {hero.Name}. It mattered when it left.";
    }

    /// <summary>Every event that put a crafted item into a specific hero's hands — shelf, counter,
    /// commission. The vigil runner's <see cref="SupplyDelivered"/> moves a carried consumable,
    /// never a sold item, so it is not a departure here.</summary>
    private static IEnumerable<(ItemId Item, HeroId Hero, int Day)> Departures(
        GameState state, IEnumerable<ItemId> crafted)
    {
        var craftedSet = crafted.ToHashSet();
        foreach (var e in state.EventLog)
        {
            switch (e)
            {
                case ItemSold s when craftedSet.Contains(s.Item):
                    yield return (s.Item, s.Buyer, s.Day);
                    break;
                case CounterSaleClosed c when craftedSet.Contains(c.Item):
                    yield return (c.Item, c.Hero, c.Day);
                    break;
                case CommissionFulfilled f when craftedSet.Contains(f.Item):
                    yield return (f.Item, f.Hero, f.Day);
                    break;
            }
        }
    }

    private static string? SaidGoodbyeToEveryName(GameState state)
    {
        var memorials = state.Drama.Memorials;
        return memorials.Count > 0 && memorials.All(m => m.Honored)
            ? "Every name on the wall heard you say goodbye."
            : null;
    }

    private static string? WallKeepsNamesUnspoken(GameState state)
    {
        var memorials = state.Drama.Memorials;
        return memorials.Count > 0 && memorials.All(m => !m.Honored)
            ? $"The wall keeps {memorials.Count} names. You never said one of them aloud."
            : null;
    }

    private static string? WallStayedBare(GameState state) =>
        CampaignHasRun(state) && state.Drama.Memorials.Count == 0
            ? "The wall stayed bare. That is the rarest line in this book."
            : null;

    /// <summary>A posted bounty's target floor, accepted by a hero who then set a personal
    /// deepest-floor record at exactly that floor — the gold named the depth, the hero's own
    /// judgment carried them to it.</summary>
    private static string? GoldPointedAtFloor(GameState state)
    {
        var acceptedBy = state.EventLog.OfType<BountyJudged>()
            .Where(j => j.Accepted)
            .ToDictionary(j => j.Bounty, j => j.Hero);
        var reachedFloor = state.EventLog.OfType<FloorRecordSet>()
            .Select(r => (r.Hero, r.Floor)).ToHashSet();

        (BountyId Bounty, HeroId Hero, int Floor)? best = null;
        foreach (var posted in state.EventLog.OfType<BountyPosted>())
        {
            if (!acceptedBy.TryGetValue(posted.Bounty, out var hero)
                || !reachedFloor.Contains((hero, posted.TargetFloor)))
            {
                continue;
            }

            if (best is null || posted.TargetFloor > best.Value.Floor
                || (posted.TargetFloor == best.Value.Floor && posted.Bounty.Value < best.Value.Bounty.Value))
            {
                best = (posted.Bounty, hero, posted.TargetFloor);
            }
        }

        if (best is not { } b || !state.Heroes.TryGetValue(b.Hero.Value, out var heroRecord))
        {
            return null;
        }

        return $"Your gold pointed at floor {b.Floor}. {heroRecord.Name} followed it down and stayed.";
    }

    private static string? NoWorkTurnedAFight(GameState state) =>
        CampaignHasRun(state) && TotalAttributionBeats(state) == 0
            ? "No work of yours turned a fight this season. The Mine neither knows nor cares. The town does."
            : null;

    // ---- the five authored predicates ------------------------------------------------------

    /// <summary>Coupled to <see cref="NoWorkTurnedAFight"/> by design ("zero beats earns its
    /// epithet plus one line") — identical firing condition, declared right after it, so ordinary
    /// top-3 scoring prints both. No special case, no branch that bypasses scoring.</summary>
    private static string? ShortChapter(GameState state) =>
        CampaignHasRun(state) && TotalAttributionBeats(state) == 0
            ? "This chapter is short. The next campaign's doesn't have to be."
            : null;

    /// <summary>The "send the runner" side of the sixth decision — the opposite fact from
    /// <see cref="NeverReachedIntoDark"/>'s "trust their judgment" side.</summary>
    private static string? SentSupplyToEveryCampedParty(GameState state)
    {
        var campReports = state.EventLog.OfType<PartyCampReport>().Count();
        var deliveries = state.EventLog.OfType<SupplyDelivered>().Count();
        return campReports > 0 && deliveries >= campReports
            ? $"Every one of the {campReports} parties that camped below the line got your supplies."
            : null;
    }

    /// <summary>The commission channel kept rather than dropped: every accepted commission
    /// delivered, none left to expire.</summary>
    private static string? FinishedEveryCommission(GameState state)
    {
        var fulfilled = state.EventLog.OfType<CommissionFulfilled>().Count();
        var expired = state.EventLog.OfType<CommissionExpired>().Count();
        return fulfilled > 0 && expired == 0
            ? "Every commission you took, you finished."
            : null;
    }

    /// <summary>Proven killing blows credited to a player-crafted item, tallied per hero, for
    /// whichever hero carries the most (ties broken by lowest <see cref="HeroId"/>).</summary>
    private static string? ProvenKillsCarryYourMark(GameState state)
    {
        var best = state.EventLog.OfType<AttributionBeatEvent>()
            .Where(b => b.Beat == BeatType.KillingBlow)
            .GroupBy(b => b.Hero)
            .Select(g => (Hero: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Hero.Value)
            .FirstOrDefault();

        if (best.Count == 0 || !state.Heroes.TryGetValue(best.Hero.Value, out var hero))
        {
            return null;
        }

        return $"{best.Count} proven kills carry your mark and {hero.Name}'s name.";
    }

    /// <summary>The roster kept setting new personal-best depths without the player ever being
    /// asked — parties form and pick their own depth.</summary>
    private static string? RosterSetFloorRecordsUnasked(GameState state)
    {
        var records = state.EventLog.OfType<FloorRecordSet>().Count();
        return records > 0
            ? $"Your roster set {records} new personal-best floors, and not one of them asked your permission."
            : null;
    }
}
