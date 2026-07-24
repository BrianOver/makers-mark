using GameSim.Classes;
using GameSim.Contracts;
using GameSim.Counter;

namespace GameSim.Tests.Counter;

/// <summary>
/// PA4 (plan 2026-07-21-002, PKD6): pure-function pins for <see cref="WillingnessModel"/> — the
/// band math table (rounds 1-3, three classes) and the pin-window predicate. These are the
/// constants: change the table in <see cref="WillingnessModel"/> and these expected numbers
/// TOGETHER, per the plan's instruction ("tests pin the table; changing constants changes both").
/// No kernel, no RNG, no hero/item fixtures beyond raw numbers — the model is pure integer math
/// of (listPrice, heroGold, classId, interestPermille, moodPermille, round).
/// </summary>
public class WillingnessModelTests
{
    private const int ListPrice = 100;
    private const int PlentifulGold = 1000; // never the binding constraint in these fixtures

    [Theory]
    // classId              round  expectedWillingness  expectedFloor  expectedCeiling
    [InlineData(ClassRegistry.StrikerId, 1, 100, 82, 98)]
    [InlineData(ClassRegistry.StrikerId, 2, 100, 91, 107)]
    [InlineData(ClassRegistry.StrikerId, 3, 100, 100, 116)]
    [InlineData(ClassRegistry.VanguardId, 1, 115, 94, 112)]
    [InlineData(ClassRegistry.VanguardId, 2, 115, 104, 123)]
    [InlineData(ClassRegistry.VanguardId, 3, 115, 115, 133)]
    [InlineData(SkirmisherClass.Id, 1, 82, 67, 80)]
    [InlineData(SkirmisherClass.Id, 2, 82, 74, 87)]
    [InlineData(SkirmisherClass.Id, 3, 82, 82, 95)]
    public void Band_ExactTable_PerClassPerRound(
        string classId, int round, int expectedWillingness, int expectedFloor, int expectedCeiling)
    {
        var willingness = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, classId, interestPermille: 0, moodPermille: 0);
        Assert.Equal(expectedWillingness, willingness);

        var (floor, ceiling) = WillingnessModel.Band(willingness, round);
        Assert.Equal(expectedFloor, floor);
        Assert.Equal(expectedCeiling, ceiling);
    }

    [Fact]
    public void Band_RoundBeyondCap_ClampsToMaxRounds()
    {
        var willingness = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.StrikerId, 0, 0);
        var atCap = WillingnessModel.Band(willingness, WillingnessModel.MaxRounds);
        var pastCap = WillingnessModel.Band(willingness, WillingnessModel.MaxRounds + 5);

        Assert.Equal(atCap, pastCap);
    }

    [Fact]
    public void ClassPriceFactor_VanguardOverpays_SkirmisherIsStingy_DistinctBands()
    {
        // Recettear's headline comparable, pinned as a number: same item, same list price, same
        // gold — Vanguard and Skirmisher land on VISIBLY DIFFERENT bands (PA4 anti-solved-meta:
        // one global markup cannot fit both — see HaggleEconomicsTests for the live-sale version).
        var vanguardWillingness = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.VanguardId, 0, 0);
        var skirmisherWillingness = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, SkirmisherClass.Id, 0, 0);

        Assert.True(vanguardWillingness > skirmisherWillingness);

        var (vanguardFloor, vanguardCeiling) = WillingnessModel.Band(vanguardWillingness, round: 1);
        var (skirmisherFloor, skirmisherCeiling) = WillingnessModel.Band(skirmisherWillingness, round: 1);

        Assert.NotEqual(vanguardFloor, skirmisherFloor);
        Assert.NotEqual(vanguardCeiling, skirmisherCeiling);
        Assert.True(vanguardCeiling > skirmisherCeiling);
    }

    [Fact]
    public void TrueWillingness_CappedByHeroGold_NeverExceedsWhatHeroActuallyHas()
    {
        // A Vanguard (1150 permille factor) "wants" to pay more than list price, but a broke
        // Vanguard still can't pay more than they physically have.
        var willingness = WillingnessModel.TrueWillingness(ListPrice, heroGold: 50, ClassRegistry.VanguardId, 0, 0);
        Assert.Equal(50, willingness);
    }

    [Fact]
    public void IsPin_WithinWindow_True_OutsideWindow_False()
    {
        const int trueWillingness = 100;
        Assert.True(WillingnessModel.IsPin(94, trueWillingness));   // lower bound (940 permille)
        Assert.True(WillingnessModel.IsPin(106, trueWillingness));  // upper bound (1060 permille)
        Assert.True(WillingnessModel.IsPin(100, trueWillingness));  // exact center
        Assert.False(WillingnessModel.IsPin(93, trueWillingness));  // just below the window
        Assert.False(WillingnessModel.IsPin(107, trueWillingness)); // just above the window
    }

    [Fact]
    public void AddInterest_ClampsAtMax_NeverInflatesWithoutBound()
    {
        var stacked = WillingnessModel.AddInterest(WillingnessModel.MaxInterestPermille - 10, 150);
        Assert.Equal(WillingnessModel.MaxInterestPermille, stacked);
    }

    [Fact]
    public void ClassPriceFactor_UnregisteredClass_DefaultsNeutral()
    {
        Assert.Equal(WillingnessModel.NeutralPriceFactorPermille, WillingnessModel.ClassPriceFactor("not-a-real-class"));
    }

    [Fact]
    public void TrueWillingness_HigherQualityGrade_StrictlyHigherWillingness()
    {
        // U9 ("quality gets teeth"): the same list price/class/gold, only the crafted
        // QualityGrade changes — willingness must climb strictly with the grade, Poor lowest,
        // Masterwork highest. Same shape as the existing Vanguard-vs-Skirmisher class proof.
        var poor = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.StrikerId, 0, 0, QualityGrade.Poor);
        var common = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.StrikerId, 0, 0, QualityGrade.Common);
        var fine = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.StrikerId, 0, 0, QualityGrade.Fine);
        var superior = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.StrikerId, 0, 0, QualityGrade.Superior);
        var masterwork = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.StrikerId, 0, 0, QualityGrade.Masterwork);

        Assert.True(poor < common, $"Poor ({poor}) should be strictly cheaper than Common ({common})");
        Assert.True(common < fine, $"Common ({common}) should be strictly cheaper than Fine ({fine})");
        Assert.True(fine < superior, $"Fine ({fine}) should be strictly cheaper than Superior ({superior})");
        Assert.True(superior < masterwork, $"Superior ({superior}) should be strictly cheaper than Masterwork ({masterwork})");
    }

    [Fact]
    public void TrueWillingness_OmittedQuality_DefaultsToCommon_ByteIdenticalToPreU9Callers()
    {
        // Every pre-U9 call site (and every existing test above) never passes a quality argument —
        // this proves the default keeps them byte-identical: omitting it must equal passing
        // QualityGrade.Common explicitly (the neutral, 0-bonus entry in the table).
        var omitted = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.VanguardId, 10, 5);
        var explicitCommon = WillingnessModel.TrueWillingness(ListPrice, PlentifulGold, ClassRegistry.VanguardId, 10, 5, QualityGrade.Common);

        Assert.Equal(explicitCommon, omitted);
    }

    [Fact]
    public void QualityBonus_UnknownGrade_DefaultsNeutral()
    {
        // Defensive default (mirrors ClassPriceFactor_UnregisteredClass_DefaultsNeutral): every real
        // QualityGrade is in the table today, but an unmapped value must still resolve total at 0.
        Assert.Equal(0, WillingnessModel.QualityBonus((QualityGrade)999));
    }
}
