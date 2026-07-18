using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>A memorial stone in the town square — one per dead hero, accumulating (R13).</summary>
public sealed record Memorial(HeroId Hero, string HeroName, int Day, string GearNamed);

/// <summary>Drama-surface state: memorials, the Depths Progress board, recruit gating (R10, R13, R15).</summary>
public sealed record DramaState(
    ImmutableList<Memorial> Memorials,
    ImmutableSortedDictionary<int, int> DepthsBoard, // HeroId.Value -> deepest floor
    int DaysUntilNextRecruit)
{
    public static readonly DramaState Empty = new(
        ImmutableList<Memorial>.Empty,
        ImmutableSortedDictionary<int, int>.Empty,
        DaysUntilNextRecruit: 0);
}

/// <summary>One logged batch of player actions — the replay record (KTD4).</summary>
public sealed record LoggedBatch(int Day, DayPhase Phase, ImmutableList<PlayerAction> Actions);

/// <summary>
/// Per-venue mutable world state (M4, P9 dens / P5 closures): days since a party last cleared
/// ground here, the den's escalation meter (per-mille), and whether routes to it are closed.
/// Written by post-weekend systems (M11a escalation, M6 closures) — until they land, a venue
/// simply has no entry and the game reads it as untouched/open. APPEND fields via contracts
/// micro-PR only (KTD4).
/// </summary>
public sealed record VenueState(int DaysUntouched, int InfectionPerMille, bool Closed);

/// <summary>
/// The entire world. Immutable; every field is deterministically serializable
/// (sorted dictionaries, ordered lists). Advanced only by <c>GameKernel.Tick</c>.
/// </summary>
public sealed record GameState(
    int Day,
    DayPhase Phase,
    RngState Rng,
    int NextItemId,
    int NextHeroId,
    int NextBountyId,
    int NextEventId,
    PlayerState Player,
    ImmutableSortedDictionary<int, Hero> Heroes,          // HeroId.Value -> Hero
    ImmutableSortedDictionary<int, Item> Items,           // ItemId.Value -> Item
    ImmutableList<ShelfEntry> RivalShelf,
    ImmutableList<Bounty> Bounties,
    ImmutableList<ExpeditionResult> PendingExpeditions,   // resolved at departure, revealed at Evening (KTD5)
    ImmutableList<OreOffered> OpenOreOffers,
    DramaState Drama,
    ImmutableList<GameEvent> EventLog,
    ImmutableList<LoggedBatch> ActionLog)
{
    /// <summary>Staged expeditions between the Expedition and ExpeditionDeep ticks (KTD5 staged).
    /// Non-positional init member: pre-staging saves (no property) deserialize to empty.</summary>
    public ImmutableList<InFlightExpedition> InFlight { get; init; } = ImmutableList<InFlightExpedition>.Empty;

    /// <summary>Per-venue mutable state keyed by VenueRegistry id (M4). Non-positional init
    /// member: pre-M4 saves (no property) deserialize to empty — no entry = untouched/open.</summary>
    public ImmutableSortedDictionary<string, VenueState> Venues { get; init; } = ImmutableSortedDictionary<string, VenueState>.Empty;
}

/// <summary>Result of one phase tick: the new world, what happened, and what was refused.</summary>
public sealed record TickResult(
    GameState NewState,
    ImmutableList<GameEvent> Events,
    ImmutableList<RejectedAction> Rejected);
