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
        ImmutableList<Bounty> bounties)
    {
        var predictedBounties = BountyRules.JudgeFirstAccept(heroes, bounties);

        var parties = PartyFormation.FormParties(heroes);

        // The Mine is the only LIVE venue today (P4 live-venue contract: VenueRegistry.LiveRotation)
        // — mirrors ExpeditionSystem's own hardcode; both read the same single source of truth.
        var venue = VenueRegistry.Mine;

        var plans = ImmutableList.CreateBuilder<PartyPlan>();
        foreach (var partyIds in parties)
        {
            var party = partyIds.Select(id => heroes[id.Value]).ToImmutableList();
            var targetFloor = ExpeditionSystem.TargetFloorFor(party, partyIds, predictedBounties, venue);
            plans.Add(new PartyPlan(partyIds, targetFloor, VenueRegistry.MineId));
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
        var parties = MusterPlan.Compute(state.Heroes, state.Bounties);
        events.Emit(new PartiesFormed(parties));
        return state;
    }
}
