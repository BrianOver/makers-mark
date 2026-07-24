using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>Buy or Pass — the two outcomes of a hero looking at a shelf item.</summary>
public enum ShoppingVerdictKind
{
    Buy,
    Pass,
}

/// <summary>Typed pass reasons (R8) so callers can branch without parsing prose.</summary>
public enum PassReasonKind
{
    None,          // Buy verdicts only
    RoleMismatch,  // "shields don't suit a striker"
    TooHeavy,      // "too heavy for a mystic"
    CannotAfford,  // "can't afford at 45g — has 30g"
    NotAnUpgrade,  // "current blade is better"
    QualityTooLow, // U9: a deep-floor veteran won't trust sub-Fine work
}

/// <summary>
/// One evaluation result. <see cref="Reason"/> is always human-readable — it is what
/// the player sees on a <see cref="HeroPassedOnItem"/> card (R8/AE4).
/// </summary>
public sealed record ShoppingVerdict(
    ShoppingVerdictKind Kind,
    PassReasonKind PassReason,
    string Reason,
    int GearScoreGain)
{
    public static ShoppingVerdict MakeBuy(int gain, string reason) =>
        new(ShoppingVerdictKind.Buy, PassReasonKind.None, reason, gain);

    public static ShoppingVerdict MakePass(PassReasonKind kind, string reason) =>
        new(ShoppingVerdictKind.Pass, kind, reason, GearScoreGain: 0);
}

/// <summary>
/// Pure hero shopping judgment (R7/R8): utility = gear-score improvement gated by
/// role fit and affordability. Integer math only; no RNG — same hero, same item,
/// same verdict, forever. Kept deliberately small (plan risk: second-AI-system creep).
/// </summary>
public static class ShoppingAi
{
    /// <summary>
    /// Heaviest item a Mystic will carry in any slot. Strikers "prefer high-attack
    /// two-handed weapons" without a special rule: two-handers carry the highest
    /// Attack, so they win on gear-score gain naturally.
    /// </summary>
    public const int MysticMaxWeight = 4;

    /// <summary>
    /// U9 ("quality gets teeth"): the deep-floor "veteran" gate. A hero who has reached this Mine
    /// floor or deeper has earned the right to be picky about who forged their gear — see
    /// <see cref="VeteranMinQualityGrade"/>. Rookies (<see cref="Hero.DeepestFloorReached"/> below
    /// this) are NEVER gated: a fresh game's only stock is whatever the smith just auto-crafted
    /// (often Poor/Common), so gating rookies too would risk a quality soft-lock (KD3 no-softlock
    /// guard, precedent: <c>docs/design/2026-07-19-flavorforge-erenshor-recommendations.md</c> M3).
    /// Chosen at floor 3 of 5 (<see cref="Expedition.MonsterTable.FloorCount"/>) — past the
    /// halfway point, matching the existing "veteran" hero fixtures already used elsewhere in this
    /// suite (e.g. <c>ExpeditionRevealSystemTests</c>).
    /// </summary>
    public const int VeteranFloorThreshold = 3;

    /// <summary>
    /// The minimum <see cref="QualityGrade"/> a veteran will accept — below this, they pass with a
    /// named reason regardless of price or gear-score gain. Set to <see cref="QualityGrade.Common"/>
    /// (gate-b retune, 2026-07-24): a deep veteran categorically refuses <b>Poor</b> junk, matching
    /// the plan's "refuse Poor" intent. The initial U9 value (Fine) also refused all Common —
    /// including the flat-Common rival shelf and any Common player craft — which throttled the early
    /// economy too hard for how few Fine+ items a fresh smith can make. Continuous quality demand
    /// still comes from <c>WillingnessModel.QualityWillingnessBonusPermille</c> (Poor −120 … Masterwork
    /// +220); this hard gate is only the floor that keeps a veteran off outright junk, so a
    /// Masterwork is still never valued like Poor (the problem U9 fixes) without bricking Common flow.
    /// </summary>
    public const QualityGrade VeteranMinQualityGrade = QualityGrade.Common;

    /// <summary>
    /// Judge one shelf item for one hero, resolving the hero's class from the registry
    /// (production path).
    /// </summary>
    public static ShoppingVerdict EvaluateItem(
        Hero hero,
        Item item,
        int price,
        ImmutableSortedDictionary<int, Item> items) =>
        EvaluateItem(hero, ClassRegistry.Require(hero.ClassId), item, price, items);

    /// <summary>
    /// Judge one shelf item for one hero against an explicit class definition (P3). Check
    /// order is fixed (role fit, then veteran quality gate (U9), then affordability, then gear
    /// score) so pass reasons are stable across runs. Role fit reads the DEFINITION — an add-on
    /// class's shield/weight rules are honored with no code change — while the built-ins stay
    /// byte-identical (DisplayName lowercased is the role word the R8 prose names).
    /// </summary>
    public static ShoppingVerdict EvaluateItem(
        Hero hero,
        ClassDefinition heroClass,
        Item item,
        int price,
        ImmutableSortedDictionary<int, Item> items)
    {
        // 1. Role fit (R8: the reason must name the role).
        if (item.Slot == ItemSlot.Shield && !heroClass.AllowsShield)
        {
            return ShoppingVerdict.MakePass(
                PassReasonKind.RoleMismatch,
                $"shields don't suit a {heroClass.DisplayName.ToLowerInvariant()}");
        }

        if (heroClass.MaxItemWeight is { } cap && item.Stats.Weight > cap)
        {
            return ShoppingVerdict.MakePass(
                PassReasonKind.TooHeavy,
                $"too heavy for a {heroClass.DisplayName.ToLowerInvariant()} — {item.Stats.Weight} weight, carries at most {cap}");
        }

        // 2. Veteran pickiness (U9, KD3 no-softlock: gated on floor depth so a rookie's first
        // shopping trip is never blocked). Checked before affordability — a veteran refuses
        // sub-Fine work on principle, not because of the price tag.
        if (hero.DeepestFloorReached >= VeteranFloorThreshold && item.Quality < VeteranMinQualityGrade)
        {
            return ShoppingVerdict.MakePass(
                PassReasonKind.QualityTooLow,
                $"a floor-{hero.DeepestFloorReached} veteran won't trust {item.Quality.ToString().ToLowerInvariant()} work — bring {VeteranMinQualityGrade.ToString().ToLowerInvariant()} or better");
        }

        // 3. Affordability.
        if (price > hero.Gold)
        {
            return ShoppingVerdict.MakePass(
                PassReasonKind.CannotAfford,
                $"can't afford at {price}g — has {hero.Gold}g");
        }

        // 4. Gear-score improvement: only buy strict upgrades.
        var currentScore = Hero.GearScore(hero.Gear, items);
        var newScore = Hero.GearScore(hero.Gear.WithSlot(item.Slot, item.Id), items);
        var gain = newScore - currentScore;
        if (gain <= 0)
        {
            var currentName = hero.Gear.Slot(item.Slot) is { } currentId
                && items.TryGetValue(currentId.Value, out var current)
                    ? $"current {current.Name} is better"
                    : "no gear-score improvement";
            return ShoppingVerdict.MakePass(PassReasonKind.NotAnUpgrade, currentName);
        }

        return ShoppingVerdict.MakeBuy(gain, $"upgrade: +{gain} gear score for {price}g");
    }

    /// <summary>
    /// Judge one shelf CONSUMABLE for one hero (P2). Consumables carry no gear score
    /// and fit every role, so the only judgment is affordability. The Pack-empty gate
    /// and the Heal filter live in <see cref="HeroShoppingSystem"/> — they are not
    /// per-item verdicts. Integer math only; no RNG.
    /// </summary>
    public static ShoppingVerdict EvaluateConsumable(Hero hero, Item item, int price)
    {
        if (price > hero.Gold)
        {
            return ShoppingVerdict.MakePass(
                PassReasonKind.CannotAfford,
                $"can't afford at {price}g — has {hero.Gold}g");
        }

        return ShoppingVerdict.MakeBuy(gain: 0, $"stocked up: {item.Name} {price}g");
    }

    /// <summary>
    /// True when consumable candidate A beats B (P2): cheapest first, price ties prefer
    /// the player shelf (mirroring the gear pass's deterministic tie rule in spirit),
    /// then the LOWER ItemId settles same-shelf same-price ties.
    /// </summary>
    public static bool IsBetterConsumable(
        int priceA, bool playerShelfA, ItemId idA,
        int priceB, bool playerShelfB, ItemId idB)
    {
        if (priceA != priceB)
        {
            return priceA < priceB;
        }

        if (playerShelfA != playerShelfB)
        {
            return playerShelfA;
        }

        return idA.Value < idB.Value;
    }

    /// <summary>
    /// True when candidate A is strictly better value than B. Value = gear-score gain
    /// per gold, compared by cross-multiplication (pure integers, no division, handles
    /// price 0). Ties fall to raw gain, then to the LOWER ItemId — the deterministic
    /// tie-break the tests pin.
    /// </summary>
    public static bool IsBetterValue(int gainA, int priceA, ItemId idA, int gainB, int priceB, ItemId idB)
    {
        // gainA/priceA > gainB/priceB  <=>  gainA*priceB > gainB*priceA (prices non-negative).
        var lhs = (long)gainA * priceB;
        var rhs = (long)gainB * priceA;
        if (lhs != rhs)
        {
            return lhs > rhs;
        }

        if (gainA != gainB)
        {
            return gainA > gainB;
        }

        return idA.Value < idB.Value;
    }
}
