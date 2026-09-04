namespace GameSim.Crafting;

/// <summary>
/// P2-OQ11 (owner ruling 2026-09-04, <c>MAKERS-MARK.md</c> §11.7.12): the ONE quality curve every
/// profession's craft scorer reports through. Pure integer math — no RNG, no wall clock, no
/// transcendental <c>Math.*</c> (KTD2/KTD4): same points in, same grade out, forever, on every OS.
///
/// <para><b>What was wrong.</b> Measured for the first time in #715 (20 seeds x 100 days per
/// profession), the four crafts had four unrelated grade responses: Alchemy never produced a
/// Masterwork at all, Tanning handed one over 87.3% of the time from day 6, Engineering 59.9%, and
/// the blacksmith sat at 94.6% Superior. The scorers all fed the SAME band table
/// (<see cref="QualityRoller.RollActive"/>) but their raw accuracy fractions meant completely
/// different things: a hand that ignored everything the puzzle showed it scored 500 per-mille in
/// Alchemy, 937 in Tanning and 590 in Engineering. Feeding three incomparable numbers into one
/// shared table is what produced three unrelated curves.</para>
///
/// <para><b>The rule.</b> Each scorer reports its performance as an integer POINT count on its own
/// terms, plus the two reference point counts that make those points comparable across
/// professions:
/// <list type="bullet">
///   <item><description><paramref name="indifferentPoints"/> — what the profession's INDIFFERENT
///   hand earns: the best hand available to someone who ignores the information the puzzle puts on
///   screen (pours the right reagents in no particular order; scrapes the whole hide with one
///   unvarying pass; seats the right parts in the wrong sockets). This anchors at
///   <see cref="IndifferentAnchorPermille"/> — the middle of the Common band.</description></item>
///   <item><description><paramref name="flawlessPoints"/> — what a perfect craft earns. This
///   anchors at 1000, which is Masterwork in every profession.</description></item>
/// </list>
/// Everything between the two is linear; everything below the indifferent hand is compressed
/// linearly into the floor. So the SHAPE of the skill-to-outcome response is now identical in all
/// four crafts — an indifferent hand lands in Common, a flawless one earns Masterwork, and the
/// distance between them is spent at the same rate — while the INPUTS stay completely different.
/// That distinction is the point: <c>THE-GAME.md</c> §4.1 requires the professions to feel
/// different in the hands, so what a "point" costs to earn (a remembered pour order, a surveyed
/// hide, an identified part, a tracked heat) is untouched here and must stay that way.</para>
///
/// <para><b>Strict ordering is structural, not asserted.</b> Both segments have a slope of at
/// least 1 per-mille per point for every calibration this repo uses (the widest point scale is
/// Tanning's 80), so a strictly better point count always produces a strictly better grade — at
/// every talent level, since talent forgiveness is a flat bonus applied AFTER this curve and
/// therefore cannot flatten it. This is the same guarantee <see cref="ForgeScorer"/> gets from its
/// own <c>MaxForgivenessPermille</c> cap, reached a different way.</para>
///
/// <para><b>The blacksmith is the archetype and is deliberately NOT routed through this type.</b>
/// <see cref="ForgeScorer"/> grades a continuous tracking deviation rather than discrete points, and
/// it already has this response: a flawless trace is 1000 (Masterwork) and its slope carries a hand
/// smoothly down through every band to Poor. The three point-scored professions are calibrated TO
/// it, which is why <see cref="IndifferentAnchorPermille"/> is the grade <see cref="ForgeScorer"/>
/// already gives a smith whose hand is off by roughly 14% of the heat axis. Re-routing the forge
/// through this type would buy nothing and would disturb the §11.7.11 proportional-forgiveness
/// pins for no gain.</para>
/// </summary>
public static class CraftCurve
{
    /// <summary>
    /// Where a profession's INDIFFERENT hand lands, in per-mille — the middle of
    /// <see cref="QualityRoller.RollActive"/>'s Common band (200..549). Deliberately 100 per-mille
    /// clear of the Fine seam so that <c>RollActive</c>'s +/-25 jitter can never lift an
    /// indifferent craft out of Common: "automatic in none" is then a property of the arithmetic
    /// rather than of the dice. Equal to the grade <see cref="ForgeScorer"/> already awards a
    /// tracking deviation of ~137 per-mille (see this type's class doc on the forge as archetype).
    /// </summary>
    public const int IndifferentAnchorPermille = 450;

    /// <summary>
    /// Map a profession's own point count onto the shared per-mille grade scale.
    ///
    /// <para>Pure and total, matching every scorer's own contract: any input — negative points,
    /// points past <paramref name="flawlessPoints"/>, a zero or inverted calibration — maps to a
    /// value in [0, 1000] and never throws. Callers add their talent-assist bonus to the RESULT
    /// (never to the points), so forgiveness raises the floor without ever flattening the slope.</para>
    /// </summary>
    /// <param name="points">The performance, in the profession's own point units.</param>
    /// <param name="indifferentPoints">Points the indifferent hand earns — anchors at
    /// <see cref="IndifferentAnchorPermille"/>.</param>
    /// <param name="flawlessPoints">Points a perfect craft earns — anchors at 1000.</param>
    public static int GradeFor(int points, int indifferentPoints, int flawlessPoints)
    {
        if (flawlessPoints <= 0)
        {
            return 0; // a profession with nothing to score has nothing to grade
        }

        if (points <= 0)
        {
            return 0;
        }

        if (points >= flawlessPoints)
        {
            return 1000;
        }

        // Keep the calibration non-degenerate so both segments have a real width. A caller whose
        // indifferent hand is at or past flawless has a broken calibration, not a broken craft:
        // grade it on the flawless anchor alone rather than dividing by zero.
        var floor = indifferentPoints;
        if (floor <= 0)
        {
            floor = 1;
        }

        if (floor >= flawlessPoints)
        {
            floor = flawlessPoints - 1;
        }

        if (points <= floor)
        {
            // Below the indifferent hand: compress linearly into [0, IndifferentAnchorPermille].
            return points * IndifferentAnchorPermille / floor;
        }

        // Above it: linear across the remaining bands up to Masterwork at flawless.
        return IndifferentAnchorPermille
            + ((points - floor) * (1000 - IndifferentAnchorPermille) / (flawlessPoints - floor));
    }
}
