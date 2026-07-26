using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>Phase B (B3, R-B6): the 4 hero&lt;-&gt;hero relationship kinds a derived edge can carry. Not
/// a value judgment ordering — <see cref="RelationshipSystem.EdgeFor"/> picks whichever kind the
/// pair's dominant contributor implies. Wave D (Erenshor) owns escalating <see cref="RivalrySeed"/>
/// into full rivalry mechanics; this Phase only lays the seed and narrates it.</summary>
public enum RelationshipKind
{
    /// <summary>No event has linked this pair (or every contribution has fully decayed).</summary>
    None = 0,

    /// <summary>Net-positive, bond-dominant: shared expeditions.</summary>
    ComradeBond,

    /// <summary>Net-positive, grief-dominant: both survived a party where a third hero died.</summary>
    Grief,

    /// <summary>Net-negative: one of the pair lost an item/commission to the other.</summary>
    Grudge,

    /// <summary>Net-negative AND the pair has crossed <see cref="RelationshipSystem.RivalrySeedThreshold"/>
    /// distinct outbid events — the same friction, repeated enough to name it something sharper than
    /// a single grudge. Still a LABEL only (no raid teeth) — Wave D's territory.</summary>
    RivalrySeed,
}

/// <summary>One hero-pair's derived standing: a signed, decayed magnitude plus the kind label that
/// best names its dominant contributor. Never stored — recomputed from the event log on every read.</summary>
public readonly record struct RelationshipEdge(RelationshipKind Kind, int Value);

/// <summary>
/// Phase B (B3, R-B6): hero&lt;-&gt;hero relationship edges, derived from the event log exactly like
/// <see cref="RelationshipBands"/> derives the player&lt;-&gt;hero band — a PURE read model. No RNG draw,
/// no mutation, no stored <see cref="GameState"/> field, no <c>Contracts</c> change (KTD-B6/KTD2):
/// every call rescans <see cref="GameState.EventLog"/> and recomputes from scratch, so two identical
/// states always report identical edges regardless of when they're read.
///
/// <para><b>Nemesis rule — only 3 mechanisms stamp an edge, all off events the sim ALREADY logs:</b></para>
/// <list type="bullet">
/// <item><description><b>Shared expeditions</b> (<see cref="PartyDeparted"/> naming both heroes) ->
/// <see cref="RelationshipKind.ComradeBond"/> (+).</description></item>
/// <item><description><b>Witnessed party-death</b>: a <see cref="HeroDied"/> hero's own
/// <see cref="PartyDeparted"/> party (same day, party contains the dead hero) also contained BOTH
/// heroes in the pair (and neither hero IS the one who died) -> <see cref="RelationshipKind.Grief"/>
/// (+) — shared loss bonds the survivors.</description></item>
/// <item><description><b>Outbid</b>: same day, same <see cref="ItemId"/>, one hero evaluated the item
/// and did NOT get it (<see cref="HeroPassedOnItem"/> or <see cref="CustomerWalked"/>), and the OTHER
/// hero closed the sale on that exact item later the same day (<see cref="ItemSold"/> or
/// <see cref="CounterSaleClosed"/>, ordered by <see cref="EventId"/> so the miss strictly precedes the
/// sale) -> <see cref="RelationshipKind.Grudge"/> (-) against the buyer. Two or more DISTINCT outbid
/// events between the same pair escalate the label (not the mechanic) to
/// <see cref="RelationshipKind.RivalrySeed"/> (<see cref="RivalrySeedThreshold"/>).</description></item>
/// </list>
///
/// <para><b>Decay:</b> each event's contribution shrinks linearly to zero over
/// <see cref="DecayWindowDays"/> days (integer division only — no transcendental <c>Math.*</c>, KTD2).
/// A pair with no recent events reads <see cref="RelationshipKind.None"/>, not a stale label.</para>
/// </summary>
public static class RelationshipSystem
{
    /// <summary>Magnitude a single shared expedition contributes (before decay).</summary>
    public const int ComradeBondMagnitude = 20;

    /// <summary>Magnitude a single witnessed party-death contributes to each survivor pair (before decay).</summary>
    public const int GriefMagnitude = 35;

    /// <summary>Magnitude (signed, negative) a single outbid contributes (before decay).</summary>
    public const int GrudgeMagnitude = -30;

    /// <summary>Days for one event's contribution to decay linearly to zero.</summary>
    public const int DecayWindowDays = 40;

    /// <summary>Distinct outbid events (undecayed count) between the same pair before the label
    /// escalates from <see cref="RelationshipKind.Grudge"/> to <see cref="RelationshipKind.RivalrySeed"/>.</summary>
    public const int RivalrySeedThreshold = 2;

    /// <summary>The derived edge between two heroes as of <see cref="GameState.Day"/>. Order of the two
    /// ids doesn't matter — the edge is symmetric (both heroes read the same kind/value). Self-pairs
    /// and a pair with no qualifying event both resolve to <see cref="RelationshipKind.None"/>/0
    /// (defensive; never throws).</summary>
    public static RelationshipEdge EdgeFor(HeroId a, HeroId b, GameState state)
    {
        if (a == b)
        {
            return default;
        }

        var (lo, hi) = a.Value <= b.Value ? (a, b) : (b, a);
        var log = state.EventLog;

        var bondSum = 0;
        foreach (var departed in log.OfType<PartyDeparted>())
        {
            if (departed.Id.Value == 0)
            {
                continue; // unstamped — mirrors the GossipGenerator R14 "not a real logged event" guard
            }

            if (departed.Party.Contains(lo) && departed.Party.Contains(hi))
            {
                bondSum += Decayed(ComradeBondMagnitude, departed.Day, state.Day);
            }
        }

        var griefSum = 0;
        foreach (var died in log.OfType<HeroDied>())
        {
            if (died.Id.Value == 0 || died.Hero == lo || died.Hero == hi)
            {
                continue; // the pair's own death is grief for nobody's RELATIONSHIP edge here
            }

            var party = log.OfType<PartyDeparted>()
                .FirstOrDefault(p => p.Day == died.Day && p.Party.Contains(died.Hero));
            if (party is not null && party.Party.Contains(lo) && party.Party.Contains(hi))
            {
                griefSum += Decayed(GriefMagnitude, died.Day, state.Day);
            }
        }

        var (grudgeSum, grudgeEvents) = GrudgeContribution(lo, hi, log, state.Day);

        var net = bondSum + griefSum + grudgeSum;
        if (net == 0)
        {
            return default;
        }

        var kind = DominantKind(bondSum, griefSum, grudgeSum, grudgeEvents);
        return new RelationshipEdge(kind, net);
    }

    /// <summary>The strongest (by |Value|) edges this hero holds against any other roster member,
    /// ties broken by ascending HeroId for determinism. Used by the <c>hero &lt;name&gt;</c> CLI card
    /// (R-B6) — read-only, never a decision input.</summary>
    public static ImmutableArray<(HeroId Other, RelationshipEdge Edge)> TopEdgesFor(
        HeroId hero, GameState state, int max = 2)
    {
        var found = new List<(HeroId Other, RelationshipEdge Edge)>();
        foreach (var otherValue in state.Heroes.Keys)
        {
            if (otherValue == hero.Value)
            {
                continue;
            }

            var other = new HeroId(otherValue);
            var edge = EdgeFor(hero, other, state);
            if (edge.Kind != RelationshipKind.None)
            {
                found.Add((other, edge));
            }
        }

        return found
            .OrderByDescending(f => Math.Abs(f.Edge.Value))
            .ThenBy(f => f.Other.Value)
            .Take(max)
            .ToImmutableArray();
    }

    /// <summary>Absolute edge magnitude between two heroes — the sole input gossip salience v2
    /// (<see cref="Drama.GossipGenerator"/>) mixes in, so a hero's news travels further when someone
    /// they have real history with is also in the day's log. Zero for an unrelated or fully-decayed
    /// pair (graceful default — no edge means no salience bonus, not an error).</summary>
    public static int Affinity(HeroId a, HeroId b, GameState state) => Math.Abs(EdgeFor(a, b, state).Value);

    /// <summary>Player-facing phrase for the <c>hero &lt;name&gt;</c> card, e.g. "comrades with",
    /// "a grudge against" — the caller appends the other hero's display name.</summary>
    public static string Phrase(RelationshipKind kind) => kind switch
    {
        RelationshipKind.ComradeBond => "comrades with",
        RelationshipKind.Grief => "grief-bonded with",
        RelationshipKind.Grudge => "a grudge against",
        RelationshipKind.RivalrySeed => "a simmering rivalry with",
        _ => "no history with",
    };

    /// <summary>Scans for outbid pairs (Nemesis rule, third mechanism): a same-day, same-item miss by
    /// one hero followed (strictly later in log/<see cref="EventId"/> order) by the other closing the
    /// sale. Returns the decayed signed sum plus the RAW (undecayed) count of distinct outbid events,
    /// the latter feeding the <see cref="RivalrySeedThreshold"/> escalation.</summary>
    private static (int Sum, int Count) GrudgeContribution(
        HeroId lo, HeroId hi, ImmutableList<GameEvent> log, int currentDay)
    {
        var sum = 0;
        var count = 0;

        for (var saleIndex = 0; saleIndex < log.Count; saleIndex++)
        {
            if (!TryReadSale(log[saleIndex], out var buyer, out var item, out var saleDay))
            {
                continue;
            }

            for (var missIndex = 0; missIndex < saleIndex; missIndex++) // strictly precedes the sale
            {
                if (!TryReadMiss(log[missIndex], out var missedHero, out var missedItem, out var missDay))
                {
                    continue;
                }

                if (missDay != saleDay || missedItem != item || missedHero == buyer)
                {
                    continue;
                }

                var isThisPair = (missedHero == lo && buyer == hi) || (missedHero == hi && buyer == lo);
                if (!isThisPair)
                {
                    continue;
                }

                sum += Decayed(GrudgeMagnitude, saleDay, currentDay);
                count++;
            }
        }

        return (sum, count);
    }

    private static bool TryReadSale(GameEvent gameEvent, out HeroId buyer, out ItemId item, out int day)
    {
        switch (gameEvent)
        {
            case ItemSold sold when sold.Id.Value != 0:
                (buyer, item, day) = (sold.Buyer, sold.Item, sold.Day);
                return true;
            case CounterSaleClosed closed when closed.Id.Value != 0:
                (buyer, item, day) = (closed.Hero, closed.Item, closed.Day);
                return true;
            default:
                (buyer, item, day) = (default, default, 0);
                return false;
        }
    }

    private static bool TryReadMiss(GameEvent gameEvent, out HeroId hero, out ItemId item, out int day)
    {
        switch (gameEvent)
        {
            case HeroPassedOnItem passed when passed.Id.Value != 0:
                (hero, item, day) = (passed.Hero, passed.Item, passed.Day);
                return true;
            case CustomerWalked walked when walked.Id.Value != 0 && walked.Item is { } walkedItem:
                (hero, item, day) = (walked.Hero, walkedItem, walked.Day);
                return true;
            default:
                (hero, item, day) = (default, default, 0);
                return false;
        }
    }

    /// <summary>Which of the 4 kinds best names a pair's net: negative net reports the outbid kinds
    /// (escalating to <see cref="RelationshipKind.RivalrySeed"/> at the event-count threshold);
    /// non-negative net reports whichever of bond/grief summed larger (ties favor
    /// <see cref="RelationshipKind.ComradeBond"/> — the more common, less dramatic mechanism).</summary>
    private static RelationshipKind DominantKind(int bondSum, int griefSum, int grudgeSum, int grudgeEvents)
    {
        if (grudgeSum < 0 && Math.Abs(grudgeSum) >= Math.Max(bondSum, griefSum))
        {
            return grudgeEvents >= RivalrySeedThreshold ? RelationshipKind.RivalrySeed : RelationshipKind.Grudge;
        }

        return griefSum > bondSum ? RelationshipKind.Grief : RelationshipKind.ComradeBond;
    }

    private static int Decayed(int magnitude, int eventDay, int currentDay)
    {
        var daysSince = currentDay - eventDay;
        if (daysSince <= 0)
        {
            return magnitude;
        }

        if (daysSince >= DecayWindowDays)
        {
            return 0;
        }

        // Integer-only linear decay to zero at DecayWindowDays (KTD2: no transcendental Math.*).
        return magnitude * (DecayWindowDays - daysSince) / DecayWindowDays;
    }
}
