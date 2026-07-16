using System.Numerics;

namespace GameSim.Kernel;

/// <summary>
/// Shared integer curve helpers for catalog formula adaptations (plan R7-R10, KTD6).
///
/// Catalog formulas are curve intent, not code: each adopted formula is redesigned as an
/// integer rule at per-mille (or coarser) granularity, its rational constants computed at
/// design time, and its rounding made an explicit part of its contract. These are the only
/// building blocks such rules share: geometric decay, EWMA shift decay, digit-count log10
/// (with per-mille interpolation), piecewise-linear LUT evaluation, and the MulDiv/FloorDiv
/// guards under them.
///
/// Pure static functions over integers only — no float/double, no transcendental Math.*,
/// no wall clock — so results are byte-identical on every OS and JIT (CLAUDE.md rules 4-5).
/// </summary>
public static class IntegerCurves
{
    /// <summary>Powers of ten representable in a long: Pow10[d] == 10^d for d in [0, 18].</summary>
    private static readonly long[] Pow10 =
    [
        1L,
        10L,
        100L,
        1_000L,
        10_000L,
        100_000L,
        1_000_000L,
        10_000_000L,
        100_000_000L,
        1_000_000_000L,
        10_000_000_000L,
        100_000_000_000L,
        1_000_000_000_000L,
        10_000_000_000_000L,
        100_000_000_000_000L,
        1_000_000_000_000_000L,
        10_000_000_000_000_000L,
        100_000_000_000_000_000L,
        1_000_000_000_000_000_000L,
    ];

    /// <summary>
    /// Multiply-then-divide with a full 128-bit intermediate (<see cref="Math.BigMul(long, long, out long)"/>),
    /// rounding to nearest with ties away from zero: <c>round(value * num / den)</c>.
    ///
    /// The 128-bit intermediate makes the product exact even where a naive <c>long</c>
    /// multiply would overflow (R9). Rounding adds <c>den / 2</c> to the product's magnitude
    /// before the truncating divide, sign-aware, so negative inputs mirror positive ones
    /// exactly. Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="den"/>
    /// is not positive and <see cref="OverflowException"/> when the rounded quotient does
    /// not fit in a <see cref="long"/>.
    /// </summary>
    public static long MulDiv(long value, long num, long den)
    {
        if (den <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(den), den, "den must be positive");
        }

        var high = Math.BigMul(value, num, out long low);
        var product = new Int128(unchecked((ulong)high), unchecked((ulong)low));

        // Round-to-nearest, ties away from zero: shift the product half a den toward
        // larger magnitude, then let Int128 division truncate toward zero.
        Int128 half = den / 2;
        var adjusted = product >= 0 ? product + half : product - half;
        return checked((long)(adjusted / den));
    }

    /// <summary>
    /// Geometric (exponential) decay, one tick: <c>MulDiv(value, num, den)</c>.
    ///
    /// The rational <c>num/den</c> encodes the continuous per-tick factor e^(-lambda)
    /// computed at design time — e.g. 15/16 = 0.9375 approximates lambda ~ 0.0645 for a
    /// half-life of ~11 ticks. Constants are design-time inputs; nothing is computed from
    /// lambda at runtime (R8). Inherits MulDiv's round-to-nearest contract, so small
    /// magnitudes have nonzero fixed points (e.g. 1 x 15/16 rounds back to 1); callers
    /// needing decay-to-zero should floor instead or clamp at a threshold.
    /// </summary>
    public static long DecayPerTick(long value, int num, int den) => MulDiv(value, num, den);

    /// <summary>
    /// EWMA-style shift decay, one tick: <c>value - (value &gt;&gt; k)</c> — the classic
    /// exponentially-weighted-moving-average update toward zero with alpha = 2^-k, done
    /// entirely in shifts (no divide). Per-tick factor is (2^k - 1)/2^k, so the half-life
    /// is ~ ln2 * 2^k ticks (k = 4 gives ~11 ticks).
    ///
    /// <c>&gt;&gt;</c> on a negative long is an arithmetic shift, i.e. floor division by 2^k:
    /// the subtracted term is one larger in magnitude whenever 2^k does not divide value
    /// exactly, so negative values shed slightly more per tick than their positive mirrors
    /// (and -1 collapses straight to 0). Positive values never decay below 1 by shifting
    /// alone (1 &gt;&gt; k == 0). <paramref name="k"/> must be in [1, 62]: C# masks shift
    /// counts to 6 bits, so larger values would silently wrap instead of failing.
    /// </summary>
    public static long EwmaShiftDecay(long value, int k)
    {
        if (k is < 1 or > 62)
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, "k must be in [1, 62]");
        }

        return value - (value >> k);
    }

    /// <summary>
    /// Integer log10 by digit count: floor(log10(value)) for value &gt;= 1, so 999 -&gt; 2 and
    /// 1000 -&gt; 3 (one less than the decimal digit count).
    ///
    /// Uses the Bit Twiddling Hacks "find integer log base 10" technique: estimate from the
    /// binary log via <see cref="BitOperations.Log2(ulong)"/> scaled by 1233/4096 (~ 1/log2(10)),
    /// then correct the at-most-one-too-high estimate against the powers-of-10 table. Pure
    /// integer, branch-light, exact at every power of 10. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for value &lt;= 0 (log10 undefined).
    /// </summary>
    public static int Log10(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Log10 requires value >= 1");
        }

        var estimate = (int)(((uint)BitOperations.Log2((ulong)value) + 1) * 1233 >> 12);
        return estimate - (value < Pow10[estimate] ? 1 : 0);
    }

    /// <summary>
    /// Per-mille log10 with intra-decade linear interpolation: <c>1000 * Log10(value)</c>
    /// plus the value's position within its decade [10^d, 10^(d+1)) mapped linearly onto
    /// [0, 1000] via MulDiv. Exact at every power of 10 (10^d -&gt; 1000 * d), monotone
    /// non-decreasing everywhere, integer math only.
    ///
    /// The intra-decade segment is a chord, not the log curve — a documented piecewise-linear
    /// approximation (R8), at most ~46 per-mille below true log10 mid-decade. Round-to-nearest
    /// means the last few values of a decade may round up to the next decade's boundary value
    /// (e.g. 9999 -&gt; 4000, same as 10000); monotonicity is preserved. Rejects value &lt;= 0.
    /// </summary>
    public static long Log10PerMille(long value)
    {
        var digitsLog = Log10(value); // validates value >= 1
        var low = Pow10[digitsLog];
        var span = 9L * low; // decade width; fits in long even for d = 18 (9e18 < 2^63)
        return 1000L * digitsLog + MulDiv(value - low, 1000L, span);
    }

    /// <summary>
    /// Piecewise-linear lookup table (PWL LUT) evaluation over sorted integer breakpoints:
    /// exact Y at every breakpoint X, straight-line interpolation between neighbors
    /// (MulDiv round-to-nearest, sign-aware for falling segments), clamped to the first/last
    /// Y outside the table's domain.
    ///
    /// Breakpoints must be strictly increasing in X (validated; throws
    /// <see cref="ArgumentException"/> on empty, unsorted, or duplicate-X input). Caller
    /// contract: adjacent Y values must differ by at most long.MaxValue — game-scale tables
    /// are nowhere near this. Interpolated results always lie between the segment's
    /// endpoints, so no intermediate overflows.
    /// </summary>
    public static long LutEval(ReadOnlySpan<(long X, long Y)> points, long x)
    {
        if (points.IsEmpty)
        {
            throw new ArgumentException("at least one breakpoint is required", nameof(points));
        }

        for (var i = 1; i < points.Length; i++)
        {
            if (points[i].X <= points[i - 1].X)
            {
                throw new ArgumentException("breakpoints must be strictly increasing in X", nameof(points));
            }
        }

        if (x <= points[0].X)
        {
            return points[0].Y; // clamp below (and exact first breakpoint)
        }

        var (lastX, lastY) = points[^1];
        if (x >= lastX)
        {
            return lastY; // clamp above (and exact last breakpoint)
        }

        for (var i = 1; i < points.Length; i++)
        {
            if (x < points[i].X)
            {
                var (x0, y0) = points[i - 1];
                var (x1, y1) = points[i];
                return y0 + MulDiv(x - x0, y1 - y0, x1 - x0);
            }
        }

        return lastY; // unreachable: x < lastX guarantees a segment above
    }

    /// <summary>
    /// Floored division: rounds the quotient toward negative infinity, so
    /// <c>FloorDiv(-7, 2) == -4</c> where C#'s <c>/</c> truncates toward zero
    /// (<c>-7 / 2 == -3</c>). The two agree whenever the operands share a sign or divide
    /// exactly; they differ by exactly 1 otherwise. Use this wherever a formula's contract
    /// says "floor" and negatives are possible (R9). Division by zero throws
    /// <see cref="DivideByZeroException"/>; <c>long.MinValue / -1</c> overflows, as with <c>/</c>.
    /// </summary>
    public static long FloorDiv(long a, long b)
    {
        var quotient = a / b;
        if (a % b != 0 && (a ^ b) < 0)
        {
            quotient--;
        }

        return quotient;
    }
}
