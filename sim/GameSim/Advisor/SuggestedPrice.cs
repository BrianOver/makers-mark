using System;
using GameSim.Contracts;

namespace GameSim.Advisor;

/// <summary>
/// What to suggest a player charge for one of their own crafts.
///
/// <para><b>Why this exists.</b> The formula <c>max(1, (Attack + Defense) * 2)</c> was written out
/// THREE times — <c>ActionLegality</c> plus two places in <c>ObjectiveAdvisor</c> — and it values an
/// item purely by its combat stats. A Consumable has no attack and no defense, so every healing
/// draught, poultice and elixir the player made priced at exactly <b>1 gold</b>. From the 2026-07-30
/// playtest ticker:</para>
///
/// <code>
/// day 3: Your Field Poultice sold to Torvald for 1g.
/// day 4: Rival's Soldier's Longsword sold to Sable for 40g.
/// </code>
///
/// <para>This is not only a driver artifact — the ADVISOR uses the same formula, so the game was
/// actively telling a real player to shelve their alchemy at 1g, below the cost of the copper that
/// went into it. Task P2 taught demand to want Trinkets and Consumables; nothing ever taught pricing
/// what they are worth, so the two halves disagreed.</para>
///
/// <para><b>Deliberately conservative.</b> Combat gear keeps the exact old expression, so weapons,
/// armour and shields price byte-identically to before and nothing about the balanced economy moves.
/// The only change is a floor for items the stat formula cannot see. <c>Harness/BaselinePlayer</c> is
/// intentionally NOT routed through here — it carries its own copy of the old formula and is what the
/// 100-day balance gate drives, so the gate stays a like-for-like comparison against its own history.</para>
/// </summary>
public static class SuggestedPrice
{
    /// <summary>Gold per point of consumable magnitude. A healing draught that restores a meaningful
    /// chunk of a hero's HP should read as worth more than a dagger, because to someone about to walk
    /// into floor 3 it is.</summary>
    private const int GoldPerMagnitude = 2;

    /// <summary>Floor by quality, in gold, for an item the other terms value at nothing. Deliberately
    /// modest: this is a suggestion the player is free to raise, and a floor that starts high would
    /// price the player out of a hero's willingness (see <c>WillingnessModel</c>, which scales what a
    /// hero will pay from the LIST price) — an unsold item earns less than a cheap one.</summary>
    private static int QualityFloor(QualityGrade quality) => quality switch
    {
        QualityGrade.Poor => 4,
        QualityGrade.Common => 8,
        QualityGrade.Fine => 14,
        QualityGrade.Superior => 22,
        QualityGrade.Masterwork => 34,
        _ => 4,
    };

    /// <summary>
    /// The price to suggest for <paramref name="item"/> — the greatest of what its combat stats, its
    /// consumable effect, and its quality say it is worth. Never below 1, and never below the quality
    /// floor, so no real craft is ever suggested at a token price.
    /// </summary>
    public static int For(Item item)
    {
        // The original expression, unchanged, so combat gear is unaffected.
        var fromStats = (item.Stats.Attack + item.Stats.Defense) * 2;

        var fromEffect = item.Effect is { } effect ? effect.Magnitude * GoldPerMagnitude : 0;

        return Math.Max(1, Math.Max(QualityFloor(item.Quality), Math.Max(fromStats, fromEffect)));
    }
}
