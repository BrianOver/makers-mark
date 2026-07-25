using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Heroes;

/// <summary>
/// Phase B (B2, R-B5): translates a hero's 2 derived <see cref="TraitId"/>s (<see cref="TraitRegistry.TraitsFor"/>)
/// into signed offsets on the FIVE existing knobs <see cref="ShoppingAi"/>/<c>WillingnessModel</c>/
/// <c>HeroShoppingSystem</c>/<c>CounterQueueSystem</c> already read. SHOP TEETH ONLY — nothing here
/// is read by <c>ExpeditionResolver</c>/<c>CombatMath</c>/target-floor/flee logic (raid teeth are
/// Phase C, KTD-B2 scope boundary). Every function is pure integer math over the hero's derived
/// traits; none draws RNG (KTD-B3 grep gate: this file contains no <c>rng.</c>).
/// </summary>
public static class TraitEffects
{
    // ---- Price sensitivity (WillingnessModel.TrueWillingness's factor sum) ------------------------
    /// <summary>Spendthrift's additive bonus onto the willingness factor (permille) — pays up.</summary>
    public const int SpendthriftBonusPermille = 90;

    /// <summary>Thrifty's additive penalty onto the willingness factor (permille) — a tighter
    /// purse, so the SAME list price reads as "overpriced" sooner (a lower ceiling/pin window).</summary>
    public const int ThriftyPenaltyPermille = -90;

    /// <summary>The price-sensitivity trait's offset onto <c>WillingnessModel.TrueWillingness</c>'s
    /// factor sum — 0 for a hero holding neither <see cref="TraitId.Thrifty"/> nor
    /// <see cref="TraitId.Spendthrift"/> (BYTE IDENTICAL to pre-trait behavior for such a hero).</summary>
    public static int PriceSensitivityPermille(Hero hero)
    {
        var traits = TraitsFor(hero);
        if (traits.Contains(TraitId.Spendthrift))
        {
            return SpendthriftBonusPermille;
        }

        return traits.Contains(TraitId.Thrifty) ? ThriftyPenaltyPermille : 0;
    }

    // ---- Quality demand (ShoppingAi's veteran minimum-quality gate) ------------------------------
    /// <summary>Discerning shifts the veteran gate's minimum acceptable <see cref="QualityGrade"/>
    /// up by this many steps (demands better work).</summary>
    public const int DiscerningGradeSteps = 1;

    /// <summary>Unfussy shifts the veteran gate's minimum acceptable grade down by this many steps
    /// (tolerates rougher work — clamped at <see cref="QualityGrade.Poor"/>, the enum floor).</summary>
    public const int UnfussyGradeSteps = -1;

    /// <summary>
    /// The <see cref="QualityGrade"/> floor a veteran hero (already gated by
    /// <see cref="ShoppingAi.VeteranFloorThreshold"/>) demands, shifted by this hero's
    /// quality-demand trait. Does NOT touch the floor-depth gate itself (KD3 no-softlock guard is
    /// preserved unconditionally — only ONCE a hero is veteran-gated does this trait bias WHICH
    /// grade clears the bar). Neutral heroes (neither <see cref="TraitId.Discerning"/> nor
    /// <see cref="TraitId.Unfussy"/>) get <paramref name="baseGrade"/> back unchanged.
    /// </summary>
    public static QualityGrade VeteranMinQualityGradeFor(Hero hero, QualityGrade baseGrade)
    {
        var traits = TraitsFor(hero);
        var steps = traits.Contains(TraitId.Discerning) ? DiscerningGradeSteps
            : traits.Contains(TraitId.Unfussy) ? UnfussyGradeSteps
            : 0;
        if (steps == 0)
        {
            return baseGrade;
        }

        var shifted = Math.Clamp((int)baseGrade + steps, (int)QualityGrade.Poor, (int)QualityGrade.Masterwork);
        return (QualityGrade)shifted;
    }

    // ---- Sentiment (ShoppingAi's storied-gear loyalty gate) ---------------------------------------
    /// <summary>Sentimental lowers the worn-deeds threshold that triggers loyalty (clings sooner);
    /// clamped so the threshold never drops below 1 deed.</summary>
    public const int SentimentalThresholdSteps = -2;

    /// <summary>Practical raises the threshold far past any deed count a hero could plausibly rack
    /// up — effectively disables the gate (upgrades freely, sentiment never wins).</summary>
    public const int PracticalThresholdSteps = 1000;

    /// <summary>The worn-deeds threshold (<see cref="ShoppingAi.SentimentalDeedThreshold"/>'s
    /// per-hero value) that triggers the storied-gear loyalty gate. Neutral heroes (neither
    /// <see cref="TraitId.Sentimental"/> nor <see cref="TraitId.Practical"/>) get
    /// <paramref name="baseThreshold"/> back unchanged.</summary>
    public static int SentimentalDeedThresholdFor(Hero hero, int baseThreshold)
    {
        var traits = TraitsFor(hero);
        if (traits.Contains(TraitId.Sentimental))
        {
            return Math.Max(1, baseThreshold + SentimentalThresholdSteps);
        }

        return traits.Contains(TraitId.Practical) ? baseThreshold + PracticalThresholdSteps : baseThreshold;
    }

    // ---- Haggle patience (WillingnessModel.InitialPatienceRounds, per active customer) -----------
    /// <summary>Patient's bonus round before the customer's patience runs out.</summary>
    public const int PatientRoundsBonus = 1;

    /// <summary>Stubborn's penalty round — walks fast; clamped so a customer always gets at least
    /// one round to consider an offer.</summary>
    public const int StubbornRoundsPenalty = -1;

    /// <summary>The Patience-round budget (<c>WillingnessModel.InitialPatienceRounds</c>'s
    /// per-hero value) a freshly-promoted active customer starts with. Neutral heroes (neither
    /// <see cref="TraitId.Patient"/> nor <see cref="TraitId.Stubborn"/>) get
    /// <paramref name="baseRounds"/> back unchanged.</summary>
    public static int PatienceRoundsFor(Hero hero, int baseRounds)
    {
        var traits = TraitsFor(hero);
        if (traits.Contains(TraitId.Patient))
        {
            return baseRounds + PatientRoundsBonus;
        }

        return traits.Contains(TraitId.Stubborn) ? Math.Max(1, baseRounds + StubbornRoundsPenalty) : baseRounds;
    }

    // ---- Consumable stocking (HeroShoppingSystem's restock target) --------------------------------
    /// <summary>Baseline restock target: buy a Heal only once the pack is completely empty, at most
    /// one per Morning — the pre-trait behavior, unchanged for any hero holding neither
    /// <see cref="TraitId.Prepared"/> nor <see cref="TraitId.Reckless"/>.</summary>
    public const int BaselineStockTarget = 1;

    /// <summary>Prepared restocks a little early — keeps up to 2 Heals on hand instead of running
    /// completely dry first.</summary>
    public const int PreparedStockTarget = 2;

    /// <summary>Reckless never restocks — heads down with an empty pack every time (the "likeliest
    /// survival bleed" the Balance re-fit should expect, per the Phase B plan).</summary>
    public const int RecklessStockTarget = 0;

    /// <summary>The pack size this hero is content to carry before browsing for another Heal.
    /// <see cref="HeroShoppingSystem"/> restocks (at most one purchase per Morning, unchanged) only
    /// while <c>hero.Pack.Count</c> is below this target.</summary>
    public static int ConsumableStockTargetFor(Hero hero)
    {
        var traits = TraitsFor(hero);
        if (traits.Contains(TraitId.Prepared))
        {
            return PreparedStockTarget;
        }

        return traits.Contains(TraitId.Reckless) ? RecklessStockTarget : BaselineStockTarget;
    }

    private static ImmutableArray<TraitId> TraitsFor(Hero hero) => TraitRegistry.TraitsFor(hero.Id, hero.Name);
}
