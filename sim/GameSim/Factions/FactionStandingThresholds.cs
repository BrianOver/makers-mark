using GameSim.Contracts;

namespace GameSim.Factions;

/// <summary>
/// The named standing thresholds that voicing crosses (P5 U4, R9/KTD7) — the ONLY place a faction's
/// standing turns into a "favored"/"cooled" gossip beat. Derived from the faction's
/// <see cref="FactionDefinition.StandingCap"/> so any faction scales without new data.
///
/// <para><b>Hysteresis (the load-bearing part).</b> The favored band has a SEPARATE enter and exit
/// boundary, not one shared threshold: standing warms when it rises through <see cref="FavoredEnter"/>
/// (cap/2) and cools when it drifts down through <see cref="FavoredExit"/> (cap*2/5). The gap between
/// them — a deadband of cap/10 — is wider than one day-cycle's movement (a single Morning
/// <see cref="FactionDriftSystem"/> drift of <see cref="FactionDefinition.DriftStep"/> plus a single
/// Evening buy of <see cref="FactionDefinition.RiseStep"/>; for Deepvein 2 + 5 = 7 &lt; 10). So a
/// standing hovering at the boundary that drifts DOWN in the Morning and is bought back UP the same
/// Evening cannot cross both boundaries in one day-cycle, and therefore cannot emit a contradictory
/// "cooled + favored" pair into a single gossip batch (KTD7). A move only ever crosses the boundary
/// that lies in its path.</para>
///
/// <para><b>Per-direction debounce falls out for free.</b> The crossing test is edge-triggered
/// (<c>old on one side, new on the other</c>): repeated same-direction moves after the crossing
/// (further buys the same Evening, further drift the next Morning) find <c>old</c> already past the
/// boundary and emit nothing. Drift runs once per Morning and only lowers standing, so it can only
/// ever emit <see cref="StandingShiftDirection.Cooled"/>; the buy handler only raises, so it can only
/// ever emit <see cref="StandingShiftDirection.Favored"/> — at most one of each per faction per
/// day-cycle by construction.</para>
///
/// Pure integer, no RNG, no float, no wall clock. Determinism-safe by construction.
/// </summary>
public static class FactionStandingThresholds
{
    /// <summary>Standing at/above which the faction is "favored"; rising THROUGH it warms the town (cap/2).</summary>
    public static int FavoredEnter(FactionDefinition faction) => faction.StandingCap / 2;

    /// <summary>Standing below which the favored band is lost; drifting THROUGH it cools the town (cap*2/5).</summary>
    public static int FavoredExit(FactionDefinition faction) => faction.StandingCap * 2 / 5;

    /// <summary>
    /// The band boundary a standing change from <paramref name="oldStanding"/> to
    /// <paramref name="newStanding"/> crossed, or null when it stayed on one side (including any move
    /// inside the deadband). A rise through <see cref="FavoredEnter"/> is
    /// <see cref="StandingShiftDirection.Favored"/>; a fall through <see cref="FavoredExit"/> is
    /// <see cref="StandingShiftDirection.Cooled"/>. Because enter &gt; exit, no single change can be
    /// both.
    /// </summary>
    public static StandingShiftDirection? Crossing(FactionDefinition faction, int oldStanding, int newStanding)
    {
        var enter = FavoredEnter(faction);
        if (oldStanding < enter && newStanding >= enter)
        {
            return StandingShiftDirection.Favored; // rose through the upper boundary
        }

        var exit = FavoredExit(faction);
        if (oldStanding >= exit && newStanding < exit)
        {
            return StandingShiftDirection.Cooled; // fell through the lower boundary
        }

        return null;
    }
}
