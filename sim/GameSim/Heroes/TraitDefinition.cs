using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Flavor;

namespace GameSim.Heroes;

/// <summary>
/// Phase B (B2, R-B5): the five opposing shop-teeth axes a hero's two traits are drawn from.
/// APPEND ONLY — <see cref="TraitRegistry.TraitsFor"/> indexes this order by a hash modulo, so
/// reordering or removing an axis re-derives every hero in every campaign.
/// </summary>
public enum TraitAxis
{
    PriceSensitivity,
    QualityDemand,
    Sentiment,
    HagglePatience,
    ConsumableStocking,
}

/// <summary>The 10 traits (B2, R-B5) — exactly two opposing entries per <see cref="TraitAxis"/>,
/// all with a non-zero shop tooth (<see cref="TraitEffects"/>). APPEND ONLY.</summary>
public enum TraitId
{
    Thrifty,
    Spendthrift,
    Discerning,
    Unfussy,
    Sentimental,
    Practical,
    Patient,
    Stubborn,
    Prepared,
    Reckless,
}

/// <summary>One trait's identity, axis, and player-facing copy (CLI chip / tooltip / gossip
/// template — R-B5).</summary>
public sealed record TraitDefinition(TraitId Id, TraitAxis Axis, string DisplayName, string Tooltip);

/// <summary>
/// Phase B (B2, KTD-B3): the trait catalogue plus the DERIVED (never drawn, never stored)
/// 2-traits-per-hero pick. <see cref="TraitsFor"/> is a pure function of
/// <c>(HeroId, Name)</c> ONLY — no campaign identity mixed in (the fable-adopted default,
/// plan 2026-07-25-002 "trait-variance decision"): the starting cast carries the SAME two traits
/// every campaign, a consistent anchor cast. <c>StableHash.Avalanche</c> runs before every modulo
/// (the <see cref="Crafting.ForgePath"/> precedent) — raw FNV-1a low bits barely move across
/// sequential HeroIds, so skipping the avalanche would make picks cluster instead of vary.
///
/// <para><b>Dedup (KTD-B3):</b> the two picks are two DISTINCT axes (never the same axis twice),
/// so a hero can never hold both a trait and its opposite — dedup is structural, not a retry loop.</para>
///
/// <para><b>Zero draws:</b> nothing here calls <c>IDeterministicRng</c>; a hero's traits are
/// recomputed from its stored <c>Id</c>/<c>Name</c> on every read, never persisted on
/// <see cref="Hero"/> itself (KTD-B3/KTD9 — no <c>Contracts</c> field, no save-format change).</para>
/// </summary>
public static class TraitRegistry
{
    /// <summary>All 10 traits, in the order they're grouped below (display/registry-conformance
    /// order only — NOT the derivation order, which reads <see cref="TraitAxis"/>'s declaration
    /// order via <see cref="Enum.GetValues{TEnum}"/>-independent fixed array below).</summary>
    public static readonly ImmutableArray<TraitDefinition> All = ImmutableArray.Create(
        new TraitDefinition(TraitId.Thrifty, TraitAxis.PriceSensitivity,
            "Thrifty", "Walks from an overpriced deal sooner — a tight purse."),
        new TraitDefinition(TraitId.Spendthrift, TraitAxis.PriceSensitivity,
            "Spendthrift", "Pays up for what they want — gold burns a hole in this pocket."),
        new TraitDefinition(TraitId.Discerning, TraitAxis.QualityDemand,
            "Discerning", "Wants a higher grade of work — won't trust anything common."),
        new TraitDefinition(TraitId.Unfussy, TraitAxis.QualityDemand,
            "Unfussy", "Common work suits them just fine."),
        new TraitDefinition(TraitId.Sentimental, TraitAxis.Sentiment,
            "Sentimental", "Clings to storied gear that's carried them this far."),
        new TraitDefinition(TraitId.Practical, TraitAxis.Sentiment,
            "Practical", "Upgrades freely — sentiment never slows a good trade."),
        new TraitDefinition(TraitId.Patient, TraitAxis.HagglePatience,
            "Patient", "Haggles a few extra rounds before giving up."),
        new TraitDefinition(TraitId.Stubborn, TraitAxis.HagglePatience,
            "Stubborn", "Walks away fast when a deal doesn't suit them."),
        new TraitDefinition(TraitId.Prepared, TraitAxis.ConsumableStocking,
            "Prepared", "Keeps a deeper stock of Heals before heading down."),
        new TraitDefinition(TraitId.Reckless, TraitAxis.ConsumableStocking,
            "Reckless", "Carries fewer Heals than they probably should."));

    /// <summary>Derivation axis order — fixed, APPEND ONLY (indexed by a hash modulo below).</summary>
    private static readonly ImmutableArray<TraitAxis> AxisOrder = ImmutableArray.Create(
        TraitAxis.PriceSensitivity, TraitAxis.QualityDemand, TraitAxis.Sentiment,
        TraitAxis.HagglePatience, TraitAxis.ConsumableStocking);

    /// <summary>(positive-side, negative-side) trait per axis — "positive"/"negative" is a labeling
    /// convenience only (which side the coin-flip hash calls 0 vs 1), not a value judgment.</summary>
    private static readonly ImmutableDictionary<TraitAxis, (TraitId Side0, TraitId Side1)> AxisSides =
        new Dictionary<TraitAxis, (TraitId, TraitId)>
        {
            [TraitAxis.PriceSensitivity] = (TraitId.Spendthrift, TraitId.Thrifty),
            [TraitAxis.QualityDemand] = (TraitId.Discerning, TraitId.Unfussy),
            [TraitAxis.Sentiment] = (TraitId.Sentimental, TraitId.Practical),
            [TraitAxis.HagglePatience] = (TraitId.Patient, TraitId.Stubborn),
            [TraitAxis.ConsumableStocking] = (TraitId.Prepared, TraitId.Reckless),
        }.ToImmutableDictionary();

    /// <summary>This trait's catalogue entry. Throws only for a value outside the enum's declared
    /// range — every declared <see cref="TraitId"/> has exactly one entry (registry-conformance
    /// tested).</summary>
    public static TraitDefinition Definition(TraitId id) => All.First(t => t.Id == id);

    /// <summary>
    /// The 2 traits this hero carries, from 2 DISTINCT axes — campaign-invariant, derived fresh
    /// from <c>(heroId, name)</c> every call (KTD-B3: no draw, no stored field). Order is stable
    /// (axis-order ascending) so callers get a deterministic pair, not a set.
    /// </summary>
    public static ImmutableArray<TraitId> TraitsFor(HeroId heroId, string name)
    {
        var seed = StableHash.Mix(unchecked((ulong)heroId.Value), StableHash.HashString(name));
        var axisCount = AxisOrder.Length;

        var axis1Index = (int)(StableHash.Avalanche(StableHash.Mix(seed, 1UL)) % (ulong)axisCount);

        // Second axis: draw among the (axisCount - 1) OTHER axes, mapping the roll around the
        // gap at axis1Index — structurally excludes axis1Index, so the two picks are always
        // distinct axes (the dedup KTD-B3 requires) without a retry loop.
        var axis2Roll = (int)(StableHash.Avalanche(StableHash.Mix(seed, 2UL)) % (ulong)(axisCount - 1));
        var axis2Index = axis2Roll >= axis1Index ? axis2Roll + 1 : axis2Roll;

        var (loIndex, hiIndex) = axis1Index <= axis2Index ? (axis1Index, axis2Index) : (axis2Index, axis1Index);

        return ImmutableArray.Create(
            PickSide(seed, AxisOrder[loIndex]),
            PickSide(seed, AxisOrder[hiIndex]));
    }

    /// <summary>True iff this hero's 2 derived traits include <paramref name="trait"/>.</summary>
    public static bool Has(HeroId heroId, string name, TraitId trait) =>
        TraitsFor(heroId, name).Contains(trait);

    private static TraitId PickSide(ulong seed, TraitAxis axis)
    {
        var (side0, side1) = AxisSides[axis];
        var coin = StableHash.Avalanche(StableHash.Mix(seed, 3UL, unchecked((ulong)axis))) % 2UL;
        return coin == 0 ? side0 : side1;
    }
}
