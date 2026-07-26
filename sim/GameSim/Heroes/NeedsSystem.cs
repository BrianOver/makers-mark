using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>One hero's current needs-lite standing (B4) — a presentation read model, never stored.
/// <see cref="StreakDays"/> is the derived unmet-demand streak; the four bool flags are the
/// crossing/level signals the demand board and hero card render, computed by comparing
/// <see cref="NeedsSystem.UnmetDemandStreakDays(HeroId,GameState,int)"/> today against yesterday so
/// a telegraph/bite/recovery line fires exactly once, the day it happens (the
/// <c>FactionStandingShifted</c> at-most-once-per-crossing precedent) — with zero new event type
/// (KTD-B4/KTD2): nothing here is stamped to <see cref="GameState.EventLog"/>.</summary>
public sealed record NeedsEntry(
    HeroId Hero,
    string HeroName,
    int StreakDays,
    bool Telegraphed,
    bool Boycotting,
    bool TelegraphedToday,
    bool BoycottBeganToday,
    bool RecoveredToday);

/// <summary>
/// Phase B (B4, R-B7): needs-lite — a per-hero UNMET-DEMAND STREAK, purely DERIVED from the event
/// log exactly like <see cref="RelationshipBands"/>/<see cref="RelationshipSystem"/> derive the
/// player-standing band and hero-pair edges (KTD2/KTD-B3 precedent): NO new <c>Hero</c>/
/// <c>GameState</c> field, NO new event type, no RNG draw, no wall clock — every call rescans
/// <see cref="GameState.EventLog"/> and recomputes from scratch, so two identical states always
/// report identical streaks regardless of when they're read.
///
/// <para>The streak counts consecutive days since this hero's last purchase FROM THE PLAYER'S OWN
/// SHOP (an <see cref="ItemSold"/> with <see cref="ItemSold.FromPlayerShop"/> true) — "hasn't found
/// anything worth buying," in the plan's words. At <see cref="BoycottThresholdDays"/> the hero
/// enters a BOYCOTT: <see cref="Heroes.HeroShoppingSystem"/> reads <see cref="IsBoycotting"/> and
/// biases (never blocks) the hero toward the rival shelf — no roster removal, ever (R-B7).
/// <see cref="TelegraphThresholdDays"/> crosses a couple of days earlier so the demand board warns
/// before the bite lands.</para>
///
/// <para><b>Recovery:</b> a single good sale resets the streak to 0 the same day it lands — the
/// hero is never locked out, only nudged. Because the boycott bias
/// (<see cref="BoycottPerceivedPricePenaltyPermille"/>) is a comparison-only price handicap, not an
/// outright block, a genuinely standout player deal still wins and recovers the hero.</para>
/// </summary>
public static class NeedsSystem
{
    /// <summary>Days of unmet demand before the demand board telegraphs the coming boycott — a
    /// couple of days ahead of <see cref="BoycottThresholdDays"/> so the player has a warning window
    /// to act (stock something this hero actually wants) before the bite lands.</summary>
    public const int TelegraphThresholdDays = 4;

    /// <summary>Days of unmet demand before the hero actually starts favoring the rival shelf — the
    /// plan's "~5-6 days."</summary>
    public const int BoycottThresholdDays = 6;

    /// <summary>The comparison-only price penalty (permille) a boycotting hero reads onto a
    /// PLAYER-shelf candidate when ranking Buy candidates in <see cref="Heroes.HeroShoppingSystem"/>
    /// — NOT the price actually charged (the purchase itself still pays/credits the real listed
    /// price). Models "prefers the rival" as a strong-but-not-absolute bias: a player deal that is
    /// enough of a standout still wins and RECOVERS the hero, so the boycott never becomes a
    /// permanent lockout.</summary>
    public const int BoycottPerceivedPricePenaltyPermille = 400;

    /// <summary>The number of consecutive days (through <see cref="GameState.Day"/> inclusive) this
    /// hero has gone without a player-shop purchase — 0 the day of a purchase. Never negative; a
    /// hero who has never bought anything streaks from their arrival day (day 1 for the starting
    /// six, the day their own <see cref="RecruitArrived"/> stamped otherwise — the same
    /// arrival-day fallback <see cref="Drama.DemandBoard"/>'s depth-stall read uses).</summary>
    public static int UnmetDemandStreakDays(HeroId hero, GameState state) =>
        UnmetDemandStreakDays(hero, state, state.Day);

    /// <summary>As <see cref="UnmetDemandStreakDays(HeroId,GameState)"/>, but computed as of an
    /// arbitrary past day — used to detect a threshold CROSSING (today vs yesterday) for the
    /// telegraph/bite/recovery narration lines without stamping a new event (the same
    /// presentation-side shadow-read trick <see cref="GameSim.Advisor.HeroForecast"/>'s shadow-tick
    /// uses, just parametrized by day instead of by hypothetical state).</summary>
    public static int UnmetDemandStreakDays(HeroId hero, GameState state, int asOfDay)
    {
        var lastPurchaseDay = 0;
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent.Day > asOfDay)
            {
                continue;
            }

            if (gameEvent is ItemSold { FromPlayerShop: true } sold && sold.Buyer == hero)
            {
                lastPurchaseDay = Math.Max(lastPurchaseDay, sold.Day);
            }
        }

        var since = lastPurchaseDay > 0 ? lastPurchaseDay : ArrivalDay(hero, state);
        return Math.Max(0, asOfDay - since);
    }

    /// <summary>True once the streak has crossed <see cref="BoycottThresholdDays"/> — the shopping
    /// bias in <see cref="Heroes.HeroShoppingSystem"/> is active this day. Read live, mid-Morning,
    /// against whatever purchases have already landed earlier THIS same tick for other heroes
    /// (deterministic: hero order is fixed ascending <see cref="HeroId"/>).</summary>
    public static bool IsBoycotting(HeroId hero, GameState state) =>
        UnmetDemandStreakDays(hero, state) >= BoycottThresholdDays;

    /// <summary>True once the streak has crossed <see cref="TelegraphThresholdDays"/> (whether or
    /// not the boycott itself has started yet) — the demand-board warning window.</summary>
    public static bool IsTelegraphed(HeroId hero, GameState state) =>
        UnmetDemandStreakDays(hero, state) >= TelegraphThresholdDays;

    /// <summary>The whole needs-lite snapshot (B4): one <see cref="NeedsEntry"/> per ALIVE hero who
    /// currently has something to report — telegraphed, boycotting, or who just recovered today.
    /// A content hero (streak below the telegraph threshold) produces no entry, so this stays a
    /// bark, not a status dump of the whole roster every day. Heroes iterate in
    /// <see cref="GameState.Heroes"/>'s own ascending-HeroId order (already deterministic — no sort
    /// needed).</summary>
    public static ImmutableList<NeedsEntry> Snapshot(GameState state)
    {
        var entries = ImmutableList.CreateBuilder<NeedsEntry>();
        foreach (var hero in state.Heroes.Values)
        {
            if (!hero.Alive)
            {
                continue; // dead heroes have no ongoing shopping relationship to telegraph
            }

            var today = UnmetDemandStreakDays(hero.Id, state, state.Day);
            var yesterday = state.Day > 1 ? UnmetDemandStreakDays(hero.Id, state, state.Day - 1) : 0;

            var telegraphed = today >= TelegraphThresholdDays;
            var boycotting = today >= BoycottThresholdDays;
            var telegraphedToday = telegraphed && yesterday < TelegraphThresholdDays;
            var boycottBeganToday = boycotting && yesterday < BoycottThresholdDays;
            var recoveredToday = today == 0 && yesterday >= TelegraphThresholdDays;

            if (!telegraphed && !recoveredToday)
            {
                continue; // content hero (or a same-day-buy recovery that never reached the warning window) — nothing to report
            }

            entries.Add(new NeedsEntry(
                hero.Id, hero.Name, today, telegraphed, boycotting,
                telegraphedToday, boycottBeganToday, recoveredToday));
        }

        return entries.ToImmutable();
    }

    /// <summary>The day this hero joined the roster — day 1 for the starting six (no
    /// <see cref="RecruitArrived"/> of their own), else the day their own
    /// <see cref="RecruitArrived"/> event stamped (mirrors <c>Drama.DemandBoard.DepthStalls</c>'s
    /// identical fallback).</summary>
    private static int ArrivalDay(HeroId hero, GameState state)
    {
        foreach (var gameEvent in state.EventLog)
        {
            if (gameEvent is RecruitArrived arrived && arrived.Hero == hero)
            {
                return arrived.Day;
            }
        }

        return 1;
    }
}
