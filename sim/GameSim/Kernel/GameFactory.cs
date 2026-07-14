using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Kernel;

/// <summary>Builds the day-1 world. Hero seeding arrives with U5; U3 starts an empty town.</summary>
public static class GameFactory
{
    public const int StartingPlayerGold = 100;

    public static GameState NewGame(ulong seed, ImmutableSortedDictionary<int, Hero>? heroes = null) => new(
        Day: 1,
        Phase: DayPhase.Morning,
        Rng: RngState.FromSeed(seed),
        NextItemId: 1,
        NextHeroId: 1,
        NextBountyId: 1,
        NextEventId: 1,
        Player: PlayerState.NewGame(StartingPlayerGold),
        Heroes: heroes ?? ImmutableSortedDictionary<int, Hero>.Empty,
        Items: ImmutableSortedDictionary<int, Item>.Empty,
        RivalShelf: ImmutableList<ShelfEntry>.Empty,
        Bounties: ImmutableList<Bounty>.Empty,
        PendingExpeditions: ImmutableList<ExpeditionResult>.Empty,
        OpenOreOffers: ImmutableList<OreOffered>.Empty,
        Drama: DramaState.Empty,
        EventLog: ImmutableList<GameEvent>.Empty,
        ActionLog: ImmutableList<LoggedBatch>.Empty);
}
