using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>A memorial stone in the town square — one per dead hero, accumulating (R13).
/// <para>Wave 4c (U18, farewell rite): <paramref name="Honored"/> flips true exactly once when the
/// player performs the fallen's farewell rite (<c>HonorMemorialAction</c>) — an earned goodbye, not
/// just an economy event (R6). Trailing positional with a default so old saves and existing
/// constructors deserialize/compile unchanged (default = not yet honored); DATA only, no sim rule
/// keys off it beyond the rite's own idempotency guard + presentation.</para></summary>
public sealed record Memorial(HeroId Hero, string HeroName, int Day, string GearNamed, bool Honored = false);

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
/// The guild-rent deadline heartbeat (Game-Feel Plan G3): due every <see cref="CadenceDays"/>
/// Mornings, escalating whether paid or missed. A missed payment is a legible SOFT consequence
/// (a confidence hit) — never game-over; the shop keeps running at low confidence forever if the
/// player never catches up. The Morning rent system in the Economy module is the only writer;
/// pure integer, no RNG, no wall clock (KTD2).
/// </summary>
/// <param name="DaysUntilDue">Mornings left before the next payment (counts down to 0).</param>
/// <param name="AmountDueGold">Gold owed at the next due date; escalates each cycle.</param>
/// <param name="MissedPayments">Lifetime count of due dates that landed unaffordable.</param>
/// <param name="ConfidencePermille">0-1000 legible morale/confidence gauge: drops on a missed
/// payment, recovers a little on a paid one. Cosmetic-but-visible in this slice — a hook for a
/// later unit to feed recruit trickle / hero mood, deliberately NOT wired yet (scope control).</param>
public sealed record RentState(int DaysUntilDue, int AmountDueGold, int MissedPayments, int ConfidencePermille)
{
    /// <summary>Mornings between rent due-dates. The ONE cadence knob (~10 days per the plan).</summary>
    public const int CadenceDays = 10;

    /// <summary>Starting/base rent, before any escalation.</summary>
    public const int BaseRentGold = 30;

    /// <summary>A fresh campaign's rent clock: a full cadence away, at the base rate, full confidence.</summary>
    public static readonly RentState Initial = new(
        DaysUntilDue: CadenceDays,
        AmountDueGold: BaseRentGold,
        MissedPayments: 0,
        ConfidencePermille: 1000);
}

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

    /// <summary>Real-work action slots left today (Game-Feel Plan G3): craft, restock/buy, and
    /// negotiate (see <see cref="ActionBudget.ConsumesSlot"/>) each spend one; <c>GameKernel.Tick</c>
    /// resets it to <see cref="ActionBudget.SlotsPerDay"/> whenever <see cref="Day"/> actually
    /// advances (the Counter-teardown precedent for a day-boundary reset in the kernel). Non-
    /// positional init member (InFlight/Venues/Counter pattern): a pre-G3 save (no property in the
    /// JSON) deserializes to a FULL day's budget — the scarcity mechanic is always-on once this
    /// ships (there's no "off" state to preserve), so a fresh full slot count is the
    /// least-surprising load, never a mid-day save mysteriously starting at zero.</summary>
    public int ActionSlotsRemaining { get; init; } = ActionBudget.SlotsPerDay;

    /// <summary>The guild-rent deadline heartbeat (Game-Feel Plan G3), or <see cref="RentState.Initial"/>
    /// for a pre-G3 save (no property in the JSON) — the InFlight/Venues/Counter precedent: absence
    /// deserializes to the feature's fresh-start baseline, not a behavior change beyond the countdown
    /// restarting from a full cadence.</summary>
    public RentState Rent { get; init; } = RentState.Initial;

    /// <summary>The rival vendor's competitive edge, 0-1000 (Game-Feel Plan G3): a full idle day
    /// (zero action-budget slots spent) raises it; any real-work day lowers it. The Morning rival
    /// restock system reads it to discount newly-minted rival stock, so idling visibly cedes market
    /// share. Non-positional init member defaulting to 0 (no edge) — a pre-G3 save loads with the
    /// rival at its old, undiscounted catalog prices (byte-identical pricing for the default trace).</summary>
    public int RivalMarketSharePermille { get; init; } = 0;

    /// <summary>Wave 3 (commissions): open + accepted hero commissions. Trailing init member
    /// (the InFlight/Venues/Counter/Rent save-compat precedent) — a pre-Wave-3 save has no property
    /// and deserializes to empty, byte-identical to today. The Morning <c>CommissionSystem</c> posts
    /// them; player accept/decline flips <see cref="Commission.Accepted"/>; fulfillment/expiry drains them.</summary>
    public ImmutableList<Commission> Commissions { get; init; } = ImmutableList<Commission>.Empty;
}

/// <summary>Wave 3: one hero's gear request — forge <see cref="Slot"/> at or above
/// <see cref="MinQuality"/> by <see cref="DeadlineDay"/> for a <see cref="PremiumGold"/> premium over
/// list. <see cref="Accepted"/> is false when first posted; the player's AcceptCommissionAction flips
/// it. Pure data (no Godot, integer-only).</summary>
public sealed record Commission(
    HeroId Hero,
    ItemSlot Slot,
    QualityGrade MinQuality,
    int DeadlineDay,
    int PremiumGold,
    bool Accepted = false);

/// <summary>Result of one phase tick: the new world, what happened, and what was refused.</summary>
public sealed record TickResult(
    GameState NewState,
    ImmutableList<GameEvent> Events,
    ImmutableList<RejectedAction> Rejected);
