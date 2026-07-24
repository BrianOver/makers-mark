using System.Linq;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>The player's standing with one hero (U7, plan 2026-07-24-003). Ordered — a higher band
/// sorts ahead at the counter (U8). PURELY DERIVED from existing state (mood + how much the hero has
/// bought from the player's shop); carries no new sim field or event, so it never touches the golden
/// trace. NOT a command lever (PKD7): band only reorders the counter queue and drives prose/UI.</summary>
public enum RelationshipBand
{
    Stranger = 0,
    Regular = 1,
    Patron = 2,
    Sworn = 3,
}

/// <summary>
/// Derives a hero's <see cref="RelationshipBand"/> (U7) from two signals the sim already tracks: the
/// hero's <see cref="Hero.MoodPermille"/> opinion (Erenshor M2) and the number of items they have
/// bought FROM THE PLAYER'S SHOP (<see cref="ItemSold"/> with <c>FromPlayerShop</c> and this buyer,
/// counted off the event log). Pure/integer, no RNG, no new state — module-side (not a deny-listed
/// <c>Contracts</c> type), same placement rule as <c>ShoppingAi.PassReasonKind</c>.
/// </summary>
public static class RelationshipBands
{
    // Thresholds (tuning knobs; v1 has no hysteresis — bands are derived-only and only reorder the
    // counter queue + drive UI, so flicker is low-stakes. The threshold-crossing EVENT + hysteresis
    // is the deferred U11 piece).
    public const int SwornMinPurchases = 5;
    public const int SwornMinMood = 300;
    public const int PatronMinPurchases = 3;
    public const int PatronMinMood = 200;
    public const int RegularMinPurchases = 1;
    public const int RegularMinMood = 80;

    /// <summary>Count of items this hero bought from the player's shop over the campaign so far.</summary>
    public static int PlayerShopPurchases(HeroId hero, GameState state) =>
        state.EventLog.OfType<ItemSold>().Count(s => s.FromPlayerShop && s.Buyer == hero);

    /// <summary>The hero's current band. A hero with no id in the roster resolves to
    /// <see cref="RelationshipBand.Stranger"/> (defensive; never throws).</summary>
    public static RelationshipBand For(HeroId hero, GameState state)
    {
        var mood = state.Heroes.TryGetValue(hero.Value, out var h) ? h.MoodPermille : 0;
        var buys = PlayerShopPurchases(hero, state);

        if (buys >= SwornMinPurchases && mood >= SwornMinMood)
        {
            return RelationshipBand.Sworn;
        }

        if (buys >= PatronMinPurchases || mood >= PatronMinMood)
        {
            return RelationshipBand.Patron;
        }

        if (buys >= RegularMinPurchases || mood >= RegularMinMood)
        {
            return RelationshipBand.Regular;
        }

        return RelationshipBand.Stranger;
    }

    /// <summary>Display label for UI/prose.</summary>
    public static string Label(RelationshipBand band) => band switch
    {
        RelationshipBand.Sworn => "Sworn",
        RelationshipBand.Patron => "Patron",
        RelationshipBand.Regular => "Regular",
        _ => "Stranger",
    };
}
