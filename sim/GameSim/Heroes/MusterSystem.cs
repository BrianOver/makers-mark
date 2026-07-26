using System.Collections.Immutable;
using GameSim.Bounties;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Venues;

namespace GameSim.Heroes;

/// <summary>
/// Pure Morning-tick projection of the Expedition tick's outcome (world rework U9, KTD8): what
/// parties will form and what floor each will target, computed WITHOUT waiting for the Expedition
/// tick to actually happen. Consumed by the adapter (ticker lines, HUD, save-visible history via
/// <see cref="PartiesFormed"/>) two phases before <see cref="ExpeditionSystem"/> makes it real.
///
/// Zero RNG draws — every step below is deterministic integer/ordering logic already proven
/// RNG-free elsewhere (<c>GossipSystem</c>/<c>FactionDriftSystem</c> precedent): bounty
/// first-accept judging (<see cref="BountyRules.JudgeFirstAccept"/>), party formation
/// (<see cref="PartyFormation.FormParties"/>), then the same target-floor rule
/// (<see cref="ExpeditionSystem.TargetFloorFor"/>) the real tick uses. One rule, two call sites —
/// prediction and authority can never drift apart because they call the identical helpers.
/// </summary>
public static class MusterPlan
{
    /// <summary>
    /// Predicts today's muster from the roster and bounty board as they stand right now (Morning,
    /// after every earlier Morning system has run — registration order is load-bearing, see
    /// <see cref="MusterSystem"/> and <c>GameComposition</c>). Never mutates the real bounty board:
    /// the predicted acceptances are a local projection, silent (no <c>BountyJudged</c> events) —
    /// the authoritative judging still happens at the Expedition tick, two phases later.
    /// </summary>
    public static ImmutableList<PartyPlan> Compute(
        ImmutableSortedDictionary<int, Hero> heroes,
        ImmutableList<Bounty> bounties,
        ImmutableSortedDictionary<int, Item> items)
    {
        var predictedBounties = BountyRules.JudgeFirstAccept(heroes, bounties);

        var parties = PartyFormation.FormParties(heroes);

        // Phase C U-C4: the SAME queue-seeded routing ExpeditionSystem.Process runs, over the
        // identical parties in the identical order — so this Morning prediction never disagrees
        // with what the Expedition tick actually forms two phases later (the byte-match property
        // test, MusterSystemTests.PredictedRoster_ByteMatches_ExpeditionSystem_Over100Days).
        var queueCounts = VenueRegistry.LiveRotation.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);

        var plans = ImmutableList.CreateBuilder<PartyPlan>();
        foreach (var partyIds in parties)
        {
            var party = partyIds.Select(id => heroes[id.Value]).ToImmutableList();

            var bounty = predictedBounties.FirstOrDefault(b =>
                b.AcceptedBy is { } acceptor && partyIds.Contains(acceptor));

            string venueId;
            if (bounty is not null)
            {
                venueId = VenueRegistry.MineId; // bounties are Mine-scoped (R18) — see ExpeditionSystem
            }
            else
            {
                var partyDepth = party.Max(h => h.DeepestFloorReached);
                var partyPower = CombatMath.PartyAveragePower(party, items);
                venueId = VenueRouter.ChooseVenue(partyDepth, partyPower, VenueRegistry.LiveRotation, queueCounts);
            }

            queueCounts[venueId] = queueCounts.TryGetValue(venueId, out var count) ? count + 1 : 1;
            var venue = VenueRegistry.Require(venueId);

            var targetFloor = ExpeditionSystem.TargetFloorFor(party, partyIds, predictedBounties, venue);
            plans.Add(new PartyPlan(partyIds, targetFloor, venueId));
        }

        return plans.ToImmutable();
    }
}

/// <summary>
/// Morning-phase system (world rework U9): emits <see cref="PartiesFormed"/> so the adapter knows
/// tomorrow's — actually TODAY's, later this same day — parties and target floors before the
/// Expedition tick makes them real. REGISTRATION POSITION IS LOAD-BEARING (KTD8): must register
/// LAST in <c>GameComposition</c>'s Morning block, after <c>RecruitSystem</c> (adds heroes the same
/// tick — a same-morning recruit must appear in the emitted roster) and after
/// <c>HeroShoppingSystem</c> (mutates hero fields <see cref="MusterPlan.Compute"/> reads via
/// <see cref="BountyRules.Judge"/>) — otherwise the emitted roster/floor diverges from what
/// <see cref="ExpeditionSystem"/> actually forms two phases later, breaking the byte-match property
/// test. Zero RNG draws; state is never mutated (pure projection + one event emission).
/// </summary>
public sealed class MusterSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "muster";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var parties = MusterPlan.Compute(state.Heroes, state.Bounties, state.Items);
        events.Emit(new PartiesFormed(parties));

        // Phase B (B1a, R-B1): explain the muster's target-floor decision — capped to the one case
        // that is an actual DECISION (a bounty overriding the usual depth-based floor), not every
        // party every morning, mirroring HeroPassedOnItem's anti-spam precedent. Pure read over the
        // just-computed plan; stamps no state, draws no RNG.
        foreach (var plan in parties)
        {
            StampTargetFloorDecision(state, plan, events);
        }

        return state;
    }

    /// <summary>
    /// Phase B (B1a): names the party's target floor when it diverges from the ordinary
    /// depth-based default (a member's accepted bounty overrode it — <c>ExpeditionSystem.TargetFloorFor</c>'s
    /// own rule, duplicated here as one small formula rather than reaching into Expedition/ so this
    /// stays a pure read over MusterSystem's own inputs). Silent when the floor IS the default —
    /// that is a fallback, not a decision worth a card.
    /// </summary>
    private static void StampTargetFloorDecision(GameState state, PartyPlan plan, IEventSink events)
    {
        if (plan.Roster.IsEmpty)
        {
            return;
        }

        var deepestReached = 0;
        foreach (var id in plan.Roster)
        {
            if (state.Heroes.TryGetValue(id.Value, out var hero))
            {
                deepestReached = Math.Max(deepestReached, hero.DeepestFloorReached);
            }
        }

        // Phase C U-C4: the default floor is clamped to the PLAN'S OWN venue (not always the Mine) —
        // otherwise a bounty-free party routed to a shallower venue (e.g. the Gloomwood's 4 floors vs
        // the Mine's 5) would look like a "bounty override" here when it's actually just routing.
        var defaultFloor = Math.Clamp(deepestReached + 1, 1, VenueRegistry.Require(plan.VenueId).FloorCount);
        if (plan.TargetFloor == defaultFloor)
        {
            return; // no override — the default floor isn't a "decision" worth explaining
        }

        var leader = plan.Roster[0]; // lowest HeroId — PartyFormation's own deterministic sort order
        var gapPermille = Math.Clamp(Math.Abs(plan.TargetFloor - defaultFloor) * 200, 0, 1000);
        events.Emit(new HeroDecisionExplained(
            leader,
            $"floor {plan.TargetFloor} (bounty)",
            $"floor {defaultFloor} (deepest reached + 1)",
            "the party's accepted bounty overrode the usual depth-based target floor",
            gapPermille));
    }
}
