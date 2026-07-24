using System.Collections.Immutable;
using GameSim.Classes;
using GameSim.Contracts;

namespace GameSim.Counter;

/// <summary>
/// PA4 (plan 2026-07-21-002, PKD6): the deterministic Potionomics/Recettear haggle economics —
/// per-class price factors, the round-shifting Recettear band, and the Potionomics meters'
/// tuning constants. Every function here is PURE integer math over (state, action) — ZERO RNG,
/// by hard constraint (spec §Determinism model: "a slow player and a fast player converge").
///
/// <para><b>Willingness-to-pay</b> derives from the EXISTING utility (<see cref="Heroes.ShoppingAi"/>'s
/// Buy verdict already gates on role-fit + affordability + gear-score gain — no second AI system):
/// the shelf list price, scaled by a per-class price factor (permille; Recettear's "Vanguard overpays
/// for a fitting shield, Skirmisher stingy"), the session's Interest meter, and the hero's persistent
/// mood, then capped by the hero's actual gold on hand.</para>
///
/// <para><b>Band</b>: <see cref="Band"/> reads a floor/ceiling permille pair off the ROUND (1-based;
/// capped at <see cref="MaxRounds"/>) and scales them onto the true willingness — the Recettear shift
/// that lets <c>HoldFirm</c> genuinely win a later round (the band widens/rises with each surviving
/// round, it is never a trap).</para>
/// </summary>
public static class WillingnessModel
{
    /// <summary>Per-mille factor when a class has no explicit entry below (neutral: pays list price).</summary>
    public const int NeutralPriceFactorPermille = 1000;

    /// <summary>
    /// Recettear per-class price factor (permille of list price the hero is willing to pay before
    /// any Interest/mood adjustment): above 1000 overpays, below 1000 is stingy. Vanguard/Sentinel
    /// are the shield-bearing anchors that overpay for gear that keeps them alive; Skirmisher is the
    /// mobility-focused tightwad hoarding gold; the rest sit near neutral. Pinned by
    /// <c>Counter/PerClassFactorTests</c> — change this table and the pinned test together.
    /// </summary>
    public static readonly ImmutableSortedDictionary<string, int> ClassPriceFactorPermille =
        new Dictionary<string, int>
        {
            [ClassRegistry.VanguardId] = 1150,
            [SentinelClass.Id] = 1120,
            [ClassRegistry.StrikerId] = 1000,
            [OccultistClass.Id] = 980,
            [ClassRegistry.MysticId] = 950,
            [SkirmisherClass.Id] = 820,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>Opener bonus (permille, additive onto the Interest meter): a strong role-fit
    /// present — today, a Shield shown to a shield-allowed anchor class — seeds Interest the
    /// instant the round opens (Potionomics' opener bonus).</summary>
    public const int RoleFitOpenerBonusPermille = 150;

    /// <summary>Upsell bonus (permille, additive onto Interest) for <c>SuggestItem</c> landing on a
    /// complementary EMPTY gear slot the hero would actually buy into (Potionomics upsell).</summary>
    public const int UpsellInterestBonusPermille = 80;

    /// <summary>Interest meter ceiling — bounds repeated Present/Suggest spam from inflating the
    /// band without limit (integer safety valve, not a modeled mechanic).</summary>
    public const int MaxInterestPermille = 300;

    /// <summary>Starting Patience budget per customer, in ROUNDS not seconds (Potionomics).
    /// Each <c>HaggleResponse</c> consumes exactly one, whatever its kind.</summary>
    public const int InitialPatienceRounds = 3;

    /// <summary>Hard cap on how far a single customer's round can climb — aligned with
    /// <see cref="InitialPatienceRounds"/> so Patience always exhausts at or before the cap.</summary>
    public const int MaxRounds = 3;

    /// <summary>Round-1 band floor/ceiling, permille of true willingness.</summary>
    public const int RoundFloorStartPermille = 820;
    public const int RoundCeilingStartPermille = 980;

    /// <summary>Per-surviving-round shift (Recettear): both floor and ceiling climb by this many
    /// permille for every round beyond the first — the mechanism that makes <c>HoldFirm</c> a real
    /// choice instead of a trap.</summary>
    public const int RoundStepPermille = 90;

    /// <summary>A Counter landing within this many permille of true willingness (either side) pins
    /// the sale — Recettear's "reading the hero IS the counter skill" reward.</summary>
    public const int PinWindowPermille = 60;

    /// <summary>Mood bonus applied to the hero on a pinned sale.</summary>
    public const int PinMoodBonus = 60;

    /// <summary>Session Goodwill penalty (informational meter) and persistent mood penalty applied
    /// when the player Counters ABOVE the round's ceiling — Potionomics' Suspicion, the fleece
    /// memory that feeds future bands and gossip via <c>Hero.MoodPermille</c>.</summary>
    public const int FleeceGoodwillPenaltyPermille = 120;
    public const int FleeceMoodPenalty = 80;

    /// <summary>Never let Interest/mood swing the effective price factor at or below zero —
    /// integer safety floor, not a modeled mechanic.</summary>
    public const int MinEffectiveFactorPermille = 100;

    /// <summary>
    /// U9 ("quality gets teeth"): additive permille bonus onto the effective price factor, keyed
    /// by the crafted <see cref="QualityGrade"/> — a Masterwork earns real price tolerance, Poor
    /// gear gets lowballed. <see cref="QualityGrade.Common"/> is the neutral baseline (0 bonus) BY
    /// DESIGN: every pre-existing haggle fixture/test in this suite crafts Common-quality items, so
    /// this addition is byte-identical for them without touching a single existing test — only the
    /// new non-Common cases (and any future ones) see the effect. Bounded, integer, deterministic;
    /// tune this table alone.
    /// </summary>
    public static readonly ImmutableSortedDictionary<QualityGrade, int> QualityWillingnessBonusPermille =
        new Dictionary<QualityGrade, int>
        {
            [QualityGrade.Poor] = -120,
            [QualityGrade.Common] = 0,
            [QualityGrade.Fine] = 60,
            [QualityGrade.Superior] = 130,
            [QualityGrade.Masterwork] = 220,
        }.ToImmutableSortedDictionary();

    /// <summary>Resolve a class's price factor, defaulting to neutral for an unregistered id
    /// (defensive — every built-in and add-on class is registered, but this keeps the model total).</summary>
    public static int ClassPriceFactor(string classId) =>
        ClassPriceFactorPermille.TryGetValue(classId, out var factor) ? factor : NeutralPriceFactorPermille;

    /// <summary>Resolve a quality grade's willingness bonus (U9), defaulting to neutral for any
    /// grade absent from the table (defensive — the enum is APPEND ONLY in Contracts, so a future
    /// grade this table hasn't been tuned for yet still resolves total, at 0 bonus).</summary>
    public static int QualityBonus(QualityGrade quality) =>
        QualityWillingnessBonusPermille.TryGetValue(quality, out var bonus) ? bonus : 0;

    /// <summary>
    /// True willingness-to-pay in gold: list price scaled by (class factor + session Interest +
    /// persistent hero mood + U9's quality bonus), capped at the hero's gold on hand (PKD6/PKD7:
    /// mood is read here, it never writes anywhere but the counter/gossip surfaces). Integer math,
    /// floor division. <paramref name="quality"/> defaults to <see cref="QualityGrade.Common"/> (0
    /// bonus) so pre-U9 callers/tests that never pass it are unaffected.
    /// </summary>
    public static int TrueWillingness(
        int listPrice, int heroGold, string classId, int interestPermille, int moodPermille,
        QualityGrade quality = QualityGrade.Common)
    {
        var factor = ClassPriceFactor(classId) + interestPermille + moodPermille + QualityBonus(quality);
        if (factor < MinEffectiveFactorPermille)
        {
            factor = MinEffectiveFactorPermille;
        }

        var willingness = (int)((long)listPrice * factor / 1000);
        return Math.Min(willingness, heroGold);
    }

    /// <summary>The Recettear band for a given round (1-based, clamped to <see cref="MaxRounds"/>):
    /// floor/ceiling in gold, scaled off <paramref name="trueWillingness"/>. Widens/shifts upward
    /// with every surviving round.</summary>
    public static (int Floor, int Ceiling) Band(int trueWillingness, int round)
    {
        var steps = Math.Clamp(round, 1, MaxRounds) - 1;
        var floorPermille = RoundFloorStartPermille + steps * RoundStepPermille;
        var ceilingPermille = RoundCeilingStartPermille + steps * RoundStepPermille;
        var floor = (int)((long)trueWillingness * floorPermille / 1000);
        var ceiling = (int)((long)trueWillingness * ceilingPermille / 1000);
        return (floor, ceiling);
    }

    /// <summary>True when a countered price lands within the pin window of true willingness —
    /// "reading the hero" (Recettear).</summary>
    public static bool IsPin(int counterPrice, int trueWillingness)
    {
        var lower = (int)((long)trueWillingness * (1000 - PinWindowPermille) / 1000);
        var upper = (int)((long)trueWillingness * (1000 + PinWindowPermille) / 1000);
        return counterPrice >= lower && counterPrice <= upper;
    }

    /// <summary>Clamp a meter add so repeated Present/Suggest spam cannot inflate Interest
    /// without bound (integer safety valve).</summary>
    public static int AddInterest(int currentPermille, int bonusPermille) =>
        Math.Min(currentPermille + bonusPermille, MaxInterestPermille);
}
