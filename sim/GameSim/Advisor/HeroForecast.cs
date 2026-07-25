using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Advisor;

/// <summary>One hero's shopping forecast, "as the shelf stands" (B1b): what they would buy right
/// now if Morning shopping ran this instant, or why they'd pass. Presentation data only.</summary>
public sealed record HeroShoppingForecast(bool WouldBuy, string? ItemName, string? Reason);

/// <summary>
/// Phase B (B1b, R-B2): the advisor's hero forecast — "as the shelf stands, Torvald buys the Iron
/// Sword." A same-day, conditional shadow-tick: it evaluates the hero against the CURRENT
/// <see cref="GameState"/> exactly as <see cref="HeroShoppingSystem"/> would this Morning, by
/// calling that system's own <see cref="HeroShoppingSystem.EvaluateGearCandidates"/> — the same
/// helper, the same tie-breaks — so the forecast can never disagree with what the real system
/// does the next time it actually runs against this same state (the forecast-exactness contract,
/// R-B2). Deliberately does NOT predict past today: the Night's Expedition draws RNG and mutates
/// hero state, so an exact "what will they buy tomorrow, post-raid" forecast is impossible without
/// replaying the RNG stream — out of scope by design (fable fix 2).
///
/// Pure read: <see cref="GameState"/> is an immutable record, so nothing is cloned or mutated —
/// there is nothing to clone. No event is stamped, so this needs no golden re-pin; it is called
/// on demand by the CLI/UI, never by a phase system.
///
/// Scope note: mirrors the ORDINARY gear-shopping pass only, not the Wave 3 accepted-commission
/// short-circuit (<c>CommissionHandlers.TryFulfillFromShelf</c>) — a standing commission is already
/// legible via 'demand', and folding its side-effecting Apply into a read-only forecast would blur
/// the "mutates nothing" guarantee this module exists to keep.
/// </summary>
public static class HeroForecast
{
    /// <summary>Forecasts one hero's next gear purchase against the shelf as it stands right now.
    /// A dead or unknown hero forecasts nothing.</summary>
    public static HeroShoppingForecast ForShelfAsItStands(GameState state, HeroId heroId)
    {
        if (!state.Heroes.TryGetValue(heroId.Value, out var hero) || !hero.Alive)
        {
            return new HeroShoppingForecast(WouldBuy: false, ItemName: null, Reason: "not present");
        }

        var (best, _) = HeroShoppingSystem.EvaluateGearCandidates(state, hero);
        return best is null
            ? new HeroShoppingForecast(WouldBuy: false, ItemName: null, Reason: "nothing on either shelf is worth buying today")
            : new HeroShoppingForecast(WouldBuy: true, best.Item.Name, best.Verdict!.Reason);
    }
}
