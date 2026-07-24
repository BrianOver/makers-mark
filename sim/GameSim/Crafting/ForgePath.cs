using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Flavor;

namespace GameSim.Crafting;

/// <summary>
/// Wave 5 (U23b, tactile forge / "Anvil Map"): the SINGLE SOURCE OF TRUTH for the forging
/// target line. Both the sim scorer (<see cref="ForgeScorer"/>, this same unit) and the
/// Godot "Anvil Map" overlay (a later unit) regenerate this polyline from the same recipe
/// inputs + <see cref="ForgeTraceInput.PathSeed"/> and MUST agree byte-for-byte — so every
/// step here is integer-only, RNG-free, wall-clock-free, and transcendental-<c>Math.*</c>-free
/// (KTD2/KTD4): only <see cref="Math.Clamp(int,int,int)"/>/<c>Min</c>/<c>Max</c> and the
/// project-owned <see cref="StableHash"/> are used for seed-varied "randomness".
///
/// <para><b>Shape (the three beats the scorer buckets samples into):</b></para>
/// <list type="bullet">
///   <item><description><b>Smelt</b> (x 0..333): starts cold (y ~150-250) and climbs into the
///   working-heat band (y ~650-850) by x ~260-300 — comfortably inside the smelt third.</description></item>
///   <item><description><b>Forge</b> (x 334..666): traverses the working band with
///   <paramref name="tier"/>-scaled undulation (more interior vertices at higher tier = a
///   busier line to track) before settling near the zone's end.</description></item>
///   <item><description><b>Quench</b> (x 667..1000): plunges to a quench trough (y ~100-200).
///   Higher tier plunges SOONER after the forge zone (a sharper, less forgiving transition);
///   heavier <paramref name="baseWeight"/> recipes push the plunge slightly later (a longer
///   sustained working heat, mirroring a heavier piece holding its temper longer).</description></item>
/// </list>
/// </summary>
public static class ForgePath
{
    /// <summary>Inclusive end of the smelt zone (the scorer's x-bucket boundary).</summary>
    public const int SmeltZoneEnd = 333;

    /// <summary>Inclusive end of the forge zone (the scorer's x-bucket boundary).</summary>
    public const int ForgeZoneEnd = 666;

    private const int DomainMax = 1000;

    // ---- Smelt-zone shape constants -------------------------------------------------------
    private const int StartYMin = 150;
    private const int StartYRange = 101; // 150..250

    private const int ClimbXMin = 260;
    private const int ClimbXRange = 41; // 260..300 (inside the smelt zone, <333)

    private const int ClimbYMin = 650;
    private const int ClimbYRange = 101; // 650..750 (entering the working band)

    // ---- Forge-zone (working band) shape constants ----------------------------------------
    private const int ForgeZoneStart = 340;
    private const int ForgeZoneWidth = 320; // 340..660, safely inside [334, 666]

    private const int WorkingYMin = 650;
    private const int WorkingYRange = 201; // 650..850

    // ---- Quench-zone shape constants -------------------------------------------------------
    private const int TroughYMin = 100;
    private const int TroughYRange = 101; // 100..200

    /// <summary>Base quench-plunge distance past the forge zone for tier 1 (least sharp).</summary>
    private const int QuenchSpanTier1 = 260;

    /// <summary>How much sharper (shorter) the plunge distance gets per tier step above 1.</summary>
    private const int QuenchSpanPerTierStep = 70;

    /// <summary>Cap on how far the weight/slot bias may push the trough out, so it can never
    /// crowd the mandatory final vertex at x=1000 regardless of recipe data.</summary>
    private const int MaxTroughX = 970;

    /// <summary>
    /// Generate the deterministic target polyline for one recipe + path variant: a FLAT,
    /// strictly-x-increasing (xPermille, yPermille) vertex list from x=0 to x=1000. Pure and
    /// total — every input is clamped defensively, so this never throws.
    /// </summary>
    public static ImmutableList<int> Generate(int tier, ItemSlot slot, int baseWeight, int pathSeed)
    {
        var clampedTier = Math.Clamp(tier, 1, 3);
        var clampedWeight = Math.Clamp(baseWeight, 0, 100);

        var seedHash = StableHash.Mix(
            unchecked((ulong)pathSeed),
            unchecked((ulong)clampedTier),
            StableHash.Mix(unchecked((ulong)(int)slot), unchecked((ulong)clampedWeight)));

        var counter = 0UL;

        int Draw(int rangeExclusive)
        {
            counter++;
            if (rangeExclusive <= 0)
            {
                return 0;
            }

            var h = StableHash.Avalanche(StableHash.Mix(seedHash, counter));
            return (int)(h % (ulong)rangeExclusive);
        }

        var builder = ImmutableList.CreateBuilder<int>();

        // --- Smelt: cold start, then climb into the working band -------------------------
        var startY = StartYMin + Draw(StartYRange);
        builder.Add(0);
        builder.Add(startY);

        var climbX = ClimbXMin + Draw(ClimbXRange);
        var climbY = ClimbYMin + Draw(ClimbYRange);
        builder.Add(climbX);
        builder.Add(climbY);

        // --- Forge: tier-scaled interior undulation across a fixed working-band window ----
        var segments = clampedTier + 1;
        var segWidth = ForgeZoneWidth / segments;
        for (var k = 1; k <= clampedTier; k++)
        {
            var nominalX = ForgeZoneStart + k * segWidth;
            var jitterRange = Math.Max(segWidth / 2 + 1, 1);
            var jitter = Draw(jitterRange) - jitterRange / 2;
            var x = nominalX + jitter;
            var y = WorkingYMin + Draw(WorkingYRange);
            builder.Add(x);
            builder.Add(y);
        }

        var forgeEndX = ForgeZoneStart + segments * segWidth; // deterministic, no jitter
        var forgeEndY = WorkingYMin + Draw(WorkingYRange);
        builder.Add(forgeEndX);
        builder.Add(forgeEndY);

        // --- Quench: plunge to the trough. Higher tier plunges sooner (sharper); heavier
        // baseWeight holds the working heat a little longer (biases the plunge later). -------
        var quenchSpan = QuenchSpanTier1 - (clampedTier - 1) * QuenchSpanPerTierStep;
        var weightBias = clampedWeight / 2; // small, legible: heavier = slightly longer hold
        var slotBias = (int)slot * 5;       // small per-slot shape variety
        var troughJitter = Draw(21) - 10;   // -10..10

        var xTrough = forgeEndX + quenchSpan + weightBias + slotBias + troughJitter;
        xTrough = Math.Min(xTrough, MaxTroughX);
        xTrough = Math.Max(xTrough, forgeEndX + 10);

        var troughY = TroughYMin + Draw(TroughYRange);
        builder.Add(xTrough);
        builder.Add(troughY);

        var endY = TroughYMin + Draw(TroughYRange);
        builder.Add(DomainMax);
        builder.Add(endY);

        return builder.ToImmutable();
    }

    /// <summary>
    /// Linear-interpolate the target heat at <paramref name="xPermille"/> along
    /// <paramref name="path"/> (a flat (x,y) vertex list as returned by <see cref="Generate"/>).
    /// Integer math only — the sim scorer and the (later) Godot overlay both call this, so any
    /// change here changes both. Defensive: a null/too-short/malformed path returns a neutral
    /// mid-value (500) rather than throwing; x is clamped to the path's own domain.
    /// </summary>
    public static int HeatAt(ImmutableList<int> path, int xPermille)
    {
        if (path is null || path.Count < 4 || path.Count % 2 != 0)
        {
            return 500;
        }

        var vertexCount = path.Count / 2;
        var x = Math.Clamp(xPermille, path[0], path[(vertexCount - 1) * 2]);

        for (var i = 0; i < vertexCount - 1; i++)
        {
            var x0 = path[i * 2];
            var y0 = path[i * 2 + 1];
            var x1 = path[(i + 1) * 2];
            var y1 = path[(i + 1) * 2 + 1];

            if (x < x0 || x > x1)
            {
                continue;
            }

            if (x1 == x0)
            {
                return y0;
            }

            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }

        // x sits exactly at the last vertex (loop above never matches x == last x0..x1 span
        // when i reaches vertexCount-2 it DOES cover it; this is belt-and-braces only).
        return path[(vertexCount - 1) * 2 + 1];
    }
}
