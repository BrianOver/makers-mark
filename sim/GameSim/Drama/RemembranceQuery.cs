using GameSim.Contracts;

namespace GameSim.Drama;

/// <summary>
/// P2-PEOPLE-05: which logged events truly NAME a hero — the only events a remembrance may be chosen from,
/// so the wall never carries a generic line about the fallen. The set is the events whose payload carries
/// the hero's id as the subject or counterpart; anything else (a rival restock, a tariff, another hero's
/// deed) is not theirs. Pure predicate, no RNG, no clock.
/// </summary>
public static class RemembranceQuery
{
    public static bool NamesHero(GameEvent gameEvent, HeroId hero) => gameEvent switch
    {
        HeroDied e => e.Hero == hero,
        AttributionBeatEvent e => e.Hero == hero,
        ItemSold e => e.Buyer == hero,
        CounterSaleClosed e => e.Hero == hero,
        HeroPassedOnItem e => e.Hero == hero,
        CommissionPosted e => e.Hero == hero,
        CommissionFulfilled e => e.Hero == hero,
        BountyJudged e => e.Hero == hero,
        BountyPaid e => e.To == hero,
        SupplyDelivered e => e.To == hero,
        FloorRecordSet e => e.Hero == hero,
        HeroRankUp e => e.Hero == hero,
        RecruitArrived e => e.Hero == hero,
        MemorialHonored e => e.Hero == hero,
        _ => false,
    };

    /// <summary>Every logged event that names <paramref name="hero"/>, in log order — the choices a wake offers.</summary>
    public static IEnumerable<GameEvent> Naming(GameState state, HeroId hero) =>
        state.EventLog.Where(e => NamesHero(e, hero));
}
