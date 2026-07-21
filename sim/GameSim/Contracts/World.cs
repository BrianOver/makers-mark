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
/// The stepped counter-service session state (PKD5/PKD6), present only while the player is
/// working the counter this Morning. <c>null</c> on <see cref="GameState.Counter"/> means the
/// classic atomic auto-shopping pass — the default, byte-identical to pre-Phase-A.
/// <para>Meters are Potionomics-shaped: <paramref name="InterestPermille"/> raises the acceptable
/// price band, <paramref name="PatienceRounds"/> counts DOWN in ROUNDS (not seconds — the sim never
/// sees time; 0 = the customer leaves), <paramref name="GoodwillPermille"/> is the fleece memory that
/// feeds <see cref="Hero.MoodPermille"/> and gossip. The price band around the standing offer is
/// Recettear-shaped (per-class factor, shifts per surviving round, "pin" bonus near true willingness).</para>
/// <para>Determinism: every field is deterministically serializable (ordered list, sorted set) and the
/// whole record is a save-compat init member on <see cref="GameState"/>. The haggle resolves with ZERO
/// RNG (PA4) — a slow player and a fast player produce identical state for identical choices.</para>
/// </summary>
/// <param name="Queue">Heroes still to be offered service this Morning, in HeroId order (the existing
/// deterministic shopping order). The head is normally the <paramref name="Active"/> customer.</param>
/// <param name="Active">The customer currently at the counter, or null when the queue is empty and the
/// player is only arranging (a valid open state).</param>
/// <param name="Round">Which haggle round the active customer is in (1-based; capped ~3 by PA4).</param>
/// <param name="Presented">The item currently shown to the active customer (PresentItem), if any.</param>
/// <param name="StandingOfferGold">The customer's live offer in gold for the presented item, if any.</param>
/// <param name="Served">HeroId.Value of every hero already resolved this session — the gate that keeps
/// the closing atomic fallback (PKD5) from serving anyone twice.</param>
/// <param name="Closed">True once <c>CloseCounterAction</c> landed or the queue emptied; the next
/// Morning Advance runs the unserved-hero fallback and clears <see cref="GameState.Counter"/> to null.</param>
public sealed record CounterState(
    ImmutableList<HeroId> Queue,
    HeroId? Active,
    int Round,
    int InterestPermille,
    int PatienceRounds,
    int GoodwillPermille,
    ItemId? Presented,
    int? StandingOfferGold,
    ImmutableSortedSet<int> Served,
    bool Closed)
{
    public static readonly CounterState Empty = new(
        ImmutableList<HeroId>.Empty,
        Active: null,
        Round: 0,
        InterestPermille: 0,
        PatienceRounds: 0,
        GoodwillPermille: 0,
        Presented: null,
        StandingOfferGold: null,
        Served: ImmutableSortedSet<int>.Empty,
        Closed: false);
}

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

    /// <summary>The stepped counter-service session (PKD5/PKD6), or null for the classic atomic
    /// Morning shopping pass. Non-positional init member (the <see cref="InFlight"/>/<see cref="Venues"/>
    /// pattern): pre-Phase-A saves (no property) deserialize to null, which is byte-identical to today.</summary>
    public CounterState? Counter { get; init; } = null;
}

/// <summary>Result of one phase tick: the new world, what happened, and what was refused.</summary>
public sealed record TickResult(
    GameState NewState,
    ImmutableList<GameEvent> Events,
    ImmutableList<RejectedAction> Rejected);
