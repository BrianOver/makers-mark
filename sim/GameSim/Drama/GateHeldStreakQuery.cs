using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// P2-END-01 option 4 ("say it out loud", MAKERS-MARK.md §11.8.1): a party held at the same
/// venue's structural gate night after night is a DIFFERENT, WORSE fact than one held once — this
/// answers "how many consecutive evenings running" so a UI can say so honestly.
///
/// <para>Reads only what <see cref="GameSim.Drama.ExpeditionRevealSystem"/> already persists
/// (§11.14.8): every Evening it emits <see cref="DecisionExplained"/>("expedition-halt:{venueId}",
/// <see cref="ExpeditionHalt"/> as its <c>Chosen</c>) into <see cref="GameState.EventLog"/>,
/// unconditionally, for whichever venue that night's result names. This is a pure count over
/// that durable record — it never re-derives <see cref="ExpeditionResolver"/>'s own gate/power
/// comparison, which is the exact "client hand-wrote a threshold that disagreed with the sim"
/// defect this repo has already paid for twice (HeroPanel's stale ladder thresholds).</para>
///
/// <para><b>Known, accepted imprecision.</b> The key is venue-only (no floor, no party) because
/// that is all <see cref="DecisionExplained"/> carries for this producer. Two DIFFERENT parties
/// holding at the SAME venue on the SAME day would conflate into one streak; nothing shipped today
/// routes more than one live party per venue per rank, so the ambiguity is unreachable in practice,
/// not silently wrong. If that ever changes, this query is the one place to widen the key —
/// never the place to guess a per-party split from prose.</para>
/// </summary>
public static class GateHeldStreakQuery
{
    /// <summary>
    /// How many evenings in a row, ending at and including <paramref name="day"/>, <paramref
    /// name="venueId"/>'s party reported <see cref="ExpeditionHalt.GateHeld"/> — 0 if <paramref
    /// name="day"/> itself was not held (a clean clear, a different halt, or no expedition to this
    /// venue that day all read the same: "not currently held", exactly like a normal return would).
    /// Walks backward one day at a time and stops at the first day that breaks the chain, so a
    /// years-long campaign never pays for scanning its entire history to answer "still now?".
    /// </summary>
    public static int ConsecutiveNights(GameState state, string venueId, int day)
    {
        var what = ExpeditionHaltWhat(venueId);
        var streak = 0;

        for (var d = day; d >= 1; d--)
        {
            var heldThatDay = false;
            foreach (var gameEvent in DayLog.For(state.EventLog, d))
            {
                if (gameEvent is DecisionExplained explained
                    && explained.What == what
                    && explained.Chosen == nameof(ExpeditionHalt.GateHeld))
                {
                    heldThatDay = true;
                    break;
                }
            }

            if (!heldThatDay)
            {
                break;
            }

            streak++;
        }

        return streak;
    }

    /// <summary>The exact <see cref="DecisionExplained.What"/> slug <c>ExpeditionRevealSystem</c>
    /// stamps for this venue's expedition-halt explanation — the one place both that emitter and
    /// this reader spell it, so they can never drift apart.</summary>
    public static string ExpeditionHaltWhat(string venueId) => $"expedition-halt:{venueId}";

    /// <summary>
    /// This file's own anti-nag rule (the task's "pin whatever rule you choose", weighed against the
    /// repo's own scarred-in 1,287x memorial-advisor nag): surface the streak fact on doubling
    /// nights only — 2, 4, 8, 16, 32, 64 — never on the nights between. A held gate is a fixed point
    /// (§11.8.1: nothing changes while it holds), so every night in between would repeat literally
    /// the same words for no new information; doubling reports the fact increasingly rarely as it
    /// keeps being true, instead of insisting on it every single evening.
    /// </summary>
    public static bool IsMilestoneNight(int consecutiveNights) =>
        consecutiveNights >= 2 && (consecutiveNights & (consecutiveNights - 1)) == 0;
}
