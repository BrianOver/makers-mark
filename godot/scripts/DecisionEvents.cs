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
/// <para><b>A seventh, generic case.</b> <see cref="DecisionExplained"/> is the sim's own persisted
/// version of exactly this file's shape (<c>What</c>/<c>Chosen</c>/<c>Reason</c>/<c>Candidates</c>,
/// 1:1 with <see cref="PlaytestLog.Decision"/>'s parameters) for a decision that has no typed event
/// of its own to carry a reason on. Its case is a straight echo, not a reformat — see
/// <c>GameSim.Drama.ExpeditionRevealSystem</c>'s expedition-halt explanation for the first
/// producer, which replaced this file's own former <c>LogRevealed</c>/
/// <see cref="SimAdapter.LastRevealedExpeditions"/> snapshot workaround now that the sim persists
/// the reason itself instead of the client racing the tick that discards it.</para>
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

                case DecisionExplained d:
                    // The generic persisted channel (§11.14.8): a decision with no typed event of
                    // its own to carry a Reason on. First (and so far only) producer is
                    // ExpeditionRevealSystem's expedition-halt explanation — "the reveal deletes
                    // its own evidence" is now fixed at the sim, not patched here: this case is a
                    // straight echo, unlike the five above it, which each reformat a typed field.
                    PlaytestLog.Decision(d.What, d.Chosen, d.Reason, d.Candidates);
                    break;
            }
        }
    }
}
