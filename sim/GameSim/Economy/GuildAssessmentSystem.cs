using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Economy;

/// <summary>
/// Phase D (U-D2, plan 2026-07-21-008): the Guild Assessment heartbeat. Every
/// <see cref="GuildAssessmentState.CadenceDays"/> Mornings the guild collects escalating dues (its OWN
/// cadence/track — <see cref="GameState.Assessment"/> — separate from and IN ADDITION TO the existing
/// <see cref="RentSystem"/>'s 10-day rent cycle, which is left untouched). This system EXTENDS the
/// shared town Confidence gauge (still <see cref="RentState.ConfidencePermille"/>, 0-1000 — the doc
/// comment there names this exact wiring as deliberately deferred) with the plan's fuller AtS/XCOM-
/// hybrid signal set: a passive daily decay plus depth-record / attribution-beat / hero-death deltas
/// read off YESTERDAY's stamped <see cref="GameState.EventLog"/> (the same day-after read as
/// <see cref="GossipSystem"/> — ids aren't stamped until after a system returns, so "today" can't cite
/// itself), and the assessment's own paid/missed swing.
///
/// Legible, edge-triggered threshold consequences (never game-over): below
/// <see cref="RivalExpansionThreshold"/> the rival vendor presses its advantage (feeds the existing
/// <see cref="GameState.RivalMarketSharePermille"/> edge <see cref="MarketShareSystem"/> and
/// <see cref="RivalRestockSystem"/> already read); below <see cref="HeroLeavingThreshold"/> the
/// roster's most discontented hero (<see cref="NeedsSystem.IsBoycotting"/>) visibly considers leaving;
/// at 0 a telegraphed soft-fail fires once (latched by <see cref="GuildAssessmentState.SoftFailed"/>) —
/// the actual "restart the era keeping talents + recipes" mechanics are U-D5 (prestige era, POST-v1),
/// deliberately NOT implemented here (scope control, same class as RentState's original deferral).
///
/// COMPOSITION ORDER: registered in the Morning group right after <see cref="RentSystem"/> and BEFORE
/// <see cref="DestitutionRecoverySystem"/> — a due-today assessment that leaves the till at a true
/// dead-end is caught and rescued THE SAME MORNING by the no-softlock floor, same contract as rent.
///
/// Determinism: pure integer (<see cref="IntegerCurves.MulDiv"/> for escalation), no RNG, no wall
/// clock, no transcendental <c>Math.*</c> (CLAUDE.md rules 4-5). Held-Morning guarded (mirrors
/// <see cref="RentSystem"/>/<see cref="GossipSystem"/>) so every delta fires exactly once per calendar
/// Morning, never once per stepped-counter tick.
/// </summary>
public sealed class GuildAssessmentSystem : IPhaseSystem
{
    // --- Confidence deltas, in the SAME 0-1000 permille scale RentState.ConfidencePermille already
    // uses — 10x the plan's stated 0-100 numbers (-1/day -> -10‰, +8/record -> +80‰, etc).
    public const int PassiveDailyDecayPermille = 10;
    public const int DepthRecordBonusPermille = 80;
    public const int AttributionBeatBonusPermille = 50;
    public const int HeroDeathPenaltyPermille = 100;
    public const int AssessmentPassedBonusPermille = 100;

    /// <summary>Design call (not spelled out in the plan, documented here): a missed assessment ALSO
    /// costs a modest, bounded Confidence penalty — mirrors <see cref="RentSystem"/>'s paid/missed
    /// split (a miss must sting more than a pass helps) without double-counting the passive decay.</summary>
    public const int AssessmentMissedPenaltyPermille = 50;

    /// <summary>On-time dues escalation: +500‰ (the plan's "×~1.5/period").</summary>
    public const int OnTimeEscalationPerMille = 500;

    /// <summary>Missed dues escalation: steeper than on-time — mirrors <see cref="RentSystem"/>'s
    /// on-time-vs-missed spread (150‰ vs 350‰, roughly 2.3x) at U-D2's own scale.</summary>
    public const int MissedEscalationPerMille = 750;

    /// <summary>A cap so dues escalation cannot run away across a long campaign.</summary>
    public const int MaxDuesGold = 800;

    /// <summary>Below this Confidence (400‰ = the plan's "&lt;40" on its 0-100 scale) the rival vendor
    /// visibly expands.</summary>
    public const int RivalExpansionThreshold = 400;

    /// <summary>Below this Confidence (200‰ = the plan's "&lt;20") a hero considers leaving.</summary>
    public const int HeroLeavingThreshold = 200;

    /// <summary>Rival-share pressure applied EVERY Morning Confidence sits below
    /// <see cref="RivalExpansionThreshold"/> (continuous, while <see cref="RivalExpansionTriggered"/>
    /// itself is edge-triggered — fires once on the crossing).</summary>
    public const int RivalExpansionSharePermille = 60;

    public DayPhase Phase => DayPhase.Morning;

    public string Name => "guild-assessment";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // Held-Morning guard (see RentSystem): fire once per calendar Morning, not once per
        // counter-queue tick.
        if (state.Counter is { Closed: false })
        {
            return state;
        }

        var confidenceBefore = state.Rent.ConfidencePermille;
        var confidence = confidenceBefore - PassiveDailyDecayPermille;

        var yesterday = state.Day - 1;
        if (yesterday >= 1)
        {
            foreach (var gameEvent in DayLog.For(state.EventLog, yesterday))
            {
                confidence = gameEvent switch
                {
                    FloorRecordSet => confidence + DepthRecordBonusPermille,
                    AttributionBeatEvent { Beat: BeatType.KillingBlow or BeatType.LethalSave or BeatType.BreakpointClear }
                        => confidence + AttributionBeatBonusPermille,
                    HeroDied => confidence - HeroDeathPenaltyPermille,
                    _ => confidence,
                };
            }
        }

        var assessment = state.Assessment;
        var gold = state.Player.Gold;
        var daysLeft = assessment.DaysUntilAssessment - 1;
        if (daysLeft > 0)
        {
            assessment = assessment with { DaysUntilAssessment = daysLeft };
        }
        else if (gold >= assessment.DuesGold)
        {
            var due = assessment.DuesGold;
            var nextDues = EscalatedDues(due, OnTimeEscalationPerMille);
            confidence += AssessmentPassedBonusPermille;
            gold -= due;

            events.Emit(new GuildAssessmentPassed(due, nextDues, Math.Clamp(confidence, 0, 1000)));

            assessment = new GuildAssessmentState(
                GuildAssessmentState.CadenceDays, nextDues, assessment.AssessmentsPassed + 1,
                assessment.MissedAssessments, assessment.SoftFailed);
        }
        else
        {
            // Missed: no gold moves (never drive the till negative — DestitutionRecoverySystem,
            // which runs immediately after this system, rescues a true dead-end this same Morning).
            var nextDues = EscalatedDues(assessment.DuesGold, MissedEscalationPerMille);
            confidence -= AssessmentMissedPenaltyPermille;
            var missedCount = assessment.MissedAssessments + 1;

            events.Emit(new GuildAssessmentMissed(assessment.DuesGold, nextDues, missedCount, Math.Clamp(confidence, 0, 1000)));

            assessment = new GuildAssessmentState(
                GuildAssessmentState.CadenceDays, nextDues, assessment.AssessmentsPassed,
                missedCount, assessment.SoftFailed);
        }

        confidence = Math.Clamp(confidence, 0, 1000);

        // Legible threshold consequences: edge-triggered events (fire once on the crossing), a
        // continuous rival-share pressure while below the threshold, and a latched soft-fail.
        var rivalShare = state.RivalMarketSharePermille;
        if (confidence < RivalExpansionThreshold)
        {
            rivalShare = Math.Min(1000, rivalShare + RivalExpansionSharePermille);
            if (confidenceBefore >= RivalExpansionThreshold)
            {
                events.Emit(new RivalExpansionTriggered(confidence));
            }
        }

        if (confidence < HeroLeavingThreshold && confidenceBefore >= HeroLeavingThreshold
            && MostDiscontentedHero(state) is { } discontented)
        {
            events.Emit(new HeroConsideringLeaving(discontented, confidence));
        }

        var softFailed = assessment.SoftFailed;
        if (confidence == 0 && !softFailed)
        {
            softFailed = true;
            events.Emit(new TownConfidenceCollapsed(assessment.MissedAssessments));
        }

        return state with
        {
            Player = state.Player with { Gold = gold },
            Rent = state.Rent with { ConfidencePermille = confidence },
            Assessment = assessment with { SoftFailed = softFailed },
            RivalMarketSharePermille = rivalShare,
        };
    }

    private static int EscalatedDues(int current, int escalationPerMille)
    {
        var raised = (int)IntegerCurves.MulDiv(current, 1000 + escalationPerMille, 1000);
        return Math.Min(MaxDuesGold, Math.Max(current, raised));
    }

    /// <summary>The roster's most discontented alive hero (deterministic, ascending HeroId):
    /// whoever is already boycotting the shop (<see cref="NeedsSystem.IsBoycotting"/>) if any,
    /// else the first alive hero. Reuses the existing B4 needs-lite signal rather than inventing a
    /// second unhappiness meter — null only when the roster has no one left alive.</summary>
    private static HeroId? MostDiscontentedHero(GameState state)
    {
        HeroId? fallback = null;
        foreach (var (id, hero) in state.Heroes)
        {
            if (!hero.Alive)
            {
                continue;
            }

            var heroId = new HeroId(id);
            fallback ??= heroId;
            if (NeedsSystem.IsBoycotting(heroId, state))
            {
                return heroId;
            }
        }

        return fallback;
    }
}
