using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Professions;

namespace GameSim.Kernel;

/// <summary>Builds the day-1 world. Hero seeding arrives with U5; U3 starts an empty town.</summary>
public static class GameFactory
{
    public const int StartingPlayerGold = 100;

    /// <summary>Starter base-material stock for a chosen profession (Playable Core R4/KD3):
    /// every profession's tier-1 recipes draw 2 copper, so 6 copper = ~3 tier-1 crafts.</summary>
    public const int StarterCopper = 6;

    /// <summary>
    /// A day-1 world with a CHOSEN starting profession + starter copper (Playable Core R4/KD3).
    /// Pure data, no RNG draw beyond the seed init the default path already does — the default
    /// <see cref="NewGame(ulong, ImmutableSortedDictionary{int, Hero}?)"/> stays byte-identical
    /// for the CLI, replays, and every existing test.
    /// </summary>
    public static GameState NewGame(ulong seed, string startingProfession,
        ImmutableSortedDictionary<int, Hero>? heroes = null)
    {
        if (!ProfessionRegistry.IsRegistered(startingProfession))
        {
            throw new ArgumentException($"Unknown profession '{startingProfession}'.", nameof(startingProfession));
        }

        return NewGame(seed, heroes) with
        {
            Player = PlayerState.NewGame(
                StartingPlayerGold,
                startingProfession,
                ImmutableSortedDictionary<string, int>.Empty.Add("copper", StarterCopper)),
        };
    }

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
