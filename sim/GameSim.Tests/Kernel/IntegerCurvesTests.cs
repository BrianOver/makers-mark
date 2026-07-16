using GameSim.Kernel;

namespace GameSim.Tests.Kernel;

/// <summary>
/// Golden pins + curve-shape tables for the integer curve kernel (plan U2, R7-R10).
///
/// Every expected value below was computed by hand from the documented contract and is
/// pinned forever: MulDiv is round-to-nearest (ties away from zero) over a 128-bit
/// Math.BigMul intermediate; EwmaShiftDecay uses arithmetic-shift (floor) semantics;
/// Log10 is the Bit-Twiddling-Hacks digit-count log10; LutEval is a piecewise-linear
/// LUT exact at breakpoints and clamped outside. Any drift between implementation and
/// contract fails deterministically on all three CI OSes — no floats anywhere, so there
/// is nothing platform-dependent to drift.
/// </summary>
public class IntegerCurvesTests
{
    // ------------------------------------------------------------------ MulDiv

    [Theory]
    // Boundary pins: 0, 1, den-1, exact halves (ties away from zero).
    [InlineData(0L, 123L, 7L, 0L)]
    [InlineData(1L, 1L, 2L, 1L)]    // 0.5 rounds away from zero
    [InlineData(-1L, 1L, 2L, -1L)]  // -0.5 rounds away from zero
    [InlineData(15L, 1L, 16L, 1L)]  // den-1: 0.9375 -> 1
    [InlineData(7L, 1L, 16L, 0L)]   // 0.4375 -> 0
    [InlineData(8L, 1L, 16L, 1L)]   // exactly half -> away from zero
    [InlineData(-8L, 1L, 16L, -1L)] // negative half -> away from zero
    [InlineData(-1000L, 15L, 16L, -938L)] // sign-aware rounding mirrors +1000 -> 938
    [InlineData(long.MaxValue, 1L, 1L, long.MaxValue)]
    [InlineData(long.MaxValue, 1L, 2L, 4611686018427387904L)] // (2^63-1)/2 = ...903.5 -> 904
    public void MulDiv_GoldenPins_BoundariesAndRounding(long value, long num, long den, long expected)
    {
        Assert.Equal(expected, IntegerCurves.MulDiv(value, num, den));
    }

    [Fact]
    public void MulDiv_NearOverflow_BigMulPathExact_WhereNaiveMultiplyWraps()
    {
        // 6e18 * 2 = 1.2e19 > long.MaxValue (~9.22e18): a naive long multiply wraps negative.
        Assert.True(unchecked(6_000_000_000_000_000_000L * 2L) < 0);
        Assert.Equal(4_000_000_000_000_000_000L, IntegerCurves.MulDiv(6_000_000_000_000_000_000L, 2L, 3L));

        // (2^62 - 1) * 4 = 2^64 - 4: wraps to exactly -4 in a naive long multiply.
        Assert.Equal(-4L, unchecked(4_611_686_018_427_387_903L * 4L));
        Assert.Equal(9_223_372_036_854_775_806L, IntegerCurves.MulDiv(4_611_686_018_427_387_903L, 4L, 2L));

        // long.MaxValue = 3 * 3074457345618258602 + 1, so /3 = ...602.33 -> rounds down.
        Assert.Equal(3_074_457_345_618_258_602L, IntegerCurves.MulDiv(long.MaxValue, 1_000_000L, 3_000_000L));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-16L)]
    public void MulDiv_NonPositiveDen_Rejected(long den)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerCurves.MulDiv(1000L, 15L, den));
    }

    [Fact]
    public void MulDiv_ResultOutsideLongRange_Throws()
    {
        Assert.Throws<OverflowException>(() => IntegerCurves.MulDiv(long.MaxValue, 2L, 1L));
        Assert.Throws<OverflowException>(() => IntegerCurves.MulDiv(long.MinValue, 2L, 1L));
    }

    // ------------------------------------------------------------ DecayPerTick

    [Fact]
    public void DecayPerTick_15Over16_From1000_PinnedSequence_MonotonicNonIncreasing()
    {
        // Geometric decay x15/16 per tick (num/den ~ e^-lambda with lambda ~ 0.0645,
        // half-life ~ 10.7 ticks). Exact round-to-nearest sequence, pinned forever.
        var sequence = new long[12];
        var v = 1000L;
        for (var t = 0; t < 12; t++)
        {
            sequence[t] = v;
            v = IntegerCurves.DecayPerTick(v, 15, 16);
        }

        // ~half by tick 11 (493 vs true continuous value ~491.7).
        Assert.Equal("1000,938,879,824,773,725,680,638,598,561,526,493", string.Join(",", sequence));

        // Monotonic non-increasing over a long horizon (never rebounds).
        var prev = 1000L;
        for (var t = 0; t < 50; t++)
        {
            var next = IntegerCurves.DecayPerTick(prev, 15, 16);
            Assert.True(next <= prev, $"decay increased at tick {t}: {prev} -> {next}");
            prev = next;
        }
    }

    [Fact]
    public void DecayPerTick_Boundaries()
    {
        Assert.Equal(0L, IntegerCurves.DecayPerTick(0L, 15, 16));

        // Round-to-nearest has nonzero fixed points: 1 * 15/16 = 0.9375 rounds back to 1.
        Assert.Equal(1L, IntegerCurves.DecayPerTick(1L, 15, 16));

        // Negative values decay toward zero, mirrored (sign-aware rounding).
        Assert.Equal(-938L, IntegerCurves.DecayPerTick(-1000L, 15, 16));
    }

    // --------------------------------------------------------- EwmaShiftDecay

    [Fact]
    public void EwmaShiftDecay_GoldenPins()
    {
        Assert.Equal(0L, IntegerCurves.EwmaShiftDecay(0L, 4));
        Assert.Equal(1L, IntegerCurves.EwmaShiftDecay(1L, 4));       // 1 >> 4 == 0: sticky at 1
        Assert.Equal(938L, IntegerCurves.EwmaShiftDecay(1000L, 4));  // 1000 - 62
        Assert.Equal(4611686018427387904L, IntegerCurves.EwmaShiftDecay(long.MaxValue, 1));
    }

    [Fact]
    public void EwmaShiftDecay_Negatives_FloorShiftSemantics()
    {
        // Arithmetic shift floors: -1 >> 4 == -1, so -1 collapses straight to 0.
        Assert.Equal(0L, IntegerCurves.EwmaShiftDecay(-1L, 4));

        // -1000 >> 4 == floor(-62.5) == -63: negatives shed one extra unit of magnitude
        // versus the positive mirror (938 vs -937). Documented asymmetry.
        Assert.Equal(-937L, IntegerCurves.EwmaShiftDecay(-1000L, 4));
        Assert.Equal(938L, IntegerCurves.EwmaShiftDecay(1000L, 4));
    }

    [Fact]
    public void EwmaShiftDecay_K4_HalfLifeLandmark_Tick11()
    {
        // k=4: per-tick factor 15/16, half-life ~ ln2 * 2^4 ~ 11.1 ticks.
        var v = 1000L;
        for (var t = 0; t < 11; t++)
        {
            v = IntegerCurves.EwmaShiftDecay(v, 4);
        }

        Assert.Equal(495L, v); // exact floor-shift sequence value, pinned
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(63)] // C# masks shift counts to 6 bits; 63+ would silently misbehave, so reject
    public void EwmaShiftDecay_KOutOfRange_Rejected(int k)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerCurves.EwmaShiftDecay(1000L, k));
    }

    // ------------------------------------------------------------------ Log10

    [Fact]
    public void Log10_StepsExactlyAtEachPowerOf10()
    {
        var pow = 1L;
        for (var d = 0; d <= 18; d++)
        {
            Assert.Equal(d, IntegerCurves.Log10(pow));
            if (d >= 1)
            {
                Assert.Equal(d - 1, IntegerCurves.Log10(pow - 1)); // 999 -> 2, 1000 -> 3, ...
            }

            pow = d < 18 ? pow * 10 : pow;
        }
    }

    [Theory]
    [InlineData(1L, 0)]
    [InlineData(9L, 0)]
    [InlineData(10L, 1)]
    [InlineData(99L, 1)]
    [InlineData(100L, 2)]
    [InlineData(999L, 2)]
    [InlineData(1000L, 3)]
    [InlineData(long.MaxValue, 18)]
    public void Log10_GoldenPins(long value, int expected)
    {
        Assert.Equal(expected, IntegerCurves.Log10(value));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(long.MinValue)]
    public void Log10_NonPositive_Rejected(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerCurves.Log10(value));
    }

    // ---------------------------------------------------------- Log10PerMille

    [Fact]
    public void Log10PerMille_ExactAtPowersOf10()
    {
        var pow = 1L;
        for (var d = 0; d <= 18; d++)
        {
            Assert.Equal(1000L * d, IntegerCurves.Log10PerMille(pow));
            pow = d < 18 ? pow * 10 : pow;
        }
    }

    [Theory]
    [InlineData(5L, 444L)]        // 0 + round(4 * 1000 / 9)
    [InlineData(99L, 1989L)]      // 1000 + round(89 * 1000 / 90)
    [InlineData(999L, 2999L)]     // 2000 + round(899 * 1000 / 900)
    [InlineData(5000L, 3444L)]    // 3000 + round(4000 * 1000 / 9000)
    [InlineData(long.MaxValue, 18914L)] // 18000 + round-to-nearest fraction into the partial top decade
    public void Log10PerMille_GoldenPins(long value, long expected)
    {
        Assert.Equal(expected, IntegerCurves.Log10PerMille(value));
    }

    [Fact]
    public void Log10PerMille_MonotoneNonDecreasing_Sweep()
    {
        var prev = IntegerCurves.Log10PerMille(1L);
        for (var v = 2L; v <= 10_000L; v++)
        {
            var next = IntegerCurves.Log10PerMille(v);
            Assert.True(next >= prev, $"Log10PerMille decreased at {v}: {prev} -> {next}");
            prev = next;
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Log10PerMille_NonPositive_Rejected(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegerCurves.Log10PerMille(value));
    }

    // ---------------------------------------------------------------- LutEval

    private static readonly (long X, long Y)[] Curve =
    [
        (0L, 0L), (10L, 100L), (20L, 150L), (40L, 150L), (100L, 0L),
    ];

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(10L, 100L)]
    [InlineData(20L, 150L)]
    [InlineData(40L, 150L)]
    [InlineData(100L, 0L)]
    public void LutEval_ExactAtBreakpoints(long x, long expected)
    {
        Assert.Equal(expected, IntegerCurves.LutEval(Curve, x));
    }

    [Theory]
    [InlineData(5L, 50L)]    // rising segment
    [InlineData(15L, 125L)]  // rising segment
    [InlineData(30L, 150L)]  // flat segment
    [InlineData(70L, 75L)]   // falling segment (negative dy through sign-aware MulDiv)
    public void LutEval_InterpolatesBetweenBreakpoints_Pinned(long x, long expected)
    {
        Assert.Equal(expected, IntegerCurves.LutEval(Curve, x));
    }

    [Fact]
    public void LutEval_RoundsToNearestWithinSegment()
    {
        var steep = new (long X, long Y)[] { (0L, 0L), (3L, 10L) };
        Assert.Equal(3L, IntegerCurves.LutEval(steep, 1L)); // 3.33 -> 3
        Assert.Equal(7L, IntegerCurves.LutEval(steep, 2L)); // 6.67 -> 7
    }

    [Fact]
    public void LutEval_ClampsOutsideDomain()
    {
        var signed = new (long X, long Y)[] { (-10L, -100L), (10L, 100L) };
        Assert.Equal(-100L, IntegerCurves.LutEval(signed, -50L)); // below first breakpoint
        Assert.Equal(100L, IntegerCurves.LutEval(signed, 50L));   // above last breakpoint
        Assert.Equal(0L, IntegerCurves.LutEval(signed, 0L));      // interior, negative domain
        Assert.Equal(50L, IntegerCurves.LutEval(signed, 5L));

        Assert.Equal(0L, IntegerCurves.LutEval(Curve, -1L));
        Assert.Equal(0L, IntegerCurves.LutEval(Curve, 1000L));
    }

    [Fact]
    public void LutEval_MonotoneBetweenBreakpoints()
    {
        var prev = IntegerCurves.LutEval(Curve, 0L);
        for (var x = 1L; x <= 20L; x++)
        {
            var next = IntegerCurves.LutEval(Curve, x);
            Assert.True(next >= prev, $"LUT decreased on rising segments at {x}: {prev} -> {next}");
            prev = next;
        }
    }

    [Fact]
    public void LutEval_SinglePoint_IsConstant()
    {
        var point = new (long X, long Y)[] { (5L, 42L) };
        Assert.Equal(42L, IntegerCurves.LutEval(point, long.MinValue));
        Assert.Equal(42L, IntegerCurves.LutEval(point, 5L));
        Assert.Equal(42L, IntegerCurves.LutEval(point, long.MaxValue));
    }

    [Fact]
    public void LutEval_EmptyOrUnsorted_Rejected()
    {
        Assert.Throws<ArgumentException>(() => IntegerCurves.LutEval(ReadOnlySpan<(long, long)>.Empty, 0L));

        var unsorted = new (long X, long Y)[] { (10L, 0L), (0L, 10L) };
        Assert.Throws<ArgumentException>(() => IntegerCurves.LutEval(unsorted, 5L));

        var duplicate = new (long X, long Y)[] { (0L, 0L), (0L, 10L) };
        Assert.Throws<ArgumentException>(() => IntegerCurves.LutEval(duplicate, 5L));
    }

    // --------------------------------------------------------------- FloorDiv

    [Fact]
    public void FloorDiv_VersusTruncatingDivision_OnNegatives()
    {
        // C# `/` truncates toward zero; FloorDiv rounds toward negative infinity.
        Assert.Equal(-3L, -7L / 2L);
        Assert.Equal(-4L, IntegerCurves.FloorDiv(-7L, 2L));

        Assert.Equal(-3L, 7L / -2L);
        Assert.Equal(-4L, IntegerCurves.FloorDiv(7L, -2L));

        // Same-sign and exact divisions agree with `/`.
        Assert.Equal(3L, IntegerCurves.FloorDiv(7L, 2L));
        Assert.Equal(3L, IntegerCurves.FloorDiv(-7L, -2L));
        Assert.Equal(-4L, IntegerCurves.FloorDiv(-8L, 2L));
        Assert.Equal(0L, IntegerCurves.FloorDiv(0L, 5L));
    }

    [Fact]
    public void FloorDiv_ZeroDivisor_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => IntegerCurves.FloorDiv(7L, 0L));
    }

    // ------------------------------------------- Worked catalog examples (R7)

    /// <summary>Catalog tax curve rebuilt as an integer rule: 50 + 25 * digit-count log10.</summary>
    private static long TaxPerMille(long gold) => 50L + 25L * IntegerCurves.Log10(gold);

    [Theory]
    [InlineData(99L, 75L)]       // Log10(99) = 1
    [InlineData(100L, 100L)]     // Log10(100) = 2 — bracket steps exactly at the power of 10
    [InlineData(5000L, 125L)]    // Log10(5000) = 3
    [InlineData(100000L, 175L)]  // Log10(100000) = 5
    public void CatalogExample_TaxPerMille_50Plus25Log10Gold(long gold, long expected)
    {
        Assert.Equal(expected, TaxPerMille(gold));
    }

    [Fact]
    public void CatalogExample_LoreDecay_15Over16_PinnedLandmarks()
    {
        // Lore relevance 1000 per-mille decaying x15/16 per tick; landmarks at 0/5/11/22.
        var expected = new Dictionary<int, long> { [0] = 1000L, [5] = 725L, [11] = 493L, [22] = 243L };

        var v = 1000L;
        for (var t = 0; t <= 22; t++)
        {
            if (expected.TryGetValue(t, out var pin))
            {
                Assert.Equal(pin, v);
            }

            v = IntegerCurves.DecayPerTick(v, 15, 16);
        }
    }

    [Theory]
    // Den growth = MulDiv(base, 100 + rate * days, 100): linear percent scaling.
    [InlineData(200L, 3, 0, 200L)]    // day 0: x100/100 identity
    [InlineData(200L, 3, 10, 260L)]   // +30%
    [InlineData(200L, 3, 50, 500L)]   // +150%
    [InlineData(333L, 7, 3, 403L)]    // 333 * 121 / 100 = 402.93 -> rounds to 403
    [InlineData(3_000_000_000L, 5, 20, 6_000_000_000L)] // large base, exact doubling
    public void CatalogExample_DenGrowth_LinearPercentScaling(long baseValue, int rate, int days, long expected)
    {
        Assert.Equal(expected, IntegerCurves.MulDiv(baseValue, 100L + (long)rate * days, 100L));
    }
}
