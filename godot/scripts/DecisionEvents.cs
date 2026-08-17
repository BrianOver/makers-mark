using System.Collections.Immutable;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// U-T6: the second missing half of the owner's standing directive ("ideally all actions and REASON
/// behind them is logged so you can check later" — register item #164, MAKERS-MARK.md §11.14.8).
///
/// <para><b>What went wrong.</b> <see cref="PlaytestLog.Decision"/> is the general reason channel and,
/// before this file, had exactly ONE call site in the whole repo (<c>AudioDirector.SpeakNarrator</c>).
/// Meanwhile the sim was already stamping typed reasons onto six event records —
/// <see cref="HeroPassedOnItem.Reason"/>, <see cref="HeroDecisionExplained.Reason"/>,
/// <see cref="BountyJudged.Reason"/>, <see cref="CustomerWalked.Reason"/>, <see cref="HeroDied.Cause"/>,
/// <see cref="AttributionBeatEvent.Detail"/> — and every one of them reached a UI panel (ShopPanel,
/// HeroPanel, BountyPanel, CounterPanel/CustomerVoice, the Ledger's memorial line, LedgerModal) but
/// NONE of them reached the session log a human playtest actually produces. A reason that only ever
/// scrolls past on screen is not a reason you can check later — it is exactly the "23-item checklist
/// asking him to write it down by hand" problem <c>PlaytestLog</c>'s own doc exists to end.</para>
///
/// <para><b>Hard constraint: this may not ask the sim anything.</b> Same discipline as
/// <see cref="ActionSubject"/> — every case below reads ONLY a field the sim already put on the
/// event record. No id is resolved to a name, no evaluator is consulted, nothing here computes a
/// reason the sim did not already compute. This is a mirror, not an opinion.</para>
///
/// <para><b>Wired at the choke points, not per-panel.</b> <see cref="LogAll"/> is called from
/// <see cref="SimAdapter.Queue"/>'s immediate branch and <see cref="SimAdapter.AdvancePhase"/> — the
/// same two spots <see cref="PlaytestLog.Action"/> already fires from — over the events THAT CALL
/// PRODUCED (<c>TickResult.Events</c>/<c>ApplyNow</c>'s result), never over the phase's accumulated
/// <c>LastEvents</c>, so a hero decision is logged exactly once no matter how many immediate actions
/// land in the same phase before the bell.</para>
/// </summary>
public static class DecisionEvents
{
    /// <summary>
    /// Echoes every reason-bearing event in <paramref name="events"/> into <see cref="PlaytestLog"/>.
    /// A no-op when the recorder is off (checked once, not per-event, since a session with logging
    /// disabled is the common case and should not pay for the iteration).
    /// </summary>
    public static void LogAll(ImmutableList<GameEvent> events)
    {
        if (!PlaytestLog.Active || events.IsEmpty)
        {
            return;
        }

        foreach (var evt in events)
        {
            switch (evt)
            {
                case HeroDecisionExplained d:
                    // candidates: -1 — the client only ever sees the chosen item and its named
                    // runner-up, never the full candidate count EvaluateGearCandidates actually
                    // ranked, so claiming a specific number here would be exactly the "client
                    // invents a value the sim did not hand it" defect this file exists to avoid.
                    PlaytestLog.Decision($"hero-gear-pick:{d.Hero.Value}", d.Chosen, d.Reason);
                    break;

                case HeroPassedOnItem p:
                    PlaytestLog.Decision($"hero-item-pass:{p.Hero.Value}", $"declined item #{p.Item.Value}", p.Reason);
                    break;

                case BountyJudged b:
                    // A bounty judgment is structurally binary (accept/decline) — 2 is a fact about
                    // the type, not a guess.
                    PlaytestLog.Decision($"bounty-judged:{b.Bounty.Value}", b.Accepted ? "accepted" : "declined", b.Reason, candidates: 2);
                    break;

                case CustomerWalked w:
                    PlaytestLog.Decision($"customer-walked:{w.Hero.Value}",
                        w.Item is { } item ? $"walked from item #{item.Value}" : "walked, nothing presented", w.Reason);
                    break;

                case HeroDied hd:
                    PlaytestLog.Decision($"hero-died:{hd.Hero.Value}", $"floor {hd.Floor}", hd.Cause);
                    break;

                case AttributionBeatEvent beat:
                    // The counterfactual-proven beat this whole game is named after (PlaytestLog's
                    // own 2026-08-11 doc note). Detail already reaches the Ledger/JourneyStream/
                    // AdventureTicker on screen; this is the first time it reaches the session log.
                    PlaytestLog.Decision($"attribution-beat:{beat.Beat}",
                        $"item #{beat.Item.Value} / hero #{beat.Hero.Value} / floor {beat.Floor}", beat.Detail);
                    break;
            }
        }
    }

    /// <summary>
    /// U-T6: "the reveal deletes its own evidence" (§11.14.8) — every recorded roll and the typed
    /// <see cref="ExpeditionResult.Halt"/> are destroyed the SAME TICK <c>ExpeditionRevealSystem</c>
    /// narrates them (<c>state.PendingExpeditions</c> is cleared to empty in the same
    /// <c>Process</c> call that reads it). After Evening, nothing in <c>GameState</c> says why a
    /// party stopped short of its target — that permanent record needs a Contracts change (see this
    /// unit's CONTRACT-REQUEST) and is out of scope here.
    ///
    /// <para>What IS in scope: <see cref="SimAdapter.LastRevealedExpeditions"/> already snapshots the
    /// full <see cref="ExpeditionResult"/> — Halt included — the tick BEFORE the reveal clears it
    /// (V7b, for <c>ExpeditionNarrator</c>'s retelling). Calling this from that exact snapshot point
    /// means the typed halt reaches the durable session log before the sim forgets it, at zero
    /// Contracts cost: every field read here already exists on <see cref="ExpeditionResult"/>.</para>
    /// </summary>
    public static void LogRevealed(ImmutableList<ExpeditionResult> results)
    {
        if (!PlaytestLog.Active || results.IsEmpty)
        {
            return;
        }

        foreach (var result in results)
        {
            var why = $"{result.Survivors.Count} survived, {result.Deaths.Count} dead, "
                + $"cleared {result.DeepestFloorCleared}/{result.TargetFloor}, {result.Floors.Count} floors fought";
            PlaytestLog.Decision($"expedition-halt:{result.VenueId}", result.Halt.ToString(), why);
        }
    }
}
