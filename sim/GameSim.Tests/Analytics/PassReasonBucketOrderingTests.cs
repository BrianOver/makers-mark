using Analytics;

namespace GameSim.Tests.Analytics;

/// <summary>
/// P2-HONEST-48 (§11.18 measurement 2): <see cref="Report.Bucket"/> tested the reason string for
/// the bare word "shield" before "better", so any "current gear is better" refusal that merely
/// NAMES a shield-slot item — "current Banded Kite Shield is better" — was filed as though the
/// hero's class could not equip a shield at all. Measured pre-Ending: 3,296 of 8,077 forgecounter
/// "role doesn't use shields" passes (41%) were actually this; 434 of 614 under BaselinePlayer
/// (71%).
///
/// These tests are phrased against the RULE — a reason that names a shield the hero was
/// outclassed by is not a role mismatch — not against one fixture string, so they still catch a
/// future keyword-order regression regardless of which item name collides. The reason shapes
/// below are copied verbatim from <c>ShoppingAi.EvaluateItem</c>
/// (<c>sim/GameSim/Heroes/ShoppingAi.cs</c>): <c>PassReasonKind.RoleMismatch</c> emits
/// <c>$"shields don't suit a {class}"</c>; <c>PassReasonKind.NotAnUpgrade</c> emits
/// <c>$"current {item.Name} is better"</c>. The chronicle's <c>HeroPassedOnItem</c> event
/// (<c>sim/GameSim/Contracts/Events.cs</c>) carries only the rendered string, not the
/// <c>PassReasonKind</c> enum that produced it — Contracts is deny-listed for this unit, and the
/// enum is not on the wire — so the bucketer has to key on the string SHAPE the producer actually
/// emits, not an incidental substring.
/// </summary>
public class PassReasonBucketOrderingTests
{
    [Theory]
    [InlineData("current Banded Kite Shield is better")]
    [InlineData("current Tower Shield is better")]
    [InlineData("current Aegis of the Vanguard is better")]
    public void NotAnUpgrade_NamingAShieldItem_BucketsAsCurrentGearIsBetter_NotRoleMismatch(
        string reason)
    {
        Assert.Equal("current gear is better", Report.Bucket(reason));
    }

    [Theory]
    [InlineData("shields don't suit a mystic")]
    [InlineData("shields don't suit a striker")]
    [InlineData("shields don't suit a ranger")]
    public void RoleMismatch_GenuineClassCannotEquipShield_StillBucketsAsRoleMismatch(
        string reason)
    {
        Assert.Equal("role doesn't use shields", Report.Bucket(reason));
    }

    /// <summary>The two reasons say opposite things about the shelf — one is "the smith stocked
    /// a thing this hero's class cannot use", the other is "the smith stocked the right thing
    /// and was outclassed" — so no input may land in both buckets, and every reason lands in
    /// exactly one of the known buckets (never silently dropped).</summary>
    [Theory]
    [InlineData("shields don't suit a mystic", "role doesn't use shields")]
    [InlineData("current Banded Kite Shield is better", "current gear is better")]
    [InlineData("current Tower Shield is better", "current gear is better")]
    [InlineData("no gear-score improvement", "other")]
    [InlineData("too heavy for a mystic — 12 weight, carries at most 8", "too heavy for role")]
    [InlineData("can't afford at 45g — has 30g", "can't afford")]
    [InlineData(
        "won't part with Cinderforge Blade — it's carried them through 6 fights", "other")]
    public void EveryReason_LandsInExactlyOneKnownBucket(string reason, string expectedBucket)
    {
        var knownBuckets = new[]
        {
            "can't afford", "too heavy for role", "role doesn't use shields",
            "current gear is better", "other",
        };

        var actual = Report.Bucket(reason);

        Assert.Contains(actual, knownBuckets);
        Assert.Equal(expectedBucket, actual);
    }
}
