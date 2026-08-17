using Analytics;

namespace GameSim.Tests.Analytics;

/// <summary>
/// U-T6 (§11.14.8's named defect): "Downstream, Analytics buckets that false reason as a
/// gear-quality problem." PR #536 (fix(sim): the boycott stops blaming the gear) fixed the
/// EMITTED reason at the source — <c>HeroShoppingSystem.BoycottReason</c> now names the grudge and
/// deliberately avoids the word "better" so it can never collide with <see cref="Report.Bucket"/>'s
/// keyword scan. This is the CONSUMER half of that verification: proof <see cref="Report.Bucket"/>
/// itself needs no separate fix, because it was never patched for this and never had to be — a plain
/// keyword scan that does not contain "grudge"/"boycott" simply never matches that text.
///
/// <para><b>Why a literal string, not a live <c>HeroShoppingSystem</c> call.</b> <c>BoycottReason</c>
/// is a private method (this unit's lane is <c>tools/Analytics</c> + <c>sim/GameSim.Tests</c>, not
/// <c>sim/GameSim</c> — see CLAUDE.md's deny-list and this program's own lane split). The literal
/// below is copied VERBATIM from a real 20-seed/150-day batch corpus
/// (<c>batch-seed10-days150-baseline.json</c>, generated 2026-08-17 to verify this exact question),
/// not hand-typed from memory of the source — if <c>HeroShoppingSystem.BoycottReason</c>'s wording
/// ever changes, <c>HeroShoppingSystemTests</c> (sim-side) is the test that must track it; this one
/// exists solely to pin what <see cref="Report.Bucket"/> does with that shape today.</para>
/// </summary>
public class BoycottReasonBucketTests
{
    private const string RealBoycottLossReason =
        "still boycotting the shop over unmet demand — Cinderforge Blade won on the grudge, not the gear";

    [Fact]
    public void BoycottLossReason_NeverBucketsAsCurrentGearIsBetter()
    {
        Assert.NotEqual("current gear is better", Report.Bucket(RealBoycottLossReason));
    }

    /// <summary>Pinned to the ACTUAL bucket, not just "not gear" — so a future keyword added to
    /// <see cref="Report.Bucket"/> that accidentally starts matching boycott text (e.g. a naive
    /// "grudge" or "boycott" keyword added without checking order) changes this test's expected
    /// value deliberately, rather than the test staying green by accident.</summary>
    [Fact]
    public void BoycottLossReason_BucketsAsOther_UnderTodaysKeywordSet()
    {
        Assert.Equal("other", Report.Bucket(RealBoycottLossReason));
    }
}
